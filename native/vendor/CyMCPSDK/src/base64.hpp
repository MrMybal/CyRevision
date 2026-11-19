#pragma once
#include <string>
#include <string_view>
namespace cymcp::detail {
inline constexpr std::string_view base64_alphabet =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
inline std::string base64(std::string_view bytes) {
    std::string out;
    out.reserve((bytes.size() + 2) / 3 * 4);
    for (std::size_t n = 0; n < bytes.size(); n += 3) {
        unsigned value = static_cast<unsigned char>(bytes[n]) << 16;
        if (n + 1 < bytes.size())
            value |= static_cast<unsigned char>(bytes[n + 1]) << 8;
        if (n + 2 < bytes.size())
            value |= static_cast<unsigned char>(bytes[n + 2]);
        out += base64_alphabet[(value >> 18) & 63];
        out += base64_alphabet[(value >> 12) & 63];
        out += n + 1 < bytes.size() ? base64_alphabet[(value >> 6) & 63] : '=';
        out += n + 2 < bytes.size() ? base64_alphabet[value & 63] : '=';
    }
    return out;
}
} // namespace cymcp::detail
