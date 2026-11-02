#include "CodeSyntaxInternal.h"
#include <algorithm>
#include <cctype>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <unordered_set>

namespace cyui { namespace {
std::string folded(std::string_view value) {
    std::string result(value);
    for (auto &c : result) if (static_cast<unsigned char>(c)<128) c=char(std::tolower(static_cast<unsigned char>(c)));
    return result;
}
const detail::LanguageProfile *builtin(CodeLanguage language) {
    const auto &items=detail::builtinProfiles();
    const auto found=std::find_if(items.begin(),items.end(),[language](const auto &p) { return p.info.language==language; });
    return found==items.end() ? nullptr : &*found;
}
struct Extension { CodeLanguageDefinition definition; detail::LanguageProfile profile; };
struct Registry {
    std::mutex mutex;
    std::map<CodeLanguage,std::shared_ptr<const Extension>> extensions;
    std::uint64_t revision=1;
};
Registry &registry() { static Registry value;return value; }
std::shared_ptr<const Extension> extension(CodeLanguage language) {
    auto &r=registry();std::lock_guard<std::mutex> lock(r.mutex);
    const auto found=r.extensions.find(language);return found==r.extensions.end() ? nullptr : found->second;
}
bool identifier(unsigned char c) { return c>=128 || std::isalnum(c) || c=='_'; }
void add(std::vector<CodeToken> &tokens,std::size_t start,std::size_t length,TokenRole role) {
    if (!length) return;
    if (!tokens.empty() && tokens.back().role==role && tokens.back().start+tokens.back().length==start)
        tokens.back().length+=length;
    else tokens.push_back({start,length,role});
}
void validate(std::string_view text,const std::vector<CodeToken> &tokens) {
    std::size_t end=0;
    auto boundary=[&](std::size_t at) { return at==text.size() || (static_cast<unsigned char>(text[at])&0xc0)!=0x80; };
    if (tokens.size()>text.size()+1) throw std::invalid_argument("Too many syntax tokens");
    for (const auto &t : tokens) {
        if (!t.length || t.start<end || t.start>text.size() || t.length>text.size()-t.start ||
            t.role<TokenRole::text || t.role>=TokenRole::count || !boundary(t.start) || !boundary(t.start+t.length))
            throw std::invalid_argument("Invalid syntax token range, role or UTF-8 boundary");
        end=t.start+t.length;
    }
}
// Whole-line levels are independent of a logger's timestamp/category prefix.
std::vector<CodeToken> logTokens(std::string_view text) {
    std::vector<CodeToken> result;
    for (std::size_t start=0;start<text.size();) {
        auto end=text.find_first_of("\r\n",start);if (end==text.npos) end=text.size();
        const auto line=folded(text.substr(start,end-start));
        TokenRole role=TokenRole::text;int priority=0;
        for (std::size_t at=0;at<line.size();) {
            // ANSI color prefixes must not turn ERROR into the identifier "merror".
            if (line[at]=='\x1b' && at+1<line.size() && line[at+1]=='[') {
                at+=2;while (at<line.size() && !(line[at]>=0x40 && line[at]<=0x7e)) ++at;
                if (at<line.size()) ++at;continue;
            }
            if (!identifier(static_cast<unsigned char>(line[at]))) { ++at;continue; }
            const auto wordStart=at++;
            while (at<line.size() && identifier(static_cast<unsigned char>(line[at]))) ++at;
            const auto word=std::string_view(line).substr(wordStart,at-wordStart);
            TokenRole candidate=TokenRole::text;int rank=0;
            if (word=="error" || word=="fatal" || word=="critical" || word=="panic" || word=="exception") { candidate=TokenRole::error;rank=3; }
            else if (word=="warning" || word=="warn") { candidate=TokenRole::warning;rank=2; }
            else if (word=="debug" || word=="trace" || word=="verbose" || word=="veryverbose") { candidate=TokenRole::comment;rank=1; }
            if (rank>priority) { role=candidate;priority=rank; }
            if (priority==3) break;
        }
        add(result,start,end-start,role);
        if (end<text.size()) {
            auto next=end+1;if (text[end]=='\r' && next<text.size() && text[next]=='\n') ++next;
            add(result,end,next-end,TokenRole::text);start=next;
        } else start=end;
    }
    return result;
}
std::vector<CodeToken> ruleTokens(std::string_view text,const CodeSyntaxRules &rules) {
    std::unordered_set<std::string> keywords,types,functions;
    auto key=[&](std::string_view word) { return rules.caseSensitive ? std::string(word) : folded(word); };
    for (const auto &word : rules.keywords) keywords.insert(key(word));
    for (const auto &word : rules.types) types.insert(key(word));
    for (const auto &word : rules.functions) functions.insert(key(word));
    auto matches=[&](std::size_t at,const std::string &token) { return !token.empty() && text.substr(at,token.size())==token; };
    std::vector<CodeToken> result;
    for (std::size_t at=0;at<text.size();) {
        const auto start=at;TokenRole role=TokenRole::text;
        if (matches(at,rules.lineComment)) {
            at=text.find_first_of("\r\n",at);if (at==text.npos) at=text.size();role=TokenRole::comment;
        } else if (matches(at,rules.blockCommentStart)) {
            const auto end=text.find(rules.blockCommentEnd,at+rules.blockCommentStart.size());
            at=end==text.npos ? text.size() : end+rules.blockCommentEnd.size();role=TokenRole::comment;
        } else if (rules.quotes.find(text[at])!=std::string::npos) {
            const char quote=text[at++];
            while (at<text.size()) {
                const char c=text[at++];
                if (c==rules.escape && rules.escape && at<text.size()) ++at;
                else if (c==quote) break;
            }role=TokenRole::string;
        } else if (std::isdigit(static_cast<unsigned char>(text[at]))) {
            ++at;while (at<text.size() && (identifier(static_cast<unsigned char>(text[at])) || text[at]=='.')) ++at;
            role=TokenRole::number;
        } else if (identifier(static_cast<unsigned char>(text[at]))) {
            ++at;while (at<text.size() && identifier(static_cast<unsigned char>(text[at]))) ++at;
            const auto word=key(text.substr(start,at-start));
            role=keywords.count(word) ? TokenRole::keyword : types.count(word) ? TokenRole::type :
                 functions.count(word) ? TokenRole::function : TokenRole::text;
        } else ++at;
        add(result,start,at-start,role);
    }return result;
}
} // namespace
namespace detail {
TokenRole styleRole(std::string_view name) {
    auto has=[&](std::string_view part) { return name.find(part)!=name.npos; };
    if (has("ESCSEQ_") || has("ERR_ES_")) {
        auto foreground=name.substr(name.find(has("ESCSEQ_") ? "ESCSEQ_" : "ERR_ES_")+(has("ESCSEQ_") ? 7 : 7));
        if (foreground.substr(0,5)=="BOLD_") foreground.remove_prefix(5);
        foreground=foreground.substr(0,foreground.find('_'));
        const char *names[]={"BLACK","RED","GREEN","YELLOW","BLUE","MAGENTA","CYAN","WHITE"};
        for (int i=0;i<8;++i) if (foreground==names[i]) return TokenRole(int(TokenRole::ansiBlack)+i);
        return TokenRole::text;
    }
    if (has("SCE_HEX_")) {
        if (has("UNKNOWN") || has("WRONG")) return TokenRole::error;
        return has("RECTYPE") || has("RECORDTYPE") ? TokenRole::keyword : TokenRole::number;
    }
    if (has("COMMENT") || has("_REM")) return TokenRole::comment;
    if (has("_DIFF_")) {
        if (has("DELETED")) return TokenRole::removed;
        if (has("ADDED")) return TokenRole::added;
        if (has("CHANGED")) return TokenRole::warning;
        if (has("HEADER") || has("POSITION") || has("COMMAND")) return TokenRole::heading;
    }
    if (has("_ERR_") && !has("DEFAULT")) return TokenRole::error;
    if (has("STRING") || has("CHARACTER") || has("_CHAR") || has("REGEX") || has("LITERAL")) return TokenRole::string;
    if (has("NUMBER") || has("NUMERIC") || has("INTEGER") || has("_REAL") || has("_FLOAT")) return TokenRole::number;
    if (has("PREPROC") || has("DIRECTIVE") || has("PRAGMA")) return TokenRole::preprocessor;
    if (has("WORD2") || has("GLOBALCLASS") || has("_TYPE") || has("_CLASS") || has("ATTRIBUTE") || (has("_KEY") && !has("KEYWORD"))) return TokenRole::type;
    if (has("KEYWORD") || has("_WORD") || has("RESERVED") || has("INSTRUCTION") || has("_TAG")) return TokenRole::keyword;
    if (has("FUNCTION") || has("COMMAND") || has("METHOD")) return TokenRole::function;
    if (has("SECTION") || has("HEADER") || has("TITLE")) return TokenRole::heading;
    return TokenRole::text;
}
} // namespace detail

std::vector<CodeLanguageInfo> codeLanguages() {
    std::vector<CodeLanguageInfo> result;
    for (const auto &p : detail::builtinProfiles()) result.push_back(p.info);
    auto &r=registry();std::lock_guard<std::mutex> lock(r.mutex);
    for (const auto &p : r.extensions) result.push_back(p.second->profile.info);
    return result;
}
CodeLanguageInfo codeLanguageInfo(CodeLanguage language) {
    if (const auto *p=builtin(language)) return p->info;
    if (const auto p=extension(language)) return p->profile.info;
    return builtin(CodeLanguage::plain)->info;
}
CodeLanguage codeLanguageFromName(std::string_view input) {
    const auto key=folded(input);
    for (const auto &p : codeLanguages()) if (folded(p.id)==key || folded(p.name)==key) return p.language;
    if (key=="ocaml") return CodeLanguage::caml;
    if (key=="bash") return CodeLanguage::shell;
    if (key=="yml") return CodeLanguage::yaml;
    return CodeLanguage::plain;
}
CodeLanguage codeLanguageForPath(std::string_view path) {
    const auto separator=path.find_last_of("/\\");
    const auto name=folded(path.substr(separator==path.npos ? 0 : separator+1));
    const auto dot=name.find_last_of('.');const auto suffix=dot==name.npos ? std::string() : name.substr(dot+1);
    auto all=codeLanguages();
    // Explicit plugin associations take precedence; removing the plugin restores built-ins.
    std::stable_sort(all.begin(),all.end(),[](const auto &a,const auto &b) { return !a.builtin && b.builtin; });
    for (const auto &p : all) for (const auto &file : p.filenames) if (folded(file)==name) return p.language;
    if (!suffix.empty()) for (const auto &p : all) for (const auto &ext : p.extensions) if (folded(ext)==suffix) return p.language;
    return CodeLanguage::plain;
}
std::uint64_t codeLanguageRevision() {
    auto &r=registry();std::lock_guard<std::mutex> lock(r.mutex);return r.revision;
}
CodeLanguage registerCodeLanguage(const CodeLanguageDefinition &definition) {
    if (definition.id.empty() || definition.id.size()>256 || definition.name.empty() || definition.name.size()>256 ||
        !std::all_of(definition.id.begin(),definition.id.end(),[](unsigned char c) { return c<128 && (std::isalnum(c) || c=='_' || c=='-' || c=='.' || c=='/'); }))
        throw std::invalid_argument("Invalid syntax plugin identity");
    const auto id=folded(definition.id);
    for (const auto &p : detail::builtinProfiles()) if (folded(p.info.id)==id) throw std::invalid_argument("Built-in syntax cannot be replaced");
    if (definition.extensions.size()>128 || definition.filenames.size()>128 || definition.wordLists.size()>9 || definition.properties.size()>128)
        throw std::length_error("Syntax plugin definition is too large");
    auto validAssociation=[](const std::string &s) { return !s.empty() && s.size()<=256 && s.find_first_of("/\\*?\r\n") == s.npos; };
    for (const auto &s : definition.extensions) if (!validAssociation(s) || s.front()=='.') throw std::invalid_argument("Extensions exclude the leading dot");
    for (const auto &s : definition.filenames) if (!validAssociation(s)) throw std::invalid_argument("Invalid syntax filename");
    for (const auto &s : definition.wordLists) if (s.size()>256*1024) throw std::length_error("Syntax keyword set exceeds 256 KiB");
    for (const auto &p : definition.properties) if (p.first.size()>256 || p.second.size()>4096) throw std::length_error("Syntax lexer property is too large");
    if (definition.rules) {
        const auto &rules=*definition.rules;
        if (rules.lineComment.size()>64 || rules.blockCommentStart.size()>64 || rules.blockCommentEnd.size()>64 || rules.quotes.size()>16 ||
            rules.blockCommentStart.empty()!=rules.blockCommentEnd.empty() || rules.keywords.size()+rules.types.size()+rules.functions.size()>50000)
            throw std::invalid_argument("Invalid declarative syntax rules");
        for (const auto *set : {&rules.keywords,&rules.types,&rules.functions}) for (const auto &word : *set)
            if (word.size()>256) throw std::length_error("Syntax keyword exceeds 256 bytes");
    }
    const auto *base=builtin(definition.base);
    if (!base) throw std::invalid_argument("Syntax base must be a built-in language");
    auto entry=std::make_shared<Extension>();entry->definition=definition;entry->profile=*base;
    std::uint32_t hash=2166136261u;for (unsigned char c : id) { hash^=c;hash*=16777619u; }
    const auto language=CodeLanguage(hash|0x80000000u);
    entry->profile.info={language,id,definition.name,definition.extensions,definition.filenames,false};
    entry->profile.wordLists.resize(std::max(entry->profile.wordLists.size(),definition.wordLists.size()));
    for (std::size_t i=0;i<definition.wordLists.size();++i) if (!definition.wordLists[i].empty()) entry->profile.wordLists[i]=definition.wordLists[i];
    for (const auto &p : definition.properties) {
        auto &settings=entry->profile.properties;
        const auto found=std::find_if(settings.begin(),settings.end(),[&](const auto &v) { return v.first==p.first; });
        if (found==settings.end()) settings.push_back(p);else found->second=p.second;
    }
    auto &r=registry();std::lock_guard<std::mutex> lock(r.mutex);
    const auto found=r.extensions.find(language);
    if (found!=r.extensions.end() && found->second->profile.info.id!=id) throw std::invalid_argument("Syntax plugin ID collision");
    r.extensions[language]=std::move(entry);++r.revision;return language;
}
bool unregisterCodeLanguage(std::string_view input) {
    const auto id=folded(input);auto &r=registry();std::lock_guard<std::mutex> lock(r.mutex);
    for (auto at=r.extensions.begin();at!=r.extensions.end();++at) if (at->second->profile.info.id==id) {
        r.extensions.erase(at);++r.revision;return true;
    }return false;
}
std::vector<CodeToken> highlightCode(std::string_view text,CodeLanguage language) {
    if (text.size()>32*1024*1024) throw std::length_error("Code source exceeds 32 MiB");
    const auto dynamic=extension(language);
    if (dynamic && dynamic->definition.highlight) {
        auto tokens=dynamic->definition.highlight(text);validate(text,tokens);return tokens;
    }
    if (dynamic && dynamic->definition.rules) return ruleTokens(text,*dynamic->definition.rules);
    if (language==CodeLanguage::log || (dynamic && dynamic->definition.base==CodeLanguage::log)) return logTokens(text);
    const auto *p=dynamic ? &dynamic->profile : builtin(language);
    if (!p || !p->module || language==CodeLanguage::plain || language==CodeLanguage::nfo) return text.empty() ? std::vector<CodeToken>{} : std::vector<CodeToken>{{0,text.size(),TokenRole::text}};
    auto styles=detail::lexSource(text,*p);
    std::unordered_set<std::string> functions(p->functions.begin(),p->functions.end());
    std::vector<CodeToken> tokens;
    for (std::size_t at=0;at<text.size();) {
        auto end=at+1;
        while (end<text.size() && styles[end]==styles[at]) ++end;
        // No lexer may split a UTF-8 code point into conflicting output spans.
        while (end<text.size() && (static_cast<unsigned char>(text[end])&0xc0)==0x80) ++end;
        const auto role=detail::styleRole((*p->styleNames)[styles[at]]);
        if (!functions.empty() && role==TokenRole::text) {
            for (std::size_t cursor=at;cursor<end;) {
                const auto start=cursor;
                if (identifier(static_cast<unsigned char>(text[cursor]))) {
                    ++cursor;while (cursor<end && identifier(static_cast<unsigned char>(text[cursor]))) ++cursor;
                    add(tokens,start,cursor-start,functions.count(std::string(text.substr(start,cursor-start))) ? TokenRole::function : role);
                } else { ++cursor;add(tokens,start,1,role); }
            }
        } else add(tokens,at,end-at,role);
        at=end;
    }
    validate(text,tokens);return tokens;
}
} // namespace cyui
