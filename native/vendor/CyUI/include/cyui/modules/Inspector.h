#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include <functional>
#include <limits>
#include <string>
#include <unordered_map>
#include <variant>

namespace cyui {
using PropertyValue = std::variant<bool, std::int64_t, double, std::string>;
struct Property {
    ItemId id = 0;
    std::string label, help;
    PropertyValue value;
    bool readOnly = false, password = false, multiline = false, bounded = false;
    double minimum = 0, maximum = 1;
    std::int64_t integerMinimum = std::numeric_limits<std::int64_t>::lowest(),
                 integerMaximum = std::numeric_limits<std::int64_t>::max();
    std::vector<std::string> choices;
};
struct PropertyEdit {
    ItemId id = 0;
    Edit<PropertyValue> edit;
};
struct PropertyDraft {
    PropertyValue value, before;
    bool active = false;
    std::string error;
    bool cancelled = false;
};
struct InspectorState {
    std::unordered_map<ItemId, PropertyDraft> drafts;
    std::uint64_t revision = 0;
};
using PropertyValidator = std::function<std::string(ItemId, const PropertyValue &)>;
struct InspectorOptions {
    const TextFilter* filter = nullptr;
    const char* filterContext = nullptr; // optional section/path, borrowed during the call
};
CYUI_API bool propertyMatches(const TextFilter&, const Property&, const char* context = nullptr);
CYUI_API std::vector<PropertyEdit> inspector(const char *id, InspectorState &, ArrayView<Property>,
                                    std::uint64_t revision = 0, const PropertyValidator & = {});
CYUI_API std::vector<PropertyEdit> inspector(const char *id, InspectorState &, ArrayView<Property>,
                                    std::uint64_t revision, const PropertyValidator &,
                                    const InspectorOptions&);
} // namespace cyui
