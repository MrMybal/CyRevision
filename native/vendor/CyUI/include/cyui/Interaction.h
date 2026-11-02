#pragma once
#include <imgui.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

namespace cyui {
using ItemId = std::uint64_t;
class ItemScope {
  public:
    explicit ItemScope(ItemId id) {
        ImGui::PushID(int(id >> 32));
        ImGui::PushID(int(id & 0xffffffffu));
    }
    ~ItemScope() {
        ImGui::PopID();
        ImGui::PopID();
    }
    ItemScope(const ItemScope &) = delete;
    ItemScope &operator=(const ItemScope &) = delete;
};
enum class EditPhase { none, begin, update, commit, cancel };
template <class T> struct Edit {
    EditPhase phase = EditPhase::none;
    T value{};
    bool committed() const {
        return phase == EditPhase::commit;
    }
};
struct Point {
    double x = 0, y = 0;
};
struct Range {
    double min = 0, max = 1;
    double span() const {
        return max - min;
    }
    bool valid() const {
        return std::isfinite(min) && std::isfinite(max) && max > min;
    }
    bool contains(double v) const {
        return v >= min && v <= max;
    }
};
struct Rect {
    ImVec2 min{}, max{};
    ImVec2 size() const {
        return {max.x - min.x, max.y - min.y};
    }
    bool contains(ImVec2 p) const {
        return p.x >= min.x && p.y >= min.y && p.x <= max.x && p.y <= max.y;
    }
};
template <class T> struct ArrayView {
    const T *data = nullptr;
    std::size_t count = 0;
    ArrayView() = default;
    ArrayView(const T *p, std::size_t n) : data(p), count(p ? n : 0) {}
    ArrayView(const std::vector<T> &v) : data(v.data()), count(v.size()) {}
    const T &operator[](std::size_t i) const {
        return data[i];
    }
};
inline double snapValue(double value, double step) {
    return step > 0 && std::isfinite(step) ? std::round(value / step) * step : value;
}
// Zooms around `anchor`. The span never drops below minSpan nor below what the
// magnitude of the values can still resolve; an unusable result keeps `r`.
inline Range zoomRange(Range r, double anchor, double factor, double minSpan = 1e-9, double maxSpan = 1e15) {
    if (!r.valid() || !std::isfinite(anchor) || !std::isfinite(factor) || factor <= 0)
        return r;
    const double floor = std::min({std::max(minSpan, std::max(std::abs(r.min), std::abs(r.max)) * 1e-12), maxSpan, r.span()});
    const double span = std::clamp(r.span() * factor, floor, std::max(floor, maxSpan));
    const double part = (anchor - r.min) / r.span();
    const Range out{anchor - part * span, anchor + (1 - part) * span};
    return out.valid() ? out : r;
}
inline bool selected(const std::vector<ItemId> &ids, ItemId id) {
    return std::find(ids.begin(), ids.end(), id) != ids.end();
}
inline void selectItem(std::vector<ItemId> &ids, ItemId id, bool extend) {
    const auto it = std::find(ids.begin(), ids.end(), id);
    if (!extend)
        ids.assign(1, id);
    else if (it == ids.end())
        ids.push_back(id);
    else
        ids.erase(it);
}
} // namespace cyui
