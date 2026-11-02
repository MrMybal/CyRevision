#pragma once
#include "cyui/modules/Images.h"
#include <array>
#include <functional>

namespace cyui {
struct Vec3 {
    double x = 0, y = 0, z = 0;
};
struct OrbitCamera {
    Vec3 target;
    double yaw = 0, pitch = .25, distance = 5;
    Vec3 up{0, 1, 0};
};
CYUI_API Vec3 cameraPosition(const OrbitCamera &);
CYUI_API std::array<float, 16> cameraViewMatrix(const OrbitCamera &); // column-major, right-handed
struct ViewportState {
    OrbitCamera camera;
    bool orbiting = false, panning = false;
};
struct ViewportResult {
    bool cameraChanged = false, clicked = false;
    ImVec2 uv{-1, -1};
};
CYUI_API ViewportResult viewport(const char *id, ViewportState &, const ImageData &, ImVec2 size,
                        bool navigation = true);
struct Transform2D {
    Point center;
    Point size{1, 1};
    double rotation = 0;
};
struct TransformState {
    Transform2D before, preview;
    Point grab;
    int handle = 0;
    std::uint64_t revision = 0;
};
struct TransformOptions {
    std::function<ImVec2(Point)> project;
    std::function<Point(ImVec2)> unproject;
    bool enabled = true;
    std::uint64_t revision = 0;
};
CYUI_API Edit<Transform2D> transformGizmo(const char *id, TransformState &, const Transform2D &,
                                 const TransformOptions & = {});
} // namespace cyui
