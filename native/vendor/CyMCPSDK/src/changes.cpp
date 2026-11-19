#include <cymcp/changes.hpp>
namespace cymcp {
struct ChangeFeed::Impl {
    std::size_t capacity;
    mutable std::mutex mutex;
    mutable std::condition_variable wake;
    std::deque<Change> records;
    std::uint64_t sequence = 0;
};
ChangeFeed::ChangeFeed(std::size_t capacity) : impl_(std::make_unique<Impl>()) {
    if (!capacity || capacity > 65536)
        throw Error("InvalidOptions", "Invalid change journal capacity");
    impl_->capacity = capacity;
}
ChangeFeed::~ChangeFeed() = default;
void ChangeFeed::publish(std::string topic, std::string uri) {
    if ((topic != "tools" && topic != "resources" && topic != "resource") || uri.size() > 4096)
        throw Error("InvalidParameters", "Invalid change topic or URI");
    std::lock_guard lock(impl_->mutex);
    impl_->records.push_back({++impl_->sequence, std::move(topic), std::move(uri)});
    if (impl_->records.size() > impl_->capacity)
        impl_->records.pop_front();
    impl_->wake.notify_all();
}
std::uint64_t ChangeFeed::sequence() const {
    std::lock_guard lock(impl_->mutex);
    return impl_->sequence;
}
ChangeBatch ChangeFeed::read(std::uint64_t after, const Cancellation& cancel,
                             std::chrono::milliseconds wait) const {
    cancel.check();
    const auto deadline = Clock::now() + wait;
    std::unique_lock lock(impl_->mutex);
    while (after >= impl_->sequence && Clock::now() < deadline) {
        impl_->wake.wait_until(lock,
                               std::min(deadline, Clock::now() + std::chrono::milliseconds(50)));
        cancel.check();
    }
    ChangeBatch batch;
    batch.sequence = impl_->sequence;
    batch.missed = !impl_->records.empty() && after < impl_->records.front().sequence - 1;
    for (const auto& entry : impl_->records)
        if (entry.sequence > after)
            batch.changes.push_back(entry);
    return batch;
}
} // namespace cymcp
