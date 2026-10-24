#pragma once
#include <imgui.h>
#include <string>
namespace ImGui {
bool InputText(const char* label,std::string* value,ImGuiInputTextFlags flags=0);
bool InputTextWithHint(const char* label,const char* hint,std::string* value,ImGuiInputTextFlags flags=0);
bool InputTextMultiline(const char* label,std::string* value,const ImVec2& size=ImVec2(0,0),ImGuiInputTextFlags flags=0);
}
