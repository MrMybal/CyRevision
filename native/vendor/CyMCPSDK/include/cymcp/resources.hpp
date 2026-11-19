#pragma once
#include "core.hpp"
#include <ostream>

namespace cymcp {
struct Resource {
    std::string uri, name, description, mime_type = "text/plain";
    std::size_t size = 0;
    bool binary = false;
    std::chrono::milliseconds cache_ttl{0};
    // Reads at most length bytes of an immutable host snapshot. No implicit file/network I/O.
    std::function<std::string(std::size_t offset, std::size_t length, const CallContext&)> read;
};
struct ResourceOptions {
    std::size_t chunk_bytes = 32 * 1024;
    std::size_t max_stored_bytes = 64 * 1024 * 1024, max_stored_results = 128;
    std::chrono::milliseconds result_ttl{300000};
    std::filesystem::path disk_directory; // absent = no disk I/O
    std::size_t max_disk_bytes = 1024ULL * 1024 * 1024, spool_threshold = 1024 * 1024;
};
class ResourceRegistry {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit ResourceRegistry(Policy = {}, ResourceOptions = {});
    ~ResourceRegistry();
    ResourceRegistry(const ResourceRegistry&) = delete;
    ResourceRegistry& operator=(const ResourceRegistry&) = delete;
    void add(Resource);
    void replace(Resource);
    void remove(const std::string& uri);
    void set_change_feed(std::shared_ptr<ChangeFeed>);
    bool readable(const std::string& uri, const CallContext& = {}) const;
    std::uint64_t revision() const;
    // Listings contain descriptions only. Large contents are manifests linking bounded chunks.
    Json list(std::size_t offset = 0, std::size_t limit = 64) const;
    Json read(const std::string& uri, const CallContext& = {}) const;
    // Literal, streaming grep over registered text snapshots; no filesystem traversal.
    Json grep(std::string text, bool ignore_case = false, std::string cursor = {},
              const CallContext& = {});
    // Retained result handles are private to context.principal, expire and are bounded in memory.
    Json store(std::string bytes, std::string mime_type, const CallContext& = {},
               bool binary = false);
    // Producer writes incrementally, once, to a bounded memory/disk sink. Failure publishes no
    // handle.
    Json store_stream(const std::function<void(std::ostream&, const CallContext&)>&,
                      std::string mime_type, const CallContext& = {}, bool binary = false);
    Json store_json(const Json&, const CallContext& = {});
    std::size_t stored_bytes() const;
    Json storage_stats() const;
};
struct ToolGatewayOptions {
    std::size_t page_size = 20, inline_bytes = 16 * 1024;
    ResourceOptions resources;
};
// Callable tools: cy_tools_search, cy_tools_describe, cy_tools_call,
// cy_resources_read and cy_resources_grep. Can be passed to MCP or any provider's tools field; the
// target policy remains authoritative.
class ToolGateway {
    struct Impl;
    std::shared_ptr<Impl> impl_;
    std::shared_ptr<Registry> tools_;

  public:
    explicit ToolGateway(std::shared_ptr<const Registry>, ToolGatewayOptions = {},
                         std::shared_ptr<ResourceRegistry> = {});
    std::shared_ptr<const Registry> tools() const {
        return tools_;
    }
    std::shared_ptr<ResourceRegistry> resources() const;
};
// Keep small registries unchanged; larger ones automatically use on-demand tool definitions.
std::shared_ptr<const Registry> compact_tools(std::shared_ptr<const Registry>,
                                              std::size_t threshold = 32);
} // namespace cymcp
