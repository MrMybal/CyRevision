#include <cymcp/ai.hpp>
#include <sstream>
namespace cymcp {
namespace {
std::string probe(ProcessOptions config, const std::vector<std::string>& args,
                  const CallContext& context) {
    config.arguments.insert(config.arguments.end(), args.begin(), args.end());
    Process process(config);
    process.close_input();
    std::string output;
    ProcessLine line;
    while (process.next(line, context.cancellation)) {
        if (line.stderr_stream)
            continue;
        if (output.size() + line.text.size() + 1 > 1024 * 1024)
            throw Error("ResponseTooLarge", "CLI discovery exceeds limit");
        output += line.text + "\n";
    }
    if (process.wait(context.cancellation) != 0)
        throw Error("ConnectionFailed",
                    "CLI discovery failed; check the native installation/login");
    return output;
}
void native_id(Session& session, const Json& value, const char* key) {
    if (!value.contains(key))
        return;
    auto id = value.at(key).get<std::string>();
    if (id.empty() || (!session.id.empty() && id != session.id))
        throw Error("SessionInvalid", "CLI returned a different or empty session ID");
    session.id = std::move(id);
}
} // namespace
AgentCliProvider::AgentCliProvider(AgentCliOptions options)
    : Provider(options.protocol == AgentCliProtocol::OpenCode      ? "opencode-cli"
               : options.protocol == AgentCliProtocol::Antigravity ? "antigravity"
               : options.protocol == AgentCliProtocol::CommandCode ? "command-code"
               : options.protocol == AgentCliProtocol::CodexExec   ? "codex-exec"
                                                                   : "codebuddy"),
      options_(std::move(options)) {
    if (!options_.native_settings.is_object() || options_.connect_timeout.count() <= 0 ||
        options_.turn_timeout.count() < 0 || options_.connect_timeout > std::chrono::hours(24) ||
        options_.turn_timeout > std::chrono::hours(24))
        throw Error("InvalidOptions", "Invalid CLI options");
    if (options_.protocol != AgentCliProtocol::CodeBuddy &&
        (!options_.mcp_config_file.empty() || !options_.allowed_tools.empty()))
        throw Error("Unsupported", "These tool configuration fields belong to CodeBuddy CLI");
    if ((options_.protocol == AgentCliProtocol::CommandCode ||
         options_.protocol == AgentCliProtocol::CodexExec) &&
        !options_.native_settings.empty())
        throw Error("Unsupported", "Use an explicit argument prefix for native configuration");
    if (options_.persistent && options_.protocol != AgentCliProtocol::Antigravity)
        throw Error("Unsupported",
                    "Persistent stream input is implemented for Antigravity; use ACP for OpenCode");
}
AgentCliProvider::~AgentCliProvider() {
    disconnect();
}
ProviderCapabilities AgentCliProvider::capabilities() const {
    bool buddy = options_.protocol == AgentCliProtocol::CodeBuddy;
    return {true,
            true,
            !buddy || options_.allow_all_actions || !options_.mcp_config_file.empty() ||
                !options_.allowed_tools.empty(),
            !buddy && options_.protocol != AgentCliProtocol::CodexExec,
            false,
            false,
            false,
            false,
            options_.persistent};
}
CallContext AgentCliProvider::begin(const CallContext& input, std::chrono::milliseconds timeout) {
    auto context = input;
    context.cancellation = input.cancellation.child();
    if (timeout.count() > 0) // 0: no deadline, only cancellation ends the operation
        context.cancellation.deadline =
            std::min(context.cancellation.deadline, Clock::now() + timeout);
    std::lock_guard lock(cancellation_mutex_);
    active_ = context.cancellation;
    return context;
}
void AgentCliProvider::connect(const CallContext& input) {
    std::lock_guard lock(mutex_);
    connected_ = false;
    auto context = begin(input, options_.connect_timeout);
    auto native_version = probe(options_.process, {"--version"}, context);
    if (options_.protocol == AgentCliProtocol::OpenCode) {
        auto start = native_version.find_first_of("0123456789");
        if (start == std::string::npos || native_version.compare(start, 2, "1.") != 0)
            throw Error("UnsupportedVersion", "This OpenCode adapter targets CLI 1.x JSONL");
    }
    connected_ = true;
    context.emit("ProviderReady", {{"authentication", "unverified"}});
}
Json AgentCliProvider::list_models(const CallContext& input) {
    std::lock_guard lock(mutex_);
    auto context = begin(input, options_.connect_timeout);
    if (!capabilities().model_catalogue)
        throw Error("Unsupported", "This CLI selects its model by ID or native default");
    auto output = probe(options_.process,
                        options_.protocol == AgentCliProtocol::CommandCode
                            ? std::vector<std::string>{"--list-models"}
                            : std::vector<std::string>{"models"},
                        context);
    Json rows = Json::array();
    if (options_.protocol == AgentCliProtocol::Antigravity ||
        options_.protocol == AgentCliProtocol::CommandCode) {
        auto first = output.find_first_not_of(" \r\n\t");
        if (first != std::string::npos && (output[first] == '[' || output[first] == '{')) {
            auto data = parse_json(output, provider_message_limit);
            rows = data.is_array() ? data : data.value("models", data.value("data", Json::array()));
            if (!rows.is_array())
                throw Error("ProtocolError", "Invalid CLI catalogue");
            for (auto& row : rows)
                if (row.is_string())
                    row = {{"id", row.get<std::string>()}};
        } else if (options_.protocol == AgentCliProtocol::Antigravity) {
            std::istringstream stream(output);
            for (std::string line; std::getline(stream, line);) {
                auto tab = line.find('\t');
                if (tab != std::string::npos && tab > 0 && line.substr(0, tab) != "ID" &&
                    line.substr(0, tab) != "MODEL")
                    rows.push_back({{"id", line.substr(0, tab)}, {"name", line.substr(tab + 1)}});
            }
        } else
            throw Error("Unsupported",
                        "Command Code returned a text catalogue; supply an exact model ID");
    } else {
        std::istringstream stream(output);
        for (std::string line; std::getline(stream, line);)
            if (!line.empty() && line.find('/') != std::string::npos &&
                line.find_first_of(" \t\r\x1b") == std::string::npos)
                rows.push_back({{"id", line}});
    }
    if (rows.empty())
        throw Error("ModelDiscoveryFailed", "No model IDs found in CLI catalogue");
    for (const auto& row : rows)
        if (!row.is_object() || !row.contains("id") || !row["id"].is_string() ||
            row["id"].get<std::string>().empty())
            throw Error("ProtocolError", "Model catalogue contains an invalid ID");
    return rows;
}
Session AgentCliProvider::start_session(std::string model, std::string resume,
                                        const CallContext& context, std::string) {
    context.cancellation.check();
    std::lock_guard lock(mutex_);
    if (!connected_)
        throw Error("InvalidState", "Connect CLI before opening a session");
    return {std::move(resume), std::move(model)};
}
ChatResult AgentCliProvider::send(Session& session, Json& messages, const CallContext& input) {
    std::lock_guard lock(mutex_);
    if (!connected_)
        throw Error("InvalidState", "Connect CLI first");
    auto context = begin(input, options_.turn_timeout);
    if (!messages.is_array() || messages.empty())
        throw Error("InvalidParameters", "Messages required");
    auto prompt = detail::content_text(messages.back().at("content"));
    // These CLIs have no portable system-role flag. Supply first-turn context as text.
    if (session.id.empty()) {
        std::string instructions;
        for (const auto& message : messages)
            if (message.value("role", std::string()) == "system") {
                if (!message.at("content").is_string())
                    throw Error("Unsupported", "System context must be text");
                instructions += message.at("content").get<std::string>() + "\n";
            }
        if (!instructions.empty())
            prompt = "Application instructions:\n" + instructions + "\nUser message:\n" + prompt;
    }
    auto config = options_.process;
    auto& args = config.arguments;
    std::unique_ptr<PrivateFile> settings_file; // kept for the launched process only
    const auto settings_path = [&] {
        settings_file =
            std::make_unique<PrivateFile>("settings.json", options_.native_settings.dump());
        return path_utf8(settings_file->path());
    };
    bool agy = options_.protocol == AgentCliProtocol::Antigravity;
    bool command = options_.protocol == AgentCliProtocol::CommandCode;
    bool buddy = options_.protocol == AgentCliProtocol::CodeBuddy;
    const bool codex = options_.protocol == AgentCliProtocol::CodexExec;
    if (codex) {
        args.insert(args.end(),
                    {"--sandbox", options_.allow_all_actions ? "danger-full-access" : "read-only",
                     "--ask-for-approval", "never", "exec"});
        if (!session.id.empty())
            args.push_back("resume");
        args.insert(args.end(), {"--json", "--skip-git-repo-check"});
    } else if (agy) {
        args.insert(args.end(), {"--input-format", "stream-json", "--output-format", "stream-json",
                                 "--disable-slash-commands"});
        if (options_.allow_all_actions)
            args.push_back("--dangerously-skip-permissions");
        else
            args.insert(args.end(), {"--mode", "plan", "--sandbox"});
        if (!options_.native_settings.empty() && !process_)
            args.insert(args.end(), {"--settings", settings_path()});
        if (!session.id.empty())
            args.insert(args.end(), {"--conversation", session.id});
    } else if (command) {
        args.insert(args.end(), {"-p", "--output-format", "json", "--skip-onboarding"});
        args.push_back(options_.allow_all_actions ? "--yolo" : "--plan");
        if (!options_.native_settings.empty())
            throw Error("Unsupported",
                        "Use an explicit Command Code argument prefix for native configuration");
        if (!session.id.empty())
            args.insert(args.end(), {"--resume", session.id});
    } else if (buddy) {
        args.insert(args.end(),
                    {"-p", "--input-format", "stream-json", "--output-format", "stream-json",
                     "--verbose", "--strict-mcp-config", "--setting-sources", ""});
        config.environment["CODEBUDDY_CODE_DISABLE_BACKGROUND_TASKS"] = "1";
        if (!options_.mcp_config_file.empty())
            args.insert(
                args.end(),
                {"--mcp-config", path_utf8(std::filesystem::absolute(options_.mcp_config_file))});
        if (!options_.native_settings.empty() && !process_)
            args.insert(args.end(), {"--settings", settings_path()});
        if (options_.allow_all_actions)
            args.insert(args.end(), {"--permission-mode", "bypassPermissions",
                                     "--dangerously-skip-permissions"});
        else {
            args.insert(args.end(), {"--permission-mode", "dontAsk", "--tools"});
            std::string tools;
            for (const auto& name : options_.allowed_tools) {
                if (!tools.empty())
                    tools += ",";
                tools += name;
            }
            args.push_back(tools);
        }
        if (!session.id.empty())
            args.insert(args.end(), {"--resume", session.id});
    } else {
        args.insert(args.end(), {"run", "--format", "json"});
        if (!session.id.empty())
            args.insert(args.end(), {"--session", session.id});
        auto settings = options_.native_settings;
        settings["permission"] = {{"*", options_.allow_all_actions ? "allow" : "deny"}};
        config.environment["OPENCODE_CONFIG_CONTENT"] = settings.dump();
    }
    if (!session.model.empty())
        args.insert(args.end(), {"--model", session.model});
    if (codex) {
        args.push_back("--");
        if (!session.id.empty())
            args.push_back(session.id);
        args.push_back("-");
    } else if (options_.protocol == AgentCliProtocol::OpenCode) {
#ifdef _WIN32
        // OpenCode 1.x only accepts its message as an argument.
        if (prompt.size() > 30000)
            throw Error("MessageTooLarge",
                        "OpenCode CLI 1.x takes the message on the Windows command line (32 767 "
                        "characters); use the 'opencode' ACP profile for long messages");
#endif
        args.insert(args.end(), {"--", prompt});
    }
    if (process_ && active_session_ != session.id)
        throw Error("SessionInvalid", "Persistent CLI session mismatch");
    bool launched = !process_;
    if (!process_) {
        process_ = std::make_unique<Process>(config);
        settings_file_ = std::move(settings_file);
        active_session_ = session.id;
    }
    auto& process = *process_;
    struct Cleanup {
        AgentCliProvider& owner;
        bool keep = false;
        ~Cleanup() {
            if (!keep) {
                owner.process_.reset();
                owner.active_session_.clear();
            }
        }
    } cleanup{*this};
    if (launched)
        context.emit("ProcessStarted", {{"transport", agy ? "agy-stream-input" : "cli-jsonl"}});
    if (agy)
        process.write(Json({{"event", "user"}, {"message", {{"content", prompt}}}}).dump() + "\n",
                      context.cancellation);
    else if (buddy)
        process.write(Json({{"type", "user"},
                            {"message",
                             {{"role", "user"},
                              {"content", Json::array({{{"type", "text"}, {"text", prompt}}})}}}})
                              .dump() +
                          "\n",
                      context.cancellation);
    else if (command || codex)
        process.write(prompt, context.cancellation);
    if (!options_.persistent)
        process.close_input();
    ChatResult result;
    bool completed = false;
    std::string codex_error;
    std::map<std::string, std::string> snapshots;
    std::size_t snapshot_bytes = 0;
    ProcessLine line;
    while (process.next(line, context.cancellation)) {
        if (line.stderr_stream || line.text.empty())
            continue;
        auto data = parse_json(line.text, provider_message_limit);
        if (!data.is_object())
            throw Error("ProtocolError", "Expected CLI event object");
        auto kind = data.value(agy ? "event" : "type", std::string());
        auto payload = agy ? data.value(kind, Json::object()) : data;
        if (payload.is_object())
            native_id(session, payload,
                      codex     ? "thread_id"
                      : agy     ? "conversation_id"
                      : command ? "sessionId"
                      : buddy   ? "session_id"
                                : "sessionID");
        if (agy)
            native_id(session, data, "conversation_id");
        const auto error = data.find("error");
        const bool open_code_error = options_.protocol == AgentCliProtocol::OpenCode &&
                                     kind == "error" && error != data.end() && error->is_object();
        if (open_code_error || (codex && (kind == "error" || kind == "turn.failed"))) {
            const auto& fields = error != data.end() && error->is_object() ? *error : data;
            const auto message = fields.find("message");
            if (message != fields.end() && message->is_string() &&
                !message->get_ref<const std::string&>().empty()) {
                auto detail = redact_error_message(message->get_ref<const std::string&>(),
                                                   process_error_secrets(options_.process));
                if (open_code_error && !detail.empty())
                    throw Error("ProviderError", std::move(detail));
                codex_error = std::move(detail);
            }
            // An error event can describe a native reconnect. The turn result decides
            // whether the generation failed. A one-shot CLI can also exit after just an error.
            if (kind == "error" && !completed && !codex_error.empty())
                continue;
        }
        if (kind == "error" || kind == "turn.failed" || data.contains("error"))
            throw Error("ProviderError",
                        codex_error.empty() ? "CLI reported a provider error" : codex_error);
        if (completed)
            throw Error("ProtocolError", "CLI emitted events after its terminal result");
        std::string text;
        if (codex && (kind == "item.completed" || kind == "item.started")) {
            const auto& item = data.at("item");
            const auto type = item.at("type").get<std::string>();
            if (kind == "item.completed" && type == "agent_message")
                text = item.at("text").get<std::string>();
            else if (type == "command_execution" || type == "mcp_tool_call")
                context.emit(kind == "item.started" ? "ToolStarted" : "ToolCompleted", item);
        } else if (codex && kind == "turn.completed") {
            result.usage = data.value("usage", Json::object());
            completed = true;
        } else if (command && kind == "event") {
            auto event = data.at("event");
            auto event_type = event.at("type").get<std::string>();
            native_id(session, event, "sessionId");
            if (event_type == "text_delta")
                text = event.value("delta", event.value("text", std::string()));
            else if (event_type == "thinking_delta")
                context.emit("ThinkingDelta",
                             {{"text", event.value("delta", event.value("text", std::string()))}});
            else if (event_type == "tool_running")
                context.emit("ToolStarted", event);
            else if (event_type == "tool_completed" || event_type == "tool_errored" ||
                     event_type == "tool_hook_blocked")
                context.emit("ToolCompleted", event);
            else if (event_type == "tool_denied")
                throw Error("AccessDenied", "Command Code denied an action");
            else if (event_type == "interrupted")
                throw Error("Cancelled", "Command Code interrupted the turn", event_type);
            else if (event_type == "run_error")
                throw Error("ProviderError", "Command Code run failed", event_type);
            else
                context.emit("ProviderEvent", event);
        } else if ((command || buddy) && kind == "result") {
            auto subtype = data.value("subtype", std::string());
            if (subtype == "max_turns" || subtype == "error_max_turns")
                throw Error("TurnLimitExceeded", "Native agent turn limit reached", subtype);
            if (subtype != "success" || data.value("is_error", false) ||
                (data.contains("denied_actions") && !data.at("denied_actions").empty()))
                throw Error(
                    "ProviderError", "Native agent turn failed",
                    subtype == "success"
                        ? ""
                        : redact_error_message(subtype, process_error_secrets(options_.process)));
            result.text = data.at(command ? "finalText" : "result").get<std::string>();
            result.usage = data.value("usage", Json::object());
            completed = true;
        } else if (buddy && kind == "assistant") {
            const auto& message = data.at("message");
            std::string snapshot;
            for (const auto& block : message.at("content")) {
                auto type = block.value("type", std::string());
                if (type == "text")
                    snapshot += block.at("text").get<std::string>();
                else if (type == "tool_use")
                    context.emit("ToolStarted", block);
            }
            if (!snapshot.empty()) {
                auto key = message.value("id", data.value("uuid", std::string("assistant")));
                auto& previous = snapshots[key];
                snapshot_bytes -= previous.size();
                snapshot_bytes += snapshot.size();
                if (snapshot_bytes > provider_message_limit || snapshots.size() > 65536)
                    throw Error("ResponseTooLarge", "CLI snapshots exceed limit");
                if (snapshot.rfind(previous, 0) == 0)
                    text = snapshot.substr(previous.size());
                else
                    context.emit("TextSnapshot", {{"id", key}, {"text", snapshot}});
                previous = std::move(snapshot);
            }
        } else if (buddy && kind == "user") {
            for (const auto& block : data.at("message").at("content"))
                if (block.is_object() && block.value("type", std::string()) == "tool_result")
                    context.emit("ToolCompleted", block);
        } else if (buddy && kind == "stream_event") {
            // Some CLI versions emit raw partial messages in addition to assistant snapshots.
            context.emit("ProviderEvent", data);
        } else if (buddy && kind == "system" && data.contains("usage")) {
            context.emit("UsageUpdated", data.at("usage"));
        } else if (agy && kind == "step_update") {
            text = payload.value("text_delta", std::string());
            if (payload.contains("tool_info"))
                context.emit(payload.value("state", std::string()) == "DONE" ? "ToolCompleted"
                                                                             : "ToolStarted",
                             payload.at("tool_info"));
            if (payload.contains("usage"))
                context.emit("UsageUpdated", payload.at("usage"));
        } else if (options_.protocol == AgentCliProtocol::OpenCode &&
                   (kind == "text" || kind == "reasoning")) {
            auto part = data.value("part", Json::object());
            if (kind == "text")
                text = part.value("text", std::string());
            else
                context.emit("ThinkingDelta", {{"text", part.value("text", std::string())}});
        } else if (options_.protocol == AgentCliProtocol::OpenCode && kind == "tool_use") {
            auto part = data.value("part", Json::object());
            auto status = part.value("state", Json::object()).value("status", std::string());
            context.emit(
                status == "completed" || status == "error" ? "ToolCompleted" : "ToolStarted", part);
        } else if (agy && kind == "result") {
            auto status = payload.value("status", std::string());
            if (status != "SUCCESS")
                throw Error("ProviderError", "Antigravity turn failed",
                            redact_error_message(status, process_error_secrets(options_.process)));
            result.text = payload.at("response").get<std::string>();
            result.usage = payload.value("usage", Json::object());
            completed = true;
        } else if (options_.protocol == AgentCliProtocol::OpenCode && kind == "step_finish") {
            auto part = data.value("part", Json::object());
            result.usage = part.value("tokens", Json::object());
            completed = part.value("reason", std::string()) == "stop";
        } else
            context.emit("ProviderEvent", data);
        if (!text.empty()) {
            result.text += text;
            if (result.text.size() > provider_message_limit)
                throw Error("ResponseTooLarge", "CLI answer exceeds limit");
            context.emit("TextDelta", {{"text", text}});
        }
        if (completed && options_.persistent)
            break;
    }
    if (!completed && !codex_error.empty())
        throw Error("ProviderError", codex_error);
    if ((!options_.persistent || !process.running()) && process.wait(context.cancellation) != 0)
        throw Error("ProcessCrash", "CLI exited with an error");
    if (!completed)
        throw Error("UnexpectedEOF", "CLI stream ended before its final result");
    if (session.id.empty())
        throw Error("ProtocolError", "Missing native CLI session ID");
    active_session_ = session.id;
    cleanup.keep = options_.persistent;
    result.finish_reason = "stop";
    messages.push_back({{"role", "assistant"}, {"content", result.text}});
    context.emit("UsageUpdated", result.usage);
    context.emit("TurnCompleted", {{"text", result.text}});
    return result;
}
void AgentCliProvider::disconnect() noexcept {
    {
        std::lock_guard lock(cancellation_mutex_);
        active_.cancel();
    }
    std::lock_guard lock(mutex_);
    process_.reset();
    active_session_.clear();
    connected_ = false;
}
} // namespace cymcp
