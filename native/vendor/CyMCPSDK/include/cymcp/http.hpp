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
using JsonEndpoint = std::function<Json(const Json&, const CallContext&)>;
class LocalHttpServer {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    LocalHttpServer(std::string token, JsonEndpoint endpoint, std::size_t workers = 4);
    ~LocalHttpServer();
    LocalHttpServer(const LocalHttpServer&) = delete;
    LocalHttpServer& operator=(const LocalHttpServer&) = delete;
    int start(int port = 0);
    void stop() noexcept;
};
} // namespace cymcp
