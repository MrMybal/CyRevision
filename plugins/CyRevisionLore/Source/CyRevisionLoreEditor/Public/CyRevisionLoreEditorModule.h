#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"

class SDockTab;
class STextBlock;
class FSpawnTabArgs;

class FCyRevisionLoreEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    static const FName LoreTabName;

    void RegisterMenus();
    void OpenLorePanel();
    void OpenCyRevision();
    void RefreshWorkspaceStatus();
    TSharedRef<SDockTab> SpawnLoreTab(const FSpawnTabArgs& Args);
    FText BuildWorkspaceSummary() const;

    TSharedPtr<STextBlock> WorkspaceStatusText;
};
