#include "cyui/modules/Images.h"

namespace cyui {
ImageResult imageViewport(const char *id, ImageState &s, const ImageData &image, ImVec2 size,
                          ImageTool tool) {
    CanvasOptions options;
    options.grid = false;
    CanvasScope scope(id, s.canvas, size, options);
    const auto f = scope.frame();
    auto *d = ImGui::GetWindowDrawList();
    auto &io = ImGui::GetIO();
    ImageResult result;
    if (image.width <= 0 || image.height <= 0)
        return result;
    if (s.fitPending || (f.hovered && ImGui::IsMouseDoubleClicked(0))) {
        s.canvas.view.fit({0, 0}, {double(image.width), double(image.height)}, f.bounds.size(), dp(12));
        s.fitPending = false;
    }
    const auto a = s.canvas.view.screen({0, 0}, f.bounds.min),
               b = s.canvas.view.screen({double(image.width), double(image.height)}, f.bounds.min);
    const float tile = dp(16);
    const float left = std::max(a.x, f.bounds.min.x), top = std::max(a.y, f.bounds.min.y),
                right = std::min(b.x, f.bounds.max.x), bottom = std::min(b.y, f.bounds.max.y);
    for (float y = top; y < bottom; y += tile)
        for (float x = left; x < right; x += tile) {
            const int index = int((x - left) / tile) + int((y - top) / tile);
            d->AddRectFilled({x, y}, {std::min(x + tile, right), std::min(y + tile, bottom)},
                             index % 2 ? palette().bg2 : palette().bg3);
        }
    if (image.texture)
        d->AddImage(image.texture, a, b, image.uvMin, image.uvMax);
    const auto mouse = s.canvas.view.world(io.MousePos, f.bounds.min);
    if (f.hovered && mouse.x >= 0 && mouse.y >= 0 && mouse.x < image.width && mouse.y < image.height) {
        result.pixelX = int(mouse.x);
        result.pixelY = int(mouse.y);
        result.clicked = ImGui::IsMouseClicked(0);
    }
    if (tool == ImageTool::crop && !s.canvas.panning) {
        if (result.clicked) {
            s.start = mouse;
            s.before = s.crop;
            s.cropping = true;
            result.crop.phase = EditPhase::begin;
        }
        if (s.cropping) {
            const ImVec2 low{float(std::clamp(std::min(s.start.x, mouse.x), 0., double(image.width))),
                             float(std::clamp(std::min(s.start.y, mouse.y), 0., double(image.height)))};
            const ImVec2 high{float(std::clamp(std::max(s.start.x, mouse.x), 0., double(image.width))),
                              float(std::clamp(std::max(s.start.y, mouse.y), 0., double(image.height)))};
            s.crop = {low, high};
            result.crop.value = s.crop;
            if (ImGui::IsKeyPressed(ImGuiKey_Escape)) {
                s.cropping = false;
                s.crop = s.before;
                result.crop.value = s.before;
                result.crop.phase = EditPhase::cancel;
            } else if (!ImGui::IsMouseDown(0)) {
                s.cropping = false;
                result.crop.phase = EditPhase::commit;
            } else if (result.crop.phase != EditPhase::begin && (io.MouseDelta.x || io.MouseDelta.y))
                result.crop.phase = EditPhase::update;
        }
    }
    if (s.cropping || s.crop.size().x > 0) {
        const auto low = s.canvas.view.screen({s.crop.min.x, s.crop.min.y}, f.bounds.min),
                   high = s.canvas.view.screen({s.crop.max.x, s.crop.max.y}, f.bounds.min);
        d->AddRectFilled(low, high, palette().selection);
        d->AddRect(low, high, palette().accent, 0, 0, dp(2));
    }
    return result;
}
ImVec4 sampleGradient(ArrayView<GradientStop> stops, double t, bool linear) {
    if (!stops.count)
        return {};
    const GradientStop *left = nullptr, *right = nullptr;
    for (std::size_t i = 0; i < stops.count; ++i) {
        const auto &s = stops[i];
        if (s.position <= t && (!left || s.position >= left->position))
            left = &s;
        if (s.position > t && (!right || s.position < right->position))
            right = &s;
    }
    if (!left) {
        left = &stops[0];
        for (std::size_t i = 1; i < stops.count; ++i)
            if (stops[i].position < left->position)
                left = &stops[i];
        return left->color;
    }
    if (!right)
        return left->color;
    const float k = float((t - left->position) / (right->position - left->position));
    auto blend = [&](float a, float b) {
        if (!linear)
            return a + (b - a) * k;
        auto decode = [](float x) {
            return x <= .04045f ? x / 12.92f : std::pow((x + .055f) / 1.055f, 2.4f);
        };
        const float v = decode(a) + (decode(b) - decode(a)) * k;
        return v <= .0031308f ? v * 12.92f : 1.055f * std::pow(v, 1 / 2.4f) - .055f;
    };
    return {blend(left->color.x, right->color.x), blend(left->color.y, right->color.y),
            blend(left->color.z, right->color.z), left->color.w + (right->color.w - left->color.w) * k};
}
Edit<std::vector<GradientStop>> gradientEditor(const char *id, GradientState &s,
                                               const std::vector<GradientStop> &stops, ImVec2 size,
                                               bool linear, std::uint64_t revision) {
    if (size.y <= 0)
        size.y = dp(48);
    CanvasState canvas;
    CanvasOptions options;
    options.grid = options.navigation = false;
    CanvasScope scope(id, canvas, size, options);
    const auto f = scope.frame();
    const auto room = f.bounds.size();
    auto *d = ImGui::GetWindowDrawList();
    auto &io = ImGui::GetIO();
    Edit<std::vector<GradientStop>> result;
    if (s.active && (s.revision != revision || ImGui::IsKeyPressed(ImGuiKey_Escape))) {
        s.active = false;
        s.dragging = 0;
        s.preview.clear();
        result.phase = EditPhase::cancel;
    }
    const auto &shown = s.active ? s.preview : stops;
    const float barBottom = f.bounds.max.y - dp(14);
    const int samples = std::clamp(int(room.x / dp(2)), 1, 4096);
    for (int i = 0; i < samples; ++i) {
        const auto color = sampleGradient(shown, (i + .5) / samples, linear);
        d->AddRectFilled({f.bounds.min.x + room.x * i / samples, f.bounds.min.y},
                         {f.bounds.min.x + room.x * (i + 1) / samples, barBottom},
                         ImGui::ColorConvertFloat4ToU32(color));
    }
    const GradientStop *hit = nullptr;
    for (const auto &stop : shown) {
        const float x = f.bounds.min.x + room.x * float(stop.position);
        const ImVec2 a{x, barBottom}, b{x - dp(6), barBottom + dp(10)}, c{x + dp(6), barBottom + dp(10)};
        d->AddTriangleFilled(a, b, c, ImGui::ColorConvertFloat4ToU32(stop.color));
        d->AddTriangle(a, b, c, s.hasSelection && s.selected == stop.id ? palette().accent : palette().text,
                       dp(1.5f));
        if (std::abs(io.MousePos.x - x) < dp(8))
            hit = &stop;
    }
    const double mouse = std::clamp(double((io.MousePos.x - f.bounds.min.x) / room.x), 0., 1.);
    if (f.hovered && ImGui::IsMouseDoubleClicked(0) && !hit) {
        result.value = stops;
        ItemId next = 1;
        for (const auto &v : stops)
            next = std::max(next, v.id + 1);
        result.value.push_back({next, mouse, sampleGradient(stops, mouse, linear)});
        s.selected = next;
        s.hasSelection = true;
        result.phase = EditPhase::commit;
    } else if (f.hovered && ImGui::IsMouseClicked(0) && hit) {
        s.selected = s.dragging = hit->id;
        s.hasSelection = s.active = true;
        s.before = s.preview = stops;
        s.revision = revision;
        result = {EditPhase::begin, s.preview};
    }
    if (s.active) {
        if (ImGui::IsMouseDown(0)) {
            for (auto &stop : s.preview)
                if (stop.id == s.dragging)
                    stop.position = mouse;
            if (result.phase != EditPhase::begin && io.MouseDelta.x)
                result = {EditPhase::update, s.preview};
        } else {
            result = {EditPhase::commit, s.preview};
            s.active = false;
            s.dragging = 0;
        }
    }
    if (f.hovered && hit && ImGui::IsMouseClicked(1) && stops.size() > 2) {
        const auto key = hit->id;
        result.value = stops;
        result.value.erase(std::remove_if(result.value.begin(), result.value.end(),
                                          [&](const GradientStop &p) { return p.id == key; }),
                           result.value.end());
        result.phase = EditPhase::commit;
    }
    return result;
}
} // namespace cyui
