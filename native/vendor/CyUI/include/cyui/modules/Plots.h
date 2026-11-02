#pragma once
#include "cyui/modules/Canvas.h"
#include <string>
#include <functional>

namespace cyui {
struct CYUI_API Axis {
    Range range;
    bool logarithmic = false;
    double fraction(double value) const;
    double value(double fraction) const;
};
enum class PlotStyle { line, step, points, bars };
struct PlotSeries {
    ItemId id = 0;
    const char *label = "";
    ArrayView<Point> points;
    PlotStyle style = PlotStyle::line;
    int colorIndex = 0;
};
struct PlotState {
    Axis x, y;
    bool dragging = false;
};
struct PlotResult {
    bool hovered = false, viewChanged = false;
    Point mouse;
    ItemId series = 0; // nearest sample's series, 0 for none: give series nonzero IDs
    std::size_t index = 0;
    std::vector<ItemId> skippedSeries;
};
struct PlotOptions {
    bool grid = true, crosshair = true, navigation = true, axes = true, legend = true;
    std::function<std::string(double)> formatX, formatY;
    std::size_t maxPoints = 65536;
};
CYUI_API PlotResult plot(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size, const PlotOptions & = {});
// Preparation helpers: run when data changes, outside the draw loop. Gaps remain gaps.
CYUI_API std::vector<Point> reducePlot(ArrayView<Point>, std::size_t budget);
CYUI_API std::vector<std::uint64_t> histogram(ArrayView<double>, Range, std::size_t bins);
struct HeatmapResult {
    int row = -1, column = -1;
    bool clicked = false;
};
CYUI_API HeatmapResult heatmap(const char *id, ArrayView<float>, int columns, int rows, Range values, ImVec2 size);
} // namespace cyui
