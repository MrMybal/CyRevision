#pragma once
#include "core.hpp"
namespace cymcp {
struct ContextOptions {
    std::size_t max_bytes = 0; // 0 disables; serialized message bytes, not tokens
    std::size_t recent_turns = 8, summary_bytes = 4096;
    // Optional host summarizer: {previous_summary,messages,max_bytes}. No implicit AI call.
    std::function<std::string(const Json&, const CallContext&)> summarize;
};
struct ContextProjection {
    Json messages = Json::array();
    std::size_t omitted_messages = 0, input_bytes = 0;
    bool summarized = false;
};
// Pins system instructions and the active user turn, preserving complete tool exchanges.
class ContextWindow {
    ContextOptions options_;
    std::size_t summarized_until_ = 0;
    std::string summary_;

  public:
    explicit ContextWindow(ContextOptions = {});
    bool enabled() const {
        return options_.max_bytes != 0;
    }
    std::size_t max_bytes() const {
        return options_.max_bytes;
    }
    void reset(); // Call after editing/replacing earlier history.
    ContextProjection project(const Json&, const CallContext& = {});
};
} // namespace cymcp
