#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include <string>

namespace cyui {
struct Command {
    ItemId id = 0;
    std::string label, shortcut, category;
    bool enabled = true;
};
struct CommandState {
    std::string query;
    int selected = 0;
    bool wasOpen = false;
};
CYUI_API std::vector<std::size_t> rankCommands(ArrayView<Command>, const std::string &query);
// Returns a requested command ID, 0 for none (give commands nonzero IDs). The host validates and
// executes it.
CYUI_API ItemId commandPalette(const char *id, bool &open, CommandState &, ArrayView<Command>, ImVec2 size = {});
} // namespace cyui
