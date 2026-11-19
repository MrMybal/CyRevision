#pragma once
#include <cymcp/core.hpp>

namespace cymcp::detail {
inline constexpr std::size_t provider_error_body_limit = 64 * 1024;

inline Json parse_provider_error_body(const std::string& body) {
    try {
        return parse_json(body, provider_error_body_limit);
    } catch (const Error&) {
        return nullptr;
    }
}

inline const char* provider_http_error_code(int status) {
    if (status == 401 || status == 403)
        return "AuthenticationRequired";
    return status == 429 ? "RateLimited" : "ProviderError";
}

// Only explicitly documented fields can be surfaced from an upstream error object.
inline Error structured_provider_error(const Json& fields, std::string_view code_field,
                                       const std::vector<std::string>& secrets,
                                       std::string sdk_code, std::string fallback) {
    std::string code;
    if (fields.is_object()) {
        if (auto message = fields.find("message"); message != fields.end() &&
                                                   message->is_string() &&
                                                   !message->get_ref<const std::string&>().empty())
            fallback = redact_error_message(message->get_ref<const std::string&>(), secrets);
        if (auto native = fields.find(std::string(code_field)); native != fields.end()) {
            if (native->is_string())
                code = native->get_ref<const std::string&>();
            else if (native->is_number_integer())
                code = native->dump();
        }
    }
    return Error(std::move(sdk_code), std::move(fallback), redact_error_message(code, secrets));
}

inline Error structured_provider_error(const Json& fields, std::string_view code_field,
                                       const std::string& api_key, std::string sdk_code,
                                       std::string fallback) {
    return structured_provider_error(fields, code_field, std::vector<std::string>{api_key},
                                     std::move(sdk_code), std::move(fallback));
}
} // namespace cymcp::detail
