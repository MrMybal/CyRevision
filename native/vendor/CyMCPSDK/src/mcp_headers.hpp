#pragma once
#include "base64.hpp"
#include <algorithm>
#include <cymcp/core.hpp>

namespace cymcp::mcp_http {
inline constexpr auto modern = "2026-07-28";
inline constexpr auto legacy = "2025-11-25";
inline constexpr auto june_2025 = "2025-06-18";
inline bool legacy_version(std::string_view version) {
    return version == legacy || version == june_2025;
}
inline constexpr auto version_key = "io.modelcontextprotocol/protocolVersion";
inline bool sentinel(const std::string& s) {
    return s.starts_with("=?base64?") && s.ends_with("?=");
}
inline std::string encoded(const std::string& s) {
    return "=?base64?" + detail::base64(s) + "?=";
}
inline std::string header_value(const std::string& s) {
    const bool padded = !s.empty() && (s.front() == ' ' || s.front() == '\t' || s.back() == ' ' ||
                                       s.back() == '\t');
    if (padded || sentinel(s) || std::any_of(s.begin(), s.end(), [](unsigned char c) {
            return c != '\t' && (c < 32 || c > 126);
        }))
        return encoded(s);
    return s;
}
inline bool matches(const std::string& header, const std::string& expected) {
    // Comparing the canonical encoding is equivalent to decoding and comparing its UTF-8 bytes.
    return sentinel(header) ? header == encoded(expected) : header == header_value(expected);
}
inline const char* name_field(const std::string& method) {
    if (method == "tools/call" || method == "prompts/get")
        return "name";
    return method == "resources/read" ? "uri" : nullptr;
}
} // namespace cymcp::mcp_http
