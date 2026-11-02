// Optional C ABI. The host owns its UI thread, CEF runtime policy and GPU textures.
#include "cyui/adapters/WebCEF.h"
#include "include/cef_task.h"
#include <sstream>
#include <thread>
#include <chrono>
#if defined(OS_WIN)
#include <windows.h>
#define CYUI_CEF_API extern "C" __declspec(dllexport)
#else
#define CYUI_CEF_API extern "C" __attribute__((visibility("default")))
#endif
namespace {
thread_local std::string error, eventJson;
// CEF can be initialized once per process: none -> running -> stopped.
enum class Runtime { none, running, stopped } runtime = Runtime::none;
int views = 0;
bool sandboxed = false;
template <class F> int protect(F &&f) {
    try {
        error.clear();
        return f();
    } catch (const std::exception &e) {
        error = e.what();
        return 0;
    } catch (...) {
        error = "CEF host failure";
        return 0;
    }
}
bool uiThread() {
    if (runtime == Runtime::running && CefCurrentlyOn(TID_UI))
        return true;
    error = "CEF view calls must run on the initializing UI thread while CEF is running";
    return false;
}
std::string trim(const std::string &value) {
    const auto first = value.find_first_not_of(" \t\r\n"), last = value.find_last_not_of(" \t\r\n");
    return first == std::string::npos ? std::string() : value.substr(first, last - first + 1);
}
} // namespace
CYUI_CEF_API const char *cyui_cef_error() {
    return error.c_str();
}
CYUI_CEF_API int cyui_cef_initialize(const char *cache, const char *helper, int noSandbox) {
    return protect([&] {
        if (runtime != Runtime::none) {
            error = runtime == Runtime::running ? "CEF is already initialized"
                                                : "CEF cannot be initialized again after shutdown";
            return 0;
        }
        if (!cache || !helper)
            return 0;
#if defined(OS_WIN)
        CefMainArgs args(GetModuleHandleW(nullptr));
#else
        int count = 1;
        char name[] = "cyui-cef-host";
        char *values[] = {name, nullptr};
        CefMainArgs args(count, values);
#endif
        CefSettings settings;
        settings.windowless_rendering_enabled = true;
#if defined(OS_WIN)
        // The Windows sandbox needs sandbox info from the host executable's entry point
        // (CEF bootstrap.exe + RunWinMain/RunConsoleMain); a library cannot supply it. Run
        // explicitly unsandboxed instead of passing nullptr with no_sandbox=false.
        settings.no_sandbox = true;
#else
        // Linux: Chromium's sandbox needs no sandbox info. macOS: the helper bundle must
        // initialize CefScopedSandboxContext. Neither is validated by this repository.
        settings.no_sandbox = noSandbox != 0;
#endif
        sandboxed = !settings.no_sandbox;
        CefString(&settings.root_cache_path) = cache;
        CefString(&settings.browser_subprocess_path) = helper;
        settings.log_severity = LOGSEVERITY_WARNING;
        if (!CefInitialize(args, settings, cyui::webCEFApp(), nullptr))
            return 0;
        runtime = Runtime::running;
        if (!noSandbox && !sandboxed)
            error = "CEF sandbox requested but unavailable from this library: running unsandboxed";
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_pump() {
    return protect([&] {
        if (runtime != Runtime::running)
            return 0;
        CefDoMessageLoopWork();
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_shutdown() {
    return protect([&] {
        if (runtime != Runtime::running)
            return 0;
        // CefShutdown with live browsers asserts: every view must be closed and destroyed.
        if (views) {
            error = "Close, pump until closed and destroy every view before shutdown";
            return 0;
        }
        CefShutdown();
        runtime = Runtime::stopped;
        return 1;
    });
}
// 1: original functions. 2: adds cyui_cef_create2 (release callback) and cyui_cef_sandboxed.
CYUI_CEF_API int cyui_cef_abi_version() {
    return 2;
}
CYUI_CEF_API int cyui_cef_sandboxed() {
    return sandboxed ? 1 : 0;
}
using Upload = std::uint64_t (*)(int, int, const void *, void *);
using Release = void (*)(std::uint64_t, void *);
// release (optional) runs once from cyui_cef_pump after the browser closed, with the last
// texture returned by upload.
CYUI_CEF_API void *cyui_cef_create2(const char *url, const char *origins, Upload upload, Release release,
                                    void *user) {
    void *result = nullptr;
    protect([&] {
        if (!uiThread() || !upload)
            return 0;
        cyui::WebCEFOptions options;
        options.url = url ? url : "";
        std::istringstream stream(origins ? origins : "");
        std::string line;
        while (std::getline(stream, line)) {
            line = trim(line);
            if (line.empty())
                continue;
            if (cyui::webOrigin(line).empty()) {
                error = "Invalid origin: " + line.substr(0, 256);
                return 0;
            }
            options.policy.origins.push_back(line);
        }
        options.upload = [=](int w, int h, const void *pixels) {
            return ImTextureID(upload(w, h, pixels, user));
        };
        if (release)
            options.release = [=](ImTextureID texture) { release(std::uint64_t(texture), user); };
        result = cyui::createWebCEF(options).release();
        ++views;
        return 1;
    });
    return result;
}
// ABI 1 signature, kept unchanged for existing callers.
CYUI_CEF_API void *cyui_cef_create(const char *url, const char *origins, Upload upload, void *user) {
    return cyui_cef_create2(url, origins, upload, nullptr, user);
}
CYUI_CEF_API int cyui_cef_close(void *handle) {
    return protect([&] {
        if (!handle || !uiThread())
            return 0;
        static_cast<cyui::WebBackend *>(handle)->close();
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_closed(void *handle) {
    return protect([&] { return !handle || static_cast<cyui::WebBackend *>(handle)->closed() ? 1 : 0; });
}
CYUI_CEF_API int cyui_cef_destroy(void *handle) {
    return protect([&] {
        if (!handle)
            return 1;
        if (!uiThread())
            return 0;
        auto *backend = static_cast<cyui::WebBackend *>(handle);
        if (!backend->closed()) {
            error = "Close and pump until closed before destroy";
            return 0;
        }
        delete backend;
        --views;
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_ready(void *handle) {
    return protect([&] { return handle && static_cast<cyui::WebBackend *>(handle)->ready() ? 1 : 0; });
}
CYUI_CEF_API int cyui_cef_focused(void *handle) {
    return protect([&] { return handle && static_cast<cyui::WebBackend *>(handle)->focused() ? 1 : 0; });
}
CYUI_CEF_API std::uint64_t cyui_cef_texture(void *handle) {
    return handle ? std::uint64_t(static_cast<cyui::WebBackend *>(handle)->texture()) : 0;
}
CYUI_CEF_API int cyui_cef_frame(void *handle, int begin) {
    return protect([&] {
        if (!handle || !uiThread())
            return 0;
        if (begin)
            static_cast<cyui::WebBackend *>(handle)->beginFrame();
        else
            static_cast<cyui::WebBackend *>(handle)->endFrame();
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_place(void *handle, float x, float y, float width, float height, float clipX,
                                float clipY, float clipWidth, float clipHeight, int visible, int focus) {
    return protect([&] {
        if (!handle || !uiThread())
            return 0;
        static_cast<cyui::WebBackend *>(handle)->place(
            {{{x, y}, {x + width, y + height}},
             {{clipX, clipY}, {clipX + clipWidth, clipY + clipHeight}},
             visible != 0,
             focus != 0});
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_input(void *handle, int kind, float x, float y, float wheelX, float wheelY,
                                int button, int clicks, int key, int nativeKey, unsigned modifiers,
                                unsigned codepoint, int down) {
    return protect([&] {
        if (!handle || kind < 0 || kind > 5 || !uiThread())
            return 0;
        static_cast<cyui::WebBackend *>(handle)->input({cyui::WebInputKind(kind), x, y, wheelX, wheelY,
                                                        button, clicks, key, nativeKey, modifiers, codepoint,
                                                        down != 0});
        return 1;
    });
}
CYUI_CEF_API int cyui_cef_navigate(void *handle, const char *url) {
    return protect(
        [&] { return handle && url && uiThread() && static_cast<cyui::WebBackend *>(handle)->navigate(url) ? 1 : 0; });
}
CYUI_CEF_API int cyui_cef_post(void *handle, const char *json) {
    return protect(
        [&] { return handle && json && uiThread() && static_cast<cyui::WebBackend *>(handle)->postJson(json) ? 1 : 0; });
}
CYUI_CEF_API const char *cyui_cef_poll(void *handle) {
    const int found = protect([&] {
        if (!handle || !uiThread())
            return 0;
        cyui::WebEvent event;
        if (!static_cast<cyui::WebBackend *>(handle)->poll(event))
            return 0;
        eventJson = cyui::webEventJson(event);
        return 1;
    });
    return found ? eventJson.c_str() : nullptr;
}
