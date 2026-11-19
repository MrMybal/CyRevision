#pragma once
#include "core.hpp"
namespace cymcp {
struct PerformanceOptions {
    std::size_t records = 1024, labels = 256, pending = 1024;
    std::filesystem::path jsonl_file; // optional private asynchronous JSONL; contains metadata only
};
struct PerformanceRecord {
    std::string stage, name, status;
    double elapsed_ms = 0;
    std::size_t input_bytes = 0, output_bytes = 0;
    bool cache_hit = false;
};
class PerformanceMonitor {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit PerformanceMonitor(PerformanceOptions = {});
    ~PerformanceMonitor();
    void observe(PerformanceRecord) noexcept;
    Json drain();
    Json snapshot() const;
};
// Counts serialized bytes without building a serialized copy. Not a token estimate.
std::size_t serialized_size(const Json&);
class PerformanceSpan {
    std::shared_ptr<PerformanceMonitor> monitor_;
    PerformanceRecord record_;
    Clock::time_point started_;

  public:
    PerformanceSpan(std::shared_ptr<PerformanceMonitor>, std::string_view stage,
                    std::string_view name, const Json& input);
    ~PerformanceSpan();
    void finish(std::string status, const Json* output = nullptr, bool cache_hit = false);
    void finish_bytes(std::string status, std::size_t output_bytes);
};
} // namespace cymcp
