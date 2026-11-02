#pragma once
#include "cyui/modules/Web.h"
#include "include/cef_app.h"
#include "include/cef_browser.h"
#include <functional>
#include <memory>

namespace cyui {
struct WebCEFOptions {
    WebPolicy policy;
    CefWindowHandle parent{};
    std::string url;
    int maxWidth = 4096, maxHeight = 4096, framesPerSecond = 30;
    // BGRA8, upper-left origin. Called on the CEF UI thread from inside CefDoMessageLoopWork.
    // Host owns/reuses the texture; pump CEF outside the ImGui frame (docs/WEB.md).
    std::function<ImTextureID(int width, int height, const void *bgra)> upload;
    // Called once, from CefDoMessageLoopWork, when the browser has closed: the last texture
    // returned by upload is no longer used by the view. Defer the GPU release until no
    // submitted frame references it.
    std::function<void(ImTextureID)> release;
};
// Pass the SAME app to CefExecuteProcess and CefInitialize, including helper executables.
// Host owns CEF initialization, process sandbox, message pump and shutdown.
CYUI_API CefRefPtr<CefApp> webCEFApp();
// Every WebBackend call must run on the CEF UI thread; calls from other threads are ignored
// (and fail in debug builds). Construction off the UI thread throws.
CYUI_API std::unique_ptr<WebBackend> createWebCEF(const WebCEFOptions &);
// Advanced host access for IME composition, native key events, drag/drop and accessibility.
CYUI_API CefRefPtr<CefBrowser> webCEFBrowser(WebBackend &);
} // namespace cyui
