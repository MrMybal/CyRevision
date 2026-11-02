#pragma once

// CyUI - a better-looking, consistent Dear ImGui for the Cy* applications.
//
// Compatibility include for the enabled modules (link CyUI::CyUI):
//   #include <cyui/CyUI.h>
// For selective use, include Core.h and the desired module headers instead.
//
// Setup, once after ImGui::CreateContext() and before the first frame:
//   cyui::Config config;
//   config.themeKey = "mid-grey";            // see cyui::builtInThemes()
//   config.density = cyui::Density::compact;
//   config.dpiScale = dpi;                   // 1.0 at 96 dpi
//   cyui::apply(config);
//   cyui::loadFonts();
//
// Then use cyui:: widgets where stock ImGui looks plain (buttons, toggles,
// cards, properties, knobs, search...), and stock ImGui for the rest: the
// theme styles it too. See docs/AI_GUIDE.md for the rules.

#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include "cyui/Widgets.h"
#include "cyui/Gallery.h"

#define CYUI_VERSION "1.2.0"
#define CYUI_VERSION_NUM 10200

// Optional modules selected by the linked CMake targets.
#if defined(CYUI_HAS_CANVAS)
#include "cyui/modules/Canvas.h"
#endif
#if defined(CYUI_HAS_PLOTS)
#include "cyui/modules/Plots.h"
#endif
#if defined(CYUI_HAS_CURVES)
#include "cyui/modules/Curves.h"
#endif
#if defined(CYUI_HAS_NODES)
#include "cyui/modules/Nodes.h"
#endif
#if defined(CYUI_HAS_TIMELINE)
#include "cyui/modules/Timeline.h"
#endif
#if defined(CYUI_HAS_AUDIOVIEWS)
#include "cyui/modules/AudioViews.h"
#endif
#if defined(CYUI_HAS_CODE)
#include "cyui/modules/Code.h"
#endif
#if defined(CYUI_HAS_DIFF)
#include "cyui/modules/Diff.h"
#endif
#if defined(CYUI_HAS_CODEEDITOR)
#include "cyui/modules/CodeEditor.h"
#endif
#if defined(CYUI_HAS_INSPECTOR)
#include "cyui/modules/Inspector.h"
#endif
#if defined(CYUI_HAS_ASSETS)
#include "cyui/modules/Assets.h"
#endif
#if defined(CYUI_HAS_IMAGES)
#include "cyui/modules/Images.h"
#endif
#if defined(CYUI_HAS_VIEWPORT)
#include "cyui/modules/Viewport.h"
#endif
#if defined(CYUI_HAS_COMMANDS)
#include "cyui/modules/Commands.h"
#endif
#if defined(CYUI_HAS_DIAGNOSTICS)
#include "cyui/modules/Diagnostics.h"
#endif
#if defined(CYUI_HAS_DOCUMENTVIEWS)
#include "cyui/modules/DocumentViews.h"
#endif

#if defined(CYUI_HAS_WEB)
#include "cyui/modules/Web.h"
#endif

#if defined(CYUI_HAS_IMGUIZMO)
#include "cyui/adapters/ImGuizmo.h"
#endif
