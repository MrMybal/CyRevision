#pragma once
#include "core.hpp"
namespace cymcp {
struct Change {
    std::uint64_t sequence = 0;
    std::string topic, uri; // tools, resources, resource; no contents or caller data
};
struct ChangeBatch {
    std::vector<Change> changes;
    std::uint64_t sequence = 0;
    bool missed = false;
};
// Optional bounded journal. A slow subscriber receives missed=true and must invalidate broadly.
class ChangeFeed {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit ChangeFeed(std::size_t capacity = 256);
    ~ChangeFeed();
    void publish(std::string topic, std::string uri = {});
    std::uint64_t sequence() const;
    ChangeBatch read(std::uint64_t after, const Cancellation& = {},
                     std::chrono::milliseconds wait = std::chrono::milliseconds(0)) const;
};
} // namespace cymcp
