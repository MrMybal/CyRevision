#pragma once

// CyUI - Gallery: every widget on one window, a theme switcher, a palette
// editor. The CyUI equivalent of ImGui::ShowDemoWindow(): open it to see
// what exists and how it looks in each theme, and read Gallery.cpp for the
// code of each example.

namespace cyui {

// selectTab >= 0 brings that page to the front (0 text, 1 buttons, 2 controls, 3 containers).
void showGallery(bool* open = nullptr, int selectTab = -1);
// Live palette editor; saves / loads .cytheme files.
void showThemeEditor(bool* open = nullptr);

}  // namespace cyui
