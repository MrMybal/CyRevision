#include "StringInput.h"
#include <algorithm>
namespace {
int resizeString(ImGuiInputTextCallbackData* data) {
    if(data->EventFlag==ImGuiInputTextFlags_CallbackResize) {
        auto* value=static_cast<std::string*>(data->UserData);
        value->resize(static_cast<size_t>(data->BufTextLen));
        data->Buf=value->data();
    }
    return 0;
}
void prepare(std::string* value) {
    if(value->capacity()<64) value->reserve(64);
}
}
namespace ImGui {
bool InputText(const char* label,std::string* value,ImGuiInputTextFlags flags) {
    prepare(value);
    return InputText(label,value->data(),value->capacity()+1,flags|ImGuiInputTextFlags_CallbackResize,resizeString,value);
}
bool InputTextWithHint(const char* label,const char* hint,std::string* value,ImGuiInputTextFlags flags) {
    prepare(value);
    return InputTextWithHint(label,hint,value->data(),value->capacity()+1,flags|ImGuiInputTextFlags_CallbackResize,resizeString,value);
}
bool InputTextMultiline(const char* label,std::string* value,const ImVec2& size,ImGuiInputTextFlags flags) {
    prepare(value);
    return InputTextMultiline(label,value->data(),value->capacity()+1,size,flags|ImGuiInputTextFlags_CallbackResize,resizeString,value);
}
}
