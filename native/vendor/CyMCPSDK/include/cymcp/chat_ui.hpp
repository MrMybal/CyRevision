#pragma once
#include "ai.hpp"
#include <cstdint>

namespace cyui {
struct DocumentServices;
struct ImageData;
} // namespace cyui
namespace cymcp {
// Optional CyMCP::ChatUI module. The host owns the ImGui context and renderer.
struct ChatConnection {
    std::string provider_id, model, system_prompt, resume_id;
    ProviderConfig provider;
    std::filesystem::path history_directory;
    std::string history_id;
    bool restore = false;
    bool automatic_model = false;
    ContextOptions context;
    std::shared_ptr<PerformanceMonitor> performance;
};
struct ChatAttachment {
    std::uint64_t id = 0;
    std::string name, source, mime_type;
    std::uint64_t bytes = 0;
    // An immutable portable content block, loaded on the worker. No local path or URL fetching.
    std::shared_ptr<const Json> content;
};
struct ChatMessage {
    std::uint64_t id = 0;
    std::string role, markdown;
    bool interrupted = false;
    std::vector<ChatAttachment> attachments;
    // A failed turn retains its partial Markdown and a separate, sanitized diagnostic.
    // interrupted is reserved for cancellation.
    std::string error_code, error_message, provider_code;
    // Present only for a fully displayed, committed user prompt managed by the host.
    std::optional<std::size_t> user_turn;
};
struct ChatActivity {
    std::string type, name;
};
// Resolve a Markdown filename only among the attachments of its own message.
// Ambiguous names, absolute paths, schemes and traversal do not resolve.
const ChatAttachment* find_chat_attachment(const ChatMessage&, std::string_view target);
struct ChatDiagnostic {
    std::uint64_t id = 0;
    std::int64_t timestamp_ms = 0;
    std::string level, provider_id, operation, code, message;
    double elapsed_ms = 0;
    std::string provider_code;
};
struct ChatViewState {
    std::string phase = "disconnected", provider_id, model, session_id, error_code, storage_error;
    bool busy = false, connected = false;
    std::uint64_t revision = 0;
    std::vector<ChatMessage> messages;
    std::vector<ChatActivity> activity;
    // Bounded event summaries and sanitized provider explanations, including native error codes.
    std::deque<ChatDiagnostic> diagnostics;
    std::string thinking;
    Json models = Json::array(), metrics = Json::object(), usage = Json::object();
    std::string error_message, provider_code;
    bool editable_history = false;
    bool turn_model_selection = false;
};
// Call methods and inspect state on one host/UI thread. All provider and file I/O
// runs on the owned worker. poll() transfers bounded events without waiting.
// Destruction cancels and joins the worker; destroy before unloading providers.
class ChatController {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit ChatController(std::shared_ptr<ProviderRegistry> = {});
    ~ChatController();
    ChatController(const ChatController&) = delete;
    ChatController& operator=(const ChatController&) = delete;
    void open(ChatConnection);
    void send(std::string text, std::vector<std::filesystem::path> attachments = {});
    void select_model(std::string model);
    void edit_message(std::uint64_t id, std::string text);
    void delete_turn(std::uint64_t user_id);
    void refresh_models();
    void reconnect();
    void save();
    void disconnect();
    void cancel() noexcept;
    void poll();
    const ChatViewState& state() const noexcept;
};
struct ChatPanelOptions {
    // Called on the UI thread, after explicit user activation. The host decides
    // whether/how to open links and choose files; the module never uses a shell.
    std::function<void(const std::string&)> open_link;
    std::function<std::vector<std::filesystem::path>()> pick_attachments;
    const cyui::DocumentServices* document_services = nullptr;
    std::function<void(const ChatAttachment&, bool save)> open_attachment;
    // Optional host clipboard import; return true when consumed. Native text paste works otherwise.
    std::function<bool(std::string&, std::vector<std::filesystem::path>&)> paste;
    // Both callbacks are required to enable native edit/delete actions on user rows.
    // The host owns provider-compatible mutations; the panel never rewrites a native session.
    std::function<void(std::uint64_t, const std::string&)> edit_message;
    std::function<void(std::uint64_t)> delete_message;
    // Ask the host's nonblocking DocumentServices::preview for cached link cards while drawing.
    bool link_previews = false;
    // Nonblocking thumbnail lookup for a file the user added to the draft.
    std::function<cyui::ImageData(const std::filesystem::path&)> preview_attachment;
    // Use the SDK's history capabilities for edit/delete. External callbacks can otherwise
    // implement their own edits. Up on an empty draft recalls the text when edits are unavailable.
    bool require_editable_history = false;
    std::function<void()> open_settings;
};
class ChatPanel {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit ChatPanel(ChatController&, ChatPanelOptions = {});
    ~ChatPanel();
    ChatPanel(const ChatPanel&) = delete;
    ChatPanel& operator=(const ChatPanel&) = delete;
    // Draw inside the host's current ImGui window, between NewFrame/Render.
    // Dimensions are screen pixels; zero fills the available region.
    void draw(const char* id = "Chat", float width = 0, float height = 0);
    void set_draft(std::string);
    const std::string& draft() const noexcept;
    void add_attachments(std::vector<std::filesystem::path>);
    const std::vector<std::filesystem::path>& pending_attachments() const noexcept;
};
} // namespace cymcp
