#pragma once
#include <string>
#include <string_view>

namespace cymcp::detail {
inline bool credential_name(std::string_view name) {
    // Match common header/JSON/CLI names, including prefixed environment variables.
    if (name.size() > 128)
        return false;
    std::string key;
    for (char ch : name) {
        if (ch == '-' || ch == '_')
            continue;
        key += ch >= 'A' && ch <= 'Z' ? static_cast<char>(ch - 'A' + 'a') : ch;
    }
    return key == "key" || key.ends_with("apikey") || key.ends_with("accesskeyid") ||
           key.ends_with("accesskey") || key.ends_with("privatekey") ||
           key.ends_with("token") || key.ends_with("password") || key.ends_with("passwd") ||
           key.ends_with("secret") || key.ends_with("secretkey") || key.ends_with("authorization") ||
           key == "cookie" || key == "setcookie" || key == "signature" || key == "xamzsignature" ||
           key == "sig" || key == "credential" || key == "xamzcredential";
}
} // namespace cymcp::detail
