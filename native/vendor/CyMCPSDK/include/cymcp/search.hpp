#pragma once
#include "process.hpp"
namespace cymcp {
struct FileSearchOptions {
    std::filesystem::path root, ripgrep;
    std::size_t page_size = 100, max_searches = 8, max_excerpt_bytes = 2048;
    std::chrono::milliseconds idle_ttl{60000};
};
// Optional ripgrep adapter, never bundled or installed implicitly. Regex uses rg's bounded Rust
// engine, never a shell or PCRE backtracking. Only the host's selected root is exposed.
class FileSearch {
    struct Impl;
    std::shared_ptr<Impl> impl_;

  public:
    explicit FileSearch(FileSearchOptions);
    Json grep(const Json& arguments, const CallContext& = {});
    Json read(const Json& arguments, const CallContext& = {});
    void add_to(Registry&, std::string prefix = "workspace");
};
} // namespace cymcp
