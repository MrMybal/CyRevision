#include "provider_error.hpp"
#include <algorithm>
#include <cymcp/ai.hpp>
#include <cymcp/performance.hpp>
#include <cymcp/resources.hpp>
#include <limits>
#include <set>
#include <sstream>
namespace cymcp {
namespace {
class SseDecoder {
    std::string line_, data_;
    std::function<void(const std::string&)> event_;

  public:
    explicit SseDecoder(std::function<void(const std::string&)> cb) : event_(std::move(cb)) {}
    void feed(const char* bytes, std::size_t size) {
        for (std::size_t n = 0; n < size; ++n) {
            if (bytes[n] == '\n') {
                if (!line_.empty() && line_.back() == '\r')
                    line_.pop_back();
                if (line_.empty()) {
                    if (!data_.empty()) {
                        if (data_.back() == '\n')
                            data_.pop_back();
                        event_(data_);
                        data_.clear();
                    }
                } else if (line_.rfind("data:", 0) == 0) {
                    auto start = line_.size() > 5 && line_[5] == ' ' ? 6 : 5;
                    data_ += line_.substr(start) + "\n";
                }
                line_.clear();
            } else
                line_ += bytes[n];
            if (line_.size() > provider_message_limit || data_.size() > provider_message_limit)
                throw Error("ResponseTooLarge", "SSE event exceeds the memory guard");
        }
    }
    void finish() {
        if (!line_.empty() || !data_.empty())
            throw Error("UnexpectedEOF", "Truncated SSE event");
    }
};
// Result recorded for a call that never ran because the turn was cancelled or timed out.
std::string interrupted_output(const std::string& code) {
    return Json({{"error",
                  {{"code", code}, {"message", "Tool call interrupted before completion"}}}})
        .dump();
}
HttpRequest model_request(const HttpChatOptions& options, const std::string& path) {
    HttpRequest request;
    request.url = options.base_url + path;
    request.timeout = options.timeout;
    request.max_response_bytes = provider_message_limit;
    request.headers = {{"Accept", "application/json"}, {"Content-Type", "application/json"}};
    if (!options.api_key.empty())
        request.headers["Authorization"] = "Bearer " + options.api_key;
    return request;
}
std::string latest_text(const Json& messages) {
    if (messages.empty() || !messages.back().contains("content") ||
        !messages.back()["content"].is_string())
        throw Error("Unsupported", "This connector currently accepts text input");
    return messages.back()["content"].get<std::string>();
}
} // namespace
HttpChatProvider::HttpChatProvider(HttpChatOptions opts)
    : Provider("openai-compatible"), options_(std::move(opts)) {
    options_.tools = compact_tools(std::move(options_.tools), options_.tool_discovery_threshold);
    while (!options_.base_url.empty() && options_.base_url.back() == '/')
        options_.base_url.pop_back();
    parse_url(options_.base_url);
    if (parse_url(options_.base_url).target.find('?') != std::string::npos ||
        options_.max_output_tokens < 0 || options_.turn_timeout.count() < 0 ||
        options_.timeout.count() <= 0 || options_.timeout > std::chrono::hours(24))
        throw Error("InvalidOptions", "Invalid chat options");
}
bool HttpChatProvider::local_service() const noexcept {
    try {
        return parse_url(options_.base_url).loopback;
    } catch (...) {
        return false;
    }
}
ProviderCapabilities HttpChatProvider::capabilities() const {
    return {true, false, static_cast<bool>(options_.tools), true, true, true, true, false, false};
}
void HttpChatProvider::validate_user_content(const Json& content) const {
    detail::content_for(content, detail::ContentFormat::ChatCompletions);
}
void HttpChatProvider::connect(const CallContext& context) {
    context.cancellation.check();
    parse_url(options_.base_url);
}
Json HttpChatProvider::list_models(const CallContext& context) {
    auto response = HttpClient().perform(model_request(options_, "/models"), context.cancellation);
    if (response.status != 200)
        throw Error(response.status == 401 || response.status == 403 ? "AuthenticationRequired"
                    : response.status == 429                         ? "RateLimited"
                                                                     : "ProviderError",
                    "Model catalogue request failed");
    auto result = parse_json(response.body, provider_message_limit);
    if (!result.contains("data") || !result["data"].is_array())
        throw Error("ProtocolError", "Missing model catalogue");
    return result["data"];
}
Session HttpChatProvider::start_session(std::string model, std::string resume,
                                        const CallContext& context, std::string) {
    context.cancellation.check();
    if (!resume.empty())
        throw Error(
            "Unsupported",
            "HTTP chat sessions are owned by the application; restore their messages explicitly");
    if (model.empty())
        throw Error("UnknownModel", "Select an exact model ID");
    return {random_token(16), std::move(model)};
}
ChatResult HttpChatProvider::send(Session& session, Json& messages, const CallContext& input) {
    auto context = input;
    if (options_.turn_timeout.count() > 0)
        context.cancellation.deadline =
            std::min(context.cancellation.deadline, Clock::now() + options_.turn_timeout);
    if (!messages.is_array() || messages.empty())
        throw Error("InvalidParameters", "Messages required");
    std::map<std::string, std::string> names;
    for (std::size_t round = 0; options_.max_tool_rounds == 0 || round < options_.max_tool_rounds;
         ++round) {
        if (input.context_bytes_limit && serialized_size(messages) > input.context_bytes_limit)
            throw Error("ContextTooLarge", "Active tool turn exceeds the context budget");
        context.cancellation.check();
        ChatResult result;
        Json calls = Json::array();
        bool terminal = false, done = false;
        auto request = model_request(options_, "/chat/completions");
        request.method = "POST";
        request.headers["Accept"] = "text/event-stream";
        // A generation stream has no total deadline or size budget: only silence aborts it.
        request.idle_timeout = options_.timeout;
        request.timeout = std::chrono::milliseconds(0);
        request.max_response_bytes = std::numeric_limits<std::size_t>::max();
        request.error_body_limit = detail::provider_error_body_limit;
        auto wire_messages = messages;
        for (auto& message : wire_messages) {
            message.erase("_cymcp_native");
            message["content"] =
                detail::content_for(message.at("content"), detail::ContentFormat::ChatCompletions,
                                    message.at("role").get<std::string>());
        }
        Json payload = {{"model", session.model},
                        {"messages", wire_messages},
                        {"stream", true},
                        {"stream_options", {{"include_usage", true}}}};
        if (options_.max_output_tokens > 0)
            payload["max_tokens"] = options_.max_output_tokens;
        if (options_.tools) {
            Json tools = Json::array();
            for (const auto& tool : options_.tools->describe()) {
                auto name = tool.at("name").get<std::string>();
                auto wire = name;
                if (wire.size() > 64 ||
                    wire.find_first_not_of(
                        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-") !=
                        std::string::npos)
                    wire = "cy_" + sha256(name).substr(0, 32);
                auto found = names.find(wire);
                if (found != names.end() && found->second != name)
                    throw Error("InvalidOptions", "Tool wire-name collision");
                names[wire] = name;
                tools.push_back({{"type", "function"},
                                 {"function",
                                  {{"name", wire},
                                   {"description", tool["description"]},
                                   {"parameters", tool["inputSchema"]}}}});
            }
            if (!tools.empty())
                payload["tools"] = tools;
        }
        request.body = payload.dump();
        SseDecoder decoder([&](const std::string& data) {
            if (done)
                throw Error("ProtocolError", "Data after terminal SSE marker");
            if (data == "[DONE]") {
                done = true;
                return;
            }
            auto event = parse_json(data, provider_message_limit);
            if (auto error = event.find("error"); error != event.end() && !error->is_null())
                throw detail::structured_provider_error(*error, "code", options_.api_key,
                                                        "ProviderError",
                                                        "Provider returned an error");
            if (event.contains("usage") && !event["usage"].is_null()) {
                result.usage = event["usage"];
                context.emit("UsageUpdated", result.usage);
            }
            if (!event.contains("choices") || !event["choices"].is_array())
                throw Error("ProtocolError", "Missing chat choices");
            for (const auto& choice : event["choices"]) {
                if (choice.value("index", 0) != 0)
                    continue;
                if (terminal)
                    throw Error("ProtocolError", "Choice after its finish reason");
                auto delta = choice.value("delta", Json::object());
                if (delta.contains("content") && delta["content"].is_string()) {
                    auto text = delta["content"].get<std::string>();
                    result.text += text;
                    context.emit("TextDelta", {{"text", text}});
                }
                if (delta.contains("reasoning_content") && delta["reasoning_content"].is_string())
                    context.emit("ThinkingDelta", {{"text", delta["reasoning_content"]}});
                if (delta.contains("tool_calls"))
                    for (const auto& part : delta["tool_calls"]) {
                        auto index = part.at("index").get<int>();
                        if (index < 0 || index >= 65536)
                            throw Error("ProtocolError", "Invalid tool call index");
                        while (calls.size() <= static_cast<std::size_t>(index))
                            calls.push_back({{"id", ""},
                                             {"type", "function"},
                                             {"function", {{"name", ""}, {"arguments", ""}}}});
                        auto& call = calls[index];
                        if (part.contains("id"))
                            call["id"] = part["id"];
                        if (part.contains("function")) {
                            auto fn = part["function"];
                            for (auto key : {"name", "arguments"})
                                if (fn.contains(key))
                                    call["function"][key] =
                                        call["function"][key].get<std::string>() +
                                        fn[key].get<std::string>();
                        }
                    }
                if (choice.contains("finish_reason") && !choice["finish_reason"].is_null()) {
                    result.finish_reason = choice["finish_reason"].get<std::string>();
                    terminal = true;
                }
            }
        });
        auto response = HttpClient().perform(request, context.cancellation,
                                             [&](const char* data, std::size_t size) {
                                                 decoder.feed(data, size);
                                                 return true;
                                             });
        if (response.status < 200 || response.status >= 300) {
            const auto body = detail::parse_provider_error_body(response.body);
            const auto category = detail::provider_http_error_code(response.status);
            const auto fallback =
                "Chat request rejected with HTTP " + std::to_string(response.status);
            if (auto error = body.find("error"); error != body.end())
                throw detail::structured_provider_error(*error, "code", options_.api_key, category,
                                                        fallback);
            throw Error(category, fallback);
        }
        decoder.finish();
        if (!terminal || !done)
            throw Error("UnexpectedEOF", "Provider stream ended before completion");
        Json assistant = {{"role", "assistant"}, {"content", result.text}};
        // A length/content-filter stop can cut a call: never replay it without a result.
        if (result.finish_reason != "tool_calls")
            calls = Json::array();
        if (calls.empty()) {
            if (result.finish_reason == "tool_calls")
                throw Error("ProtocolError", "Missing tool calls");
            messages.push_back(std::move(assistant));
            context.emit("TurnCompleted",
                         {{"text", result.text}, {"finishReason", result.finish_reason}});
            return result;
        }
        if (!options_.tools || result.finish_reason != "tool_calls")
            throw Error("ProtocolError", "Unexpected tool call result");
        std::set<std::string> ids;
        for (const auto& call : calls) {
            auto id = call.at("id").get<std::string>();
            if (id.empty() || !ids.insert(id).second)
                throw Error("ProtocolError", "Missing or duplicate tool call ID");
        }
        assistant["tool_calls"] = calls;
        messages.push_back(assistant);
        std::size_t answered = 0;
        try {
            for (const auto& call : calls) {
                auto name = call["function"]["name"].get<std::string>();
                auto id = call["id"].get<std::string>();
                if (id.empty())
                    throw Error("ProtocolError", "Missing tool call ID");
                context.emit("ToolStarted", {{"id", id}, {"name", name}});
                Json output;
                try {
                    auto args = parse_json(call["function"]["arguments"].get<std::string>());
                    auto found = names.find(name);
                    if (found == names.end())
                        throw Error("UnknownOperation", "Unknown API tool");
                    output = options_.tools->invoke(found->second, args, context);
                } catch (const Error& e) {
                    if (e.code == "Cancelled" || e.code == "Timeout")
                        throw;
                    output = {{"error", {{"code", e.code}, {"message", e.what()}}}};
                } catch (const Json::exception&) {
                    output = {{"error", {{"code", "InvalidArguments"}}}};
                }
                messages.push_back(
                    {{"role", "tool"}, {"tool_call_id", id}, {"content", output.dump()}});
                ++answered;
                context.emit("ToolCompleted", {{"id", id}, {"name", name}, {"output", output}});
            }
        } catch (const Error& error) {
            // Every recorded call needs a result, or the next request is rejected (HTTP 400).
            for (std::size_t n = answered; n < calls.size(); ++n)
                messages.push_back({{"role", "tool"},
                                    {"tool_call_id", calls[n].at("id")},
                                    {"content", interrupted_output(error.code)}});
            throw;
        }
    }
    throw Error("ToolBudgetExceeded", "Maximum tool rounds reached");
}
struct Conversation::State {
    std::shared_ptr<Provider> provider;
    std::string model;
    Session session;
    bool connected = false;
    std::string phase = "disconnected", last_error, last_error_message, last_provider_code;
    Json failed_turns = Json::array(); // display records anchored in committed model history
    Json metrics = Json::object();
    std::atomic_bool reserved{false};
    std::atomic_bool disconnect_requested{false};
    Json messages = Json::array();
    Json native_system_messages = Json::array();
    std::unique_ptr<ContextWindow> context_window;
    std::size_t native_history_bytes = 2; // compact JSON size including the array brackets
    std::mutex turn, history, cancellation_mutex;
    Cancellation active;
    State(std::shared_ptr<Provider> p, std::string m)
        : provider(std::move(p)), model(std::move(m)) {}
};
void Conversation::configure_context(ContextOptions options) {
    auto context = reserve({});
    struct Release {
        State& s;
        ~Release() {
            s.reserved.store(false);
        }
    } release{*state_};
    std::lock_guard turn(state_->turn);
    if (options.max_bytes &&
        (!state_->provider->uses_host_history() || state_->provider->uses_native_turn_context()))
        throw Error("Unsupported", "Native agents manage their own context");
    state_->context_window = std::make_unique<ContextWindow>(std::move(options));
}
Conversation::Conversation(std::shared_ptr<Provider> provider, std::string model,
                           std::string system)
    : state_(std::make_shared<State>(std::move(provider), std::move(model))) {
    if (!state_->provider)
        throw Error("InvalidOptions", "Provider required");
    if (!system.empty()) {
        state_->messages.push_back({{"role", "system"}, {"content", std::move(system)}});
        if (state_->provider->uses_native_turn_context()) {
            state_->native_system_messages = state_->messages;
            state_->native_history_bytes =
                detail::json_size(state_->messages, std::numeric_limits<std::size_t>::max());
        }
    }
}
void Conversation::connect(std::string resume, const CallContext& input, bool automatic_model,
                           Json* models) {
    auto& s = *state_;
    auto context = reserve(input);
    struct Release {
        State& s;
        ~Release() {
            s.reserved.store(false);
        }
    } release{s};
    std::unique_lock turn(s.turn, std::try_to_lock);
    if (!turn.owns_lock())
        throw Error("Busy", "Conversation busy");
    context.cancellation.check();
    s.disconnect_requested.store(false);
    {
        std::lock_guard history(s.history);
        s.connected = false;
        s.phase = "connecting";
    }
    auto started = Clock::now();
    try {
        context.emit("ConnectionStage", {{"code", "ProviderConnect"}});
        s.provider->connect(context);
        Json catalogue = Json::array();
        // The catalogue is informative: failing to list it never fails a valid session.
        const auto optional_catalogue = [&] {
            try {
                context.emit("ConnectionStage", {{"code", "ModelCatalogue"}});
                return s.provider->list_models(context);
            } catch (const Error&) {
                if (context.cancellation.cancelled())
                    throw;
                return Json::array();
            }
        };
        if (automatic_model && s.provider->uses_host_history() && s.model.empty()) {
            if (!s.provider->local_service()) {
                // A paid catalogue's order is not a recommendation: return it for an explicit
                // choice instead of opening a session on an arbitrary commercial model.
                if (models)
                    *models = optional_catalogue();
                throw Error("UnknownModel", "Select a model from the provider catalogue");
            }
            context.emit("ConnectionStage", {{"code", "ModelCatalogue"}});
            catalogue = s.provider->list_models(context);
            s.model = default_model(catalogue);
            if (s.model.empty())
                throw Error("UnknownModel", "No models are available on this service");
        }
        std::string system;
        {
            std::lock_guard history(s.history);
            for (const auto& message : s.messages)
                if (message.value("role", std::string()) == "system" &&
                    message["content"].is_string())
                    system += message["content"].get<std::string>() + "\n";
        }
        context.emit("ConnectionStage", {{"code", "SessionStart"}});
        const bool automatic_native_model = automatic_model && s.model.empty() && resume.empty() &&
                                            s.provider->capabilities().turn_model_selection;
        auto session = s.provider->start_session(s.model, std::move(resume), context, system);
        if (models || automatic_native_model) {
            if (catalogue.empty() && s.provider->capabilities().model_catalogue)
                catalogue = optional_catalogue();
        }
        if (automatic_native_model && catalogue.is_array()) {
            // The CLI's profile may name a model from a different client build. Prefer the
            // active catalogue's advertised default when the profile model isn't listed.
            std::string recommended;
            bool listed = false;
            for (const auto& entry : catalogue) {
                if (!entry.is_object() || entry.value("hidden", Json(false)) == true)
                    continue;
                auto model = entry.find("model");
                if (model == entry.end() || !model->is_string())
                    model = entry.find("id");
                if (model == entry.end() || !model->is_string())
                    continue;
                const auto& id = model->get_ref<const std::string&>();
                listed |= id == session.model;
                if (recommended.empty() && entry.value("isDefault", Json(false)) == true)
                    recommended = id;
            }
            if (!listed && !recommended.empty())
                session.model = std::move(recommended);
        }
        if (models)
            *models = std::move(catalogue);
        std::lock_guard lock(s.history);
        context.cancellation.check();
        if (s.disconnect_requested.load())
            throw Error("Cancelled", "Conversation was disconnected during connection");
        s.session = std::move(session);
        s.connected = true;
        s.phase = "ready";
        s.last_error.clear();
        s.last_error_message.clear();
        s.last_provider_code.clear();
        s.metrics["connect_ms"] =
            std::chrono::duration<double, std::milli>(Clock::now() - started).count();
    } catch (...) {
        s.provider->disconnect();
        std::lock_guard history(s.history);
        s.phase = "disconnected";
        throw;
    }
}
CallContext Conversation::reserve(const CallContext& original) {
    if (state_->reserved.exchange(true))
        throw Error("Busy", "Conversation already has a scheduled or active turn");
    CallContext context = original;
    context.cancellation = original.cancellation.child();
    {
        std::lock_guard lock(state_->cancellation_mutex);
        state_->active = context.cancellation;
    }
    return context;
}
ChatResult Conversation::send(Json content, const CallContext& original) {
    const auto submitted = Clock::now();
    return send_reserved(std::move(content), reserve(original), submitted);
}
ChatResult Conversation::send_reserved(Json content, const CallContext& context,
                                       Clock::time_point started) {
    auto& s = *state_;
    struct Release {
        State& state;
        ~Release() {
            state.reserved.store(false);
        }
    } release{s};
    std::unique_lock turn(s.turn, std::try_to_lock);
    if (!turn.owns_lock())
        throw Error("Busy", "Conversation busy");
    context.cancellation.check();
    const bool native_turn = s.provider->uses_native_turn_context();
    std::size_t prefix_bytes = 0;
    Json messages = Json::array();
    Session session;
    {
        std::lock_guard lock(s.history);
        if (!s.connected)
            throw Error("InvalidState", "Connect conversation first");
        if (native_turn) {
            prefix_bytes = s.native_history_bytes + (s.messages.empty() ? 0 : 1);
            messages = s.native_system_messages;
        } else {
            messages = s.messages;
        }
        session = s.session;
    }
    validate_content(content);
    std::size_t user_index = messages.size();
    Json user = {{"role", "user"}};
    user["content"] = std::move(content);
    if (prefix_bytes > provider_message_limit)
        throw Error("ContextTooLarge", "Conversation exceeds the memory guard");
    const auto user_bytes =
        native_turn ? detail::json_size(user, provider_message_limit - prefix_bytes) : 0;
    messages.push_back(std::move(user));
    const bool projected = !native_turn && s.context_window && s.context_window->enabled();
    Json context_metrics = Json::object();
    if (projected) {
        auto projection = s.context_window->project(messages, context);
        messages = std::move(projection.messages);
        user_index = messages.size() - 1;
        context_metrics = {{"omitted_messages", projection.omitted_messages},
                           {"input_bytes", projection.input_bytes},
                           {"summarized", projection.summarized}};
        context.emit("ContextReduced", context_metrics);
    }
    if (!native_turn)
        detail::json_size(messages, provider_message_limit);
    bool committed = false;
    auto commit = [&] {
        if (committed)
            return;
        std::lock_guard lock(s.history);
        if (native_turn) {
            auto bytes = s.native_history_bytes;
            for (std::size_t i = user_index; i < messages.size(); ++i) {
                bytes += (i != user_index || !s.messages.empty()) ? 1 : 0;
                bytes +=
                    i == user_index
                        ? user_bytes
                        : detail::json_size(messages[i], std::numeric_limits<std::size_t>::max());
            }
            auto& committed_messages = s.messages.get_ref<Json::array_t&>();
            const auto needed = committed_messages.size() + (messages.size() - user_index);
            if (needed > committed_messages.capacity())
                committed_messages.reserve(std::max(needed, committed_messages.capacity() * 2));
            for (std::size_t i = user_index; i < messages.size(); ++i)
                s.messages.push_back(std::move(messages[i]));
            s.native_history_bytes = bytes;
        } else if (projected) {
            for (std::size_t i = user_index; i < messages.size(); ++i)
                s.messages.push_back(std::move(messages[i]));
        } else {
            s.messages = std::move(messages);
        }
        s.session = std::move(session);
        committed = true;
    };
    double first_token = -1;
    std::size_t event_count = 0;
    std::string partial;
    CallContext measured = context;
    PerformanceSpan performance(context.performance, "conversation", s.provider->identity(),
                                messages);
    if (projected)
        measured.context_bytes_limit = s.context_window->max_bytes();
    measured.events = [&](const Event& event) {
        ++event_count;
        if (event.type == "TextDelta" && event.data.contains("text") &&
            event.data.at("text").is_string()) {
            const auto& delta = event.data.at("text").get_ref<const std::string&>();
            if (first_token < 0 && !delta.empty())
                first_token =
                    std::chrono::duration<double, std::milli>(Clock::now() - started).count();
            partial += delta;
        }
        context.emit(event.type, event.data);
    };
    auto metrics = [&] {
        return Json{
            {"first_token_ms", first_token < 0 ? Json() : Json(first_token)},
            {"total_ms", std::chrono::duration<double, std::milli>(Clock::now() - started).count()},
            {"events", event_count},
            {"context", context_metrics}};
    };
    {
        std::lock_guard lock(s.history);
        s.phase = "busy";
        s.last_error.clear();
        s.last_error_message.clear();
        s.last_provider_code.clear();
    }
    auto failed = [&](const std::string& code, std::string_view reason,
                      std::string_view native = {}) {
        performance.finish(code);
        const auto detail = s.provider->error_message(reason);
        const auto provider_code = s.provider->error_message(native);
        // A locally rejected attachment must not become a future provider input.
        if (code != "UnsupportedContent" || messages.size() != user_index + 1)
            commit();
        {
            std::lock_guard lock(s.history);
            s.metrics = metrics();
            s.last_error = code;
            s.last_error_message = detail;
            s.last_provider_code = provider_code;
            s.phase = s.disconnect_requested.load() ? "disconnected"
                      : code == "Cancelled"         ? "interrupted"
                                                    : "failed";
            if (committed)
                s.failed_turns.push_back({{"after", s.messages.size()},
                                          {"code", code},
                                          {"provider_code", provider_code},
                                          {"message", detail},
                                          {"partial", std::move(partial)}});
        }
        if (context.events)
            context.events(
                {code == "Cancelled" ? "TurnCancelled" : "TurnFailed",
                 {{"code", code}, {"message", detail}, {"provider_code", provider_code}}});
        return Error(code, detail, provider_code);
    };
    try {
        measured.emit("TurnStarted", {{"sessionId", session.id}});
        auto result = s.provider->send(session, messages, measured);
        performance.finish_bytes("ok", result.text.size());
        context.cancellation.check();
        commit();
        result.metrics = metrics();
        {
            std::lock_guard lock(s.history);
            s.metrics = result.metrics;
            s.phase = s.disconnect_requested.load() ? "disconnected" : "ready";
        }
        context.emit("TurnMetrics", result.metrics);
        return result;
    } catch (const Error& e) {
        throw failed(e.code, e.what(), e.provider_code);
    } catch (const Json::exception&) {
        // Parser diagnostics can contain fragments of the raw provider payload.
        throw failed("ProviderError", "Invalid provider response");
    } catch (const std::exception& e) {
        throw failed("ProviderError", e.what());
    } catch (...) {
        throw failed("ProviderError", "Provider generation failed");
    }
}
std::future<ChatResult> Conversation::send_async(Executor& executor, Json content,
                                                 CallContext context) {
    const auto submitted = Clock::now();
    context = reserve(context);
    try {
        auto self = *this;
        auto task = std::make_shared<std::packaged_task<ChatResult()>>(
            [self, content = std::move(content), context = std::move(context),
             submitted]() mutable {
                return self.send_reserved(std::move(content), context, submitted);
            });
        auto result = task->get_future();
        executor.post([task] { (*task)(); });
        return result;
    } catch (...) {
        state_->reserved.store(false);
        throw;
    }
}
void Conversation::cancel() noexcept {
    std::lock_guard lock(state_->cancellation_mutex);
    state_->active.cancel();
}
void Conversation::select_model(std::string model) {
    auto& s = *state_;
    std::unique_lock turn(s.turn, std::try_to_lock);
    if (!turn.owns_lock() || s.reserved.load())
        throw Error("Busy", "Conversation has an active operation");
    if (model.empty())
        throw Error("UnknownModel", "Select an exact model ID");
    std::lock_guard lock(s.history);
    if (!s.connected)
        throw Error("InvalidState", "Connect conversation first");
    if (!s.provider->capabilities().turn_model_selection)
        throw Error("Unsupported", "This provider requires a new session to select another model");
    s.session.model = std::move(model);
    s.phase = "ready";
    s.last_error.clear();
    s.last_error_message.clear();
    s.last_provider_code.clear();
}
Json Conversation::history() const {
    std::lock_guard lock(state_->history);
    return state_->messages;
}
std::size_t Conversation::user_turn_count() const {
    std::lock_guard lock(state_->history);
    return static_cast<std::size_t>(
        std::count_if(state_->messages.begin(), state_->messages.end(),
                      [](const Json& row) { return row.at("role") == "user"; }));
}
void Conversation::edit_message(std::size_t user_turn, std::string text) {
    change_user_turn(user_turn, std::move(text));
}
void Conversation::delete_turn(std::size_t user_turn) {
    change_user_turn(user_turn, std::nullopt);
}
void Conversation::change_user_turn(std::size_t user_turn, std::optional<std::string> text) {
    auto& s = *state_;
    std::unique_lock turn(s.turn, std::try_to_lock);
    if (!turn.owns_lock() || s.reserved.load())
        throw Error("Busy", "Conversation has an active operation");
    std::lock_guard lock(s.history);
    if (!s.connected)
        throw Error("InvalidState", "Connect conversation first");
    if (!s.provider->uses_host_history() || s.provider->uses_native_turn_context())
        throw Error("Unsupported", "The provider owns this conversation history");
    const auto is_user = [](const Json& row) { return row.at("role") == "user"; };
    auto first = std::find_if(s.messages.begin(), s.messages.end(), is_user);
    while (user_turn && first != s.messages.end()) {
        first = std::find_if(first + 1, s.messages.end(), is_user);
        --user_turn;
    }
    if (first == s.messages.end())
        throw Error("InvalidParameters", "Unknown user turn");
    if (text) {
        auto& original = first->at("content");
        Json replacement = *text;
        if (original.is_array()) {
            replacement = Json::array();
            bool inserted = false;
            for (const auto& block : original) {
                if (block.at("type") != "text")
                    replacement.push_back(block);
                else if (!inserted && !text->empty()) {
                    replacement.push_back(text_block(*text));
                    inserted = true;
                }
            }
            if (!inserted && !text->empty())
                replacement.insert(replacement.begin(), text_block(*text));
        }
        if (text->find_first_not_of(" \t\r\n") == std::string::npos &&
            (!replacement.is_array() ||
             std::none_of(replacement.begin(), replacement.end(),
                          [](const Json& block) { return block.at("type") != "text"; })))
            throw Error("InvalidContent", "A message needs text or an attachment");
        s.provider->validate_user_content(replacement);
        original.swap(replacement);
        try {
            detail::json_size(s.messages, provider_message_limit);
        } catch (...) {
            original.swap(replacement);
            throw;
        }
        if (s.context_window)
            s.context_window->reset();
        return;
    }
    const auto next = std::find_if(first + 1, s.messages.end(), is_user);
    const auto begin = static_cast<std::size_t>(first - s.messages.begin());
    const auto end = static_cast<std::size_t>(next - s.messages.begin());
    const bool latest = next == s.messages.end();
    const auto is_turn_message = [](const Json& row) { return row.at("role") != "system"; };
    const auto removed = static_cast<std::size_t>(std::count_if(first, next, is_turn_message));
    auto failures = Json::array();
    for (auto failure : s.failed_turns) {
        auto after = failure.at("after").get<std::size_t>();
        if (after > begin && after <= end)
            continue;
        if (after > end)
            failure["after"] = after - removed;
        failures.push_back(std::move(failure));
    }
    s.messages.erase(std::remove_if(first, next, is_turn_message), next);
    if (s.context_window)
        s.context_window->reset();
    s.failed_turns.swap(failures);
    if (latest) {
        s.metrics = Json::object();
        s.phase = "ready";
        s.last_error.clear();
        s.last_error_message.clear();
        s.last_provider_code.clear();
        if (!s.failed_turns.empty() && s.failed_turns.back().at("after") == s.messages.size()) {
            const auto& previous = s.failed_turns.back();
            s.last_error = previous.at("code").get<std::string>();
            s.last_error_message = previous.at("message").get<std::string>();
            s.last_provider_code = previous.value("provider_code", std::string());
            s.phase = s.last_error == "Cancelled" ? "interrupted" : "failed";
        }
    }
}
Json Conversation::transcript() const {
    std::lock_guard lock(state_->history);
    if (state_->failed_turns.empty())
        return state_->messages;
    Json rows = Json::array();
    auto failure = state_->failed_turns.begin();
    for (std::size_t i = 0; i < state_->messages.size(); ++i) {
        rows.push_back(state_->messages[i]);
        if (failure == state_->failed_turns.end() || failure->at("after") != i + 1)
            continue;
        // Cancellation can arrive immediately after a provider commits its final answer.
        const auto& partial = failure->at("partial").get_ref<const std::string&>();
        if (rows.back().at("role") != "assistant" || rows.back().contains("tool_calls")) {
            rows.push_back({{"role", "assistant"}, {"content", partial}});
        } else if (auto& content = rows.back().at("content");
                   content.is_string() &&
                   partial.starts_with(content.get_ref<const std::string&>())) {
            content = partial;
        }
        rows.back()["error_code"] = failure->at("code");
        rows.back()["error_message"] = failure->at("message");
        rows.back()["provider_code"] = failure->value("provider_code", std::string());
        ++failure;
    }
    return rows;
}
Session Conversation::session() const {
    std::lock_guard lock(state_->history);
    return state_->session;
}
void Conversation::disconnect() noexcept {
    state_->disconnect_requested.store(true);
    cancel();
    // Provider disconnect is cancellation-aware, including silent subprocesses.
    state_->provider->disconnect();
    std::lock_guard lock(state_->history);
    state_->connected = false;
    state_->phase = "disconnected";
}
Json Conversation::status() const {
    std::lock_guard lock(state_->history);
    return {{"state", state_->phase},
            {"last_error", state_->last_error},
            {"last_error_message", state_->last_error_message},
            {"last_provider_code", state_->last_provider_code},
            {"metrics", state_->metrics}};
}
Json Conversation::checkpoint() const {
    auto& s = *state_;
    std::lock_guard lock(s.history);
    if (s.reserved.load())
        throw Error("Busy", "Cannot snapshot a scheduled or active turn");
    return {{"format", "cymcp-conversation"},
            {"version", 1},
            {"provider", s.provider->identity()},
            {"history_mode", s.provider->uses_host_history() ? "host" : "native"},
            {"session", {{"id", s.session.id}, {"model", s.session.model}}},
            {"model", s.model},
            {"messages", s.messages},
            {"last_error", s.last_error},
            {"last_error_message", s.last_error_message},
            {"last_provider_code", s.last_provider_code},
            {"failed_turns", s.failed_turns},
            {"metrics", s.metrics}};
}
void Conversation::restore(const Json& snapshot, const CallContext& input) {
    auto context = reserve(input);
    auto& s = *state_;
    struct Release {
        State& s;
        ~Release() {
            s.reserved.store(false);
        }
    } release{s};
    std::unique_lock turn(s.turn, std::try_to_lock);
    if (!turn.owns_lock())
        throw Error("Busy", "Conversation busy");
    Json messages;
    std::string id, model, native_model, system, previous_error, previous_message,
        previous_provider_code;
    Json failed_turns;
    Json native_system_messages = Json::array();
    const bool native_turn = s.provider->uses_native_turn_context();
    std::size_t native_history_bytes = 2;
    try {
        detail::json_size(snapshot, provider_message_limit);
        if (snapshot.at("format") != "cymcp-conversation" || snapshot.at("version") != 1)
            throw Error("UnsupportedVersion", "Unsupported conversation checkpoint");
        if (snapshot.at("provider") != s.provider->identity() ||
            snapshot.at("history_mode") != (s.provider->uses_host_history() ? "host" : "native"))
            throw Error("SessionInvalid", "Checkpoint belongs to a different provider/transport");
        messages = snapshot.at("messages");
        if (snapshot.contains("metrics") && !snapshot.at("metrics").is_object())
            throw Error("InvalidCheckpoint", "Invalid checkpoint metrics");
        if (!messages.is_array() || messages.size() > 100000)
            throw Error("InvalidCheckpoint", "Invalid message history");
        for (const auto& message : messages) {
            auto role = message.at("role").get<std::string>();
            if (role != "system" && role != "user" && role != "assistant" && role != "tool")
                throw Error("InvalidCheckpoint", "Invalid message role");
            validate_content(message.at("content"));
            if (role == "system") {
                system += detail::content_text(message.at("content")) + "\n";
                if (native_turn)
                    native_system_messages.push_back(message);
            }
            if (role == "tool")
                message.at("tool_call_id").get<std::string>();
        }
        model = snapshot.at("model").get<std::string>();
        native_model = snapshot.at("session").at("model").get<std::string>();
        id = snapshot.at("session").at("id").get<std::string>();
        previous_error = snapshot.value("last_error", std::string());
        previous_message =
            s.provider->error_message(snapshot.value("last_error_message", std::string()));
        previous_provider_code =
            s.provider->error_message(snapshot.value("last_provider_code", std::string()));
        failed_turns = snapshot.value("failed_turns", Json::array());
        if (!failed_turns.is_array())
            throw Error("InvalidCheckpoint", "Invalid failed turns");
        std::size_t previous_index = 0;
        for (auto& failure : failed_turns) {
            const auto& after = failure.at("after");
            if (!after.is_number_integer() || after <= previous_index || after > messages.size() ||
                !failure.at("partial").is_string() || !failure.at("code").is_string())
                throw Error("InvalidCheckpoint", "Invalid failed turn");
            previous_index = after.get<std::size_t>();
            failure["message"] =
                s.provider->error_message(failure.at("message").get_ref<const std::string&>());
            failure["provider_code"] =
                s.provider->error_message(failure.value("provider_code", std::string()));
        }
        // Older versions saved locally rejected inputs and later unsuccessful retries. Only
        // repair the unanswered user suffix, preserving completed messages and tool results.
        if (previous_error == "UnsupportedContent" && s.provider->uses_host_history()) {
            auto first = messages.end();
            while (first != messages.begin() && (first - 1)->at("role") == "user")
                --first;
            for (auto it = first; it != messages.end(); ++it) {
                try {
                    s.provider->validate_user_content(it->at("content"));
                } catch (const Error& e) {
                    if (e.code != "UnsupportedContent")
                        throw;
                    messages.erase(it, messages.end());
                    break;
                }
            }
        }
        if (!s.model.empty() && s.model != model)
            throw Error("SessionInvalid", "Checkpoint model differs from requested model");
        if (!s.provider->uses_host_history() && id.empty())
            throw Error("SessionInvalid", "Native checkpoint has no exact session ID");
        if (native_turn)
            native_history_bytes = detail::json_size(messages, provider_message_limit);
    } catch (const Json::exception&) {
        throw Error("InvalidCheckpoint", "Invalid conversation checkpoint");
    }
    s.disconnect_requested.store(false);
    {
        std::lock_guard history(s.history);
        s.connected = false;
        s.phase = "connecting";
    }
    s.provider->disconnect();
    try {
        context.cancellation.check();
        s.provider->connect(context);
        if (!s.provider->uses_host_history() && !s.provider->capabilities().resume)
            throw Error("Unsupported", "Provider cannot reload this exact native session");
        auto session =
            s.provider->start_session(native_model.empty() ? model : native_model,
                                      s.provider->uses_host_history() ? "" : id, context, system);
        if (!s.provider->uses_host_history() && session.id != id)
            throw Error("SessionInvalid", "Provider replaced the native session");
        if (s.provider->uses_host_history())
            session.id = id.empty() ? session.id : id;
        {
            std::lock_guard history(s.history);
            context.cancellation.check();
            if (s.disconnect_requested.load())
                throw Error("Cancelled", "Conversation was disconnected during restore");
            s.model = model;
            s.session = std::move(session);
            s.messages = std::move(messages);
            if (s.context_window)
                s.context_window->reset();
            s.native_system_messages = std::move(native_system_messages);
            s.native_history_bytes = native_history_bytes;
            s.metrics = snapshot.value("metrics", Json::object());
            s.connected = true;
            s.phase = "ready";
            s.last_error = previous_error;
            s.last_error_message = std::move(previous_message);
            s.last_provider_code = std::move(previous_provider_code);
            s.failed_turns = std::move(failed_turns);
        }
        context.emit("ConversationRestored",
                     {{"sessionId", s.session.id}, {"previous_error", previous_error}});
    } catch (...) {
        s.provider->disconnect();
        std::lock_guard history(s.history);
        s.connected = false;
        s.phase = "needs_reconnect";
        throw;
    }
}
void Conversation::reconnect(const CallContext& context) {
    restore(checkpoint(), context);
}
} // namespace cymcp
