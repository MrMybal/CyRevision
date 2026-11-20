#pragma once
#include "core.hpp"
namespace cymcp {
struct HttpRequest {
    std::string method = "GET", url, body;
    std::map<std::string, std::string> headers;
    std::size_t max_response_bytes = 16 * 1024 * 1024;
    // Total deadline; 0 means none (cancellation still applies).
    std::chrono::milliseconds timeout{120000};
    // Longest silence while sending or receiving; 0 uses the total deadline. A stream that
    // keeps producing bytes is never cut by this value.
    std::chrono::milliseconds idle_timeout{0};
    // With a streaming sink, buffer non-2xx bodies separately up to this size. An
    // oversized error yields an empty body and its HTTP status. Zero streams all statuses.
    std::size_t error_body_limit = 0;
};
struct HttpResponse {
    int status = 0;
    std::map<std::string, std::string> headers;
    std::string body;
};
// Receive chunks on the calling worker thread. Return false to cancel reception.
using ChunkSink = std::function<bool(const char*, std::size_t)>;
class HttpClient {
  public:
    HttpResponse perform(const HttpRequest&, const Cancellation& = {}, ChunkSink = {}) const;
};
struct Url {
    std::string scheme, host, target;
    int port = 0;
    bool loopback = false;
};
Url parse_url(const std::string&);
// Host authentication returns an opaque identity from the transport Bearer credential.
using TokenAuthenticator = std::function<std::string(std::string_view)>;
using JsonEndpoint = std::function<Json(const Json&, const CallContext&)>;

// Presets for hosts that expose their operations over loopback HTTP. The host applies
// control_operations when it constructs its Registry policy. The transport only authenticates.
enum class LocalAccessProfile { free, local_controlled, token_only, protected_access };
struct LocalAccessSettings {
    bool require_token = true;
    bool control_operations = true;
    constexpr bool operator==(const LocalAccessSettings&) const noexcept = default;
};
constexpr LocalAccessSettings local_access_settings(LocalAccessProfile profile) noexcept {
    switch (profile) {
    case LocalAccessProfile::free:
        return {false, false};
    case LocalAccessProfile::local_controlled:
        return {false, true};
    case LocalAccessProfile::token_only:
        return {true, false};
    case LocalAccessProfile::protected_access:
        return {true, true};
    }
    return {};
}

class LocalHttpServer {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    // Explicit opt-in. Every local caller shares an unauthenticated identity on /mcp and /v1.
    struct UnauthenticatedLoopback {};

    LocalHttpServer(std::string token, JsonEndpoint endpoint, std::size_t workers = 4,
                    std::size_t max_request_bytes = provider_message_limit);
    LocalHttpServer(TokenAuthenticator, JsonEndpoint endpoint, std::size_t workers = 4,
                    std::size_t max_request_bytes = provider_message_limit);
    LocalHttpServer(UnauthenticatedLoopback, JsonEndpoint endpoint, std::size_t workers = 4,
                    std::size_t max_request_bytes = provider_message_limit);
    ~LocalHttpServer();
    LocalHttpServer(const LocalHttpServer&) = delete;
    LocalHttpServer& operator=(const LocalHttpServer&) = delete;
    int start(int port = 0);
    void stop() noexcept;
};
} // namespace cymcp
