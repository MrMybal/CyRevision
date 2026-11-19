#include "provider_error.hpp"
#include <cymcp/ai.hpp>
#include <cymcp/performance.hpp>
#include <cymcp/resources.hpp>
#include <limits>
#include <set>
namespace cymcp {
namespace {
constexpr std::size_t frame_limit = provider_message_limit;
// Used only when an Anthropic-compatible catalogue does not publish the model's output cap.
constexpr int fallback_output_tokens = 64000;
void http_status(int status) {
    if (status >= 200 && status < 300)
        return;
    throw Error(detail::provider_http_error_code(status),
                "API request rejected with HTTP " + std::to_string(status));
}
Error generation_error(const Json& event, const ApiOptions& options, std::string fallback,
                       std::string sdk_code = "ProviderError") {
    // Read only the documented error fields. Adjacent output, account and tool
    // metadata remain private, including when a proxy uses an unfamiliar error envelope.
    if (!event.is_object())
        return Error(std::move(sdk_code), std::move(fallback));
    const Json* envelope = &event;
    const auto type = event.find("type");
    const auto response = event.find("response");
    if (options.protocol == ApiProtocol::OpenAIResponses && type != event.end() &&
        (*type == "response.failed" || *type == "response.completed" ||
         *type == "response.incomplete") &&
        response != event.end() && response->is_object() && response->contains("error"))
        envelope = &*response;
    const auto error = envelope->find("error");
    if (error != envelope->end() && error->is_object())
        return detail::structured_provider_error(
            *error, options.protocol == ApiProtocol::AnthropicMessages ? "type" : "code",
            options.api_key, std::move(sdk_code), std::move(fallback));
    if (options.protocol == ApiProtocol::OpenAIResponses && type != event.end() && *type == "error")
        return detail::structured_provider_error(event, "code", options.api_key,
                                                 std::move(sdk_code), std::move(fallback));
    if (options.protocol == ApiProtocol::Ollama && error != envelope->end() && error->is_string()) {
        auto message =
            redact_error_message(error->get_ref<const std::string&>(), {options.api_key});
        if (!message.empty())
            return Error(std::move(sdk_code), std::move(message));
    }
    return Error(std::move(sdk_code), std::move(fallback));
}
HttpRequest request_for(const ApiOptions& options, const std::string& path) {
    HttpRequest request;
    request.url = options.base_url + path;
    request.timeout = options.timeout;
    request.headers = {{"Accept", "application/json"}, {"Content-Type", "application/json"}};
    if (options.protocol == ApiProtocol::AnthropicMessages) {
        request.headers["x-api-key"] = options.api_key;
        request.headers["anthropic-version"] = "2023-06-01";
    } else if (!options.api_key.empty())
        request.headers["Authorization"] = "Bearer " + options.api_key;
    return request;
}
Json get(const ApiOptions& options, const std::string& path, const CallContext& context) {
    auto request = request_for(options, path);
    request.max_response_bytes = provider_message_limit;
    auto response = HttpClient().perform(request, context.cancellation);
    http_status(response.status);
    return parse_json(response.body, provider_message_limit);
}
std::string percent_encode(const std::string& value) {
    const char* hex = "0123456789ABCDEF";
    std::string result;
    for (unsigned char c : value) {
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
            c == '-' || c == '_' || c == '.' || c == '~')
            result += static_cast<char>(c);
        else {
            result += '%';
            result += hex[c >> 4];
            result += hex[c & 15];
        }
    }
    return result;
}
// Incremental UTF-8 framing: bytes are decoded as JSON only after a complete frame.
class Frames {
    bool ndjson_;
    std::string line_, data_;
    std::function<void(const Json&)> consume_;
    void line() {
        if (!line_.empty() && line_.back() == '\r')
            line_.pop_back();
        if (ndjson_) {
            if (!line_.empty())
                consume_(parse_json(line_, frame_limit));
        } else if (line_.empty()) {
            if (!data_.empty()) {
                data_.pop_back();
                consume_(parse_json(data_, frame_limit));
                data_.clear();
            }
        } else if (line_.rfind("data:", 0) == 0)
            data_ += line_.substr(line_.size() > 5 && line_[5] == ' ' ? 6 : 5) + "\n";
        line_.clear();
    }

  public:
    Frames(bool ndjson, std::function<void(const Json&)> consume)
        : ndjson_(ndjson), consume_(std::move(consume)) {}
    void feed(const char* bytes, std::size_t size) {
        for (std::size_t i = 0; i < size; ++i) {
            if (bytes[i] == '\n')
                line();
            else
                line_ += bytes[i];
            if (line_.size() > frame_limit || data_.size() > frame_limit)
                throw Error("ResponseTooLarge", "API stream frame exceeds limit");
        }
    }
    void finish() {
        if (ndjson_ && !line_.empty())
            line();
        if (!line_.empty() || !data_.empty())
            throw Error("UnexpectedEOF", "Truncated API stream frame");
    }
};
void stream(HttpRequest request, const ApiOptions& options, const Json& body,
            const CallContext& context, const std::function<void(const Json&)>& consume) {
    request.method = "POST";
    request.headers["Accept"] =
        options.protocol == ApiProtocol::Ollama ? "application/x-ndjson" : "text/event-stream";
    request.body = body.dump();
    // A generation stream has no total deadline or size budget: only silence aborts it.
    request.idle_timeout = options.timeout;
    request.timeout = std::chrono::milliseconds(0);
    request.max_response_bytes = std::numeric_limits<std::size_t>::max();
    request.error_body_limit = detail::provider_error_body_limit;
    Frames frames(options.protocol == ApiProtocol::Ollama, consume);
    auto response =
        HttpClient().perform(request, context.cancellation, [&](const char* p, std::size_t n) {
            frames.feed(p, n);
            return true;
        });
    if (response.status < 200 || response.status >= 300)
        throw generation_error(detail::parse_provider_error_body(response.body), options,
                               "API request rejected with HTTP " + std::to_string(response.status),
                               detail::provider_http_error_code(response.status));
    frames.finish();
}
struct Tools {
    std::map<std::string, std::string> original;
    Json descriptions = Json::array();
    explicit Tools(const std::shared_ptr<const Registry>& registry) {
        if (!registry)
            return;
        for (auto tool : registry->describe()) {
            auto name = tool.at("name").get<std::string>();
            auto wire = name;
            if (wire.size() > 64 ||
                wire.find_first_not_of(
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-") !=
                    std::string::npos)
                wire = "cy_" + sha256(name).substr(0, 32);
            if (!original.emplace(wire, name).second)
                throw Error("InvalidOptions", "Tool wire-name collision");
            tool["name"] = wire;
            descriptions.push_back(std::move(tool));
        }
    }
    Json definitions(ApiProtocol protocol) const {
        Json result = Json::array();
        for (const auto& tool : descriptions) {
            Json function = {{"name", tool.at("name")}, {"description", tool.at("description")}};
            if (protocol == ApiProtocol::AnthropicMessages)
                function["input_schema"] = tool.at("inputSchema");
            else
                function["parameters"] = tool.at("inputSchema");
            if (protocol == ApiProtocol::OpenAIResponses) {
                function["type"] = "function";
                function["strict"] = false;
                result.push_back(function);
            } else if (protocol == ApiProtocol::AnthropicMessages)
                result.push_back(function);
            else
                result.push_back({{"type", "function"}, {"function", function}});
        }
        return result;
    }
    Json execute(const ApiOptions& options, const Json& call, const CallContext& context) const {
        auto wire = call.at("name").get<std::string>();
        auto id = call.at("id").get<std::string>();
        context.emit("ToolStarted", {{"id", id}, {"name", wire}});
        Json output;
        try {
            auto found = original.find(wire);
            if (!options.tools || found == original.end())
                throw Error("UnknownOperation", "Unknown API tool");
            auto args = call.at("arguments");
            if (args.is_string())
                args = parse_json(args.get<std::string>());
            output = options.tools->invoke(found->second, args, context);
        } catch (const Error& error) {
            if (error.code == "Cancelled" || error.code == "Timeout")
                throw;
            output = {{"error", {{"code", error.code}, {"message", error.what()}}}};
        } catch (const Json::exception&) {
            output = {{"error", {{"code", "InvalidArguments"}}}};
        }
        context.emit("ToolCompleted", {{"id", id}, {"name", wire}, {"output", output}});
        return output;
    }
};
// Result recorded for a call that never ran because the turn was cancelled or timed out.
std::string interrupted_output(const std::string& code) {
    return Json({{"error",
                  {{"code", code}, {"message", "Tool call interrupted before completion"}}}})
        .dump();
}
std::string protocol_id(ApiProtocol protocol) {
    return protocol == ApiProtocol::OpenAIResponses     ? "responses"
           : protocol == ApiProtocol::AnthropicMessages ? "anthropic"
                                                        : "ollama";
}
bool native(const Json& message, const std::string& protocol) {
    return message.contains("_cymcp_native") &&
           message.at("_cymcp_native").value("protocol", std::string()) == protocol;
}
Json history_for(const Json& messages, ApiProtocol protocol, std::string& system) {
    Json history = Json::array();
    for (const auto& message : messages) {
        auto role = message.at("role").get<std::string>();
        const auto& content = message.at("content");
        if (protocol == ApiProtocol::AnthropicMessages && role == "system") {
            system += detail::content_text(content) + "\n";
            continue;
        }
        if (role == "tool") {
            if (protocol == ApiProtocol::OpenAIResponses)
                history.push_back({{"type", "function_call_output"},
                                   {"call_id", message.at("tool_call_id")},
                                   {"output", content}});
            else if (protocol == ApiProtocol::AnthropicMessages) {
                Json block = {{"type", "tool_result"},
                              {"tool_use_id", message.at("tool_call_id")},
                              {"content", content}};
                if (!history.empty() && history.back().at("role") == "user" &&
                    history.back().at("content").is_array())
                    history.back()["content"].push_back(block);
                else
                    history.push_back({{"role", "user"}, {"content", Json::array({block})}});
            } else
                history.push_back(
                    {{"role", "tool"}, {"tool_name", message.at("name")}, {"content", content}});
        } else if (native(message, protocol_id(protocol))) {
            const auto& blocks = message.at("_cymcp_native").at("content");
            if (protocol == ApiProtocol::OpenAIResponses)
                for (const auto& item : blocks)
                    history.push_back(item);
            else if (protocol == ApiProtocol::AnthropicMessages)
                history.push_back({{"role", role}, {"content", blocks}});
            else
                history.push_back(blocks);
        } else if (protocol == ApiProtocol::Ollama && content.is_array()) {
            validate_content(content);
            Json images = Json::array();
            std::string text;
            for (const auto& block : content) {
                auto type = block.at("type").get<std::string>();
                if (type == "text")
                    text += block.at("text").get<std::string>();
                else if (type == "image")
                    images.push_back(block.at("data"));
                else if (type == "resource" && block.at("resource").contains("text"))
                    text += "\n" + block.at("resource").at("uri").get<std::string>() + "\n" +
                            block.at("resource").at("text").get<std::string>();
                else
                    throw Error("UnsupportedContent",
                                "Ollama chat supports text and embedded images");
            }
            Json wire_message = {{"role", role}, {"content", text}};
            if (!images.empty())
                wire_message["images"] = images;
            history.push_back(std::move(wire_message));
        } else {
            auto format =
                protocol == ApiProtocol::OpenAIResponses     ? detail::ContentFormat::Responses
                : protocol == ApiProtocol::AnthropicMessages ? detail::ContentFormat::Anthropic
                                                             : detail::ContentFormat::Ollama;
            history.push_back(
                {{"role", role}, {"content", detail::content_for(content, format, role)}});
        }
    }
    return history;
}
void append(std::string& text, const std::string& delta) {
    text += delta;
    if (text.size() > provider_message_limit)
        throw Error("ResponseTooLarge", "API answer exceeds the memory guard");
}
} // namespace
ApiProvider::ApiProvider(ApiOptions options)
    : Provider(options.protocol == ApiProtocol::OpenAIResponses     ? "openai"
               : options.protocol == ApiProtocol::AnthropicMessages ? "anthropic"
               : options.protocol == ApiProtocol::Ollama            ? "ollama"
                                                                    : "openrouter"),
      options_(std::move(options)) {
    options_.tools = compact_tools(std::move(options_.tools), options_.tool_discovery_threshold);
    if (options_.base_url.empty())
        switch (options_.protocol) {
        case ApiProtocol::OpenAIResponses:
            options_.base_url = "https://api.openai.com/v1";
            break;
        case ApiProtocol::AnthropicMessages:
            options_.base_url = "https://api.anthropic.com/v1";
            break;
        case ApiProtocol::Ollama:
            options_.base_url = "http://127.0.0.1:11434";
            break;
        case ApiProtocol::OpenRouter:
            options_.base_url = "https://openrouter.ai/api/v1";
            break;
        }
    while (!options_.base_url.empty() && options_.base_url.back() == '/')
        options_.base_url.pop_back();
    auto url = parse_url(options_.base_url);
    if (url.target.find('?') != std::string::npos || options_.max_output_tokens < 0 ||
        options_.timeout.count() <= 0 || options_.timeout > std::chrono::hours(24) ||
        options_.turn_timeout.count() < 0)
        throw Error("InvalidOptions", "Invalid API options");
    const auto& think = options_.ollama_think;
    if (!think.is_null() &&
        (options_.protocol != ApiProtocol::Ollama ||
         !(think.is_boolean() ||
           (think.is_string() && !think.get_ref<const std::string&>().empty()))))
        throw Error("InvalidOptions", "Ollama think requires a boolean or a model reasoning level");
}
bool ApiProvider::local_service() const noexcept {
    try {
        return parse_url(options_.base_url).loopback;
    } catch (...) {
        return false;
    }
}
void ApiProvider::validate_user_content(const Json& content) const {
    if (options_.protocol == ApiProtocol::OpenRouter) {
        detail::content_for(content, detail::ContentFormat::ChatCompletions);
        return;
    }
    std::string system;
    history_for(Json::array({{{"role", "user"}, {"content", content}}}), options_.protocol, system);
}
ProviderCapabilities ApiProvider::capabilities() const {
    bool chat = options_.protocol == ApiProtocol::OpenRouter;
    return {true,
            false,
            static_cast<bool>(options_.tools),
            true,
            true,
            true,
            chat,
            options_.protocol == ApiProtocol::OpenAIResponses ||
                options_.protocol == ApiProtocol::AnthropicMessages,
            false};
}
void ApiProvider::connect(const CallContext& input) {
    auto context = input;
    context.cancellation.deadline =
        std::min(context.cancellation.deadline, Clock::now() + options_.timeout);
    context.cancellation.check();
    if (options_.protocol != ApiProtocol::Ollama && options_.api_key.empty())
        throw Error("AuthenticationRequired", "Supply an API key through the host application");
    if (options_.protocol == ApiProtocol::OpenRouter)
        get(options_, "/key", context);
    list_models(context);
}
Json ApiProvider::list_models(const CallContext& input) {
    auto context = input;
    context.cancellation.deadline =
        std::min(context.cancellation.deadline, Clock::now() + options_.timeout);
    Json rows = Json::array();
    std::set<std::string> cursors;
    std::string path = options_.protocol == ApiProtocol::Ollama ? "/api/tags" : "/models";
    for (int page = 0; page < 128; ++page) {
        auto data = get(options_, path, context);
        auto entries =
            data.value(options_.protocol == ApiProtocol::Ollama ? "models" : "data", Json());
        if (!entries.is_array())
            throw Error("ProtocolError", "Missing API model catalogue");
        for (auto row : entries) {
            if (options_.protocol == ApiProtocol::Ollama)
                row["id"] = row.value("model", row.value("name", std::string()));
            if (!row.is_object() || !row.contains("id") || !row.at("id").is_string() ||
                row.at("id").get<std::string>().empty())
                throw Error("ProtocolError", "Invalid model catalogue entry");
            if (options_.protocol == ApiProtocol::AnthropicMessages && row.contains("max_tokens") &&
                row.at("max_tokens").is_number_integer() && row.at("max_tokens").get<int>() > 0) {
                std::lock_guard lock(limits_mutex_);
                output_limits_[row.at("id").get<std::string>()] = row.at("max_tokens").get<int>();
            }
            rows.push_back(std::move(row));
        }
        if (rows.size() > 100000)
            throw Error("ResponseTooLarge", "Model catalogue exceeds the memory guard");
        if (options_.protocol != ApiProtocol::AnthropicMessages || !data.value("has_more", false))
            return rows;
        auto cursor = data.value("last_id", std::string());
        if (cursor.empty() || !cursors.insert(cursor).second)
            throw Error("ProtocolError", "Invalid model catalogue pagination");
        path = "/models?after_id=" + percent_encode(cursor);
    }
    throw Error("ProtocolError", "Too many model catalogue pages");
}
int ApiProvider::output_limit(const std::string& model, const CallContext& context) {
    if (options_.max_output_tokens > 0)
        return options_.max_output_tokens;
    {
        std::lock_guard lock(limits_mutex_);
        if (auto found = output_limits_.find(model); found != output_limits_.end())
            return found->second;
    }
    int limit = fallback_output_tokens;
    try {
        auto row = get(options_, "/models/" + percent_encode(model), context);
        if (row.contains("max_tokens") && row.at("max_tokens").is_number_integer() &&
            row.at("max_tokens").get<int>() > 0)
            limit = row.at("max_tokens").get<int>();
    } catch (const Error&) {
        context.cancellation.check();
    }
    std::lock_guard lock(limits_mutex_);
    output_limits_[model] = limit;
    return limit;
}
Session ApiProvider::start_session(std::string model, std::string resume,
                                   const CallContext& context, std::string) {
    context.cancellation.check();
    if (!resume.empty())
        throw Error("Unsupported",
                    "API conversation history is owned by the host, not a native CLI session");
    if (model.empty())
        throw Error("UnknownModel", "Select an exact model ID");
    return {random_token(16), std::move(model)};
}
ChatResult ApiProvider::send(Session& session, Json& messages, const CallContext& input) {
    auto context = input;
    if (options_.turn_timeout.count() > 0)
        context.cancellation.deadline =
            std::min(context.cancellation.deadline, Clock::now() + options_.turn_timeout);
    if (!messages.is_array() || messages.empty())
        throw Error("InvalidParameters", "Messages required");
    if (options_.protocol == ApiProtocol::OpenRouter) {
        HttpChatOptions opts{options_.base_url,        options_.api_key, options_.tools,
                             options_.max_tool_rounds, options_.timeout, options_.max_output_tokens,
                             options_.turn_timeout};
        opts.tool_discovery_threshold = options_.tool_discovery_threshold;
        return HttpChatProvider(std::move(opts)).send(session, messages, context);
    }
    Tools tools(options_.tools);
    auto protocol = options_.protocol;
    for (std::size_t round = 0; options_.max_tool_rounds == 0 || round < options_.max_tool_rounds;
         ++round) {
        if (input.context_bytes_limit && serialized_size(messages) > input.context_bytes_limit)
            throw Error("ContextTooLarge", "Active tool turn exceeds the context budget");
        context.cancellation.check();
        ChatResult result;
        Json calls = Json::array(), content = Json::array();
        std::string system;
        auto history = history_for(messages, protocol, system);
        Json body = {{"model", session.model}, {"stream", true}};
        std::string path;
        if (protocol == ApiProtocol::OpenAIResponses) {
            path = "/responses";
            body["input"] = history;
            body["store"] = false;
            body["include"] = {"reasoning.encrypted_content"};
            if (options_.max_output_tokens > 0)
                body["max_output_tokens"] = options_.max_output_tokens;
        } else if (protocol == ApiProtocol::AnthropicMessages) {
            path = "/messages";
            body["messages"] = history;
            // Required by Messages: the model's own maximum unless the host sets a value.
            body["max_tokens"] = output_limit(session.model, context);
            if (!system.empty())
                body["system"] = system;
        } else {
            path = "/api/chat";
            body["messages"] = history;
            if (!options_.ollama_think.is_null())
                body["think"] = options_.ollama_think;
            if (options_.max_output_tokens > 0)
                body["options"] = {{"num_predict", options_.max_output_tokens}};
        }
        if (!tools.descriptions.empty())
            body["tools"] = tools.definitions(protocol);
        bool terminal = false;
        std::string reason, thinking;
        std::map<int, Json> blocks;
        std::map<int, std::string> arguments;
        std::set<int> closed_blocks;
        stream(request_for(options_, path), options_, body, context, [&](const Json& data) {
            if (!data.is_object())
                throw Error("ProtocolError", "Expected API event object");
            auto type = data.value("type", std::string());
            if (type == "error" || type == "response.failed" ||
                (data.contains("error") && !data.at("error").is_null()))
                throw generation_error(data, options_, "API generation failed");
            if (terminal)
                throw Error("ProtocolError", "API data after terminal result");
            std::string text;
            if (protocol == ApiProtocol::OpenAIResponses) {
                if (type == "response.output_text.delta")
                    text = data.at("delta").get<std::string>();
                else if (type == "response.reasoning_summary_text.delta")
                    context.emit("ThinkingDelta", {{"text", data.at("delta")}});
                else if (type == "response.completed" || type == "response.incomplete") {
                    auto response = data.at("response");
                    const auto status = response.value("status", std::string());
                    if (status != "completed" && status != "incomplete")
                        throw generation_error(data, options_, "OpenAI response did not complete");
                    // A truncated answer is a result, like Messages max_tokens: keep its text.
                    const bool incomplete = status == "incomplete";
                    content = response.at("output");
                    if (!content.is_array())
                        throw Error("ProtocolError", "Invalid Responses output");
                    result.text.clear();
                    for (const auto& item : content) {
                        auto kind = item.value("type", std::string());
                        if (kind == "message")
                            for (const auto& part : item.at("content")) {
                                if (part.value("type", std::string()) == "output_text")
                                    append(result.text, part.at("text").get<std::string>());
                                else if (part.value("type", std::string()) == "refusal")
                                    append(result.text, part.at("refusal").get<std::string>());
                            }
                        else if (kind == "function_call" && !incomplete)
                            calls.push_back({{"id", item.at("call_id")},
                                             {"name", item.at("name")},
                                             {"arguments", item.at("arguments")}});
                    }
                    if (incomplete) {
                        // Never replay a truncated function call without its output.
                        Json kept = Json::array();
                        for (const auto& item : content)
                            if (item.value("type", std::string()) != "function_call")
                                kept.push_back(item);
                        content = std::move(kept);
                    }
                    result.usage = response.value("usage", Json::object());
                    std::string why = "length";
                    if (auto details = response.value("incomplete_details", Json());
                        details.is_object() &&
                        details.value("reason", std::string("max_output_tokens")) !=
                            "max_output_tokens")
                        why = details.value("reason", std::string("incomplete"));
                    reason = incomplete ? why : calls.empty() ? "stop" : "tool_calls";
                    terminal = true;
                }
            } else if (protocol == ApiProtocol::AnthropicMessages) {
                if (type == "message_start")
                    result.usage = data.at("message").value("usage", Json::object());
                else if (type == "content_block_start") {
                    int index = data.at("index").get<int>();
                    if (index < 0 || index >= 65536 || blocks.contains(index))
                        throw Error("ProtocolError", "Invalid content block index");
                    blocks[index] = data.at("content_block");
                    if (blocks.at(index).value("type", std::string()) == "text")
                        text = blocks.at(index).value("text", std::string());
                } else if (type == "content_block_delta") {
                    int index = data.at("index").get<int>();
                    if (!blocks.contains(index) || closed_blocks.contains(index))
                        throw Error("ProtocolError", "Missing content block start");
                    auto& block = blocks.at(index);
                    auto delta = data.at("delta");
                    auto kind = delta.at("type").get<std::string>();
                    if (kind == "text_delta") {
                        text = delta.at("text").get<std::string>();
                        block["text"] = block.value("text", std::string()) + text;
                    } else if (kind == "input_json_delta") {
                        append(arguments[index], delta.at("partial_json").get<std::string>());
                    } else if (kind == "thinking_delta") {
                        auto value = delta.at("thinking").get<std::string>();
                        block["thinking"] = block.value("thinking", std::string()) + value;
                        context.emit("ThinkingDelta", {{"text", value}});
                    } else if (kind == "signature_delta")
                        block["signature"] = block.value("signature", std::string()) +
                                             delta.at("signature").get<std::string>();
                } else if (type == "content_block_stop") {
                    int index = data.at("index").get<int>();
                    if (!blocks.contains(index) || !closed_blocks.insert(index).second)
                        throw Error("ProtocolError", "Invalid content block stop");
                } else if (type == "message_delta") {
                    reason = data.at("delta").value("stop_reason", std::string());
                    if (data.contains("usage"))
                        result.usage.update(data.at("usage"));
                } else if (type == "message_stop") {
                    if (reason.empty() || closed_blocks.size() != blocks.size())
                        throw Error("ProtocolError", "Incomplete Anthropic message");
                    for (auto& [index, block] : blocks) {
                        if (block.value("type", std::string()) == "tool_use") {
                            // max_tokens/refusal can cut a call: drop it rather than replaying
                            // an unanswerable tool_use.
                            if (reason != "tool_use")
                                continue;
                            if (arguments.contains(index))
                                block["input"] = parse_json(arguments.at(index), frame_limit);
                            calls.push_back({{"id", block.at("id")},
                                             {"name", block.at("name")},
                                             {"arguments", block.at("input")}});
                        }
                        content.push_back(block);
                    }
                    terminal = true;
                }
            } else {
                auto message = data.value("message", Json::object());
                text = message.value("content", std::string());
                if (message.contains("thinking")) {
                    auto delta = message.at("thinking").get<std::string>();
                    append(thinking, delta);
                    context.emit("ThinkingDelta", {{"text", delta}});
                }
                if (message.contains("tool_calls"))
                    for (const auto& call : message.at("tool_calls"))
                        content.push_back(call);
                terminal = data.value("done", false);
                if (terminal) {
                    reason = data.value("done_reason", std::string("stop"));
                    for (auto key :
                         {"prompt_eval_count", "eval_count", "total_duration", "load_duration"})
                        if (data.contains(key))
                            result.usage[key] = data.at(key);
                }
            }
            if (!text.empty()) {
                append(result.text, text);
                context.emit("TextDelta", {{"text", text}});
            }
        });
        if (!terminal)
            throw Error("UnexpectedEOF", "API stream ended before its final result");
        if (protocol == ApiProtocol::Ollama) {
            // Only a completed generation may commit or execute tool calls. Truncated fragments
            // may even lack their arguments; retain the text and finish reason in that case.
            if (reason != "stop")
                content.clear();
            for (const auto& call : content)
                calls.push_back({{"id", "ollama_" + std::to_string(calls.size())},
                                 {"name", call.at("function").at("name")},
                                 {"arguments", call.at("function").at("arguments")}});
        }
        if (!calls.empty() &&
            ((protocol == ApiProtocol::AnthropicMessages && reason != "tool_use") ||
             (protocol == ApiProtocol::OpenAIResponses && reason != "tool_calls")))
            throw Error("ProtocolError", "Invalid tool completion reason");
        if (protocol == ApiProtocol::AnthropicMessages && calls.empty() && reason == "tool_use")
            throw Error("ProtocolError", "Missing Anthropic tool calls");
        std::set<std::string> ids;
        for (const auto& call : calls) {
            auto id = call.at("id").get<std::string>();
            if (id.empty() || !ids.insert(id).second)
                throw Error("ProtocolError", "Missing or duplicate API tool call ID");
        }
        if (protocol == ApiProtocol::Ollama) {
            Json assistant = {{"role", "assistant"}, {"content", result.text}};
            if (!thinking.empty())
                assistant["thinking"] = thinking;
            if (!content.empty())
                assistant["tool_calls"] = content;
            content = assistant;
        }
        messages.push_back(
            {{"role", "assistant"},
             {"content", result.text},
             {"_cymcp_native", {{"protocol", protocol_id(protocol)}, {"content", content}}}});
        result.finish_reason = reason;
        context.emit("UsageUpdated", result.usage);
        if (calls.empty()) {
            context.emit("TurnCompleted", {{"text", result.text}, {"finishReason", reason}});
            return result;
        }
        std::size_t answered = 0;
        try {
            for (const auto& call : calls) {
                auto output = tools.execute(options_, call, context);
                messages.push_back({{"role", "tool"},
                                    {"tool_call_id", call.at("id")},
                                    {"name", call.at("name")},
                                    {"content", output.dump()}});
                ++answered;
            }
        } catch (const Error& error) {
            // Every recorded call needs a result, or the next request is rejected (HTTP 400).
            for (std::size_t n = answered; n < calls.size(); ++n)
                messages.push_back({{"role", "tool"},
                                    {"tool_call_id", calls[n].at("id")},
                                    {"name", calls[n].at("name")},
                                    {"content", interrupted_output(error.code)}});
            throw;
        }
    }
    throw Error("ToolBudgetExceeded", "Maximum API tool rounds reached");
}
} // namespace cymcp
