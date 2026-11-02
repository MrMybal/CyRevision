#include "cyui/modules/AudioViews.h"
#include "ModuleInternal.h"
#include <limits>

namespace cyui {
std::vector<Peak> makePeaks(ArrayView<float> data, std::size_t columns) {
    if (!columns || !data.count)
        return {};
    columns = std::min(columns, data.count);
    std::vector<Peak> out(columns);
    for (std::size_t x = 0; x < columns; ++x) {
        const auto a = x * data.count / columns, b = (x + 1) * data.count / columns;
        bool have = false;
        for (auto i = a; i < b; ++i)
            if (std::isfinite(data[i])) {
                if (!have) {
                    out[x] = {data[i], data[i]};
                    have = true;
                } else {
                    out[x].minimum = std::min(out[x].minimum, data[i]);
                    out[x].maximum = std::max(out[x].maximum, data[i]);
                }
            }
        if (!have)
            out[x] = {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::quiet_NaN()};
    }
    return out;
}
WaveformResult waveform(const char *id, WaveformState &s, const WaveformData &data, double playhead,
                        ImVec2 size) {
    CanvasState canvas;
    CanvasOptions o;
    o.grid = o.navigation = false;
    o.claimWheel = true;
    CanvasScope scope(id, canvas, size, o);
    const auto f = scope.frame();
    const auto room = f.bounds.size();
    auto *d = ImGui::GetWindowDrawList();
    auto &io = ImGui::GetIO();
    WaveformResult result;
    if (!s.view.valid())
        s.view = data.coverage.valid() ? data.coverage : Range{0, 1};
    const double mouse = s.view.min + (io.MousePos.x - f.bounds.min.x) / room.x * s.view.span();
    auto x = [&](double t) { return f.bounds.min.x + float((t - s.view.min) / s.view.span()) * room.x; };
    if (f.wheel != 0) {
        if (io.KeyShift)
            s.view = detail::panView(s.view, -f.wheel * s.view.span() * .1);
        else
            s.view = detail::zoomView(s.view, mouse, std::exp(-f.wheel * .15));
    }
    if (f.hovered && ImGui::IsMouseClicked(2))
        s.panning = true;
    if (!ImGui::IsMouseDown(2))
        s.panning = false;
    if (s.panning)
        s.view = detail::panView(s.view, -io.MouseDelta.x / room.x * s.view.span());
    if (f.hovered && ImGui::IsMouseClicked(0)) {
        s.selection = {mouse, mouse};
        s.anchor = mouse;
        s.selecting = true;
        result.seek = true;
        result.time = mouse;
    }
    if (s.selecting) {
        s.selection = {std::min(s.anchor, mouse), std::max(s.anchor, mouse)};
        result.selectionChanged = true;
        if (!ImGui::IsMouseDown(0))
            s.selecting = false;
    }
    if (f.hovered && ImGui::IsMouseDoubleClicked(0)) {
        s.selection = {0, 0};
        s.selecting = false;
        result.selectionChanged = true;
    }
    if (data.coverage.valid())
        for (std::size_t channel = 0; channel < data.channels.size(); ++channel) {
            const auto peaks = data.channels[channel];
            if (!peaks.count)
                continue;
            const float top = f.bounds.min.y + room.y * float(channel) / data.channels.size(),
                        height = room.y / data.channels.size(), mid = top + height * .5f;
            d->AddLine({f.bounds.min.x, mid}, {f.bounds.max.x, mid}, palette().grid, dp(1));
            const int columns = std::clamp(int(room.x), 1, 8192);
            for (int pixel = 0; pixel < columns; ++pixel) {
                const double t = s.view.min + (double(pixel) + .5) / columns * s.view.span();
                if (!data.coverage.contains(t))
                    continue;
                const auto index = std::min(peaks.count - 1, std::size_t((t - data.coverage.min) /
                                                                         data.coverage.span() * peaks.count));
                const auto p = peaks[index];
                if (std::isfinite(p.minimum) && std::isfinite(p.maximum))
                    d->AddLine({f.bounds.min.x + room.x * pixel / columns,
                                mid - std::clamp(p.maximum, -1.f, 1.f) * height * .45f},
                               {f.bounds.min.x + room.x * pixel / columns,
                                mid - std::clamp(p.minimum, -1.f, 1.f) * height * .45f},
                               palette().info, dp(1));
            }
        }
    if (s.selection.min != s.selection.max)
        d->AddRectFilled({x(std::min(s.selection.min, s.selection.max)), f.bounds.min.y},
                         {x(std::max(s.selection.min, s.selection.max)), f.bounds.max.y},
                         palette().selection);
    d->AddLine({x(playhead), f.bounds.min.y}, {x(playhead), f.bounds.max.y}, palette().accent, dp(2));
    return result;
}
PlotResult spectrum(const char *id, PlotState &s, ArrayView<PlotSeries> data, ImVec2 size,
                    const PlotOptions &o) {
    s.x.logarithmic = true;
    if (!s.x.range.valid() || s.x.range.min <= 0)
        s.x.range = {20, 20000};
    return plot(id, s, data, size, o);
}
PlotResult spectrum(const char *id, PlotState &s, ArrayView<PlotSeries> data, ImVec2 size) {
    return spectrum(id, s, data, size, PlotOptions{});
}
PlotResult oscilloscope(const char *id, PlotState &s, ArrayView<PlotSeries> data, ImVec2 size,
                        const PlotOptions &o) {
    s.x.logarithmic = s.y.logarithmic = false;
    return plot(id, s, data, size, o);
}
PlotResult oscilloscope(const char *id, PlotState &s, ArrayView<PlotSeries> data, ImVec2 size) {
    return oscilloscope(id, s, data, size, PlotOptions{});
}
PlotResult stereoScope(const char *id, PlotState &s, ArrayView<PlotSeries> data, ImVec2 size,
                       const PlotOptions &o) {
    // Square, with one symmetric extent on both axes: wheel zoom scales it, the centre stays at 0.
    s.x.logarithmic = s.y.logarithmic = false;
    const auto avail = ImGui::GetContentRegionAvail();
    const float side = std::max(dp(1), std::min(size.x > 0 ? size.x : avail.x + size.x,
                                                 size.y > 0 ? size.y : avail.y + size.y));
    const double extent = s.x.range.valid() ? s.x.range.span() * .5 : 1;
    s.x.range = s.y.range = {-extent, extent};
    auto result = plot(id, s, data, {side, side}, o);
    const double zoomed = s.x.range.valid() ? s.x.range.span() * .5 : extent;
    s.x.range = s.y.range = {-zoomed, zoomed};
    return result;
}
PlotResult stereoScope(const char *id, PlotState &s, ArrayView<PlotSeries> data, ImVec2 size) {
    return stereoScope(id, s, data, size, PlotOptions{});
}
} // namespace cyui
