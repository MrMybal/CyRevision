#include "CyRevisionLoreEditorModule.h"

#include "Framework/Docking/TabManager.h"
#include "HAL/PlatformFileManager.h"
#include "HAL/PlatformProcess.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/Paths.h"
#include "Styling/CoreStyle.h"
#include "ToolMenus.h"
#include "Widgets/Docking/SDockTab.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "CyRevisionLoreEditor"

const FName FCyRevisionLoreEditorModule::LoreTabName(TEXT("CyRevisionLore"));

void FCyRevisionLoreEditorModule::StartupModule()
{
    FGlobalTabmanager::Get()->RegisterNomadTabSpawner(
        LoreTabName,
        FOnSpawnTab::CreateRaw(this, &FCyRevisionLoreEditorModule::SpawnLoreTab))
        .SetDisplayName(LOCTEXT("LoreTabTitle", "CyRevision Lore"))
        .SetMenuType(ETabSpawnerMenuType::Hidden);

    UToolMenus::RegisterStartupCallback(
        FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FCyRevisionLoreEditorModule::RegisterMenus));
}

void FCyRevisionLoreEditorModule::ShutdownModule()
{
    UToolMenus::UnRegisterStartupCallback(this);
    UToolMenus::UnregisterOwner(this);
    FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(LoreTabName);
    WorkspaceStatusText.Reset();
}

void FCyRevisionLoreEditorModule::RegisterMenus()
{
    FToolMenuOwnerScoped Owner(this);
    UToolMenu* Menu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Tools"));
    FToolMenuSection& Section = Menu->FindOrAddSection(TEXT("CyRevision"));
    Section.AddMenuEntry(
        TEXT("CyRevisionLorePanel"),
        LOCTEXT("OpenLorePanel", "CyRevision Lore (experimental)"),
        LOCTEXT("OpenLorePanelTip", "Inspect this project's Lore workspace and open its CyRevision management tools."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionLoreEditorModule::OpenLorePanel)));
}

void FCyRevisionLoreEditorModule::OpenLorePanel()
{
    FGlobalTabmanager::Get()->TryInvokeTab(LoreTabName);
}

TSharedRef<SDockTab> FCyRevisionLoreEditorModule::SpawnLoreTab(const FSpawnTabArgs& Args)
{
    return SNew(SDockTab)
        .TabRole(ETabRole::NomadTab)
        [
            SNew(SBorder)
            .Padding(12.0f)
            [
                SNew(SVerticalBox)
                + SVerticalBox::Slot().AutoHeight().Padding(0, 0, 0, 4)
                [
                    SNew(STextBlock)
                    .Text(LOCTEXT("Title", "CyRevision Lore Companion"))
                    .Font(FCoreStyle::GetDefaultFontStyle("Bold", 15))
                ]
                + SVerticalBox::Slot().AutoHeight().Padding(0, 0, 0, 10)
                [
                    SNew(STextBlock)
                    .Text(LOCTEXT("Experimental", "Experimental CyRevision companion — not Epic's official Unreal Lore provider."))
                    .AutoWrapText(true)
                ]
                + SVerticalBox::Slot().AutoHeight().Padding(0, 0, 0, 10)
                [
                    SAssignNew(WorkspaceStatusText, STextBlock)
                    .Text(BuildWorkspaceSummary())
                    .AutoWrapText(true)
                ]
                + SVerticalBox::Slot().AutoHeight()
                [
                    SNew(SHorizontalBox)
                    + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 8, 0)
                    [
                        SNew(SButton)
                        .Text(LOCTEXT("Refresh", "Refresh workspace status"))
                        .OnClicked_Lambda([this]()
                        {
                            RefreshWorkspaceStatus();
                            return FReply::Handled();
                        })
                    ]
                    + SHorizontalBox::Slot().AutoWidth()
                    [
                        SNew(SButton)
                        .Text(LOCTEXT("OpenCyRevision", "Open CyRevision"))
                        .OnClicked_Lambda([this]()
                        {
                            OpenCyRevision();
                            return FReply::Handled();
                        })
                    ]
                ]
                + SVerticalBox::Slot().AutoHeight().Padding(0, 12, 0, 0)
                [
                    SNew(STextBlock)
                    .Text(LOCTEXT("WritePolicy", "Status in this panel is filesystem-only. Scanning, staging, committing, pushing and syncing remain explicit operations in CyRevision."))
                    .AutoWrapText(true)
                ]
            ]
        ];
}

FText FCyRevisionLoreEditorModule::BuildWorkspaceSummary() const
{
    const FString ProjectRoot = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
    const FString LoreConfiguration = FPaths::Combine(ProjectRoot, TEXT(".lore"), TEXT("config.toml"));
    const bool bLoreWorkspace = FPlatformFileManager::Get().GetPlatformFile().FileExists(*LoreConfiguration);
    return bLoreWorkspace
        ? FText::Format(LOCTEXT("Detected", "Lore workspace detected.\nConfiguration: {0}\nProject: {1}"), FText::FromString(LoreConfiguration), FText::FromString(ProjectRoot))
        : FText::Format(LOCTEXT("NotDetected", "No .lore/config.toml was found under {0}. CyRevision will not initialize Lore automatically."), FText::FromString(ProjectRoot));
}

void FCyRevisionLoreEditorModule::RefreshWorkspaceStatus()
{
    if (WorkspaceStatusText.IsValid()) WorkspaceStatusText->SetText(BuildWorkspaceSummary());
}

void FCyRevisionLoreEditorModule::OpenCyRevision()
{
#if PLATFORM_WINDOWS
    const FString Executable(TEXT("CyRevision.Desktop.exe"));
#else
    const FString Executable(TEXT("CyRevision.Desktop"));
#endif
    const FString ProjectArgument = FString::Printf(TEXT("--project=\"%s\""), *FPaths::ConvertRelativePathToFull(FPaths::ProjectDir()));
    FPlatformProcess::CreateProc(*Executable, *ProjectArgument, true, false, false, nullptr, 0, nullptr, nullptr);
}

IMPLEMENT_MODULE(FCyRevisionLoreEditorModule, CyRevisionLoreEditor)

#undef LOCTEXT_NAMESPACE
