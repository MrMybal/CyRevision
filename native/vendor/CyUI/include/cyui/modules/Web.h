#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include <string>

namespace cyui {
// Exact http(s) origins only. No file:, javascript:, credentials or wildcard hosts.
CYUI_API std::string webOrigin(const std::string &url);
// Lower-case URL scheme ("https", "mailto"), empty when malformed.
CYUI_API std::string webScheme(const std::string &url);
struct CYUI_API WebPolicy {
    std::vector<std::string> origins;
    std::size_t maxMessageBytes = 1024 * 1024, maxQueuedEvents = 256;
    // Fields below were added later; keep new ones at the end (positional brace-init).
    // maxQueuedErrors: separate, lower cap for error/console events so they cannot evict messages.
    std::size_t maxQueuedErrors = 16;
    // Schemes that may leave the panel as `external` events. Others (file:, ms-settings:,
    // custom protocol handlers...) are dropped and counted. Add schemes deliberately.
    std::vector<std::string> externalSchemes{"http", "https"};
    bool allows(const std::string &url) const;
    bool allowsExternal(const std::string &url) const;
};
// dropped: data holds the number of events discarded since the last one (full queue, caps, policy).
enum class WebEventKind { ready, navigated, message, external, error, dropped };
struct WebEvent {
    WebEventKind kind = WebEventKind::error;
    std::string url, data;
    // Added fields (keep at the end). external only: requested by a user gesture / by the main frame, and the requesting frame's
    // http(s) origin (empty otherwise). A request, never a command: confirm before opening.
    bool userGesture = false, mainFrame = true;
    std::string sourceOrigin;
};
struct WebPlacement {
    Rect bounds, clip;
    bool visible = true, focus = false;
};
enum class WebInputKind { move, button, wheel, key, text, focus };
struct WebInput {
    WebInputKind kind = WebInputKind::move;
    float x = 0, y = 0, wheelX = 0, wheelY = 0;
    int button = 0, clicks = 1, key = 0, nativeKey = 0;
    unsigned modifiers = 0, codepoint = 0;
    bool down = false;
};
// All methods run on the host UI thread. No renderer/browser is loaded by Core.
class WebBackend {
  public:
    virtual ~WebBackend() = default;
    virtual void place(const WebPlacement &) = 0;
    virtual bool navigate(const std::string &) = 0;
    virtual bool postJson(const std::string &) = 0;
    virtual bool poll(WebEvent &) = 0;
    virtual bool ready() const = 0;
    virtual ImTextureID texture() const {
        return 0;
    }
    virtual bool usesTexture() const {
        return false;
    }
    virtual void input(const WebInput &) {}
    virtual bool focused() const {
        return false;
    }
    virtual void close() {}
    virtual bool closed() const {
        return true;
    }
    virtual void beginFrame() {}
    virtual void endFrame() {}
};
struct WebPanelOptions {
    // focus: one-shot; the backend takes keyboard focus when it changes false -> true.
    bool visible = true, focus = false;
    ImVec2 viewportOrigin{};
};
// Call backend.beginFrame() before and backend.endFrame() after the ImGui frame, every frame,
// including frames where the panel's tab is hidden (see docs/WEB.md).
// Bounds are host-client pixels after subtracting viewportOrigin. While focused, the panel owns
// the keys it forwards: ImGui navigation and other widgets do not see them.
CYUI_API std::vector<WebEvent> webPanel(const char *id, WebBackend &, ImVec2 size, const WebPanelOptions & = {});
CYUI_API std::string webThemeJson();
CYUI_API std::string webEventJson(const WebEvent &);
} // namespace cyui
