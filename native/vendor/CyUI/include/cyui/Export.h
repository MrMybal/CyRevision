#pragma once

// Override when building/consuming CyUI as a shared library (e.g. Unreal UBT).
// Static CMake consumers keep the original API and ABI.
#ifndef CYUI_API
#define CYUI_API
#endif
