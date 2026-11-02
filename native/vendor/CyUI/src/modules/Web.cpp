#include "cyui/modules/Web.h"
#include <imgui_internal.h>
#include <cctype>
#include <sstream>
#include <locale>

namespace cyui {
namespace {
std::string quote(const std::string &value) {
    std::string out = "\"";
    for (unsigned char c : value) {
        if (c == '"' || c == '\\') {
            out += '\\';
            out += char(c);
        } else if (c < 32) {
            const char *hex = "0123456789abcdef";
            out += "\\u00";
            out += hex[c >> 4];
            out += hex[c & 15];
        } else
            out += char(c);
    }
    return out + '"';
}
std::string lower(std::string value) {
    for (auto &c : value)
        c = char(std::tolower(static_cast<unsigned char>(c)));
    return value;
}
// ImGui key -> Windows virtual-key code (the DOM keyCode CEF expects). Left/right modifiers keep
// their side (0xA0-0xA5); the CEF adapter turns them into the generic code plus a scan code.
struct KeyMap {
    ImGuiKey key;
    int code;
};
constexpr KeyMap kKeys[] = {
    {ImGuiKey_Backspace, 0x08}, {ImGuiKey_Tab, 0x09}, {ImGuiKey_Enter, 0x0d}, {ImGuiKey_KeypadEnter, 0x0d},
    {ImGuiKey_Pause, 0x13}, {ImGuiKey_CapsLock, 0x14}, {ImGuiKey_Escape, 0x1b}, {ImGuiKey_Space, 0x20},
    {ImGuiKey_PageUp, 0x21}, {ImGuiKey_PageDown, 0x22}, {ImGuiKey_End, 0x23}, {ImGuiKey_Home, 0x24},
    {ImGuiKey_LeftArrow, 0x25}, {ImGuiKey_UpArrow, 0x26}, {ImGuiKey_RightArrow, 0x27},
    {ImGuiKey_DownArrow, 0x28}, {ImGuiKey_PrintScreen, 0x2c}, {ImGuiKey_Insert, 0x2d},
    {ImGuiKey_Delete, 0x2e}, {ImGuiKey_LeftSuper, 0x5b}, {ImGuiKey_RightSuper, 0x5c}, {ImGuiKey_Menu, 0x5d},
    {ImGuiKey_KeypadMultiply, 0x6a}, {ImGuiKey_KeypadAdd, 0x6b}, {ImGuiKey_KeypadSubtract, 0x6d},
    {ImGuiKey_KeypadDecimal, 0x6e}, {ImGuiKey_KeypadDivide, 0x6f}, {ImGuiKey_NumLock, 0x90},
    {ImGuiKey_ScrollLock, 0x91}, {ImGuiKey_LeftShift, 0xa0}, {ImGuiKey_RightShift, 0xa1},
    {ImGuiKey_LeftCtrl, 0xa2}, {ImGuiKey_RightCtrl, 0xa3}, {ImGuiKey_LeftAlt, 0xa4}, {ImGuiKey_RightAlt, 0xa5},
    {ImGuiKey_AppBack, 0xa6}, {ImGuiKey_AppForward, 0xa7}, {ImGuiKey_Semicolon, 0xba}, {ImGuiKey_Equal, 0xbb},
    {ImGuiKey_Comma, 0xbc}, {ImGuiKey_Minus, 0xbd}, {ImGuiKey_Period, 0xbe}, {ImGuiKey_Slash, 0xbf},
    {ImGuiKey_GraveAccent, 0xc0}, {ImGuiKey_LeftBracket, 0xdb}, {ImGuiKey_Backslash, 0xdc},
    {ImGuiKey_RightBracket, 0xdd}, {ImGuiKey_Apostrophe, 0xde}, {ImGuiKey_Oem102, 0xe2}};
template <class F> void forEachKey(F &&f) {
    for (int n = 0; n < 26; ++n)
        f(ImGuiKey(ImGuiKey_A + n), 0x41 + n);
    for (int n = 0; n < 10; ++n) {
        f(ImGuiKey(ImGuiKey_0 + n), 0x30 + n);
        f(ImGuiKey(ImGuiKey_Keypad0 + n), 0x60 + n);
    }
    for (int n = 0; n < 24; ++n)
        f(ImGuiKey(ImGuiKey_F1 + n), 0x70 + n);
    for (const auto &k : kKeys)
        f(k.key, k.code);
}
} // namespace
std::string webOrigin(const std::string &url) {
    if (url.size() > 8192 || url.find_first_of("\\\r\n\t ") != std::string::npos)
        return {};
    for (unsigned char c : url)
        if (c < 32 || c == 127)
            return {};
    const auto marker = url.find("://");
    if (marker == std::string::npos)
        return {};
    auto scheme = url.substr(0, marker);
    for (auto &c : scheme)
        c = char(std::tolower(static_cast<unsigned char>(c)));
    if (scheme != "http" && scheme != "https")
        return {};
    const auto end = url.find_first_of("/?#", marker + 3);
    auto authority = url.substr(marker + 3, end == std::string::npos ? end : end - marker - 3);
    if (authority.empty() || authority.find('@') != std::string::npos ||
        authority.find('%') != std::string::npos)
        return {};
    for (auto &c : authority) {
        if (static_cast<unsigned char>(c) >= 128)
            return {};
        c = char(std::tolower(static_cast<unsigned char>(c)));
    }
    std::string host, port;
    if (authority.front() == '[') {
        const auto close = authority.find(']');
        if (close == std::string::npos || close <= 2)
            return {};
        host = authority.substr(0, close + 1);
        for (std::size_t n = 1; n < close; ++n)
            if (!std::isxdigit(static_cast<unsigned char>(host[n])) && host[n] != ':')
                return {};
        if (close + 1 < authority.size()) {
            if (authority[close + 1] != ':')
                return {};
            port = authority.substr(close + 2);
            if (port.empty())
                return {};
        }
    } else {
        const auto colon = authority.find(':');
        host = authority.substr(0, colon);
        if (colon != std::string::npos) {
            port = authority.substr(colon + 1);
            if (port.empty())
                return {};
        }
        for (char c : host)
            if (!std::isalnum(static_cast<unsigned char>(c)) && c != '.' && c != '-')
                return {};
    }
    if (host.empty())
        return {};
    if (!port.empty()) {
        unsigned number = 0;
        for (char c : port) {
            if (c < '0' || c > '9')
                return {};
            number = number * 10 + unsigned(c - '0');
            if (number > 65535)
                return {};
        }
        if (number == 0)
            return {};
        port = std::to_string(number);
        if ((scheme == "https" && port == "443") || (scheme == "http" && port == "80"))
            port.clear();
    }
    return scheme + "://" + host + (port.empty() ? "" : ":" + port);
}
std::string webScheme(const std::string &url) {
    const auto colon = url.find(':');
    if (colon == std::string::npos || colon == 0 || colon > 64 ||
        !std::isalpha(static_cast<unsigned char>(url[0])))
        return {};
    for (std::size_t n = 0; n < colon; ++n) {
        const auto c = static_cast<unsigned char>(url[n]);
        if (!std::isalnum(c) && c != '+' && c != '-' && c != '.')
            return {};
    }
    return lower(url.substr(0, colon));
}
bool WebPolicy::allowsExternal(const std::string &url) const {
    const auto scheme = webScheme(url);
    if (scheme.empty() || url.size() > 8192 ||
        ((scheme == "http" || scheme == "https") && webOrigin(url).empty()))
        return false;
    for (unsigned char c : url)
        if (c < 32 || c == 127)
            return false;
    for (const auto &entry : externalSchemes)
        if (lower(entry) == scheme)
            return true;
    return false;
}
bool WebPolicy::allows(const std::string &url) const {
    const auto origin = webOrigin(url);
    if (origin.empty())
        return false;
    for (const auto &entry : origins)
        if (webOrigin(entry) == origin)
            return true;
    return false;
}
std::vector<WebEvent> webPanel(const char *id, WebBackend &backend, ImVec2 size, const WebPanelOptions &o) {
    if (size.x <= 0)
        size.x = std::max(dp(1), ImGui::GetContentRegionAvail().x + size.x);
    if (size.y <= 0)
        size.y = std::max(dp(1), ImGui::GetContentRegionAvail().y + size.y);
    const auto a = ImGui::GetCursorScreenPos();
    ImGui::InvisibleButton(id, size);
    const ImGuiID owner = ImGui::GetItemID();
    const auto b = ImVec2(a.x + size.x, a.y + size.y);
    auto *d = ImGui::GetWindowDrawList();
    const auto low = d->GetClipRectMin(), high = d->GetClipRectMax();
    WebPlacement placement;
    placement.bounds = {{a.x - o.viewportOrigin.x, a.y - o.viewportOrigin.y},
                        {b.x - o.viewportOrigin.x, b.y - o.viewportOrigin.y}};
    placement.clip = {
        {std::max(a.x, low.x) - o.viewportOrigin.x, std::max(a.y, low.y) - o.viewportOrigin.y},
        {std::min(b.x, high.x) - o.viewportOrigin.x, std::min(b.y, high.y) - o.viewportOrigin.y}};
    placement.visible =
        o.visible && ImGui::IsItemVisible() && placement.clip.size().x > 0 && placement.clip.size().y > 0;
    placement.focus = o.focus;
    d->AddRectFilled(a, b, palette().bg0);
    if (!backend.ready())
        centredText(d, a, b, palette().textDim, tr("Web view loading...").c_str());
    backend.place(placement);
    if (backend.texture())
        d->AddImage(backend.texture(), a, b);
    if (backend.usesTexture() && placement.visible) {
        auto &io = ImGui::GetIO();
        const bool hovered = ImGui::IsItemHovered();
        WebInput event;
        event.x = io.MousePos.x - a.x;
        event.y = io.MousePos.y - a.y;
        event.modifiers = (io.KeyShift ? 1u : 0u) | (io.KeyCtrl ? 2u : 0u) | (io.KeyAlt ? 4u : 0u) |
                          (io.KeySuper ? 8u : 0u) | (io.MouseDown[0] ? 16u : 0u) |
                          (io.MouseDown[1] ? 32u : 0u) | (io.MouseDown[2] ? 64u : 0u);
        if (ImGui::IsMouseClicked(0)) {
            event.kind = WebInputKind::focus;
            event.down = hovered;
            backend.input(event);
        }
        event.kind = WebInputKind::move;
        event.down = hovered;
        backend.input(event);
        auto *storage = ImGui::GetStateStorage();
        for (int button = 0; button < 3; ++button)
            if ((hovered || backend.focused()) &&
                (ImGui::IsMouseClicked(button) || ImGui::IsMouseReleased(button))) {
                event.kind = WebInputKind::button;
                event.button = button;
                event.down = ImGui::IsMouseClicked(button);
                // The release repeats the press's count, or Blink never fires dblclick.
                const ImGuiID clicks = ImHashData(&button, sizeof button, owner);
                if (event.down)
                    storage->SetInt(clicks, std::max(1, ImGui::GetMouseClickedCount(button)));
                event.clicks = storage->GetInt(clicks, 1);
                backend.input(event);
            }
        // The panel is still the last item: claim both wheels in time so the parent never scrolls too.
        // Horizontal first: itemWheel() marks the wheel taken, which must not drop a diagonal X.
        event.wheelX = 0;
        if (hovered) {
            if (ImGui::TestKeyOwner(ImGuiKey_MouseWheelX, owner) && !wheelTaken())
                event.wheelX = io.MouseWheelH;
            ImGui::SetItemKeyOwner(ImGuiKey_MouseWheelX);
        }
        event.wheelY = itemWheel();
        if (event.wheelX || event.wheelY) {
            event.kind = WebInputKind::wheel;
            backend.input(event);
        }
        event.wheelX = event.wheelY = 0;
        if (backend.focused()) {
            ImGui::SetNextFrameWantCaptureKeyboard(true);
            event.kind = WebInputKind::key;
            // Claim every forwarded key: locked for the rest of this frame (until release when
            // held) and owned next frame, so keyboard nav (Tab/arrows/Space/Enter/Escape),
            // shortcuts and widgets drawn later do not act on keys meant for the page.
            forEachKey([&](ImGuiKey code, int virtualKey) {
                const bool pressed = ImGui::IsKeyPressed(code, ImGuiInputFlags_Repeat, owner),
                           released = ImGui::IsKeyReleased(code, owner);
                ImGui::SetKeyOwner(code, owner, ImGuiInputFlags_LockUntilRelease);
                event.key = virtualKey;
                if (pressed) {
                    event.down = true;
                    backend.input(event);
                }
                if (released) {
                    event.down = false;
                    backend.input(event);
                }
            });
            event.kind = WebInputKind::text;
            for (auto cp : io.InputQueueCharacters) {
                event.codepoint = unsigned(cp);
                backend.input(event);
            }
        }
    }
    std::vector<WebEvent> events;
    WebEvent event;
    while (events.size() < 256 && backend.poll(event))
        events.push_back(std::move(event));
    return events;
}
std::string webThemeJson() {
    const auto &p = palette();
    std::ostringstream stream;
    stream.imbue(std::locale::classic());
    stream << "{\"protocol\":\"cyui.web\",\"version\":1,\"type\":\"theme\",\"payload\":{\"scale\":" << dp(1)
           << ",\"locale\":" << quote(locale()) << ",\"colors\":{";
    bool first = true;
    auto color = [&](const char *name, ImU32 value) {
        if (!first)
            stream << ',';
        first = false;
        const auto v = vec(value);
        stream << quote(name) << ":\"rgba(" << int(v.x * 255 + .5f) << ',' << int(v.y * 255 + .5f) << ','
               << int(v.z * 255 + .5f) << ',' << v.w << ")\"";
    };
    color("bg0", p.bg0);
    color("bg1", p.bg1);
    color("bg2", p.bg2);
    color("bg3", p.bg3);
    color("text", p.text);
    color("textDim", p.textDim);
    color("textFaint", p.textFaint);
    color("accent", p.accent);
    color("selection", p.selection);
    color("line", p.line);
    color("success", p.success);
    color("warning", p.warning);
    color("error", p.error);
    color("info", p.info);
    stream << "}}}";
    return stream.str();
}
std::string webEventJson(const WebEvent &e) {
    const char *kind = e.kind == WebEventKind::ready       ? "ready"
                       : e.kind == WebEventKind::navigated ? "navigated"
                       : e.kind == WebEventKind::message   ? "message"
                       : e.kind == WebEventKind::external  ? "external"
                       : e.kind == WebEventKind::dropped   ? "dropped"
                                                           : "error";
    return "{\"kind\":" + quote(kind) + ",\"url\":" + quote(e.url) + ",\"data\":" + quote(e.data) +
           ",\"userGesture\":" + (e.userGesture ? "true" : "false") +
           ",\"mainFrame\":" + (e.mainFrame ? "true" : "false") +
           ",\"sourceOrigin\":" + quote(e.sourceOrigin) + "}";
}
} // namespace cyui
