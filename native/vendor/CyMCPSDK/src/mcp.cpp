#include "mcp_headers.hpp"
#include <charconv>
#include <cymcp/changes.hpp>
#include <cymcp/mcp.hpp>
#include <cymcp/performance.hpp>
#include <limits>
#include <optional>
#include <set>
namespace cymcp {
namespace {
using mcp_http::legacy;
using mcp_http::modern;
using mcp_http::version_key;
constexpr auto caps_key = "io.modelcontextprotocol/clientCapabilities";
std::size_t page_offset(const Json& params, const std::string& generation, std::size_t size) {
    if (!params.contains("cursor"))
        return 0;
    const auto cursor = params.at("cursor").get<std::string>();
    const auto prefix = generation + ":";
    if (!cursor.starts_with(prefix))
        throw Error("InvalidParameters", "Catalogue cursor is invalid or stale");
    const auto tail = std::string_view(cursor).substr(prefix.size());
    std::size_t offset = 0;
    const auto parsed = std::from_chars(tail.data(), tail.data() + tail.size(), offset);
    if (tail.empty() || parsed.ec != std::errc{} || parsed.ptr != tail.data() + tail.size() ||
        offset > size)
        throw Error("InvalidParameters", "Catalogue cursor is outside the current snapshot");
    return offset;
}
Json rpc_error(const Json& id, int code, const std::string& message) {
    return {{"jsonrpc", "2.0"}, {"id", id}, {"error", {{"code", code}, {"message", message}}}};
}
std::string metric_method(const Json& request) {
    if (!request.is_object() || !request.contains("method") || !request["method"].is_string())
        return "unknown";
    auto method = request["method"].get<std::string>();
    for (const auto* known :
         {"server/discover", "initialize", "ping", "tools/list", "tools/call", "cymcp/tools/get",
          "resources/list", "resources/read", "resources/templates/list", "subscriptions/listen",
          "notifications/initialized", "notifications/cancelled"})
        if (method == known)
            return method;
    return "unknown";
}
Json meta() {
    return {{version_key, modern},
            {caps_key, Json::object()},
            {"io.modelcontextprotocol/clientInfo", {{"name", "CyMCPSDK"}, {"version", version}}}};
}
struct HeaderParameter {
    std::string name, type;
    Json::json_pointer path;
};
using HeaderParameters = std::vector<HeaderParameter>;
bool collect_headers(const Json& schema, const Json::json_pointer& path, bool reachable,
                     HeaderParameters& parameters, std::set<std::string>& names) {
    if (!schema.is_object())
        return true;
    if (const auto it = schema.find("x-mcp-header"); it != schema.end()) {
        if (!reachable || path.empty() || !it->is_string())
            return false;
        auto name = it->get<std::string>();
        constexpr auto tokens =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#$%&'*+-.^_`|~";
        if (name.empty() || name.find_first_not_of(tokens) != std::string::npos)
            return false;
        auto lower = name;
        std::transform(lower.begin(), lower.end(), lower.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        const auto type = schema.value("type", Json());
        if (!names.insert(lower).second ||
            (type != "string" && type != "integer" && type != "boolean"))
            return false;
        parameters.push_back({"Mcp-Param-" + name, type.get<std::string>(), path});
    }
    for (const auto& [key, child] : schema.items()) {
        if (key == "properties" || key == "patternProperties" || key == "$defs" ||
            key == "definitions" || key == "dependentSchemas") {
            if (!child.is_object())
                continue;
            for (const auto& [name, property] : child.items())
                if (!collect_headers(property, path / name, reachable && key == "properties",
                                     parameters, names))
                    return false;
        } else if (key == "items" || key == "prefixItems" || key == "allOf" || key == "anyOf" ||
                   key == "oneOf" || key == "not" || key == "if" || key == "then" ||
                   key == "else" || key == "additionalProperties" || key == "additionalItems" ||
                   key == "contains" || key == "propertyNames" || key == "unevaluatedProperties" ||
                   key == "unevaluatedItems") {
            if (child.is_array()) {
                for (const auto& item : child)
                    if (!collect_headers(item, path, false, parameters, names))
                        return false;
            } else if (!collect_headers(child, path, false, parameters, names))
                return false;
        }
    }
    return true;
}
std::string parameter_value(const HeaderParameter& header, const Json& value) {
    if (header.type == "string" && value.is_string())
        return mcp_http::header_value(value.get_ref<const std::string&>());
    if (header.type == "boolean" && value.is_boolean())
        return value.dump();
    constexpr std::int64_t safe = 9007199254740991LL;
    if (header.type == "integer" && value.is_number_integer() &&
        (value.is_number_unsigned()
             ? value.get<std::uint64_t>() <= static_cast<std::uint64_t>(safe)
             : value.get<std::int64_t>() >= -safe && value.get<std::int64_t>() <= safe))
        return value.dump();
    throw Error("InvalidParameters", "Cannot represent tool parameter in " + header.name);
}
} // namespace
struct McpServer::ActiveCall {
    McpServer& server;
    RequestKey key;
    CallContext context;
    bool registered = false;
    ActiveCall(McpServer& server, RequestKey key, CallContext context)
        : server(server), key(std::move(key)), context(std::move(context)) {}
    ~ActiveCall() {
        if (registered) {
            std::lock_guard lock(server.mutex_);
            server.active_.erase(key);
        }
    }
};
McpServer::McpServer(const Registry& registry, std::string name, McpServerOptions options)
    : registry_(registry), name_(std::move(name)), options_(std::move(options)) {
    if (!options_.page_size || options_.page_size > 1000 || options_.cache_ttl.count() < 0 ||
        !options_.max_subscriptions || options_.max_subscriptions > 64 ||
        !options_.max_resource_subscriptions || options_.max_resource_subscriptions > 1024)
        throw Error("InvalidOptions", "Invalid MCP catalogue options");
}
Json McpServer::tool_page(const Json& params) {
    auto snapshot = registry_.catalogue();
    std::string generation;
    {
        std::lock_guard lock(mutex_);
        if (snapshot != catalogue_) {
            catalogue_ = snapshot;
            catalogue_generation_ = random_token(16);
        }
        generation = catalogue_generation_;
    }
    const auto offset = page_offset(params, generation, snapshot->size());
    const auto end = std::min(snapshot->size(), offset + options_.page_size);
    Json tools = Json::array();
    for (auto n = offset; n < end; ++n)
        tools.push_back((*snapshot)[n]);
    Json result = {
        {"tools", tools}, {"ttlMs", options_.cache_ttl.count()}, {"cacheScope", "private"}};
    if (end < snapshot->size())
        result["nextCursor"] = generation + ":" + std::to_string(end);
    result["_meta"] = {{"com.cymcp/catalogueRevision", generation}};
    return result;
}
void McpServer::cancel_all() {
    std::lock_guard lock(mutex_);
    for (const auto& [id, cancel] : active_)
        cancel.cancel();
}
std::shared_ptr<McpServer::ActiveCall> McpServer::reserve(const Json& request,
                                                          const CallContext& input, Json& error) {
    if (!request.is_object() ||
        (request.value("method", Json()) != "tools/call" &&
         request.value("method", Json()) != "resources/read" &&
         request.value("method", Json()) != "subscriptions/listen") ||
        !request.contains("id") ||
        !(request["id"].is_string() || request["id"].is_number_integer()))
        return {};
    const auto& id = request["id"];
    auto call = std::make_shared<ActiveCall>(
        *this, RequestKey{{input.principal, input.transport_scope}, id.dump()}, input);
    call->context.cancellation = input.cancellation.child();
    call->context.request_id = id.dump();
    std::lock_guard lock(mutex_);
    if (active_.size() >= 128)
        error = rpc_error(id, -32001, "Server busy");
    else if (!active_.emplace(call->key, call->context.cancellation).second)
        error = rpc_error(id, -32600, "Request ID already active");
    else
        call->registered = true;
    return call;
}
Json McpServer::handle(const Json& request, const CallContext& input) {
    auto measured = input;
    if (!measured.performance)
        measured.performance = options_.performance;
    Json error;
    auto call = reserve(request, measured, error);
    if (!error.is_null()) {
        return error;
    }
    return dispatch(request, call ? call->context : measured);
}
Json McpServer::listen(const Json& id, const Json& params, const CallContext& c) {
    if (subscriptions_.fetch_add(1) >= options_.max_subscriptions) {
        --subscriptions_;
        throw Error("Busy", "Subscription capacity reached");
    }
    struct Release {
        std::atomic_uint& count;
        ~Release() {
            --count;
        }
    } release{subscriptions_};
    if (!options_.changes || !c.notifications)
        throw Error("InvalidParameters",
                    "Notifications require an enabled journal and streaming transport");
    auto requested = params.value("notifications", Json::object());
    if (!requested.is_object())
        throw Error("InvalidParameters", "Notification filter must be an object");
    Json filter = Json::object();
    for (const auto* key : {"toolsListChanged", "resourcesListChanged"})
        if (requested.value(key, false) &&
            (std::string(key) == "toolsListChanged" || options_.resources))
            filter[key] = true;
    Json uris = requested.value("resourceSubscriptions", Json::array());
    if (!uris.is_array() || uris.size() > options_.max_resource_subscriptions)
        throw Error("InvalidParameters", "Resource subscription limit exceeded");
    Json accepted = Json::array();
    for (const auto& uri : uris) {
        const auto name = uri.get<std::string>();
        if (name.empty() || name.size() > 4096)
            throw Error("InvalidParameters", "Invalid subscription URI");
        if (options_.resources && options_.resources->readable(name, c))
            accepted.push_back(name);
    }
    if (!accepted.empty())
        filter["resourceSubscriptions"] = accepted;
    const auto metadata = Json{{"io.modelcontextprotocol/subscriptionId", id}};
    auto send = [&](std::string method, Json data) {
        data["_meta"] = metadata;
        c.notifications(
            {{"jsonrpc", "2.0"}, {"method", std::move(method)}, {"params", std::move(data)}});
    };
    auto sequence = options_.changes->sequence();
    send("notifications/subscriptions/acknowledged", {{"notifications", filter}});
    auto heartbeat = Clock::now();
    while (!c.cancellation.cancelled()) {
        auto batch =
            options_.changes->read(sequence, c.cancellation, std::chrono::milliseconds(250));
        sequence = batch.sequence;
        bool tools = batch.missed, resources = batch.missed;
        std::set<std::string> changed;
        for (const auto& change : batch.changes) {
            tools |= change.topic == "tools";
            resources |= change.topic == "resources";
            if (change.topic == "resource")
                changed.insert(change.uri);
        }
        if (tools && filter.value("toolsListChanged", false))
            send("notifications/tools/list_changed", Json::object());
        if (resources && filter.value("resourcesListChanged", false))
            send("notifications/resources/list_changed", Json::object());
        for (const auto& uri : accepted) {
            auto name = uri.get<std::string>();
            if (batch.missed || changed.contains(name))
                send("notifications/resources/updated", {{"uri", name}});
        }
        if (Clock::now() - heartbeat >= std::chrono::seconds(1)) {
            c.notifications(nullptr);
            heartbeat = Clock::now();
        }
    }
    return {{"jsonrpc", "2.0"},
            {"id", id},
            {"result", {{"resultType", "complete"}, {"_meta", metadata}}}};
}
Json McpServer::dispatch(const Json& request, const CallContext& input) {
    PerformanceSpan span(input.performance, "mcp.server", metric_method(request), request);
    Json id = nullptr;
    if (!request.is_object() || request.value("jsonrpc", Json()) != "2.0" ||
        !request.contains("method") || !request["method"].is_string())
        return rpc_error(id, -32600, "Invalid JSON-RPC request");
    bool notification = !request.contains("id");
    if (!notification) {
        id = request["id"];
        if (!id.is_string() && !id.is_number_integer())
            return rpc_error(nullptr, -32600, "Invalid request ID");
    }
    auto method = request["method"].get<std::string>();
    Json params = request.value("params", Json::object());
    if (!params.is_object())
        return notification ? Json() : rpc_error(id, -32602, "Parameters must be an object");
    if (notification) {
        if (method == "notifications/cancelled" && params.contains("requestId")) {
            std::lock_guard lock(mutex_);
            auto it = active_.find(
                {{input.principal, input.transport_scope}, params["requestId"].dump()});
            if (it != active_.end())
                it->second.cancel();
        }
        span.finish("ok");
        return nullptr;
    }
    try {
        auto metadata = params.value("_meta", Json::object());
        if (!metadata.is_object())
            return rpc_error(id, -32602, "Invalid metadata");
        bool current = metadata.contains(version_key);
        if (current) {
            if (metadata[version_key] != modern) {
                auto reply = rpc_error(id, -32022, "Unsupported protocol version");
                reply["error"]["data"] = {
                    {"supported", Json::array({modern, legacy, mcp_http::june_2025})},
                    {"requested", metadata[version_key]}};
                return reply;
            }
            if (!metadata.contains(caps_key) || !metadata[caps_key].is_object())
                return rpc_error(id, -32602, "Client capabilities metadata required");
        }
        Json result;
        Json capabilities = {{"tools", Json::object()},
                             {"experimental", {{"com.cymcp/toolLookup", Json::object()}}}};
        if (options_.resources)
            capabilities["resources"] = Json::object();
        if (current && options_.changes) {
            capabilities["tools"]["listChanged"] = true;
            if (options_.resources)
                capabilities["resources"] = {{"listChanged", true}, {"subscribe", true}};
        }
        if (method == "server/discover") {
            if (!current)
                return rpc_error(id, -32602, "Discovery requires version metadata");
            result = {
                {"supportedVersions", Json::array({modern, legacy, mcp_http::june_2025})},
                {"capabilities", capabilities},
                {"_meta",
                 {{"io.modelcontextprotocol/serverInfo", {{"name", name_}, {"version", version}}}}},
                {"ttlMs", options_.cache_ttl.count()},
                {"cacheScope", "private"}};
        } else if (method == "initialize") {
            if (!mcp_http::legacy_version(params.value("protocolVersion", std::string())))
                return rpc_error(id, -32602, "Supported legacy versions: 2025-11-25, 2025-06-18");
            {
                std::lock_guard lock(mutex_);
                initialized_ = true;
            }
            result = {{"protocolVersion", params["protocolVersion"]},
                      {"capabilities", capabilities},
                      {"serverInfo", {{"name", name_}, {"version", version}}}};
        } else {
            if (!current) {
                std::lock_guard lock(mutex_);
                if (!initialized_)
                    return rpc_error(id, -32000, "Initialize the connection first");
            }
            if (method == "subscriptions/listen" && current)
                return listen(id, params, input);
            if (method == "ping")
                result = Json::object();
            else if (method == "tools/list") {
                result = tool_page(params);
            } else if (method == "cymcp/tools/get") {
                result = {{"tool", registry_.describe(params.at("name").get<std::string>())},
                          {"ttlMs", options_.cache_ttl.count()},
                          {"cacheScope", "private"}};
            } else if (method == "resources/list" && options_.resources) {
                auto revision = options_.resources->revision();
                auto first = options_.resources->list(0, 1);
                const auto generation = "resources-" + std::to_string(revision);
                const auto offset =
                    page_offset(params, generation, first.at("total").get<std::size_t>());
                auto page = options_.resources->list(offset, options_.page_size);
                if (page.at("revision") != revision)
                    throw Error("InvalidParameters",
                                "Resource catalogue changed; restart pagination");
                result = {{"resources", page.at("resources")},
                          {"ttlMs", options_.cache_ttl.count()},
                          {"cacheScope", "private"}};
                const auto end = offset + result.at("resources").size();
                if (end < page.at("total").get<std::size_t>())
                    result["nextCursor"] = generation + ":" + std::to_string(end);
            } else if (method == "resources/templates/list" && options_.resources) {
                if (params.contains("cursor"))
                    throw Error("InvalidParameters", "Resource templates have no next page");
                result = {{"resourceTemplates", Json::array()},
                          {"ttlMs", options_.cache_ttl.count()},
                          {"cacheScope", "private"}};
            } else if (method == "resources/read" && options_.resources) {
                result = options_.resources->read(params.at("uri").get<std::string>(), input);
            } else if (method == "tools/call") {
                if (!params.contains("name") || !params["name"].is_string() ||
                    (params.contains("arguments") && !params["arguments"].is_object()))
                    return rpc_error(id, -32602, "Tool name and object arguments required");
                try {
                    auto value = registry_.invoke(params["name"].get<std::string>(),
                                                  params.value("arguments", Json::object()), input);
                    result = {{"content", Json::array({{{"type", "text"},
                                                        {"text", options_.result_text
                                                                     ? options_.result_text(value)
                                                                     : value.dump()}}})},
                              {"isError", false}};
                    if (value.is_object())
                        result["structuredContent"] = value;
                    if (options_.resources && value.is_object() && value.contains("resource") &&
                        value["resource"].is_object() &&
                        value["resource"]
                            .value("uri", std::string())
                            .starts_with("cymcp://result/"))
                        result["content"].push_back({{"type", "resource_link"},
                                                     {"uri", value["resource"]["uri"]},
                                                     {"name", "Tool result"},
                                                     {"mimeType", "application/json"}});
                } catch (const Error& e) {
                    result = {
                        {"content",
                         Json::array(
                             {{{"type", "text"},
                               {"text", Json({{"code", e.code}, {"message", e.what()}}).dump()}}})},
                        {"isError", true}};
                }
            } else
                return rpc_error(id, -32601, "Method not implemented");
        }
        if (current)
            result["resultType"] = "complete";
        Json reply = {{"jsonrpc", "2.0"}, {"id", id}, {"result", std::move(result)}};
        span.finish(reply["result"].value("isError", false) ? "ToolError" : "ok", &reply);
        return reply;
    } catch (const Json::exception&) {
        span.finish("InvalidParameters");
        return rpc_error(id, -32602, "Invalid method parameters");
    } catch (const Error& e) {
        span.finish(e.code);
        return rpc_error(id,
                         e.code == "InvalidParameters" || e.code == "UnknownOperation" ? -32602
                         : e.code == "ResourceNotFound"                                ? -32002
                         : e.code == "AccessDenied"                                    ? -32001
                                                                                       : -32603,
                         e.what());
    } catch (...) {
        span.finish("InternalError");
        return rpc_error(id, -32603, "Internal error");
    }
}
void McpServer::serve_stdio(std::istream& in, std::ostream& out, std::size_t workers) {
    std::mutex output;
    CallContext base;
    base.transport_scope = random_token(16);
    auto send = [&](const Json& result, const Cancellation* cancelled = nullptr) {
        if (!result.is_null()) {
            std::lock_guard lock(output);
            if (!cancelled || !cancelled->cancelled())
                out << result.dump() << '\n' << std::flush;
        }
    };
    Executor executor(workers);
    std::unique_ptr<Executor> subscribers;
    if (options_.changes)
        subscribers = std::make_unique<Executor>(options_.max_subscriptions);
    base.notifications = [&](const Json& message) { send(message); };
    base.performance = options_.performance;
    std::string line;
    line.reserve(4096);
    bool oversized = false;
    char ch;
    while (in.get(ch)) {
        if (ch != '\n') {
            if (line.size() < provider_message_limit)
                line += ch;
            else
                oversized = true;
            continue;
        }
        if (oversized) {
            send(rpc_error(nullptr, -32700, "Message exceeds the memory guard"));
            line.clear();
            oversized = false;
            continue;
        }
        if (line.empty())
            continue;
        Json request;
        try {
            request = parse_json(line, provider_message_limit);
        } catch (...) {
            send(rpc_error(nullptr, -32700, "Parse error"));
            line.clear();
            continue;
        }
        line.clear();
        if (request.is_object() && request.value("jsonrpc", Json()) == "2.0" &&
            !request.contains("id") &&
            request.value("method", Json()) == "notifications/cancelled") {
            // Order accepting cancellation against the last possible reply on this stream.
            std::lock_guard lock(output);
            dispatch(request, base);
            continue;
        }
        Json error;
        auto call = reserve(request, base, error);
        if (!error.is_null())
            send(error);
        else if (call) {
            auto id = request.at("id");
            try {
                auto& pool =
                    subscribers && request.value("method", Json()) == "subscriptions/listen"
                        ? *subscribers
                        : executor;
                pool.post([&, request = std::move(request), call] {
                    if (!call->context.cancellation.cancelled())
                        send(dispatch(request, call->context), &call->context.cancellation);
                });
            } catch (const Error&) {
                send(rpc_error(id, -32001, "Server busy"));
            }
        } else
            send(dispatch(request, base));
    }
    base.cancellation.cancel();
}
struct McpClient::Impl {
    struct Watch {
        Cancellation lifetime;
        std::string id;
        mutable std::mutex mutex;
        std::condition_variable wake;
        std::deque<Json> notifications, replies;
        Json accepted = Json::object();
        bool acknowledged = false, ended = false;
        std::string error;
        std::atomic<std::uint64_t> epoch{0};
        std::jthread worker;
        ~Watch() {
            lifetime.cancel();
            if (worker.joinable())
                worker.join();
        }
        void finish(std::string code) {
            std::lock_guard lock(mutex);
            ended = true;
            if (error.empty())
                error = std::move(code);
            ++epoch;
            wake.notify_all();
        }
        void route(Json message) {
            std::lock_guard lock(mutex);
            if (message.contains("method")) {
                const auto method = message.value("method", std::string());
                const auto params = message.value("params", Json::object());
                if (params.value("_meta", Json::object())
                        .value("io.modelcontextprotocol/subscriptionId", Json()) != id)
                    return;
                if (method == "notifications/subscriptions/acknowledged") {
                    acknowledged = true;
                    accepted = params.value("notifications", Json::object());
                } else if (method == "notifications/tools/list_changed" ||
                           method == "notifications/resources/list_changed" ||
                           method == "notifications/resources/updated")
                    ++epoch;
                else
                    return;
                if (notifications.size() >= 256)
                    notifications.pop_front();
                notifications.push_back(std::move(message));
            } else if (message.value("id", Json()) == id) {
                ended = true;
                if (message.contains("error"))
                    error = "RemoteError";
                ++epoch;
            } else {
                if (replies.size() >= 128) {
                    ended = true;
                    error = "ProtocolError";
                } else
                    replies.push_back(std::move(message));
            }
            wake.notify_all();
        }
        Json next(const Cancellation& cancel) {
            std::unique_lock lock(mutex);
            while (replies.empty() && !ended) {
                cancel.check();
                wake.wait_for(lock, std::chrono::milliseconds(25));
            }
            cancel.check();
            if (replies.empty())
                throw Error("UnexpectedEOF", "MCP notification reader stopped");
            auto reply = std::move(replies.front());
            replies.pop_front();
            return reply;
        }
        void stream(const char* bytes, std::size_t count, std::string& line, std::string& data) {
            for (std::size_t i = 0; i < count; ++i) {
                if (bytes[i] != '\n') {
                    line += bytes[i];
                    if (line.size() > 1024 * 1024)
                        throw Error("ResponseTooLarge", "Notification line exceeds limit");
                    continue;
                }
                if (!line.empty() && line.back() == '\r')
                    line.pop_back();
                if (line.starts_with("data:")) {
                    data += line.substr(5);
                    data += '\n';
                    if (data.size() > 1024 * 1024)
                        throw Error("ResponseTooLarge", "Notification exceeds limit");
                } else if (line.empty() && !data.empty()) {
                    route(parse_json(data));
                    data.clear();
                }
                line.clear();
            }
        }
    };
    std::unique_ptr<Watch> watch;
    std::uint64_t observed_epoch = 0;
    ProcessOptions options;
    std::unique_ptr<Process> process;
    std::string url, token, protocol = modern;
    std::string session_id;
    std::int64_t counter = 0;
    bool connected = false;
    bool tool_lookup = false;
    McpClientOptions cache_options;
    struct Cached {
        Json value;
        Clock::time_point expires;
        std::size_t bytes;
        std::uint64_t used;
    };
    std::map<std::string, Cached> cache;
    McpCacheStats stats;
    std::uint64_t cache_tick = 0;
    void clear_cache() {
        cache.clear();
        stats.bytes = 0;
        stats.entries = 0;
        tool_headers.reset();
    }
    std::mutex mutex;
    using HeaderCatalogue = std::map<std::string, std::optional<HeaderParameters>>;
    std::optional<HeaderCatalogue> tool_headers;
    void close() noexcept {
        stop_watch();
        try {
            Cancellation grace;
            grace.deadline = Clock::now() + std::chrono::seconds(1);
            if (process) {
                process->close_input();
                ProcessLine discarded;
                // A server may flush diagnostics or answer an earlier cancelled request
                // during shutdown. Drain both pipes so a full output queue cannot block exit.
                while (process->next(discarded, grace)) {
                }
                process->wait(grace);
            }
#ifdef CYMCP_HAS_HTTP
            else if (!session_id.empty() && mcp_http::legacy_version(protocol)) {
                HttpRequest req;
                req.method = "DELETE";
                req.url = url;
                req.headers = {{"MCP-Protocol-Version", protocol}, {"Mcp-Session-Id", session_id}};
                if (!token.empty())
                    req.headers["Authorization"] = "Bearer " + token;
                req.timeout = std::chrono::seconds(1);
                req.max_response_bytes = 4096;
                HttpClient().perform(req, grace);
            }
#endif
        } catch (...) {
            // A peer can ignore EOF or refuse DELETE. Owned processes are stopped below.
        }
        process.reset();
        session_id.clear();
        connected = false;
        clear_cache();
    }
    void stop_watch() noexcept {
        if (!watch)
            return;
        try {
            if (process) {
                Cancellation grace;
                grace.deadline = Clock::now() + std::chrono::seconds(1);
                process->write(Json({{"jsonrpc", "2.0"},
                                     {"method", "notifications/cancelled"},
                                     {"params", {{"requestId", watch->id}, {"_meta", meta()}}}})
                                       .dump() +
                                   "\n",
                               grace);
            }
        } catch (...) {
        }
        watch.reset();
        clear_cache();
    }
    Json list_tools(const Cancellation&);
    Json exchange(std::string method, Json params, const Cancellation& cancel,
                  bool notify = false) {
        if (protocol == modern)
            params["_meta"] = meta();
        Json message = {{"jsonrpc", "2.0"}, {"method", method}, {"params", std::move(params)}};
        auto id = ++counter;
        if (!notify)
            message["id"] = id;
        Json reply;
        if (process) {
            cancel.check();
            try {
                process->write(message.dump() + "\n", cancel);
            } catch (...) {
                // A partial JSON line cannot be resumed safely on this stream.
                connected = false;
                stop_watch();
                process.reset();
                throw;
            }
            if (notify)
                return nullptr;
            ProcessLine line;
            try {
                while (watch || process->next(line, cancel)) {
                    if (watch) {
                        auto next = watch->next(cancel);
                        if (next.value("id", Json()) == id) {
                            reply = std::move(next);
                            break;
                        }
                        continue;
                    }
                    if (line.stderr_stream)
                        continue;
                    Json next = parse_json(line.text, provider_message_limit);
                    if (next.is_object() &&
                        (next.value("method", Json()) == "notifications/tools/list_changed" ||
                         next.value("method", Json()) == "notifications/resources/list_changed" ||
                         next.value("method", Json()) == "notifications/resources/updated")) {
                        clear_cache();
                        continue;
                    }
                    // JSON-RPC answers an undeterminable request with id null: on this ordered
                    // channel it is the reply to the request just written.
                    if (next.is_object() && next.contains("error") && next.contains("id") &&
                        next.at("id").is_null() && !next.contains("method"))
                        next["id"] = id;
                    if (next.contains("method") && next.contains("id")) {
                        process->write(
                            rpc_error(next["id"], -32601, "Client request unsupported").dump() +
                            "\n");
                        continue;
                    }
                    if (next.value("id", Json()) == id) {
                        reply = std::move(next);
                        break;
                    }
                }
            } catch (const Error& e) {
                if (e.code == "Cancelled" || e.code == "Timeout") {
                    Json stop = {{"requestId", id}};
                    if (protocol == modern)
                        stop["_meta"] = meta();
                    const Json notification = {{"jsonrpc", "2.0"},
                                               {"method", "notifications/cancelled"},
                                               {"params", std::move(stop)}};
                    Cancellation delivery;
                    delivery.deadline = Clock::now() + std::chrono::seconds(1);
                    try {
                        process->write(notification.dump() + "\n", delivery);
                    } catch (...) {
                        connected = false;
                        process.reset();
                    }
                }
                throw;
            }
        } else {
#ifdef CYMCP_HAS_HTTP
            HttpRequest req;
            req.method = "POST";
            req.url = url;
            req.body = message.dump();
            // A tool may legitimately run for a long time: only the caller's cancellation and
            // deadline bound the exchange.
            req.timeout = std::chrono::milliseconds(0);
            req.max_response_bytes = provider_message_limit;
            req.headers = {{"Content-Type", "application/json"},
                           {"Accept", "application/json, text/event-stream"},
                           {"MCP-Protocol-Version", protocol}};
            if (!token.empty())
                req.headers["Authorization"] = "Bearer " + token;
            if (protocol == modern) {
                req.headers["Mcp-Method"] = method;
                const auto& fields = message.at("params");
                if (auto field = mcp_http::name_field(method))
                    req.headers["Mcp-Name"] =
                        mcp_http::header_value(fields.at(field).get_ref<const std::string&>());
                if (method == "tools/call" && tool_headers) {
                    auto tool = tool_headers->find(fields.at("name").get_ref<const std::string&>());
                    if (tool != tool_headers->end()) {
                        if (!tool->second)
                            throw Error("InvalidTool", "Tool has invalid HTTP header annotations");
                        const auto& arguments = fields.at("arguments");
                        for (const auto& header : *tool->second)
                            if (arguments.contains(header.path))
                                req.headers[header.name] =
                                    parameter_value(header, arguments.at(header.path));
                    }
                }
            }
            if (!session_id.empty())
                req.headers["Mcp-Session-Id"] = session_id;
            HttpResponse response;
            try {
                response = HttpClient().perform(req, cancel);
            } catch (const Error& e) {
                if (mcp_http::legacy_version(protocol) && method == "tools/call" &&
                    (e.code == "Cancelled" || e.code == "Timeout")) {
                    // Legacy peers may continue work after the request socket closes.
                    Cancellation delivery;
                    delivery.deadline = Clock::now() + std::chrono::seconds(1);
                    try {
                        exchange("notifications/cancelled", {{"requestId", id}}, delivery, true);
                    } catch (const Error&) {
                        // Preserve the caller's original cancellation or timeout.
                    }
                }
                throw;
            }
            if (method == "initialize" && mcp_http::legacy_version(protocol) &&
                response.status >= 200 && response.status < 300 &&
                response.headers.count("mcp-session-id"))
                session_id = response.headers.at("mcp-session-id");
            if (notify && response.status == 202)
                return nullptr;
            if (response.status < 200 || response.status >= 300) {
                try {
                    reply = parse_json(response.body, provider_message_limit);
                } catch (...) {
                    reply = nullptr;
                }
                // Represent any rejection as a JSON-RPC error for this request, so a server
                // that refuses server/discover (e.g. "not initialized", id null, or a plain HTTP
                // error) still lets connect() fall back to the legacy handshake.
                if (!reply.is_object() || !reply.contains("error"))
                    reply = rpc_error(id, -32600, "MCP HTTP request rejected");
                else if (!reply.contains("id") || reply.at("id").is_null())
                    reply["id"] = id;
            } else if (response.headers["content-type"].find("text/event-stream") !=
                       std::string::npos) {
                std::istringstream events(response.body);
                std::string line, data;
                while (std::getline(events, line)) {
                    if (!line.empty() && line.back() == '\r')
                        line.pop_back();
                    if (line.rfind("data:", 0) == 0)
                        data += line.substr(5) + "\n";
                    else if (line.empty() && !data.empty()) {
                        auto next = parse_json(data, provider_message_limit);
                        if (next.is_object() && next.contains("error") &&
                            next.value("id", Json()).is_null())
                            next["id"] = id;
                        if (next.value("id", Json()) == id)
                            reply = std::move(next);
                        data.clear();
                    }
                }
            } else {
                reply = parse_json(response.body, provider_message_limit);
                if (reply.is_object() && reply.contains("error") &&
                    reply.value("id", Json()).is_null())
                    reply["id"] = id;
            }
#else
            throw Error("Unsupported", "HTTP module disabled");
#endif
        }
        if (!reply.is_object() || reply.value("id", Json()) != id)
            throw Error("UnexpectedEOF", "No matching MCP response");
        return reply;
    }
    Json call(std::string method, Json params, const Cancellation& cancel) {
        PerformanceSpan span(cache_options.performance, "mcp.client", method, params);
        if (watch && observed_epoch != watch->epoch.load()) {
            observed_epoch = watch->epoch.load();
            clear_cache();
        }
        const auto epoch = watch ? watch->epoch.load() : 0;
        cancel.check();
        const bool cacheable = method == "tools/list" || method == "cymcp/tools/get" ||
                               method == "resources/list" || method == "resources/read" ||
                               method == "resources/templates/list";
        const auto key = cacheable ? method + "\n" + params.dump() : std::string();
        if (cacheable) {
            auto found = cache.find(key);
            if (found != cache.end()) {
                if (found->second.expires > Clock::now()) {
                    ++stats.hits;
                    found->second.used = ++cache_tick;
                    span.finish("ok", &found->second.value, true);
                    return found->second.value;
                }
                stats.bytes -= found->second.bytes;
                cache.erase(found);
                stats.entries = cache.size();
            }
            ++stats.misses;
        }
        auto response = exchange(method, params, cancel);
        if (response.contains("error"))
            throw Error("RemoteError", response["error"].dump());
        if (!response.contains("result"))
            throw Error("ProtocolError", "Missing MCP result");
        auto result = std::move(response["result"]);
        const bool changed = watch && epoch != watch->epoch.load();
        if (changed) {
            observed_epoch = watch->epoch.load();
            clear_cache();
        }
        if (!changed && cacheable && result.is_object() &&
            result.value("resultType", std::string("complete")) == "complete" &&
            result.contains("ttlMs") && result["ttlMs"].is_number_integer() &&
            cache_options.cache_entries && cache_options.cache_bytes &&
            cache_options.max_cache_ttl.count() > 0) {
            const auto ttl = std::min<std::int64_t>(result["ttlMs"].get<std::int64_t>(),
                                                    cache_options.max_cache_ttl.count());
            const auto bytes = result.dump().size();
            if (ttl > 0 && bytes <= cache_options.cache_bytes) {
                while (!cache.empty() && (cache.size() >= cache_options.cache_entries ||
                                          bytes > cache_options.cache_bytes - stats.bytes)) {
                    auto oldest = std::min_element(
                        cache.begin(), cache.end(),
                        [](const auto& a, const auto& b) { return a.second.used < b.second.used; });
                    stats.bytes -= oldest->second.bytes;
                    cache.erase(oldest);
                }
                cache.emplace(key, Cached{result, Clock::now() + std::chrono::milliseconds(ttl),
                                          bytes, ++cache_tick});
                stats.bytes += bytes;
                stats.entries = cache.size();
            }
        }
        span.finish(result.is_object() && result.value("isError", false) ? "ToolError" : "ok",
                    &result);
        return result;
    }
};
McpClient::McpClient(ProcessOptions options, McpClientOptions cache)
    : impl_(std::make_unique<Impl>()) {
    if (cache.max_cache_ttl.count() < 0 || cache.max_cache_ttl > std::chrono::hours(24))
        throw Error("InvalidOptions", "Invalid MCP cache TTL");
    impl_->cache_options = cache;
    impl_->options = std::move(options);
}
#ifdef CYMCP_HAS_HTTP
McpClient::McpClient(std::string url, std::string token, McpClientOptions cache)
    : impl_(std::make_unique<Impl>()) {
    if (cache.max_cache_ttl.count() < 0 || cache.max_cache_ttl > std::chrono::hours(24))
        throw Error("InvalidOptions", "Invalid MCP cache TTL");
    impl_->cache_options = cache;
    parse_url(url);
    impl_->url = std::move(url);
    impl_->token = std::move(token);
}
#endif
McpClient::~McpClient() {
    impl_->close();
}
void McpClient::start_notifications(Json filter, const Cancellation& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    if (p.protocol != modern)
        throw Error("Unsupported",
                    "This peer uses legacy MCP; use TTL or explicit cache invalidation");
    p.stop_watch();
    p.watch = std::make_unique<Impl::Watch>();
    auto* w = p.watch.get();
    w->id = "subscription-" + random_token(16);
    Json message = {{"jsonrpc", "2.0"},
                    {"id", w->id},
                    {"method", "subscriptions/listen"},
                    {"params", {{"_meta", meta()}, {"notifications", std::move(filter)}}}};
    if (p.process) {
        try {
            p.process->write(message.dump() + "\n", input);
        } catch (...) {
            p.watch.reset();
            throw;
        }
        auto* process = p.process.get();
        w->worker = std::jthread([w, process] {
            try {
                ProcessLine line;
                while (process->next(line, w->lifetime)) {
                    if (!line.stderr_stream)
                        w->route(parse_json(line.text, provider_message_limit));
                }
                w->finish("UnexpectedEOF");
            } catch (const Error& e) {
                if (!w->lifetime.cancelled())
                    w->finish(e.code);
            } catch (...) {
                w->finish("ProtocolError");
            }
        });
    } else {
#ifdef CYMCP_HAS_HTTP
        HttpRequest req;
        req.method = "POST";
        req.url = p.url;
        req.body = message.dump();
        req.timeout = std::chrono::milliseconds(0);
        req.max_response_bytes = std::numeric_limits<std::size_t>::max();
        req.error_body_limit = 4096;
        req.headers = {{"Content-Type", "application/json"},
                       {"Accept", "text/event-stream"},
                       {"MCP-Protocol-Version", modern},
                       {"Mcp-Method", "subscriptions/listen"}};
        if (!p.token.empty())
            req.headers["Authorization"] = "Bearer " + p.token;
        w->worker = std::jthread([w, req = std::move(req)] {
            try {
                std::string line, data;
                auto result =
                    HttpClient().perform(req, w->lifetime, [&](const char* b, std::size_t n) {
                        w->stream(b, n, line, data);
                        return !w->lifetime.cancelled();
                    });
                w->finish(result.status == 200 ? "UnexpectedEOF" : "RemoteError");
            } catch (const Error& e) {
                if (!w->lifetime.cancelled())
                    w->finish(e.code);
            } catch (...) {
                w->finish("ProtocolError");
            }
        });
#else
        p.watch.reset();
        throw Error("Unsupported", "HTTP module disabled");
#endif
    }
    auto cancel = input;
    if (cancel.deadline == Clock::time_point::max())
        cancel.deadline = Clock::now() + std::chrono::seconds(10);
    try {
        std::unique_lock wait(w->mutex);
        while (!w->acknowledged && !w->ended) {
            cancel.check();
            w->wake.wait_for(wait, std::chrono::milliseconds(25));
        }
        if (!w->acknowledged)
            throw Error("Unsupported", "Peer did not accept change notifications");
    } catch (...) {
        p.stop_watch();
        throw;
    }
    p.observed_epoch = w->epoch.load();
    p.clear_cache();
}
void McpClient::stop_notifications() noexcept {
    std::lock_guard lock(impl_->mutex);
    impl_->stop_watch();
}
Json McpClient::drain_notifications() {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    Json rows = Json::array();
    if (p.watch) {
        std::lock_guard inner(p.watch->mutex);
        for (auto& n : p.watch->notifications)
            rows.push_back(std::move(n));
        p.watch->notifications.clear();
    }
    return rows;
}
Json McpClient::notification_status() const {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.watch)
        return {{"running", false}};
    std::lock_guard inner(p.watch->mutex);
    return {{"running", p.watch->acknowledged && !p.watch->ended},
            {"error", p.watch->error},
            {"filter", p.watch->accepted},
            {"revision", p.watch->epoch.load()}};
}
void McpClient::connect(const Cancellation& input) {
    auto cancel = input;
    if (cancel.deadline == Clock::time_point::max())
        cancel.deadline = Clock::now() + std::chrono::seconds(30);
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (p.connected)
        throw Error("InvalidState", "MCP client already connected");
    p.protocol = modern;
    p.clear_cache();
    p.tool_lookup = false;
    try {
        if (p.url.empty())
            p.process = std::make_unique<Process>(p.options);
        auto discovery = p.exchange("server/discover", Json::object(), cancel);
        if (discovery.contains("error")) {
            if (discovery["error"].value("code", 0) == -32022)
                throw Error("Unsupported",
                            "MCP server does not support the selected modern revision");
            p.protocol = legacy;
            const auto negotiated =
                p.call("initialize",
                       {{"protocolVersion", legacy},
                        {"capabilities", Json::object()},
                        {"clientInfo", {{"name", "CyMCPSDK"}, {"version", version}}}},
                       cancel);
            if (!negotiated.is_object() || !negotiated.contains("protocolVersion") ||
                !negotiated.at("protocolVersion").is_string())
                throw Error("ProtocolError", "Missing negotiated MCP protocol version");
            p.protocol = negotiated.at("protocolVersion").get<std::string>();
            if (!mcp_http::legacy_version(p.protocol))
                throw Error("Unsupported", "Peer negotiated an unsupported MCP protocol version");
            p.tool_lookup = negotiated.value("capabilities", Json::object())
                                .value("experimental", Json::object())
                                .contains("com.cymcp/toolLookup");
            p.exchange("notifications/initialized", Json::object(), cancel, true);
        } else if (!discovery.contains("result") ||
                   !discovery["result"].contains("supportedVersions"))
            throw Error("ProtocolError", "Invalid discovery response");
        if (discovery.contains("result"))
            p.tool_lookup = discovery["result"]
                                .value("capabilities", Json::object())
                                .value("experimental", Json::object())
                                .contains("com.cymcp/toolLookup");
        p.connected = true;
    } catch (...) {
        p.close();
        throw;
    }
}
Json McpClient::list_tools(const Cancellation& input) {
    auto cancel = input;
    if (cancel.deadline == Clock::time_point::max())
        cancel.deadline = Clock::now() + std::chrono::seconds(120);
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    return p.list_tools(cancel);
}
Json McpClient::Impl::list_tools(const Cancellation& cancel) {
    Json tools = Json::array();
    Json params = Json::object();
    std::set<std::string> seen;
    HeaderCatalogue headers;
    std::size_t count = 0;
    for (int page = 0; page < 1000; ++page) {
        auto result = call("tools/list", params, cancel);
        if (!result.contains("tools") || !result["tools"].is_array())
            throw Error("ProtocolError", "Missing tool catalogue");
        for (const auto& tool : result["tools"]) {
            if (++count > 10000)
                throw Error("ResponseTooLarge", "Tool catalogue exceeds limit");
            if (!url.empty() && protocol == modern) {
                if (!tool.is_object() || !tool.contains("name") || !tool.at("name").is_string() ||
                    !tool.contains("inputSchema"))
                    throw Error("ProtocolError", "Invalid MCP tool definition");
                auto& entry = headers[tool.at("name").get_ref<const std::string&>()];
                HeaderParameters parameters;
                std::set<std::string> names;
                if (!collect_headers(tool.at("inputSchema"), Json::json_pointer(), true, parameters,
                                     names)) {
                    entry.reset();
                    continue;
                }
                entry = std::move(parameters);
            }
            tools.push_back(tool);
        }
        if (!result.contains("nextCursor")) {
            tool_headers = std::move(headers);
            return tools;
        }
        auto cursor = result.at("nextCursor").get<std::string>();
        if (!seen.insert(cursor).second)
            throw Error("ProtocolError", "Cyclic tool pagination");
        params["cursor"] = cursor;
    }
    throw Error("ResponseTooLarge", "Too many catalogue pages");
}
Json McpClient::call_tool(const std::string& name, const Json& args, const Cancellation& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    try {
        if (!p.url.empty() && p.protocol == modern) {
            if (p.tool_lookup) {
                const auto tool = p.call("cymcp/tools/get", {{"name", name}}, input).at("tool");
                HeaderParameters parameters;
                std::set<std::string> names;
                const bool valid = collect_headers(tool.at("inputSchema"), Json::json_pointer(),
                                                   true, parameters, names);
                if (!p.tool_headers)
                    p.tool_headers.emplace();
                (*p.tool_headers)[name] =
                    valid ? std::optional<HeaderParameters>(std::move(parameters)) : std::nullopt;
            } else if (!p.tool_headers)
                p.list_tools(input);
        }
        auto result = p.call("tools/call", {{"name", name}, {"arguments", args}}, input);
        if (result.value("isError", false))
            p.clear_cache();
        return result;
    } catch (const Error& e) {
        if (e.code == "UnexpectedEOF" || e.code == "ProcessClosed")
            p.close();
        throw;
    }
}
Json McpClient::list_tools_page(std::optional<std::string> cursor, const Cancellation& cancel) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    Json args = Json::object();
    if (cursor)
        args["cursor"] = std::move(*cursor);
    return p.call("tools/list", std::move(args), cancel);
}
Json McpClient::get_tool(const std::string& name, const Cancellation& cancel) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    if (p.tool_lookup)
        return p.call("cymcp/tools/get", {{"name", name}}, cancel).at("tool");
    for (const auto& tool : p.list_tools(cancel))
        if (tool.at("name") == name)
            return tool;
    throw Error("UnknownOperation", "Peer does not expose this tool");
}
Json McpClient::list_resources(const Cancellation& cancel) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    Json all = Json::array(), params = Json::object();
    std::set<std::string> seen;
    for (std::size_t page = 0; page < 1000; ++page) {
        auto result = p.call("resources/list", params, cancel);
        if (!result.contains("resources") || !result.at("resources").is_array())
            throw Error("ProtocolError", "Missing resource catalogue");
        for (const auto& resource : result.at("resources")) {
            if (all.size() >= 10000)
                throw Error("ResponseTooLarge", "Resource catalogue exceeds limit");
            all.push_back(resource);
        }
        if (!result.contains("nextCursor"))
            return all;
        const auto cursor = result.at("nextCursor").get<std::string>();
        if (!seen.insert(cursor).second)
            throw Error("ProtocolError", "Cyclic resource pagination");
        params["cursor"] = cursor;
    }
    throw Error("ResponseTooLarge", "Too many resource pages");
}
Json McpClient::read_resource(const std::string& uri, const Cancellation& cancel) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (!p.connected)
        throw Error("InvalidState", "Connect MCP client first");
    return p.call("resources/read", {{"uri", uri}}, cancel);
}
void McpClient::clear_cache() {
    std::lock_guard lock(impl_->mutex);
    impl_->clear_cache();
}
McpCacheStats McpClient::cache_stats() const {
    std::lock_guard lock(impl_->mutex);
    return impl_->stats;
}
} // namespace cymcp
