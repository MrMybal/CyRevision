#include <algorithm>
#include <array>
#include <cymcp/performance.hpp>
#include <sstream>
namespace cymcp {
namespace {
class CountBuffer : public std::streambuf {
  public:
    std::size_t count = 0;
    std::streamsize xsputn(const char*, std::streamsize n) override {
        count += static_cast<std::size_t>(n);
        return n;
    }
    int_type overflow(int_type c) override {
        if (!traits_type::eq_int_type(c, traits_type::eof()))
            ++count;
        return traits_type::not_eof(c);
    }
};
std::string label(std::string s) {
    if (s.empty() || s.size() > 128 ||
        s.find_first_not_of(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-/:") !=
            std::string::npos)
        return "other";
    return s;
}
Json json(const PerformanceRecord& r) {
    return {{"stage", r.stage},
            {"name", r.name},
            {"status", r.status},
            {"elapsed_ms", r.elapsed_ms},
            {"input_bytes", r.input_bytes},
            {"output_bytes", r.output_bytes},
            {"cache_hit", r.cache_hit}};
}
constexpr std::array<double, 12> limits{1, 5, 10, 25, 50, 100, 250, 500, 1000, 5000, 30000, 300000};
} // namespace
std::size_t serialized_size(const Json& value) {
    CountBuffer buffer;
    std::ostream out(&buffer);
    out << value;
    return buffer.count;
}
struct PerformanceMonitor::Impl {
    PerformanceOptions options;
    mutable std::mutex mutex;
    std::condition_variable wake;
    std::deque<Json> records, pending;
    std::uint64_t dropped = 0, file_errors = 0;
    struct Totals {
        std::uint64_t calls = 0, errors = 0, hits = 0, input = 0, output = 0;
        double total = 0, maximum = 0;
        std::array<std::uint64_t, 13> histogram{};
    };
    std::map<std::pair<std::string, std::string>, Totals> totals;
    PrivateOutputFile file{nullptr, &std::fclose};
    std::jthread writer;
    explicit Impl(PerformanceOptions o) : options(std::move(o)) {
        if (!options.records || options.records > 100000 || !options.labels ||
            options.labels > 10000 || !options.pending || options.pending > 100000)
            throw Error("InvalidOptions", "Invalid performance monitor limits");
        if (!options.jsonl_file.empty()) {
            file = open_private_output_file(options.jsonl_file);
            writer = std::jthread([this](std::stop_token stop) {
                std::unique_lock lock(mutex);
                for (;;) {
                    wake.wait(lock, [&] { return stop.stop_requested() || !pending.empty(); });
                    if (pending.empty() && stop.stop_requested())
                        break;
                    auto value = std::move(pending.front());
                    pending.pop_front();
                    lock.unlock();
                    auto line = value.dump() + "\n";
                    const bool failed =
                        std::fwrite(line.data(), 1, line.size(), file.get()) != line.size() ||
                        std::fflush(file.get()) != 0;
                    lock.lock();
                    if (failed)
                        ++file_errors;
                }
            });
        }
    }
    ~Impl() {
        if (writer.joinable()) {
            writer.request_stop();
            wake.notify_all();
            writer.join();
        }
    }
};
PerformanceMonitor::PerformanceMonitor(PerformanceOptions o)
    : impl_(std::make_unique<Impl>(std::move(o))) {}
PerformanceMonitor::~PerformanceMonitor() = default;
void PerformanceMonitor::observe(PerformanceRecord r) noexcept {
    try {
        r.stage = label(std::move(r.stage));
        r.name = label(std::move(r.name));
        r.status = label(std::move(r.status));
        auto value = json(r);
        value["timestamp_ms"] = std::chrono::duration_cast<std::chrono::milliseconds>(
                                    std::chrono::system_clock::now().time_since_epoch())
                                    .count();
        std::lock_guard lock(impl_->mutex);
        auto key = std::pair{r.stage, r.name};
        if (!impl_->totals.contains(key) && impl_->totals.size() >= impl_->options.labels - 1)
            key = {"other", "other"};
        auto& t = impl_->totals[key];
        ++t.calls;
        t.errors += r.status != "ok";
        t.hits += r.cache_hit;
        t.total += r.elapsed_ms;
        t.maximum = std::max(t.maximum, r.elapsed_ms);
        t.input += r.input_bytes;
        t.output += r.output_bytes;
        const auto bucket =
            std::lower_bound(limits.begin(), limits.end(), r.elapsed_ms) - limits.begin();
        ++t.histogram[static_cast<std::size_t>(bucket)];
        impl_->records.push_back(value);
        if (impl_->records.size() > impl_->options.records)
            impl_->records.pop_front();
        if (impl_->file) {
            if (impl_->pending.size() >= impl_->options.pending)
                ++impl_->dropped;
            else {
                impl_->pending.push_back(std::move(value));
                impl_->wake.notify_one();
            }
        }
    } catch (...) {
    } // instrumentation must never change operation behavior
}
Json PerformanceMonitor::drain() {
    std::lock_guard lock(impl_->mutex);
    Json result = Json::array();
    for (auto& r : impl_->records)
        result.push_back(std::move(r));
    impl_->records.clear();
    return result;
}
Json PerformanceMonitor::snapshot() const {
    std::lock_guard lock(impl_->mutex);
    Json rows = Json::array();
    for (const auto& [key, t] : impl_->totals) {
        auto percentile = [&](std::uint64_t percent) -> Json {
            std::uint64_t sum = 0;
            for (std::size_t i = 0; i < t.histogram.size(); ++i) {
                sum += t.histogram[i];
                if (sum >= (t.calls * percent + 99) / 100)
                    return i < limits.size() ? Json(limits[i]) : Json();
            }
            return nullptr;
        };
        rows.push_back({{"stage", key.first},
                        {"name", key.second},
                        {"calls", t.calls},
                        {"errors", t.errors},
                        {"cache_hits", t.hits},
                        {"input_bytes", t.input},
                        {"output_bytes", t.output},
                        {"mean_ms", t.total / static_cast<double>(t.calls)},
                        {"max_ms", t.maximum},
                        {"p50_upper_ms", percentile(50)},
                        {"p95_upper_ms", percentile(95)}});
    }
    return {{"operations", rows},
            {"dropped_file_records", impl_->dropped},
            {"file_errors", impl_->file_errors}};
}
PerformanceSpan::PerformanceSpan(std::shared_ptr<PerformanceMonitor> m, std::string_view stage,
                                 std::string_view name, const Json& input)
    : monitor_(std::move(m)) {
    if (monitor_) {
        started_ = Clock::now();
        record_.stage = stage;
        record_.name = name;
        record_.status = "error";
        try {
            record_.input_bytes = serialized_size(input);
        } catch (...) {
        }
    }
}
PerformanceSpan::~PerformanceSpan() {
    if (monitor_) {
        record_.elapsed_ms =
            std::chrono::duration<double, std::milli>(Clock::now() - started_).count();
        monitor_->observe(std::move(record_));
    }
}
void PerformanceSpan::finish(std::string status, const Json* output, bool hit) {
    if (monitor_) {
        record_.status = std::move(status);
        record_.cache_hit = hit;
        if (output)
            try {
                record_.output_bytes = serialized_size(*output);
            } catch (...) {
            }
    }
}
void PerformanceSpan::finish_bytes(std::string status, std::size_t bytes) {
    if (monitor_) {
        record_.status = std::move(status);
        record_.output_bytes = bytes;
    }
}
} // namespace cymcp
