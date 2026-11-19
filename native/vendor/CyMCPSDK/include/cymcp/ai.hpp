#pragma once
#include "content.hpp"
#include "context.hpp"
#include "http.hpp"
#include "process.hpp"
#include <optional>
namespace cymcp {
struct Session {
    std::string id, model;
};
struct ChatResult {
    std::string text, finish_reason;
    Json usage = Json::object();
    Json metrics = Json::object(); // timings in milliseconds; no prompt or credential data
};
struct ProviderCapabilities {
    bool streaming = false, resume = false, tools = false, model_catalogue = false,
         multimodal = false;
    bool image_input = false, audio_input = false, file_input = false, persistent_process = false;
    bool turn_model_selection = false;
};
class Provider {
    friend class ProviderRegistry;
    std::string identity_;
    std::vector<std::string> error_secrets_;

  public:
    explicit Provider(std::string identity = "custom") : identity_(std::move(identity)) {}
    const std::string& identity() const noexcept {
        return identity_;
    }
    std::string error_message(std::string_view message) const {
        return redact_error_message(message, error_secrets_);
    }
    virtual ~Provider() = default;
    virtual bool uses_host_history() const noexcept {
        return false;
    }
    // Native transports can opt into receiving only system instructions and the new user
    // message. Other providers continue to receive the complete editable history.
    virtual bool uses_native_turn_context() const noexcept {
        return false;
    }
    // Check input locally, without starting a session, sending a message or executing an action.
    virtual void validate_user_content(const Json& content) const {
        validate_content(content);
    }
    // True for a loopback model service (Ollama, LM Studio, llama.cpp...). Only these
    // may have a model chosen automatically: a cloud catalogue order implies no price.
    virtual bool local_service() const noexcept {
        return false;
    }
    virtual ProviderCapabilities capabilities() const = 0;
    virtual void connect(const CallContext& = {}) = 0;
    virtual Json list_models(const CallContext& = {}) {
        throw Error("Unsupported", "This transport does not expose a model catalogue");
    }
    virtual Session start_session(std::string model, std::string resume_id = {},
                                  const CallContext& = {}, std::string system_prompt = {}) = 0;
    // messages are typed JSON chat messages; tool calls/results may be appended during a turn.
    virtual ChatResult send(Session&, Json& messages, const CallContext&) = 0;
    virtual void disconnect() noexcept = 0;
};
// Agent defaults: no tool-round, output or whole-turn budget. Cancellation stops a turn; the
// idle timeout only detects a stalled connection and never cuts a stream that produces data.
struct HttpChatOptions {
    std::string base_url = "http://127.0.0.1:9931/v1", api_key;
    std::shared_ptr<const Registry> tools;
    std::size_t max_tool_rounds = 0;           // 0 = unlimited
    std::chrono::milliseconds timeout{600000}; // idle timeout of each HTTP request
    int max_output_tokens = 0;                 // 0 = server/model default (not sent)
    std::chrono::milliseconds turn_timeout{0}; // optional whole-turn deadline; 0 = none
    // Above this many tools, use on-demand discovery. size_t max preserves full definitions.
    std::size_t tool_discovery_threshold = 32;
};
class HttpChatProvider final : public Provider {
    HttpChatOptions options_;

  public:
    explicit HttpChatProvider(HttpChatOptions);
    void validate_user_content(const Json&) const override;
    bool uses_host_history() const noexcept override {
        return true;
    }
    bool local_service() const noexcept override;
    ProviderCapabilities capabilities() const override;
    void connect(const CallContext& = {}) override;
    Json list_models(const CallContext& = {}) override;
    Session start_session(std::string, std::string = {}, const CallContext& = {},
                          std::string = {}) override;
    ChatResult send(Session&, Json&, const CallContext&) override;
    void disconnect() noexcept override {}
};
// Callback returns a provider-native result. No raw requests are logged by the SDK.
using ProviderRequestHandler =
    std::function<Json(const std::string&, const Json&, const CallContext&)>;
struct CodexOptions {
    std::chrono::milliseconds connect_timeout{30000}, turn_timeout{0}; // 0 = no turn deadline
    ProcessOptions process;
    std::string approval_policy = "never", sandbox = "read-only";
    std::shared_ptr<const Registry> tools;
    Policy approvals;
    ProviderRequestHandler request_handler;
    std::size_t tool_discovery_threshold = 32;
};
class CodexProvider final : public Provider {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit CodexProvider(CodexOptions);
    bool uses_native_turn_context() const noexcept override {
        return true;
    }
    ~CodexProvider();
    ProviderCapabilities capabilities() const override;
    void connect(const CallContext& = {}) override;
    Json list_models(const CallContext& = {}) override;
    Session start_session(std::string, std::string = {}, const CallContext& = {},
                          std::string = {}) override;
    ChatResult send(Session&, Json&, const CallContext&) override;
    void disconnect() noexcept override;
};
struct ClaudeOptions {
    std::chrono::milliseconds connect_timeout{15000}, turn_timeout{0}; // 0 = no turn deadline
    ProcessOptions process;
    Json mcp_servers = Json::object();
    std::vector<std::string> allowed_tools; // empty means no built-in tools
    bool allow_all_actions = false;         // maps explicitly to the provider's bypass mode
    bool persistent = true;
    std::string setting_sources = "user"; // preserve the native user's provider/auth configuration
};
class ClaudeProvider final : public Provider {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit ClaudeProvider(ClaudeOptions);
    bool uses_native_turn_context() const noexcept override {
        return true;
    }
    ~ClaudeProvider();
    ProviderCapabilities capabilities() const override;
    void connect(const CallContext& = {}) override;
    Session start_session(std::string, std::string = {}, const CallContext& = {},
                          std::string = {}) override;
    ChatResult send(Session&, Json&, const CallContext&) override;
    void disconnect() noexcept override;
};
// API protocols have different authentication, request and streaming formats.
enum class ApiProtocol { OpenAIResponses, AnthropicMessages, Ollama, OpenRouter };
struct ApiOptions {
    ApiProtocol protocol = ApiProtocol::OpenAIResponses;
    std::string base_url, api_key; // empty URL selects the protocol's official endpoint
    std::shared_ptr<const Registry> tools;
    std::size_t max_tool_rounds = 0;           // 0 = unlimited
    int max_output_tokens = 0;                 // 0 = the model's maximum output
    std::chrono::milliseconds timeout{600000}; // idle timeout of each HTTP request
    std::chrono::milliseconds turn_timeout{0}; // optional whole-turn deadline; 0 = none
    Json ollama_think = nullptr; // omitted, on/off, or a reasoning level supported by the model
    std::size_t tool_discovery_threshold = 32;
};
class ApiProvider final : public Provider {
    ApiOptions options_;
    mutable std::mutex limits_mutex_;
    std::map<std::string, int> output_limits_; // per-model maximum output from the catalogue
    int output_limit(const std::string& model, const CallContext&);

  public:
    explicit ApiProvider(ApiOptions);
    void validate_user_content(const Json&) const override;
    bool uses_host_history() const noexcept override {
        return true;
    }
    bool local_service() const noexcept override;
    ProviderCapabilities capabilities() const override;
    void connect(const CallContext& = {}) override;
    Json list_models(const CallContext& = {}) override;
    Session start_session(std::string, std::string = {}, const CallContext& = {},
                          std::string = {}) override;
    ChatResult send(Session&, Json&, const CallContext&) override;
    void disconnect() noexcept override {}
};
enum class AgentCliProtocol { OpenCode, Antigravity, CommandCode, CodeBuddy, CodexExec };
struct AgentCliOptions {
    AgentCliProtocol protocol = AgentCliProtocol::OpenCode;
    ProcessOptions process;
    std::chrono::milliseconds connect_timeout{15000}, turn_timeout{0}; // 0 = no turn deadline
    bool allow_all_actions = false;
    bool persistent = false; // supported by Antigravity stream input
    // OpenCode v1 config, or Antigravity/CodeBuddy --settings; policy flags take precedence.
    Json native_settings = Json::object();
    std::filesystem::path mcp_config_file;  // optional CodeBuddy MCP file supplied by the host
    std::vector<std::string> allowed_tools; // CodeBuddy built-ins; empty disables them by default
};
class AgentCliProvider final : public Provider {
    AgentCliOptions options_;
    std::mutex mutex_;
    std::mutex cancellation_mutex_;
    Cancellation active_;
    bool connected_ = false;
    std::unique_ptr<Process> process_;
    std::unique_ptr<PrivateFile> settings_file_; // --settings without exposing it in argv
    std::string active_session_;
    CallContext begin(const CallContext&, std::chrono::milliseconds);

  public:
    explicit AgentCliProvider(AgentCliOptions);
    bool uses_native_turn_context() const noexcept override {
        return true;
    }
    ~AgentCliProvider();
    ProviderCapabilities capabilities() const override;
    void connect(const CallContext& = {}) override;
    Json list_models(const CallContext& = {}) override;
    Session start_session(std::string, std::string = {}, const CallContext& = {},
                          std::string = {}) override;
    ChatResult send(Session&, Json&, const CallContext&) override;
    void disconnect() noexcept override;
};
struct AcpOptions {
    // Complete command including the agent's ACP subcommand/flag; no shell is used.
    ProcessOptions process;
    std::chrono::milliseconds connect_timeout{15000}, turn_timeout{0}; // 0 = no turn deadline
    Json mcp_servers = Json::array(); // ACP stdio/HTTP server objects, not Claude's keyed map
    Policy approvals; // denies agent permission requests until explicitly configured
    std::string mode; // optional exact native mode/config value
    Json client_capabilities = Json::object(); // optional fs/terminal services supplied by the host
    ProviderRequestHandler request_handler;
    // Optional independent non-generating command, including any wrapper/script prefix.
    ProcessOptions models_process;
};
class AcpProvider final : public Provider {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit AcpProvider(AcpOptions);
    bool uses_native_turn_context() const noexcept override {
        return true;
    }
    ~AcpProvider();
    ProviderCapabilities capabilities() const override;
    void connect(const CallContext& = {}) override;
    Json list_models(const CallContext& = {}) override;
    Session start_session(std::string, std::string = {}, const CallContext& = {},
                          std::string = {}) override;
    ChatResult send(Session&, Json&, const CallContext&) override;
    void disconnect() noexcept override;
};
struct ProviderDiscoveryHints {
    // Optional host-extensible discovery recipe. No shell commands or credentials.
    std::vector<std::string> executables, npm_packages;
    std::string api_key_env, endpoint_env, default_endpoint;
    bool local_service = false;
    // Executables of a local service: found while its endpoint is silent → "not-running".
    std::vector<std::string> service_executables;
    // Arguments of the CLI's own interactive sign-in (empty: run the CLI itself, which signs in
    // on first use). The SDK never runs it: a host launches it in a visible console on request.
    std::vector<std::string> login_arguments;
};
struct ProviderDescriptor {
    std::string id, name, transport, authentication;
    // Available features; instance capabilities reflect the supplied configuration.
    ProviderCapabilities capabilities;
    ProviderDiscoveryHints discovery;
    // Conventional non-secret settings. ProviderRegistry::create() merges them under the
    // host's settings, and describe() publishes them so a UI can pre-fill its fields.
    Json default_settings = Json::object();
};
struct ProviderConfig {
    ProcessOptions process;
    Json settings = Json::object();
    std::string api_key; // supplied by the host's vault/environment; never catalogued
    std::shared_ptr<const Registry> tools;
    Policy approvals;
    ProviderRequestHandler request_handler;
};
using ProviderFactory = std::function<std::shared_ptr<Provider>(const ProviderConfig&)>;
struct DiscoveryOptions {
    bool use_environment = true, probe_local_services = true;
    std::chrono::milliseconds probe_timeout{600};
    // Additional installation locations; never searched recursively.
    std::vector<std::filesystem::path> executable_directories, npm_roots;
    std::map<std::string, ProviderConfig> configured;
};
struct DiscoveredConnection {
    std::string provider_id, name, state, source, api_key_env, error_code;
    ProviderConfig config; // secrets stay separate and in memory
    Json models = Json::array();
    bool available() const noexcept {
        return state == "available";
    }
};
// A configured executable is authoritative: an invalid one never falls back.
ProcessOptions resolve_provider_process(const ProviderDiscoveryHints&, ProcessOptions = {},
                                        const DiscoveryOptions& = {});
// Select only from actual catalogue entries. Empty means use the native default.
std::string default_model(const Json&);
class ProviderRegistry {
    mutable std::mutex mutex_;
    std::map<std::string, std::pair<ProviderDescriptor, ProviderFactory>> entries_;

  public:
    explicit ProviderRegistry(bool builtins = true);
    void add(ProviderDescriptor, ProviderFactory);
    Json describe() const;
    // Blocking, cancellable discovery. Run on a host worker, never the UI thread.
    // Inspects installations/environment and probes only local HTTP model catalogues.
    std::vector<DiscoveredConnection> discover(const DiscoveryOptions& = {},
                                               const CallContext& = {}) const;
    std::shared_ptr<Provider> create(const std::string&, const ProviderConfig& = {}) const;
};
class Conversation {
    struct State;
    std::shared_ptr<State> state_;
    CallContext reserve(const CallContext&);
    ChatResult send_reserved(Json, const CallContext&, Clock::time_point submitted);
    void change_user_turn(std::size_t, std::optional<std::string>);

  public:
    Conversation(std::shared_ptr<Provider>, std::string model, std::string system_prompt = {});
    void configure_context(ContextOptions); // host-history APIs only; idle conversation
    void connect(std::string resume_id = {}, const CallContext& = {}, bool automatic_model = false,
                 Json* models = nullptr);
    ChatResult send(Json user_content, const CallContext& = {});
    std::future<ChatResult> send_async(Executor&, Json user_content, CallContext = {});
    // Use a different model on the next explicit send, keeping the same conversation.
    void select_model(std::string model);
    void cancel() noexcept;
    void disconnect() noexcept;
    // Reconnect the exact native session, or restore host history for stateless APIs.
    // Never retries a submitted generation or an executed tool.
    void reconnect(const CallContext& = {});
    Json checkpoint() const;
    void restore(const Json&, const CallContext& = {});
    Json status() const;
    Json history() const;
    // Zero-based user-turn ordinals exclude systems, assistant messages and tool results.
    // Edits preserve attachments. Deleting a turn also removes its replies and tool exchanges.
    std::size_t user_turn_count() const;
    void edit_message(std::size_t user_turn, std::string text);
    void delete_turn(std::size_t user_turn);
    // Display history including failed/cancelled turns. Diagnostics are never sent to a model.
    Json transcript() const;
    Session session() const;
};
// Optional private, atomic on-disk storage. Keys/configuration are never included.
// Conversation contents (including embedded attachments) remain private user data.
class ConversationStore {
    std::filesystem::path directory_;

  public:
    explicit ConversationStore(std::filesystem::path);
    void save(const std::string& id, const Conversation&) const;
    Json load(const std::string& id) const;
    void erase(const std::string& id) const;
};
} // namespace cymcp
