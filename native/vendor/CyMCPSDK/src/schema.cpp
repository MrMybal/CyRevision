#include <cmath>
#include <cymcp/core.hpp>
#include <set>
namespace cymcp {
namespace {
const std::set<std::string> keywords = {"type",
                                        "properties",
                                        "required",
                                        "additionalProperties",
                                        "items",
                                        "enum",
                                        "const",
                                        "minimum",
                                        "maximum",
                                        "exclusiveMinimum",
                                        "exclusiveMaximum",
                                        "minLength",
                                        "maxLength",
                                        "minItems",
                                        "maxItems",
                                        "uniqueItems",
                                        "minProperties",
                                        "maxProperties",
                                        "allOf",
                                        "anyOf",
                                        "oneOf",
                                        "not",
                                        "if",
                                        "then",
                                        "else",
                                        "$schema",
                                        "title",
                                        "description",
                                        "default",
                                        "examples",
                                        "readOnly",
                                        "writeOnly",
                                        "deprecated",
                                        "$comment"};
bool type_is(const Json& v, const std::string& type) {
    if (type == "object")
        return v.is_object();
    if (type == "array")
        return v.is_array();
    if (type == "string")
        return v.is_string();
    if (type == "boolean")
        return v.is_boolean();
    if (type == "null")
        return v.is_null();
    if (type == "number")
        return v.is_number();
    if (type == "integer")
        return v.is_number_integer() || (v.is_number_float() && std::isfinite(v.get<double>()) &&
                                         std::floor(v.get<double>()) == v.get<double>());
    return false;
}
void inspect(const Json& s, int depth) {
    if (depth > 48)
        throw Error("InvalidSchema", "Schema nesting exceeds limit");
    if (s.is_boolean())
        return;
    if (!s.is_object())
        throw Error("InvalidSchema", "Schema must be an object or boolean");
    for (auto it = s.begin(); it != s.end(); ++it) {
        const auto& k = it.key();
        const auto& v = it.value();
        if (!keywords.count(k))
            throw Error("UnsupportedSchema", "Unsupported schema keyword: " + k);
        if (k == "type") {
            auto types = v.is_array() ? v : Json::array({v});
            if (types.empty())
                throw Error("InvalidSchema", "Empty type list");
            for (const auto& t : types)
                if (!t.is_string() || !std::set<std::string>{"object", "array", "string", "number",
                                                             "integer", "boolean", "null"}
                                           .count(t.get<std::string>()))
                    throw Error("InvalidSchema", "Invalid type");
        } else if (k == "properties") {
            if (!v.is_object())
                throw Error("InvalidSchema", "properties must be an object");
            for (const auto& child : v)
                inspect(child, depth + 1);
        } else if (k == "additionalProperties" || k == "items" || k == "not" || k == "if" ||
                   k == "then" || k == "else")
            inspect(v, depth + 1);
        else if (k == "allOf" || k == "anyOf" || k == "oneOf") {
            if (!v.is_array() || v.empty())
                throw Error("InvalidSchema", "Combinator must contain schemas");
            for (const auto& child : v)
                inspect(child, depth + 1);
        } else if (k == "required") {
            if (!v.is_array())
                throw Error("InvalidSchema", "required must be an array");
            std::set<std::string> seen;
            for (const auto& name : v)
                if (!name.is_string() || !seen.insert(name.get<std::string>()).second)
                    throw Error("InvalidSchema", "Invalid required list");
        } else if (k == "enum" && (!v.is_array() || v.empty()))
            throw Error("InvalidSchema", "enum must be nonempty");
        else if (k == "minimum" || k == "maximum" || k == "exclusiveMinimum" ||
                 k == "exclusiveMaximum") {
            if (!v.is_number() || !std::isfinite(v.get<double>()))
                throw Error("InvalidSchema", "Numeric bound must be finite");
        } else if (k == "minLength" || k == "maxLength" || k == "minItems" || k == "maxItems" ||
                   k == "minProperties" || k == "maxProperties") {
            if (!v.is_number_integer() || v.get<long double>() < 0)
                throw Error("InvalidSchema", "Size bound must be a nonnegative integer");
        } else if (k == "uniqueItems" && !v.is_boolean())
            throw Error("InvalidSchema", "uniqueItems must be boolean");
    }
}
void verify(const Json& v, const Json& s, const std::string& path, int depth) {
    if (depth > 64)
        throw Error("InvalidParameters", "Value nesting exceeds limit");
    auto fail = [&](const std::string& message) {
        throw Error("InvalidParameters", path + ": " + message);
    };
    if (s.is_boolean()) {
        if (!s.get<bool>())
            fail("value forbidden");
        return;
    }
    if (s.contains("type")) {
        auto ts = s["type"].is_array() ? s["type"] : Json::array({s["type"]});
        bool found = false;
        for (const auto& t : ts)
            found |= type_is(v, t.get<std::string>());
        if (!found)
            fail("unexpected type");
    }
    if (s.contains("enum") && std::find(s["enum"].begin(), s["enum"].end(), v) == s["enum"].end())
        fail("value outside enum");
    if (s.contains("const") && v != s["const"])
        fail("constant mismatch");
    if (v.is_number()) {
        const auto x = v.get<long double>();
        if (!std::isfinite(x))
            fail("nonfinite number");
        if (s.contains("minimum") && x < s["minimum"].get<long double>())
            fail("below minimum");
        if (s.contains("maximum") && x > s["maximum"].get<long double>())
            fail("above maximum");
        if (s.contains("exclusiveMinimum") && x <= s["exclusiveMinimum"].get<long double>())
            fail("below exclusive minimum");
        if (s.contains("exclusiveMaximum") && x >= s["exclusiveMaximum"].get<long double>())
            fail("above exclusive maximum");
    }
    auto size_check = [&](std::size_t size, const char* min, const char* max) {
        if (s.contains(min) && size < s[min].get<std::size_t>())
            fail("size below minimum");
        if (s.contains(max) && size > s[max].get<std::size_t>())
            fail("size above maximum");
    };
    if (v.is_string()) {
        std::size_t length = 0;
        for (unsigned char c : v.get_ref<const std::string&>())
            if ((c & 0xc0) != 0x80)
                ++length;
        size_check(length, "minLength", "maxLength");
    }
    if (v.is_object()) {
        size_check(v.size(), "minProperties", "maxProperties");
        if (s.contains("required"))
            for (const auto& key : s["required"])
                if (!v.contains(key.get<std::string>()))
                    fail("missing required property " + key.get<std::string>());
        const auto& props = s.contains("properties") ? s["properties"] : Json::object();
        for (auto it = v.begin(); it != v.end(); ++it) {
            if (props.contains(it.key()))
                verify(it.value(), props[it.key()], path + "/" + it.key(), depth + 1);
            else if (s.contains("additionalProperties"))
                verify(it.value(), s["additionalProperties"], path + "/" + it.key(), depth + 1);
        }
    }
    if (v.is_array()) {
        size_check(v.size(), "minItems", "maxItems");
        if (s.contains("items"))
            for (std::size_t i = 0; i < v.size(); ++i)
                verify(v[i], s["items"], path + "/" + std::to_string(i), depth + 1);
        if (s.value("uniqueItems", false)) {
            std::set<Json> seen;
            for (const auto& item : v)
                if (!seen.insert(item).second)
                    fail("duplicate array item");
        }
    }
    auto matches = [&](const Json& sub) {
        try {
            verify(v, sub, path, depth + 1);
            return true;
        } catch (const Error& e) {
            if (e.code != "InvalidParameters")
                throw;
            return false;
        }
    };
    for (auto name : {"allOf", "anyOf", "oneOf"})
        if (s.contains(name)) {
            std::size_t n = 0;
            for (const auto& sub : s[name])
                n += matches(sub) ? 1 : 0;
            if ((std::string(name) == "allOf" && n != s[name].size()) ||
                (std::string(name) == "anyOf" && n == 0) ||
                (std::string(name) == "oneOf" && n != 1))
                fail("schema branch mismatch");
        }
    if (s.contains("not") && matches(s["not"]))
        fail("excluded schema matched");
    if (s.contains("if")) {
        const char* branch = matches(s["if"]) ? "then" : "else";
        if (s.contains(branch))
            verify(v, s[branch], path, depth + 1);
    }
}
} // namespace
void check_schema(const Json& s) {
    inspect(s, 0);
}
void validate(const Json& v, const Json& s) {
    check_schema(s);
    verify(v, s, "$", 0);
}
} // namespace cymcp
