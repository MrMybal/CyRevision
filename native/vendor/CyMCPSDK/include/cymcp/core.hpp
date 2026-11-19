#pragma once
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <deque>
#include <filesystem>
#include <functional>
#include <future>
#include <map>
#include <memory>
#include <mutex>
#include <nlohmann/json.hpp>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace cymcp {
using Json = nlohmann::json;
using Clock = std::chrono::steady_clock;
Json parse_json(const std::string&, std::size_t max_bytes = 1024 * 1024, int max_depth = 64);
// Bound for one JSON message received from a trusted provider (child process, model API).
// A memory guard against a runaway stream, not a content budget: agent turns carry large
// tool outputs, diffs and file contents.
inline constexpr std::size_t provider_message_limit = 256 * 1024 * 1024;
std::string path_utf8(const std::filesystem::path&);
// Format an error explanation for display or diagnostics. Masks credential values and
// conventional authorization fields, removes control characters and bounds the UTF-8 text.
std::string redact_error_message(std::string_view message,
                                 const std::vector<std::string>& secrets = {});
inline constexpr const char* version = "0.7.0";
class Error : public std::runtime_error {
  public:
    std::string code;
    // Optional identifier from the provider, e.g. a JSON-RPC number or an error enum.
    std::string provider_code;
    Error(std::string code, std::string message);
    Error(std::string code, std::string message, std::string provider_code);
};
struct Cancellation {
    std::shared_ptr<std::atomic_bool> flag = std::make_shared<std::atomic_bool>(false);
    std::vector<std::shared_ptr<std::atomic_bool>> ancestors;
    Clock::time_point deadline = Clock::time_point::max();
    void cancel() const noexcept {
        flag->store(true);
    }
    bool cancelled() const noexcept {
        if (flag->load())
            return true;
        for (const auto& parent : ancestors)
            if (parent->load())
                return true;
        return false;
    }
    Cancellation child() const {
        auto result = *this;
        result.ancestors.push_back(flag);
        result.flag = std::make_shared<std::atomic_bool>(false);
        return result;
    }
    void check() const;
};
struct Event {
    std::string type;
    Json data = Json::object();
};
using EventSink = std::function<void(const Event&)>;
class PerformanceMonitor;
class ChangeFeed;
struct CallContext {
    std::string principal = "local";
    std::string request_id;
    Cancellation cancellation;
    EventSink events;
    // Transport-local namespace for correlating peer request IDs, separate from authorization.
    std::string transport_scope;
    std::shared_ptr<PerformanceMonitor> performance;
    // Optional transport channel for streaming JSON-RPC notifications; null is a heartbeat.
    std::function<void(const Json&)> notifications;
    std::size_t context_bytes_limit = 0; // optional host-history message budget
    void emit(std::string type, Json data = Json::object()) const;
};
enum class Decision { allow, deny };
using PolicyCallback = std::function<Decision(const CallContext&, const std::string&, const Json&)>;
class Policy {
    PolicyCallback callback_;

  public:
    Policy(); // deny until the host explicitly chooses a policy
    explicit Policy(PolicyCallback callback);
    static Policy allow_all();
    static Policy deny_all();
    bool allows(const CallContext&, const std::string&, const Json&) const;
};
// Versioned, fail-closed JSON Schema profile; unsupported validation keywords are rejected.
void check_schema(const Json& schema);
void validate(const Json& instance, const Json& schema);
struct Operation {
    std::string name, description;
    Json input_schema = {{"type", "object"}, {"additionalProperties", false}};
    Json output_schema = Json::object();
    bool read_only = false;
    std::function<Json(const Json&, const CallContext&)> handler;
};
class Registry {
    mutable std::mutex mutex_;
    std::map<std::string, std::shared_ptr<const Operation>> operations_;
    Policy policy_;
    mutable std::shared_ptr<const Json> catalogue_;
    mutable std::shared_ptr<const Json> summaries_;
    std::uint64_t revision_ = 0;
    std::shared_ptr<PerformanceMonitor> performance_;
    std::shared_ptr<ChangeFeed> changes_;
    void register_operation(Operation, bool replace);

  public:
    explicit Registry(Policy policy = {});
    void set_performance_monitor(std::shared_ptr<PerformanceMonitor>);
    void set_change_feed(std::shared_ptr<ChangeFeed>);
    void add(Operation operation);
    void replace(Operation operation);
    void remove(const std::string& name);
    Json describe() const;
    Json describe(const std::string& name) const;
    // Immutable catalogue snapshot, rebuilt only after a registry change.
    std::shared_ptr<const Json> catalogue() const;
    std::shared_ptr<const Json> summaries() const;
    std::size_t size() const;
    std::uint64_t revision() const;
    Json invoke(const std::string& name, const Json& arguments,
                const CallContext& context = {}) const;
};
Json dispatch_app(const Registry&, const Json&, const CallContext& = {});
class Executor {
    std::mutex mutex_;
    std::condition_variable wake_;
    std::deque<std::function<void()>> queue_;
    std::vector<std::thread> threads_;
    std::size_t capacity_;
    bool stopping_ = false;

  public:
    explicit Executor(std::size_t workers = 2, std::size_t capacity = 128);
    ~Executor();
    Executor(const Executor&) = delete;
    Executor& operator=(const Executor&) = delete;
    void post(std::function<void()> work);
};
// Bounded queue for delivery from worker threads to the host's UI/event loop.
class EventQueue {
    std::mutex mutex_;
    std::deque<Event> queue_;
    std::size_t capacity_, bytes_ = 0, byte_limit_;

  public:
    explicit EventQueue(std::size_t capacity = 1024, std::size_t bytes = 4 * 1024 * 1024);
    void push(const Event& event);
    std::vector<Event> drain(std::size_t max_events = 256);
};
std::string random_token(std::size_t bytes = 32);
bool constant_time_equal(const std::string&, const std::string&) noexcept;
std::string sha256_file(const std::filesystem::path&, const Cancellation& = {});
std::string sha256(const std::string&);
void private_directory(const std::filesystem::path&);
using PrivateOutputFile = std::unique_ptr<std::FILE, decltype(&std::fclose)>;
// Replace the destination with a new owner-only file and keep its writable stream open.
// The parent directory's permissions are preserved.
PrivateOutputFile open_private_output_file(const std::filesystem::path&);
std::string platform_id();
} // namespace cymcp
