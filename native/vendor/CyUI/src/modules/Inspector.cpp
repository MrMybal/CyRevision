#include "cyui/modules/Inspector.h"
#include "ModuleInternal.h"
#include <unordered_set>

namespace cyui {
bool propertyMatches(const TextFilter& filter, const Property& property, const char* context) {
    if (!filter.active()) return true;
    const std::string path = context ? context : "";
    return filter.matches(property.label, path + " " + tr(path.c_str()) + " " + property.help + " " + tr(property.help.c_str()));
}
std::vector<PropertyEdit> inspector(const char* id, InspectorState& state, ArrayView<Property> properties,
                                    std::uint64_t revision, const PropertyValidator& validate) {
    return inspector(id, state, properties, revision, validate, {});
}
std::vector<PropertyEdit> inspector(const char *id, InspectorState &s, ArrayView<Property> properties,
                                    std::uint64_t revision, const PropertyValidator &validate, const InspectorOptions& options) {
    std::vector<PropertyEdit> result;
    ScopedID scope(id);
    std::unordered_set<ItemId> present;
    if (s.revision != revision) {
        for (auto &entry : s.drafts)
            if (entry.second.active)
                result.push_back({entry.first, {EditPhase::cancel, entry.second.before}});
        s.drafts.clear();
        s.revision = revision;
    }
    if (!beginProperties("##properties"))
        return result;
    std::size_t visible = 0;
    for (std::size_t i = 0; i < properties.count; ++i) {
        const auto &p = properties[i];
        present.insert(p.id);
        if (options.filter && !propertyMatches(*options.filter, p, options.filterContext)) {
            const auto it = s.drafts.find(p.id);
            if (it != s.drafts.end() && it->second.active) {
                ItemScope key(p.id);
                if (ImGui::GetActiveID() == ImGui::GetID("##value")) ImGui::ClearActiveID();
                auto& draft = it->second;
                result.push_back({p.id, {EditPhase::cancel, draft.before}});
                draft.value = draft.before;
                draft.active = false;
                draft.cancelled = true;
                draft.error.clear();
            }
            continue;
        }
        ++visible;
        auto &draft = s.drafts[p.id];
        if (!draft.active) {
            draft.value = p.value;
            draft.before = p.value;
        }
        ItemScope key(p.id);
        property(tr(p.label.c_str()).c_str(), p.help.empty() ? nullptr : tr(p.help.c_str()).c_str());
        bool changed = false, immediate = false;
        {
            ScopedDisabled disabled(p.readOnly);
            if (auto *boolValue = std::get_if<bool>(&draft.value)) {
                changed = toggleSwitch("##value", boolValue);
                immediate = true;
            } else if (auto *intValue = std::get_if<std::int64_t>(&draft.value)) {
                const std::int64_t *low = p.bounded ? &p.integerMinimum : nullptr,
                                   *high = p.bounded ? &p.integerMaximum : nullptr;
                changed = ImGui::DragScalar("##value", ImGuiDataType_S64, intValue, 1.f, low, high, "%lld",
                                            ImGuiSliderFlags_AlwaysClamp);
            } else if (auto *doubleValue = std::get_if<double>(&draft.value)) {
                const double *low = p.bounded ? &p.minimum : nullptr,
                             *high = p.bounded ? &p.maximum : nullptr;
                changed = ImGui::DragScalar("##value", ImGuiDataType_Double, doubleValue, .01f, low, high,
                                            "%.6g", ImGuiSliderFlags_AlwaysClamp);
            } else if (auto *value = std::get_if<std::string>(&draft.value)) {
                if (!p.choices.empty()) {
                    if (ImGui::BeginCombo("##value", tr(value->c_str()).c_str())) {
                        for (const auto &choice : p.choices)
                            if (ImGui::Selectable(trLabel(choice.c_str()).c_str(), choice == *value)) {
                                *value = choice;
                                changed = true;
                            }
                        ImGui::EndCombo();
                    }
                    immediate = true;
                } else
                    changed =
                        detail::inputText("##value", *value, p.password ? ImGuiInputTextFlags_Password : 0,
                                          p.multiline ? ImVec2(-1, dp(84)) : ImVec2{});
            }
        }
        EditPhase phase = EditPhase::none;
        const bool itemActive = ImGui::IsItemActive(), deactivated = ImGui::IsItemDeactivated();
        // Escape cancels whoever ends the edit: this code (still active), ImGui's own drag cancel or
        // keyboard navigation (deactivated this frame). Either way the draft must not commit.
        const bool escaped =
            draft.active && (itemActive || deactivated) && ImGui::IsKeyPressed(ImGuiKey_Escape, false);
        // The frame after ClearActiveID() ImGui reports the deactivation of an edit already cancelled.
        const bool stale = draft.cancelled && !itemActive;
        draft.cancelled = false;
        if (stale) {
        } else if (!p.readOnly && escaped) {
            if (itemActive) {
                ImGui::ClearActiveID();
                draft.cancelled = true;
            }
            phase = EditPhase::cancel;
            draft.value = draft.before;
            draft.active = false;
            draft.error.clear();
        } else if (!p.readOnly) {
            if (ImGui::IsItemActivated()) {
                draft.active = true;
                draft.before = p.value;
                phase = EditPhase::begin;
            }
            if (changed) {
                draft.active = true;
                phase = EditPhase::update;
                draft.error.clear();
            }
            if ((immediate && changed) || ImGui::IsItemDeactivatedAfterEdit()) {
                draft.error = validate ? validate(p.id, draft.value) : std::string{};
                if (draft.error.empty()) {
                    phase = EditPhase::commit;
                    draft.active = false;
                } else
                    phase = EditPhase::none;
            }
            if (deactivated && !ImGui::IsItemDeactivatedAfterEdit() && draft.active) {
                phase = EditPhase::cancel;
                draft.active = false;
                draft.value = draft.before;
            }
        }
        if (phase != EditPhase::none)
            result.push_back({p.id, {phase, draft.value}});
        if (!draft.error.empty())
            textColoured(palette().error, tr("%s").c_str(), draft.error.c_str());
    }
    endProperties();
    if (!visible && options.filter && options.filter->active())
        textDim("%s", tr("No matching properties").c_str());
    for (auto it = s.drafts.begin(); it != s.drafts.end();)
        if (!present.count(it->first))
            it = s.drafts.erase(it);
        else
            ++it;
    return result;
}
} // namespace cyui
