#include <algorithm>
#include <array>
#include <cctype>
#include <cymcp/http.hpp>
#include <limits>
#include <sstream>
#ifdef _WIN32
#include <windows.h>
#include <winhttp.h>
#else
#include <curl/curl.h>
#endif
namespace cymcp {
Url parse_url(const std::string& input) {
    if (input.size() > 8192 ||
        std::any_of(input.begin(), input.end(),
                    [](unsigned char c) { return c <= 32 || c == 127; }) ||
        input.find('\\') != std::string::npos || input.find('#') != std::string::npos)
        throw Error("InvalidUrl", "Invalid URL");
    auto colon = input.find("://");
    if (colon == std::string::npos)
        throw Error("InvalidUrl", "URL must include a scheme");
    Url u;
    u.scheme = input.substr(0, colon);
    if (u.scheme != "http" && u.scheme != "https")
        throw Error("InvalidUrl", "Only HTTP(S) URLs supported");
    auto end = input.find_first_of("/?", colon + 3);
    std::string authority =
        input.substr(colon + 3, end == std::string::npos ? end : end - colon - 3);
    if (authority.empty() || authority.find('@') != std::string::npos)
        throw Error("InvalidUrl", "URL credentials are forbidden");
    u.port = u.scheme == "https" ? 443 : 80;
    std::string port;
    if (authority[0] == '[') {
        auto b = authority.find(']');
        if (b == std::string::npos)
            throw Error("InvalidUrl", "Malformed IPv6 host");
        u.host = authority.substr(1, b - 1);
        if (b + 1 < authority.size()) {
            if (authority[b + 1] != ':')
                throw Error("InvalidUrl", "Malformed authority");
            port = authority.substr(b + 2);
        }
    } else {
        auto b = authority.find(':');
        u.host = authority.substr(0, b);
        if (b != std::string::npos)
            port = authority.substr(b + 1);
    }
    if (u.host.empty())
        throw Error("InvalidUrl", "Host is required");
    if (authority.back() == ':' ||
        u.host.find_first_not_of(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-:") !=
            std::string::npos)
        throw Error("InvalidUrl", "Invalid host or port");
    if (!port.empty()) {
        if (port.size() > 5 || port.find_first_not_of("0123456789") != std::string::npos)
            throw Error("InvalidUrl", "Invalid port");
        u.port = std::stoi(port);
        if (!u.port || u.port > 65535)
            throw Error("InvalidUrl", "Invalid port");
    }
    std::transform(u.host.begin(), u.host.end(), u.host.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    u.loopback = u.host == "localhost" || u.host == "127.0.0.1" || u.host == "::1";
    if (u.scheme == "http" && !u.loopback)
        throw Error("InsecureTransport", "Remote endpoints require HTTPS");
    u.target = end == std::string::npos ? "/" : input.substr(end);
    if (u.target[0] == '?')
        u.target = "/" + u.target;
    return u;
}
namespace {
std::string lower(std::string s) {
    std::transform(s.begin(), s.end(), s.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return s;
}
void validate_request(const HttpRequest& r) {
    if (r.method != "GET" && r.method != "POST" && r.method != "DELETE" && r.method != "HEAD")
        throw Error("InvalidOptions", "Unsupported HTTP method");
    if (r.timeout.count() < 0 || r.idle_timeout.count() < 0 ||
        r.timeout > std::chrono::hours(24 * 365) || r.idle_timeout > std::chrono::hours(24) ||
        !r.max_response_bytes)
        throw Error("InvalidOptions", "Invalid HTTP limits");
    for (const auto& [key, value] : r.headers)
        if (key.empty() ||
            key.find_first_not_of(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") !=
                std::string::npos ||
            value.find_first_of("\r\n") != std::string::npos ||
            value.find('\0') != std::string::npos)
            throw Error("InvalidOptions", "Invalid HTTP header");
}
#ifdef _WIN32
std::wstring wide_http(const std::string& s) {
    if (s.empty())
        return {};
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()),
                                nullptr, 0);
    if (!n)
        throw Error("InvalidEncoding", "Expected UTF-8");
    std::wstring out(n, 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()),
                        out.data(), n);
    return out;
}
struct Internet {
    std::atomic<HINTERNET> value{nullptr};
    explicit Internet(HINTERNET h) : value(h) {
        if (!h)
            throw Error("NetworkError", "Cannot open HTTP connection");
    }
    ~Internet() {
        close();
    }
    void close() {
        auto h = value.exchange(nullptr);
        if (h)
            WinHttpCloseHandle(h);
    }
    HINTERNET get() const {
        return value.load();
    }
};
#endif
} // namespace
HttpResponse HttpClient::perform(const HttpRequest& request, const Cancellation& input,
                                 ChunkSink sink) const {
    validate_request(request);
    auto url = parse_url(request.url);
    Cancellation cancel = input;
    if (request.timeout.count() > 0)
        cancel.deadline = std::min(cancel.deadline, Clock::now() + request.timeout);
    cancel.check();
    HttpResponse result;
    std::size_t received = 0;
    // Idle detection shared by both back ends: any received byte restarts the interval.
    std::atomic<std::int64_t> last_activity{Clock::now().time_since_epoch().count()};
    std::atomic_bool idle_expired{false};
    auto accept = [&](const char* data, std::size_t size) {
        last_activity.store(Clock::now().time_since_epoch().count());
        cancel.check();
        if (size > request.max_response_bytes - std::min(received, request.max_response_bytes))
            throw Error("ResponseTooLarge", "HTTP response exceeds limit");
        received += size;
        if (sink && request.error_body_limit && (result.status < 200 || result.status >= 300)) {
            if (received > request.error_body_limit) {
                result.body.clear();
                return false;
            }
            result.body.append(data, size);
        } else if (sink) {
            if (!sink(data, size))
                throw Error("Cancelled", "Response consumer stopped");
        } else
            result.body.append(data, size);
        return true;
    };
#ifdef _WIN32
    auto user_agent = wide_http(std::string("CyMCPSDK/") + version);
    Internet session(WinHttpOpen(user_agent.c_str(),
                                 url.loopback ? WINHTTP_ACCESS_TYPE_NO_PROXY
                                              : WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                                 WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0));
    // WinHTTP send/receive timeouts are per operation: they bound silence, not a whole stream.
    // Zero means infinite; the total deadline is enforced by the watcher below.
    const auto idle = request.idle_timeout.count() > 0 ? request.idle_timeout : request.timeout;
    auto timeout =
        static_cast<int>(std::min<std::int64_t>(idle.count(), std::numeric_limits<int>::max()));
    WinHttpSetTimeouts(session.get(), timeout ? std::min(timeout, 10000) : 10000,
                       timeout ? std::min(timeout, 10000) : 10000, timeout, timeout);
    Internet connection(WinHttpConnect(session.get(), wide_http(url.host).c_str(),
                                       static_cast<INTERNET_PORT>(url.port), 0));
    Internet handle(WinHttpOpenRequest(connection.get(), wide_http(request.method).c_str(),
                                       wide_http(url.target).c_str(), nullptr, WINHTTP_NO_REFERER,
                                       WINHTTP_DEFAULT_ACCEPT_TYPES,
                                       url.scheme == "https" ? WINHTTP_FLAG_SECURE : 0));
    // Waiting for the response headers is a silence too (default would be 90 s, or infinite
    // for a zero idle value).
    DWORD header_wait = static_cast<DWORD>(timeout);
    WinHttpSetOption(handle.get(), WINHTTP_OPTION_RECEIVE_RESPONSE_TIMEOUT, &header_wait,
                     sizeof(header_wait));
    DWORD disabled = WINHTTP_DISABLE_REDIRECTS;
    WinHttpSetOption(handle.get(), WINHTTP_OPTION_DISABLE_FEATURE, &disabled, sizeof(disabled));
    std::jthread watcher([&](std::stop_token stop) {
        std::mutex mutex;
        std::condition_variable_any wake;
        std::unique_lock wait(mutex);
        while (!stop.stop_requested()) {
            if (cancel.cancelled() || Clock::now() >= cancel.deadline) {
                handle.close();
                return;
            }
            if (request.idle_timeout.count() > 0 &&
                Clock::now() - Clock::time_point(Clock::duration(last_activity.load())) >
                    request.idle_timeout) {
                idle_expired.store(true);
                handle.close();
                return;
            }
            wake.wait_for(wait, stop, std::chrono::milliseconds(20), [] { return false; });
        }
    });
    std::wstring headers;
    for (const auto& [key, value] : request.headers)
        headers += wide_http(key + ": " + value + "\r\n");
    auto failed = [&] {
        const auto code = GetLastError();
        cancel.check();
        if (code == ERROR_WINHTTP_TIMEOUT || idle_expired.load())
            throw Error("Timeout", "HTTP connection stayed silent beyond its idle timeout");
        throw Error("NetworkError",
                    "HTTP request failed (system code " + std::to_string(code) + ")");
    };
    if (request.body.size() > std::numeric_limits<DWORD>::max())
        throw Error("InvalidOptions", "Request body too large");
    if (!WinHttpSendRequest(
            handle.get(), headers.empty() ? WINHTTP_NO_ADDITIONAL_HEADERS : headers.c_str(),
            static_cast<DWORD>(headers.size()),
            request.body.empty() ? WINHTTP_NO_REQUEST_DATA : const_cast<char*>(request.body.data()),
            static_cast<DWORD>(request.body.size()), static_cast<DWORD>(request.body.size()), 0) ||
        !WinHttpReceiveResponse(handle.get(), nullptr))
        failed();
    DWORD status = 0, size = sizeof(status);
    if (!WinHttpQueryHeaders(handle.get(), WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                             WINHTTP_HEADER_NAME_BY_INDEX, &status, &size, WINHTTP_NO_HEADER_INDEX))
        failed();
    result.status = static_cast<int>(status);
    last_activity.store(Clock::now().time_since_epoch().count());
    DWORD header_size = 0;
    WinHttpQueryHeaders(handle.get(), WINHTTP_QUERY_RAW_HEADERS_CRLF, nullptr, nullptr,
                        &header_size, nullptr);
    if (header_size > 65536)
        throw Error("ResponseTooLarge", "HTTP headers exceed limit");
    std::wstring raw(header_size / sizeof(wchar_t), 0);
    if (header_size && WinHttpQueryHeaders(handle.get(), WINHTTP_QUERY_RAW_HEADERS_CRLF, nullptr,
                                           raw.data(), &header_size, nullptr)) {
        std::string ascii;
        for (auto c : raw)
            if (c > 0 && c < 128)
                ascii += static_cast<char>(c);
        std::istringstream stream(ascii);
        std::string line;
        while (std::getline(stream, line)) {
            auto p = line.find(':');
            if (p == std::string::npos)
                continue;
            auto value = line.substr(p + 1);
            while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front())))
                value.erase(value.begin());
            while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back())))
                value.pop_back();
            result.headers[lower(line.substr(0, p))] = value;
        }
    }
    std::array<char, 16384> buffer{};
    while (true) {
        DWORD wanted = static_cast<DWORD>(buffer.size());
        if (sink) {
            // A synchronous ReadData tries to fill its buffer. For SSE/NDJSON,
            // read only the bytes already available instead of waiting for EOF.
            DWORD available = 0;
            if (!WinHttpQueryDataAvailable(handle.get(), &available))
                failed();
            if (!available)
                break;
            wanted = std::min(wanted, available);
        }
        DWORD bytes = 0;
        if (!WinHttpReadData(handle.get(), buffer.data(), wanted, &bytes))
            failed();
        if (!bytes)
            break;
        if (!accept(buffer.data(), bytes))
            break;
    }
    watcher.request_stop();
    if (idle_expired.load())
        throw Error("Timeout", "HTTP connection stayed silent beyond its idle timeout");
#else
    static const auto initialized = curl_global_init(CURL_GLOBAL_DEFAULT);
    if (initialized != CURLE_OK)
        throw Error("NetworkError", "Cannot initialize TLS transport");
    CURL* handle = curl_easy_init();
    if (!handle)
        throw Error("NetworkError", "Cannot initialize HTTP request");
    struct Cleanup {
        CURL* handle;
        curl_slist* headers = nullptr;
        ~Cleanup() {
            curl_slist_free_all(headers);
            curl_easy_cleanup(handle);
        }
    } cleanup{handle};
    struct State {
        decltype(accept)* receive;
        Cancellation cancel;
        HttpResponse* response;
        CURL* handle;
        std::atomic<std::int64_t>& activity;
        curl_off_t uploaded = 0;
        std::exception_ptr error;
        std::size_t headers = 0;
        bool error_body_discarded = false;
    } state{&accept, cancel, &result, handle, last_activity, 0, {}, 0, false};
    curl_easy_setopt(handle, CURLOPT_URL, request.url.c_str());
    curl_easy_setopt(handle, CURLOPT_FOLLOWLOCATION, 0L);
    curl_easy_setopt(handle, CURLOPT_NOSIGNAL, 1L);
    curl_easy_setopt(handle, CURLOPT_TIMEOUT_MS, static_cast<long>(request.timeout.count()));
    curl_easy_setopt(handle, CURLOPT_CONNECTTIMEOUT_MS, 10000L);
    if (url.loopback)
        curl_easy_setopt(handle, CURLOPT_NOPROXY, "*");
    curl_easy_setopt(handle, CURLOPT_CUSTOMREQUEST, request.method.c_str());
    if (request.method == "HEAD")
        curl_easy_setopt(handle, CURLOPT_NOBODY, 1L);
    if (!request.body.empty() || request.method == "POST") {
        curl_easy_setopt(handle, CURLOPT_POSTFIELDS, request.body.data());
        curl_easy_setopt(handle, CURLOPT_POSTFIELDSIZE_LARGE,
                         static_cast<curl_off_t>(request.body.size()));
    }
    for (const auto& [key, value] : request.headers)
        cleanup.headers = curl_slist_append(cleanup.headers, (key + ": " + value).c_str());
    curl_easy_setopt(handle, CURLOPT_HTTPHEADER, cleanup.headers);
    curl_easy_setopt(
        handle, CURLOPT_WRITEFUNCTION,
        +[](char* data, std::size_t size, std::size_t count, void* raw) -> std::size_t {
            auto& s = *static_cast<State*>(raw);
            try {
                if (!s.response->status) {
                    long status = 0;
                    if (curl_easy_getinfo(s.handle, CURLINFO_RESPONSE_CODE, &status) != CURLE_OK ||
                        !status)
                        throw Error("NetworkError", "Cannot read HTTP response status");
                    s.response->status = static_cast<int>(status);
                }
                if (!(*s.receive)(data, size * count)) {
                    s.error_body_discarded = true;
                    return 0;
                }
                return size * count;
            } catch (...) {
                s.error = std::current_exception();
                return 0;
            }
        });
    curl_easy_setopt(handle, CURLOPT_WRITEDATA, &state);
    curl_easy_setopt(
        handle, CURLOPT_HEADERFUNCTION,
        +[](char* data, std::size_t size, std::size_t count, void* raw) -> std::size_t {
            auto& s = *static_cast<State*>(raw);
            auto n = size * count;
            s.activity.store(Clock::now().time_since_epoch().count());
            s.headers += n;
            if (s.headers > 65536)
                return 0;
            std::string line(data, n);
            auto p = line.find(':');
            if (p != std::string::npos) {
                auto value = line.substr(p + 1);
                while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front())))
                    value.erase(value.begin());
                while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back())))
                    value.pop_back();
                s.response->headers[lower(line.substr(0, p))] = value;
            }
            return n;
        });
    curl_easy_setopt(handle, CURLOPT_HEADERDATA, &state);
    curl_easy_setopt(handle, CURLOPT_NOPROGRESS, 0L);
    curl_easy_setopt(
        handle, CURLOPT_XFERINFOFUNCTION,
        +[](void* raw, curl_off_t, curl_off_t, curl_off_t, curl_off_t uploaded) -> int {
            auto& s = *static_cast<State*>(raw);
            if (uploaded != s.uploaded) {
                s.uploaded = uploaded;
                s.activity.store(Clock::now().time_since_epoch().count());
            }
            return s.cancel.cancelled() || Clock::now() >= s.cancel.deadline ? 1 : 0;
        });
    curl_easy_setopt(handle, CURLOPT_XFERINFODATA, &state);
    // curl_easy_perform may leave the progress callback idle for an entire second.
    // Poll to the caller's millisecond deadline even when no bytes arrive.
    struct Transfer {
        CURLM* multi;
        CURL* easy;
        ~Transfer() {
            if (multi) {
                curl_multi_remove_handle(multi, easy);
                curl_multi_cleanup(multi);
            }
        }
    } transfer{curl_multi_init(), handle};
    if (!transfer.multi || curl_multi_add_handle(transfer.multi, handle) != CURLM_OK)
        throw Error("NetworkError", "Cannot initialize HTTP transfer");
    int running = 0;
    do {
        cancel.check();
        auto now = Clock::now();
        auto deadline = cancel.deadline;
        if (request.idle_timeout.count() > 0) {
            auto last = Clock::time_point(Clock::duration(last_activity.load()));
            deadline = std::min(deadline, last + request.idle_timeout);
            if (now >= deadline)
                throw Error("Timeout", "HTTP connection stayed silent beyond its idle timeout");
        }
        if (curl_multi_perform(transfer.multi, &running) != CURLM_OK)
            throw Error("NetworkError", "HTTP transfer failed");
        if (running) {
            auto wait = std::chrono::milliseconds(40);
            if (deadline != Clock::time_point::max())
                wait = std::clamp(
                    std::chrono::ceil<std::chrono::milliseconds>(deadline - Clock::now()),
                    std::chrono::milliseconds(0), wait);
#if LIBCURL_VERSION_NUM >= 0x074200
            auto status = curl_multi_poll(transfer.multi, nullptr, 0,
                                          static_cast<int>(wait.count()), nullptr);
#else
            int ready = 0;
            auto status =
                curl_multi_wait(transfer.multi, nullptr, 0, static_cast<int>(wait.count()), &ready);
            if (!ready && status == CURLM_OK)
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
#endif
            if (status != CURLM_OK)
                throw Error("NetworkError", "Cannot poll HTTP transfer");
        }
    } while (running);
    if (state.error)
        std::rethrow_exception(state.error);
    cancel.check();
    int queued = 0;
    const auto* message = curl_multi_info_read(transfer.multi, &queued);
    if (!message || message->msg != CURLMSG_DONE)
        throw Error("NetworkError", "Missing HTTP transfer result");
    const auto code = message->data.result;
    if (code == CURLE_OPERATION_TIMEDOUT)
        throw Error("Timeout", "HTTP request timed out");
    if (code != CURLE_OK && !(state.error_body_discarded && code == CURLE_WRITE_ERROR))
        throw Error("NetworkError",
                    "HTTP request failed: " + std::string(curl_easy_strerror(code)));
    long status = 0;
    curl_easy_getinfo(handle, CURLINFO_RESPONSE_CODE, &status);
    result.status = static_cast<int>(status);
    last_activity.store(Clock::now().time_since_epoch().count());
#endif
    cancel.check();
    return result;
}
} // namespace cymcp
