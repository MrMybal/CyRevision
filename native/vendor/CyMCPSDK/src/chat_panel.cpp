#include <algorithm>
#include <array>
#include <charconv>
#include <cctype>
#include <cstring>
#include <cymcp/chat_ui.hpp>
#include <cyui/Core.h>
#include <cyui/modules/DocumentViews.h>

namespace cymcp {
namespace {
std::string attachment_name(std::string_view target) {
    while (target.starts_with("./"))
        target.remove_prefix(2);
    if (target.empty() || target.find_first_of("/\\:?#") != target.npos)
        return {};
    std::string name;
    for (std::size_t i = 0; i < target.size(); ++i) {
        unsigned char c = target[i];
        if (c == '%' && i + 2 < target.size()) {
            const auto* end = target.data() + i + 3;
            unsigned value = 0;
            const auto hex = std::from_chars(end - 2, end, value, 16);
            if (hex.ptr == end && hex.ec == std::errc{}) {
                c = static_cast<unsigned char>(value);
                i += 2;
            }
        }
        if (c < 32 || c == 127 || c == '/' || c == '\\' || c == ':')
            return {};
        name += static_cast<char>(c);
    }
    return name == "." || name == ".." ? std::string{} : name;
}
template <class List, class Name>
const typename List::value_type* named_attachment(const List& attachments, std::string_view target,
                                                  Name get_name) {
    const auto name = attachment_name(target);
    if (name.empty())
        return nullptr;
    const typename List::value_type* match = nullptr;
    for (const auto& attachment : attachments) {
        if (get_name(attachment) == name) {
            if (match)
                return nullptr;
            match = &attachment;
        }
    }
    return match;
}
const char* phase_label(const std::string& phase) {
    if (phase == "ready")
        return "Ready";
    if (phase == "choosing_model" || phase == "selecting_model")
        return "Choose a model";
    if (phase == "connecting")
        return "Connecting";
    if (phase == "generating")
        return "Generating";
    if (phase == "loading_models")
        return "Loading models";
    if (phase == "reconnecting")
        return "Reconnecting";
    if (phase == "saving")
        return "Saving";
    if (phase == "disconnecting")
        return "Disconnecting";
    if (phase == "interrupted")
        return "Interrupted";
    if (phase == "failed")
        return "Failed";
    return "Disconnected";
}
const char* error_label(const std::string& code) {
    if (code == "Busy")
        return "Wait for the response or stop it before sending. Your draft is kept.";
    if (code == "AttachmentUnavailable")
        return "Attachment preview is not available in this host.";
    if (code == "UnknownModel")
        return "Select a model in Providers, then connect.";
    if (code == "AuthenticationRequired")
        return "The provider requires authentication in its native environment.";
    if (code == "ExecutableMissing")
        return "CLI not found. Choose its native executable in Connection settings.";
    if (code == "Unsupported")
        return "This provider does not support this operation. You can enter a model ID manually.";
    if (code == "Cancelled")
        return "Operation interrupted. Reconnect explicitly if the provider closed its session.";
    if (code == "ContextTooLarge" || code == "Backpressure")
        return "The conversation reached a memory limit. Save it and start a new conversation.";
    if (code == "UnsupportedContent")
        return "This provider or model does not support this attachment type.";
    if (code == "SessionInvalid")
        return "The exact saved session could not be resumed. Its history has been preserved.";
    if (code == "InvalidState")
        return "Connect a session before sending a message.";
    if (code == "InvalidContent")
        return "Enter a message or select an attachment before sending.";
    return "The operation failed. Check the provider, model and connection settings.";
}
// Keep provider text literal in the transcript, even if the partial reply ends inside a
// Markdown fence. Escaping punctuation also prevents diagnostic text from activating services.
std::string quoted_diagnostic(std::string_view detail) {
    std::string result = "> ";
    for (const unsigned char c : detail) {
        if (c == '\n') {
            result += "\n> ";
        } else {
            if (c < 128 && std::ispunct(c))
                result += '\\';
            result += static_cast<char>(c);
        }
    }
    return result;
}
void register_chat_translations() {
    static std::once_flag once;
    std::call_once(once, [] {
        cyui::registerTranslations(
            "fr",
            {
                {"Ready", "Prêt"},
                {"Change model", "Changer de modèle"},
                {"Choose a model", "Choisir un modèle"},
                {"Select a model in Providers, then connect.",
                 "Sélectionnez un modèle dans Fournisseurs puis connectez."},
                {"Connecting", "Connexion"},
                {"Generating", "Génération"},
                {"Loading models", "Chargement des modèles"},
                {"Reconnecting", "Reconnexion"},
                {"Saving", "Enregistrement"},
                {"Disconnecting", "Déconnexion"},
                {"Interrupted", "Interrompu"},
                {"Failed", "Échec"},
                {"Disconnected", "Déconnecté"},
                {"You", "Vous"},
                {"Assistant", "Assistant"},
                {"Chat", "Tchat"},
                {"Reconnect", "Reconnecter"},
                {"Disconnect", "Déconnecter"},
                {"Save conversation", "Enregistrer la conversation"},
                {"Follow responses", "Suivre les réponses"},
                {"Chat options", "Options du tchat"},
                {"Find in conversation", "Rechercher dans la conversation"},
                {"Previous match", "Résultat précédent"},
                {"Next match", "Résultat suivant"},
                {"Search result", "Résultat de recherche"},
                {"No matches", "Aucun résultat"},
                {"Preview draft", "Aperçu du brouillon"},
                {"Reply", "Répondre"},
                {"Replying to", "Réponse à"},
                {"Cancel reply", "Annuler la réponse"},
                {"Edit message", "Modifier le message"},
                {"Delete message", "Supprimer le message"},
                {"Copy message", "Copier le message"},
                {"Save message (Enter)", "Enregistrer (Entrée)"},
                {"Send message (Enter)", "Envoyer (Entrée)"},
                {"Message…", "Écrire un message…"},
                {"Attach files", "Joindre des fichiers"},
                {"Remove attachment", "Retirer la pièce jointe"},
                {"Send", "Envoyer"},
                {"Stop", "Arrêter"},
                {"Activity", "Activité"},
                {"Thinking", "Réflexion"},
                {"First token", "Premier token"},
                {"Total", "Total"},
                {"No messages yet", "Aucun message pour le moment"},
                {"Send your first message.", "Envoyez votre premier message."},
                {"Connect a provider and send your first message.",
                 "Connectez un fournisseur puis envoyez votre premier message."},
                {"Type a message. Ctrl+Enter sends; Enter inserts a new line.",
                 "Écrivez un message. Ctrl+Entrée envoie ; Entrée ajoute une ligne."},
                {"Ctrl+Enter to send", "Ctrl+Entrée pour envoyer"},
                {"Enter to send · Shift+Enter for a new line",
                 "Entrée pour envoyer · Maj+Entrée pour une nouvelle ligne"},
                {"Wait for the response or stop it before sending. Your draft is kept.",
                 "Attendez la réponse ou arrêtez-la avant d’envoyer. Votre brouillon est "
                 "conservé."},
                {"Cancel edit", "Annuler la modification"},
                {"Attachment preview is not available in this host.",
                 "Cet hôte ne fournit pas de prévisualisation de pièce jointe."},
                {"Link", "Lien"},
                {"Copy link", "Copier le lien"},
                {"Close", "Fermer"},
                {"The provider requires authentication in its native environment.",
                 "Le fournisseur demande une authentification dans son environnement natif."},
                {"CLI not found. Choose its native executable in Connection settings.",
                 "CLI introuvable. Choisissez son exécutable natif dans les paramètres de "
                 "connexion."},
                {"This provider does not support this operation. You can enter a model ID "
                 "manually.",
                 "Ce fournisseur ne propose pas cette opération. Vous pouvez saisir un identifiant "
                 "de modèle."},
                {"Operation interrupted. Reconnect explicitly if the provider closed its session.",
                 "Opération interrompue. Reconnectez explicitement si le fournisseur a fermé sa "
                 "session."},
                {"The conversation reached a memory limit. Save it and start a new conversation.",
                 "La conversation a atteint une limite mémoire. Enregistrez-la puis ouvrez une "
                 "nouvelle conversation."},
                {"This provider or model does not support this attachment type.",
                 "Ce fournisseur ou modèle ne prend pas en charge ce type de pièce jointe."},
                {"The exact saved session could not be resumed. Its history has been preserved.",
                 "La session enregistrée n’a pas pu être reprise. Son historique est conservé."},
                {"Connect a session before sending a message.",
                 "Connectez une session avant d’envoyer un message."},
                {"Enter a message or select an attachment before sending.",
                 "Écrivez un message ou choisissez une pièce jointe."},
                {"The operation failed. Check the provider, model and connection settings.",
                 "L’opération a échoué. Vérifiez le fournisseur, le modèle et les paramètres de "
                 "connexion."},
                {"Conversation could not be saved", "La conversation n’a pas pu être enregistrée"},
            });
    });
}
} // namespace
const ChatAttachment* find_chat_attachment(const ChatMessage& message, std::string_view target) {
    return named_attachment(message.attachments, target,
                            [](const auto& item) -> std::string_view { return item.name; });
}
struct ChatPanel::Impl {
    ChatController& controller;
    ChatPanelOptions options;
    cyui::TranscriptState transcript;
    cyui::TranscriptMessage row;
    cyui::Document thinking, draft_preview;
    cyui::DocumentState thinking_view, draft_view;
    cyui::MessageComposerState composer, editor;
    std::uint32_t composer_id = 0;
    std::uint64_t editing = 0, replying = 0;
    std::vector<std::filesystem::path> attachments;
    struct BoundMarkdown {
        std::string source, rendered;
    };
    std::map<std::uint64_t, BoundMarkdown> bound_rows;
    const std::string& markdown(const ChatMessage& item) {
        if (item.attachments.empty() || item.markdown.find('[') == std::string::npos ||
            item.attachments.front().source.empty())
            return item.markdown;
        auto& cached = bound_rows[item.id];
        if (cached.source == item.markdown)
            return cached.rendered;
        cyui::Document doc;
        doc.setMarkdown(item.markdown);
        std::map<std::size_t, std::pair<std::size_t, std::string>> replacements;
        for (const auto& destination : doc.destinations()) {
            if (const auto* file = find_chat_attachment(item, destination.target);
                file && !file->source.empty())
                replacements.emplace(destination.begin, std::make_pair(destination.end, file->source));
        }
        cached.source = item.markdown;
        cached.rendered.clear();
        std::size_t begin = 0;
        for (const auto& [start, replacement] : replacements) {
            cached.rendered.append(item.markdown, begin, start - begin);
            cached.rendered += replacement.second;
            begin = replacement.first;
        }
        cached.rendered.append(item.markdown, begin, std::string::npos);
        return cached.rendered;
    }
    std::string local_error, link;
    std::array<char, 256> query{};
    std::uint64_t match = 0;
    bool find_open = false, focus_find = false, preview = false;
    void find_match(bool previous, bool restart) {
        const auto& rows = controller.state().messages;
        const auto* current = restart ? nullptr : message(match);
        match = 0;
        if (!query.front() || rows.empty())
            return;
        const std::string_view needle(query.data());
        const auto contains = [needle](std::string_view text) {
            return std::search(text.begin(), text.end(), needle.begin(), needle.end(),
                               [](unsigned char a, unsigned char b) {
                                   return std::tolower(a) == std::tolower(b);
                               }) != text.end();
        };
        const auto count = rows.size();
        std::size_t index =
            current ? static_cast<std::size_t>(current - rows.data()) : (previous ? 0 : count - 1);
        for (std::size_t n = 0; n < count; ++n) {
            index = previous ? (index + count - 1) % count : (index + 1) % count;
            const auto& candidate = rows[index];
            if (contains(candidate.markdown) || contains(candidate.error_message) ||
                contains(candidate.error_code) || contains(candidate.provider_code) ||
                std::any_of(candidate.attachments.begin(), candidate.attachments.end(),
                            [&](const auto& attachment) { return contains(attachment.name); })) {
                transcript.scrollTo = match = candidate.id;
                return;
            }
        }
    }
    void attach(std::vector<std::filesystem::path> paths) {
        if (paths.size() + attachments.size() > 256)
            throw Error("InvalidContent", "At most 256 attachments");
        attachments.insert(attachments.end(), paths.begin(), paths.end());
    }
    const ChatMessage* message(std::uint64_t id) const {
        const auto& rows = controller.state().messages;
        auto found = std::lower_bound(rows.begin(), rows.end(), id,
                                      [](const auto& row, auto key) { return row.id < key; });
        return found != rows.end() && found->id == id ? &*found : nullptr;
    }
    bool can_edit(const ChatMessage& item) const {
        const auto& state = controller.state();
        return item.role == "user" && !state.busy && options.edit_message &&
               options.delete_message &&
               (!options.require_editable_history ||
                (state.editable_history && item.user_turn.has_value()));
    }
    std::string submission_text() const {
        const auto* reply = message(replying);
        if (!reply)
            return composer.text;
        const auto& source = reply->markdown.empty() ? reply->error_message : reply->markdown;
        auto keep = std::min<std::size_t>(source.size(), 4096);
        while (keep < source.size() && keep &&
               (static_cast<unsigned char>(source[keep]) & 0xc0) == 0x80)
            --keep;
        std::string quoted = "> ";
        for (const char c : std::string_view(source).substr(0, keep)) {
            quoted += c;
            if (c == '\n')
                quoted += "> ";
        }
        return quoted + "\n\n" + composer.text;
    }
    explicit Impl(ChatController& c, ChatPanelOptions o) : controller(c), options(std::move(o)) {
        register_chat_translations();
    }
    template <class F> void action(F&& fn) {
        try {
            fn();
            local_error.clear();
        } catch (const Error& error) {
            local_error = error.code;
        } catch (...) {
            local_error = "HostError";
        }
    }
};
ChatPanel::ChatPanel(ChatController& controller, ChatPanelOptions options)
    : impl_(std::make_unique<Impl>(controller, std::move(options))) {}
ChatPanel::~ChatPanel() = default;
void ChatPanel::set_draft(std::string text) {
    impl_->composer.text = std::move(text);
    impl_->composer.focus = true;
}
const std::string& ChatPanel::draft() const noexcept {
    return impl_->composer.text;
}
void ChatPanel::add_attachments(std::vector<std::filesystem::path> paths) {
    impl_->attach(std::move(paths));
}
const std::vector<std::filesystem::path>& ChatPanel::pending_attachments() const noexcept {
    return impl_->attachments;
}
void ChatPanel::draw(const char* id, float width, float height) {
    auto& s = *impl_;
    s.controller.poll();
    const auto& state = s.controller.state();
    if (s.local_error == "Busy" && !state.busy)
        s.local_error.clear();
    if (s.editing) {
        const auto* item = s.message(s.editing);
        if (!item || !s.can_edit(*item))
            s.editing = 0;
    }
    if (s.replying && !s.message(s.replying))
        s.replying = 0;
    cyui::ScopedID scope(id);
    ImGui::BeginChild("##chat-panel", {width, height}, 0,
                      ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    if (ImGui::Shortcut(ImGuiMod_Ctrl | ImGuiKey_F)) {
        s.find_open = true;
        s.focus_find = true;
    }
    if (s.find_open) {
        const float buttons = 3 * (ImGui::GetFrameHeightWithSpacing() + cyui::dp(4));
        ImGui::SetNextItemWidth(std::max(cyui::dp(40), ImGui::GetContentRegionAvail().x - buttons));
        if (s.focus_find)
            ImGui::SetKeyboardFocusHere();
        s.focus_find = false;
        const bool next = ImGui::InputTextWithHint(
            "##find", cyui::tr("Find in conversation").c_str(), s.query.data(), s.query.size(),
            ImGuiInputTextFlags_EnterReturnsTrue);
        if (ImGui::IsItemEdited())
            s.find_match(false, true);
        if (next) {
            s.find_match(ImGui::GetIO().KeyShift, false);
            s.focus_find = true;
        }
        const bool escape_find = ImGui::IsItemDeactivated() && ImGui::IsKeyPressed(ImGuiKey_Escape);
        ImGui::SameLine();
        if (cyui::iconButton(cyui::icon::chevronUp, cyui::tr("Previous match").c_str()))
            s.find_match(true, false);
        ImGui::SameLine();
        if (cyui::iconButton(cyui::icon::chevronDown, cyui::tr("Next match").c_str()))
            s.find_match(false, false);
        ImGui::SameLine();
        if (cyui::iconButton(cyui::icon::close, cyui::tr("Close").c_str()) || escape_find ||
            ImGui::Shortcut(ImGuiKey_Escape)) {
            s.find_open = false;
            s.composer.focus = true;
        }
        if (s.find_open && s.query.front() && !s.match)
            cyui::textFaint("%s", cyui::tr("No matches").c_str());
    }
    const auto error = s.local_error.empty() ? state.error_code : s.local_error;
    const bool error_in_transcript = s.local_error.empty() && !state.messages.empty() &&
                                     state.messages.back().error_code == error &&
                                     state.messages.back().provider_code == state.provider_code &&
                                     state.messages.back().error_message == state.error_message;
    if (!error.empty() && !error_in_transcript) {
        const auto detail = s.local_error.empty() && !state.error_message.empty()
                                ? state.error_message
                                : cyui::tr(error_label(error));
        cyui::callout(error == "Cancelled" ? cyui::Tone::neutral : cyui::Tone::warning,
                      detail.c_str());
        if (s.local_error.empty() && !state.provider_code.empty())
            cyui::textFaint("%s / %s", error.c_str(), state.provider_code.c_str());
        else
            cyui::textFaint("%s", error.c_str());
    }
    if (!state.storage_error.empty()) {
        cyui::callout(cyui::Tone::warning, cyui::tr("Conversation could not be saved").c_str());
        cyui::textFaint("%s", state.storage_error.c_str());
    }
    cyui::DocumentOptions document;
    document.services = s.options.document_services;
    document.tasks = false;
    document.linkPreviews = s.options.link_previews;
    std::string activated_link;
    const bool has_activity = !state.thinking.empty() || !state.activity.empty();
    const auto activity_id = ImGui::GetID(cyui::trLabel("Activity").c_str());
    const bool activity_open = ImGui::GetStateStorage()->GetBool(activity_id);
    if (has_activity && activity_open) {
        ImGui::BeginChild("##activity",
                          {0, std::min(cyui::dp(140), ImGui::GetContentRegionAvail().y * .25f)},
                          ImGuiChildFlags_Borders);
        if (!state.thinking.empty()) {
            s.thinking.setMarkdown(state.thinking);
            activated_link = cyui::documentView("##thinking", s.thinking_view, s.thinking,
                                                cyui::dp(0, 95), document)
                                 .link;
        }
        for (const auto& activity : state.activity)
            cyui::textDim("%s: %s", activity.type.c_str(), activity.name.c_str());
        ImGui::EndChild();
    }
    const bool generating = state.busy && state.phase == "generating";
    const auto spacing = ImGui::GetStyle().ItemSpacing;
    const float button_width = ImGui::GetFrameHeight() + cyui::dp(2);
    const auto stop = cyui::trLabel("Stop");
    const float buttons_width = (s.options.pick_attachments ? button_width + spacing.x : 0) +
                                (generating ? cyui::buttonWidth(stop.c_str()) + spacing.x : 0);
    const float composer_width =
        std::max(cyui::dp(40), ImGui::GetContentRegionAvail().x - buttons_width - cyui::dp(2));
    const auto measured = ImGui::CalcTextSize(
        s.composer.text.c_str(), nullptr, false,
        std::max(cyui::dp(1), composer_width - button_width - spacing.x - cyui::dp(24)));
    const auto available = ImGui::GetContentRegionAvail().y;
    const float status_height = ImGui::GetFrameHeightWithSpacing() +
                                ImGui::GetStyle().CellPadding.y * 2 + spacing.y;
    const float min_input = ImGui::GetFrameHeight() + cyui::dp(8);
    const float input_height = std::clamp(
        measured.y + ImGui::GetStyle().FramePadding.y * 2 + cyui::dp(8), min_input,
        std::max(min_input, std::min(cyui::dp(180), (available - status_height) * .55f)));
    float remaining = available - input_height - status_height - spacing.y -
                      (s.replying ? ImGui::GetFrameHeightWithSpacing() : 0);
    const float attachments_height = s.attachments.empty() ? 0 :
        std::max(0.f, std::min(s.options.preview_attachment ? cyui::dp(99) :
                              ImGui::GetFrameHeightWithSpacing() + ImGui::GetStyle().ScrollbarSize,
                              remaining * .45f));
    if (attachments_height > 0)
        remaining -= attachments_height + spacing.y;
    const float preview_height = s.preview && !s.composer.text.empty()
        ? std::max(0.f, std::min(cyui::dp(180), remaining * .45f)) : 0;
    if (preview_height > 0)
        remaining -= preview_height + spacing.y;
    const float transcript_height = std::max(1.f, remaining);
    if (state.messages.empty()) {
        ImGui::BeginChild("##empty", {0, transcript_height});
        cyui::emptyState(cyui::tr("No messages yet").c_str(),
                         cyui::tr(state.connected
                                      ? "Send your first message."
                                      : "Connect a provider and send your first message.")
                             .c_str(),
                         cyui::icon::mail);
        ImGui::EndChild();
    } else {
        cyui::TranscriptOptions options;
        options.document = document;
        options.editing = s.editing;
        bool edited = false;
        options.editor = [&](const auto&, float editor_width) {
            cyui::MessageComposerOptions input;
            input.attachments = false;
            input.editing = true;
            auto result =
                cyui::messageComposer("##edit-message", s.editor, {editor_width, 0}, input);
            if (result.submitted && s.options.edit_message)
                edited = true;
            if (result.cancelled || cyui::button(cyui::trLabel("Cancel edit").c_str()))
                s.editing = 0;
        };
        cyui::TranscriptSource source;
        source.id = ImGui::GetID("##chat-history");
        source.count = state.messages.size();
        // Streaming edits do not reset the sparse height index or parsed cache.
        source.revision = state.messages.front().id;
        source.message = [&](std::size_t index) -> const cyui::TranscriptMessage* {
            if (index >= state.messages.size())
                return nullptr;
            const auto& message = state.messages[index];
            const auto& display = s.markdown(message);
            s.row = {};
            s.row.id = message.id;
            s.row.role = cyui::tr(message.role == "user" ? "You" : "Assistant");
            if (!message.error_code.empty()) {
                s.row.time = cyui::tr("Failed") + " [" + message.error_code;
                if (!message.provider_code.empty())
                    s.row.time += " / " + message.provider_code;
                s.row.time += "]";
                s.row.content = quoted_diagnostic(message.error_message.empty()
                                                      ? cyui::tr(error_label(message.error_code))
                                                      : message.error_message);
                if (!display.empty()) {
                    s.row.content += "\n\n";
                    s.row.content += display;
                }
            } else {
                s.row.content = display;
                s.row.time = message.interrupted ? cyui::tr("Interrupted") : "";
            }
            if (s.find_open && s.match == message.id)
                s.row.time += (s.row.time.empty() ? "" : " · ") + cyui::tr("Search result");
            s.row.own = s.can_edit(message);
            for (const auto& attachment : message.attachments)
                s.row.attachments.push_back({attachment.id, attachment.name, attachment.source,
                                             attachment.bytes,
                                             attachment.mime_type.rfind("image/", 0) == 0});
            return &s.row;
        };
        source.indexOf = [&](cyui::ItemId key) {
            const auto* message = s.message(key);
            return message ? static_cast<std::size_t>(message - state.messages.data())
                           : std::string::npos;
        };
        const auto result =
            cyui::transcript("##messages", s.transcript, source, {0, transcript_height}, options);
        std::erase_if(s.bound_rows, [&](const auto& item) {
            return !s.transcript.documents.contains(item.first);
        });
        // The displayed diagnostic is escaped for Markdown. Copy its original sanitized text.
        if (const auto* item = s.message(result.copy)) {
            if (item->error_code.empty()) {
                ImGui::SetClipboardText(item->markdown.c_str());
            } else {
                std::string copied = item->error_code;
                if (!item->provider_code.empty())
                    copied += " / " + item->provider_code;
                copied += "\n" + item->error_message;
                if (!item->markdown.empty())
                    copied += "\n\n" + item->markdown;
                ImGui::SetClipboardText(copied.c_str());
            }
        }
        if (edited && s.editing)
            s.action([&] {
                s.options.edit_message(s.editing, s.editor.text);
                s.editing = 0;
            });
        if (result.reply) {
            s.replying = result.reply;
            s.composer.focus = true;
        }
        if (result.edit)
            if (const auto* message = s.message(result.edit)) {
                s.editing = message->id;
                s.editor.text = message->markdown;
                s.editor.focus = true;
            }
        if (result.erase && s.options.delete_message)
            s.action([&] { s.options.delete_message(result.erase); });
        if (result.attachmentMessage)
            if (const auto* message = s.message(result.attachmentMessage);
                message && result.attachmentIndex < message->attachments.size())
                s.action([&] {
                    if (!s.options.open_attachment)
                        throw Error("AttachmentUnavailable", "Host attachment callback required");
                    s.options.open_attachment(message->attachments[result.attachmentIndex],
                                              result.download);
                });
        if (!result.link.empty())
            activated_link = result.link;
    }
    if (preview_height > 0) {
        s.draft_preview.setMarkdown(s.composer.text);
        auto preview_document = document;
        cyui::DocumentServices services = document.services ? *document.services : cyui::DocumentServices{};
        if (s.options.preview_attachment) {
            services.image = [&, fallback = services.image](const std::string& target) {
                const auto* file = named_attachment(s.attachments, target, [](const auto& path) {
                    return path_utf8(path.filename());
                });
                return file ? s.options.preview_attachment(*file)
                            : fallback ? fallback(target) : cyui::ImageData{};
            };
            preview_document.services = &services;
        }
        const auto result = cyui::documentView("##draft-preview", s.draft_view, s.draft_preview,
                                               {0, preview_height}, preview_document);
        if (!result.link.empty())
            activated_link = result.link;
    }
    if (!activated_link.empty()) {
        if (activated_link.starts_with("cymcp-attachment:")) {
            s.action([&] {
                for (const auto& item : state.messages)
                    for (const auto& attachment : item.attachments)
                        if (attachment.source == activated_link && s.options.open_attachment) {
                            s.options.open_attachment(attachment, false);
                            return;
                        }
                throw Error("AttachmentUnavailable", "No matching attachment");
            });
        } else if (s.options.open_link)
            s.action([&] { s.options.open_link(activated_link); });
        else {
            s.link = std::move(activated_link);
            ImGui::OpenPopup("##chat-link");
        }
    }
    if (ImGui::BeginPopup("##chat-link")) {
        cyui::heading(cyui::tr("Link").c_str(), 3);
        ImGui::PushTextWrapPos(ImGui::GetCursorPosX() + cyui::dp(420));
        ImGui::TextUnformatted(s.link.c_str());
        ImGui::PopTextWrapPos();
        if (cyui::button(cyui::trLabel("Copy link").c_str()))
            ImGui::SetClipboardText(s.link.c_str());
        ImGui::EndPopup();
    }
    if (const auto* reply = s.message(s.replying)) {
        ImGui::AlignTextToFramePadding();
        cyui::textDim("%s %s", cyui::tr("Replying to").c_str(),
                      cyui::tr(reply->role == "user" ? "You" : "Assistant").c_str());
        ImGui::SameLine();
        if (cyui::iconButton(cyui::icon::close, cyui::tr("Cancel reply").c_str()))
            s.replying = 0;
    }
    if (attachments_height > 0) {
        ImGui::BeginChild("##attachments", {0, attachments_height}, 0,
                          ImGuiWindowFlags_HorizontalScrollbar);
        std::size_t remove = s.attachments.size();
        for (std::size_t n = 0; n < s.attachments.size(); ++n) {
            if (n)
                ImGui::SameLine();
            cyui::ScopedID attachment_id(static_cast<int>(n));
            ImGui::BeginGroup();
            const auto slot_height = std::min(cyui::dp(48), attachments_height -
                ImGui::GetFrameHeightWithSpacing() - ImGui::GetStyle().ScrollbarSize);
            if (s.options.preview_attachment && slot_height > cyui::dp(8)) {
                const ImVec2 slot{cyui::dp(90), slot_height};
                const auto from = ImGui::GetCursorScreenPos();
                if (ImGui::IsRectVisible(slot)) {
                    const auto thumb = s.options.preview_attachment(s.attachments[n]);
                    if (thumb.texture && thumb.width > 0 && thumb.height > 0) {
                        const auto scale = std::min(slot.x / thumb.width, slot.y / thumb.height);
                        ImGui::GetWindowDrawList()->AddImage(thumb.texture, from,
                            {from.x + scale * thumb.width, from.y + scale * thumb.height},
                            thumb.uvMin, thumb.uvMax);
                    } else {
                        ImGui::GetWindowDrawList()->AddText(from, cyui::palette().textDim,
                            cyui::iconsAvailable() ? cyui::icon::document : "FILE");
                    }
                }
                ImGui::Dummy(slot);
            }
            ImGui::AlignTextToFramePadding();
            ImGui::TextUnformatted(path_utf8(s.attachments[n].filename()).c_str());
            ImGui::SameLine();
            if (cyui::iconButton(cyui::icon::close, cyui::tr("Remove attachment").c_str()))
                remove = n;
            ImGui::EndGroup();
        }
        ImGui::EndChild();
        if (remove < s.attachments.size())
            s.attachments.erase(s.attachments.begin() + remove);
    }
    cyui::MessageComposerOptions composer_options;
    composer_options.attachments = static_cast<bool>(s.options.pick_attachments);
    composer_options.canSubmitEmpty = !s.attachments.empty();
    composer_options.paste = [&](std::string& text) {
        if (!s.options.paste)
            return false;
        bool consumed = false;
        s.action([&] {
            std::vector<std::filesystem::path> paths;
            consumed = s.options.paste(text, paths);
            if (consumed)
                s.attach(std::move(paths));
        });
        return consumed;
    };
    cyui::MessageComposerResult composed;
    {
        cyui::ScopedID draft_id(static_cast<int>(s.composer_id));
        composed = cyui::messageComposer("##composer", s.composer, {composer_width, input_height},
                                          composer_options);
    }
    if (composed.cancelled)
        s.replying = 0;
    if (composed.focused && !state.busy && s.composer.text.empty() &&
        ImGui::IsKeyPressed(ImGuiKey_UpArrow, false)) {
        const auto last = std::find_if(state.messages.rbegin(), state.messages.rend(),
                                       [](const auto& item) { return item.role == "user"; });
        if (last != state.messages.rend()) {
            if (s.can_edit(*last)) {
                s.transcript.scrollTo = s.editing = last->id;
                s.editor.text = last->markdown;
                s.editor.focus = true;
            } else {
                s.composer.text = last->markdown;
                s.composer.focus = true;
            }
        }
    }
    if (composed.attach && s.options.pick_attachments)
        s.action([&] { s.attach(s.options.pick_attachments()); });
    if (generating) {
        ImGui::SameLine();
        if (cyui::button(stop.c_str()))
            s.controller.cancel();
    }
    const auto phase = cyui::tr(phase_label(state.phase));
    auto* storage = ImGui::GetStateStorage();
    if (ImGui::BeginTable("##chat-footer", s.options.open_settings ? 2 : 1,
                          ImGuiTableFlags_NoSavedSettings)) {
        ImGui::TableSetupColumn("##session", ImGuiTableColumnFlags_WidthStretch);
        if (s.options.open_settings)
            ImGui::TableSetupColumn("##settings", ImGuiTableColumnFlags_WidthFixed,
                                    cyui::iconsAvailable() ? button_width :
                                    cyui::buttonWidth(cyui::tr("Settings").c_str()));
        ImGui::TableNextColumn();
        if (cyui::iconButton(cyui::iconsAvailable() ? cyui::icon::more : "...",
                             cyui::tr("Chat options").c_str()))
            ImGui::OpenPopup("##chat-options");
        if (ImGui::BeginPopup("##chat-options")) {
            if (ImGui::MenuItem(cyui::trLabel("Find in conversation").c_str(), "Ctrl+F")) {
                s.find_open = s.focus_find = true;
            }
            ImGui::MenuItem(cyui::trLabel("Preview draft").c_str(), nullptr, &s.preview);
            ImGui::MenuItem(cyui::trLabel("Follow responses").c_str(), nullptr,
                            &s.transcript.follow);
            if (ImGui::MenuItem(cyui::trLabel("Activity").c_str(), nullptr, activity_open,
                                has_activity))
                storage->SetBool(activity_id, !activity_open);
            ImGui::Separator();
            if (ImGui::MenuItem(cyui::trLabel("Reconnect").c_str(), nullptr, false,
                                !state.busy && !state.provider_id.empty()))
                s.action([&] { s.controller.reconnect(); });
            if (ImGui::MenuItem(cyui::trLabel("Disconnect").c_str(), nullptr, false,
                                !state.busy && state.connected))
                s.action([&] { s.controller.disconnect(); });
            if (ImGui::MenuItem(cyui::trLabel("Save conversation").c_str(), nullptr, false,
                                !state.busy && state.connected))
                s.action([&] { s.controller.save(); });
            ImGui::EndPopup();
        }
        ImGui::SameLine();
        const auto label = state.provider_id + (state.model.empty() ? "" : " / " + state.model);
        if (state.turn_model_selection && state.models.is_array() && !state.models.empty()) {
            cyui::ScopedDisabled disabled(state.busy);
            if (cyui::ghostButton((label + "###chat-model").c_str()))
                ImGui::OpenPopup("##chat-models");
            cyui::tooltip(cyui::tr("Change model").c_str());
            if (ImGui::BeginPopup("##chat-models")) {
                for (const auto& row : state.models) {
                    if (!row.is_object() || row.value("hidden", Json(false)) == true)
                        continue;
                    auto model = row.find("model");
                    if (model == row.end() || !model->is_string())
                        model = row.find("id");
                    if (model == row.end() || !model->is_string())
                        continue;
                    const auto& id = model->get_ref<const std::string&>();
                    if (!id.empty() && ImGui::Selectable(id.c_str(), id == state.model) &&
                        id != state.model)
                        s.action([&] { s.controller.select_model(id); });
                }
                ImGui::EndPopup();
            }
        } else {
            ImGui::AlignTextToFramePadding();
            cyui::textDim("%s", label.c_str());
        }
        ImGui::SameLine(0, cyui::dp(16));
        ImGui::AlignTextToFramePadding();
        const auto& palette = cyui::palette();
        cyui::statusDot(!state.error_code.empty() ? palette.warning
                        : state.busy              ? palette.accent
                        : state.connected         ? palette.success
                                                  : palette.textFaint);
        ImGui::SameLine(0, cyui::dp(4));
        cyui::textDim("%s", phase.c_str());
        if (s.options.open_settings) {
            ImGui::TableNextColumn();
            const auto tip = cyui::tr("Settings");
            if (cyui::iconsAvailable() ? cyui::iconButton(cyui::icon::settings, tip.c_str())
                                       : cyui::button(cyui::trLabel("Settings").c_str()))
                s.action(s.options.open_settings);
        }
        ImGui::EndTable();
    }
    if (composed.submitted && !state.busy && state.connected)
        s.action([&] {
            s.controller.send(s.submission_text(), s.attachments);
            // A click has just moved focus off the old input. Reusing its ImGui state while
            // clearing the buffer and shrinking the wrap width for Stop can hang cursor layout.
            ++s.composer_id;
            s.composer = {};
            s.composer.focus = true;
            s.attachments.clear();
            s.replying = 0;
            if (s.transcript.follow)
                s.transcript.jumpToBottom = true;
        });
    else if (composed.submitted)
        s.local_error = state.busy ? "Busy" : "InvalidState";
    ImGui::EndChild();
}
} // namespace cymcp
