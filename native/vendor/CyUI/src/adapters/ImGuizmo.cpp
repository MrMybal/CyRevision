#include "cyui/adapters/ImGuizmo.h"

namespace cyui {
Edit<std::array<float, 16>> gizmo3D(const char *id, Gizmo3DState &s, const std::array<float, 16> &matrix,
                                    const std::array<float, 16> &view,
                                    const std::array<float, 16> &projection, const Rect &rect,
                                    const Gizmo3DOptions &o) {
    Edit<std::array<float, 16>> result;
    static ImGuiContext *context = nullptr;
    static int frame = -1;
    if (context != ImGui::GetCurrentContext() || frame != ImGui::GetFrameCount()) {
        context = ImGui::GetCurrentContext();
        frame = ImGui::GetFrameCount();
        ImGuizmo::SetImGuiContext(context);
        ImGuizmo::BeginFrame();
    }
    if (s.active && (o.revision != s.revision || !o.enabled || ImGui::IsKeyPressed(ImGuiKey_Escape))) {
        result = {EditPhase::cancel, s.before};
        s.active = false;
        s.blocked = true;
    }
    if (s.blocked && !ImGui::IsMouseDown(0))
        s.blocked = false;
    if (!s.active)
        s.preview = matrix;
    const auto saved = ImGuizmo::GetStyle();
    auto &style = ImGuizmo::GetStyle();
    const auto &p = palette();
    for (auto &color : style.Colors)
        color = vec(p.textDim);
    style.Colors[ImGuizmo::DIRECTION_X] = vec(p.error);
    style.Colors[ImGuizmo::DIRECTION_Y] = vec(p.success);
    style.Colors[ImGuizmo::DIRECTION_Z] = vec(p.info);
    style.Colors[ImGuizmo::PLANE_X] = vec(alpha(p.error, .35f));
    style.Colors[ImGuizmo::PLANE_Y] = vec(alpha(p.success, .35f));
    style.Colors[ImGuizmo::PLANE_Z] = vec(alpha(p.info, .35f));
    style.Colors[ImGuizmo::SELECTION] = vec(p.accent);
    style.Colors[ImGuizmo::INACTIVE] = vec(p.textFaint);
    style.Colors[ImGuizmo::TEXT] = vec(p.text);
    style.Colors[ImGuizmo::TEXT_SHADOW] = vec(alpha(p.bg0, 0));
    style.TranslationLineThickness = dp(2);
    style.TranslationLineArrowSize = dp(6);
    style.RotationLineThickness = dp(2);
    style.RotationOuterLineThickness = dp(2);
    style.ScaleLineThickness = dp(2);
    style.ScaleLineCircleSize = dp(5);
    style.HatchedAxisLineThickness = dp(1);
    style.CenterCircleSize = dp(5);
    // ImGuizmo keeps one process-wide state: scope ids by context so two contexts never share
    // a hot/active gizmo id.
    ImGuizmo::PushID(ImGui::GetCurrentContext());
    ImGuizmo::PushID(id);
    ImGuizmo::SetDrawlist(ImGui::GetWindowDrawList());
    ImGuizmo::SetRect(rect.min.x, rect.min.y, rect.size().x, rect.size().y);
    ImGuizmo::SetOrthographic(o.orthographic);
    ImGuizmo::Enable(o.enabled && !s.blocked);
    const bool changed = ImGuizmo::Manipulate(view.data(), projection.data(), o.operation, o.mode,
                                              s.preview.data(), nullptr, o.snap);
    const bool usingGizmo = ImGuizmo::IsUsing();
    if (usingGizmo && !s.active && !s.blocked) {
        s.active = true;
        s.before = matrix;
        s.revision = o.revision;
        result = {EditPhase::begin, s.preview};
    }
    if (s.active) {
        if (!usingGizmo) {
            result = {EditPhase::commit, s.preview};
            s.active = false;
        } else if (changed && result.phase != EditPhase::begin)
            result = {EditPhase::update, s.preview};
    }
    ImGuizmo::PopID();
    ImGuizmo::PopID();
    ImGuizmo::GetStyle() = saved;
    return result;
}
} // namespace cyui
