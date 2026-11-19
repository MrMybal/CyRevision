#include <cymcp/ai.hpp>
#include <set>
#include <sstream>
namespace cymcp {
struct ClaudeProvider::Impl {
    ClaudeOptions options;
    std::unique_ptr<Process> process;
    // MCP servers often carry tokens in env/headers; system prompts can exceed the Windows
    // 32 767-character command line. Both are passed as owner-only files for the process life.
    std::unique_ptr<PrivateFile> mcp_file, system_file;
    mutable std::mutex mutex;
    std::mutex cancellation_mutex;
    Cancellation active;
    std::string session_id;
    explicit Impl(ClaudeOptions o) : options(std::move(o)) {}
    CallContext begin(const CallContext& input, std::chrono::milliseconds timeout) {
        auto context = input;
        context.cancellation = input.cancellation.child();
        if (timeout.count() > 0) // 0: no deadline, only cancellation ends the operation
            context.cancellation.deadline =
                std::min(context.cancellation.deadline, Clock::now() + timeout);
        std::lock_guard lock(cancellation_mutex);
        active = context.cancellation;
        return context;
    }
    void reset() noexcept {
        process.reset();
        mcp_file.reset();
        system_file.reset();
        session_id.clear();
    }
};
ClaudeProvider::ClaudeProvider(ClaudeOptions options)
    : Provider("claude-code"), impl_(std::make_unique<Impl>(std::move(options))) {
    if (!impl_->options.mcp_servers.is_object() || impl_->options.connect_timeout.count() <= 0 ||
        impl_->options.connect_timeout > std::chrono::hours(24) ||
        impl_->options.turn_timeout.count() < 0 ||
        impl_->options.turn_timeout > std::chrono::hours(24))
        throw Error("InvalidOptions", "Invalid Claude options");
    const auto& sources = impl_->options.setting_sources;
    std::set<std::string> seen;
    std::istringstream input(sources);
    for (std::string source; std::getline(input, source, ',');)
        if ((source != "user" && source != "project" && source != "local") ||
            !seen.insert(source).second)
            throw Error("InvalidOptions", "Invalid Claude setting sources");
    if (!sources.empty() && sources.back() == ',')
        throw Error("InvalidOptions", "Invalid Claude setting sources");
}
ClaudeProvider::~ClaudeProvider() {
    disconnect();
}
ProviderCapabilities ClaudeProvider::capabilities() const {
    const auto& o = impl_->options;
    return {true,
            true,
            !o.mcp_servers.empty() || !o.allowed_tools.empty() || o.allow_all_actions,
            false,
            o.persistent,
            o.persistent,
            false,
            o.persistent,
            o.persistent};
}
void ClaudeProvider::connect(const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    auto config = p.options.process;
    // Availability is separate from authentication. Let the native CLI resolve
    // its account, environment, helper or provider when an actual turn is sent.
    config.arguments.push_back("--version");
    Process probe(config);
    probe.close_input();
    std::string output;
    ProcessLine line;
    while (probe.next(line, context.cancellation))
        if (!line.stderr_stream) {
            output += line.text;
            if (output.size() > 1024 * 1024)
                throw Error("ResponseTooLarge", "Claude version response exceeds limit");
        }
    auto code = probe.wait(context.cancellation);
    if (code != 0)
        throw Error("ConnectionFailed", "Claude runtime check failed");
    if (output.empty())
        throw Error("ProtocolError", "Claude runtime returned no version");
    context.emit("ProviderReady", {{"authentication", "unverified"}});
}
Session ClaudeProvider::start_session(std::string model, std::string resume,
                                      const CallContext& context, std::string) {
    context.cancellation.check();
    std::lock_guard lock(impl_->mutex);
    impl_->reset();
    return {std::move(resume), std::move(model)};
}
ChatResult ClaudeProvider::send(Session& session, Json& messages, const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.turn_timeout);
    if (messages.empty())
        throw Error("InvalidParameters", "Messages required");
    auto content = p.options.persistent ? detail::content_for(messages.back().at("content"),
                                                              detail::ContentFormat::Anthropic)
                                        : Json(detail::content_text(messages.back().at("content")));
    if (p.process && p.session_id != session.id)
        throw Error("SessionInvalid", "Claude session is not active");
    try {
        if (!p.process) {
            auto config = p.options.process;
            p.mcp_file = std::make_unique<PrivateFile>(
                "mcp.json", Json({{"mcpServers", p.options.mcp_servers}}).dump());
            config.arguments.insert(config.arguments.end(),
                                    {"-p", "--output-format", "stream-json", "--verbose",
                                     "--include-partial-messages", "--strict-mcp-config",
                                     "--mcp-config", path_utf8(p.mcp_file->path()),
                                     "--setting-sources", p.options.setting_sources});
            if (p.options.persistent)
                config.arguments.insert(config.arguments.end(), {"--input-format", "stream-json"});
            if (p.options.allow_all_actions)
                config.arguments.push_back("--dangerously-skip-permissions");
            else {
                config.arguments.insert(config.arguments.end(),
                                        {"--permission-mode", "dontAsk", "--tools"});
                std::string tools;
                for (const auto& tool : p.options.allowed_tools) {
                    if (!tools.empty())
                        tools += ",";
                    tools += tool;
                }
                config.arguments.push_back(tools);
            }
            if (!session.model.empty())
                config.arguments.insert(config.arguments.end(), {"--model", session.model});
            if (!session.id.empty())
                config.arguments.insert(config.arguments.end(), {"--resume", session.id});
            std::string system;
            for (const auto& message : messages)
                if (message.value("role", std::string()) == "system")
                    system += detail::content_text(message.at("content")) + "\n";
            if (!system.empty()) {
                p.system_file = std::make_unique<PrivateFile>("system-prompt.md", system);
                config.arguments.insert(config.arguments.end(),
                                        {"--append-system-prompt-file",
                                         path_utf8(p.system_file->path())});
            }
            p.process = std::make_unique<Process>(config);
            p.session_id = session.id;
            context.emit(
                "ProcessStarted",
                {{"transport", p.options.persistent ? "claude-stream-input" : "claude-print"}});
        }
        if (p.options.persistent)
            p.process->write(Json({{"type", "user"},
                                   {"session_id", session.id},
                                   {"parent_tool_use_id", nullptr},
                                   {"message", {{"role", "user"}, {"content", content}}}})
                                     .dump() +
                                 "\n",
                             context.cancellation);
        else {
            p.process->write(content.get<std::string>(), context.cancellation);
            p.process->close_input();
        }
        ChatResult result;
        std::string reported_error, native_error;
        ProcessLine line;
        bool completed = false;
        while (p.process->next(line, context.cancellation)) {
            if (line.stderr_stream || line.text.empty())
                continue;
            Json value;
            try {
                value = parse_json(line.text, provider_message_limit);
            } catch (...) {
                throw Error("MalformedJSON", "Invalid Claude stream JSON");
            }
            if (!value.is_object())
                throw Error("ProtocolError", "Expected Claude event object");
            if (completed)
                throw Error("ProtocolError", "Claude emitted events after its terminal result");
            if (value.contains("session_id")) {
                auto native = value.at("session_id").get<std::string>();
                if (native.empty() || (!session.id.empty() && session.id != native))
                    throw Error("SessionInvalid", "Claude returned a different session ID");
                session.id = native;
                p.session_id = native;
            }
            auto type = value.value("type", std::string());
            if (type == "assistant" && value.contains("error") && value.at("error").is_string()) {
                native_error = value.at("error").get<std::string>();
                reported_error =
                    native_error == "authentication_failed" ||
                            native_error == "cloud_credential_error"
                        ? "AuthenticationRequired"
                    : native_error == "rate_limit"    ? "RateLimited"
                    : native_error == "billing_error" ? "BillingError"
                    : native_error == "oauth_org_not_allowed" || native_error == "account_on_hold"
                        ? "AccessDenied"
                    : native_error == "model_not_found"   ? "UnknownModel"
                    : native_error == "invalid_request"   ? "InvalidRequest"
                    : native_error == "max_output_tokens" ? "TurnLimitExceeded"
                                                          : "ProviderError";
            }
            if (type == "system" && value.contains("model") && value.at("model").is_string())
                session.model = value.at("model").get<std::string>();
            if (type == "control_request") {
                p.process->write(Json({{"type", "control_response"},
                                       {"response",
                                        {{"subtype", "error"},
                                         {"request_id", value.at("request_id")},
                                         {"error", "Interactive client capability not provided"}}}})
                                         .dump() +
                                     "\n",
                                 context.cancellation);
            } else if (type == "stream_event") {
                auto delta = value.value("event", Json::object()).value("delta", Json::object());
                if (delta.value("type", std::string()) == "text_delta")
                    context.emit("TextDelta", {{"text", delta.value("text", std::string())}});
                else if (delta.value("type", std::string()) == "thinking_delta")
                    context.emit("ThinkingDelta",
                                 {{"text", delta.value("thinking", std::string())}});
            } else if (type == "result") {
                const auto subtype = value.value("subtype", std::string());
                if (value.value("is_error", false) || subtype != "success") {
                    const auto secrets = process_error_secrets(p.options.process);
                    std::string detail;
                    const auto errors = value.find("errors");
                    if (errors != value.end() && errors->is_array()) {
                        for (const auto& entry : *errors) {
                            if (!entry.is_string())
                                continue;
                            const auto message =
                                redact_error_message(entry.get_ref<const std::string&>(), secrets);
                            if (message.empty())
                                continue;
                            if (!detail.empty())
                                detail += " ";
                            detail += message;
                            if (detail.size() >= 4096)
                                break;
                        }
                    }
                    if (native_error.empty() && subtype != "success")
                        native_error = subtype;
                    throw Error(reported_error.empty() ? "ProviderError" : reported_error,
                                detail.empty() ? "Native Claude turn failed"
                                               : redact_error_message(detail, secrets),
                                redact_error_message(native_error, secrets));
                }
                result.text = value.at("result").get<std::string>();
                result.usage = value.value("usage", Json::object());
                result.finish_reason = "stop";
                completed = true;
                if (p.options.persistent) {
                    if (!p.process->running() && p.process->wait(context.cancellation) != 0)
                        throw Error("ProcessCrash", "Claude exited with an error");
                    break;
                }
            } else if ((type == "assistant" || type == "user") && value.contains("message")) {
                auto blocks = value.at("message").value("content", Json::array());
                if (blocks.is_array())
                    for (const auto& item : blocks)
                        if (item.is_object() &&
                            (item.value("type", std::string()) == "tool_use" ||
                             item.value("type", std::string()) == "tool_result"))
                            context.emit(type == "assistant" ? "ToolStarted" : "ToolCompleted",
                                         item);
            } else
                context.emit("ProviderEvent", value);
        }
        if (!p.options.persistent && p.process->wait(context.cancellation) != 0)
            throw Error("ProcessCrash", "Claude exited with an error");
        if (!completed)
            throw Error("UnexpectedEOF", "Claude stream ended before its final result");
        if (session.id.empty())
            throw Error("ProtocolError", "Missing native Claude session ID");
        if (!p.options.persistent)
            p.reset();
        messages.push_back({{"role", "assistant"}, {"content", result.text}});
        context.emit("UsageUpdated", result.usage);
        context.emit("TurnCompleted", {{"text", result.text}});
        return result;
    } catch (...) {
        p.reset();
        throw;
    }
}
void ClaudeProvider::disconnect() noexcept {
    auto& p = *impl_;
    {
        std::lock_guard lock(p.cancellation_mutex);
        p.active.cancel();
    }
    std::lock_guard lock(p.mutex);
    p.reset();
}
} // namespace cymcp
