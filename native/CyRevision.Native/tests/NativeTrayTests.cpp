#include "NativeTray.h"
#include <iostream>
#include <stdexcept>
#include <vector>

using namespace cyrevision;
namespace {
std::vector<DWORD> calls;
bool rejectAdd=false;
BOOL WINAPI shell(DWORD operation,PNOTIFYICONDATAW item){
    if(item->uID!=1 || item->hWnd==nullptr)throw std::runtime_error("Invalid notification icon identity.");
    calls.push_back(operation);return operation==NIM_ADD && rejectAdd?FALSE:TRUE;
}
void require(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
HWND fixture(){return CreateWindowExW(WS_EX_TOOLWINDOW,L"STATIC",L"CyRevision tray fixture",WS_OVERLAPPEDWINDOW,-30000,-30000,160,100,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);}
}
int main(){
    HWND main=fixture(),auxiliary=fixture();
    if(!main || !auxiliary)return 1;
    try{
        ShowWindow(main,SW_SHOWNOACTIVATE);ShowWindow(auxiliary,SW_SHOWNOACTIVATE);
        {
            NativeTray tray(main,LoadIconW(nullptr,IDI_APPLICATION),shell);
            require(tray.available(),"Tray icon was not installed.");
            require(calls.size()==2 && calls[0]==NIM_ADD && calls[1]==NIM_SETVERSION,"Notification version was not configured.");
            require(tray.handleMessage(WM_CLOSE,0,0),"Window close was not handled.");
            require(tray.takeAction()==NativeTray::Action::Hide,"Window close requested application exit.");
            require(IsWindow(main)!=FALSE,"Closing the window destroyed the application.");
            require(tray.hide() && tray.allWindowsHidden(),"Main and auxiliary windows were not hidden.");
            require(!IsWindowVisible(main) && !IsWindowVisible(auxiliary),"An auxiliary window remained visible.");
            tray.handleMessage(NativeTray::CallbackMessage,0,MAKELPARAM(NIN_KEYSELECT,1));
            require(tray.takeAction()==NativeTray::Action::Open,"Keyboard tray selection did not restore the UI.");
            tray.restore();
            require(IsWindowVisible(main) && IsWindowVisible(auxiliary),"Restoring lost an auxiliary window.");
            ShowWindow(auxiliary,SW_MINIMIZE);tray.hide();tray.restore();
            require(IsIconic(auxiliary)!=FALSE,"Restoring changed an already minimized auxiliary window.");
            tray.handleMessage(WM_COMMAND,NativeTray::QuitCommand,0);
            require(tray.takeAction()==NativeTray::Action::Quit,"Explicit tray quit was not routed to the confirmation flow.");
            require(IsWindow(main)!=FALSE,"Tray quit bypassed the application confirmation flow.");
            tray.hide();size_t before=calls.size();
            tray.handleMessage(tray.taskbarCreatedMessage(),0,0);
            require(calls.size()==before+2 && tray.available() && tray.allWindowsHidden(),"Explorer restart lost the tray icon.");
            tray.setEnabled(false);
            require(!tray.available() && IsWindowVisible(main),"Disabling the tray left an unreachable hidden app.");
            tray.handleMessage(WM_CLOSE,0,0);
            require(tray.takeAction()==NativeTray::Action::Quit,"Explicitly disabled close-to-tray did not use normal quit.");
            tray.setEnabled(true);tray.setCloseToTray(false);tray.handleMessage(WM_CLOSE,0,0);
            require(tray.takeAction()==NativeTray::Action::Quit,"Close-to-tray preference was ignored.");
        }
        rejectAdd=true;
        {
            NativeTray tray(main,LoadIconW(nullptr,IDI_APPLICATION),shell);
            require(!tray.available() && !tray.hide() && IsWindowVisible(main),"A failed icon registration stranded the application.");
            rejectAdd=false;tray.handleMessage(tray.taskbarCreatedMessage(),0,0);tray.hide();
            rejectAdd=true;tray.handleMessage(tray.taskbarCreatedMessage(),0,0);
            require(IsWindowVisible(main) && !tray.hidden(),"Failed tray recovery hid the only way back to the app.");
        }
        DestroyWindow(auxiliary);DestroyWindow(main);
        std::cout<<"Native tray lifecycle passed: close/hide, restore, explicit quit, preferences and Explorer recovery.\n";
        return 0;
    }catch(const std::exception& error){DestroyWindow(auxiliary);DestroyWindow(main);std::cerr<<error.what()<<'\n';return 1;}
}
