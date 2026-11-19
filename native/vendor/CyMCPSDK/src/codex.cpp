#include "provider_error.hpp"
#include <cymcp/ai.hpp>
#include <cymcp/resources.hpp>
#include <set>
namespace cymcp {
namespace {
// A rejected RPC or a terminal provider turn leaves the transport usable.
struct CodexRejection final : Error {
    using Error::Error;
};
} // namespace
struct CodexProvider::Impl {
    CodexOptions options;
    std::unique_ptr<Process> process;
    std::mutex mutex;
    std::mutex cancellation_mutex;
    Cancellation active;
    std::int64_t counter = 0;
    std::deque<Json> pending;
    std::size_t pending_bytes = 0;
    std::map<std::string, std::string> tool_names;
    std::string active_session;
    explicit Impl(CodexOptions config) : options(std::move(config)) {}
    [[noreturn]] void reject(const Json& error, const CallContext& context, std::string fallback,
                             bool interrupted = false) const {
        const auto info = error.find("codexErrorInfo");
        std::string category;
        int status = 0;
        if (info != error.end()) {
            if (info->is_string())
                category = info->get<std::string>();
            else if (info->is_object() && info->size() == 1) {
                category = info->begin().key();
                const auto& details = info->begin().value();
                const auto http = details.find("httpStatusCode");
                if (http != details.end() && http->is_number_integer() && *http >= 100 &&
                    *http <= 599)
                    status = http->get<int>();
            }
        }
        static const std::set<std::string_view> native_codes = {"contextWindowExceeded",
                                                                "sessionBudgetExceeded",
                                                                "usageLimitExceeded",
                                                                "rateLimitExceeded",
                                                                "serverOverloaded",
                                                                "cyberPolicy",
                                                                "misalignmentPolicyViolation",
                                                                "internalServerError",
                                                                "unauthorized",
                                                                "badRequest",
                                                                "threadRollbackFailed",
                                                                "sandboxError",
                                                                "other",
                                                                "httpConnectionFailed",
                                                                "responseStreamConnectionFailed",
                                                                "responseStreamDisconnected",
                                                                "responseTooManyFailedAttempts",
                                                                "activeTurnNotSteerable"};
        if (!native_codes.contains(category))
            category.clear();
        const auto secrets = process_error_secrets(options.process);
        std::string upstream_code;
        const auto explanation = [&](const char* name) -> std::string {
            const auto field = error.find(name);
            if (field == error.end() || !field->is_string())
                return {};
            const auto& text = field->get_ref<const std::string&>();
            const auto start = text.find_first_not_of(" \r\n\t");
            if (start == text.npos)
                return {};
            if (text[start] != '{')
                return redact_error_message(text, secrets);
            // Some App Server builds stringify the upstream error object in TurnError.message.
            const auto body = detail::parse_provider_error_body(text);
            if (!body.is_object() || body.value("type", Json()) != "error")
                return {};
            const auto reason = body.find("error");
            if (reason == body.end() || !reason->is_object())
                return {};
            const auto http = body.find("status");
            if (!status && http != body.end() && http->is_number_integer() && *http >= 100 &&
                *http <= 599)
                status = http->get<int>();
            const auto decoded =
                detail::structured_provider_error(*reason, "code", secrets, "", "");
            upstream_code = decoded.provider_code;
            return decoded.what();
        };
        auto message = explanation("message");
        if (message.empty())
            message = explanation("additionalDetails");
        const std::string code =
            interrupted                          ? "Cancelled"
            : upstream_code == "model_not_found" ? "UnknownModel"
            : category == "unauthorized" || status == 401 || status == 403
                ? "AuthenticationRequired"
            : category == "rateLimitExceeded" || category == "usageLimitExceeded" || status == 429
                ? "RateLimited"
            : category == "contextWindowExceeded" ? "ContextTooLarge"
                                                  : "ProviderError";
        if (message.empty()) {
            message = std::move(fallback);
            if (!category.empty())
                message += ": " + category;
            if (status)
                message += " (HTTP " + std::to_string(status) + ")";
        }
        auto provider_code = upstream_code.empty() ? category : upstream_code;
        if (!interrupted) {
            Json event = {{"code", code},
                          {"message", message},
                          {"native_code", category.empty() ? "other" : category},
                          {"http_status", status}};
            const auto rpc_code = error.find("code");
            if (rpc_code != error.end() && rpc_code->is_number_integer()) {
                event["rpc_code"] = *rpc_code;
                if (upstream_code.empty())
                    provider_code = rpc_code->dump();
            }
            context.emit("ProviderError", std::move(event));
        }
        throw CodexRejection(code, std::move(message), std::move(provider_code));
    }
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
    Json read(const Cancellation& cancel) {
        ProcessLine line;
        while (process->next(line, cancel)) {
            if (line.stderr_stream)
                continue;
            try {
                auto value = parse_json(line.text, provider_message_limit);
                if (!value.is_object())
                    throw Error("ProtocolError", "Expected provider message object");
                return value;
            } catch (const Json::exception&) {
                throw Error("MalformedJSON", "Invalid provider protocol JSON");
            }
        }
        throw Error("UnexpectedEOF", "Provider process closed its stream");
    }
    void server_request(const Json& request, const CallContext& context) {
        auto method = request.at("method").get<std::string>();
        auto params = request.value("params", Json::object());
        Json response = {{"id", request["id"]}};
        if (method == "item/tool/call" && options.tools) {
            bool success = true;
            Json output;
            try {
                context.cancellation.check();
                if (active_session.empty() ||
                    params.value("threadId", std::string()) != active_session)
                    throw Error("SessionInvalid", "Tool call belongs to another session");
                auto requested = params.at("tool").get<std::string>();
                auto found = tool_names.find(requested);
                if (found == tool_names.end())
                    throw Error("UnknownOperation", "Provider requested an unregistered tool");
                output = options.tools->invoke(found->second, params.at("arguments"), context);
            } catch (const Error& e) {
                success = false;
                output = {{"code", e.code}, {"message", e.what()}};
            } catch (...) {
                success = false;
                output = {{"code", "InvalidArguments"}};
            }
            response["result"] = {
                {"contentItems", Json::array({{{"type", "inputText"}, {"text", output.dump()}}})},
                {"success", success}};
        } else if (options.request_handler)
            response["result"] = options.request_handler(method, params, context);
        else if (method == "item/commandExecution/requestApproval" ||
                 method == "item/fileChange/requestApproval")
            response["result"] = {{"decision", options.approvals.allows(context, method, params)
                                                   ? "accept"
                                                   : "decline"}};
        else if (method == "item/permissions/requestApproval")
            response["result"] = {{"permissions", options.approvals.allows(context, method, params)
                                                      ? params.value("permissions", Json::object())
                                                      : Json::object()},
                                  {"scope", "turn"}};
        else
            response["error"] = {{"code", -32601},
                                 {"message", "Host did not implement this provider request"}};
        process->write(response.dump() + "\n", context.cancellation);
    }
    Json rpc(const std::string& method, Json params, const CallContext& context) {
        auto id = ++counter;
        process->write(
            Json({{"id", id}, {"method", method}, {"params", std::move(params)}}).dump() + "\n",
            context.cancellation);
        while (true) {
            auto value = read(context.cancellation);
            if (value.contains("method") && value.contains("id")) {
                server_request(value, context);
                continue;
            }
            if (value.value("id", Json()) == id) {
                if (value.contains("error"))
                    reject(value.at("error"), context, "Codex RPC rejected: " + method);
                if (!value.contains("result"))
                    throw Error("ProtocolError", "Missing provider RPC result");
                return value["result"];
            }
            auto bytes = value.dump().size();
            if (pending.size() >= 1000000 || pending_bytes + bytes > provider_message_limit)
                throw Error("OutputOverflow", "Provider event backlog exceeds limit");
            pending_bytes += bytes;
            pending.push_back(std::move(value));
        }
    }
    Json next(const Cancellation& cancel) {
        if (pending.empty())
            return read(cancel);
        auto value = std::move(pending.front());
        pending.pop_front();
        pending_bytes -= value.dump().size();
        return value;
    }
};
CodexProvider::CodexProvider(CodexOptions options)
    : Provider("codex"), impl_(std::make_unique<Impl>(std::move(options))) {
    impl_->options.tools =
        compact_tools(std::move(impl_->options.tools), impl_->options.tool_discovery_threshold);
}
CodexProvider::~CodexProvider() = default;
ProviderCapabilities CodexProvider::capabilities() const {
    return {true, true, static_cast<bool>(impl_->options.tools), true, true, true, false, false,
            true, true};
}
void CodexProvider::connect(const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    if (p.process)
        throw Error("InvalidState", "Codex already connected");
    auto config = p.options.process;
    config.arguments.insert(config.arguments.end(), {"app-server", "--listen", "stdio://"});
    p.pending.clear();
    p.pending_bytes = 0;
    try {
        p.process = std::make_unique<Process>(config);
        context.emit("ProcessStarted", {{"transport", "codex-app-server"}});
        context.emit("ConnectionStage", {{"code", "ProtocolInit"}});
        p.rpc("initialize",
              {{"clientInfo", {{"name", "cymcp_sdk"}, {"version", version}}},
               {"capabilities", {{"experimentalApi", static_cast<bool>(p.options.tools)}}}},
              context);
        p.process->write("{\"method\":\"initialized\",\"params\":{}}\n");
        context.emit("ConnectionStage", {{"code", "AccountCheck"}});
        auto account = p.rpc("account/read", {{"refreshToken", false}}, context);
        if (!account.contains("account") || account["account"].is_null())
            throw Error("AuthenticationRequired", "Sign in using the provider's native login flow");
    } catch (...) {
        p.process.reset();
        throw;
    }
}
Json CodexProvider::list_models(const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    if (!p.process)
        throw Error("InvalidState", "Connect Codex first");
    Json models = Json::array(), params = Json::object();
    std::set<std::string> seen;
    for (int page = 0; page < 1000; ++page) {
        auto result = p.rpc("model/list", params, context);
        for (const auto& model : result.at("data")) {
            models.push_back(model);
            if (models.size() > 10000)
                throw Error("ResponseTooLarge", "Model catalogue exceeds limit");
        }
        auto cursor = result.value("nextCursor", Json());
        if (cursor.is_null() || cursor == "")
            return models;
        auto text = cursor.get<std::string>();
        if (!seen.insert(text).second)
            throw Error("ProtocolError", "Cyclic model pagination");
        params["cursor"] = text;
    }
    throw Error("ResponseTooLarge", "Too many model pages");
}
Session CodexProvider::start_session(std::string model, std::string resume,
                                     const CallContext& input, std::string system) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.connect_timeout);
    if (!p.process)
        throw Error("InvalidState", "Connect Codex first");
    Json params = {{"approvalPolicy", p.options.approval_policy}, {"sandbox", p.options.sandbox}};
    if (!p.options.process.cwd.empty())
        params["cwd"] = path_utf8(p.options.process.cwd);
    if (!model.empty())
        params["model"] = model;
    if (!resume.empty())
        params["threadId"] = resume;
    if (!system.empty())
        params["developerInstructions"] = std::move(system);
    if (p.options.tools) {
        Json tools = Json::array();
        p.tool_names.clear();
        for (const auto& tool : p.options.tools->describe()) {
            auto original = tool["name"].get<std::string>();
            auto name = original;
            if (name.size() > 64 ||
                name.find_first_not_of(
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-") !=
                    std::string::npos)
                name = "cy_" + sha256(original).substr(0, 32);
            p.tool_names.emplace(name, original);
            tools.push_back(
                {{"type", "function"},
                 {"name", name},
                 {"description", "[" + original + "] " + tool["description"].get<std::string>()},
                 {"inputSchema", tool["inputSchema"]}});
        }
        if (resume.empty())
            params["dynamicTools"] = std::move(tools);
    }
    auto result = p.rpc(resume.empty() ? "thread/start" : "thread/resume", params, context);
    auto native = result.at("thread").at("id").get<std::string>();
    if (native.empty() || (!resume.empty() && native != resume))
        throw Error("SessionInvalid", "Provider returned a different session ID");
    auto actual_model = result.value("model", model);
    return {native, actual_model};
}
ChatResult CodexProvider::send(Session& session, Json& messages, const CallContext& input) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    auto context = p.begin(input, p.options.turn_timeout);
    if (!p.process || session.id.empty())
        throw Error("InvalidState", "Connect and start a Codex session first");
    if (messages.empty())
        throw Error("InvalidParameters", "Messages required");
    auto content = detail::content_for(messages.back().at("content"), detail::ContentFormat::Codex);
    std::string turn_id;
    p.active_session = session.id;
    struct SessionCleanup {
        Impl& p;
        ~SessionCleanup() {
            p.active_session.clear();
        }
    } session_cleanup{p};
    ChatResult result;
    std::string streamed;
    std::vector<std::string> order;
    std::map<std::string, std::string> final_items;
    try {
        Json turn_params = {{"threadId", session.id},
                            {"input", content},
                            {"approvalPolicy", p.options.approval_policy}};
        if (!session.model.empty())
            turn_params["model"] = session.model;
        auto turn = p.rpc("turn/start", std::move(turn_params), context);
        const auto& started = turn.at("turn");
        turn_id = started.at("id").get<std::string>();
        const auto initial_status = started.value("status", std::string());
        if (initial_status == "failed" || initial_status == "interrupted")
            p.reject(started.value("error", Json()), context, "Codex turn " + initial_status,
                     initial_status == "interrupted");
        while (true) {
            context.cancellation.check();
            auto value = p.next(context.cancellation);
            if (value.contains("method") && value.contains("id")) {
                p.server_request(value, context);
                continue;
            }
            auto method = value.value("method", std::string());
            auto params = value.value("params", Json::object());
            if (params.contains("threadId") && params["threadId"] != session.id)
                continue;
            if (params.contains("turnId") && params["turnId"] != turn_id)
                continue;
            if (method == "item/agentMessage/delta") {
                auto text = params.at("delta").get<std::string>();
                streamed += text;
                context.emit("TextDelta", {{"text", text}});
            } else if (method == "item/reasoning/textDelta" ||
                       method == "item/reasoning/summaryTextDelta")
                context.emit("ThinkingDelta", {{"text", params.value("delta", std::string())}});
            else if (method == "item/started" || method == "item/completed") {
                auto item = params.at("item");
                bool completed = method == "item/completed";
                auto type = item.value("type", std::string());
                if (completed && type == "agentMessage") {
                    auto id = item.at("id").get<std::string>();
                    if (!final_items.count(id))
                        order.push_back(id);
                    final_items[id] = item.value("text", std::string());
                } else if (type == "commandExecution" || type == "fileChange" ||
                           type == "mcpToolCall" || type == "dynamicToolCall")
                    context.emit(completed ? "ToolCompleted" : "ToolStarted", item);
            } else if (method == "thread/tokenUsage/updated") {
                result.usage = params.value("tokenUsage", Json::object());
                context.emit("UsageUpdated", result.usage);
            } else if (method == "turn/completed" && params.at("turn").at("id") == turn_id) {
                const auto& final = params.at("turn");
                const auto status = final.value("status", std::string());
                if (status != "completed")
                    p.reject(final.value("error", Json()), context,
                             status == "interrupted" ? "Codex turn interrupted"
                                                     : "Codex turn failed",
                             status == "interrupted");
                for (const auto& id : order)
                    result.text += final_items[id];
                if (order.empty())
                    result.text = streamed;
                result.finish_reason = "stop";
                messages.push_back({{"role", "assistant"}, {"content", result.text}});
                context.emit("TurnCompleted", {{"text", result.text}});
                return result;
            } else if (method == "error" && !params.value("willRetry", true))
                p.reject(params.value("error", Json()), context, "Codex turn failed");
        }
    } catch (const CodexRejection&) {
        // Some servers return complete items without deltas, including before an error.
        // Forward only the unseen suffix before Conversation closes the failed turn.
        std::string completed_text;
        for (const auto& id : order)
            completed_text += final_items[id];
        if (completed_text.size() > streamed.size() && completed_text.starts_with(streamed))
            context.emit("TextDelta", {{"text", completed_text.substr(streamed.size())}});
        throw;
    } catch (const Error& e) {
        if ((e.code == "Cancelled" || e.code == "Timeout") && !turn_id.empty()) {
            try {
                Cancellation cleanup;
                cleanup.deadline = Clock::now() + std::chrono::seconds(2);
                p.process->write(Json({{"id", ++p.counter},
                                       {"method", "turn/interrupt"},
                                       {"params", {{"threadId", session.id}, {"turnId", turn_id}}}})
                                         .dump() +
                                     "\n",
                                 cleanup);
                while (true) {
                    auto v = p.next(cleanup);
                    if (v.contains("method") && v.contains("id")) {
                        p.process->write(
                            Json({{"id", v["id"]},
                                  {"error",
                                   {{"code", -32603}, {"message", "Turn cancelled by host"}}}})
                                .dump() +
                            "\n");
                        continue;
                    }
                    if (v.value("method", std::string()) == "turn/completed" &&
                        v["params"]["turn"]["id"] == turn_id)
                        break;
                }
            } catch (...) {
                p.process.reset();
            }
        } else
            p.process.reset();
        throw;
    } catch (...) {
        p.process.reset();
        throw;
    }
}
void CodexProvider::disconnect() noexcept {
    {
        std::lock_guard lock(impl_->cancellation_mutex);
        impl_->active.cancel();
    }
    std::lock_guard lock(impl_->mutex);
    impl_->process.reset();
    impl_->pending.clear();
    impl_->pending_bytes = 0;
}
} // namespace cymcp
