#include "mcp_headers.hpp"
#include <cymcp/http.hpp>
#include <httplib.h>
namespace cymcp {
namespace {
using mcp_http::legacy_version;
bool valid_mcp_headers(const httplib::Request& req, const Json& message) {
    if (!message.is_object())
        return true;
    const auto params = message.find("params");
    const Json* metadata = nullptr;
    if (params != message.end() && params->is_object()) {
        const auto it = params->find("_meta");
        if (it != params->end() && it->is_object())
            metadata = &*it;
    }
    const auto version = req.get_header_value("MCP-Protocol-Version");
    const bool body_version = metadata && metadata->contains(mcp_http::version_key);
    if (!body_version && version != mcp_http::modern)
        return req.get_header_value_count("MCP-Protocol-Version") <= 1 &&
               (version.empty() || legacy_version(version));
    if (!body_version || req.get_header_value_count("MCP-Protocol-Version") != 1 ||
        metadata->at(mcp_http::version_key) != version)
        return false;
    const auto method = message.find("method");
    if (method == message.end() || !method->is_string() ||
        req.get_header_value_count("Mcp-Method") != 1 ||
        *method != req.get_header_value("Mcp-Method"))
        return false;
    if (auto field = mcp_http::name_field(method->get_ref<const std::string&>())) {
        const auto name = params->find(field);
        return name != params->end() && name->is_string() &&
               req.get_header_value_count("Mcp-Name") == 1 &&
               mcp_http::matches(req.get_header_value("Mcp-Name"),
                                 name->get_ref<const std::string&>());
    }
    return true;
}
} // namespace
int available_loopback_port() {
    httplib::Server probe;
    auto port = probe.bind_to_any_port("127.0.0.1");
    if (port < 1)
        throw Error("NetworkError", "Cannot allocate loopback port");
    probe.stop();
    return port;
}
struct LocalHttpServer::Impl {
    httplib::Server server;
    std::thread thread;
    std::mutex lifecycle;
    std::string token;
    JsonEndpoint endpoint;
    Cancellation shutdown;
    struct LegacySession {
        std::string protocol;
        Cancellation lifetime;
        Clock::time_point used;
    };
    std::mutex session_mutex;
    std::map<std::string, std::shared_ptr<LegacySession>> legacy_sessions;
    std::string create_session(std::string protocol) {
        auto id = random_token(32);
        std::lock_guard lock(session_mutex);
        // Clients may disappear without DELETE. Retain recent idle sessions and never evict
        // a session while any HTTP worker holds it.
        if (legacy_sessions.size() >= 1024) {
            auto oldest = legacy_sessions.end();
            for (auto it = legacy_sessions.begin(); it != legacy_sessions.end(); ++it)
                if (it->second.use_count() == 1 &&
                    (oldest == legacy_sessions.end() || it->second->used < oldest->second->used))
                    oldest = it;
            if (oldest == legacy_sessions.end())
                throw Error("Busy", "Legacy session capacity reached");
            legacy_sessions.erase(oldest);
        }
        legacy_sessions.emplace(id, std::make_shared<LegacySession>(LegacySession{
                                        std::move(protocol), shutdown.child(), Clock::now()}));
        return id;
    }
    std::shared_ptr<LegacySession> session_for(const httplib::Request& req,
                                               httplib::Response& res) {
        if (req.get_header_value_count("Mcp-Session-Id") != 1) {
            res.status = 400;
            return {};
        }
        std::lock_guard lock(session_mutex);
        auto it = legacy_sessions.find(req.get_header_value("Mcp-Session-Id"));
        if (it == legacy_sessions.end()) {
            res.status = 404;
            return {};
        }
        auto session = it->second;
        const auto version = req.get_header_value("MCP-Protocol-Version");
        if (!version.empty() && version != session->protocol) {
            res.status = 400;
            return {};
        }
        session->used = Clock::now();
        if (req.method == "DELETE") {
            session->lifetime.cancel();
            legacy_sessions.erase(it);
        }
        return session;
    }
    Impl(std::string key, JsonEndpoint fn, std::size_t workers)
        : token(std::move(key)), endpoint(std::move(fn)) {
        if (token.size() < 32 || !endpoint || workers < 1 || workers > 64)
            throw Error("InvalidOptions",
                        "Server requires a strong token, endpoint and worker limit");
        server.new_task_queue = [workers] {
            return new httplib::ThreadPool(workers, workers, 128, 30);
        };
        server.set_payload_max_length(provider_message_limit);
        server.set_read_timeout(10, 0);
        server.set_write_timeout(10, 0);
        server.set_keep_alive_max_count(100);
        auto handler = [this](const httplib::Request& req, httplib::Response& res) {
            if (req.has_header("Origin")) {
                res.status = 403;
                return;
            }
            auto host = req.get_header_value("Host");
            if (host != "127.0.0.1" && host.rfind("127.0.0.1:", 0) != 0 && host != "localhost" &&
                host.rfind("localhost:", 0) != 0) {
                res.status = 403;
                return;
            }
            if (!constant_time_equal(req.get_header_value("Authorization"), "Bearer " + token)) {
                res.status = 401;
                res.set_header("WWW-Authenticate", "Bearer");
                return;
            }
            if (req.method == "GET") {
                // This transport has no standalone SSE stream, including on legacy sessions.
                res.status = 405;
                return;
            }
            if (req.method == "DELETE") {
                if (req.get_header_value("MCP-Protocol-Version") == mcp_http::modern)
                    res.status = 405;
                else if (session_for(req, res))
                    res.status = 204;
                return;
            }
            if (req.get_header_value("Content-Type").rfind("application/json", 0) != 0) {
                res.status = 415;
                return;
            }
            try {
                auto value = parse_json(req.body, provider_message_limit);
                if (req.path == "/mcp" && !valid_mcp_headers(req, value)) {
                    res.status = 400;
                    Json error = {
                        {"jsonrpc", "2.0"},
                        {"id", value.value("id", Json())},
                        {"error", {{"code", -32020}, {"message", "MCP header mismatch"}}}};
                    res.set_content(error.dump(), "application/json");
                    return;
                }
                CallContext context;
                context.principal = "authenticated-local";
                // Modern HTTP uses a separate channel per POST. In legacy mode a cancellation
                // arrives in another POST belonging to the same initialized session.
                static std::atomic<std::uint64_t> channel{0};
                context.transport_scope = "http:" + std::to_string(++channel);
                context.cancellation = shutdown.child();
                const auto protocol = req.get_header_value("MCP-Protocol-Version");
                const bool legacy = req.path == "/mcp" && value.is_object() &&
                                    value.value("jsonrpc", Json()) == "2.0" &&
                                    value.contains("method") &&
                                    (protocol.empty() || legacy_version(protocol));
                const bool initialize = legacy && value.at("method") == "initialize";
                std::shared_ptr<LegacySession> session;
                if (initialize && req.has_header("Mcp-Session-Id")) {
                    res.status = 400;
                    return;
                }
                if (legacy && !initialize) {
                    session = session_for(req, res);
                    if (!session)
                        return;
                    context.transport_scope = "legacy:" + req.get_header_value("Mcp-Session-Id");
                    context.cancellation = session->lifetime.child();
                }
                std::jthread watch_connection([&](std::stop_token stop) {
                    std::mutex mutex;
                    std::condition_variable_any wake;
                    std::unique_lock wait(mutex);
                    while (!stop.stop_requested()) {
                        if (req.is_connection_closed()) {
                            context.cancellation.cancel();
                            return;
                        }
                        wake.wait_for(wait, stop, std::chrono::milliseconds(20),
                                      [] { return false; });
                    }
                });
                if (req.path == "/mcp" && !legacy && value.is_object() &&
                    value.value("method", Json()) == "subscriptions/listen") {
                    // The SSE provider runs while its request/socket remain alive. Its cancellation
                    // is tied to server shutdown and heartbeat write failures.
                    res.set_chunked_content_provider(
                        "text/event-stream", [fn = endpoint, value, context](
                                                 std::size_t, httplib::DataSink& sink) mutable {
                            context.notifications = [&](const Json& message) {
                                auto bytes = message.is_null() ? std::string(": keepalive\n\n")
                                                               : "data: " + message.dump() + "\n\n";
                                if (!sink.write(bytes.data(), bytes.size()))
                                    context.cancellation.cancel();
                            };
                            try {
                                auto result = fn(value, context);
                                if (!result.is_null())
                                    context.notifications(result);
                            } catch (...) {
                            }
                            sink.done();
                            return true;
                        });
                    return;
                }
                auto reply = endpoint(value, context);
                if (reply.is_null()) {
                    res.status = 202;
                    return;
                }
                if (initialize && reply.contains("result") && reply.at("result").is_object()) {
                    const auto negotiated =
                        reply.at("result").value("protocolVersion", std::string());
                    if (legacy_version(negotiated))
                        res.set_header("Mcp-Session-Id", create_session(negotiated));
                }
                res.set_content(reply.dump(), "application/json");
            } catch (const Json::exception&) {
                res.status = 400;
                res.set_content(
                    Json(
                        {{"error", {{"code", "InvalidJson"}, {"message", "Invalid JSON request"}}}})
                        .dump(),
                    "application/json");
            } catch (const Error& e) {
                res.status = e.code == "AccessDenied" ? 403 : 400;
                res.set_content(Json({{"error", {{"code", e.code}, {"message", e.what()}}}}).dump(),
                                "application/json");
            } catch (...) {
                res.status = 500;
                res.set_content("{\"error\":{\"code\":\"InternalError\"}}", "application/json");
            }
        };
        server.Post("/mcp", handler);
        server.Get("/mcp", handler);
        server.Delete("/mcp", handler);
        server.Post("/v1", handler);
        server.Get("/health", [](const auto&, auto& res) {
            res.set_content("{\"status\":\"ready\"}", "application/json");
        });
    }
};
LocalHttpServer::LocalHttpServer(std::string token, JsonEndpoint endpoint, std::size_t workers)
    : impl_(std::make_unique<Impl>(std::move(token), std::move(endpoint), workers)) {}
LocalHttpServer::~LocalHttpServer() {
    stop();
}
int LocalHttpServer::start(int port) {
    auto& p = *impl_;
    std::lock_guard lock(p.lifecycle);
    if (p.thread.joinable() || port < 0 || port > 65535)
        throw Error("InvalidState", "Server already started or invalid port");
    int actual = port;
    if (port == 0)
        actual = p.server.bind_to_any_port("127.0.0.1");
    else if (!p.server.bind_to_port("127.0.0.1", port))
        actual = -1;
    if (actual < 1)
        throw Error("NetworkError", "Cannot bind loopback server");
    p.shutdown = Cancellation();
    p.thread = std::thread([&p] { p.server.listen_after_bind(); });
    p.server.wait_until_ready();
    return actual;
}
void LocalHttpServer::stop() noexcept {
    if (!impl_)
        return;
    std::lock_guard lock(impl_->lifecycle);
    impl_->shutdown.cancel();
    impl_->server.stop();
    if (impl_->thread.joinable())
        impl_->thread.join();
    std::lock_guard sessions(impl_->session_mutex);
    impl_->legacy_sessions.clear();
}
} // namespace cymcp
