#include "base64.hpp"
#include <algorithm>
#include <cctype>
#include <charconv>
#include <cymcp/changes.hpp>
#include <cymcp/performance.hpp>
#include <cymcp/resources.hpp>
#include <fstream>
#include <limits>

namespace cymcp {
namespace {
constexpr auto chunk_prefix = "cymcp://chunk/";
std::string preview(std::string_view text, std::size_t limit = 240) {
    const auto bound = std::min(text.size(), limit);
    std::size_t end = 0;
    while (end < bound) {
        const auto lead = static_cast<unsigned char>(text[end]);
        const std::size_t width = lead < 128              ? 1
                                  : (lead & 0xe0) == 0xc0 ? 2
                                  : (lead & 0xf0) == 0xe0 ? 3
                                  : (lead & 0xf8) == 0xf0 ? 4
                                                          : 1;
        if (width > bound - end)
            break;
        end += width;
    }
    return std::string(text.substr(0, end));
}
std::size_t number(std::string_view text) {
    std::size_t result = 0;
    const auto value = std::from_chars(text.data(), text.data() + text.size(), result);
    if (text.empty() || value.ec != std::errc{} || value.ptr != text.data() + text.size())
        throw Error("InvalidParameters", "Invalid cursor or resource offset");
    return result;
}
std::string lower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
        return static_cast<char>(c >= 'A' && c <= 'Z' ? c + ('a' - 'A') : c);
    });
    return value;
}
struct DiskRoot {
    std::filesystem::path path;
    std::mutex mutex;
    std::size_t used = 0;
    ~DiskRoot() {
        std::error_code error;
        std::filesystem::remove(path, error);
    }
};
struct DiskData {
    std::shared_ptr<DiskRoot> root;
    std::filesystem::path path;
    std::size_t reserved = 0;
    ~DiskData() {
        std::error_code error;
        std::filesystem::remove(path, error);
        if (root) {
            std::lock_guard lock(root->mutex);
            root->used -= reserved;
        }
    }
};
class StoreBuffer : public std::streambuf {
    ResourceOptions options;
    std::shared_ptr<DiskRoot> root;
    const CallContext& context;
    PrivateOutputFile file{nullptr, &std::fclose};
    std::function<void(std::size_t)> reserve_disk;

  public:
    std::string memory;
    std::shared_ptr<DiskData> disk;
    std::size_t bytes = 0;
    StoreBuffer(ResourceOptions o, std::shared_ptr<DiskRoot> r, const CallContext& c,
                std::function<void(std::size_t)> reserve)
        : options(std::move(o)), root(std::move(r)), context(c), reserve_disk(std::move(reserve)) {}
    ~StoreBuffer() {
        file.reset();
        disk.reset();
    }
    std::streamsize xsputn(const char* data, std::streamsize count) override {
        context.cancellation.check();
        const auto n = static_cast<std::size_t>(count);
        const auto limit = root ? std::max(options.max_disk_bytes, options.max_stored_bytes)
                                : options.max_stored_bytes;
        if (n > limit || bytes > limit - n)
            throw Error("ResourceTooLarge", "Result exceeds storage budget");
        if (root && !disk &&
            bytes + n > std::min(options.spool_threshold, options.max_stored_bytes)) {
            disk = std::make_shared<DiskData>();
            disk->root = root;
            reserve_disk(memory.size());
            disk->reserved = memory.size();
            disk->path = root->path / random_token(32);
            file = open_private_output_file(disk->path);
            if (std::fwrite(memory.data(), 1, memory.size(), file.get()) != memory.size())
                throw Error("FileError", "Cannot spool result");
            std::string().swap(memory);
        }
        if (disk) {
            reserve_disk(n);
            disk->reserved += n;
            if (std::fwrite(data, 1, n, file.get()) != n)
                throw Error("FileError", "Cannot spool result");
        } else
            memory.append(data, n);
        bytes += n;
        return count;
    }
    int_type overflow(int_type value) override {
        if (traits_type::eq_int_type(value, traits_type::eof()))
            return traits_type::not_eof(value);
        const char c = traits_type::to_char_type(value);
        xsputn(&c, 1);
        return value;
    }
    void finish() {
        if (file) {
            if (std::fflush(file.get()))
                throw Error("FileError", "Cannot flush result");
            file.reset();
        }
        context.cancellation.check();
    }
};
bool exceeds(const Json& value, std::size_t limit) {
    struct Limit {};
    struct Buffer : std::streambuf {
        std::size_t remaining;
        explicit Buffer(std::size_t n) : remaining(n) {}
        std::streamsize xsputn(const char*, std::streamsize n) override {
            if (static_cast<std::size_t>(n) > remaining)
                throw Limit{};
            remaining -= static_cast<std::size_t>(n);
            return n;
        }
        int_type overflow(int_type c) override {
            if (!traits_type::eq_int_type(c, traits_type::eof())) {
                if (!remaining)
                    throw Limit{};
                --remaining;
            }
            return traits_type::not_eof(c);
        }
    } buffer(limit);
    std::ostream out(&buffer);
    out.exceptions(std::ios::badbit | std::ios::failbit);
    try {
        out << value;
        return false;
    } catch (const Limit&) {
        return true;
    }
}
} // namespace
struct ResourceRegistry::Impl {
    struct Entry {
        Resource resource;
        std::string token, owner;
        bool stored = false, on_disk = false;
        Clock::time_point expires = Clock::time_point::max();
        std::uint64_t used = 0;
    };
    Policy policy;
    ResourceOptions options;
    mutable std::mutex mutex;
    std::map<std::string, std::shared_ptr<Entry>> public_resources, stored, chunks;
    std::size_t bytes = 0;
    std::size_t disk_bytes = 0;
    std::shared_ptr<DiskRoot> disk_root;
    std::shared_ptr<ChangeFeed> changes;
    std::condition_variable_any wake;
    std::jthread janitor;
    std::uint64_t revision = 0, tick = 0;
    struct Grep {
        std::string text, owner, tail;
        bool ignore_case = false, skip_line = false;
        std::uint64_t revision = 0;
        std::size_t index = 0, offset = 0, line = 1, line_start = 0;
        Clock::time_point expires;
    };
    std::map<std::string, Grep> greps;
    Impl(Policy p, ResourceOptions o) : policy(std::move(p)), options(o) {
        if (o.chunk_bytes < 4 || o.chunk_bytes > 4 * 1024 * 1024 || !o.max_stored_results ||
            !o.max_stored_bytes || o.result_ttl.count() <= 0)
            throw Error("InvalidOptions", "Invalid resource storage limits");
        if (!o.disk_directory.empty()) {
            if (!o.max_disk_bytes || !o.spool_threshold)
                throw Error("InvalidOptions", "Invalid disk storage limits");
            disk_root = std::make_shared<DiskRoot>();
            disk_root->path =
                std::filesystem::absolute(o.disk_directory) / ("cymcp-results-" + random_token(16));
            private_directory(disk_root->path);
            janitor = std::jthread([this](std::stop_token stop) {
                std::unique_lock lock(mutex);
                while (!stop.stop_requested()) {
                    wake.wait_for(lock, stop, std::chrono::seconds(1), [] { return false; });
                    prune();
                }
            });
        }
    }
    ~Impl() {
        if (janitor.joinable()) {
            janitor.request_stop();
            wake.notify_all();
            janitor.join();
        }
    }
    void erase_stored(decltype(stored)::iterator it) {
        (it->second->on_disk ? disk_bytes : bytes) -= it->second->resource.size;
        chunks.erase(it->second->token);
        stored.erase(it);
    }
    void prune() {
        for (auto it = stored.begin(); it != stored.end();)
            if (it->second->expires <= Clock::now()) {
                auto old = it++;
                erase_stored(old);
            } else
                ++it;
    }
    void reserve_disk(std::size_t count) {
        std::lock_guard lock(mutex);
        prune();
        for (;;) {
            {
                std::lock_guard quota(disk_root->mutex);
                if (count <= options.max_disk_bytes &&
                    disk_root->used <= options.max_disk_bytes - count) {
                    disk_root->used += count;
                    return;
                }
            }
            auto oldest = stored.end();
            for (auto it = stored.begin(); it != stored.end(); ++it)
                if (it->second->on_disk &&
                    (oldest == stored.end() || it->second->used < oldest->second->used))
                    oldest = it;
            if (oldest == stored.end())
                throw Error("ResourceTooLarge",
                            "Disk result budget is occupied by active streams or readers");
            erase_stored(oldest);
        }
    }
    void add(Resource resource, bool replace) {
        if (resource.uri.empty() || resource.uri.size() > 4096 ||
            resource.uri.find(':') == std::string::npos || resource.uri.starts_with("cymcp:") ||
            resource.uri.find_first_of("\r\n\0", 0, 3) != std::string::npos ||
            resource.name.empty() || resource.name.size() > 256 ||
            resource.description.size() > 16384 || resource.mime_type.empty() ||
            resource.mime_type.size() > 256 || !resource.read || resource.cache_ttl.count() < 0)
            throw Error("InvalidResource",
                        "Resource needs a URI, name, MIME type and bounded reader");
        auto entry = std::make_shared<Entry>();
        entry->resource = std::move(resource);
        entry->token = random_token(32);
        std::unique_lock lock(mutex);
        auto found = public_resources.find(entry->resource.uri);
        if (found != public_resources.end()) {
            if (!replace)
                throw Error("DuplicateResource", "Resource already registered");
            chunks.erase(found->second->token);
        }
        chunks[entry->token] = entry;
        public_resources[entry->resource.uri] = entry;
        ++revision;
        greps.clear();
        auto feed = changes;
        auto uri = entry->resource.uri;
        lock.unlock();
        if (feed) {
            feed->publish("resources");
            feed->publish("resource", uri);
        }
    }
};
ResourceRegistry::ResourceRegistry(Policy policy, ResourceOptions options)
    : impl_(std::make_unique<Impl>(std::move(policy), options)) {}
ResourceRegistry::~ResourceRegistry() = default;
void ResourceRegistry::add(Resource resource) {
    impl_->add(std::move(resource), false);
}
void ResourceRegistry::replace(Resource resource) {
    impl_->add(std::move(resource), true);
}
void ResourceRegistry::remove(const std::string& uri) {
    auto& p = *impl_;
    std::unique_lock lock(p.mutex);
    auto found = p.public_resources.find(uri);
    if (found != p.public_resources.end()) {
        p.chunks.erase(found->second->token);
        p.public_resources.erase(found);
        ++p.revision;
        p.greps.clear();
        auto feed = p.changes;
        lock.unlock();
        if (feed) {
            feed->publish("resources");
            feed->publish("resource", uri);
        }
    }
}
void ResourceRegistry::set_change_feed(std::shared_ptr<ChangeFeed> feed) {
    std::lock_guard lock(impl_->mutex);
    impl_->changes = std::move(feed);
}
bool ResourceRegistry::readable(const std::string& uri, const CallContext& c) const {
    c.cancellation.check();
    if (!impl_->policy.allows(c, "resources.read", {{"uri", uri}}))
        return false;
    std::lock_guard lock(impl_->mutex);
    impl_->prune();
    if (impl_->public_resources.contains(uri))
        return true;
    auto found = impl_->stored.find(uri);
    return found != impl_->stored.end() && found->second->owner == c.principal;
}
std::uint64_t ResourceRegistry::revision() const {
    std::lock_guard lock(impl_->mutex);
    return impl_->revision;
}
Json ResourceRegistry::list(std::size_t offset, std::size_t limit) const {
    if (!limit || limit > 1000)
        throw Error("InvalidParameters", "Invalid resource page size");
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (offset > p.public_resources.size())
        throw Error("InvalidParameters", "Invalid resource cursor");
    Json result = Json::array();
    auto it = p.public_resources.begin();
    std::advance(it, offset);
    for (std::size_t n = 0; it != p.public_resources.end() && n < limit; ++it, ++n) {
        const auto& r = it->second->resource;
        const bool manifest = r.size > p.options.chunk_bytes;
        Json item = {{"uri", r.uri},
                     {"name", r.name},
                     {"description", r.description},
                     {"mimeType", manifest ? "application/json" : r.mime_type}};
        if (!manifest)
            item["size"] = r.size;
        result.push_back(std::move(item));
    }
    return {{"resources", result}, {"total", p.public_resources.size()}, {"revision", p.revision}};
}
Json ResourceRegistry::read(const std::string& uri, const CallContext& context) const {
    context.cancellation.check();
    auto& p = *impl_;
    if (!p.policy.allows(context, "resources.read", {{"uri", uri}}))
        throw Error("AccessDenied", "Resource read denied");
    std::shared_ptr<Impl::Entry> entry;
    std::size_t offset = 0;
    bool chunk = false;
    {
        std::lock_guard lock(p.mutex);
        p.prune();
        auto known = p.public_resources.find(uri);
        if (known != p.public_resources.end())
            entry = known->second;
        else if (auto stored = p.stored.find(uri); stored != p.stored.end())
            entry = stored->second;
        else if (uri.starts_with(chunk_prefix)) {
            const auto tail =
                std::string_view(uri).substr(std::char_traits<char>::length(chunk_prefix));
            const auto slash = tail.find('/');
            if (slash == std::string_view::npos)
                throw Error("InvalidParameters", "Invalid chunk URI");
            auto found = p.chunks.find(std::string(tail.substr(0, slash)));
            if (found != p.chunks.end())
                entry = found->second;
            offset = number(tail.substr(slash + 1));
            chunk = true;
        }
        if (!entry || (entry->stored && entry->owner != context.principal))
            throw Error("ResourceNotFound", "Resource is unavailable or expired");
        entry->used = ++p.tick;
    }
    const auto& r = entry->resource;
    if (offset > r.size || (offset == r.size && offset != 0))
        throw Error("InvalidParameters", "Resource offset outside content");
    auto ttl = r.cache_ttl.count();
    if (entry->stored)
        ttl = std::min<std::int64_t>(
            ttl, std::max<std::int64_t>(0, std::chrono::duration_cast<std::chrono::milliseconds>(
                                               entry->expires - Clock::now())
                                               .count()));
    const auto chunk_uri = [&](std::size_t n) {
        return std::string(chunk_prefix) + entry->token + "/" + std::to_string(n);
    };
    Json content = {{"uri", uri}};
    Json result = {{"ttlMs", ttl}, {"cacheScope", "private"}};
    if (!chunk && r.size > p.options.chunk_bytes) {
        content["mimeType"] = "application/json";
        content["text"] = Json({{"type", "cymcp.resource.v1"},
                                {"size", r.size},
                                {"mimeType", r.mime_type},
                                {"binary", r.binary},
                                {"chunkBytes", p.options.chunk_bytes},
                                {"firstUri", chunk_uri(0)}})
                              .dump();
    } else {
        const auto length = std::min(p.options.chunk_bytes, r.size - offset);
        const auto requested = std::min(r.size - offset, length + (r.binary ? 0 : 4));
        auto data = r.read(offset, requested, context);
        context.cancellation.check();
        if (data.size() != requested)
            throw Error("ResourceChanged", "Resource reader returned an inconsistent snapshot");
        auto keep = length;
        if (!r.binary) {
            if (!data.empty() && (static_cast<unsigned char>(data.front()) & 0xc0) == 0x80)
                throw Error("InvalidParameters", "Resource offset splits a UTF-8 character");
            while (keep && keep < data.size() &&
                   (static_cast<unsigned char>(data[keep]) & 0xc0) == 0x80)
                --keep;
        }
        data.resize(keep);
        content["mimeType"] = r.mime_type;
        if (r.binary)
            content["blob"] = detail::base64(data);
        else {
            try {
                (void)Json(data).dump();
            } catch (const Json::exception&) {
                throw Error("InvalidResource", "Resource text must be UTF-8");
            }
            content["text"] = std::move(data);
        }
        Json page = {{"offset", offset},
                     {"bytes", keep},
                     {"totalBytes", r.size},
                     {"eof", offset + keep == r.size}};
        if (offset + keep < r.size)
            page["nextUri"] = chunk_uri(offset + keep);
        result["_meta"] = {{"com.cymcp/chunk", page}};
    }
    result["contents"] = Json::array({std::move(content)});
    return result;
}
Json ResourceRegistry::store(std::string bytes, std::string mime_type, const CallContext& context,
                             bool binary) {
    return store_stream(
        [&bytes](std::ostream& out, const auto&) {
            out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        },
        std::move(mime_type), context, binary);
}
Json ResourceRegistry::store_json(const Json& value, const CallContext& context) {
    return store_stream([&value](std::ostream& out, const auto&) { out << value; },
                        "application/json", context);
}
Json ResourceRegistry::store_stream(
    const std::function<void(std::ostream&, const CallContext&)>& producer, std::string mime_type,
    const CallContext& context, bool binary) {
    context.cancellation.check();
    auto& p = *impl_;
    if (!producer || mime_type.empty() || mime_type.size() > 256)
        throw Error("InvalidParameters", "Invalid stream producer or MIME type");
    StoreBuffer buffer(p.options, p.disk_root, context, [&p](std::size_t n) { p.reserve_disk(n); });
    std::ostream out(&buffer);
    out.exceptions(std::ios::badbit | std::ios::failbit);
    producer(out, context);
    if (!out)
        throw Error("FileError", "Stream producer failed");
    buffer.finish();
    auto entry = std::make_shared<Impl::Entry>();
    entry->stored = true;
    entry->token = random_token(32);
    entry->owner = context.principal;
    entry->expires = Clock::now() + p.options.result_ttl;
    entry->resource.uri = "cymcp://result/" + entry->token;
    entry->resource.name = "Tool result";
    entry->resource.mime_type = std::move(mime_type);
    entry->resource.size = buffer.bytes;
    entry->resource.binary = binary;
    entry->on_disk = static_cast<bool>(buffer.disk);
    entry->resource.cache_ttl = p.options.result_ttl;
    if (buffer.disk) {
        entry->resource.read = [disk = buffer.disk](std::size_t offset, std::size_t length,
                                                    const CallContext& c) {
            c.cancellation.check();
            std::ifstream in(disk->path, std::ios::binary);
            in.seekg(static_cast<std::streamoff>(offset));
            std::string result(length, '\0');
            in.read(result.data(), static_cast<std::streamsize>(length));
            if (!in || static_cast<std::size_t>(in.gcount()) != length)
                throw Error("FileError", "Cannot read retained result");
            return result;
        };
    } else {
        auto data = std::make_shared<const std::string>(std::move(buffer.memory));
        entry->resource.read = [data](std::size_t offset, std::size_t length,
                                      const CallContext& ctx) {
            ctx.cancellation.check();
            return data->substr(offset, length);
        };
    }
    std::lock_guard lock(p.mutex);
    p.prune();
    const auto budget = entry->on_disk ? p.options.max_disk_bytes : p.options.max_stored_bytes;
    auto retained = [&] { return entry->on_disk ? p.disk_bytes : p.bytes; };
    while (!p.stored.empty() && (p.stored.size() >= p.options.max_stored_results ||
                                 entry->resource.size > budget - retained())) {
        auto oldest =
            std::min_element(p.stored.begin(), p.stored.end(), [](const auto& a, const auto& b) {
                return a.second->used < b.second->used;
            });
        p.erase_stored(oldest);
    }
    entry->used = ++p.tick;
    (entry->on_disk ? p.disk_bytes : p.bytes) += entry->resource.size;
    p.chunks[entry->token] = entry;
    p.stored[entry->resource.uri] = entry;
    return {{"uri", entry->resource.uri},
            {"name", entry->resource.name},
            {"mimeType", entry->resource.size > p.options.chunk_bytes ? "application/json"
                                                                      : entry->resource.mime_type},
            {"bytes", entry->resource.size},
            {"chunked", entry->resource.size > p.options.chunk_bytes},
            {"expiresInMs", p.options.result_ttl.count()}};
}
Json ResourceRegistry::storage_stats() const {
    std::lock_guard lock(impl_->mutex);
    impl_->prune();
    return {{"memory_bytes", impl_->bytes},
            {"disk_bytes", impl_->disk_bytes},
            {"results", impl_->stored.size()}};
}
std::size_t ResourceRegistry::stored_bytes() const {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    p.prune();
    return p.bytes;
}

Json ResourceRegistry::grep(std::string text, bool ignore_case, std::string cursor,
                            const CallContext& context) {
    context.cancellation.check();
    auto& p = *impl_;
    if (text.empty() || text.size() > 4096)
        throw Error("InvalidParameters", "Resource grep text must contain 1..4096 bytes");
    Impl::Grep state;
    std::vector<std::shared_ptr<Impl::Entry>> entries;
    {
        std::lock_guard lock(p.mutex);
        for (auto it = p.greps.begin(); it != p.greps.end();) {
            if (it->second.expires <= Clock::now())
                it = p.greps.erase(it);
            else
                ++it;
        }
        if (!cursor.empty()) {
            auto found = p.greps.find(cursor);
            if (found == p.greps.end() || found->second.owner != context.principal ||
                found->second.text != text || found->second.ignore_case != ignore_case)
                throw Error("SearchExpired",
                            "Resource search cursor is unavailable or belongs to another query");
            state = found->second;
        } else {
            state.text = text;
            state.ignore_case = ignore_case;
            state.owner = context.principal;
            state.revision = p.revision;
        }
        for (const auto& [uri, entry] : p.public_resources)
            entries.push_back(entry);
    }
    const auto needle = ignore_case ? lower(text) : text;
    Json matches = Json::array();
    while (state.index < entries.size() && matches.size() < 50) {
        context.cancellation.check();
        const auto& entry = *entries[state.index];
        const auto& resource = entry.resource;
        if (resource.binary ||
            !p.policy.allows(context, "resources.read", {{"uri", resource.uri}}) ||
            state.offset >= resource.size) {
            ++state.index;
            state.offset = 0;
            state.line = 1;
            state.line_start = 0;
            state.tail.clear();
            state.skip_line = false;
            continue;
        }
        const auto wanted = std::min(p.options.chunk_bytes, resource.size - state.offset);
        auto block = resource.read(state.offset, wanted, context);
        context.cancellation.check();
        if (block.size() != wanted)
            throw Error("ResourceChanged", "Resource changed during grep");
        std::size_t pos = 0;
        while (pos < block.size() && matches.size() < 50) {
            const auto newline = block.find('\n', pos);
            const auto end = newline == std::string::npos ? block.size() : newline;
            const auto fragment = block.substr(pos, end - pos);
            if (!state.skip_line) {
                auto combined = state.tail + fragment;
                const auto searchable = ignore_case ? lower(combined) : combined;
                if (const auto hit = searchable.find(needle); hit != std::string::npos) {
                    const auto absolute = state.offset + pos - state.tail.size() + hit;
                    auto start = hit > 60 ? hit - 60 : 0;
                    while (start < combined.size() &&
                           (static_cast<unsigned char>(combined[start]) & 0xc0) == 0x80)
                        ++start;
                    matches.push_back({{"uri", resource.uri},
                                       {"name", resource.name},
                                       {"line", state.line},
                                       {"byteOffset", absolute},
                                       {"text", preview(std::string_view(combined).substr(start))},
                                       {"readUri", std::string(chunk_prefix) + entry.token + "/" +
                                                       std::to_string(state.line_start)}});
                    state.skip_line = true;
                    state.tail.clear();
                } else
                    state.tail = combined.substr(
                        combined.size() > needle.size() ? combined.size() - needle.size() : 0);
            }
            pos = end;
            if (newline != std::string::npos) {
                ++pos;
                ++state.line;
                state.line_start = state.offset + pos;
                state.tail.clear();
                state.skip_line = false;
            }
        }
        state.offset += pos;
    }
    const bool complete = state.index == entries.size();
    Json result = {{"matches", matches}, {"complete", complete}};
    {
        std::lock_guard lock(p.mutex);
        if (p.revision != state.revision)
            throw Error("ResourceChanged", "Resource catalogue changed during grep");
        if (!cursor.empty())
            p.greps.erase(cursor);
        if (!complete) {
            if (p.greps.size() >= 128)
                p.greps.erase(p.greps.begin());
            const auto next = random_token(32);
            state.expires = Clock::now() + std::chrono::seconds(60);
            p.greps[next] = std::move(state);
            result["nextCursor"] = next;
        }
    }
    return result;
}
struct ToolGateway::Impl {
    std::shared_ptr<const Registry> target;
    std::shared_ptr<ResourceRegistry> resources;
    ToolGatewayOptions options;
    std::mutex mutex;
    std::shared_ptr<const Json> snapshot;
    struct Summary {
        Json value;
        std::string searchable;
    };
    std::vector<Summary> summaries;
    std::string generation;
    Impl(std::shared_ptr<const Registry> r, ToolGatewayOptions o,
         std::shared_ptr<ResourceRegistry> shared_resources)
        : target(std::move(r)),
          resources(shared_resources
                        ? std::move(shared_resources)
                        : std::make_shared<ResourceRegistry>(Policy::allow_all(), o.resources)),
          options(o) {
        if (!target || !o.page_size || o.page_size > 100 || !o.inline_bytes)
            throw Error("InvalidOptions", "Invalid tool gateway options");
    }
    Json search(const Json& args, const CallContext& ctx) {
        ctx.cancellation.check();
        const auto query = lower(args.value("query", std::string()));
        if (query.size() > 1024)
            throw Error("InvalidParameters", "Search query exceeds limit");
        auto current = target->summaries();
        std::lock_guard lock(mutex);
        if (current != snapshot) {
            snapshot = std::move(current);
            summaries.clear();
            generation = random_token(16);
            for (const auto& tool : *snapshot) {
                const auto name = tool.at("name").get<std::string>();
                const auto description = tool.at("description").get<std::string>();
                summaries.push_back({{{"name", name},
                                      {"description", preview(description)},
                                      {"annotations", tool.at("annotations")}},
                                     lower(name + " " + description)});
            }
        }
        std::size_t offset = 0;
        if (args.contains("cursor")) {
            const auto cursor = args.at("cursor").get<std::string>();
            const auto colon = cursor.find(':');
            if (colon == std::string::npos ||
                cursor.substr(0, colon) != generation + "-" + sha256(query))
                throw Error("InvalidParameters",
                            "Search cursor expired or belongs to another query");
            offset = number(std::string_view(cursor).substr(colon + 1));
        }
        if (offset > summaries.size())
            throw Error("InvalidParameters", "Invalid search cursor");
        Json matches = Json::array();
        std::size_t i = offset;
        for (; i < summaries.size() && matches.size() < options.page_size; ++i) {
            if ((i & 127) == 0)
                ctx.cancellation.check();
            if (query.empty() || summaries[i].searchable.find(query) != std::string::npos)
                matches.push_back(summaries[i].value);
        }
        Json result = {{"tools", matches}, {"revision", generation}};
        if (i < summaries.size())
            result["nextCursor"] = generation + "-" + sha256(query) + ":" + std::to_string(i);
        return result;
    }
    Json invoke(const Json& args, const CallContext& context) {
        auto result = target->invoke(args.at("name").get<std::string>(),
                                     args.value("arguments", Json::object()), context);
        if (!exceeds(result, options.inline_bytes))
            return result;
        try {
            return Json{
                {"resource", resources->store_json(result, context)},
                {"message",
                 "Read the resource manifest, then follow chunk URIs with cy_resources_read."}};
        } catch (const Error& error) {
            if (error.code != "ResourceTooLarge")
                throw;
            return result;
        }
    }
};
ToolGateway::ToolGateway(std::shared_ptr<const Registry> target, ToolGatewayOptions options,
                         std::shared_ptr<ResourceRegistry> resources)
    : impl_(std::make_shared<Impl>(std::move(target), options, std::move(resources))),
      tools_(std::make_shared<Registry>(Policy::allow_all())) {
    auto text = [] { return Json{{"type", "string"}}; };
    auto object = [](Json properties, Json required) {
        return Json{{"type", "object"},
                    {"properties", std::move(properties)},
                    {"required", std::move(required)},
                    {"additionalProperties", false}};
    };
    tools_->add({"cy_tools_search",
                 "Search application tools by name or description. Returns summaries only; follow "
                 "nextCursor for more.",
                 object({{"query", text()}, {"cursor", text()}}, Json::array()), Json::object(),
                 true,
                 [p = impl_](const Json& a, const CallContext& c) { return p->search(a, c); }});
    tools_->add({"cy_tools_describe",
                 "Load the complete input/output schema of one application tool before calling it.",
                 object({{"name", text()}}, {"name"}), Json::object(), true,
                 [p = impl_](const Json& a, const CallContext& c) {
                     c.cancellation.check();
                     return p->target->describe(a.at("name").get<std::string>());
                 }});
    tools_->add(
        {"cy_tools_call",
         "Call an application tool by name and validated arguments. Large results return a private "
         "resource handle, retained for a bounded time; read it with cy_resources_read.",
         object({{"name", text()}, {"arguments", {{"type", "object"}}}}, {"name"}), Json::object(),
         false, [p = impl_](const Json& a, const CallContext& c) { return p->invoke(a, c); }});
    tools_->add({"cy_resources_read",
                 "Read a resource manifest or one bounded chunk. Follow firstUri/nextUri only when "
                 "needed. Expired handles require a new explicit operation.",
                 object({{"uri", text()}}, {"uri"}), Json::object(), true,
                 [p = impl_](const Json& a, const CallContext& c) {
                     return p->resources->read(a.at("uri").get<std::string>(), c);
                 }});
    tools_->add(
        {"cy_resources_grep",
         "Search registered text resources for literal text without loading whole resources. "
         "Returns matching lines and readUri links; continue with the same text/ignore_case and "
         "nextCursor.",
         object({{"text", text()}, {"ignore_case", {{"type", "boolean"}}}, {"cursor", text()}},
                {"text"}),
         Json::object(), true, [p = impl_](const Json& a, const CallContext& c) {
             return p->resources->grep(a.at("text").get<std::string>(),
                                       a.value("ignore_case", false),
                                       a.value("cursor", std::string()), c);
         }});
}
std::shared_ptr<ResourceRegistry> ToolGateway::resources() const {
    return impl_->resources;
}
std::shared_ptr<const Registry> compact_tools(std::shared_ptr<const Registry> tools,
                                              std::size_t threshold) {
    if (!tools || tools->size() <= threshold)
        return tools;
    const auto snapshot = tools->summaries();
    if (snapshot->size() == 5 &&
        std::all_of(snapshot->begin(), snapshot->end(), [](const auto& tool) {
            const auto& name = tool.at("name");
            return name == "cy_tools_search" || name == "cy_tools_describe" ||
                   name == "cy_tools_call" || name == "cy_resources_read" ||
                   name == "cy_resources_grep";
        }))
        return tools;
    return ToolGateway(std::move(tools)).tools();
}
} // namespace cymcp
