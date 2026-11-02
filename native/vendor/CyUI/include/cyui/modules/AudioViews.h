#pragma once
#include "cyui/modules/Plots.h"

namespace cyui {
struct Peak {
    float minimum = 0, maximum = 0;
};
// Prepared envelope covers [start,end]; one view per channel. No decoder or FFT runs in a widget.
struct WaveformData {
    Range coverage;
    std::vector<ArrayView<Peak>> channels;
};
struct WaveformState {
    Range view{0, 1}, selection{0, 0}; // selection is kept ordered (min <= max)
    bool selecting = false, panning = false;
    double anchor = 0; // where the selection drag started
};
struct WaveformResult {
    bool seek = false, selectionChanged = false;
    double time = 0;
};
CYUI_API WaveformResult waveform(const char *id, WaveformState &, const WaveformData &, double playhead, ImVec2 size);
CYUI_API std::vector<Peak> makePeaks(ArrayView<float> samples, std::size_t columns);
// Logarithmic X; an invalid or non-positive X range resets to 20..20000.
CYUI_API PlotResult spectrum(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size);
CYUI_API PlotResult spectrum(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size, const PlotOptions &);
// Linear axes.
CYUI_API PlotResult oscilloscope(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size);
CYUI_API PlotResult oscilloscope(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size, const PlotOptions &);
// Square X/Y scope: both axes share one range symmetric around 0 (X sets the extent; zoom scales it).
CYUI_API PlotResult stereoScope(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size);
CYUI_API PlotResult stereoScope(const char *id, PlotState &, ArrayView<PlotSeries>, ImVec2 size, const PlotOptions &);
} // namespace cyui
