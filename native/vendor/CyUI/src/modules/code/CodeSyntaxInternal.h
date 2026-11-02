#pragma once
#include "cyui/modules/Code.h"
#include <array>
namespace Lexilla { class LexerModule; }
namespace cyui { namespace detail {
struct LanguageProfile {
    CodeLanguageInfo info;
    const Lexilla::LexerModule *module = nullptr;
    std::vector<std::string> wordLists;
    std::vector<std::pair<std::string,std::string>> properties;
    const std::array<const char*,256> *styleNames = nullptr;
    std::vector<std::string> functions;
};
const std::vector<LanguageProfile>& builtinProfiles();
std::vector<unsigned char> lexSource(std::string_view, const LanguageProfile&);
TokenRole styleRole(std::string_view);
} }
