#include "cyui/modules/Viewport.h"
#include "ModuleInternal.h"

namespace cyui {
namespace {
Vec3 add(Vec3 a, Vec3 b) {
    return {a.x + b.x, a.y + b.y, a.z + b.z};
}
Vec3 mul(Vec3 a, double s) {
    return {a.x * s, a.y * s, a.z * s};
}
double dot(Vec3 a, Vec3 b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
Vec3 cross(Vec3 a, Vec3 b) {
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}
Vec3 unit(Vec3 a) {
    return mul(a, 1 / std::max(1e-12, std::sqrt(dot(a, a))));
}
void basis(const OrbitCamera &c, Vec3 &right, Vec3 &forward, Vec3 &up) {
    up = dot(c.up, c.up) > 1e-10 ? unit(c.up) : Vec3{0, 1, 0};
    const Vec3 ref = std::abs(up.x) < .9 ? Vec3{1, 0, 0} : Vec3{0, 0, 1};
    forward = unit(cross(ref, up));
    right = unit(cross(up, forward));
}
} // namespace
Vec3 cameraPosition(const OrbitCamera &c) {
    Vec3 r, f, u;
    basis(c, r, f, u);
    const double pitch = std::clamp(c.pitch, -1.56, 1.56), distance = std::max(1e-6, c.distance);
    return add(c.target, mul(add(add(mul(r, std::sin(c.yaw) * std::cos(pitch)),
                                     mul(f, std::cos(c.yaw) * std::cos(pitch))),
                                 mul(u, std::sin(pitch))),
                             distance));
}
std::array<float, 16> cameraViewMatrix(const OrbitCamera &c) {
    const auto eye = cameraPosition(c);
    const auto z = unit(add(eye, mul(c.target, -1))),
               x = unit(cross(dot(c.up, c.up) > 1e-10 ? c.up : Vec3{0, 1, 0}, z)), y = cross(z, x);
    return {float(x.x),          float(y.x),          float(z.x),          0,
            float(x.y),          float(y.y),          float(z.y),          0,
            float(x.z),          float(y.z),          float(z.z),          0,
            float(-dot(x, eye)), float(-dot(y, eye)), float(-dot(z, eye)), 1};
}
ViewportResult viewport(const char *id, ViewportState &s, const ImageData &image, ImVec2 size,
                        bool navigation) {
    CanvasState canvas;
    CanvasOptions o;
    o.grid = o.navigation = false;
    o.claimWheel = navigation;
    CanvasScope scope(id, canvas, size, o);
    const auto f = scope.frame();
    auto &io = ImGui::GetIO();
    auto *d = ImGui::GetWindowDrawList();
    ViewportResult result;
    Rect display = f.bounds;
    if (image.width > 0 && image.height > 0) {
        const auto room = f.bounds.size();
        const float factor = std::min(room.x / image.width, room.y / image.height);
        const ImVec2 extent{image.width * factor, image.height * factor};
        display.min = {f.bounds.min.x + (room.x - extent.x) * .5f,
                       f.bounds.min.y + (room.y - extent.y) * .5f};
        display.max = {display.min.x + extent.x, display.min.y + extent.y};
    }
    if (image.texture)
        d->AddImage(image.texture, display.min, display.max, image.uvMin, image.uvMax);
    if (f.hovered && display.contains(io.MousePos)) {
        const auto room = display.size();
        result.uv = {(io.MousePos.x - display.min.x) / room.x, (io.MousePos.y - display.min.y) / room.y};
        result.clicked = ImGui::IsMouseClicked(0) && !io.KeyAlt;
    }
    if (navigation) {
        if (f.hovered && io.KeyAlt && ImGui::IsMouseClicked(0))
            s.orbiting = true;
        if (f.hovered && ImGui::IsMouseClicked(2))
            s.panning = true;
        if (!ImGui::IsMouseDown(0))
            s.orbiting = false;
        if (!ImGui::IsMouseDown(2))
            s.panning = false;
        if (s.orbiting && (io.MouseDelta.x || io.MouseDelta.y)) {
            s.camera.yaw -= io.MouseDelta.x * .007;
            s.camera.pitch = std::clamp(s.camera.pitch + io.MouseDelta.y * .007, -1.56, 1.56);
            result.cameraChanged = true;
        }
        if (s.panning && (io.MouseDelta.x || io.MouseDelta.y)) {
            const auto eye = cameraPosition(s.camera), z = unit(add(eye, mul(s.camera.target, -1))),
                       x = unit(
                           cross(dot(s.camera.up, s.camera.up) > 1e-10 ? s.camera.up : Vec3{0, 1, 0}, z)),
                       y = cross(z, x);
            const double factor = s.camera.distance / std::max(dp(1), f.bounds.size().y);
            s.camera.target = add(s.camera.target,
                                  add(mul(x, -io.MouseDelta.x * factor), mul(y, io.MouseDelta.y * factor)));
            result.cameraChanged = true;
        }
        if (f.wheel != 0) {
            s.camera.distance = std::clamp(s.camera.distance * std::exp(-f.wheel * .15), 1e-5, 1e12);
            result.cameraChanged = true;
        }
    }
    return result;
}
Edit<Transform2D> transformGizmo(const char *id, TransformState &s, const Transform2D &value,
                                 const TransformOptions &o) {
    ScopedID scope(id);
    auto &io = ImGui::GetIO();
    auto *d = ImGui::GetWindowDrawList();
    Edit<Transform2D> result;
    auto project = [&](Point p) { return o.project ? o.project(p) : ImVec2(float(p.x), float(p.y)); };
    auto unproject = [&](ImVec2 p) { return o.unproject ? o.unproject(p) : Point{p.x, p.y}; };
    const ImGuiID item = ImGui::GetID("##handles");
    auto release = [&] {
        s.handle = 0;
        if (ImGui::GetActiveID() == item)
            ImGui::ClearActiveID();
    };
    if (s.handle && (s.revision != o.revision || !o.enabled || ImGui::IsKeyPressed(ImGuiKey_Escape))) {
        release();
        result.phase = EditPhase::cancel;
    }
    const auto shown = s.handle ? s.preview : value;
    const double c = std::cos(shown.rotation), sn = std::sin(shown.rotation);
    auto corner = [&](double x, double y) {
        return project({shown.center.x + x * c - y * sn, shown.center.y + x * sn + y * c});
    };
    const ImVec2 points[] = {
        corner(-shown.size.x * .5, -shown.size.y * .5), corner(shown.size.x * .5, -shown.size.y * .5),
        corner(shown.size.x * .5, shown.size.y * .5), corner(-shown.size.x * .5, shown.size.y * .5)};
    for (int i = 0; i < 4; ++i) {
        d->AddLine(points[i], points[(i + 1) % 4], palette().accent, dp(1.5f));
        d->AddRectFilled({points[i].x - dp(3), points[i].y - dp(3)},
                         {points[i].x + dp(3), points[i].y + dp(3)}, palette().text);
    }
    // The rotation handle sits dp(24) beyond the top edge in screen space, whatever the world scale.
    const auto center = project(shown.center), top = corner(0, -shown.size.y * .5);
    const float ux = top.x - center.x, uy = top.y - center.y, length = std::hypot(ux, uy);
    const ImVec2 rot = length > 1e-3f ? ImVec2(top.x + ux / length * dp(24), top.y + uy / length * dp(24))
                                      : ImVec2(top.x, top.y - dp(24));
    d->AddCircle(center, dp(7), palette().accent, 0, dp(2));
    d->AddCircleFilled(rot, dp(4), palette().info);
    int hit = 0;
    if (std::hypot(center.x - io.MousePos.x, center.y - io.MousePos.y) < dp(10))
        hit = 1;
    for (int i = 0; i < 4; ++i)
        if (std::hypot(points[i].x - io.MousePos.x, points[i].y - io.MousePos.y) < dp(8))
            hit = i + 2;
    if (std::hypot(rot.x - io.MousePos.x, rot.y - io.MousePos.y) < dp(8))
        hit = 6;
    if (o.enabled || s.handle)
        markCanvasOverlay();
    // A real item over the hovered (or dragged) handle, so the click does not reach the canvas or
    // window underneath.
    bool hovered = false;
    if (const int target = s.handle ? s.handle : o.enabled ? hit : 0) {
        const ImVec2 at = target == 1 ? center : target == 6 ? rot : points[target - 2];
        const ImRect bb({at.x - dp(10), at.y - dp(10)}, {at.x + dp(10), at.y + dp(10)});
        if (ImGui::ItemAdd(bb, item, nullptr, ImGuiItemFlags_NoNav))
            hovered = hit && ImGui::ItemHoverable(bb, item, ImGuiItemFlags_NoNav);
    }
    if (o.enabled && !s.handle && hovered && ImGui::IsMouseClicked(0)) {
        ImGui::SetActiveID(item, ImGui::GetCurrentWindow());
        ImGui::SetKeyOwner(ImGuiKey_MouseLeft, item);
        s.handle = hit;
        s.grab = unproject(io.MousePos);
        s.before = s.preview = value;
        s.revision = o.revision;
        result = {EditPhase::begin, s.preview};
    }
    if (s.handle) {
        if (ImGui::IsMouseDown(0)) {
            const auto mouse = unproject(io.MousePos);
            s.preview = s.before;
            if (s.handle == 1) {
                s.preview.center.x += mouse.x - s.grab.x;
                s.preview.center.y += mouse.y - s.grab.y;
            } else if (s.handle == 6) {
                s.preview.rotation += std::atan2(mouse.y - value.center.y, mouse.x - value.center.x) -
                                      std::atan2(s.grab.y - value.center.y, s.grab.x - value.center.x);
                if (io.KeyCtrl)
                    s.preview.rotation = snapValue(s.preview.rotation, 3.141592653589793 / 12);
            } else {
                const double dx = mouse.x - value.center.x, dy = mouse.y - value.center.y;
                s.preview.size = {
                    std::max(1e-6,
                             std::abs(dx * std::cos(value.rotation) + dy * std::sin(value.rotation)) * 2),
                    std::max(1e-6,
                             std::abs(-dx * std::sin(value.rotation) + dy * std::cos(value.rotation)) * 2)};
            }
            if (result.phase != EditPhase::begin && (io.MouseDelta.x || io.MouseDelta.y))
                result = {EditPhase::update, s.preview};
        } else {
            result = {EditPhase::commit, s.preview};
            release();
        }
    }
    return result;
}
} // namespace cyui
