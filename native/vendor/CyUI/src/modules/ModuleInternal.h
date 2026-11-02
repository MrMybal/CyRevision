#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include <imgui_internal.h>
#include <string>
#include <string_view>
#include <cctype>

namespace cyui::detail {
inline int resizeText(ImGuiInputTextCallbackData *data) {
    if (data->EventFlag == ImGuiInputTextFlags_CallbackResize) {
        auto *text = static_cast<std::string *>(data->UserData);
        text->resize(std::size_t(data->BufTextLen));
        data->Buf = text->data();
    }
    return 0;
}
inline bool inputText(const char *id, std::string &text, ImGuiInputTextFlags flags = 0,
                      ImVec2 multiline = {}) {
    flags |= ImGuiInputTextFlags_CallbackResize;
    if (multiline.y > 0)
        return ImGui::InputTextMultiline(id, text.data(), text.capacity() + 1, multiline, flags, resizeText,
                                         &text);
    return ImGui::InputText(id, text.data(), text.capacity() + 1, flags, resizeText, &text);
}
inline std::string folded(std::string value) {
    for (auto &c : value)
        if (static_cast<unsigned char>(c) < 128)
            c = char(std::tolower(static_cast<unsigned char>(c)));
    return value;
}
inline bool matches(const std::string &text, const std::string &query) {
    return folded(text).find(folded(query)) != std::string::npos;
}
// ASCII case-insensitive search for an already folded query, without allocating.
inline bool containsFolded(std::string_view text, std::string_view query) {
    if (query.size() > text.size())
        return false;
    for (std::size_t i = 0; i + query.size() <= text.size(); ++i) {
        std::size_t j = 0;
        for (; j < query.size(); ++j) {
            const auto c = static_cast<unsigned char>(text[i + j]);
            if ((c < 128 ? char(std::tolower(c)) : char(c)) != query[j])
                break;
        }
        if (j == query.size())
            return true;
    }
    return false;
}
// Matches "first second" against a folded query without building the joined string.
inline bool matchesPair(const std::string &first, const std::string &second, const std::string &query) {
    if (query.find(' ') == std::string::npos)
        return containsFolded(first, query) || containsFolded(second, query);
    return containsFolded(first + " " + second, query);
}
// Smallest span still resolvable around a range: tiny steps at large magnitudes stall otherwise.
inline double minimumSpan(const Range &r) {
    return std::max(1e-9, std::max(std::abs(r.min), std::abs(r.max)) * 1e-12);
}
// zoomRange with a magnitude-relative floor; an unusable result keeps the old range.
inline Range zoomView(const Range &r, double anchor, double factor, double maxSpan = 1e15) {
    if (!r.valid())
        return r;
    const auto z = zoomRange(r, anchor, factor, minimumSpan(r), maxSpan);
    return z.valid() && z.span() >= minimumSpan(z) * .5 ? z : r;
}
inline Range panView(const Range &r, double delta) {
    const Range p{r.min + delta, r.max + delta};
    return std::isfinite(delta) && p.valid() && p.span() >= minimumSpan(p) * .5 ? p : r;
}
// Pans by `shift` spans and zooms by `factor` at `anchor`, in log space for a log axis. Invalid
// results (overflowing exp, collapsed spans) are rejected, so the axis never becomes unusable.
template <class A> bool navigateAxis(A &a, double shift, double anchor, double factor) {
    if (!a.range.valid())
        return false;
    Range next = a.range;
    if (a.logarithmic && a.range.min > 0) {
        constexpr double limit = 700; // exp() stays finite and nonzero
        Range log{std::log(a.range.min), std::log(a.range.max)};
        log = panView(log, shift * log.span());
        if (anchor > 0)
            log = zoomView(log, std::log(anchor), factor, 2 * limit);
        if (log.min < -limit || log.max > limit)
            return false;
        next = {std::exp(log.min), std::exp(log.max)};
    } else
        next = zoomView(panView(a.range, shift * a.range.span()), anchor, factor);
    if (!next.valid() || (next.min == a.range.min && next.max == a.range.max))
        return false;
    a.range = next;
    return true;
}
// Calls draw(t) for each multiple of step in [low, high], at most `cap` times. Counting with an
// integer keeps steps below the ULP of t from stalling the loop.
template <class F> void forEachTick(double low, double high, double step, F &&draw, int cap = 1000) {
    if (!(step > 0) || !std::isfinite(step) || !std::isfinite(low) || !std::isfinite(high))
        return;
    const double first = std::ceil(low / step);
    double last = -HUGE_VAL;
    for (int i = 0; i < cap; ++i) {
        const double t = (first + i) * step;
        if (t > high || t <= last)
            break;
        draw(t);
        last = t;
    }
}
// A list row whose pitch is exactly its height (no ItemSpacing), so clipper heights, content sizes
// and scroll targets computed from `size.y` line up with the rows.
inline bool listRow(const char *id, ImVec2 size) {
    ScopedVar tight(ImGuiStyleVar_ItemSpacing, ImVec2(ImGui::GetStyle().ItemSpacing.x, 0));
    return ImGui::InvisibleButton(id, size);
}
} // namespace cyui::detail
