cmake_minimum_required(VERSION 3.16)
project(CyUI VERSION 1.0.0 LANGUAGES CXX)

# CyUI: a themed widget layer over Dear ImGui (>= 1.92).
#
# In an app that already builds Dear ImGui as a target:
#   set(CYUI_IMGUI_TARGET my_imgui_target)
#   add_subdirectory(path/to/CyImgui_Interface)
#   target_link_libraries(my_app PRIVATE CyUI::CyUI)
#
# Standalone (the demo), with the bundled copy in third_party/imgui:
#   cmake -B build -DCYUI_BUILD_DEMO=ON && cmake --build build --config Release

set(CYUI_IMGUI_TARGET "" CACHE STRING "An existing Dear ImGui target to link (empty: build the bundled copy)")
set(CYUI_IMGUI_DIR "${CMAKE_CURRENT_SOURCE_DIR}/third_party/imgui" CACHE PATH "Dear ImGui sources, when no target is given")
option(CYUI_BUILD_DEMO "Build the Win32 + DX11 demo" ${PROJECT_IS_TOP_LEVEL})

if(CYUI_IMGUI_TARGET STREQUAL "")
    if(NOT TARGET cyui_imgui)
        add_library(cyui_imgui STATIC
            ${CYUI_IMGUI_DIR}/imgui.cpp
            ${CYUI_IMGUI_DIR}/imgui_draw.cpp
            ${CYUI_IMGUI_DIR}/imgui_tables.cpp
            ${CYUI_IMGUI_DIR}/imgui_widgets.cpp
            ${CYUI_IMGUI_DIR}/imgui_demo.cpp)
        target_include_directories(cyui_imgui PUBLIC ${CYUI_IMGUI_DIR})
        target_compile_features(cyui_imgui PUBLIC cxx_std_17)
    endif()
    set(CYUI_IMGUI_TARGET cyui_imgui)
endif()

add_library(cyui STATIC
    src/Theme.cpp
    src/Widgets.cpp
    src/Layout.cpp
    src/Gallery.cpp)
add_library(CyUI::CyUI ALIAS cyui)
target_include_directories(cyui PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_link_libraries(cyui PUBLIC ${CYUI_IMGUI_TARGET})
target_compile_features(cyui PUBLIC cxx_std_17)
if(MSVC)
    target_compile_options(cyui PRIVATE /W4 /utf-8)
else()
    target_compile_options(cyui PRIVATE -Wall -Wextra)
endif()

if(CYUI_BUILD_DEMO AND WIN32)
    add_executable(cyui_demo WIN32
        examples/demo_win32_dx11/main.cpp
        ${CYUI_IMGUI_DIR}/backends/imgui_impl_win32.cpp
        ${CYUI_IMGUI_DIR}/backends/imgui_impl_dx11.cpp)
    target_include_directories(cyui_demo PRIVATE ${CYUI_IMGUI_DIR}/backends)
    target_link_libraries(cyui_demo PRIVATE CyUI::CyUI d3d11 dxgi d3dcompiler dwmapi shell32)
    if(MSVC)
        target_compile_options(cyui_demo PRIVATE /utf-8)
    endif()
endif()
