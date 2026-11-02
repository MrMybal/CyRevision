#pragma once
#include "cyui/Export.h"
#include <string>
#include <unordered_map>
#include <vector>

namespace cyui {
struct LocaleInfo {
    std::string code, name;
};
using TranslationCatalog = std::unordered_map<std::string, std::string>;
// English is the default. French is built in. Regional tags fall back to their
// registered language (fr-CA -> fr). Unknown languages leave the locale unchanged.
CYUI_API bool setLocale(const char* code);
CYUI_API const std::string& locale();
CYUI_API std::vector<LocaleInfo> availableLocales();
// Merge host strings or add another language. Call outside concurrent rendering.
CYUI_API bool registerTranslations(const char* code, const TranslationCatalog&, const char* name = nullptr);
// English messages can be keys; applications may also use namespaced keys with
// an explicit English fallback. Missing translations always keep readable text.
CYUI_API std::string tr(const char* key, const char* englishFallback = nullptr);
// For interactive labels/window titles: translated text plus a stable ImGui ID.
// The identity is based on the English label and survives a live language change.
CYUI_API std::string trLabel(const char* key, const char* englishFallback = nullptr);
} // namespace cyui
