#pragma once
#include "core.hpp"
namespace cymcp {
// Portable blocks follow MCP/ACP: text, image/audio (mimeType + base64 data),
// and resource (uri + mimeType + text or base64 blob). No implicit file reads.
Json text_block(std::string text);
Json attachment_from_file(const std::filesystem::path&, std::string mime_type = {},
                          std::size_t max_bytes = 32 * 1024 * 1024);
void validate_content(const Json&);
namespace detail {
// Exact compact JSON size, including UTF-8 and escaping. Stops at the supplied byte budget.
std::size_t json_size(const Json&, std::size_t limit);
Json content_from_files(std::string text, const std::vector<std::filesystem::path>&,
                        const Cancellation&, std::size_t max_bytes = provider_message_limit);
enum class ContentFormat { ChatCompletions, Responses, Anthropic, Ollama, Acp, Codex };
Json content_for(const Json&, ContentFormat, const std::string& role = "user");
std::string content_text(const Json&);
} // namespace detail
} // namespace cymcp
