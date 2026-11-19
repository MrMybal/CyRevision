#include <algorithm>
#include <cstdlib>
#include <cymcp/search.hpp>
#include <fstream>

namespace cymcp {
namespace {
std::filesystem::path find_rg() {
    const auto* raw = std::getenv("PATH");
    if (!raw)
        return {};
#ifdef _WIN32
    constexpr char separator = ';';
    constexpr auto name = "rg.exe";
#else
    constexpr char separator = ':';
    constexpr auto name = "rg";
#endif
    const std::string paths = raw;
    std::size_t first = 0;
    while (first <= paths.size()) {
        const auto next = paths.find(separator, first);
        auto folder = paths.substr(first, next - first);
        if (!folder.empty()) {
            auto candidate = std::filesystem::u8path(folder) / name;
            if (std::filesystem::is_regular_file(candidate))
                return std::filesystem::absolute(candidate);
        }
        if (next == std::string::npos)
            break;
        first = next + 1;
    }
    return {};
}
std::string excerpt(std::string text, std::size_t limit) {
    auto end = std::min(limit, text.size());
    while (end && end < text.size() && (static_cast<unsigned char>(text[end]) & 0xc0) == 0x80)
        --end;
    text.resize(end);
    return text;
}
} // namespace
struct FileSearch::Impl {
    struct Search {
        std::unique_ptr<Process> process;
        std::string owner;
        Cancellation lifetime;
        Clock::time_point expires;
        bool failed = false;
        std::mutex mutex;
    };
    FileSearchOptions options;
    std::filesystem::path root;
    std::mutex mutex;
    std::map<std::string, std::shared_ptr<Search>> searches;
    std::condition_variable_any wake;
    std::jthread janitor;
    explicit Impl(FileSearchOptions o) : options(std::move(o)) {
        if (options.root.empty() || !std::filesystem::is_directory(options.root) ||
            !options.page_size || options.page_size > 1000 || !options.max_searches ||
            options.max_searches > 64 || options.max_excerpt_bytes < 4 ||
            options.max_excerpt_bytes > 65536 || options.idle_ttl.count() <= 0)
            throw Error("InvalidOptions", "Invalid file-search root or limits");
        root = std::filesystem::canonical(options.root);
        if (options.ripgrep.empty())
            options.ripgrep = find_rg();
        janitor = std::jthread([this](std::stop_token stop) {
            std::unique_lock lock(mutex);
            while (!stop.stop_requested()) {
                for (auto it = searches.begin(); it != searches.end();) {
                    if (it->second->expires <= Clock::now()) {
                        auto expired = it->second;
                        std::unique_lock active(expired->mutex, std::try_to_lock);
                        if (!active.owns_lock()) {
                            ++it;
                            continue;
                        }
                        expired->lifetime.cancel();
                        it = searches.erase(it);
                    } else
                        ++it;
                }
                wake.wait_for(lock, stop, std::chrono::milliseconds(250), [] { return false; });
            }
        });
    }
    ~Impl() {
        janitor.request_stop();
        wake.notify_all();
        janitor.join();
        for (auto& [id, search] : searches)
            search->lifetime.cancel();
    }
    std::filesystem::path resolve(const std::string& relative) const {
        auto path = std::filesystem::u8path(relative);
        if (path.empty() || path.is_absolute() || path.has_root_name() ||
            relative.find(':') != std::string::npos)
            throw Error("UnsafePath", "A relative workspace file is required");
        auto current = root;
        for (const auto& part : path) {
            if (part == "..")
                throw Error("UnsafePath", "Workspace traversal is not allowed");
            current /= part;
            if (std::filesystem::is_symlink(std::filesystem::symlink_status(current)))
                throw Error("UnsafePath", "Workspace links are not followed");
        }
        const auto resolved = std::filesystem::canonical(current);
        auto inside = resolved.lexically_relative(root);
        if (inside.empty() || *inside.begin() == ".." ||
            !std::filesystem::is_regular_file(resolved))
            throw Error("UnsafePath", "File is outside the selected workspace");
        return resolved;
    }
    Json grep(const Json& args, const CallContext& context) {
        context.cancellation.check();
        std::string cursor;
        std::shared_ptr<Search> search;
        if (args.contains("cursor")) {
            if (args.size() != 1)
                throw Error("InvalidParameters", "Continue a grep with its cursor only");
            cursor = args.at("cursor").get<std::string>();
            std::lock_guard lock(mutex);
            auto found = searches.find(cursor);
            if (found == searches.end() || found->second->owner != context.principal ||
                found->second->expires <= Clock::now())
                throw Error("SearchExpired", "Search cursor is unavailable or expired");
            search = found->second;
            search->expires = Clock::now() + options.idle_ttl;
        } else {
            const auto pattern = args.at("pattern").get<std::string>();
            if (pattern.empty() || pattern.size() > 4096)
                throw Error("InvalidParameters", "Grep pattern must contain 1..4096 bytes");
            if (options.ripgrep.empty())
                throw Error("ExecutableMissing",
                            "Install ripgrep or configure FileSearchOptions.ripgrep");
            search = std::make_shared<Search>();
            search->owner = context.principal;
            search->expires = Clock::now() + options.idle_ttl;
            ProcessOptions process;
            process.executable = options.ripgrep;
            process.cwd = root;
            process.arguments = {"--json",  "--no-config", "--engine",
                                 "default", "--no-follow", "--hidden",
                                 "--color", "never",       "--line-number"};
            if (args.value("include_ignored", false))
                process.arguments.push_back("--no-ignore");
            if (!args.value("regex", true))
                process.arguments.push_back("--fixed-strings");
            if (args.value("ignore_case", false))
                process.arguments.push_back("--ignore-case");
            if (args.contains("glob")) {
                auto glob = args.at("glob").get<std::string>();
                if (glob.size() > 1024)
                    throw Error("InvalidParameters", "Glob exceeds limit");
                process.arguments.insert(process.arguments.end(), {"--glob", glob});
            }
            process.arguments.insert(
                process.arguments.end(),
                {"--glob", "!**/.git/**", "--glob", "!.git/**", "--regexp", pattern, "--", "."});
            process.max_queue_bytes = 2 * 1024 * 1024;
            process.max_line_bytes = 8 * 1024 * 1024;
            {
                std::lock_guard lock(mutex);
                if (searches.size() >= options.max_searches)
                    throw Error("Busy", "Too many active workspace searches");
                search->process = std::make_unique<Process>(std::move(process));
                cursor = random_token(32);
                searches[cursor] = search;
            }
        }
        std::unique_lock operation(search->mutex, std::try_to_lock);
        if (!operation.owns_lock())
            throw Error("Busy", "This grep cursor is already in use");
        auto cancellation = search->lifetime.child();
        cancellation.ancestors.push_back(context.cancellation.flag);
        cancellation.ancestors.insert(cancellation.ancestors.end(),
                                      context.cancellation.ancestors.begin(),
                                      context.cancellation.ancestors.end());
        cancellation.deadline = context.cancellation.deadline;
        Json rows = Json::array();
        std::size_t skipped_non_utf8 = 0;
        bool done = false;
        try {
            ProcessLine line;
            while (rows.size() < options.page_size) {
                if (!search->process->next(line, cancellation)) {
                    done = true;
                    break;
                }
                if (line.stderr_stream) {
                    search->failed = true;
                    continue;
                }
                const auto record = parse_json(line.text, 8 * 1024 * 1024);
                if (record.value("type", std::string()) != "match")
                    continue;
                const auto& data = record.at("data");
                if (!data.at("path").contains("text") || !data.at("lines").contains("text")) {
                    ++skipped_non_utf8;
                    continue;
                }
                const auto relative = data.at("path").at("text").get<std::string>();
                auto file = resolve(relative);
                auto text = data.at("lines").at("text").get<std::string>();
                const auto bytes = text.size();
                rows.push_back({{"path", path_utf8(file.lexically_relative(root))},
                                {"line", data.at("line_number")},
                                {"byteOffset", data.at("absolute_offset")},
                                {"text", excerpt(std::move(text), options.max_excerpt_bytes)},
                                {"truncated", bytes > options.max_excerpt_bytes},
                                {"lineBytes", bytes}});
            }
            if (done) {
                const auto exit = search->process->wait(cancellation);
                if (search->failed || exit > 1)
                    throw Error("SearchError",
                                "ripgrep rejected the pattern or could not read a workspace file");
            }
        } catch (...) {
            search->lifetime.cancel();
            std::lock_guard lock(mutex);
            searches.erase(cursor);
            throw;
        }
        {
            std::lock_guard lock(mutex);
            searches.erase(cursor);
            if (!done) {
                search->expires = Clock::now() + options.idle_ttl;
                cursor = random_token(32);
                searches[cursor] = search;
            }
        }
        Json result = {{"matches", rows}, {"complete", done}, {"skippedNonUtf8", skipped_non_utf8}};
        if (!done)
            result["nextCursor"] = cursor;
        return result;
    }
    Json read(const Json& args, const CallContext& context) {
        context.cancellation.check();
        const auto file = resolve(args.at("path").get<std::string>());
        const auto offset = args.value("offset", std::size_t(0));
        const auto length = args.value("length", std::size_t(8192));
        if (!length || length > 65536)
            throw Error("InvalidParameters", "Read length must be 1..65536 bytes");
        const auto size = std::filesystem::file_size(file);
        if (offset > size)
            throw Error("InvalidParameters", "Read offset outside file");
        const auto modified = std::filesystem::last_write_time(file).time_since_epoch().count();
        const auto revision = sha256(std::to_string(size) + ":" + std::to_string(modified));
        if (args.contains("revision") && args.at("revision") != revision)
            throw Error("ResourceChanged", "Workspace file changed; restart reading");
        std::ifstream input(file, std::ios::binary);
        input.seekg(static_cast<std::streamoff>(offset));
        std::string text(std::min<std::uintmax_t>(length + 4, size - offset), '\0');
        input.read(text.data(), static_cast<std::streamsize>(text.size()));
        if (static_cast<std::size_t>(input.gcount()) != text.size())
            throw Error("ResourceChanged", "Workspace file changed while reading");
        if (text.find('\0') != std::string::npos)
            throw Error("UnsupportedContent", "Workspace read accepts text files only");
        if (!text.empty() && (static_cast<unsigned char>(text.front()) & 0xc0) == 0x80)
            throw Error("InvalidParameters", "Read offset splits UTF-8");
        auto keep = std::min(length, text.size());
        while (keep && keep < text.size() &&
               (static_cast<unsigned char>(text[keep]) & 0xc0) == 0x80)
            --keep;
        if (!keep && !text.empty())
            throw Error("InvalidParameters", "Read length is too short for this UTF-8 character");
        text.resize(keep);
        try {
            (void)Json(text).dump();
        } catch (const Json::exception&) {
            throw Error("UnsupportedContent", "Workspace text must be UTF-8");
        }
        if (std::filesystem::file_size(file) != size ||
            std::filesystem::last_write_time(file).time_since_epoch().count() != modified)
            throw Error("ResourceChanged", "Workspace file changed while reading");
        context.cancellation.check();
        Json result = {{"path", path_utf8(file.lexically_relative(root))},
                       {"text", text},
                       {"offset", offset},
                       {"size", size},
                       {"revision", revision},
                       {"complete", offset + keep == size}};
        if (offset + keep < size)
            result["nextOffset"] = offset + keep;
        return result;
    }
};
FileSearch::FileSearch(FileSearchOptions options)
    : impl_(std::make_shared<Impl>(std::move(options))) {}
Json FileSearch::grep(const Json& args, const CallContext& context) {
    return impl_->grep(args, context);
}
Json FileSearch::read(const Json& args, const CallContext& context) {
    return impl_->read(args, context);
}
void FileSearch::add_to(Registry& registry, std::string prefix) {
    registry.add(
        {prefix + ".grep",
         "Search workspace text with ripgrep. Supports regex, literal mode, ignore_case, glob and "
         "paged cursors. Continue with cursor only. Paths are relative to the host-selected root.",
         {{"type", "object"},
          {"properties",
           {{"pattern", {{"type", "string"}}},
            {"regex", {{"type", "boolean"}}},
            {"ignore_case", {{"type", "boolean"}}},
            {"include_ignored", {{"type", "boolean"}}},
            {"glob", {{"type", "string"}}},
            {"cursor", {{"type", "string"}}}}},
          {"additionalProperties", false}},
         Json::object(),
         true,
         [p = impl_](const Json& a, const CallContext& c) { return p->grep(a, c); }});
    registry.add({prefix + ".read",
                  "Read a bounded UTF-8 workspace file range. Follow nextOffset and retain "
                  "revision to detect changes; no file writes.",
                  {{"type", "object"},
                   {"properties",
                    {{"path", {{"type", "string"}}},
                     {"offset", {{"type", "integer"}, {"minimum", 0}}},
                     {"length", {{"type", "integer"}, {"minimum", 1}, {"maximum", 65536}}},
                     {"revision", {{"type", "string"}}}}},
                   {"required", {"path"}},
                   {"additionalProperties", false}},
                  Json::object(),
                  true,
                  [p = impl_](const Json& a, const CallContext& c) { return p->read(a, c); }});
}
} // namespace cymcp
