#pragma once
#include "cyui/modules/Canvas.h"

namespace cyui {
struct ImageData {
    ImTextureID texture = 0;
    int width = 0, height = 0;
    ImVec2 uvMin{0, 0}, uvMax{1, 1};
};
enum class ImageTool { inspect, crop };
struct ImageState {
    CanvasState canvas;
    bool fitPending = true, cropping = false;
    Point start;
    Rect crop, before;
};
struct ImageResult {
    int pixelX = -1, pixelY = -1;
    bool clicked = false;
    Edit<Rect> crop;
};
CYUI_API ImageResult imageViewport(const char *id, ImageState &, const ImageData &, ImVec2 size,
                          ImageTool tool = ImageTool::inspect);
struct GradientStop {
    ItemId id = 0;
    double position = 0;
    ImVec4 color{0, 0, 0, 1};
};
struct GradientState {
    ItemId selected = 0, dragging = 0; // valid while hasSelection / active
    std::vector<GradientStop> before, preview;
    std::uint64_t revision = 0;
    bool hasSelection = false, active = false;
};
CYUI_API ImVec4 sampleGradient(ArrayView<GradientStop>, double position, bool linearLight = false);
CYUI_API Edit<std::vector<GradientStop>> gradientEditor(const char *id, GradientState &,
                                               const std::vector<GradientStop> &, ImVec2 size = {},
                                               bool linearLight = false, std::uint64_t revision = 0);
} // namespace cyui
