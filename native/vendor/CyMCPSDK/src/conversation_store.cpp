#include <cymcp/ai.hpp>
#include <fstream>
#ifdef _WIN32
#include <windows.h>
#else
#include <cstdio>
#endif
namespace cymcp {
namespace {
void safe_path(const std::filesystem::path& path) {
    auto absolute = std::filesystem::absolute(path).lexically_normal();
    std::filesystem::path current;
    for (const auto& part : absolute) {
        current /= part;
#ifdef _WIN32
        auto attributes = GetFileAttributesW(current.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT))
            throw Error("UnsafePath", "Conversation store cannot use reparse points");
        if (attributes == INVALID_FILE_ATTRIBUTES && GetLastError() != ERROR_FILE_NOT_FOUND &&
            GetLastError() != ERROR_PATH_NOT_FOUND)
            throw Error("UnsafePath", "Cannot inspect conversation storage path");
#else
        if (std::filesystem::is_symlink(std::filesystem::symlink_status(current)))
            throw Error("UnsafePath", "Conversation store cannot use symlinks");
#endif
    }
}
std::filesystem::path file_for(const std::filesystem::path& directory, const std::string& id) {
    if (id.empty() || id.size() > 128 ||
        id.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-") !=
            std::string::npos)
        throw Error("UnsafePath", "Invalid conversation storage ID");
    auto path = directory / (id + ".json");
    safe_path(path);
    return path;
}
} // namespace
ConversationStore::ConversationStore(std::filesystem::path directory)
    : directory_(std::filesystem::absolute(std::move(directory)).lexically_normal()) {
#ifdef __APPLE__
    // macOS supplies /var/folders/... as TMPDIR. Resolve only its root-owned OS aliases.
    for (const auto* alias : {"/var", "/tmp"}) {
        const auto suffix = directory_.lexically_relative(alias);
        if (suffix.empty() || *suffix.begin() == ".." || suffix == ".")
            continue;
        const auto physical = std::filesystem::path("/private") / (alias + 1);
        std::error_code ec;
        if (std::filesystem::canonical(alias, ec) == physical && !ec)
            directory_ = physical / suffix;
        break;
    }
#endif
    safe_path(directory_);
    private_directory(directory_);
}
void ConversationStore::save(const std::string& id, const Conversation& conversation) const {
    auto target = file_for(directory_, id);
    auto snapshot = conversation.checkpoint();
    auto bytes = snapshot.dump();
    if (bytes.size() > provider_message_limit)
        throw Error("ContextTooLarge", "Checkpoint exceeds limit");
    auto temporary = directory_ / ("pending-" + random_token(16));
    struct Cleanup {
        std::filesystem::path p;
        ~Cleanup() {
            std::error_code ec;
            std::filesystem::remove(p, ec);
        }
    } cleanup{temporary};
    {
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        output.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        output.flush();
        if (!output)
            throw Error("FileError", "Cannot save conversation checkpoint");
    }
#ifdef _WIN32
    if (!MoveFileExW(temporary.c_str(), target.c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
        throw Error("FileError", "Cannot replace conversation checkpoint");
#else
    std::filesystem::permissions(
        temporary, std::filesystem::perms::owner_read | std::filesystem::perms::owner_write,
        std::filesystem::perm_options::replace);
    if (std::rename(temporary.c_str(), target.c_str()))
        throw Error("FileError", "Cannot replace conversation checkpoint");
#endif
}
Json ConversationStore::load(const std::string& id) const {
    auto path = file_for(directory_, id);
    if (!std::filesystem::is_regular_file(path))
        throw Error("FileError", "Conversation checkpoint not found");
    if (std::filesystem::file_size(path) > provider_message_limit)
        throw Error("ContextTooLarge", "Checkpoint exceeds limit");
    std::ifstream input(path, std::ios::binary);
    std::string data;
    char buffer[65536];
    while (input.read(buffer, sizeof(buffer)) || input.gcount()) {
        data.append(buffer, static_cast<std::size_t>(input.gcount()));
        if (data.size() > provider_message_limit)
            throw Error("ContextTooLarge", "Checkpoint exceeds limit");
    }
    if (!input.eof())
        throw Error("FileError", "Cannot read conversation checkpoint");
    return parse_json(data, provider_message_limit);
}
void ConversationStore::erase(const std::string& id) const {
    std::filesystem::remove(file_for(directory_, id));
}
} // namespace cymcp
