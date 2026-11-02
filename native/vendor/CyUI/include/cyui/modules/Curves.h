#pragma once
#include "cyui/modules/Plots.h"
#include <functional>

namespace cyui {
enum class CurveShape { linear, step, smooth, bezier };
struct CurvePoint {
    ItemId id = 0;
    double time = 0, value = 0;
    CurveShape shape = CurveShape::linear;
    Point in{-0.1, 0}, out{0.1, 0}; // offsets in the host's time/value units
};
CYUI_API double evaluateCurve(ArrayView<CurvePoint>, double time, double fallback = 0);
struct CurveState {
    PlotState view;
    std::vector<ItemId> selection;
    std::vector<CurvePoint> before, preview, clipboard;
    Point grab{}, boxStart{};
    ItemId active = 0;
    int handle = 0;
    bool dragging = false, boxing = false, focused = false;
    std::uint64_t revision = 0;
};
struct CurveOptions {
    double snap = 0;
    bool readOnly = false, handles = true;
    std::uint64_t revision = 0;
    // Evaluates the preview, so host interpolation remains authoritative during a gesture.
    std::function<double(ArrayView<CurvePoint>, double)> evaluate;
    std::function<double(double)> snapTime;
};
CYUI_API Edit<std::vector<CurvePoint>> curveEditor(const char *id, CurveState &, const std::vector<CurvePoint> &,
                                          ImVec2 size, const CurveOptions & = {});
} // namespace cyui
