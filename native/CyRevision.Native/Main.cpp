#include "Workspace.h"
#include "Assets.h"
#include <imgui_internal.h>
#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <d3d11.h>
#include <shellapi.h>
#include <shlobj.h>
#include <filesystem>
#include <fstream>
#include <memory>
#include <cstdio>
#include <stdexcept>

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND,UINT,WPARAM,LPARAM);
namespace {
bool wantClose=false,minimized=false;
bool captureRequested=false;
std::string uiError;
float pendingDpi=0;
ULONGLONG lastInput=0;
ID3D11Device* gDevice {};
ID3D11DeviceContext* gContext {};
IDXGISwapChain* gSwapChain {};
ID3D11RenderTargetView* gTarget {};
UINT gResizeWidth {}, gResizeHeight {};

bool createTarget()
{
    ID3D11Texture2D* back {};
    if(FAILED(gSwapChain->GetBuffer(0,IID_PPV_ARGS(&back)))) return false;
    auto result=gDevice->CreateRenderTargetView(back,nullptr,&gTarget);
    back->Release();
    return SUCCEEDED(result);
}

void releaseTarget()
{
    if (gTarget != nullptr) {
        gTarget->Release();
        gTarget = nullptr;
    }
}

bool createDevice(HWND hwnd)
{
    DXGI_SWAP_CHAIN_DESC sd {};
    sd.BufferCount = 2;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferDesc.RefreshRate.Numerator = 60;
    sd.BufferDesc.RefreshRate.Denominator = 1;
    sd.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hwnd;
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    const D3D_FEATURE_LEVEL levels[] { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0 };
    D3D_FEATURE_LEVEL level {};
    auto result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, 2, D3D11_SDK_VERSION, &sd,
                                                &gSwapChain, &gDevice, &level, &gContext);
    if (result == DXGI_ERROR_UNSUPPORTED)
        result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, levels, 2, D3D11_SDK_VERSION, &sd,
                                               &gSwapChain, &gDevice, &level, &gContext);
    if (result != S_OK)
        return false;
    return createTarget();
}

void releaseDevice()
{
    releaseTarget();
    if (gSwapChain != nullptr)
        gSwapChain->Release();
    if (gContext != nullptr)
        gContext->Release();
    if (gDevice != nullptr)
        gDevice->Release();
}

// Writes the back buffer to a 32-bit BMP (the --capture option, for checks).
bool captureBackBuffer(const wchar_t* path)
{
    ID3D11Texture2D* back {};
    if (FAILED(gSwapChain->GetBuffer(0, IID_PPV_ARGS(&back))))
        return false;
    D3D11_TEXTURE2D_DESC desc {};
    back->GetDesc(&desc);
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.MiscFlags = 0;
    ID3D11Texture2D* staging {};
    if (FAILED(gDevice->CreateTexture2D(&desc, nullptr, &staging))) {
        back->Release();
        return false;
    }
    gContext->CopyResource(staging, back);
    back->Release();
    D3D11_MAPPED_SUBRESOURCE mapped {};
    if (FAILED(gContext->Map(staging, 0, D3D11_MAP_READ, 0, &mapped))) {
        staging->Release();
        return false;
    }
    const auto width = static_cast<int>(desc.Width);
    const auto height = static_cast<int>(desc.Height);
    std::vector<unsigned char> pixels(static_cast<std::size_t>(width) * height * 4);
    for (auto y = 0; y < height; ++y) {
        const auto* row = static_cast<const unsigned char*>(mapped.pData) + static_cast<std::size_t>(y) * mapped.RowPitch;
        for (auto x = 0; x < width; ++x) {
            auto* out = &pixels[(static_cast<std::size_t>(y) * width + x) * 4];
            out[0] = row[x * 4 + 2];
            out[1] = row[x * 4 + 1];
            out[2] = row[x * 4 + 0];
            out[3] = 255;
        }
    }
    gContext->Unmap(staging, 0);
    staging->Release();
    BITMAPFILEHEADER file {};
    BITMAPINFOHEADER info {};
    info.biSize = sizeof(info);
    info.biWidth = width;
    info.biHeight = -height;
    info.biPlanes = 1;
    info.biBitCount = 32;
    info.biCompression = BI_RGB;
    file.bfType = 0x4d42;
    file.bfOffBits = sizeof(file) + sizeof(info);
    file.bfSize = file.bfOffBits + static_cast<DWORD>(pixels.size());
    FILE* out {};
    if (_wfopen_s(&out, path, L"wb") != 0 || out == nullptr)
        return false;
    std::fwrite(&file, sizeof(file), 1, out);
    std::fwrite(&info, sizeof(info), 1, out);
    std::fwrite(pixels.data(), 1, pixels.size(), out);
    std::fclose(out);
    return true;
}


LRESULT WINAPI windowProc(HWND hwnd,UINT message,WPARAM wParam,LPARAM lParam) {
    if(ImGui::GetCurrentContext() && ImGui_ImplWin32_WndProcHandler(hwnd,message,wParam,lParam)) return true;
    if(message==WM_MOUSEMOVE || message==WM_MOUSEWHEEL || message==WM_KEYDOWN || message==WM_LBUTTONDOWN) lastInput=GetTickCount64();
    switch(message) {
    case WM_SIZE:
        minimized=wParam==SIZE_MINIMIZED;
        if(!minimized) {gResizeWidth=LOWORD(lParam);gResizeHeight=HIWORD(lParam);}
        return 0;
    case WM_DPICHANGED: {
        pendingDpi=HIWORD(wParam)/96.0f;
        const auto* rectangle=reinterpret_cast<const RECT*>(lParam);
        SetWindowPos(hwnd,nullptr,rectangle->left,rectangle->top,rectangle->right-rectangle->left,
            rectangle->bottom-rectangle->top,SWP_NOZORDER|SWP_NOACTIVATE);
        return 0;
    }
    case WM_GETMINMAXINFO: {
        auto* sizes=reinterpret_cast<MINMAXINFO*>(lParam);
        sizes->ptMinTrackSize={960,600};
        return 0;
    }
    case WM_CLOSE: wantClose=true;return 0;
    case WM_SYSCOMMAND: if((wParam&0xfff0)==SC_KEYMENU) return 0;break;
    case WM_DESTROY: PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(hwnd,message,wParam,lParam);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR,int) {
    using namespace cyrevision;
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    int result=0;
    HWND window=nullptr;
    bool winBackend=false,dxBackend=false,registered=false;
    std::unique_ptr<Client> client;
    try {
        std::wstring capture,backend,profile;
        Json savedWindows=Json::array();std::vector<std::string> openWindows;
        std::vector<std::wstring> arguments;
        std::string theme="mid-grey";
        bool comfortable=false,verifyMultiWindow=false;
        int argumentCount=0;
        auto** commandLine=CommandLineToArgvW(GetCommandLineW(),&argumentCount);
        for(int index=1;index<argumentCount;++index) {
            std::wstring argument=commandLine[index];
            auto next=[&](){return index+1<argumentCount ? std::wstring(commandLine[++index]) : std::wstring();};
            if(argument==L"--backend") backend=next();
            else if(argument==L"--capture") capture=next();
            else if(argument==L"--theme") theme=utf8(next());
            else if(argument==L"--comfortable") comfortable=true;
            else if(argument==L"--open-window")openWindows.push_back(utf8(next()));
            else if(argument==L"--verify-multi-window")verifyMultiWindow=true;
            else if(argument==L"--profile-dir") {
                profile=next(); arguments.push_back(L"--profile-dir");arguments.push_back(profile);
            } else arguments.push_back(argument);
        }
        LocalFree(commandLine);
        captureRequested=!capture.empty();
        if(verifyMultiWindow && (capture.empty() || openWindows.empty()))throw std::runtime_error("Multi-window verification requires a capture and an auxiliary view.");
        wchar_t executable[32768];
        auto length=GetModuleFileNameW(nullptr,executable,32768);
        if(length==0 || length==32768) throw std::runtime_error("Could not locate CyRevision.");
        if(backend.empty()) backend=(std::filesystem::path(std::wstring(executable,length)).parent_path()/L"CyRevision.Desktop.exe").wstring();
        if(!std::filesystem::is_regular_file(backend)) throw std::runtime_error("CyRevision.Desktop.exe is missing beside the native interface.");
        std::filesystem::path configuration;
        if(!profile.empty()) configuration=std::filesystem::path(profile)/L"config";
        else {
            PWSTR root=nullptr;
            if(FAILED(SHGetKnownFolderPath(FOLDERID_RoamingAppData,0,nullptr,&root))) throw std::runtime_error("Could not locate application preferences.");
            configuration=std::filesystem::path(root)/L"CyRevision";CoTaskMemFree(root);
        }
        std::filesystem::create_directories(configuration);
        auto settingsPath=configuration/L"native-v2.json";
        auto layoutPath=utf8((configuration/L"native-v2.ini").wstring());
        if(std::ifstream settings{settingsPath};settings && capture.empty()) {
            auto saved=Json::parse(settings,nullptr,false);
            if(saved.is_object()) {theme=text(saved,"theme",theme);comfortable=saved.value("comfortable",comfortable);savedWindows=saved.value("windows",Json::array());}
        }
        ImGui_ImplWin32_EnableDpiAwareness();
        IMGUI_CHECKVERSION();ImGui::CreateContext();
        ImGui::GetCurrentContext()->ErrorCallback=[](ImGuiContext*,void*,const char* message){uiError=message;fprintf(stderr,"Native UI: %s\n",message);};
        float dpi=ImGui_ImplWin32_GetDpiScaleForMonitor(MonitorFromPoint(POINT{0,0},MONITOR_DEFAULTTOPRIMARY));
        cyui::Config config;config.themeKey=theme.c_str();config.density=comfortable ? cyui::Density::comfortable : cyui::Density::compact;config.dpiScale=dpi;
        cyui::apply(config);cyui::loadFonts();
        auto& io=ImGui::GetIO();
        io.ConfigFlags|=ImGuiConfigFlags_NavEnableKeyboard|ImGuiConfigFlags_DockingEnable|ImGuiConfigFlags_ViewportsEnable;
        io.IniFilename=capture.empty() ? layoutPath.c_str() : nullptr;
        WNDCLASSEXW wc{sizeof(wc),CS_CLASSDC,windowProc,0,0,instance,nullptr,nullptr,nullptr,nullptr,L"CyRevisionNativeV2",nullptr};
        wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(1));wc.hIconSm=wc.hIcon;
        if(!RegisterClassExW(&wc)) throw std::runtime_error("Could not register the application window.");
        registered=true;
        window=CreateWindowW(wc.lpszClassName,L"CyRevision — Interface V2 Alpha",WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT,CW_USEDEFAULT,static_cast<int>(cyui::dp(1500)),static_cast<int>(cyui::dp(960)),nullptr,nullptr,instance,nullptr);
        if(!window || !createDevice(window)) throw std::runtime_error("Could not create the Direct3D 11 renderer.");
        if(!ImGui_ImplWin32_Init(window)) throw std::runtime_error("Could not initialize the Windows interface.");
        winBackend=true;
        if(!ImGui_ImplDX11_Init(gDevice,gContext)) throw std::runtime_error("Could not initialize the renderer.");
        dxBackend=true;
        initNativeTextures(gDevice);
        client=std::make_unique<Client>(backend,arguments);
        Workspace workspace(*client,backend);
        workspace.restoreWindowLayout(savedWindows);for(const auto& id:openWindows)workspace.openWorkspaceWindow(id);
        ShowWindow(window,SW_SHOWDEFAULT);UpdateWindow(window);
        bool done=false,didMinimize=false,didRestore=false;int captureFrames=0,minimizedFrames=0;auto started=GetTickCount64();ImVec2 mainDisplaySize=cyui::dp(1500,960);
        while(!done) {
            MSG message;
            while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {
                TranslateMessage(&message);DispatchMessageW(&message);
                if(message.message==WM_QUIT) done=true;
            }
            if(done) break;
            if(pendingDpi>0) {cyui::applyDpi(pendingDpi);pendingDpi=0;}
            if(minimized && workspace.requestClose)ShowWindow(window,SW_RESTORE);
            if(minimized && !workspace.hasAuxiliaryWindows() && ImGui::GetPlatformIO().Viewports.Size<=1) {MsgWaitForMultipleObjectsEx(0,nullptr,250,QS_ALLINPUT,MWMO_INPUTAVAILABLE);continue;}
            if(gResizeWidth && gResizeHeight) {
                releaseTarget();
                auto resized=gSwapChain->ResizeBuffers(0,gResizeWidth,gResizeHeight,DXGI_FORMAT_UNKNOWN,0);
                gResizeWidth=gResizeHeight=0;
                if(FAILED(resized) || !createTarget()) throw std::runtime_error("The graphics device could not resize the window.");
            }
            ImGui_ImplDX11_NewFrame();ImGui_ImplWin32_NewFrame();if(minimized)io.DisplaySize=mainDisplaySize;else mainDisplaySize=io.DisplaySize;ImGui::NewFrame();
            if(wantClose) {workspace.requestClose=true;wantClose=false;}
            beginNativeTexturesFrame();workspace.frame();ImGui::Render();
            if(captureRequested && !uiError.empty())throw std::runtime_error(uiError);
            auto ground=cyui::vec(cyui::palette().bg0);const float clear[]{ground.x,ground.y,ground.z,1};
            if(!minimized){gContext->OMSetRenderTargets(1,&gTarget,nullptr);gContext->ClearRenderTargetView(gTarget,clear);ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());}
            if(io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable){ImGui::UpdatePlatformWindows();ImGui::RenderPlatformWindowsDefault();}
            if(verifyMultiWindow && captureFrames>=5){
                if(!didMinimize){ShowWindow(window,SW_MINIMIZE);didMinimize=true;}
                else if(!didRestore){if(workspace.detachedPlatformWindowCount()==0)throw std::runtime_error("Auxiliary windows disappeared while the main window was minimized.");if(++minimizedFrames>=8){ShowWindow(window,SW_RESTORE);didRestore=true;}}
            }
            if(!capture.empty() && ((workspace.settled() && ++captureFrames>=40) || GetTickCount64()-started>20000)) {
                if(!openWindows.empty() && workspace.detachedPlatformWindowCount()==0)throw std::runtime_error("The detached view did not create a native platform window.");
                if(verifyMultiWindow && !didRestore)throw std::runtime_error("Minimized multi-window verification did not complete.");
                if(verifyMultiWindow)fprintf(stderr,"Native multi-window verification passed (%zu windows, minimized main window).\n",workspace.detachedPlatformWindowCount());
                if(verifyMultiWindow){std::ofstream report(std::filesystem::path(capture+L".windows.json"));report<<Json{{"nativeWindows",workspace.detachedPlatformWindowCount()},{"minimizedFrames",minimizedFrames},{"restored",didRestore}}.dump();if(!report)throw std::runtime_error("Could not write the multi-window verification result.");}
                if(!captureBackBuffer(capture.c_str())) throw std::runtime_error("Could not write the interface capture.");
                done=true;
            }
            auto presented=minimized?S_OK:gSwapChain->Present(1,0);
            if(FAILED(presented)) throw std::runtime_error("The graphics device stopped responding.");
            done=done || workspace.done();
            HANDLE wake=client->wakeEvent();
            DWORD delay=minimized?16:GetTickCount64()-lastInput<800 || ImGui::IsAnyItemActive() || workspace.hasAuxiliaryWindows() || !capture.empty() ? 1 : 80;
            MsgWaitForMultipleObjectsEx(1,&wake,delay,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
        }
        if(capture.empty()) {
            ImGui::SaveIniSettingsToDisk(layoutPath.c_str());
            std::ofstream settings(settingsPath);
            settings<<Json{{"theme",cyui::currentThemeKey()},{"comfortable",cyui::density()==cyui::Density::comfortable},{"windows",workspace.windowLayout()}}.dump(2);
        }
    } catch(const std::exception& error) {
        if(captureRequested)fprintf(stderr,"CyRevision: %s\n",error.what());else MessageBoxW(window,wide(error.what()).c_str(),L"CyRevision",MB_ICONERROR|MB_OK);result=1;
    }
    client.reset();
    releaseNativeTextures();
    if(dxBackend) ImGui_ImplDX11_Shutdown();
    if(winBackend) ImGui_ImplWin32_Shutdown();
    if(ImGui::GetCurrentContext()) ImGui::DestroyContext();
    releaseDevice();
    if(window) DestroyWindow(window);
    if(registered) UnregisterClassW(L"CyRevisionNativeV2",instance);
    CoUninitialize();
    return result;
}
