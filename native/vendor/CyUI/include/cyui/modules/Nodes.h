#pragma once
#include "cyui/modules/Canvas.h"
#include <functional>
#include <string>

namespace cyui {
struct NodePin {
    ItemId id = 0;
    std::string label;
    int colorIndex = 0;
};
struct Node {
    ItemId id = 0;
    Point position;
    std::string label;
    std::vector<NodePin> inputs, outputs;
    int colorIndex = 0;
};
struct NodeLink {
    ItemId id = 0, from = 0, to = 0;
    int colorIndex = 0;
};
struct NodeMove {
    ItemId id = 0;
    Point position;
};
struct NodeResult {
    Edit<std::vector<NodeMove>> moved;
    ItemId connectFrom = 0, connectTo = 0; // valid when connect
    std::vector<ItemId> removeNodes;
    ItemId removeLink = 0; // valid when linkRemoved
    bool connect = false, linkRemoved = false;
};
struct NodeState {
    CanvasState canvas;
    std::vector<ItemId> selection;
    std::vector<NodeMove> before, preview;
    Point grab, boxStart;
    ItemId wire = 0; // the pin a new link starts from, while wiring
    bool wireOutput = false, dragging = false, boxing = false, focused = false;
    std::uint64_t revision = 0;
    bool wiring = false;
};
struct NodeOptions {
    bool readOnly = false;
    double snap = 0;
    std::uint64_t revision = 0;
    float width = 190, header = 28, row = 24;
    std::function<bool(ItemId, ItemId)> canConnect;
    // Extra drawing inside each card, in screen coordinates (world-to-screen via state.canvas.view).
    std::function<void(const Node &, const Rect &)> drawBody;
};
CYUI_API NodeResult nodeEditor(const char *id, NodeState &, const std::vector<Node> &, const std::vector<NodeLink> &,
                      ImVec2 size, const NodeOptions & = {});
// Layout produces positions; the host decides whether and how to commit them.
CYUI_API std::vector<NodeMove> arrangeNodes(const std::vector<Node> &, const std::vector<NodeLink> &,
                                   Point spacing = {240, 130});
} // namespace cyui
