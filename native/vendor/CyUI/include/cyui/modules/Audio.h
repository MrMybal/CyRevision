#pragma once

// Optional audio controls. Link CyUI::Audio; no audio engine is required.
#include "cyui/CoreWidgets.h"

namespace cyui {

enum KnobFlags : int {
    knobNone = 0,
    knobBipolar = 1 << 0,      // the arc starts at the centre (pan, detune)
    knobLogarithmic = 1 << 1,  // frequency-like ranges (min > 0)
    knobNoLabel = 1 << 2,
    knobInteger = 1 << 3,
};

struct KnobStyle {
    float diameter = 30.0f;     // in dp
    ImU32 colour = 0;           // 0: the accent
    const char* label = nullptr;  // under the knob; replaced by the value while hovered
    const char* format = "%.2f";
    const char* unit = nullptr;
    int flags = knobNone;
    float modulation = -1.0f;   // a live position (0..1) shown as a dot, <0 for none
    int steps = 0;              // snap to this many steps across the range; 0: continuous
};

// A rotary knob. Returns true while the value changes. Clicking its value
// (under it) without dragging lets it be typed.
CYUI_API bool knob(const char* id, float* value, float minimum, float maximum, float defaultValue, const KnobStyle& style = {});

// A vertical fader in dB with its scale; minimumDb sits at the bottom, maximumDb at the top. A
// size of 0 or less follows ImGui (default / relative to the available space). Returns true while
// it changes.
CYUI_API bool fader(const char* id, float* gainDb, float minimumDb, float maximumDb, ImVec2 size, ImU32 colour = 0);
// A level meter: peak and RMS of one or two channels, linear gain (1 = 0 dB).
CYUI_API void meter(const char* id, ImVec2 size, float peakLeft, float peakRight, float rmsLeft, float rmsRight,
           bool vertical = true);
// A meter's position (0..1) for a level in dB.
[[nodiscard]] CYUI_API float meterPosition(float gainDb);

} // namespace cyui
