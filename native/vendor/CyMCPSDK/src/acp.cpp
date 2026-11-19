#include "provider_error.hpp"
#include <cymcp/ai.hpp>
#include <set>
namespace cymcp {
namespace {
void collect_models(const Json& options, Json& models) {
    if (!options.is_array())
        throw Error("ProtocolError", "Invalid ACP model options");
    for (const auto& item : options) {
        if (item.contains("options"))
            collect_models(item.at("options"), models);
        else {
            auto id = item.at("value").get<std::string>();
            if (id.empty())
                throw Error("ProtocolError", "Empty ACP model ID");
            models.push_back({{"id", id}, {"name", item.value("name", id)}});
        }
    }
}
Json model_rows(const Json& data) {
    auto entries = data.is_array() ? data : data.value("models", data.value("data", Json()));
    if (!entries.is_array())
        throw Error("ProtocolError", "Expected JSON model catalogue");
    Json result = Json::array();
    for (const auto& item : entries) {
        auto id = item.is_string()
                      ? item.get<std::string>()
                      : item.value("id", item.value("modelId", item.value("slug", std::string())));
        if (id.empty())
            throw Error("ProtocolError", "Missing model ID in catalogue");
        result.push_back({{"id", id}, {"name", item.is_string() ? id : item.value("name", id)}});
    }
    return result;
}
} // namespace
struct AcpProvider::Impl {
    AcpOptions options;
    std::unique_ptr<Process> process;
    mutable std::mutex mutex;
    std::mutex cancellation_mutex;
    Cancellation active;
    std::int64_t counter = 0;
    bool initialized = false, load_session = false;
    std::string session_id, system;
    bool first_prompt = true;
    Json config_options = Json::array(), models = Json::array(), legacy_models = Json::object();
    Json modes = Json::object();
    Json prompt_capabilities = Json::object();
    Session open_session(std::string model, std::string resume, std::string system_prompt,
                         const std::string& requested_mode, const CallContext& context);
    explicit Impl(AcpOptions config) : options(std::move(config)) {}
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
        initialized = load_session = false;
        prompt_capabilities = Json::object();
        session_id.clear();
        models = Json::array();
    }
    Json read(const CallContext& context) {
        ProcessLine line;
        while (process->next(line, context.cancellation)) {
            if (line.stderr_stream || line.text.empty())
                continue;
            auto message = parse_json(line.text, provider_message_limit);
            if (!message.is_object() || message.value("jsonrpc", std::string()) != "2.0")
                throw Error("ProtocolError", "Expected ACP JSON-RPC message");
            return message;
        }
        throw Error("UnexpectedEOF", "ACP process closed before its response");
    }
    void permission(const Json& message, const CallContext& context) {
        try {
            const auto& params = message.at("params");
            Json response = {{"jsonrpc", "2.0"}, {"id", message.at("id")}};
            if (params.value("sessionId", std::string()) != session_id || session_id.empty()) {
                response["error"] = {{"code", -32602}, {"message", "Unknown active session"}};
            } else {
                context.emit("ApprovalRequested", params);
                bool allow = options.approvals.allows(context, "acp.permission", params);
                auto choices = params.at("options");
                if (!choices.is_array() || choices.size() > 32)
                    throw Error("ProtocolError", "Invalid ACP permission choices");
                std::string choice;
                for (int i = 0; i < 2; ++i) {
                    const char* kind = allow ? (i == 0 ? "allow_once" : "allow_always")
                                             : (i == 0 ? "reject_once" : "reject_always");
                    for (const auto& option : choices)
                        if (option.value("kind", std::string()) == kind) {
                            choice = option.at("optionId").get<std::string>();
                            break;
                        }
                    if (!choice.empty())
                        break;
                }
                response["result"] = {{"outcome", choice.empty() ? Json{{"outcome", "cancelled"}}
                                                                 : Json{{"outcome", "selected"},
                                                                        {"optionId", choice}}}};
                context.emit("ApprovalResolved", {{"allowed", allow && !choice.empty()}});
            }
            process->write(response.dump() + "\n", context.cancellation);
        } catch (...) {
            if (context.cancellation.cancelled() || Clock::now() >= context.cancellation.deadline) {
                Cancellation cleanup;
                cleanup.deadline = Clock::now() + std::chrono::milliseconds(100);
                try {
                    process->write(Json({{"jsonrpc", "2.0"},
                                         {"id", message.at("id")},
                                         {"result", {{"outcome", {{"outcome", "cancelled"}}}}}})
                                           .dump() +
                                       "\n",
                                   cleanup);
                } catch (...) {
                }
            }
            throw;
        }
    }
    void metadata(const Json& state) {
        config_options = state.value("configOptions", config_options);
        legacy_models = state.value("models", legacy_models);
        modes = state.value("modes", modes);
        if (!config_options.is_array())
            throw Error("ProtocolError", "Invalid ACP configuration");
        models = Json::array();
        for (const auto& option : config_options)
            if (option.value("category", std::string()) == "model" &&
                option.value("type", std::string()) == "select")
                collect_models(option.at("options"), models);
        if (models.empty() && legacy_models.contains("availableModels"))
            models = model_rows(legacy_models.at("availableModels"));
    }
    void client_request(const Json& message, const CallContext& context) {
        auto method = message.at("method").get<std::string>();
        auto params = message.at("params");
        Json response = {{"jsonrpc", "2.0"}, {"id", message.at("id")}};
        auto fs = options.client_capabilities.value("fs", Json::object());
        bool supported = method == "fs/read_text_file" ? fs.value("readTextFile", false)
                         : method == "fs/write_text_file"
                             ? fs.value("writeTextFile", false)
                             : (method == "terminal/create" || method == "terminal/output" ||
                                method == "terminal/release" ||
                                method == "terminal/wait_for_exit" || method == "terminal/kill") &&
                                   options.client_capabilities.value("terminal", false);
        if (!supported || !options.request_handler)
            response["error"] = {{"code", -32601}, {"message", "Client capability not provided"}};
        else if (session_id.empty() || params.value("sessionId", std::string()) != session_id)
            response["error"] = {{"code", -32602},
                                 {"message", "Request belongs to another session"}};
        else {
            try {
                context.cancellation.check();
                if (!options.approvals.allows(context, method, params))
                    throw Error("AccessDenied", "Client service denied by host policy");
                auto result = options.request_handler(method, params, context);
                context.cancellation.check();
                if (!result.is_object() || result.dump().size() > provider_message_limit)
                    throw Error("InvalidResult", "Invalid ACP client service result");
                response["result"] = std::move(result);
            } catch (const Error& error) {
                if (error.code == "Cancelled" || error.code == "Timeout")
                    throw;
                response["error"] = {{"code", -32603},
                                     {"message", "Host client service failed or was denied"}};
            } catch (...) {
                response["error"] = {{"code", -32603}, {"message", "Host client service failed"}};
            }
        }
        process->write(response.dump() + "\n", context.cancellation);
    }
    void update(const Json& params, const CallContext& context, ChatResult* result) {
        if (params.value("sessionId", std::string()) != session_id || session_id.empty())
            return;
        auto value = params.at("update");
        auto kind = value.at("sessionUpdate").get<std::string>();
        if (kind == "config_option_update")
            metadata({{"configOptions", value.at("configOptions")}});
        // session/load replays history; it must never become the new turn's answer.
        if (!result)
            return;
        if (kind == "agent_message_chunk" || kind == "agent_thought_chunk") {
            auto content = value.at("content");
            if (content.value("type", std::string()) == "text") {
                auto text = content.at("text").get<std::string>();
                if (kind == "agent_message_chunk") {
                    result->text += text;
                    if (result->text.size() > provider_message_limit)
                        throw Error("ResponseTooLarge", "ACP answer exceeds limit");
                }
                context.emit(kind == "agent_message_chunk" ? "TextDelta" : "ThinkingDelta",
                             {{"text", text}});
            } else
                context.emit("ProviderEvent", value);
        } else if (kind == "tool_call" || kind == "tool_call_update") {
            auto status = value.value("status", std::string());
            context.emit(status == "completed" || status == "failed" ? "ToolCompleted"
                                                                     : "ToolStarted",
                         value);
        } else if (kind == "usage_update") {
            result->usage = value;
            context.emit("UsageUpdated", value);
        } else
            context.emit("ProviderEvent", value);
    }
    Json rpc(const std::string& method, Json params, const CallContext& context,
             ChatResult* result = nullptr) {
        auto id = ++counter;
        process->write(
            Json(
                {{"jsonrpc", "2.0"}, {"id", id}, {"method", method}, {"params", std::move(params)}})
                    .dump() +
                "\n",
            context.cancellation);
        for (;;) {
            auto message = read(context);
            if (message.contains("method")) {
                auto name = message.at("method").get<std::string>();
                if (message.contains("id")) {
                    if (name == "session/request_permission")
                        permission(message, context);
                    else
                        client_request(message, context);
                } else if (name == "session/update")
                    update(message.at("params"), context, result);
                continue;
            }
            if (!message.contains("id") || message.at("id") != id)
                throw Error("ProtocolError", "Unexpected ACP response ID");
            if (auto error = message.find("error"); error != message.end()) {
                const auto code = error->find("code");
                const char* category = "ProviderError";
                if (code != error->end()) {
                    if (*code == -32000)
                        category = "AuthenticationRequired";
                    else if (*code == -32601)
                        category = "Unsupported";
                }
                throw detail::structured_provider_error(*error, "code",
                                                        process_error_secrets(options.process),
                                                        category, "ACP agent rejected the request");
            }
            if (!message.contains("result") || !message.at("result").is_object())
                throw Error("ProtocolError", "Missing ACP response result");
            return message.at("result");
        }
    }
    std::string current_model() const {
        for (const auto& option : config_options)
            if (option.value("category", std::string()) == "model" &&
                option.value("type", std::string()) == "select")
                return option.value("currentValue", std::string());
        return legacy_models.value("currentModelId", std::string());
    }
    void select(const std::string& category, const std::string& value, const CallContext& context) {
        if (value.empty())
            return;
        for (const auto& option : config_options)
            if (option.value("category", std::string()) == category &&
                option.value("type", std::string()) == "select") {
                if (option.value("currentValue", std::string()) == value)
                    return;
                auto id = option.at("id");
                auto response =
                    rpc("session/set_config_option",
                        {{"sessionId", session_id}, {"configId", id}, {"value", value}}, context);
                metadata(response);
                bool confirmed = false;
                for (const auto& updated : config_options)
                    if (updated.at("id") == id &&
                        updated.value("currentValue", std::string()) == value)
                        confirmed = true;
                if (!confirmed)
                    throw Error("ProtocolError", "ACP selection was not confirmed");
                return;
            }
        if (category == "model" && legacy_models.contains("currentModelId")) {
            if (current_model() != value) {
                rpc("session/set_model", {{"sessionId", session_id}, {"modelId", value}}, context);
                legacy_models["currentModelId"] = value;
            }
        } else if (category == "mode" && modes.contains("currentModeId")) {
            if (modes.at("currentModeId") != value) {
                rpc("session/set_mode", {{"sessionId", session_id}, {"modeId", value}}, context);
                modes["currentModeId"] = value;
            }
        } else
            throw Error("Unsupported", "ACP agent does not expose the requested session selection");
    }
};
AcpProvider::AcpProvider(AcpOptions options)
    : Provider("acp"), impl_(std::make_unique<Impl>(std::move(options))) {
    const auto& opts = impl_->options;
    if (!opts.mcp_servers.is_array() || opts.mcp_servers.dump().size() > 1024 * 1024 ||
        opts.connect_timeout.count() <= 0 || opts.turn_timeout.count() < 0 ||
        opts.connect_timeout > std::chrono::hours(24) || opts.turn_timeout > std::chrono::hours(24))
        throw Error("InvalidOptions", "Invalid ACP options");
    Json boolean_schema = {{"type", "boolean"}};
    Json fs_schema = {
        {"type", "object"},
        {"additionalProperties", false},
        {"properties", {{"readTextFile", boolean_schema}, {"writeTextFile", boolean_schema}}}};
    Json capability_schema = {{"type", "object"},
                              {"additionalProperties", false},
                              {"properties", {{"terminal", boolean_schema}, {"fs", fs_schema}}}};
    try {
        validate(opts.client_capabilities, capability_schema);
    } catch (const Error&) {
        throw Error("InvalidOptions", "Invalid ACP client capabilities");
    }
    auto fs = opts.client_capabilities.value("fs", Json::object());
    if ((opts.client_capabilities.value("terminal", false) || fs.value("readTextFile", false) ||
         fs.value("writeTextFile", false)) &&
        !opts.request_handler)
        throw Error("InvalidOptions", "Advertised ACP client services require a host handler");
}
AcpProvider::~AcpProvider() {
    disconnect();
}
ProviderCapabilities AcpProvider::capabilities() const {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    bool image = p.prompt_capabilities.value("image", false),
         audio = p.prompt_capabilities.value("audio", false),
         embedded = p.prompt_capabilities.value("embeddedContext", false);
    return {true,
            p.load_session,
            true,
            !p.models.empty() || !p.options.models_process.executable.empty(),
            image || audio || embedded,
            image,
            audio,
            embedded,
            true};
}
void AcpProvider::connect(const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    context.cancellation.check();
    if (p.initialized)
        return;
    try {
        p.process = std::make_unique<Process>(p.options.process);
        Json client_capabilities = {{"fs", {{"readTextFile", false}, {"writeTextFile", false}}},
                                    {"terminal", false}};
        client_capabilities.merge_patch(p.options.client_capabilities);
        auto result = p.rpc("initialize",
                            {{"protocolVersion", 1},
                             {"clientCapabilities", client_capabilities},
                             {"clientInfo", {{"name", "cymcp"}, {"version", version}}}},
                            context);
        if (result.at("protocolVersion") != 1)
            throw Error("UnsupportedVersion", "Only ACP protocol version 1 is implemented");
        p.load_session =
            result.value("agentCapabilities", Json::object()).value("loadSession", false);
        p.prompt_capabilities = result.value("agentCapabilities", Json::object())
                                    .value("promptCapabilities", Json::object());
        p.initialized = true;
        context.emit("ProviderReady", {{"authentication", "unverified"}, {"protocol", "acp-v1"}});
    } catch (...) {
        p.reset();
        throw;
    }
}
Json AcpProvider::list_models(const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    // Some agents publish their catalogue only in session/new. Negotiate a session if the
    // caller just connected and no independent catalogue command is configured.
    if (p.models.empty() && p.initialized && p.session_id.empty() &&
        p.options.models_process.executable.empty())
        p.open_session({}, {}, {}, {}, context);
    if (!p.models.empty())
        return p.models;
    if (p.options.models_process.executable.empty())
        throw Error("Unsupported",
                    "ACP model catalogue is available only if advertised by a session");
    Process probe(p.options.models_process);
    probe.close_input();
    ProcessLine line;
    std::string output;
    while (probe.next(line, context.cancellation)) {
        if (line.stderr_stream)
            continue;
        output += line.text + "\n";
        if (output.size() > 1024 * 1024)
            throw Error("ResponseTooLarge", "Model catalogue exceeds limit");
    }
    if (probe.wait(context.cancellation) != 0)
        throw Error("ModelDiscoveryFailed", "Native model discovery failed");
    return model_rows(parse_json(output, provider_message_limit));
}
Session AcpProvider::Impl::open_session(std::string model, std::string resume,
                                        std::string system_prompt,
                                        const std::string& requested_mode,
                                        const CallContext& context) {
    if (!initialized)
        throw Error("InvalidState", "Connect ACP first");
    if (!resume.empty() && !load_session)
        throw Error("Unsupported", "Agent does not advertise session loading");
    auto cwd = options.process.cwd.empty() ? std::filesystem::current_path()
                                           : std::filesystem::absolute(options.process.cwd);
    Json params = {{"cwd", path_utf8(cwd)}, {"mcpServers", options.mcp_servers}};
    session_id = resume;
    models = config_options = Json::array();
    modes = legacy_models = Json::object();
    if (!resume.empty())
        params["sessionId"] = resume;
    try {
        auto result = rpc(resume.empty() ? "session/new" : "session/load", params, context);
        auto id = resume.empty() ? result.at("sessionId").get<std::string>() : resume;
        if (id.empty() || (result.contains("sessionId") && result.at("sessionId") != id))
            throw Error("SessionInvalid", "ACP returned a different session ID");
        session_id = id;
        metadata(result);
        select("mode", requested_mode, context);
        select("model", model, context);
        system = std::move(system_prompt);
        first_prompt = resume.empty();
        return {id, current_model()};
    } catch (const Error& error) {
        if (error.code == "Cancelled" || error.code == "Timeout" || error.code == "UnexpectedEOF" ||
            error.code == "ProtocolError")
            reset();
        else
            session_id.clear();
        throw;
    } catch (...) {
        reset();
        throw;
    }
}
Session AcpProvider::start_session(std::string model, std::string resume, const CallContext& input,
                                   std::string system) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    return p.open_session(std::move(model), std::move(resume), std::move(system), p.options.mode,
                          context);
}
ChatResult AcpProvider::send(Session& session, Json& messages, const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.turn_timeout);
    if (!p.initialized)
        throw Error("InvalidState", "Reconnect ACP after disconnect or interruption");
    if (session.id.empty() || session.id != p.session_id)
        throw Error("SessionInvalid", "ACP session is not active");
    if (!messages.is_array() || messages.empty())
        throw Error("InvalidParameters", "Messages required");
    auto prompt = detail::content_for(messages.back().at("content"), detail::ContentFormat::Acp);
    for (const auto& block : prompt) {
        auto type = block.at("type").get<std::string>();
        auto capability = type == "image" ? "image" : type == "audio" ? "audio" : "embeddedContext";
        if (type != "text" &&
            (type == "image_url" || !p.prompt_capabilities.value(capability, false)))
            throw Error("UnsupportedContent",
                        "ACP agent did not negotiate this prompt content type");
    }
    // ACP v1 has no portable system role. It is first-turn application context.
    if (p.first_prompt && !p.system.empty())
        prompt.insert(prompt.begin(),
                      text_block("Application instructions:\n" + p.system + "\nUser message:\n"));
    p.first_prompt = false; // submitted turns must never be resent implicitly
    ChatResult result;
    bool answered = false; // the correlated session/prompt response was received
    try {
        auto response = p.rpc("session/prompt", {{"sessionId", session.id}, {"prompt", prompt}},
                              context, &result);
        answered = true;
        auto reason = response.at("stopReason").get<std::string>();
        if (reason == "cancelled")
            throw Error("Cancelled", "ACP agent cancelled the turn");
        if (reason == "max_turn_requests" || reason == "max_tokens")
            throw Error("TurnLimitExceeded", "ACP agent stopped before completing its turn");
        if (reason == "refusal")
            throw Error("ProviderRefusal", "ACP agent refused the request");
        if (reason != "end_turn")
            throw Error("ProtocolError", "Unknown ACP stop reason");
        if (response.contains("usage"))
            result.usage = response.at("usage");
        result.finish_reason = reason;
        messages.push_back({{"role", "assistant"}, {"content", result.text}});
        if (!p.current_model().empty())
            session.model = p.current_model();
        context.emit("TurnCompleted", {{"text", result.text}, {"finishReason", reason}});
        return result;
    } catch (const Error&) {
        // Any failure before the agent's answer (cancellation, timeout, overflow, a closed or
        // misbehaving process...) leaves its prompt running: cancel it and drop the process so
        // a later turn never reads stale updates. A turn that answered left the session clean.
        if (!answered) {
            CallContext cleanup;
            cleanup.cancellation.deadline = Clock::now() + std::chrono::milliseconds(100);
            try {
                if (p.process)
                    p.process->write(Json({{"jsonrpc", "2.0"},
                                       {"method", "session/cancel"},
                                       {"params", {{"sessionId", session.id}}}})
                                         .dump() +
                                     "\n",
                                 cleanup.cancellation);
            } catch (...) {
            }
            p.reset();
        }
        throw;
    } catch (...) {
        p.reset();
        throw;
    }
}
void AcpProvider::disconnect() noexcept {
    auto& p = *impl_;
    {
        std::lock_guard lock(p.cancellation_mutex);
        p.active.cancel();
    }
    std::lock_guard lock(p.mutex);
    p.reset();
}
} // namespace cymcp
