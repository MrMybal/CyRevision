#include "CodeSyntaxInternal.h"
#include "ILexer.h"
#include "LexerModule.h"
#include <algorithm>
#include <cstring>
#include <memory>
#include <stdexcept>

namespace cyui { namespace detail {
// The lexer sees a bounded UTF-8 snapshot, never a live widget or application document.
class LexDocument final : public Scintilla::IDocument {
    std::string text_;
    std::vector<unsigned char> styles_;
    std::vector<Sci_Position> lines_{0};
    std::vector<int> states_, levels_;
    Sci_Position styled_ = 0;
    int error_ = 0;
    static bool trail(unsigned char c) { return (c & 0xc0) == 0x80; }
  public:
    explicit LexDocument(std::string_view source) : text_(source),styles_(source.size()) {
        for (std::size_t i=0;i<source.size();++i) {
            if (source[i]=='\r' && i+1<source.size() && source[i+1]=='\n') ++i;
            if (source[i]=='\r' || source[i]=='\n') lines_.push_back(Sci_Position(i+1));
        }
        states_.resize(lines_.size()+2);levels_.resize(lines_.size()+2,0x400);
    }
    int SCI_METHOD Version() const override { return Scintilla::dvRelease4; }
    void SCI_METHOD SetErrorStatus(int value) override { error_=value; }
    Sci_Position SCI_METHOD Length() const override { return Sci_Position(text_.size()); }
    void SCI_METHOD GetCharRange(char *out,Sci_Position at,Sci_Position size) const override {
        if (at<0 || size<0 || at>Length() || size>Length()-at) throw std::out_of_range("Lexilla source range");
        std::memcpy(out,text_.data()+at,std::size_t(size));
    }
    char SCI_METHOD StyleAt(Sci_Position at) const override { return at>=0 && at<Length() ? char(styles_[std::size_t(at)]) : 0; }
    Sci_Position SCI_METHOD LineFromPosition(Sci_Position at) const override {
        return std::max(Sci_Position(0),Sci_Position(std::upper_bound(lines_.begin(),lines_.end(),at)-lines_.begin())-1);
    }
    Sci_Position SCI_METHOD LineStart(Sci_Position line) const override {
        return line<0 ? 0 : std::size_t(line)<lines_.size() ? lines_[std::size_t(line)] : Length();
    }
    int SCI_METHOD GetLevel(Sci_Position line) const override { return line>=0 && std::size_t(line)<levels_.size() ? levels_[std::size_t(line)] : 0x400; }
    int SCI_METHOD SetLevel(Sci_Position line,int value) override {
        if (line<0 || std::size_t(line)>=levels_.size()) return 0x400;
        const auto before=levels_[std::size_t(line)];levels_[std::size_t(line)]=value;return before;
    }
    int SCI_METHOD GetLineState(Sci_Position line) const override { return line>=0 && std::size_t(line)<states_.size() ? states_[std::size_t(line)] : 0; }
    int SCI_METHOD SetLineState(Sci_Position line,int value) override {
        if (line<0 || std::size_t(line)>=states_.size()) return 0;
        const auto before=states_[std::size_t(line)];states_[std::size_t(line)]=value;return before;
    }
    void SCI_METHOD StartStyling(Sci_Position at) override { styled_=at; }
    bool SCI_METHOD SetStyleFor(Sci_Position size,char style) override {
        if (styled_<0 || size<0 || styled_>Length() || size>Length()-styled_) { error_=1;return false; }
        std::fill_n(styles_.begin()+styled_,size,static_cast<unsigned char>(style));styled_+=size;return true;
    }
    bool SCI_METHOD SetStyles(Sci_Position size,const char *styles) override {
        if (styled_<0 || size<0 || styled_>Length() || size>Length()-styled_) { error_=1;return false; }
        std::memcpy(styles_.data()+styled_,styles,std::size_t(size));styled_+=size;return true;
    }
    void SCI_METHOD DecorationSetCurrentIndicator(int) override {}
    void SCI_METHOD DecorationFillRange(Sci_Position,int,Sci_Position) override {}
    void SCI_METHOD ChangeLexerState(Sci_Position,Sci_Position) override {}
    int SCI_METHOD CodePage() const override { return 65001; }
    bool SCI_METHOD IsDBCSLeadByte(char) const override { return false; }
    const char *SCI_METHOD BufferPointer() override { return text_.c_str(); }
    int SCI_METHOD GetLineIndentation(Sci_Position line) override {
        int columns=0;
        for (auto at=LineStart(line);at<Length();++at) {
            if (text_[std::size_t(at)]==' ') ++columns;
            else if (text_[std::size_t(at)]=='\t') columns+=4-columns%4;
            else break;
        }return columns;
    }
    Sci_Position SCI_METHOD LineEnd(Sci_Position line) const override {
        auto end=LineStart(line+1),begin=LineStart(line);
        if (end>begin && text_[std::size_t(end-1)]=='\n') --end;
        if (end>begin && text_[std::size_t(end-1)]=='\r') --end;
        return end;
    }
    Sci_Position SCI_METHOD GetRelativePosition(Sci_Position at,Sci_Position characters) const override {
        while (characters<0) {
            if (at<=0) return -1;
            --at;while (at>0 && trail(static_cast<unsigned char>(text_[std::size_t(at)]))) --at;
            ++characters;
        }
        while (characters>0) {
            if (at>=Length()) return -1;
            Sci_Position width=1;GetCharacterAndWidth(at,&width);at+=width;--characters;
        }return at;
    }
    int SCI_METHOD GetCharacterAndWidth(Sci_Position at,Sci_Position *width) const override {
        if (width) *width=1;
        if (at<0 || at>=Length()) return 0;
        const auto lead=static_cast<unsigned char>(text_[std::size_t(at)]);
        int bytes=lead<0x80 ? 1 : lead>=0xc2 && lead<0xe0 ? 2 : lead<0xf0 ? 3 : lead<0xf5 ? 4 : 1;
        if (bytes>Length()-at) return lead;
        int code=lead & (bytes==1 ? 0xff : (1<<(7-bytes))-1);
        for (int i=1;i<bytes;++i) {
            const auto next=static_cast<unsigned char>(text_[std::size_t(at+i)]);
            if (!trail(next)) return lead;
            code=(code<<6)|(next&0x3f);
        }
        if (width) *width=bytes;return code;
    }
    std::vector<unsigned char> takeStyles() {
        if (error_) throw std::runtime_error("Lexilla rejected the source snapshot");
        return std::move(styles_);
    }
};
std::vector<unsigned char> lexSource(std::string_view text,const LanguageProfile &profile) {
    std::unique_ptr<Scintilla::ILexer5,void(*)(Scintilla::ILexer5*)> lexer(profile.module->Create(),
                                                                      [](auto *p) { if (p) p->Release(); });
    if (!lexer) throw std::runtime_error("Cannot create syntax lexer");
    for (const auto &property : profile.properties) lexer->PropertySet(property.first.c_str(),property.second.c_str());
    for (std::size_t i=0;i<profile.wordLists.size();++i) lexer->WordListSet(int(i),profile.wordLists[i].c_str());
    LexDocument snapshot(text);
    if (!text.empty()) lexer->Lex(0,Sci_Position(text.size()),0,&snapshot);
    return snapshot.takeStyles();
}
} }
