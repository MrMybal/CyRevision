#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"

namespace cyui {
struct CYUI_API CanvasView {
    Point origin{};
    double zoom = 1;
    Point world(ImVec2 screen, ImVec2 topLeft = {}) const;
    ImVec2 screen(Point world, ImVec2 topLeft = {}) const;
    void zoomAt(ImVec2 anchor, double newZoom, ImVec2 topLeft = {});
    void fit(Point low, Point high, ImVec2 available, double padding = 24);
};
struct CanvasState {
    CanvasView view;
    bool panning = false;
};
struct CanvasOptions {
    bool grid = true, navigation = true, spacePan = true;
    double gridStep = 32;
    float minZoom = .05f, maxZoom = 64.f;
    // Take the mouse wheel (frame().wheel) without navigation; navigating canvases always take it.
    // An unclaimed wheel scrolls the window underneath.
    bool claimWheel = false;
};
struct CanvasFrame {
    Rect bounds;
    bool hovered = false, focused = false;
    Point mouse;
    float wheel = 0; // itemWheel() of the canvas item; 0 when its navigation used it
};
// Reserves an ImGui item, handles navigation and clips all drawing until destruction.
class CYUI_API CanvasScope {
  public:
    CanvasScope(const char *id, CanvasState &, ImVec2 size, const CanvasOptions & = {});
    ~CanvasScope();
    CanvasScope(const CanvasScope &) = delete;
    CanvasScope &operator=(const CanvasScope &) = delete;
    const CanvasFrame &frame() const {
        return frame_;
    }

  private:
    CanvasFrame frame_;
};
CYUI_API void drawCanvasGrid(const Rect &, const CanvasView &, double step = 32);
// Call each frame an interactive item is drawn over a CanvasScope (as transformGizmo does), in the
// same window. Canvases there then use ImGui's overlap mode: the later item takes the hover and the
// click, at the cost of one frame of hover delay on the canvas.
CYUI_API void markCanvasOverlay();
} // namespace cyui
