#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include <string>

namespace cyui {
struct AssetItem {
    ItemId id = 0, parent = 0;
    std::string label, type;
    ImTextureID thumbnail = 0;
    bool folder = false, favorite = false;
};
struct AssetState {
    std::string query;
    ItemId folder = 0;
    std::vector<ItemId> selection;
    bool grid = true;
};
enum class AssetAction { none, open, context, favorite, rename, remove };
// Folders use parent 0 for the root, so give items nonzero IDs.
struct AssetResult {
    ItemId id = 0;
    AssetAction action = AssetAction::none;
    bool queryChanged = false;
};
CYUI_API AssetResult assetBrowser(const char *id, AssetState &, ArrayView<AssetItem>, ImVec2 size = {});
} // namespace cyui
