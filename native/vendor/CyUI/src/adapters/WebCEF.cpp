#include "cyui/adapters/WebCEF.h"
#include "include/cef_client.h"
#include "include/cef_parser.h"
#include "include/cef_v8.h"
#include "include/wrapper/cef_helpers.h"
#include <climits>
#include <cstring>
#include <deque>
#include <map>
#include <stdexcept>
#if defined(OS_WIN)
#include <windows.h>
#endif

namespace cyui {
namespace {
// Browser -> renderer: per-browser page message limit (WebPolicy::maxMessageBytes).
constexpr char kLimitKey[] = "cyui.maxMessageBytes";
constexpr std::size_t kDefaultLimit = 1024 * 1024, kMaxLimit = 64 * 1024 * 1024;
// CEF_REQUIRE_UI_THREAD is a debug check only; public entry points also fail safely in release.
bool uiThread() {
    const bool ok = CefCurrentlyOn(TID_UI);
    DCHECK(ok);
    return ok;
}
class PostHandler final : public CefV8Handler {
    std::size_t limit_;

  public:
    explicit PostHandler(std::size_t limit) : limit_(limit) {}
    bool Execute(const CefString &, CefRefPtr<CefV8Value>, const CefV8ValueList &args,
                 CefRefPtr<CefV8Value> &, CefString &exception) override {
        if (args.size() != 1 || !args[0]->IsString()) {
            exception = "Expected a JSON string";
            return true;
        }
        const auto value = args[0]->GetStringValue();
        // UTF-16 length is a lower bound of the UTF-8 size: reject before converting huge strings.
        const auto text = value.length() > limit_ ? std::string() : value.ToString();
        if (value.length() > limit_ || text.size() > limit_) {
            exception = "CyUI message exceeds the host's size limit";
            return true;
        }
        const auto context = CefV8Context::GetCurrentContext();
        if (!context || !context->GetFrame()->IsMain())
            return true;
        auto message = CefProcessMessage::Create("cyui.page");
        message->GetArgumentList()->SetString(0, text);
        context->GetFrame()->SendProcessMessage(PID_BROWSER, message);
        return true;
    }
    IMPLEMENT_REFCOUNTING(PostHandler);
};
class App final : public CefApp, public CefRenderProcessHandler {
    std::map<int, std::size_t> limits_; // Renderer main thread only.

  public:
    CefRefPtr<CefRenderProcessHandler> GetRenderProcessHandler() override {
        return this;
    }
    void OnBrowserCreated(CefRefPtr<CefBrowser> browser, CefRefPtr<CefDictionaryValue> extra) override {
        if (extra && extra->GetType(kLimitKey) == VTYPE_INT)
            limits_[browser->GetIdentifier()] = std::size_t(std::max(1, extra->GetInt(kLimitKey)));
    }
    void OnBrowserDestroyed(CefRefPtr<CefBrowser> browser) override {
        limits_.erase(browser->GetIdentifier());
    }
    void OnContextCreated(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                          CefRefPtr<CefV8Context> context) override {
        if (!frame->IsMain())
            return;
        const auto found = limits_.find(browser->GetIdentifier());
        const auto limit = found == limits_.end() ? kDefaultLimit : found->second;
        context->GetGlobal()->SetValue("__cyuiPost",
                                       CefV8Value::CreateFunction("__cyuiPost", new PostHandler(limit)),
                                       V8_PROPERTY_ATTRIBUTE_READONLY);
        frame->ExecuteJavaScript(
            R"JS((()=>{const listeners=new Set();Object.defineProperty(window,'cyuiNative',{value:Object.freeze({postMessage(message){window.__cyuiPost(JSON.stringify(message))},addEventListener(type,fn){if(type==='message')listeners.add(fn)},removeEventListener(type,fn){if(type==='message')listeners.delete(fn)}})});Object.defineProperty(window,'__cyuiReceive',{value(text){const data=JSON.parse(text);for(const fn of [...listeners])fn({data})}})})();)JS",
            frame->GetURL(), 0);
    }
    bool OnProcessMessageReceived(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame> frame, CefProcessId source,
                                  CefRefPtr<CefProcessMessage> message) override {
        if (source != PID_BROWSER || message->GetName() != "cyui.host" || !frame->IsMain())
            return false;
        // Deliver only to the origin the host addressed: the page may have navigated meanwhile.
        const auto args = message->GetArgumentList();
        const auto origin = webOrigin(frame->GetURL().ToString());
        if (args->GetSize() < 2 || origin.empty() || origin != args->GetString(1).ToString())
            return true;
        const auto context = frame->GetV8Context();
        if (!context || !context->Enter())
            return true;
        auto function = context->GetGlobal()->GetValue("__cyuiReceive");
        if (function && function->IsFunction())
            function->ExecuteFunction(nullptr, {CefV8Value::CreateString(args->GetString(0))});
        context->Exit();
        return true;
    }
    IMPLEMENT_REFCOUNTING(App);
};
struct State {
    WebCEFOptions options;
    WebPlacement placement;
    CefRefPtr<CefBrowser> browser;
    std::deque<WebEvent> events;
    std::size_t errors = 0, dropped = 0;
    // pixelsW/H: size of `pixels` (the last view paint), which may lag width/height after a resize.
    int width = 1, height = 1, pixelsW = 0, pixelsH = 0, popupWidth = 0, popupHeight = 0;
    int clicks[3] = {1, 1, 1};
    bool loaded = false, closing = false, closed = false, focused = false, popupVisible = false,
         placed = false;
    CefRect popup;
    std::vector<unsigned char> pixels, popupPixels, composite;
    ImTextureID texture = 0;
    std::string pending;
    void pop() {
        errors -= events.front().kind == WebEventKind::error ? 1 : 0;
        events.pop_front();
    }
    void push(WebEvent event) {
        // Errors (console, load) have their own low cap so a noisy page cannot evict messages.
        if (event.kind == WebEventKind::error && errors >= options.policy.maxQueuedErrors) {
            ++dropped;
            return;
        }
        if (events.size() >= options.policy.maxQueuedEvents) {
            pop();
            ++dropped;
        }
        errors += event.kind == WebEventKind::error ? 1 : 0;
        events.push_back(std::move(event));
    }
    void emit(WebEventKind kind, std::string url = {}, std::string data = {}) {
        if (data.size() > options.policy.maxMessageBytes || url.size() > 8192) {
            ++dropped;
            return;
        }
        push({kind, std::move(url), std::move(data)});
    }
    void external(std::string url, bool gesture, bool mainFrame, const std::string &source) {
        if (!options.policy.allowsExternal(url)) {
            ++dropped;
            return;
        }
        WebEvent event{WebEventKind::external, std::move(url)};
        event.userGesture = gesture;
        event.mainFrame = mainFrame;
        event.sourceOrigin = webOrigin(source);
        push(std::move(event));
    }
    void upload() {
        if (closing || pixels.empty() || !options.upload)
            return;
        const auto *data = pixels.data();
        if (popupVisible && popupWidth > 0 && popupHeight > 0 &&
            popupPixels.size() == std::size_t(popupWidth) * popupHeight * 4) {
            // The popup is clamped to the view buffer actually held: both may be from other sizes.
            composite = pixels;
            const long long left = std::max<long long>(0, popup.x),
                            right = std::min<long long>(pixelsW, (long long)popup.x + popupWidth);
            for (int y = 0; y < popupHeight && right > left; ++y) {
                const long long destY = (long long)popup.y + y;
                if (destY < 0 || destY >= pixelsH)
                    continue;
                std::memcpy(composite.data() + std::size_t(destY * pixelsW + left) * 4,
                            popupPixels.data() + std::size_t((long long)y * popupWidth + left - popup.x) * 4,
                            std::size_t(right - left) * 4);
            }
            data = composite.data();
        }
        texture = options.upload(pixelsW, pixelsH, data);
    }
    void finish() {
        browser = nullptr;
        closed = true;
        const auto last = texture;
        texture = 0;
        if (auto release = std::move(options.release); last && release)
            release(last);
        options.release = {};
    }
};
class Client final : public CefClient,
                     public CefRenderHandler,
                     public CefLifeSpanHandler,
                     public CefLoadHandler,
                     public CefRequestHandler,
                     public CefDisplayHandler,
                     public CefDownloadHandler,
                     public CefPermissionHandler,
                     public CefContextMenuHandler {
    std::shared_ptr<State> s_;

  public:
    explicit Client(std::shared_ptr<State> s) : s_(std::move(s)) {}
    CefRefPtr<CefRenderHandler> GetRenderHandler() override {
        return this;
    }
    CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override {
        return this;
    }
    CefRefPtr<CefLoadHandler> GetLoadHandler() override {
        return this;
    }
    CefRefPtr<CefRequestHandler> GetRequestHandler() override {
        return this;
    }
    CefRefPtr<CefDisplayHandler> GetDisplayHandler() override {
        return this;
    }
    CefRefPtr<CefDownloadHandler> GetDownloadHandler() override {
        return this;
    }
    CefRefPtr<CefPermissionHandler> GetPermissionHandler() override {
        return this;
    }
    CefRefPtr<CefContextMenuHandler> GetContextMenuHandler() override {
        return this;
    }
    void GetViewRect(CefRefPtr<CefBrowser>, CefRect &rect) override {
        rect = {0, 0, s_->width, s_->height};
    }
    bool GetScreenInfo(CefRefPtr<CefBrowser>, CefScreenInfo &info) override {
        // Placement/input and the host texture use the same pixel coordinate space.
        // Host DPI is expressed through the CyUI CSS theme, not a second CEF scale.
        info.device_scale_factor = 1;
        info.depth = 32;
        info.depth_per_component = 8;
        info.rect = info.available_rect = {0, 0, s_->width, s_->height};
        return true;
    }
    bool GetScreenPoint(CefRefPtr<CefBrowser>, int x, int y, int &screenX, int &screenY) override {
        screenX = int(s_->placement.bounds.min.x) + x;
        screenY = int(s_->placement.bounds.min.y) + y;
        return true;
    }
    void OnPopupShow(CefRefPtr<CefBrowser>, bool show) override {
        CEF_REQUIRE_UI_THREAD();
        s_->popupVisible = show;
        if (!show)
            s_->popupPixels.clear();
        s_->upload();
    }
    void OnPopupSize(CefRefPtr<CefBrowser>, const CefRect &rect) override {
        s_->popup = rect;
    }
    void OnPaint(CefRefPtr<CefBrowser>, PaintElementType type, const RectList &, const void *buffer,
                 int width, int height) override {
        CEF_REQUIRE_UI_THREAD();
        if (s_->closing || width < 1 || height < 1 || width > s_->options.maxWidth ||
            height > s_->options.maxHeight)
            return;
        const auto *bytes = static_cast<const unsigned char *>(buffer);
        const auto count = std::size_t(width) * height * 4;
        if (type == PET_VIEW) {
            if (width != s_->width || height != s_->height)
                return; // A queued paint from before the latest resize.
            s_->pixels.assign(bytes, bytes + count);
            s_->pixelsW = width;
            s_->pixelsH = height;
        } else {
            s_->popupWidth = width;
            s_->popupHeight = height;
            s_->popupPixels.assign(bytes, bytes + count);
        }
        s_->upload();
    }
    void OnAfterCreated(CefRefPtr<CefBrowser> browser) override {
        CEF_REQUIRE_UI_THREAD();
        s_->browser = browser;
        if (s_->closing) {
            browser->GetHost()->CloseBrowser(true);
            return;
        }
        browser->GetHost()->WasResized();
        browser->GetHost()->WasHidden(!s_->placement.visible);
        s_->emit(WebEventKind::ready);
        if (!s_->pending.empty())
            browser->GetMainFrame()->LoadURL(s_->pending);
    }
    void OnBeforeClose(CefRefPtr<CefBrowser>) override {
        CEF_REQUIRE_UI_THREAD();
        s_->finish();
    }
    bool OnBeforePopup(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame> frame, int, const CefString &url,
                       const CefString &, cef_window_open_disposition_t, bool userGesture,
                       const CefPopupFeatures &, CefWindowInfo &, CefRefPtr<CefClient> &,
                       CefBrowserSettings &, CefRefPtr<CefDictionaryValue> &, bool *) override {
        // Never opened here. Sub-frames (possibly cross-origin iframes) need a user gesture.
        const bool mainFrame = frame && frame->IsMain();
        if (mainFrame || userGesture)
            s_->external(url, userGesture, mainFrame, frame ? frame->GetURL().ToString() : std::string());
        else
            ++s_->dropped;
        return true;
    }
    bool OnBeforeBrowse(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame> frame, CefRefPtr<CefRequest> request,
                        bool userGesture, bool) override {
        if (!frame->IsMain())
            return false;
        const auto url = request->GetURL().ToString();
        if (url == "about:blank")
            return false;
        s_->loaded = false;
        if (!s_->options.policy.allows(url)) {
            s_->external(url, userGesture, true, frame->GetURL().ToString());
            return true;
        }
        return false;
    }
    void OnLoadEnd(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame> frame, int status) override {
        if (!frame->IsMain())
            return;
        const auto url = frame->GetURL().ToString();
        s_->loaded = s_->options.policy.allows(url);
        if (s_->loaded)
            s_->emit(WebEventKind::navigated, url, std::to_string(status));
    }
    void OnLoadError(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame> frame, ErrorCode code, const CefString &text,
                     const CefString &url) override {
        if (frame->IsMain() && code != ERR_ABORTED) {
            s_->loaded = false;
            s_->emit(WebEventKind::error, url, text);
        }
    }
    bool OnConsoleMessage(CefRefPtr<CefBrowser>, cef_log_severity_t level, const CefString &message,
                          const CefString &source, int) override {
        if (level >= LOGSEVERITY_ERROR)
            s_->emit(WebEventKind::error, source, message.ToString().substr(0, 4096));
        return false;
    }
    bool OnProcessMessageReceived(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame> frame, CefProcessId source,
                                  CefRefPtr<CefProcessMessage> message) override {
        if (source != PID_RENDERER || message->GetName() != "cyui.page")
            return false;
        const auto url = frame->GetURL().ToString();
        // Size is checked before parsing: the UI-thread parse is bounded by maxMessageBytes.
        const auto text = message->GetArgumentList()->GetString(0).ToString();
        if (frame->IsMain() && s_->options.policy.allows(url) &&
            text.size() <= s_->options.policy.maxMessageBytes && CefParseJSON(text, JSON_PARSER_RFC))
            s_->emit(WebEventKind::message, url, text);
        return true;
    }
    void OnBeforeContextMenu(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame>, CefRefPtr<CefContextMenuParams>,
                             CefRefPtr<CefMenuModel> model) override {
        model->Clear();
    }
    bool OnBeforeDownload(CefRefPtr<CefBrowser>, CefRefPtr<CefDownloadItem>, const CefString &,
                          CefRefPtr<CefBeforeDownloadCallback>) override {
        return false;
    }
    bool OnRequestMediaAccessPermission(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame>, const CefString &,
                                        uint32_t, CefRefPtr<CefMediaAccessCallback> callback) override {
        callback->Cancel();
        return true;
    }
    bool OnShowPermissionPrompt(CefRefPtr<CefBrowser>, uint64_t, const CefString &, uint32_t,
                                CefRefPtr<CefPermissionPromptCallback> callback) override {
        callback->Continue(CEF_PERMISSION_RESULT_DENY);
        return true;
    }
    IMPLEMENT_REFCOUNTING(Client);
};
uint32_t modifiers(unsigned value) {
    uint32_t out = 0;
    if (value & 1)
        out |= EVENTFLAG_SHIFT_DOWN;
    if (value & 2)
        out |= EVENTFLAG_CONTROL_DOWN;
    if (value & 4)
        out |= EVENTFLAG_ALT_DOWN;
    if (value & 8)
        out |= EVENTFLAG_COMMAND_DOWN;
    if (value & 16)
        out |= EVENTFLAG_LEFT_MOUSE_BUTTON;
    if (value & 32)
        out |= EVENTFLAG_RIGHT_MOUSE_BUTTON;
    if (value & 64)
        out |= EVENTFLAG_MIDDLE_MOUSE_BUTTON;
    return out;
}
// Left/right modifier virtual keys (0xA0-0xA5) become the generic DOM keyCode plus a location.
int genericKey(int key) {
    return key == 0xa0 || key == 0xa1 ? 0x10 : key == 0xa2 || key == 0xa3 ? 0x11 : key == 0xa4 || key == 0xa5 ? 0x12 : key;
}
uint32_t keyLocation(int key) {
    if (key == 0xa0 || key == 0xa2 || key == 0xa4 || key == 0x5b)
        return EVENTFLAG_IS_LEFT;
    if (key == 0xa1 || key == 0xa3 || key == 0xa5 || key == 0x5c)
        return EVENTFLAG_IS_RIGHT;
    return key >= 0x60 && key <= 0x6f ? EVENTFLAG_IS_KEY_PAD : 0;
}
int nativeKey(int key, bool down) {
#if defined(OS_WIN)
    // The bare scan code with its 0xE0/0xE1 extended prefix, not a WM_KEYDOWN lParam: tested with
    // CEF 150 windowless (tests/webcef_smoke.cpp), the lParam layout leaves KeyboardEvent.code
    // empty while this sets it (layout-dependent: VK 'A' is KeyQ on AZERTY).
    (void)down;
    return int(MapVirtualKeyW(UINT(key), MAPVK_VK_TO_VSC_EX));
#else
    (void)key;
    (void)down;
    return 0;
#endif
}
class Backend final : public WebBackend {
    std::shared_ptr<State> s_;

  public:
    explicit Backend(const WebCEFOptions &o) : s_(std::make_shared<State>()) {
        CEF_REQUIRE_UI_THREAD();
        if (!CefCurrentlyOn(TID_UI))
            throw std::logic_error("WebCEF must be created on the CEF UI thread");
        if (!o.upload)
            throw std::invalid_argument("WebCEF requires a host BGRA texture upload callback");
        s_->options = o;
        s_->options.maxWidth = std::clamp(o.maxWidth, 1, 8192);
        s_->options.maxHeight = std::clamp(o.maxHeight, 1, 8192);
        auto &policy = s_->options.policy;
        policy.maxMessageBytes = std::clamp<std::size_t>(policy.maxMessageBytes, 1, kMaxLimit);
        policy.maxQueuedEvents = std::clamp<std::size_t>(policy.maxQueuedEvents, 1, 65536);
        policy.maxQueuedErrors = std::clamp<std::size_t>(policy.maxQueuedErrors, 1, policy.maxQueuedEvents);
        s_->placement.visible = false;
        if (!o.url.empty() && !o.policy.allows(o.url))
            throw std::invalid_argument("WebCEF URL is outside the allowed origins");
        s_->pending = o.url;
        CefWindowInfo window;
        window.SetAsWindowless(o.parent);
        window.runtime_style = CEF_RUNTIME_STYLE_ALLOY;
        CefBrowserSettings settings;
        settings.windowless_frame_rate = std::clamp(o.framesPerSecond, 1, 60);
        settings.background_color = 0;
        auto extra = CefDictionaryValue::Create();
        extra->SetInt(kLimitKey, int(std::min<std::size_t>(policy.maxMessageBytes, INT_MAX)));
        if (!CefBrowserHost::CreateBrowser(window, new Client(s_), "about:blank", settings, extra, nullptr)) {
            s_->closed = true;
            throw std::runtime_error("CEF browser creation failed");
        }
    }
    ~Backend() override {
        close();
    }
    void close() override {
        if (!uiThread() || s_->closing)
            return;
        s_->closing = true;
        s_->options.upload = {};
        if (s_->browser)
            s_->browser->GetHost()->CloseBrowser(true);
    }
    bool closed() const override {
        return s_->closed;
    }
    void beginFrame() override {
        s_->placed = false;
    }
    void endFrame() override {
        if (!s_->placed && s_->placement.visible) {
            auto p = s_->placement;
            p.visible = false;
            place(p);
            input({WebInputKind::focus});
        }
    }
    void place(const WebPlacement &p) override {
        if (!uiThread())
            return;
        s_->placed = true;
        const int width = std::clamp(int(std::ceil(p.bounds.size().x)), 1, s_->options.maxWidth),
                  height = std::clamp(int(std::ceil(p.bounds.size().y)), 1, s_->options.maxHeight);
        const bool resized = width != s_->width || height != s_->height,
                   visibility = p.visible != s_->placement.visible,
                   focus = p.focus && !s_->placement.focus; // One-shot on the rising edge.
        s_->placement = p;
        s_->width = width;
        s_->height = height;
        if (s_->browser && !s_->closing) {
            if (resized)
                s_->browser->GetHost()->WasResized();
            if (visibility)
                s_->browser->GetHost()->WasHidden(!p.visible);
            if (focus) {
                s_->focused = true;
                s_->browser->GetHost()->SetFocus(true);
            }
        }
    }
    bool navigate(const std::string &url) override {
        if (!uiThread())
            return false;
        if (s_->closing || !s_->options.policy.allows(url)) {
            s_->external(url, false, true, {});
            return false;
        }
        s_->loaded = false;
        s_->pending = url;
        if (s_->browser)
            s_->browser->GetMainFrame()->LoadURL(url);
        return true;
    }
    bool postJson(const std::string &json) override {
        if (!uiThread() || s_->closing || !s_->browser || !s_->loaded ||
            json.size() > s_->options.policy.maxMessageBytes)
            return false;
        // Bind the message to the page loaded now; the renderer drops it after a navigation.
        const auto frame = s_->browser->GetMainFrame();
        const auto url = frame ? frame->GetURL().ToString() : std::string();
        if (!s_->options.policy.allows(url) || !CefParseJSON(json, JSON_PARSER_RFC))
            return false;
        auto message = CefProcessMessage::Create("cyui.host");
        message->GetArgumentList()->SetString(0, json);
        message->GetArgumentList()->SetString(1, webOrigin(url));
        frame->SendProcessMessage(PID_RENDERER, message);
        return true;
    }
    bool poll(WebEvent &event) override {
        if (!uiThread())
            return false;
        if (s_->dropped) {
            event = {WebEventKind::dropped, {}, std::to_string(s_->dropped)};
            s_->dropped = 0;
            return true;
        }
        if (s_->events.empty())
            return false;
        event = std::move(s_->events.front());
        s_->pop();
        return true;
    }
    bool ready() const override {
        return bool(s_->browser) && !s_->closing;
    }
    bool usesTexture() const override {
        return true;
    }
    ImTextureID texture() const override {
        return s_->texture;
    }
    bool focused() const override {
        return s_->focused;
    }
    CefRefPtr<CefBrowser> browser() const {
        return s_->browser;
    }
    void input(const WebInput &e) override {
        if (!uiThread() || !s_->browser || s_->closing)
            return;
        auto host = s_->browser->GetHost();
        CefMouseEvent mouse;
        mouse.x = int(e.x * s_->width / std::max(1.f, s_->placement.bounds.size().x));
        mouse.y = int(e.y * s_->height / std::max(1.f, s_->placement.bounds.size().y));
        mouse.modifiers = modifiers(e.modifiers);
        switch (e.kind) {
        case WebInputKind::focus:
            s_->focused = e.down;
            host->SetFocus(e.down);
            break;
        case WebInputKind::move:
            host->SendMouseMoveEvent(mouse, !e.down);
            break;
        case WebInputKind::button: {
            const int button = std::clamp(e.button, 0, 2);
            // The release reuses the press's click count, or Blink never fires dblclick.
            if (e.down)
                s_->clicks[button] = std::clamp(e.clicks, 1, 16);
            host->SendMouseClickEvent(mouse,
                                      button == 0   ? MBT_LEFT
                                      : button == 1 ? MBT_RIGHT
                                                    : MBT_MIDDLE,
                                      !e.down, s_->clicks[button]);
            break;
        }
        case WebInputKind::wheel:
            host->SendMouseWheelEvent(mouse, int(e.wheelX * 120), int(e.wheelY * 120));
            break;
        case WebInputKind::key: {
            if (s_->focused)
                host->SetFocus(true);
            CefKeyEvent key;
            key.type = e.down ? KEYEVENT_RAWKEYDOWN : KEYEVENT_KEYUP;
            key.windows_key_code = genericKey(e.key);
            key.native_key_code = e.nativeKey ? e.nativeKey : nativeKey(e.key, e.down);
            key.modifiers = mouse.modifiers | keyLocation(e.key);
            host->SendKeyEvent(key);
            break;
        }
        case WebInputKind::text: {
            if (s_->focused)
                host->SetFocus(true);
            unsigned cp = e.codepoint;
            if (cp > 0x10ffff || (cp >= 0xd800 && cp <= 0xdfff))
                break;
            CefKeyEvent key;
            key.type = KEYEVENT_CHAR;
            key.modifiers = mouse.modifiers;
            if (cp > 0xffff) {
                cp -= 0x10000;
                key.windows_key_code = key.character = key.unmodified_character =
                    char16_t(0xd800 + (cp >> 10));
                host->SendKeyEvent(key);
                cp = 0xdc00 + (cp & 1023);
            }
            key.windows_key_code = key.character = key.unmodified_character = char16_t(cp);
            host->SendKeyEvent(key);
            break;
        }
        }
    }
};
} // namespace
CefRefPtr<CefApp> webCEFApp() {
    return new App;
}
std::unique_ptr<WebBackend> createWebCEF(const WebCEFOptions &o) {
    return std::make_unique<Backend>(o);
}
CefRefPtr<CefBrowser> webCEFBrowser(WebBackend &backend) {
    auto *cef = dynamic_cast<Backend *>(&backend);
    return cef ? cef->browser() : nullptr;
}
} // namespace cyui
