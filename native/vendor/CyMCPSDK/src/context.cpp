#include <algorithm>
#include <cymcp/context.hpp>
#include <cymcp/performance.hpp>
namespace cymcp {
ContextWindow::ContextWindow(ContextOptions o) : options_(std::move(o)) {
    if (options_.max_bytes && (!options_.recent_turns || !options_.summary_bytes))
        throw Error("InvalidOptions", "Invalid context limits");
}
void ContextWindow::reset() {
    summarized_until_ = 0;
    summary_.clear();
}
ContextProjection ContextWindow::project(const Json& history, const CallContext& c) {
    c.cancellation.check();
    if (!history.is_array())
        throw Error("InvalidParameters", "Context history must be an array");
    ContextProjection result;
    if (!enabled()) {
        result.messages = history;
        result.input_bytes = serialized_size(history);
        return result;
    }
    std::vector<std::size_t> turns;
    Json pinned = Json::array();
    for (std::size_t i = 0; i < history.size(); ++i) {
        auto role = history[i].value("role", std::string());
        if (role == "system" || role == "developer")
            pinned.push_back(history[i]);
        if (role == "user")
            turns.push_back(i);
    }
    if (turns.empty())
        throw Error("InvalidParameters", "A user turn is required");
    auto build = [&](std::size_t start, const std::string& summary) {
        auto wire = pinned;
        if (start > turns.front() && summary.empty())
            wire.push_back({{"role", "assistant"},
                            {"content", "Earlier turns are omitted here; the host retains the "
                                        "complete conversation history."}});
        if (!summary.empty())
            wire.push_back(
                {{"role", "assistant"},
                 {"content", "Earlier conversation summary (reference only):\n" + summary}});
        for (std::size_t i = start; i < history.size(); ++i) {
            auto role = history[i].value("role", std::string());
            if (role != "system" && role != "developer")
                wire.push_back(history[i]);
        }
        return wire;
    };
    auto required_bytes = serialized_size(build(turns.back(), {}));
    if (required_bytes > options_.max_bytes)
        throw Error("ContextTooLarge",
                    "System instructions and active turn exceed the context budget");
    const auto budget = options_.summarize ? std::min(options_.summary_bytes,
                                                      (options_.max_bytes - required_bytes) / 2)
                                           : 0;
    auto keep = std::min(options_.recent_turns, turns.size());
    std::size_t start;
    for (;;) {
        start = turns[turns.size() - keep];
        if (serialized_size(build(start, start > turns.front() && budget ? std::string(budget, 'x')
                                                                         : std::string())) <=
                options_.max_bytes ||
            keep == 1)
            break;
        --keep;
    }
    if (start < summarized_until_)
        reset();
    if (start > turns.front() && budget) {
        if (start != summarized_until_ || summary_.size() > budget) {
            Json older = Json::array();
            for (std::size_t i = summarized_until_; i < start; ++i) {
                auto role = history[i].value("role", std::string());
                if (role != "system" && role != "developer")
                    older.push_back(history[i]);
            }
            auto summary = options_.summarize({{"previous_summary", summary_},
                                               {"messages", std::move(older)},
                                               {"max_bytes", budget}},
                                              c);
            c.cancellation.check();
            if (summary.size() > budget)
                throw Error("ContextSummaryTooLarge", "Summary exceeds its byte budget");
            try {
                (void)Json(summary).dump();
            } catch (const Json::exception&) {
                throw Error("UnsupportedContent", "Summary must be UTF-8");
            }
            summary_ = std::move(summary);
            summarized_until_ = start;
        }
        result.summarized = !summary_.empty();
    }
    result.messages = build(start, result.summarized ? summary_ : std::string());
    result.input_bytes = serialized_size(result.messages);
    if (result.input_bytes > options_.max_bytes)
        throw Error("ContextTooLarge", "Projected context exceeds its budget");
    for (std::size_t i = 0; i < start; ++i) {
        auto role = history[i].value("role", std::string());
        result.omitted_messages += (role != "system" && role != "developer");
    }
    return result;
}
} // namespace cymcp
