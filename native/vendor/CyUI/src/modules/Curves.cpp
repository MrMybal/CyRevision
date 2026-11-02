#include "cyui/modules/Curves.h"
#include "ModuleInternal.h"
#include <limits>
#include <unordered_set>

namespace cyui {
namespace {
double bezier(double a, double b, double c, double d, double t) {
    const double u = 1 - t;
    return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
}
ItemId fresh(const std::vector<CurvePoint> &p) {
    std::unordered_set<ItemId> used;
    for (const auto &x : p)
        used.insert(x.id);
    ItemId id = 1;
    while (used.count(id))
        ++id;
    return id;
}
} // namespace
double evaluateCurve(ArrayView<CurvePoint> p, double t, double fallback) {
    if (!p.count || !std::isfinite(t))
        return fallback;
    // Input order is time order, matching the host's document convention.
    if (t < p[0].time)
        return p[0].value;
    std::size_t low = 0, high = p.count;
    while (low < high) {
        const auto mid = low + (high - low) / 2;
        if (p[mid].time <= t)
            low = mid + 1;
        else
            high = mid;
    }
    const std::size_t i = low ? low - 1 : 0;
    if (i + 1 == p.count)
        return p[i].value;
    const auto &a = p[i];
    const auto &b = p[i + 1];
    const double span = b.time - a.time;
    if (span <= 0)
        return b.value;
    double k = std::clamp((t - a.time) / span, 0., 1.);
    if (a.shape == CurveShape::step)
        return a.value;
    if (a.shape == CurveShape::smooth)
        k = k * k * (3 - 2 * k);
    if (a.shape == CurveShape::bezier) {
        double lo = 0, hi = 1;
        const double x1 = a.time + std::clamp(a.out.x, 0., span), x2 = b.time + std::clamp(b.in.x, -span, 0.);
        for (int n = 0; n < 40; ++n) {
            k = (lo + hi) * .5;
            if (bezier(a.time, x1, x2, b.time, k) < t)
                lo = k;
            else
                hi = k;
        }
        return bezier(a.value, a.value + a.out.y, b.value + b.in.y, b.value, k);
    }
    return a.value + (b.value - a.value) * k;
}
Edit<std::vector<CurvePoint>> curveEditor(const char *id, CurveState &s,
                                          const std::vector<CurvePoint> &points, ImVec2 size,
                                          const CurveOptions &o) {
    Edit<std::vector<CurvePoint>> result;
    CanvasState canvas;
    CanvasOptions options;
    options.navigation = false;
    options.claimWheel = true;
    options.grid = false;
    CanvasScope scope(id, canvas, size, options);
    const auto f = scope.frame();
    const auto room = f.bounds.size();
    auto &io = ImGui::GetIO();
    auto *d = ImGui::GetWindowDrawList();
    const Point mouse{s.view.x.value((io.MousePos.x - f.bounds.min.x) / room.x),
                      s.view.y.value(1 - (io.MousePos.y - f.bounds.min.y) / room.y)};
    auto screen = [&](Point p) {
        return ImVec2(f.bounds.min.x + float(s.view.x.fraction(p.x)) * room.x,
                      f.bounds.max.y - float(s.view.y.fraction(p.y)) * room.y);
    };
    auto clear = [&] {
        s.dragging = s.boxing = false;
        s.active = 0;
        s.before.clear();
        s.preview.clear();
    };
    if (s.dragging && (o.revision != s.revision || o.readOnly || ImGui::IsKeyPressed(ImGuiKey_Escape))) {
        clear();
        result.phase = EditPhase::cancel;
    }
    if (f.wheel != 0)
        detail::navigateAxis(s.view.x, 0, mouse.x, std::exp(-f.wheel * .15));
    if (f.hovered && ImGui::IsMouseDragging(2))
        detail::navigateAxis(s.view.x, -io.MouseDelta.x / room.x, mouse.x, 1);
    if (f.hovered && ImGui::IsMouseClicked(0))
        s.focused = true;
    else if (ImGui::IsMouseClicked(0))
        s.focused = false;
    const auto &shown = s.dragging ? s.preview : points;
    const CurvePoint *hit = nullptr;
    int handle = 0;
    float distance = dp(8);
    for (const auto &p : shown) {
        const auto q = screen({p.time, p.value});
        const float dist = std::hypot(q.x - io.MousePos.x, q.y - io.MousePos.y);
        if (dist < distance) {
            distance = dist;
            hit = &p;
            handle = 0;
        }
        if (o.handles && selected(s.selection, p.id) && p.shape == CurveShape::bezier)
            for (int side : {-1, 1}) {
                const auto h = side < 0 ? p.in : p.out;
                const auto a = screen({p.time + h.x, p.value + h.y});
                d->AddLine(q, a, palette().textFaint, dp(1));
                d->AddCircleFilled(a, dp(3), palette().info);
                const float hd = std::hypot(a.x - io.MousePos.x, a.y - io.MousePos.y);
                if (hd < distance) {
                    distance = hd;
                    hit = &p;
                    handle = side;
                }
            }
    }
    if (!o.readOnly && f.hovered && ImGui::IsMouseDoubleClicked(0)) {
        result.value = points;
        if (hit) {
            const auto key = hit->id;
            result.value.erase(std::remove_if(result.value.begin(), result.value.end(),
                                              [&](const CurvePoint &p) { return p.id == key; }),
                               result.value.end());
        } else {
            const double t = o.snapTime ? o.snapTime(mouse.x) : snapValue(mouse.x, o.snap);
            result.value.push_back({fresh(points), t, mouse.y});
        }
        std::stable_sort(result.value.begin(), result.value.end(),
                         [](const CurvePoint &a, const CurvePoint &b) { return a.time < b.time; });
        clear();
        result.phase = EditPhase::commit;
    } else if (f.hovered && ImGui::IsMouseClicked(0)) {
        if (hit) {
            const auto key = hit->id;
            if (!selected(s.selection, key) || io.KeyCtrl)
                selectItem(s.selection, key, io.KeyCtrl || io.KeyShift);
            if (!o.readOnly && selected(s.selection, key)) {
                s.active = key;
                s.handle = handle;
                s.grab = mouse;
                s.before = s.preview = points;
                s.dragging = true;
                s.revision = o.revision;
                result = {EditPhase::begin, s.preview};
            }
        } else {
            if (!io.KeyCtrl && !io.KeyShift)
                s.selection.clear();
            s.boxStart = mouse;
            s.boxing = true;
        }
    }
    if (s.dragging) {
        if (ImGui::IsMouseDown(0)) {
            s.preview = s.before;
            double delta = mouse.x - s.grab.x;
            const double dy = mouse.y - s.grab.y;
            // The grabbed key snaps; the other selected keys follow by the same delta.
            if (!s.handle && !io.KeyAlt)
                for (const auto &p : s.before)
                    if (p.id == s.active)
                        delta = (o.snapTime ? o.snapTime(p.time + delta) : snapValue(p.time + delta, o.snap)) -
                                p.time;
            for (auto &p : s.preview)
                if (selected(s.selection, p.id)) {
                    if (s.handle && p.id == s.active) {
                        auto &h = s.handle < 0 ? p.in : p.out;
                        h.x += delta;
                        h.y += dy;
                        h.x = s.handle < 0 ? std::min(0., h.x) : std::max(0., h.x);
                    } else if (!s.handle) {
                        p.time += delta;
                        p.value += dy;
                    }
                }
            std::stable_sort(s.preview.begin(), s.preview.end(),
                             [](const CurvePoint &a, const CurvePoint &b) { return a.time < b.time; });
            if (result.phase != EditPhase::begin && (io.MouseDelta.x != 0 || io.MouseDelta.y != 0))
                result = {EditPhase::update, s.preview};
        } else {
            result = {EditPhase::commit, s.preview};
            clear();
        }
    }
    if (s.boxing) {
        const auto a = screen(s.boxStart), b = screen(mouse);
        const Rect box{{std::min(a.x, b.x), std::min(a.y, b.y)}, {std::max(a.x, b.x), std::max(a.y, b.y)}};
        d->AddRectFilled(box.min, box.max, palette().selection);
        d->AddRect(box.min, box.max, palette().accent, 0, 0, dp(1));
        if (!ImGui::IsMouseDown(0)) {
            for (const auto &p : points)
                if (box.contains(screen({p.time, p.value})) && !selected(s.selection, p.id))
                    s.selection.push_back(p.id);
            s.boxing = false;
        }
    }
    if (s.focused && f.focused && !io.WantTextInput) {
        ImGui::SetNextFrameWantCaptureKeyboard(true); // Hosts (Unreal) route the shortcuts here.
        if (io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_A)) {
            s.selection.clear();
            for (const auto &p : points)
                s.selection.push_back(p.id);
        }
        if (io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_C)) {
            s.clipboard.clear();
            for (const auto &p : points)
                if (selected(s.selection, p.id))
                    s.clipboard.push_back(p);
        }
        if (!o.readOnly && io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_V) && !s.clipboard.empty()) {
            result.value = points;
            s.selection.clear();
            const double origin = s.clipboard.front().time;
            for (auto p : s.clipboard) {
                p.id = fresh(result.value);
                p.time = p.time - origin + mouse.x;
                result.value.push_back(p);
                s.selection.push_back(p.id);
            }
            std::stable_sort(result.value.begin(), result.value.end(),
                             [](const CurvePoint &a, const CurvePoint &b) { return a.time < b.time; });
            result.phase = EditPhase::commit;
        }
        if (!o.readOnly && ImGui::IsKeyPressed(ImGuiKey_Delete) && !s.selection.empty()) {
            result.value = points;
            result.value.erase(
                std::remove_if(result.value.begin(), result.value.end(),
                               [&](const CurvePoint &p) { return selected(s.selection, p.id); }),
                result.value.end());
            result.phase = EditPhase::commit;
            clear();
        }
    }
    const auto &drawPoints = s.dragging ? s.preview : (result.committed() ? result.value : points);
    for (int i = 1; i < 4; ++i) {
        const float x = f.bounds.min.x + room.x * i / 4, y = f.bounds.min.y + room.y * i / 4;
        d->AddLine({x, f.bounds.min.y}, {x, f.bounds.max.y}, palette().grid, dp(1));
        d->AddLine({f.bounds.min.x, y}, {f.bounds.max.x, y}, palette().grid, dp(1));
    }
    ImVec2 last{};
    bool have = false;
    const int samples = std::clamp(int(room.x), 2, 4096);
    for (int i = 0; i <= samples; ++i) {
        const double t = s.view.x.value(double(i) / samples);
        const double v = o.evaluate ? o.evaluate(drawPoints, t) : evaluateCurve(drawPoints, t);
        const auto q = screen({t, v});
        if (std::isfinite(q.x) && std::isfinite(q.y)) {
            if (have)
                d->AddLine(last, q, palette().accent, dp(1.5f));
            last = q;
            have = true;
        } else
            have = false;
    }
    for (const auto &p : drawPoints) {
        const auto q = screen({p.time, p.value});
        if (std::isfinite(q.x) && std::isfinite(q.y))
            d->AddCircleFilled(q, dp(selected(s.selection, p.id) ? 4.5f : 3.5f),
                               selected(s.selection, p.id) ? palette().text : palette().accent);
    }
    return result;
}
} // namespace cyui
