#pragma once

// CyUI - Gallery: every widget on one window, a theme switcher, a palette
// editor. The CyUI equivalent of ImGui::ShowDemoWindow(): open it to see
// what exists and how it looks in each theme, and read Gallery.cpp for the
// code of each example.

#include "cyui/Export.h"
#include <imgui.h>
#include <array>

namespace cyui {
class WebBackend;
// What the optional module pages show: the host's own resources.
struct GalleryResources {
    ImTextureID texture = 0;          // shown by the Images / Viewport pages (0: none)
    int width = 0, height = 0;        // its size in texels (256 when 0)
    WebBackend *web = nullptr;        // Web page: a live backend (a preview when null)
    bool previewCommands = false;     // Commands page: keep the palette open (captures)
    std::array<ImTextureID, 4> imagePreviews{}; // Optional host textures for asset/node examples.
    bool previewImageGraph = false;   // Nodes page: show the image-processing example (captures).
};

// selectTab >= 0 brings that page to the front once (again when the value
// changes; the tabs stay clickable): 0 text, 1 buttons, 2 controls,
// 3 containers, 4-19 the optional modules (canvas, plots, curves, nodes,
// timeline, audio views, code, diff, inspector, assets, images, viewport,
// commands, diagnostics, documents, web), 20 layout.
CYUI_API void showGallery(bool *open = nullptr, int selectTab = -1);
CYUI_API void showGallery(bool *open, int selectTab, const GalleryResources &resources);
// Live palette editor; saves / loads .cytheme files.
CYUI_API void showThemeEditor(bool *open = nullptr);

} // namespace cyui
