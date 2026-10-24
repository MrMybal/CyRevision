#pragma once
#include <windows.h>
#include <shellapi.h>
#include <vector>

namespace cyrevision {
class NativeTray {
public:
    enum class Action { None, Open, Hide, Quit };
    static constexpr UINT CallbackMessage=WM_APP+47;
    static constexpr UINT OpenCommand=0x7101, HideCommand=0x7102, QuitCommand=0x7103;
    using NotifyIcon=BOOL (WINAPI*)(DWORD,PNOTIFYICONDATAW);

    NativeTray(HWND window,HICON icon,NotifyIcon notify=Shell_NotifyIconW);
    ~NativeTray();
    NativeTray(const NativeTray&)=delete;
    NativeTray& operator=(const NativeTray&)=delete;
    bool handleMessage(UINT message,WPARAM wParam,LPARAM lParam);
    Action takeAction();
    bool setEnabled(bool enabled);
    void setCloseToTray(bool enabled){closeToTray_=enabled;}
    bool hide();
    void restore();
    bool hidden()const{return hidden_;}
    bool available()const{return installed_;}
    bool allWindowsHidden()const;
    UINT taskbarCreatedMessage()const{return taskbarCreated_;}
private:
    struct HiddenWindow {HWND handle;bool minimized;};
    HWND window_{};
    HICON icon_{};
    NotifyIcon notify_;
    UINT taskbarCreated_{};
    bool installed_=false,enabled_=true,version4_=false,hidden_=false,closeToTray_=true;
    Action pending_=Action::None;
    ULONGLONG retryAt_=0;
    std::vector<HiddenWindow> windows_;
    NOTIFYICONDATAW data()const;
    bool add();
    void remove();
    void command(UINT id);
    void menu();
};
}
