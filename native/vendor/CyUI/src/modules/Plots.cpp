#include "cyui/modules/Plots.h"
#include "ModuleInternal.h"
#include <limits>
#include <cstdio>

namespace cyui {
double Axis::fraction(double v) const {
    if (!range.valid() || !std::isfinite(v))
        return std::numeric_limits<double>::quiet_NaN();
    if (logarithmic)
        return v > 0 && range.min > 0 ? std::log(v / range.min) / std::log(range.max / range.min)
                                      : std::numeric_limits<double>::quiet_NaN();
    return (v - range.min) / range.span();
}
double Axis::value(double f) const {
    if (!range.valid())
        return range.min;
    return logarithmic && range.min > 0 ? range.min * std::pow(range.max / range.min, f)
                                        : range.min + f * range.span();
}
std::vector<Point> reducePlot(ArrayView<Point> in, std::size_t budget) {
    if (budget < 4 || !in.count)
        return {};
    if (in.count <= budget)
        return {in.data, in.data + in.count};
    std::vector<Point> out;
    const auto buckets = std::max<std::size_t>(1, (budget - 2) / 3);
    out.push_back(in[0]);
    for (std::size_t b = 0; b < buckets; ++b) {
        const auto a = 1 + b * (in.count - 2) / buckets, end = 1 + (b + 1) * (in.count - 2) / buckets;
        std::size_t lo = a, hi = a;
        bool gap = false;
        for (auto i = a; i < end; ++i) {
            if (!std::isfinite(in[i].x) || !std::isfinite(in[i].y)) {
                gap = true;
                continue;
            }
            if (!std::isfinite(in[lo].y) || in[i].y < in[lo].y)
                lo = i;
            if (!std::isfinite(in[hi].y) || in[i].y > in[hi].y)
                hi = i;
        }
        if (gap) {
            out.push_back({in[a].x, std::numeric_limits<double>::quiet_NaN()});
            continue;
        }
        out.push_back(in[std::min(lo, hi)]);
        if (lo != hi)
            out.push_back(in[std::max(lo, hi)]);
    }
    out.push_back(in[in.count - 1]);
    return out;
}
std::vector<std::uint64_t> histogram(ArrayView<double> data, Range r, std::size_t bins) {
    if (!r.valid() || bins == 0 || bins > 1000000)
        return {};
    std::vector<std::uint64_t> result(bins);
    for (std::size_t i = 0; i < data.count; ++i)
        if (std::isfinite(data[i]) && r.contains(data[i])) {
            const auto j = std::min(bins - 1, static_cast<std::size_t>((data[i] - r.min) / r.span() * bins));
            ++result[j];
        }
    return result;
}
PlotResult plot(const char *id, PlotState &s, ArrayView<PlotSeries> series, ImVec2 size,
                const PlotOptions &o) {
    CanvasState canvas;
    CanvasOptions options;
    options.grid = false;
    options.navigation = false;
    options.claimWheel = o.navigation;
    CanvasScope scope(id, canvas, size, options);
    const auto &f = scope.frame();
    const auto r = f.bounds;
    const auto room = r.size();
    auto *d = ImGui::GetWindowDrawList();
    auto &io = ImGui::GetIO();
    PlotResult result;
    result.hovered = f.hovered;
    result.mouse = {s.x.value((io.MousePos.x - r.min.x) / room.x),
                    s.y.value(1 - (io.MousePos.y - r.min.y) / room.y)};
    if (o.navigation) {
        if (f.hovered && ImGui::IsMouseClicked(2))
            s.dragging = true;
        if (!ImGui::IsMouseDown(2))
            s.dragging = false;
        if (s.dragging) {
            const bool x = detail::navigateAxis(s.x, -io.MouseDelta.x / room.x, result.mouse.x, 1),
                       y = detail::navigateAxis(s.y, io.MouseDelta.y / room.y, result.mouse.y, 1);
            result.viewChanged = x || y;
        }
        if (f.wheel != 0) {
            const double factor = std::exp(-f.wheel * .15);
            bool changed = detail::navigateAxis(s.x, 0, result.mouse.x, factor);
            if (io.KeyCtrl)
                changed = detail::navigateAxis(s.y, 0, result.mouse.y, factor) || changed;
            result.viewChanged = result.viewChanged || changed;
        }
    }
    if (o.grid)
        for (int i = 0; i <= 4; ++i) {
            const float x = r.min.x + room.x * i / 4, y = r.min.y + room.y * i / 4;
            d->AddLine({x, r.min.y}, {x, r.max.y}, palette().grid, dp(1));
            d->AddLine({r.min.x, y}, {r.max.x, y}, palette().grid, dp(1));
        }
    float closest = dp(12);
    for (std::size_t n = 0; n < series.count; ++n) {
        const auto &line = series[n];
        const auto color = categorical(line.colorIndex);
        if (line.points.count > o.maxPoints) {
            result.skippedSeries.push_back(line.id);
            continue;
        } // Caller uses reducePlot or a range-query provider.
        bool previous = false;
        ImVec2 p{};
        for (std::size_t i = 0; i < line.points.count; ++i) {
            const double x = s.x.fraction(line.points[i].x), y = s.y.fraction(line.points[i].y);
            if (!std::isfinite(x) || !std::isfinite(y)) {
                previous = false;
                continue;
            }
            const ImVec2 q{r.min.x + float(x) * room.x, r.max.y - float(y) * room.y};
            if (line.style == PlotStyle::points)
                d->AddCircleFilled(q, dp(2.5f), color);
            else if (line.style == PlotStyle::bars) {
                const float w = std::max(dp(1), room.x / std::max<std::size_t>(1, line.points.count) * .8f);
                const float base = s.y.logarithmic ? r.max.y : r.max.y - float(s.y.fraction(0)) * room.y;
                d->AddRectFilled({q.x - w / 2, std::min(base, q.y)}, {q.x + w / 2, std::max(base, q.y)},
                                 color);
            } else if (previous) {
                if (line.style == PlotStyle::step) {
                    d->AddLine(p, {q.x, p.y}, color, dp(1.5f));
                    d->AddLine({q.x, p.y}, q, color, dp(1.5f));
                } else
                    d->AddLine(p, q, color, dp(1.5f));
            }
            const float distance = std::hypot(q.x - io.MousePos.x, q.y - io.MousePos.y);
            if (f.hovered && distance < closest) {
                closest = distance;
                result.series = line.id;
                result.index = i;
            }
            p = q;
            previous = true;
        }
    }
    auto axisLabel = [&](ImVec2 point, const std::string &text) {
        const auto extent = ImGui::CalcTextSize(text.c_str());
        d->AddRectFilled({point.x - dp(2), point.y}, {point.x + extent.x + dp(2), point.y + extent.y},
                         alpha(palette().bg0, .85f));
        d->AddText(point, palette().textDim, text.c_str());
    };
    auto number = [](double value) {
        char text[48];
        std::snprintf(text, sizeof(text), "%.4g", value);
        return std::string(text);
    };
    if (o.axes)
        for (int tick = 0; tick <= 4; ++tick) {
            const double part = tick / 4.;
            const auto xText = o.formatX ? o.formatX(s.x.value(part)) : number(s.x.value(part));
            const auto yText = o.formatY ? o.formatY(s.y.value(part)) : number(s.y.value(part));
            const float textWidth = ImGui::CalcTextSize(xText.c_str()).x, fontHeight = ImGui::GetFontSize();
            axisLabel({std::clamp(r.min.x + room.x * float(part) - textWidth * .5f, r.min.x + dp(3),
                                  std::max(r.min.x + dp(3), r.max.x - textWidth - dp(3))),
                       r.max.y - fontHeight - dp(3)},
                      xText);
            if (tick)
                axisLabel({r.min.x + dp(4),
                           std::clamp(r.max.y - room.y * float(part) - fontHeight * .5f, r.min.y + dp(3),
                                      std::max(r.min.y + dp(3), r.max.y - fontHeight - dp(3)))},
                          yText);
        }
    if (o.legend) {
        float x = r.min.x + dp(70);
        for (std::size_t n = 0; n < series.count; ++n) {
            const auto &line = series[n];
            if (!line.label || !line.label[0])
                continue;
            const float width = ImGui::CalcTextSize(line.label).x + dp(24);
            if (x + width > r.max.x)
                break;
            const float y = r.min.y + dp(5);
            d->AddRectFilled({x, y}, {x + width, y + ImGui::GetFontSize()}, alpha(palette().bg0, .85f));
            d->AddCircleFilled({x + dp(4), y + ImGui::GetFontSize() * .5f}, dp(3),
                               categorical(line.colorIndex));
            d->AddText({x + dp(12), y}, palette().text, line.label);
            x += width;
        }
    }
    if (f.hovered && o.crosshair) {
        d->AddLine({io.MousePos.x, r.min.y}, {io.MousePos.x, r.max.y}, palette().textFaint, dp(1));
        d->AddLine({r.min.x, io.MousePos.y}, {r.max.x, io.MousePos.y}, palette().textFaint, dp(1));
        ImGui::SetTooltip("%.6g, %.6g", result.mouse.x, result.mouse.y);
    }
    d->AddRect(r.min, r.max, palette().line, 0, 0, dp(1));
    return result;
}
HeatmapResult heatmap(const char *id, ArrayView<float> values, int columns, int rows, Range range,
                      ImVec2 size) {
    CanvasState state;
    CanvasOptions o;
    o.grid = o.navigation = false;
    CanvasScope scope(id, state, size, o);
    const auto f = scope.frame();
    const auto room = f.bounds.size();
    auto *d = ImGui::GetWindowDrawList();
    HeatmapResult result;
    if (columns <= 0 || rows <= 0 || std::size_t(columns) * std::size_t(rows) > values.count ||
        !range.valid())
        return result;
    const int maxColumns = std::clamp(int(room.x), 1, 256), maxRows = std::clamp(int(room.y), 1, 256);
    for (int y = 0; y < rows; y += std::max(1, (rows + maxRows - 1) / maxRows))
        for (int x = 0; x < columns; x += std::max(1, (columns + maxColumns - 1) / maxColumns)) {
            const float v = values[std::size_t(y) * columns + x];
            const auto c = std::isfinite(v) ? mix(palette().bg0, palette().accent,
                                                  float(std::clamp((v - range.min) / range.span(), 0., 1.)))
                                            : palette().bg2;
            const int sx = std::max(1, (columns + maxColumns - 1) / maxColumns),
                      sy = std::max(1, (rows + maxRows - 1) / maxRows);
            d->AddRectFilled({f.bounds.min.x + room.x * x / columns, f.bounds.min.y + room.y * y / rows},
                             {f.bounds.min.x + room.x * std::min(columns, x + sx) / columns,
                              f.bounds.min.y + room.y * std::min(rows, y + sy) / rows},
                             c);
        }
    if (f.hovered) {
        const auto m = ImGui::GetIO().MousePos;
        result.column = std::clamp(int((m.x - f.bounds.min.x) / room.x * columns), 0, columns - 1);
        result.row = std::clamp(int((m.y - f.bounds.min.y) / room.y * rows), 0, rows - 1);
        result.clicked = ImGui::IsMouseClicked(0);
        const float value = values[std::size_t(result.row) * columns + result.column];
        if (std::isfinite(value))
            ImGui::SetTooltip("[%d, %d] %.6g", result.column, result.row, double(value));
        else
            ImGui::SetTooltip("[%d, %d] unavailable", result.column, result.row);
    }
    return result;
}
} // namespace cyui
