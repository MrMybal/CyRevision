#include "cyui/modules/CodeEditor.h"
#include <TextEditor.h>

namespace cyui {
struct CodeEditor::Impl {
    TextEditor editor;
    TextEditor::Language custom;
    bool changed = false;
    std::string original, baseline;
    bool crlf = false, syntaxEnabled = true;
    CodeLanguage language = CodeLanguage::plain;
    std::size_t parsedVersion = ~std::size_t(0);
    std::uint64_t languageRevision = 0;
};
CodeEditor::CodeEditor() : impl_(std::make_unique<Impl>()) {
    impl_->editor.SetInsertSpacesOnTabs(false);
    impl_->editor.SetTransactionCallback([this](const auto &) { impl_->changed = true; });
}
CodeEditor::~CodeEditor() = default;
void CodeEditor::setText(const std::string &value) {
    if (text() != value)
        impl_->editor.SetText(value);
    impl_->original = value;
    impl_->baseline = impl_->editor.GetText();
    impl_->crlf = value.find("\r\n") != std::string::npos;
    impl_->changed = false;
    impl_->parsedVersion = ~std::size_t(0);
}
std::string CodeEditor::text() const {
    const auto current = impl_->editor.GetText();
    if (current == impl_->baseline)
        return impl_->original;
    if (!impl_->crlf)
        return current;
    std::string result;
    for (char c : current) {
        if (c == '\n')
            result += '\r';
        result += c;
    }
    return result;
}
TextEditor &CodeEditor::native() {
    return impl_->editor;
}
void CodeEditor::setReadOnly(bool value) {
    impl_->editor.SetReadOnlyEnabled(value);
}
CodeLanguage CodeEditor::language() const { return impl_->language; }
void CodeEditor::setLanguageForPath(std::string_view path) { setLanguage(codeLanguageForPath(path)); }
void CodeEditor::setSyntaxEnabled(bool enabled) {
    impl_->syntaxEnabled=enabled;impl_->parsedVersion=~std::size_t(0);
    if (!enabled) impl_->editor.ClearSharedSyntaxTokens();
}
void CodeEditor::setLanguage(CodeLanguage language) {
    impl_->language=language;impl_->parsedVersion=~std::size_t(0);
    impl_->custom=*TextEditor::Language::Cpp();
    impl_->custom.name=codeLanguageInfo(language).name;
    impl_->custom.keywords.clear();impl_->custom.declarations.clear();impl_->custom.identifiers.clear();
    impl_->custom.preprocess=0;
    impl_->editor.SetLanguage(language==CodeLanguage::plain ? nullptr : &impl_->custom);
    impl_->editor.ClearSharedSyntaxTokens();
}
void CodeEditor::refreshSyntax() {
    if (!impl_->syntaxEnabled) return;
    const auto revision=impl_->editor.GetTextRevision();
    const auto languageRevision=codeLanguageRevision();
    if (impl_->parsedVersion==revision && impl_->languageRevision==languageRevision) return;
    const auto raw=impl_->editor.GetText();
    const auto tokens=highlightCode(raw,impl_->language);
    std::vector<TextEditor::SharedSyntaxToken> spans;
    using C=TextEditor::Color;
    auto nativeRole=[](TokenRole role) {
        switch (role) {
        case TokenRole::keyword:return C::keyword;
        case TokenRole::type:return C::declaration;
        case TokenRole::number:return C::number;
        case TokenRole::string:return C::string;
        case TokenRole::comment:return C::comment;
        case TokenRole::preprocessor:return C::preprocessor;
        case TokenRole::function:return C::sharedFunction;
        case TokenRole::added:return C::sharedAdded;
        case TokenRole::removed:return C::sharedRemoved;
        case TokenRole::heading:return C::sharedHeading;
        case TokenRole::warning:return C::sharedWarning;
        case TokenRole::error:return C::sharedError;
        default:
            if (role>=TokenRole::ansiBlack && role<=TokenRole::ansiWhite)
                return C(int(C::sharedAnsiBlack)+int(role)-int(TokenRole::ansiBlack));
            return C::text;
        }
    };
    std::size_t byte=0,line=0,glyph=0;
    auto advance=[&](std::size_t target) {
        while (byte<target) {
            const auto c=static_cast<unsigned char>(raw[byte++]);
            if (c=='\n') { ++line;glyph=0; }
            else if (c!='\r' && (c&0xc0)!=0x80) ++glyph;
        }
    };
    for (const auto &token : tokens) {
        advance(token.start);const auto end=token.start+token.length;
        while (byte<end) {
            const auto row=line,start=glyph;
            while (byte<end && raw[byte]!='\n') advance(byte+1);
            if (glyph>start) spans.push_back({row,start,glyph,nativeRole(token.role)});
            if (byte<end) advance(byte+1);
        }
    }
    impl_->editor.SetSharedSyntaxTokens(spans);impl_->parsedVersion=revision;impl_->languageRevision=languageRevision;
}
bool CodeEditor::draw(const char *id, ImVec2 size) {
    refreshSyntax();
    auto &p = impl_->editor;
    p.SetFindButtonLabel(trLabel("Find"));
    p.SetFindAllButtonLabel(trLabel("Find All"));
    p.SetReplaceButtonLabel(trLabel("Replace"));
    p.SetReplaceAllButtonLabel(trLabel("Replace All"));
    TextEditor::Palette colors;
    colors.fill(palette().text);
    using C = TextEditor::Color;
    auto put = [&](C kind, ImU32 color) { colors[std::size_t(kind)] = color; };
    put(C::keyword, tokenColor(TokenRole::keyword));
    put(C::declaration, tokenColor(TokenRole::type));
    put(C::number, tokenColor(TokenRole::number));
    put(C::string, tokenColor(TokenRole::string));
    put(C::preprocessor, tokenColor(TokenRole::preprocessor));
    put(C::comment, tokenColor(TokenRole::comment));
    put(C::knownIdentifier, tokenColor(TokenRole::type));
    put(C::sharedFunction, tokenColor(TokenRole::function));
    put(C::sharedAdded, tokenColor(TokenRole::added));put(C::sharedRemoved, tokenColor(TokenRole::removed));
    put(C::sharedHeading, tokenColor(TokenRole::heading));put(C::sharedWarning, tokenColor(TokenRole::warning));
    put(C::sharedError, tokenColor(TokenRole::error));
    for (int i=0;i<8;++i) put(C(int(C::sharedAnsiBlack)+i),tokenColor(TokenRole(int(TokenRole::ansiBlack)+i)));
    put(C::background, palette().bg0);
    put(C::cursor, palette().text);
    put(C::selection, palette().selection);
    put(C::whitespace, palette().textFaint);
    put(C::lineNumber, palette().textFaint);
    put(C::currentLineNumber, palette().accent);
    put(C::currentLineHighlight, alpha(palette().accent, .08f));
    put(C::currentLineHighlightBorder, palette().line);
    put(C::matchingBracketBackground, palette().bg3);
    put(C::matchingBracketActive, palette().accent);
    put(C::matchingBracketLevel1, palette().info);
    put(C::matchingBracketLevel2, palette().success);
    put(C::matchingBracketLevel3, palette().warning);
    put(C::matchingBracketError, palette().error);
    p.SetPalette(colors);
    ScopedFont font(monoFont());
    p.Render(id, size);
    const bool changed = impl_->changed;
    impl_->changed = false;
    return changed;
}
} // namespace cyui
