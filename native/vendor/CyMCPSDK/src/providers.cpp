#include <cymcp/ai.hpp>
#include <limits>
namespace cymcp {
ProviderDiscoveryHints builtin_discovery(const std::string&);
namespace {
std::size_t tool_threshold(const Json& settings) {
    if (!settings.value("deferred_tools", true))
        return std::numeric_limits<std::size_t>::max();
    const auto value = settings.value("tool_discovery_threshold", 32LL);
    if (value < 0)
        throw Error("InvalidOptions", "Tool discovery threshold must be nonnegative");
    return static_cast<std::size_t>(value);
}
std::chrono::milliseconds duration(const Json& settings, const char* key, long long fallback) {
    auto value = settings.value(key, fallback);
    if (value < 1 || value > 86400000)
        throw Error("InvalidOptions", "Provider timeout must be between 1 ms and 24 h");
    return std::chrono::milliseconds(value);
}
// Optional budgets: 0 (the default) means unlimited; negative values are rejected.
std::size_t count(const Json& settings, const char* key) {
    auto value = settings.value(key, 0LL);
    if (value < 0)
        throw Error("InvalidOptions", "Budget must be 0 (unlimited) or positive");
    return static_cast<std::size_t>(value);
}
// Whole-turn deadlines are optional: 0 (the default) lets an agent work until it finishes or
// the host cancels it.
std::chrono::milliseconds optional_duration(const Json& settings, const char* key) {
    auto value = settings.value(key, 0LL);
    if (value < 0 || value > 365LL * 86400000)
        throw Error("InvalidOptions", "Turn timeout must be 0 (none) or a positive duration");
    return std::chrono::milliseconds(value);
}
ProcessOptions process_for(const ProviderConfig& config, const char* name) {
    return resolve_provider_process(builtin_discovery(name), config.process);
}
void no_host_tools(const ProviderConfig& config) {
    if (config.tools)
        throw Error(
            "Unsupported",
            "This CLI uses native/MCP tools; configure its native tool connection explicitly");
}
// Conventional settings of each built-in profile. The factories keep identical fallbacks so a
// typed caller without the registry gets the same behaviour. Policy-dependent values (Codex
// sandbox, Devin permission flags) are derived from allow_all_actions and are not listed.
Json builtin_settings(const std::string& id) {
    const bool api = id == "openai" || id == "anthropic" || id == "openrouter" || id == "ollama";
    const bool compatible = id == "openai-compatible" || id == "llama.cpp" || id == "lmstudio";
    if (api || compatible) {
        // Agent conventions: unlimited tool rounds and output (the model maximum), no turn
        // deadline; timeout_ms only detects a silent connection.
        Json settings = {{"timeout_ms", 600000},   {"turn_timeout_ms", 0},
                         {"max_tool_rounds", 0},   {"max_output_tokens", 0},
                         {"deferred_tools", true}, {"tool_discovery_threshold", 32}};
        if (id == "openai")
            settings["base_url"] = "https://api.openai.com/v1";
        else if (id == "anthropic")
            settings["base_url"] = "https://api.anthropic.com/v1";
        else if (id == "openrouter")
            settings["base_url"] = "https://openrouter.ai/api/v1";
        else if (id == "ollama")
            settings["base_url"] = "http://127.0.0.1:11434";
        else if (id == "llama.cpp")
            settings["base_url"] = "http://127.0.0.1:8080/v1";
        else if (id == "lmstudio")
            settings["base_url"] = "http://127.0.0.1:1234/v1";
        return settings;
    }
    Json settings = {{"connect_timeout_ms", id == "codex" ? 30000 : 15000},
                     {"turn_timeout_ms", 0},
                     {"allow_all_actions", false}};
    if (id == "codex") {
        settings["deferred_tools"] = true;
        settings["tool_discovery_threshold"] = 32;
        settings["approval_policy"] = "never";
    } else if (id == "codex-exec") {
        return settings;
    } else if (id == "claude-code") {
        settings["persistent"] = true;
        settings["setting_sources"] = "user";
        settings["allowed_tools"] = Json::array();
        settings["mcp_servers"] = Json::object();
    } else if (id == "antigravity" || id == "opencode-cli" || id == "command-code" ||
               id == "codebuddy") {
        settings["persistent"] = id == "antigravity";
        settings["native_settings"] = Json::object();
        if (id == "codebuddy")
            settings["allowed_tools"] = Json::array();
    } else {
        // ACP profiles.
        settings["mcp_servers"] = Json::array();
        if (id == "devin")
            settings["models_arguments"] = {"models", "list", "--format", "json"};
        else if (id == "gemini" || id == "qwen-code" || id == "codebuddy-acp")
            settings["acp_arguments"] = {"--acp"};
        else if (id != "acp")
            settings["acp_arguments"] = {"acp"};
    }
    return settings;
}
} // namespace
ProviderRegistry::ProviderRegistry(bool builtins) {
    if (!builtins)
        return;
    auto add_builtin = [this](ProviderDescriptor descriptor, ProviderFactory factory) {
        descriptor.discovery = builtin_discovery(descriptor.id);
        descriptor.default_settings = builtin_settings(descriptor.id);
        add(std::move(descriptor), std::move(factory));
    };
    add_builtin({"codex",
                 "Codex",
                 "app-server-stdio",
                 "native-account",
                 {true, true, true, true, true, true, false, false, true}},
                [](const ProviderConfig& config) {
                    CodexOptions options;
                    options.process = process_for(config, "codex");
                    options.connect_timeout =
                        duration(config.settings, "connect_timeout_ms", 30000);
                    options.turn_timeout = optional_duration(config.settings, "turn_timeout_ms");
                    bool allow = config.settings.value("allow_all_actions", false);
                    options.sandbox = config.settings.value(
                        "sandbox", std::string(allow ? "danger-full-access" : "read-only"));
                    options.approval_policy =
                        config.settings.value("approval_policy", std::string("never"));
                    options.tools = config.tools;
                    options.approvals = allow ? Policy::allow_all() : config.approvals;
                    options.tool_discovery_threshold = tool_threshold(config.settings);
                    options.request_handler = config.request_handler;
                    return std::make_shared<CodexProvider>(std::move(options));
                });
    add_builtin({"codex-exec",
                 "Codex (exec)",
                 "cli-exec-json",
                 "native-account",
                 {true, true, true, false, false}},
                [](const ProviderConfig& config) {
                    no_host_tools(config);
                    AgentCliOptions options;
                    options.protocol = AgentCliProtocol::CodexExec;
                    options.process = process_for(config, "codex");
                    options.connect_timeout =
                        duration(config.settings, "connect_timeout_ms", 15000);
                    options.turn_timeout = optional_duration(config.settings, "turn_timeout_ms");
                    options.allow_all_actions = config.settings.value("allow_all_actions", false);
                    return std::make_shared<AgentCliProvider>(std::move(options));
                });
    add_builtin({"claude-code",
                 "Claude Code",
                 "cli-stream-json",
                 "native-account",
                 {true, true, true, false, true, true, false, true, true}},
                [](const ProviderConfig& config) {
                    no_host_tools(config);
                    ClaudeOptions options;
                    options.process = process_for(config, "claude");
                    options.connect_timeout =
                        duration(config.settings, "connect_timeout_ms", 15000);
                    options.turn_timeout = optional_duration(config.settings, "turn_timeout_ms");
                    options.mcp_servers = config.settings.value("mcp_servers", Json::object());
                    options.allowed_tools =
                        config.settings.value("allowed_tools", std::vector<std::string>());
                    options.allow_all_actions = config.settings.value("allow_all_actions", false);
                    options.persistent = config.settings.value("persistent", true);
                    options.setting_sources =
                        config.settings.value("setting_sources", std::string("user"));
                    return std::make_shared<ClaudeProvider>(std::move(options));
                });
    for (auto protocol : {AgentCliProtocol::OpenCode, AgentCliProtocol::Antigravity,
                          AgentCliProtocol::CommandCode, AgentCliProtocol::CodeBuddy}) {
        bool agy = protocol == AgentCliProtocol::Antigravity;
        bool command = protocol == AgentCliProtocol::CommandCode;
        bool buddy = protocol == AgentCliProtocol::CodeBuddy;
        std::string id = agy       ? "antigravity"
                         : command ? "command-code"
                         : buddy   ? "codebuddy"
                                   : "opencode-cli";
        add_builtin(
            {id,
             agy       ? "Antigravity"
             : command ? "Command Code"
             : buddy   ? "CodeBuddy (Tencent)"
                       : "OpenCode",
             agy       ? "agy-stream-json"
             : command ? "cli-agent-events-jsonl"
             : buddy   ? "cli-stream-json"
                       : "cli-jsonl-v1",
             "native-account-unverified-until-generation",
             {true, true, true, !buddy, false, false, false, false, agy}},
            [protocol, agy, command, buddy](const ProviderConfig& config) {
                no_host_tools(config);
                AgentCliOptions options;
                options.protocol = protocol;
                options.process = process_for(config, agy       ? "agy"
                                                      : command ? "command-code"
                                                      : buddy   ? "codebuddy"
                                                                : "opencode");
                options.connect_timeout = duration(config.settings, "connect_timeout_ms", 15000);
                options.turn_timeout = optional_duration(config.settings, "turn_timeout_ms");
                options.allow_all_actions = config.settings.value("allow_all_actions", false);
                options.persistent = config.settings.value("persistent", agy);
                options.native_settings = config.settings.value("native_settings", Json::object());
                auto mcp_file = config.settings.value("mcp_config_file", std::string());
                options.mcp_config_file =
                    std::filesystem::path(std::u8string(mcp_file.begin(), mcp_file.end()));
                options.allowed_tools =
                    config.settings.value("allowed_tools", std::vector<std::string>());
                return std::make_shared<AgentCliProvider>(std::move(options));
            });
    }
    for (const std::string id : {"devin", "acp", "opencode", "gemini", "qwen-code", "kimi", "goose",
                                 "command-code-acp", "codebuddy-acp"}) {
        add_builtin(
            {id,
             id == "devin"              ? "Devin (Cognition)"
             : id == "opencode"         ? "OpenCode (ACP)"
             : id == "gemini"           ? "Gemini CLI"
             : id == "qwen-code"        ? "Qwen Code"
             : id == "kimi"             ? "Kimi Code"
             : id == "goose"            ? "goose"
             : id == "command-code-acp" ? "Command Code (ACP)"
             : id == "codebuddy-acp"    ? "CodeBuddy (ACP)"
                                        : "Agent Client Protocol",
             "acp-v1-stdio",
             "native-account-unverified-until-session",
             {true, true, true, true, true, true, true, true, true}},
            [id](const ProviderConfig& config) {
                no_host_tools(config);
                AcpOptions options;
                auto process = process_for(config, id == "acp"                ? ""
                                                   : id == "qwen-code"        ? "qwen"
                                                   : id == "command-code-acp" ? "command-code"
                                                   : id == "codebuddy-acp"    ? "codebuddy"
                                                                              : id.c_str());
                options.process = process;
                options.connect_timeout = duration(config.settings, "connect_timeout_ms", 15000);
                options.turn_timeout = optional_duration(config.settings, "turn_timeout_ms");
                bool allow = config.settings.value("allow_all_actions", false);
                options.approvals = allow ? Policy::allow_all() : config.approvals;
                options.mode = config.settings.value("mode", std::string());
                options.mcp_servers = config.settings.value("mcp_servers", Json::array());
                options.client_capabilities =
                    config.settings.value("client_capabilities", Json::object());
                options.request_handler = config.request_handler;
                std::vector<std::string> arguments;
                if (id == "devin") {
                    options.process.arguments.insert(
                        options.process.arguments.end(),
                        {"--permission-mode", allow ? "dangerous" : "normal",
                         "--respect-workspace-trust", allow ? "false" : "true"});
                    arguments = {"acp"};
                } else {
                    auto defaults = id == "acp" ? std::vector<std::string>()
                                    : id == "gemini" || id == "qwen-code" || id == "codebuddy-acp"
                                        ? std::vector<std::string>{"--acp"}
                                        : std::vector<std::string>{"acp"};
                    arguments = config.settings.value("acp_arguments", defaults);
                }
                options.process.arguments.insert(options.process.arguments.end(), arguments.begin(),
                                                 arguments.end());
                auto models = config.settings.value(
                    "models_arguments",
                    id == "devin" ? std::vector<std::string>{"models", "list", "--format", "json"}
                                  : std::vector<std::string>());
                if (!models.empty()) {
                    options.models_process = process;
                    options.models_process.arguments.insert(options.models_process.arguments.end(),
                                                            models.begin(), models.end());
                }
                return std::make_shared<AcpProvider>(std::move(options));
            });
    }
    for (auto protocol : {ApiProtocol::OpenAIResponses, ApiProtocol::AnthropicMessages,
                          ApiProtocol::OpenRouter, ApiProtocol::Ollama}) {
        const char* id = protocol == ApiProtocol::OpenAIResponses     ? "openai"
                         : protocol == ApiProtocol::AnthropicMessages ? "anthropic"
                         : protocol == ApiProtocol::OpenRouter        ? "openrouter"
                                                                      : "ollama";
        const char* name = protocol == ApiProtocol::OpenAIResponses     ? "OpenAI API"
                           : protocol == ApiProtocol::AnthropicMessages ? "Claude API"
                           : protocol == ApiProtocol::OpenRouter        ? "OpenRouter API"
                                                                        : "Ollama";
        const char* transport = protocol == ApiProtocol::OpenAIResponses     ? "responses-sse"
                                : protocol == ApiProtocol::AnthropicMessages ? "messages-sse"
                                : protocol == ApiProtocol::OpenRouter ? "chat-completions-sse"
                                                                      : "chat-ndjson";
        add_builtin({id,
                     name,
                     transport,
                     protocol == ApiProtocol::Ollama ? "optional-api-key" : "api-key",
                     {true, false, true, true, true, true, protocol == ApiProtocol::OpenRouter,
                      protocol == ApiProtocol::OpenAIResponses ||
                          protocol == ApiProtocol::AnthropicMessages,
                      false}},
                    [protocol](const ProviderConfig& config) {
                        ApiOptions options;
                        options.protocol = protocol;
                        options.tool_discovery_threshold = tool_threshold(config.settings);
                        if (protocol == ApiProtocol::Ollama && config.settings.contains("think"))
                            options.ollama_think = config.settings.at("think");
                        options.base_url = config.settings.value("base_url", std::string());
                        options.api_key = config.api_key;
                        options.tools = config.tools;
                        options.max_tool_rounds = count(config.settings, "max_tool_rounds");
                        options.max_output_tokens = config.settings.value("max_output_tokens", 0);
                        options.timeout = duration(config.settings, "timeout_ms", 600000);
                        options.turn_timeout =
                            optional_duration(config.settings, "turn_timeout_ms");
                        return std::make_shared<ApiProvider>(std::move(options));
                    });
    }
    for (const std::string id : {"openai-compatible", "llama.cpp", "lmstudio"}) {
        add_builtin(
            {id,
             id == "llama.cpp"  ? "llama.cpp server"
             : id == "lmstudio" ? "LM Studio"
                                : "Compatible Chat Completions",
             "chat-completions-sse",
             "optional-api-key",
             {true, false, true, true, true, true, true, false, false}},
            [id](const ProviderConfig& config) {
                HttpChatOptions options;
                options.tool_discovery_threshold = tool_threshold(config.settings);
                options.base_url = config.settings.value(
                    "base_url", std::string(id == "lmstudio"    ? "http://127.0.0.1:1234/v1"
                                            : id == "llama.cpp" ? "http://127.0.0.1:8080/v1"
                                                                : "http://127.0.0.1:9931/v1"));
                options.api_key = config.api_key;
                options.tools = config.tools;
                options.max_tool_rounds = count(config.settings, "max_tool_rounds");
                options.max_output_tokens = config.settings.value("max_output_tokens", 0);
                options.timeout = duration(config.settings, "timeout_ms", 600000);
                options.turn_timeout = optional_duration(config.settings, "turn_timeout_ms");
                return std::make_shared<HttpChatProvider>(std::move(options));
            });
    }
}
void ProviderRegistry::add(ProviderDescriptor descriptor, ProviderFactory factory) {
    if (descriptor.id.empty() || descriptor.id.size() > 128 ||
        descriptor.id.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789._-") !=
            std::string::npos ||
        descriptor.name.empty() || descriptor.transport.empty() || !factory)
        throw Error("InvalidOptions", "Provider requires a stable ID, name, transport and factory");
    if (!descriptor.default_settings.is_object() || descriptor.default_settings.contains("api_key"))
        throw Error("InvalidOptions", "Default settings must be a non-secret object");
    auto id = descriptor.id;
    std::lock_guard lock(mutex_);
    if (!entries_.emplace(id, std::make_pair(std::move(descriptor), std::move(factory))).second)
        throw Error("DuplicateProvider", "Provider ID is already registered");
}
namespace {
// Non-secret conventions for pre-filling a host UI. Environment names are published, never
// their values; "credential_env" names the variable that may hold the provider's API key.
Json defaults(const ProviderDescriptor& d) {
    const auto& h = d.discovery;
    auto settings = d.default_settings;
    auto endpoint = settings.value("base_url", h.default_endpoint);
    settings.erase("base_url");
    return {{"settings", std::move(settings)},      {"base_url", endpoint},
            {"endpoint_env", h.endpoint_env},       {"credential_env", h.api_key_env},
            {"executables", h.executables},         {"npm_packages", h.npm_packages},
            {"login_arguments", h.login_arguments}, {"local_service", h.local_service}};
}
} // namespace
Json ProviderRegistry::describe() const {
    std::lock_guard lock(mutex_);
    Json rows = Json::array();
    for (const auto& [id, entry] : entries_) {
        const auto& d = entry.first;
        const auto& c = d.capabilities;
        rows.push_back({{"schema_version", 1},
                        {"id", id},
                        {"name", d.name},
                        {"transport", d.transport},
                        {"authentication", d.authentication},
                        {"available_capabilities",
                         {{"streaming", c.streaming},
                          {"resume", c.resume},
                          {"tools", c.tools},
                          {"model_catalogue", c.model_catalogue},
                          {"multimodal", c.multimodal},
                          {"image_input", c.image_input},
                          {"audio_input", c.audio_input},
                          {"file_input", c.file_input},
                          {"persistent_process", c.persistent_process}}},
                        {"defaults", defaults(d)}});
    }
    return rows;
}
std::shared_ptr<Provider> ProviderRegistry::create(const std::string& id,
                                                   const ProviderConfig& config) const {
    if (!config.settings.is_object() || config.settings.contains("api_key"))
        throw Error("InvalidOptions", "Use non-secret settings and the separate API key field");
    ProviderFactory factory;
    ProviderConfig effective = config;
    {
        std::lock_guard lock(mutex_);
        auto found = entries_.find(id);
        if (found == entries_.end())
            throw Error("UnknownProvider", "Provider ID is not registered");
        factory = found->second.second;
        // Conventional defaults first; every host-supplied key wins, including explicit values.
        effective.settings = found->second.first.default_settings;
    }
    for (const auto& [key, value] : config.settings.items())
        effective.settings[key] = value;
    // Factories may call the registry; never execute extension code under its lock.
    try {
        auto provider = factory(effective);
        if (!provider)
            throw Error("InvalidProvider", "Provider factory returned null");
        provider->identity_ = id;
        provider->error_secrets_ = process_error_secrets(effective.process);
        if (!effective.api_key.empty())
            provider->error_secrets_.push_back(effective.api_key);
        return provider;
    } catch (const Json::exception&) {
        throw Error("InvalidOptions", "Invalid provider setting type");
    }
}
} // namespace cymcp
