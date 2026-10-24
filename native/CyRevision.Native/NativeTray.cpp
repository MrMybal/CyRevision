#include "NativeTray.h"
#include <algorithm>

namespace cyrevision {
NativeTray::NativeTray(HWND window,HICON icon,NotifyIcon notify)
    :window_(window),icon_(icon),notify_(notify),taskbarCreated_(RegisterWindowMessageW(L"TaskbarCreated")) {
    add();
}
NativeTray::~NativeTray(){remove();}
NOTIFYICONDATAW NativeTray::data()const {
    NOTIFYICONDATAW item{};item.cbSize=sizeof(item);item.hWnd=window_;item.uID=1;
    item.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;item.uCallbackMessage=CallbackMessage;item.hIcon=icon_;
    wcscpy_s(item.szTip,L"CyRevision — running in the background");
    return item;
}
bool NativeTray::add(){
    if(installed_)return true;
    if(!enabled_)return false;
    if(GetTickCount64()<retryAt_)return false;
    auto item=data();installed_=notify_(NIM_ADD,&item)!=FALSE;
    retryAt_=installed_?0:GetTickCount64()+5000;
    if(installed_){item.uVersion=NOTIFYICON_VERSION_4;version4_=notify_(NIM_SETVERSION,&item)!=FALSE;}
    return installed_;
}
void NativeTray::remove(){
    if(installed_){auto item=data();notify_(NIM_DELETE,&item);installed_=false;version4_=false;}
}
bool NativeTray::setEnabled(bool enabled){
    enabled_=enabled;
    if(!enabled){if(hidden_)restore();remove();return false;}
    return add();
}
void NativeTray::command(UINT id){
    if(id==OpenCommand)pending_=Action::Open;
    else if(id==HideCommand)pending_=Action::Hide;
    else if(id==QuitCommand)pending_=Action::Quit;
}
void NativeTray::menu(){
    HMENU popup=CreatePopupMenu();if(!popup)return;
    AppendMenuW(popup,MF_STRING,OpenCommand,L"Open CyRevision");
    AppendMenuW(popup,MF_STRING|(hidden_?MF_GRAYED:0),HideCommand,L"Hide to tray");
    AppendMenuW(popup,MF_SEPARATOR,0,nullptr);
    AppendMenuW(popup,MF_STRING,QuitCommand,L"Quit CyRevision...");
    SetMenuDefaultItem(popup,OpenCommand,FALSE);
    POINT cursor{};GetCursorPos(&cursor);SetForegroundWindow(window_);
    UINT selected=TrackPopupMenu(popup,TPM_RETURNCMD|TPM_NONOTIFY|TPM_RIGHTBUTTON,cursor.x,cursor.y,0,window_,nullptr);
    DestroyMenu(popup);PostMessageW(window_,WM_NULL,0,0);command(selected);
}
bool NativeTray::handleMessage(UINT message,WPARAM wParam,LPARAM lParam){
    if(message==WM_CLOSE){pending_=enabled_ && closeToTray_?Action::Hide:Action::Quit;return true;}
    if(message==WM_COMMAND && HIWORD(wParam)==0 && LOWORD(wParam)>=OpenCommand && LOWORD(wParam)<=QuitCommand){command(LOWORD(wParam));return true;}
    if(taskbarCreated_ && message==taskbarCreated_){
        installed_=false;version4_=false;retryAt_=0;
        if(enabled_ && !add() && hidden_)restore();
        return true;
    }
    if(message!=CallbackMessage)return false;
    UINT event=version4_?LOWORD(lParam):static_cast<UINT>(lParam);
    UINT id=version4_?HIWORD(lParam):static_cast<UINT>(wParam);
    if(id!=1)return true;
    if(event==NIN_SELECT || event==NIN_KEYSELECT || event==WM_LBUTTONDBLCLK)pending_=Action::Open;
    else if(event==WM_CONTEXTMENU || event==WM_RBUTTONUP)menu();
    return true;
}
NativeTray::Action NativeTray::takeAction(){auto result=pending_;pending_=Action::None;return result;}
bool NativeTray::hide(){
    // Never leave the user with neither a window nor a working restore icon.
    if(!add())return false;
    if(hidden_)return true;
    windows_.clear();
    EnumThreadWindows(GetCurrentThreadId(),[](HWND handle,LPARAM context)->BOOL{
        auto* tray=reinterpret_cast<NativeTray*>(context);
        if(IsWindowVisible(handle))tray->windows_.push_back({handle,IsIconic(handle)!=FALSE});
        return TRUE;
    },reinterpret_cast<LPARAM>(this));
    hidden_=true;
    for(const auto& item:windows_)ShowWindow(item.handle,SW_HIDE);
    ShowWindow(window_,SW_HIDE);
    return true;
}
bool NativeTray::allWindowsHidden()const {
    if(!hidden_ || IsWindowVisible(window_))return false;
    return std::none_of(windows_.begin(),windows_.end(),[](const HiddenWindow& item){return IsWindow(item.handle) && IsWindowVisible(item.handle);});
}
void NativeTray::restore(){
    hidden_=false;
    for(const auto& item:windows_)if(item.handle!=window_ && IsWindow(item.handle))
        ShowWindow(item.handle,item.minimized?SW_SHOWMINNOACTIVE:SW_SHOWNOACTIVATE);
    windows_.clear();
    ShowWindow(window_,IsIconic(window_)?SW_RESTORE:SW_SHOW);
    SetForegroundWindow(window_);
}
}
