#include "cyui/modules/Timeline.h"
#include "ModuleInternal.h"
#include <cstdio>

namespace cyui {
TimelineResult timeline(const char *id, TimelineState &s, const std::vector<TimelineLane> &lanes,
                        const std::vector<TimelineClip> &clips, ArrayView<TimelineMarker> markers,
                        ImVec2 size, const TimelineOptions &o) {
    TimelineResult result;
    CanvasState canvas;
    CanvasOptions options;
    options.grid = options.navigation = false;
    options.claimWheel = true;
    CanvasScope scope(id, canvas, size, options);
    const auto f = scope.frame();
    const auto r = f.bounds;
    auto *d = ImGui::GetWindowDrawList();
    auto &io = ImGui::GetIO();
    if (!s.view.valid())
        s.view = {0, 10};
    const float head = std::min(dp(150), r.size().x * .3f), ruler = ImGui::GetFrameHeight();
    const float left = r.min.x + head, width = std::max(dp(1), r.max.x - left);
    auto x = [&](double t) { return left + float((t - s.view.min) / s.view.span()) * width; };
    const double mouse = s.view.min + (io.MousePos.x - left) / width * s.view.span();
    auto snap = [&](double t) { return io.KeyAlt ? t : o.snapTime ? o.snapTime(t) : snapValue(t, o.snap); };
    auto clear = [&] {
        s.mode = 0;
        s.active = 0;
        s.before.clear();
        s.preview.clear();
    };
    if (s.mode > 0 && (s.revision != o.revision || o.readOnly || ImGui::IsKeyPressed(ImGuiKey_Escape))) {
        clear();
        result.clips.phase = EditPhase::cancel;
    }
    if (f.wheel != 0) {
        if (io.KeyCtrl)
            s.verticalScroll = std::max(0., s.verticalScroll - f.wheel * dp(40));
        else if (io.KeyShift)
            s.view = detail::panView(s.view, -f.wheel * s.view.span() * .1);
        else
            s.view = detail::zoomView(s.view, mouse, std::exp(-f.wheel * .15));
    }
    if (f.hovered && ImGui::IsMouseClicked(2))
        s.panning = true;
    if (!ImGui::IsMouseDown(2))
        s.panning = false;
    if (s.panning)
        s.view = detail::panView(s.view, -io.MouseDelta.x / width * s.view.span());
    struct Row {
        ItemId id;
        float top, bottom;
    };
    std::vector<Row> rows;
    float y = r.min.y + ruler - float(s.verticalScroll);
    for (const auto &lane : lanes) {
        const float height = dp(std::max(24.f, lane.height));
        rows.push_back({lane.id, y, y + height});
        y += height;
    }
    const double full = y - r.min.y - ruler + s.verticalScroll;
    s.verticalScroll = std::clamp(s.verticalScroll, 0., std::max(0., full - (r.size().y - ruler)));
    for (const auto &row : rows)
        if (row.bottom > r.min.y + ruler && row.top < r.max.y) {
            d->AddRectFilled({r.min.x, std::max(row.top, r.min.y + ruler)}, {left, row.bottom},
                             palette().bg2);
            d->AddLine({r.min.x, row.bottom}, {r.max.x, row.bottom}, palette().line, dp(1));
        }
    for (std::size_t i = 0; i < lanes.size(); ++i)
        if (rows[i].bottom > r.min.y + ruler && rows[i].top < r.max.y)
            clippedText(d, {r.min.x + dp(8), std::max(rows[i].top, r.min.y + ruler) + dp(5)}, head - dp(16),
                        palette().textDim, lanes[i].label.c_str());
    const double desired = s.view.span() * dp(90) / width,
                 power = std::pow(10., std::floor(std::log10(std::max(1e-12, desired))));
    const double step = (desired / power < 2 ? 1 : desired / power < 5 ? 2 : 5) * power;
    detail::forEachTick(s.view.min, s.view.max, step, [&](double t) {
        const float px = x(t);
        d->AddLine({px, r.min.y}, {px, r.max.y}, palette().grid, dp(1));
        char text[48];
        std::snprintf(text, sizeof(text), "%.5g", t);
        const auto label = o.formatTime ? o.formatTime(t) : std::string(text);
        d->AddText({px + dp(3), r.min.y + dp(3)}, palette().textDim, label.c_str());
    });
    const auto &shown = s.mode > 0 ? s.preview : clips;
    const TimelineClip *hit = nullptr;
    int edge = 1;
    const Row *hoveredLane = nullptr;
    for (const auto &row : rows)
        if (io.MousePos.y >= row.top && io.MousePos.y < row.bottom)
            hoveredLane = &row;
    d->PushClipRect({left, r.min.y + ruler}, r.max, true);
    for (const auto &c : shown) {
        const auto it = std::find_if(rows.begin(), rows.end(), [&](const Row &a) { return a.id == c.lane; });
        if (it == rows.end() || c.start + c.duration < s.view.min || c.start > s.view.max ||
            it->bottom < r.min.y + ruler || it->top > r.max.y)
            continue;
        Rect rect{{x(c.start), it->top + dp(3)},
                  {std::max(x(c.start) + dp(2), x(c.start + c.duration)), it->bottom - dp(3)}};
        d->AddRectFilled(rect.min, rect.max, mix(palette().bg2, categorical(c.colorIndex), .45f), dp(3));
        d->AddRect(rect.min, rect.max, selected(s.selected, c.id) ? palette().accent : palette().line, dp(3),
                   0, dp(selected(s.selected, c.id) ? 2.f : 1.f));
        clippedText(d, {std::max(left, rect.min.x) + dp(5), rect.min.y + dp(3)},
                    std::min(r.max.x, rect.max.x) - std::max(left, rect.min.x) - dp(10), palette().text,
                    c.label.c_str());
        if (o.drawClip)
            o.drawClip(c, rect);
        if (rect.contains(io.MousePos)) {
            hit = &c;
            edge = std::abs(io.MousePos.x - rect.min.x) < dp(7)   ? 2
                   : std::abs(io.MousePos.x - rect.max.x) < dp(7) ? 3
                                                                  : 1;
        }
    }
    if (s.selection.max != s.selection.min) {
        const float a = x(std::min(s.selection.min, s.selection.max)),
                    b = x(std::max(s.selection.min, s.selection.max));
        d->AddRectFilled({a, r.min.y + ruler}, {b, r.max.y}, palette().selection);
    }
    d->PopClipRect();
    for (std::size_t i = 0; i < markers.count; ++i)
        if (s.view.contains(markers[i].time)) {
            const float px = x(markers[i].time);
            d->AddLine({px, r.min.y}, {px, r.max.y}, palette().warning, dp(1));
            d->AddText({px + dp(3), r.min.y + ruler}, palette().warning, markers[i].label.c_str());
        }
    d->AddLine({x(o.playhead), r.min.y}, {x(o.playhead), r.max.y}, palette().accent, dp(2));
    if (f.hovered && ImGui::IsMouseClicked(0)) {
        s.focused = true;
        if (io.MousePos.x >= left && io.MousePos.y < r.min.y + ruler) {
            result.seek = true;
            result.time = snap(mouse);
        } else if (hit) {
            const auto key = hit->id;
            if (!selected(s.selected, key) || io.KeyCtrl)
                selectItem(s.selected, key, io.KeyCtrl || io.KeyShift);
            const auto it =
                std::find_if(clips.begin(), clips.end(), [&](const TimelineClip &c) { return c.id == key; });
            if (!o.readOnly && it != clips.end() && !it->locked) {
                s.active = key;
                s.mode = edge;
                s.grab = mouse;
                s.before = s.preview = clips;
                s.revision = o.revision;
                result.clips = {EditPhase::begin, s.preview};
            }
        } else if (io.MousePos.x >= left) {
            s.selection = {mouse, mouse};
            s.anchor = mouse;
            s.mode = -1;
            result.seek = true;
            result.time = mouse;
            if (!io.KeyCtrl)
                s.selected.clear();
        }
    } else if (ImGui::IsMouseClicked(0) && !f.hovered)
        s.focused = false;
    if (s.mode > 0) {
        if (ImGui::IsMouseDown(0)) {
            s.preview = s.before;
            // The grabbed clip snaps; the others follow by the same delta and keep their spacing.
            double delta = mouse - s.grab;
            for (const auto &c : s.before)
                if (c.id == s.active)
                    delta = s.mode == 3 ? snap(c.start + c.duration + delta) - (c.start + c.duration)
                                        : snap(c.start + delta) - c.start;
            for (auto &c : s.preview)
                if (!c.locked && ((s.mode == 1 && selected(s.selected, c.id)) || c.id == s.active)) {
                    if (s.mode == 1) {
                        c.start += delta;
                        if (c.id == s.active && hoveredLane)
                            c.lane = hoveredLane->id;
                    } else if (s.mode == 2) {
                        const double end = c.start + c.duration;
                        c.start = std::min(end - o.minDuration, c.start + delta);
                        c.duration = end - c.start;
                    } else
                        c.duration = std::max(o.minDuration, c.duration + delta);
                }
            if (result.clips.phase != EditPhase::begin && (io.MouseDelta.x || io.MouseDelta.y))
                result.clips = {EditPhase::update, s.preview};
        } else {
            result.clips = {EditPhase::commit, s.preview};
            clear();
        }
    }
    if (s.mode == -1) {
        s.selection = {std::min(s.anchor, mouse), std::max(s.anchor, mouse)};
        result.rangeChanged = true;
        if (!ImGui::IsMouseDown(0))
            s.mode = 0;
    }
    if (s.focused && f.focused && !io.WantTextInput && !o.readOnly)
        ImGui::SetNextFrameWantCaptureKeyboard(true); // Hosts (Unreal) route Delete here.
    if (s.focused && f.focused && !io.WantTextInput && !o.readOnly && ImGui::IsKeyPressed(ImGuiKey_Delete))
        for (const auto &c : clips)
            if (!c.locked && selected(s.selected, c.id))
                result.remove.push_back(c.id);
    return result;
}
} // namespace cyui
