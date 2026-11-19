#include "base64.hpp"
#include <algorithm>
#include <cctype>
#include <cymcp/content.hpp>
#include <cymcp/http.hpp>
#include <fstream>
namespace cymcp {
namespace {
constexpr std::size_t limit = provider_message_limit;
using detail::base64;
constexpr auto alphabet = detail::base64_alphabet;
void binary(const Json& value, const char* key) {
    const auto& data = value.at(key).get_ref<const std::string&>();
    if (data.empty() || data.size() > limit || data.size() % 4)
        throw Error("InvalidContent", "Invalid base64 attachment size");
    auto padding = data.size() - data.find_last_not_of('=') - 1;
    auto invalid = data.find_first_not_of(alphabet);
    if (padding > 2 ||
        (padding == 0 ? invalid != std::string::npos : invalid != data.size() - padding))
        throw Error("InvalidContent", "Invalid base64 attachment");
    if (padding) {
        auto last = alphabet.find(data[data.size() - padding - 1]);
        if ((padding == 2 && (last & 15)) || (padding == 1 && (last & 3)))
            throw Error("InvalidContent", "Noncanonical base64 attachment");
    }
}
std::string mime(const Json& value) {
    auto result = value.at("mimeType").get<std::string>();
    if (result.empty() || result.size() > 128 || result.find('/') == std::string::npos ||
        result.find_first_not_of(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789/._+-") !=
            std::string::npos)
        throw Error("InvalidContent", "Invalid attachment MIME type");
    return result;
}
std::string data_url(const Json& block) {
    return "data:" + mime(block) + ";base64," + block.at("data").get<std::string>();
}
} // namespace
Json text_block(std::string text) {
    return {{"type", "text"}, {"text", std::move(text)}};
}
void validate_content(const Json& content) {
    try {
        detail::json_size(content, limit);
        if (content.is_string())
            return;
        // The composer permits 256 attachments plus an accompanying text message.
        if (!content.is_array() || content.empty() || content.size() > 257)
            throw Error("InvalidContent", "Expected text or nonempty content blocks");
        std::size_t attachments = 0;
        for (const auto& block : content) {
            auto type = block.at("type").get<std::string>();
            if (type == "text") {
                block.at("text").get_ref<const std::string&>();
                continue;
            }
            if (++attachments > 256)
                throw Error("InvalidContent", "Too many attachments");
            if (type == "image" || type == "audio") {
                auto m = mime(block);
                if (m.rfind(type + "/", 0) != 0)
                    throw Error("InvalidContent", "Attachment MIME/type mismatch");
                binary(block, "data");
            } else if (type == "resource") {
                auto& resource = block.at("resource");
                mime(resource);
                auto uri = resource.at("uri").get<std::string>();
                if (uri.empty() || uri.size() > 4096 ||
                    uri.find_first_of("\r\n") != std::string::npos)
                    throw Error("InvalidContent", "Invalid resource URI");
                if (resource.contains("text") == resource.contains("blob"))
                    throw Error("InvalidContent", "Resource requires exactly one of text or blob");
                if (resource.contains("text"))
                    resource.at("text").get_ref<const std::string&>();
                else
                    binary(resource, "blob");
            } else if (type == "image_url") {
                auto url = block.at("image_url").at("url").get<std::string>();
                if (url.rfind("https://", 0) == 0)
                    parse_url(url);
                else
                    throw Error(
                        "InvalidContent",
                        "Image URLs must use HTTPS; use embedded image blocks for local data");
            } else
                throw Error("UnsupportedContent", "Unknown portable content block");
        }
        if (content.size() == 257 && attachments != 256)
            throw Error("InvalidContent", "The extra block is reserved for an attachment caption");
    } catch (const Json::exception&) {
        throw Error("InvalidContent", "Invalid content block fields");
    }
}
Json attachment_from_file(const std::filesystem::path& path, std::string type,
                          std::size_t max_bytes) {
    if (!max_bytes || max_bytes > 128 * 1024 * 1024)
        throw Error("InvalidOptions", "Attachment byte limit must be between 1 and 128 MiB");
    if (std::filesystem::is_symlink(std::filesystem::symlink_status(path)) ||
        !std::filesystem::is_regular_file(path))
        throw Error("UnsafePath", "Attachment must be a regular file");
    const auto file_bytes = std::filesystem::file_size(path);
    if (file_bytes > max_bytes)
        throw Error("ContextTooLarge", "Attachment exceeds limit");
    if (type.empty()) {
        auto ext = path.extension().string();
        std::transform(ext.begin(), ext.end(), ext.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        static const std::map<std::string, std::string> types = {
            {".png", "image/png"},    {".jpg", "image/jpeg"},       {".jpeg", "image/jpeg"},
            {".webp", "image/webp"},  {".gif", "image/gif"},        {".pdf", "application/pdf"},
            {".wav", "audio/wav"},    {".mp3", "audio/mpeg"},       {".txt", "text/plain"},
            {".md", "text/markdown"}, {".json", "application/json"}};
        auto found = types.find(ext);
        if (found == types.end())
            throw Error("InvalidContent", "Supply an explicit MIME type for this file");
        type = found->second;
    }
    std::ifstream input(path, std::ios::binary);
    if (!input)
        throw Error("FileError", "Cannot open attachment");
    std::string bytes;
    bytes.reserve(static_cast<std::size_t>(file_bytes));
    char buffer[65536];
    while (input.read(buffer, sizeof(buffer)) || input.gcount()) {
        const auto count = static_cast<std::size_t>(input.gcount());
        if (count > max_bytes - bytes.size())
            throw Error("ContextTooLarge", "Attachment exceeds limit");
        bytes.append(buffer, count);
    }
    if (!input.eof())
        throw Error("FileError", "Cannot read attachment");
    Json block;
    if (type.rfind("image/", 0) == 0 || type.rfind("audio/", 0) == 0)
        block = {
            {"type", type.substr(0, type.find('/'))}, {"mimeType", type}, {"data", base64(bytes)}};
    else {
        auto name = path_utf8(path.filename());
        Json resource = {
            {"uri", "attachment:" + random_token(16)}, {"mimeType", type}, {"name", name}};
        if (type.rfind("text/", 0) == 0 || type == "application/json")
            resource["text"] = std::move(bytes);
        else
            resource["blob"] = base64(bytes);
        block = {{"type", "resource"}, {"resource", std::move(resource)}};
    }
    Json single = Json::array();
    single.push_back(std::move(block));
    validate_content(single);
    return std::move(single.front());
}
namespace detail {
std::size_t json_size(const Json& value, std::size_t max_bytes) {
    class Counter final : public std::streambuf {
        std::size_t maximum_;

      public:
        std::size_t bytes = 0;
        explicit Counter(std::size_t maximum) : maximum_(maximum) {}
        std::streamsize xsputn(const char*, std::streamsize length) override {
            const auto count = static_cast<std::size_t>(length);
            if (count > maximum_ - bytes)
                throw Error("ContextTooLarge", "Content exceeds limit");
            bytes += count;
            return length;
        }
        int_type overflow(int_type ch) override {
            if (!traits_type::eq_int_type(ch, traits_type::eof()))
                xsputn(nullptr, 1);
            return traits_type::not_eof(ch);
        }
    } counter(max_bytes);
    std::ostream stream(&counter);
    stream.exceptions(std::ios::badbit | std::ios::failbit);
    stream << value;
    return counter.bytes;
}
Json content_from_files(std::string text, const std::vector<std::filesystem::path>& paths,
                        const Cancellation& cancel, std::size_t max_bytes) {
    if (paths.size() > 256)
        throw Error("InvalidContent", "Too many attachments");
    Json content = Json::array();
    if (!text.empty())
        content.push_back(text_block(std::move(text)));
    auto remaining = max_bytes - json_size(content, max_bytes);
    for (const auto& path : paths) {
        cancel.check();
        if (!content.empty()) {
            if (!remaining)
                throw Error("ContextTooLarge", "Attachments exceed message limit");
            --remaining; // comma between blocks
        }
        if (!remaining)
            throw Error("ContextTooLarge", "Attachments exceed message limit");
        auto block = attachment_from_file(path, {}, std::min<std::size_t>(remaining, 32U << 20));
        if (block.at("type") != "resource")
            block["name"] = path_utf8(path.filename());
        remaining -= json_size(block, remaining);
        content.push_back(std::move(block));
    }
    return content;
}
std::string content_text(const Json& content) {
    validate_content(content);
    if (content.is_string())
        return content.get<std::string>();
    std::string result;
    for (const auto& block : content) {
        if (block.at("type") != "text")
            throw Error("UnsupportedContent", "This transport accepts text only");
        result += block.at("text").get<std::string>();
    }
    return result;
}
Json content_for(const Json& content, ContentFormat format, const std::string& role) {
    validate_content(content);
    if (content.is_string()) {
        if (format == ContentFormat::Acp || format == ContentFormat::Codex)
            return Json::array({text_block(content.get<std::string>())});
        return content;
    }
    Json output = Json::array();
    for (const auto& block : content) {
        auto type = block.at("type").get<std::string>();
        if (format == ContentFormat::Acp) {
            if (type == "image_url")
                throw Error("UnsupportedContent", "ACP images require embedded data");
            auto wire = block;
            if (type == "resource")
                wire["resource"].erase("name"); // filename is SDK metadata
            else if (type == "image" || type == "audio")
                wire.erase("name");
            output.push_back(std::move(wire));
            continue;
        }
        if (type == "text") {
            output.push_back({{"type", format == ContentFormat::Responses
                                           ? (role == "assistant" ? "output_text" : "input_text")
                                           : "text"},
                              {"text", block.at("text")}});
        } else if (type == "image" || type == "image_url") {
            auto url = type == "image" ? data_url(block)
                                       : block.at("image_url").at("url").get<std::string>();
            if (format == ContentFormat::Responses)
                output.push_back({{"type", "input_image"}, {"image_url", url}});
            else if (format == ContentFormat::Codex)
                output.push_back({{"type", "image"}, {"url", url}});
            else if (format == ContentFormat::Anthropic) {
                Json source = type == "image" ? Json{{"type", "base64"},
                                                     {"media_type", mime(block)},
                                                     {"data", block.at("data")}}
                                              : Json{{"type", "url"}, {"url", url}};
                output.push_back({{"type", "image"}, {"source", source}});
            } else if (format == ContentFormat::ChatCompletions)
                output.push_back({{"type", "image_url"}, {"image_url", {{"url", url}}}});
            else
                throw Error("UnsupportedContent", "Use Ollama native image conversion");
        } else if (type == "resource") {
            const auto& resource = block.at("resource");
            auto m = mime(resource);
            if (resource.contains("text")) {
                auto text = resource.at("uri").get<std::string>() + "\n" +
                            resource.at("text").get<std::string>();
                output.push_back(
                    {{"type", format == ContentFormat::Responses ? "input_text" : "text"},
                     {"text", text}});
            } else if (format == ContentFormat::Responses) {
                output.push_back(
                    {{"type", "input_file"},
                     {"filename", resource.value("name", std::string("attachment.pdf"))},
                     {"file_data",
                      "data:" + m + ";base64," + resource.at("blob").get<std::string>()}});
            } else if (format == ContentFormat::Anthropic && m == "application/pdf") {
                output.push_back(
                    {{"type", "document"},
                     {"source",
                      {{"type", "base64"}, {"media_type", m}, {"data", resource.at("blob")}}}});
            } else
                throw Error("UnsupportedContent", "Binary files are unsupported by this transport");
        } else if (type == "audio" && format == ContentFormat::ChatCompletions) {
            auto m = mime(block);
            if (m != "audio/wav" && m != "audio/mpeg")
                throw Error("UnsupportedContent", "Chat audio requires WAV or MP3");
            output.push_back(
                {{"type", "input_audio"},
                 {"input_audio",
                  {{"data", block.at("data")}, {"format", m == "audio/wav" ? "wav" : "mp3"}}}});
        } else
            throw Error("UnsupportedContent", "Content type is unsupported by this transport");
    }
    return output;
}
} // namespace detail
} // namespace cymcp
