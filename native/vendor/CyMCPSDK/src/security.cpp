#include "error_credentials.hpp"
#include <algorithm>
#include <array>
#include <cymcp/core.hpp>
#include <fstream>
#include <picosha2.h>
#ifdef _WIN32
#include <aclapi.h>
#include <bcrypt.h>
#include <fcntl.h>
#include <io.h>
#include <sddl.h>
#include <windows.h>
#else
#include <cerrno>
#include <fcntl.h>
#include <unistd.h>
#endif
namespace cymcp {
namespace {
constexpr std::string_view masked = "[REDACTED]";
constexpr std::size_t diagnostic_limit = 4096;
bool word_char(char c) {
    return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
           c == '-' || c == '_';
}
bool ascii_equal(std::string_view a, std::string_view b) {
    return a.size() == b.size() &&
           std::equal(a.begin(), a.end(), b.begin(), [](char x, char y) {
               return (x >= 'A' && x <= 'Z' ? x + ('a' - 'A') : x) == y;
           });
}
std::size_t value_end(std::string_view text, std::size_t pos) {
    if (pos < text.size() && (text[pos] == '"' || text[pos] == '\'')) {
        const char quote = text[pos++];
        while (pos < text.size()) {
            const char c = text[pos++];
            if (c == quote)
                break;
            if (c == '\\' && pos < text.size())
                ++pos;
        }
        return pos;
    }
    // Keep an existing marker intact when diagnostics pass multiple boundaries.
    if (text.substr(pos).starts_with(masked))
        return pos + masked.size();
    return std::min(text.size(), text.find_first_of(" \t\r\n,;&)}]<>\"'", pos));
}
}
std::string redact_error_message(std::string_view message,
                                 const std::vector<std::string>& secrets) {
    // Look up full configured values in the original input before truncating. In particular
    // a secret that straddles the output boundary must not leave its prefix in the journal.
    std::string prepared;
    std::size_t offset = 0;
    while (offset < message.size() && prepared.size() < diagnostic_limit * 2) {
        std::size_t matched = 0;
        for (const auto& secret : secrets)
            if (secret.size() > matched && message.substr(offset).starts_with(secret))
                matched = secret.size();
        if (matched) {
            prepared += masked;
            offset += matched;
        } else {
            prepared += message[offset++];
        }
    }
    const std::string_view text = prepared;
    std::string result;
    for (std::size_t i = 0; i < text.size();) {
        if (text.substr(i).starts_with("-----BEGIN ")) {
            const auto label_end = text.find("-----", i + 11);
            const auto label = text.substr(i + 11, label_end == text.npos ? text.npos : label_end - i - 11);
            if (label_end != text.npos && label.ends_with("PRIVATE KEY")) {
                const auto footer = "-----END " + std::string(label) + "-----";
                const auto end = text.find(footer, label_end + 5);
                result += masked;
                i = end == text.npos ? text.size() : end + footer.size();
                continue;
            }
        }
        if (text.substr(i).starts_with("://")) {
            result += "://";
            i += 3;
            const auto end = text.find_first_of("/?# \t\r\n", i);
            const auto at = text.substr(i, end == text.npos ? text.npos : end - i).rfind('@');
            if (at != text.npos) {
                result += masked;
                result += '@';
                i += at + 1;
            } else if (end == text.npos && offset < message.size()) {
                result += masked;
                i = text.size(); // the end of a long URL authority is outside the excerpt
            }
            continue;
        }
        if (!word_char(text[i])) {
            const unsigned char c = text[i++];
            if (c < 32 || c == 127) {
                if (!result.empty() && result.back() != ' ')
                    result += ' ';
            } else {
                result += static_cast<char>(c);
            }
            continue;
        }
        const auto begin = i;
        while (i < text.size() && word_char(text[i]))
            ++i;
        const auto word = text.substr(begin, i - begin);
        if (word.starts_with("sk-") || word.starts_with("ghp_") || word.starts_with("gho_") ||
            word.starts_with("github_pat_") || word.starts_with("glpat-") ||
            word.starts_with("xoxb-") || word.starts_with("hf_") ||
            (word.starts_with("eyJ") && i < text.size() && text[i] == '.')) {
            result += masked;
            i = value_end(text, begin);
            continue;
        }
        result += word;
        auto p = i;
        if (p < text.size() && (text[p] == '"' || text[p] == '\''))
            ++p;
        p = std::min(text.size(), text.find_first_not_of(" \t\r\n", p));
        const bool field = detail::credential_name(word) && p < text.size() &&
                           (text[p] == ':' || text[p] == '=');
        const bool bearer = ascii_equal(word, "bearer") && p > i;
        if (!field && !bearer)
            continue;
        if (field) {
            ++p;
            p = std::min(text.size(), text.find_first_not_of(" \t\r\n", p));
        }
        result.append(text.substr(i, p - i));
        const auto end = value_end(text, p);
        auto value = text.substr(p, end - p);
        if (ascii_equal(value, "basic") || ascii_equal(value, "bearer") ||
            ascii_equal(value, "token") || ascii_equal(value, "apikey")) {
            p = std::min(text.size(), text.find_first_not_of(" \t\r\n", end));
        }
        result += masked;
        i = value_end(text, p);
        if ((ascii_equal(word, "cookie") || ascii_equal(word, "set-cookie")) &&
            !value.starts_with('"') && !value.starts_with('\''))
            i = std::min(text.size(), text.find_first_of("\r\n", i));
    }
    // what() can originate in a native library using a different encoding. A diagnostic
    // must remain serializable even when it contains a malformed byte.
    if (std::any_of(result.begin(), result.end(), [](unsigned char c) { return c >= 128; })) {
        const auto encoded = Json(result).dump(-1, ' ', false, Json::error_handler_t::replace);
        result = Json::parse(encoded).get<std::string>();
    }
    std::replace_if(result.begin(), result.end(),
                    [](char c) { return c == '\t' || c == '\r' || c == '\n'; }, ' ');
    if (result.size() > diagnostic_limit || offset != message.size()) {
        auto end = std::min(result.size(), diagnostic_limit - 3);
        while (end && end < result.size() && (static_cast<unsigned char>(result[end]) & 0xc0) == 0x80)
            --end;
        result.resize(end);
        result += "...";
    }
    return result;
}
std::string random_token(std::size_t size) {
    if (size < 16 || size > 1024)
        throw Error("InvalidOptions", "Token length must be 16..1024 bytes");
    std::vector<unsigned char> bytes(size);
#ifdef _WIN32
    if (BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(size),
                        BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0)
        throw Error("SystemError", "Secure randomness unavailable");
#else
    int fd = open("/dev/urandom", O_RDONLY | O_CLOEXEC);
    if (fd < 0)
        throw Error("SystemError", "Secure randomness unavailable");
    std::size_t at = 0;
    while (at < size) {
        auto n = read(fd, bytes.data() + at, size - at);
        if (n < 0 && errno == EINTR)
            continue;
        if (n <= 0) {
            close(fd);
            throw Error("SystemError", "Secure random read failed");
        }
        at += static_cast<std::size_t>(n);
    }
    close(fd);
#endif
    std::string result;
    result.reserve(size * 2);
    constexpr char hex[] = "0123456789abcdef";
    for (auto b : bytes) {
        result += hex[b >> 4];
        result += hex[b & 15];
    }
    return result;
}
bool constant_time_equal(const std::string& a, const std::string& b) noexcept {
    if (a.size() != b.size())
        return false;
    volatile unsigned char difference = 0;
    for (std::size_t i = 0; i < a.size(); ++i)
        difference =
            static_cast<unsigned char>(difference | static_cast<unsigned char>(a[i] ^ b[i]));
    return difference == 0;
}
std::string sha256(const std::string& s) {
    return picosha2::hash256_hex_string(s);
}
std::string sha256_file(const std::filesystem::path& path, const Cancellation& cancel) {
    std::ifstream in(path, std::ios::binary);
    if (!in)
        throw Error("FileError", "Cannot open file for verification");
    picosha2::hash256_one_by_one hash;
    std::array<char, 65536> buffer{};
    while (in) {
        cancel.check();
        in.read(buffer.data(), buffer.size());
        hash.process(buffer.begin(), buffer.begin() + in.gcount());
    }
    if (!in.eof())
        throw Error("FileError", "File read failed");
    hash.finish();
    return picosha2::get_hash_hex_string(hash);
}
#ifdef _WIN32
namespace {
using PrivateDescriptor = std::unique_ptr<void, decltype(&LocalFree)>;
PrivateDescriptor private_descriptor(bool inheritable) {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        throw Error("SystemError", "Cannot inspect directory owner");
    struct TokenCleanup {
        HANDLE h;
        ~TokenCleanup() {
            CloseHandle(h);
        }
    } cleanup{token};
    DWORD size = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<unsigned char> storage(size);
    if (!GetTokenInformation(token, TokenUser, storage.data(), size, &size))
        throw Error("SystemError", "Cannot inspect directory owner");
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(storage.data())->User.Sid, &sid))
        throw Error("SystemError", "Cannot resolve directory owner");
    std::wstring descriptor = inheritable ? L"D:P(A;OICI;FA;;;" : L"D:P(A;;FA;;;";
    descriptor += std::wstring(sid) + L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR security = nullptr;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(descriptor.c_str(), SDDL_REVISION_1,
                                                              &security, nullptr))
        throw Error("SystemError", "Cannot construct private directory ACL");
    return PrivateDescriptor(security, &LocalFree);
}
} // namespace
#endif
void private_directory(const std::filesystem::path& path) {
    std::filesystem::create_directories(path);
    if (std::filesystem::is_symlink(std::filesystem::symlink_status(path)))
        throw Error("UnsafePath", "Private directory cannot be a symlink");
#ifdef _WIN32
    auto security = private_descriptor(true);
    PACL dacl = nullptr;
    BOOL present = FALSE, defaulted = FALSE;
    GetSecurityDescriptorDacl(security.get(), &present, &dacl, &defaulted);
    auto name = path.wstring();
    if (SetNamedSecurityInfoW(name.data(), SE_FILE_OBJECT,
                              DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION,
                              nullptr, nullptr, dacl, nullptr) != ERROR_SUCCESS)
        throw Error("SystemError", "Cannot protect private directory");
#else
    std::filesystem::permissions(path, std::filesystem::perms::owner_all,
                                 std::filesystem::perm_options::replace);
#endif
}
PrivateOutputFile open_private_output_file(const std::filesystem::path& path) {
    const auto parent = path.parent_path();
    if (!parent.empty())
        std::filesystem::create_directories(parent);
    const auto temporary = parent / ("pending-" + random_token(16));
#ifdef _WIN32
    auto descriptor = private_descriptor(false);
    SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES), descriptor.get(), FALSE};
    const auto handle =
        CreateFileW(temporary.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_DELETE,
                    &security, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE)
        throw Error("FileError", "Cannot create private file");
    int fd = _open_osfhandle(reinterpret_cast<intptr_t>(handle), _O_BINARY | _O_WRONLY);
    if (fd == -1)
        CloseHandle(handle);
    PrivateOutputFile file(fd == -1 ? nullptr : _fdopen(fd, "wb"), &std::fclose);
    if (!file && fd != -1)
        _close(fd);
#else
    int fd = ::open(temporary.c_str(), O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC, 0600);
    if (fd == -1)
        throw Error("FileError", "Cannot create private file");
    PrivateOutputFile file(fdopen(fd, "wb"), &std::fclose);
    if (!file)
        close(fd);
#endif
    std::error_code ec;
    if (file)
        std::filesystem::rename(temporary, path, ec);
    if (!file || ec) {
        file.reset();
        std::filesystem::remove(temporary, ec);
        throw Error("FileError", "Cannot open private output file");
    }
    return file;
}
std::string platform_id() {
#ifdef _WIN32
    std::string os = "windows";
#elif defined(__APPLE__)
    std::string os = "macos";
#elif defined(__linux__)
    std::string os = "linux";
#else
    std::string os = "unsupported";
#endif
#if defined(_M_ARM64) || defined(__aarch64__)
    return os + "-arm64";
#elif defined(_M_X64) || defined(__x86_64__)
    return os + "-x64";
#else
    return os + "-unknown";
#endif
}
} // namespace cymcp
