#include "CyRevisionEditorModule.h"
#include "CyRevisionEngineCompatibility.h"
#include "CyRevisionRevisionTools.h"
#include "CyRevisionSourceControlProvider.h"
#include "CyRevisionSwarmTools.h"

#include "AssetRegistry/AssetData.h"
#include "ContentBrowserMenuContexts.h"
#include "Dom/JsonObject.h"
#include "Framework/Application/SlateApplication.h"
#include "Framework/Commands/UIAction.h"
#include "Features/IModularFeatures.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformProcess.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/DateTime.h"
#include "Misc/FileHelper.h"
#include "Misc/Guid.h"
#include "Misc/MessageDialog.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "Misc/SecureHash.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Styling/SlateStyle.h"
#include "Styling/SlateStyleRegistry.h"
#include "ToolMenus.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/Images/SImage.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/SWindow.h"
#include "Widgets/Text/STextBlock.h"
#include "Widgets/Views/SListView.h"
#include "Widgets/Views/STableRow.h"

#define LOCTEXT_NAMESPACE "FCyRevisionEditorModule"

namespace
{
constexpr int32 ReservationSchemaVersion = 1;
constexpr double DefaultReservationMinutes = 30.0;
const TCHAR* ToolbarEnabledKey = TEXT("ShowToolbarButton");
const TCHAR* ToolbarLabelKey = TEXT("ShowToolbarLabel");

FToolMenuSection& FindOrAddLabeledSection(UToolMenu* Menu, const FName Name, const FText& Label)
{
    FToolMenuSection& Section = Menu->FindOrAddSection(Name);
    Section.Label = Label;
    return Section;
}

bool GetCyRevisionBool(const TCHAR* Key, bool DefaultValue)
{
    bool Value = DefaultValue;
    GConfig->GetBool(TEXT("CyRevisionInterface"), Key, Value, GEditorPerProjectIni);
    return Value;
}

void SetCyRevisionBool(const TCHAR* Key, bool Value)
{
    GConfig->SetBool(TEXT("CyRevisionInterface"), Key, Value, GEditorPerProjectIni);
    GConfig->Flush(false, GEditorPerProjectIni);
}

struct FCyRevisionPresenceLocation
{
    FString Directory;
    FGuid ProjectId;
    bool bShared = false;
    FString Detail;
};

FString NormalizeDirectory(FString Path)
{
    Path = FPaths::ConvertRelativePathToFull(Path);
    FPaths::NormalizeDirectoryName(Path);
    FPaths::CollapseRelativeDirectories(Path);
    return Path;
}

FString GetConfigurationRoot()
{
    FString Root = FPlatformMisc::GetEnvironmentVariable(TEXT("APPDATA"));
    if (!Root.IsEmpty())
    {
        return Root;
    }

    FString Home = FPlatformMisc::GetEnvironmentVariable(TEXT("HOME"));
    FString Xdg = FPlatformMisc::GetEnvironmentVariable(TEXT("XDG_CONFIG_HOME"));
    return !Xdg.IsEmpty() ? Xdg : FPaths::Combine(Home, TEXT(".config"));
}

FString GetLocalDataRoot()
{
    FString Root = FPlatformMisc::GetEnvironmentVariable(TEXT("LOCALAPPDATA"));
    if (!Root.IsEmpty())
    {
        return Root;
    }

    FString Home = FPlatformMisc::GetEnvironmentVariable(TEXT("HOME"));
    FString Xdg = FPlatformMisc::GetEnvironmentVariable(TEXT("XDG_DATA_HOME"));
    return !Xdg.IsEmpty() ? Xdg : FPaths::Combine(Home, TEXT(".local"), TEXT("share"));
}

FGuid GetFallbackProjectId()
{
    FString StoredId;
    GConfig->GetString(TEXT("CyRevision"), TEXT("AdvisoryProjectId"), StoredId, GEditorPerProjectIni);
    FGuid ProjectId;
    if (!FGuid::Parse(StoredId, ProjectId))
    {
        ProjectId = FGuid::NewGuid();
        GConfig->SetString(
            TEXT("CyRevision"),
            TEXT("AdvisoryProjectId"),
            *ProjectId.ToString(EGuidFormats::DigitsWithHyphens),
            GEditorPerProjectIni);
        GConfig->Flush(false, GEditorPerProjectIni);
    }
    return ProjectId;
}

FCyRevisionPresenceLocation ResolvePresenceLocation()
{
    FString ConfiguredDirectory;
    GConfig->GetString(TEXT("CyRevision"), TEXT("AdvisoryPresenceDirectory"), ConfiguredDirectory, GEditorPerProjectIni);
    if (!ConfiguredDirectory.IsEmpty())
    {
        ConfiguredDirectory.ReplaceInline(TEXT("{ProjectDir}"), *FPaths::ProjectDir(), ESearchCase::IgnoreCase);
    }

    const FString CurrentProject = NormalizeDirectory(FPaths::ProjectDir());
    const FString CatalogPath = FPaths::Combine(GetConfigurationRoot(), TEXT("CyRevision"), TEXT("projects.json"));
    FString CatalogJson;
    if (FFileHelper::LoadFileToString(CatalogJson, *CatalogPath))
    {
        TArray<TSharedPtr<FJsonValue>> Projects;
        TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(CatalogJson);
        if (FJsonSerializer::Deserialize(Reader, Projects))
        {
            for (const TSharedPtr<FJsonValue>& ProjectValue : Projects)
            {
                const TSharedPtr<FJsonObject> Project = ProjectValue.IsValid() ? ProjectValue->AsObject() : nullptr;
                if (!Project.IsValid())
                {
                    continue;
                }

                FString RootPath;
                FString ProjectIdText;
                if (!Project->TryGetStringField(TEXT("rootPath"), RootPath) ||
                    !Project->TryGetStringField(TEXT("id"), ProjectIdText) ||
                    !NormalizeDirectory(RootPath).Equals(CurrentProject, ESearchCase::IgnoreCase))
                {
                    continue;
                }

                FGuid ProjectId;
                if (!FGuid::Parse(ProjectIdText, ProjectId))
                {
                    continue;
                }

                if (!ConfiguredDirectory.IsEmpty())
                {
                    return {
                        NormalizeDirectory(ConfiguredDirectory),
                        ProjectId,
                        true,
                        TEXT("Dossier de présence configuré manuellement pour ce projet CyRevision")
                    };
                }

                bool bGitEnabled = false;
                const TSharedPtr<FJsonObject>* Features = nullptr;
                if (Project->TryGetObjectField(TEXT("features"), Features) && Features && Features->IsValid())
                {
                    (*Features)->TryGetBoolField(TEXT("gitEnabled"), bGitEnabled);
                }

                if (bGitEnabled)
                {
                    return {
                        NormalizeDirectory(FPaths::Combine(
                            GetLocalDataRoot(),
                            TEXT("CyRevision"),
                            TEXT("git-exchange"),
                            ProjectId.ToString(EGuidFormats::Digits),
                            TEXT("presence"))),
                        ProjectId,
                        true,
                        TEXT("Zone d'échange CyRevision partagée par Syncthing quand Sync est actif")
                    };
                }

                return {
                    NormalizeDirectory(FPaths::Combine(CurrentProject, TEXT(".cyrevision"), TEXT("presence"))),
                    ProjectId,
                    true,
                    TEXT("Présence incluse dans le dossier synchronisé du projet")
                };
            }
        }
    }

    if (!ConfiguredDirectory.IsEmpty())
    {
        return {
            NormalizeDirectory(ConfiguredDirectory),
            GetFallbackProjectId(),
            false,
            TEXT("Dossier configuré, mais le projet doit être ouvert dans CyRevision pour partager le même identifiant")
        };
    }

    return {
        NormalizeDirectory(FPaths::Combine(CurrentProject, TEXT("Saved"), TEXT("CyRevision"), TEXT("Presence"))),
        GetFallbackProjectId(),
        false,
        TEXT("Mode local : ouvrez d'abord ce projet dans CyRevision pour partager les signalements")
    };
}

void GetOwnerIdentity(FGuid& OutOwnerId, FString& OutDisplayName)
{
    FString OwnerIdText;
    GConfig->GetString(TEXT("CyRevision"), TEXT("AdvisoryOwnerId"), OwnerIdText, GEditorPerProjectIni);
    if (!FGuid::Parse(OwnerIdText, OutOwnerId))
    {
        OutOwnerId = FGuid::NewGuid();
        GConfig->SetString(
            TEXT("CyRevision"),
            TEXT("AdvisoryOwnerId"),
            *OutOwnerId.ToString(EGuidFormats::DigitsWithHyphens),
            GEditorPerProjectIni);
    }

    GConfig->GetString(TEXT("CyRevision"), TEXT("AdvisoryDisplayName"), OutDisplayName, GEditorPerProjectIni);
    if (OutDisplayName.IsEmpty())
    {
        OutDisplayName = FPlatformProcess::UserName(false);
    }
    GConfig->Flush(false, GEditorPerProjectIni);
}

double GetReservationMinutes()
{
    double Minutes = DefaultReservationMinutes;
    GConfig->GetDouble(TEXT("CyRevision"), TEXT("AdvisoryExpirationMinutes"), Minutes, GEditorPerProjectIni);
    return FMath::Clamp(Minutes, 1.0, 10080.0);
}

FString GetReservationDirectory(const FCyRevisionPresenceLocation& Location)
{
    return FPaths::Combine(Location.Directory, TEXT("reservations"));
}

FString GetAssetRelativeFilename(const FAssetData& Asset)
{
    FString PackageFilename;
    FPackageName::DoesPackageExist(Asset.PackageName.ToString(), &PackageFilename);
    if (PackageFilename.IsEmpty())
    {
        return FString();
    }

    FString Relative = FPaths::ConvertRelativePathToFull(PackageFilename);
    if (FPaths::MakePathRelativeTo(Relative, *FPaths::ProjectDir()))
    {
        FPaths::NormalizeFilename(Relative);
        return Relative;
    }
    return PackageFilename;
}

FString GetReservationFilename(const FCyRevisionPresenceLocation& Location, const FGuid& OwnerId, const FString& AssetPath)
{
    const FString Key = FString::Printf(
        TEXT("%s\n%s\n%s"),
        *Location.ProjectId.ToString(EGuidFormats::Digits),
        *OwnerId.ToString(EGuidFormats::Digits),
        *AssetPath.ToUpper());
    return FPaths::Combine(GetReservationDirectory(Location), FMD5::HashAnsiString(*Key).ToLower() + TEXT(".json"));
}

bool WriteJsonAtomically(const FString& Path, const TSharedRef<FJsonObject>& Object)
{
    FString Json;
    const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Json);
    if (!FJsonSerializer::Serialize(Object, Writer))
    {
        return false;
    }

    IFileManager::Get().MakeDirectory(*FPaths::GetPath(Path), true);
    const FString TemporaryPath = Path + TEXT(".") + FGuid::NewGuid().ToString(EGuidFormats::Digits) + TEXT(".tmp");
    if (!FFileHelper::SaveStringToFile(Json, *TemporaryPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        return false;
    }

    if (!IFileManager::Get().Move(*Path, *TemporaryPath, true, true, false, true))
    {
        IFileManager::Get().Delete(*TemporaryPath, false, true, true);
        return false;
    }
    return true;
}

bool ReadJsonFile(const FString& Path, TSharedPtr<FJsonObject>& OutObject)
{
    FString Json;
    if (!FFileHelper::LoadFileToString(Json, *Path))
    {
        return false;
    }

    const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
    return FJsonSerializer::Deserialize(Reader, OutObject) && OutObject.IsValid();
}
}

struct FCyRevisionSoftReservation
{
    FString SourcePath;
    FString ReservationId;
    FString ProjectId;
    FString AssetPath;
    FString RelativePath;
    FString OwnerId;
    FString OwnerName;
    FString MachineName;
    FString Note;
    FDateTime CreatedAtUtc;
    FDateTime UpdatedAtUtc;
    FDateTime ExpiresAtUtc;
    bool bExpired = false;
};

struct FCyRevisionCollaborationItem
{
    FString Id;
    FString File;
    FString Owner;
    FString Updated;
    bool bMine = false;
};

namespace
{
TArray<TSharedPtr<FCyRevisionSoftReservation>> ReadReservations(const FCyRevisionPresenceLocation& Location)
{
    TArray<TSharedPtr<FCyRevisionSoftReservation>> Result;
    TArray<FString> Files;
    const FString Directory = GetReservationDirectory(Location);
    IFileManager::Get().FindFiles(Files, *FPaths::Combine(Directory, TEXT("*.json")), true, false);
    const FDateTime Now = FDateTime::UtcNow();

    for (const FString& File : Files)
    {
        const FString FullPath = FPaths::Combine(Directory, File);
        TSharedPtr<FJsonObject> Json;
        if (!ReadJsonFile(FullPath, Json))
        {
            continue;
        }

        double SchemaVersion = 0;
        FString ProjectId;
        FString ReservationId;
        FString AssetPath;
        FString OwnerId;
        FString OwnerName;
        FString CreatedAt;
        FString UpdatedAt;
        FString ExpiresAt;
        if (!Json->TryGetNumberField(TEXT("schemaVersion"), SchemaVersion) ||
            static_cast<int32>(SchemaVersion) != ReservationSchemaVersion ||
            !Json->TryGetStringField(TEXT("projectId"), ProjectId) ||
            !Json->TryGetStringField(TEXT("reservationId"), ReservationId) ||
            !Json->TryGetStringField(TEXT("assetPath"), AssetPath) ||
            !Json->TryGetStringField(TEXT("ownerId"), OwnerId) ||
            !Json->TryGetStringField(TEXT("ownerName"), OwnerName) ||
            !Json->TryGetStringField(TEXT("createdAtUtc"), CreatedAt) ||
            !Json->TryGetStringField(TEXT("updatedAtUtc"), UpdatedAt) ||
            !Json->TryGetStringField(TEXT("expiresAtUtc"), ExpiresAt))
        {
            continue;
        }

        FGuid ParsedProjectId;
        FDateTime Created;
        FDateTime Updated;
        FDateTime Expires;
        if (!FGuid::Parse(ProjectId, ParsedProjectId) || ParsedProjectId != Location.ProjectId ||
            !FDateTime::ParseIso8601(*CreatedAt, Created) ||
            !FDateTime::ParseIso8601(*UpdatedAt, Updated) ||
            !FDateTime::ParseIso8601(*ExpiresAt, Expires))
        {
            continue;
        }

        TSharedPtr<FCyRevisionSoftReservation> Reservation = MakeShared<FCyRevisionSoftReservation>();
        Reservation->SourcePath = FullPath;
        Reservation->ReservationId = ReservationId;
        Reservation->ProjectId = ProjectId;
        Reservation->AssetPath = AssetPath;
        Json->TryGetStringField(TEXT("relativePath"), Reservation->RelativePath);
        Reservation->OwnerId = OwnerId;
        Reservation->OwnerName = OwnerName;
        Json->TryGetStringField(TEXT("machineName"), Reservation->MachineName);
        Json->TryGetStringField(TEXT("note"), Reservation->Note);
        Reservation->CreatedAtUtc = Created;
        Reservation->UpdatedAtUtc = Updated;
        Reservation->ExpiresAtUtc = Expires;
        Reservation->bExpired = Expires <= Now;
        Result.Add(Reservation);
    }

    Result.Sort([](const TSharedPtr<FCyRevisionSoftReservation>& Left, const TSharedPtr<FCyRevisionSoftReservation>& Right)
    {
        if (Left->bExpired != Right->bExpired)
        {
            return !Left->bExpired;
        }
        const int32 AssetComparison = Left->AssetPath.Compare(Right->AssetPath, ESearchCase::IgnoreCase);
        return AssetComparison == 0
            ? Left->OwnerName.Compare(Right->OwnerName, ESearchCase::IgnoreCase) < 0
            : AssetComparison < 0;
    });
    return Result;
}

TSharedRef<FJsonObject> MakeReservationJson(
    const FCyRevisionPresenceLocation& Location,
    const FString& ReservationId,
    const FAssetData& Asset,
    const FGuid& OwnerId,
    const FString& OwnerName,
    const FDateTime& CreatedAt,
    const FDateTime& UpdatedAt,
    const FDateTime& ExpiresAt)
{
    TSharedRef<FJsonObject> Json = MakeShared<FJsonObject>();
    Json->SetNumberField(TEXT("schemaVersion"), ReservationSchemaVersion);
    Json->SetStringField(TEXT("reservationId"), ReservationId);
    Json->SetStringField(TEXT("projectId"), Location.ProjectId.ToString(EGuidFormats::DigitsWithHyphens));
    Json->SetStringField(TEXT("assetPath"), Asset.PackageName.ToString());
    Json->SetStringField(TEXT("relativePath"), GetAssetRelativeFilename(Asset));
    Json->SetStringField(TEXT("ownerId"), OwnerId.ToString(EGuidFormats::DigitsWithHyphens));
    Json->SetStringField(TEXT("ownerName"), OwnerName);
    Json->SetStringField(TEXT("machineName"), FPlatformProcess::ComputerName());
    Json->SetStringField(TEXT("note"), TEXT("Unreal Editor"));
    Json->SetStringField(TEXT("createdAtUtc"), CreatedAt.ToIso8601());
    Json->SetStringField(TEXT("updatedAtUtc"), UpdatedAt.ToIso8601());
    Json->SetStringField(TEXT("expiresAtUtc"), ExpiresAt.ToIso8601());
    return Json;
}
}

void FCyRevisionEditorModule::StartupModule()
{
    RegisterStyle();
    SourceControlProvider = MakeUnique<FCyRevisionSourceControlProvider>();
    IModularFeatures::Get().RegisterModularFeature(TEXT("SourceControl"), SourceControlProvider.Get());
    RevisionTools = MakeUnique<FCyRevisionRevisionTools>();
    SwarmTools = MakeUnique<FCyRevisionSwarmTools>();
    UToolMenus::RegisterStartupCallback(
        FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FCyRevisionEditorModule::RegisterMenus));
#if CYREVISION_UE5
    HeartbeatHandle = FTSTicker::GetCoreTicker().AddTicker(
#else
    HeartbeatHandle = FTicker::GetCoreTicker().AddTicker(
#endif
        FTickerDelegate::CreateRaw(this, &FCyRevisionEditorModule::HandleHeartbeat),
        60.0f);
}

void FCyRevisionEditorModule::ShutdownModule()
{
    if (const TSharedPtr<SWindow> Window = CollaborationWindow.Pin())
    {
        Window->RequestDestroyWindow();
    }
    CollaborationList.Reset();
    CollaborationStatusText.Reset();
    CollaborationWindow.Reset();
    if (SourceControlProvider)
    {
        SourceControlProvider->Close();
        IModularFeatures::Get().UnregisterModularFeature(TEXT("SourceControl"), SourceControlProvider.Get());
        SourceControlProvider.Reset();
    }
    if (RevisionTools)
    {
        RevisionTools->Shutdown();
        RevisionTools.Reset();
    }
    if (SwarmTools)
    {
        SwarmTools->Shutdown();
        SwarmTools.Reset();
    }
    if (HeartbeatHandle.IsValid())
    {
#if CYREVISION_UE5
        FTSTicker::GetCoreTicker().RemoveTicker(HeartbeatHandle);
#else
        FTicker::GetCoreTicker().RemoveTicker(HeartbeatHandle);
#endif
        HeartbeatHandle.Reset();
    }
    UToolMenus::UnRegisterStartupCallback(this);
    UToolMenus::UnregisterOwner(this);
    UnregisterStyle();
    ReservationList.Reset();
    ReservationStatusText.Reset();
    ReservationWindow.Reset();
}

void FCyRevisionEditorModule::RegisterStyle()
{
    if (Style.IsValid())
    {
        return;
    }

    const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("CyRevisionUnreal"));
    if (!Plugin.IsValid())
    {
        return;
    }

    Style = MakeShared<FSlateStyleSet>(TEXT("CyRevisionUnrealStyle"));
    Style->SetContentRoot(FPaths::Combine(Plugin->GetBaseDir(), TEXT("Resources")));
    Style->Set(
        TEXT("CyRevision.Icon"),
        new FSlateImageBrush(Style->RootToContentDir(TEXT("Icon128"), TEXT(".png")), FVector2D(20.0f, 20.0f)));
    Style->Set(
        TEXT("CyRevision.Icon.Small"),
        new FSlateImageBrush(Style->RootToContentDir(TEXT("Icon128"), TEXT(".png")), FVector2D(16.0f, 16.0f)));
    FSlateStyleRegistry::RegisterSlateStyle(*Style);
}

void FCyRevisionEditorModule::UnregisterStyle()
{
    if (Style.IsValid())
    {
        FSlateStyleRegistry::UnRegisterSlateStyle(*Style);
        Style.Reset();
    }
}

void FCyRevisionEditorModule::RegisterMenus()
{
    FToolMenuOwnerScoped OwnerScoped(this);
    const FSlateIcon Icon = Style.IsValid()
        ? FSlateIcon(Style->GetStyleSetName(), TEXT("CyRevision.Icon"))
        : FSlateIcon();

    UToolMenu* ToolbarPopup = UToolMenus::Get()->RegisterMenu(TEXT("CyRevision.ToolbarPopup"));
    BuildCyRevisionMenu(ToolbarPopup);

    UToolMenu* ToolsMenu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Tools"));
    FToolMenuSection& Section = FindOrAddLabeledSection(
        ToolsMenu,
        TEXT("CyRevision"),
        LOCTEXT("CyRevisionToolsSection", "CYREVISION"));
    Section.AddSubMenu(
        TEXT("CyRevisionTools"),
        LOCTEXT("CyRevisionToolsLabel", "CyRevision"),
        LOCTEXT("CyRevisionToolsTooltip", "Revision control, LFS locks, work-in-progress presence, and project network tools."),
        FNewToolMenuDelegate::CreateRaw(this, &FCyRevisionEditorModule::BuildCyRevisionMenu),
        false,
        Icon);

    UToolMenu* ToolbarMenu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.LevelEditorToolBar.AssetsToolBar"));
    FToolMenuSection& ToolbarSection = ToolbarMenu->FindOrAddSection(TEXT("CyRevision"));
    ToolbarSection.AddDynamicEntry(
        TEXT("CyRevisionToolbarDynamic"),
        FNewToolMenuSectionDelegate::CreateRaw(this, &FCyRevisionEditorModule::AddToolbarEntry));

    UToolMenu* AssetMenu = UToolMenus::Get()->ExtendMenu(TEXT("ContentBrowser.AssetContextMenu"));
    FToolMenuSection& AssetSection = AssetMenu->FindOrAddSection(TEXT("CyRevision"));
    AssetSection.AddDynamicEntry(
        TEXT("CyRevisionAssetActions"),
        FNewToolMenuSectionDelegate::CreateRaw(this, &FCyRevisionEditorModule::AddAssetContextEntries));
}

void FCyRevisionEditorModule::BuildCyRevisionMenu(UToolMenu* Menu)
{
    if (!Menu)
    {
        return;
    }

    const FSlateIcon Icon = Style.IsValid()
        ? FSlateIcon(Style->GetStyleSetName(), TEXT("CyRevision.Icon.Small"))
        : FSlateIcon();

    FToolMenuSection& General = FindOrAddLabeledSection(
        Menu,
        TEXT("General"),
        LOCTEXT("CyRevisionGeneralSection", "REVISION CONTROL"));
    General.AddMenuEntry(
        TEXT("OpenCyRevision"),
        LOCTEXT("OpenCyRevisionLabel", "Open in CyRevision"),
        LOCTEXT("OpenCyRevisionTooltip", "Opens the external CyRevision client for the current Unreal project."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::OpenCyRevision)));
    General.AddMenuEntry(
        TEXT("ShowCyRevisionDashboard"),
        LOCTEXT("ShowRevisionDashboardLabel", "Revision dashboard"),
        LOCTEXT("ShowRevisionDashboardTooltip", "Manage Git revisions inside Unreal Editor. CyRevision is optional."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ShowRevisionDashboard)));
    General.AddMenuEntry(
        TEXT("TestCyRevisionConnection"),
        LOCTEXT("TestConnectionLabel", "Test CyRevision connection"),
        LOCTEXT("TestConnectionTooltip", "Checks the authenticated local connection to CyRevision."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::TestCyRevisionConnection)));

    FToolMenuSection& Collaboration = FindOrAddLabeledSection(
        Menu,
        TEXT("Collaboration"),
        LOCTEXT("CyRevisionCollaborationSection", "LOCKS & PRESENCE"));
    Collaboration.AddMenuEntry(
        TEXT("ShowCyRevisionAllLocks"),
        LOCTEXT("ShowAllLocksLabel", "All Git LFS locks"),
        LOCTEXT("ShowAllLocksTooltip", "Shows every lock reported by the project Git LFS server."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this]()
        {
            ShowCollaborationWindow(ECyRevisionCollaborationView::AllLocks);
        })));
    Collaboration.AddMenuEntry(
        TEXT("ShowCyRevisionMyLocks"),
        LOCTEXT("ShowMyLocksLabel", "My Git LFS locks"),
        LOCTEXT("ShowMyLocksTooltip", "Shows only locks owned by the current Git LFS identity."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this]()
        {
            ShowCollaborationWindow(ECyRevisionCollaborationView::MyLocks);
        })));
    Collaboration.AddMenuEntry(
        TEXT("ShowCyRevisionReservations"),
        LOCTEXT("ShowReservationsLabel", "Work in progress"),
        LOCTEXT("ShowReservationsTooltip", "Shows who is working on each asset without checkout or locking."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ShowReservations)));

    FToolMenuSection& Network = FindOrAddLabeledSection(
        Menu,
        TEXT("Network"),
        LOCTEXT("CyRevisionNetworkSection", "DISTRIBUTED TOOLS"));
    Network.AddMenuEntry(
        TEXT("ShowCyRevisionSwarm"),
        LOCTEXT("ShowSwarmLabel", "Swarm over VPN"),
        LOCTEXT("ShowSwarmTooltip", "Configure, launch and test Unreal Swarm over the project WireGuard network."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this]()
        {
            if (SwarmTools)
            {
                SwarmTools->Show();
            }
        })));

    FToolMenuSection& Interface = FindOrAddLabeledSection(
        Menu,
        TEXT("Interface"),
        LOCTEXT("CyRevisionInterfaceSection", "INTERFACE"));
    Interface.AddMenuEntry(
        TEXT("CyRevisionToggleToolbar"),
        LOCTEXT("ToggleToolbarLabel", "Show CyRevision toolbar button"),
        LOCTEXT("ToggleToolbarTooltip", "Shows or hides the CyRevision button in the main Unreal toolbar."),
        Icon,
        FUIAction(
            FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ToggleToolbarEnabled),
            FCanExecuteAction(),
            FIsActionChecked::CreateRaw(this, &FCyRevisionEditorModule::IsToolbarEnabled)),
        EUserInterfaceActionType::ToggleButton);
    Interface.AddMenuEntry(
        TEXT("CyRevisionToggleToolbarLabel"),
        LOCTEXT("ToggleToolbarNameLabel", "Show name beside toolbar icon"),
        LOCTEXT("ToggleToolbarNameTooltip", "Shows or hides the CyRevision name beside its toolbar icon."),
        Icon,
        FUIAction(
            FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ToggleToolbarLabel),
            FCanExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::IsToolbarEnabled),
            FIsActionChecked::CreateRaw(this, &FCyRevisionEditorModule::IsToolbarLabelVisible)),
        EUserInterfaceActionType::ToggleButton);
}

void FCyRevisionEditorModule::AddToolbarEntry(FToolMenuSection& Section)
{
    if (!IsToolbarEnabled())
    {
        return;
    }

    const FSlateIcon Icon = Style.IsValid()
        ? FSlateIcon(Style->GetStyleSetName(), TEXT("CyRevision.Icon"))
        : FSlateIcon();
    const FText Label = IsToolbarLabelVisible()
        ? LOCTEXT("ToolbarCyRevisionLabel", "CyRevision")
        : FText::GetEmpty();
    FToolMenuEntry Entry = FToolMenuEntry::InitComboButton(
        TEXT("CyRevisionToolbarButton"),
        FUIAction(),
        FOnGetContent::CreateLambda([]()
        {
            return UToolMenus::Get()->GenerateWidget(
                TEXT("CyRevision.ToolbarPopup"),
                FToolMenuContext());
        }),
        Label,
        LOCTEXT("ToolbarCyRevisionTooltip", "Open CyRevision revision, lock, work-in-progress, and network tools."),
        Icon,
        false);
    Section.AddEntry(MoveTemp(Entry));
}

void FCyRevisionEditorModule::AddAssetContextEntries(FToolMenuSection& Section)
{
    const UContentBrowserAssetContextMenuContext* Context = Section.FindContext<UContentBrowserAssetContextMenuContext>();
    if (!Context)
    {
        return;
    }

#if CYREVISION_UE5
    const TArray<FAssetData> SelectedAssets = Context->SelectedAssets;
#else
    TArray<FAssetData> SelectedAssets;
    for (const TWeakObjectPtr<UObject>& SelectedObject : Context->SelectedObjects)
    {
        if (const UObject* Object = SelectedObject.Get())
        {
            SelectedAssets.Emplace(Object);
        }
    }
#endif
    if (SelectedAssets.Num() == 0)
    {
        return;
    }
    const FSlateIcon Icon = Style.IsValid()
        ? FSlateIcon(Style->GetStyleSetName(), TEXT("CyRevision.Icon.Small"))
        : FSlateIcon();
    Section.AddSubMenu(
        TEXT("CyRevisionAssetSubMenu"),
        LOCTEXT("CyRevisionAssetSubMenuLabel", "CyRevision"),
        LOCTEXT("CyRevisionAssetSubMenuTooltip", "Revision, LFS lock, and work-in-progress actions for the selected assets."),
        FNewToolMenuDelegate::CreateLambda([this, SelectedAssets](UToolMenu* Menu)
        {
            AddAssetContextSubMenu(Menu, SelectedAssets);
        }),
        false,
        Icon);
}

void FCyRevisionEditorModule::AddAssetContextSubMenu(UToolMenu* Menu, TArray<FAssetData> SelectedAssets)
{
    if (!Menu)
    {
        return;
    }

    const FSlateIcon Icon = Style.IsValid()
        ? FSlateIcon(Style->GetStyleSetName(), TEXT("CyRevision.Icon.Small"))
        : FSlateIcon();
    FToolMenuSection& Presence = FindOrAddLabeledSection(
        Menu,
        TEXT("Presence"),
        LOCTEXT("AssetPresenceSection", "WORK IN PROGRESS"));
    Presence.AddMenuEntry(
        TEXT("CyRevisionMarkInProgress"),
        LOCTEXT("MarkInProgressLabel", "Report: I am working on this"),
        LOCTEXT(
            "MarkInProgressTooltip",
            "Creates an advisory marker. The asset stays editable and no checkout or LFS lock is created."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this, SelectedAssets]() { MarkAssetsInProgress(SelectedAssets); })));
    Presence.AddMenuEntry(
        TEXT("CyRevisionReleaseAdvisory"),
        LOCTEXT("ReleaseAdvisoryLabel", "Release my advisory"),
        LOCTEXT("ReleaseAdvisoryTooltip", "Removes only your own advisory markers for the selected assets."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this, SelectedAssets]() { ReleaseAssets(SelectedAssets); })));
    Presence.AddMenuEntry(
        TEXT("CyRevisionViewAdvisories"),
        LOCTEXT("ViewAdvisoriesLabel", "View work in progress"),
        LOCTEXT("ViewAdvisoriesTooltip", "Shows active and expired advisory markers for this project."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ShowReservations)));

    FToolMenuSection& Locks = FindOrAddLabeledSection(
        Menu,
        TEXT("Locks"),
        LOCTEXT("AssetLocksSection", "GIT LFS LOCKS"));
    Locks.AddMenuEntry(
        TEXT("CyRevisionLockSelected"),
        LOCTEXT("LockSelectedLabel", "Lock selected file(s)"),
        LOCTEXT("LockSelectedTooltip", "Creates normal Git LFS locks for the selected asset files."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this, SelectedAssets]()
        {
            SetSelectedAssetsLfsLock(SelectedAssets, true);
        })));
    Locks.AddMenuEntry(
        TEXT("CyRevisionUnlockSelected"),
        LOCTEXT("UnlockSelectedLabel", "Unlock selected file(s)"),
        LOCTEXT("UnlockSelectedTooltip", "Releases your normal Git LFS locks for the selected asset files. It never force-unlocks another user."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this, SelectedAssets]()
        {
            SetSelectedAssetsLfsLock(SelectedAssets, false);
        })));
    Locks.AddMenuEntry(
        TEXT("CyRevisionViewAllLocks"),
        LOCTEXT("ViewAllLocksLabel", "View all project locks"),
        LOCTEXT("ViewAllLocksTooltip", "Shows every lock reported by the Git LFS server."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this]()
        {
            ShowCollaborationWindow(ECyRevisionCollaborationView::AllLocks);
        })));
    Locks.AddMenuEntry(
        TEXT("CyRevisionViewMyLocks"),
        LOCTEXT("ViewMyLocksLabel", "View my locks"),
        LOCTEXT("ViewMyLocksTooltip", "Shows locks owned by the current Git LFS identity."),
        Icon,
        FUIAction(FExecuteAction::CreateLambda([this]()
        {
            ShowCollaborationWindow(ECyRevisionCollaborationView::MyLocks);
        })));

    FToolMenuSection& General = FindOrAddLabeledSection(
        Menu,
        TEXT("General"),
        LOCTEXT("AssetGeneralSection", "CYREVISION"));
    General.AddMenuEntry(
        TEXT("CyRevisionOpenClientForAsset"),
        LOCTEXT("OpenClientForAssetLabel", "Open project in CyRevision"),
        LOCTEXT("OpenClientForAssetTooltip", "Opens the current Unreal project in the CyRevision desktop application."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::OpenCyRevision)));
    General.AddMenuEntry(
        TEXT("CyRevisionOpenDashboardForAsset"),
        LOCTEXT("OpenDashboardForAssetLabel", "Revision dashboard"),
        LOCTEXT("OpenDashboardForAssetTooltip", "Opens the autonomous project revision dashboard."),
        Icon,
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ShowRevisionDashboard)));
}

void FCyRevisionEditorModule::OpenCyRevision() const
{
    if (RevisionTools)
    {
        RevisionTools->OpenCyRevision();
    }
}

void FCyRevisionEditorModule::ShowRevisionDashboard()
{
    if (RevisionTools)
    {
        RevisionTools->ShowDashboard();
    }
}

void FCyRevisionEditorModule::TestCyRevisionConnection()
{
    if (RevisionTools)
    {
        RevisionTools->TestConnection();
    }
}

void FCyRevisionEditorModule::ToggleToolbarEnabled()
{
    SetCyRevisionBool(ToolbarEnabledKey, !IsToolbarEnabled());
    UToolMenus::Get()->RefreshAllWidgets();
}

void FCyRevisionEditorModule::ToggleToolbarLabel()
{
    SetCyRevisionBool(ToolbarLabelKey, !IsToolbarLabelVisible());
    UToolMenus::Get()->RefreshAllWidgets();
}

bool FCyRevisionEditorModule::IsToolbarEnabled() const
{
    return GetCyRevisionBool(ToolbarEnabledKey, true);
}

bool FCyRevisionEditorModule::IsToolbarLabelVisible() const
{
    return GetCyRevisionBool(ToolbarLabelKey, true);
}

void FCyRevisionEditorModule::SetSelectedAssetsLfsLock(TArray<FAssetData> Assets, bool bLock)
{
    if (!RevisionTools)
    {
        return;
    }

    TArray<FString> RelativePaths;
    for (const FAssetData& Asset : Assets)
    {
        const FString RelativePath = GetAssetRelativeFilename(Asset);
        if (!RelativePath.IsEmpty())
        {
            RelativePaths.AddUnique(RelativePath);
        }
    }

    if (RelativePaths.Num() == 0)
    {
        FMessageDialog::Open(
            EAppMsgType::Ok,
            LOCTEXT("NoAssetFiles", "No on-disk asset file could be resolved for the selection."));
        return;
    }

    RevisionTools->SetLfsLockState(RelativePaths, bLock);
    if (CollaborationWindow.IsValid())
    {
        RefreshCollaborationWindow();
    }
}

void FCyRevisionEditorModule::MarkAssetsInProgress(TArray<FAssetData> Assets)
{
    const FCyRevisionPresenceLocation Location = ResolvePresenceLocation();
    FGuid OwnerId;
    FString OwnerName;
    GetOwnerIdentity(OwnerId, OwnerName);
    const FDateTime Now = FDateTime::UtcNow();
    const FDateTime ExpiresAt = Now + FTimespan::FromMinutes(GetReservationMinutes());
    const TArray<TSharedPtr<FCyRevisionSoftReservation>> Existing = ReadReservations(Location);

    TSet<FString> ConflictingOwners;
    for (const FAssetData& Asset : Assets)
    {
        const FString AssetPath = Asset.PackageName.ToString();
        for (const TSharedPtr<FCyRevisionSoftReservation>& Reservation : Existing)
        {
            FGuid ExistingOwnerId;
            if (!Reservation->bExpired &&
                Reservation->AssetPath.Equals(AssetPath, ESearchCase::IgnoreCase) &&
                FGuid::Parse(Reservation->OwnerId, ExistingOwnerId) && ExistingOwnerId != OwnerId)
            {
                ConflictingOwners.Add(Reservation->OwnerName);
            }
        }

        const FString Path = GetReservationFilename(Location, OwnerId, AssetPath);
        FString ReservationId = FGuid::NewGuid().ToString(EGuidFormats::DigitsWithHyphens);
        FDateTime CreatedAt = Now;
        for (const TSharedPtr<FCyRevisionSoftReservation>& Reservation : Existing)
        {
            FGuid ExistingOwnerId;
            if (Reservation->AssetPath.Equals(AssetPath, ESearchCase::IgnoreCase) &&
                FGuid::Parse(Reservation->OwnerId, ExistingOwnerId) && ExistingOwnerId == OwnerId)
            {
                ReservationId = Reservation->ReservationId;
                CreatedAt = Reservation->CreatedAtUtc;
                break;
            }
        }

        WriteJsonAtomically(
            Path,
            MakeReservationJson(Location, ReservationId, Asset, OwnerId, OwnerName, CreatedAt, Now, ExpiresAt));
    }

    RefreshReservationWindow();
    FString Message = FString::Printf(
        TEXT("%d asset(s) signalé(s). Ceci n'est pas un verrou : les autres utilisateurs peuvent toujours les modifier."),
        Assets.Num());
    if (!Location.bShared)
    {
        Message += TEXT("\n\n") + Location.Detail;
    }
    if (ConflictingOwners.Num() > 0)
    {
        Message += TEXT("\n\nDéjà signalé(s) par : ");
        Message += FString::Join(ConflictingOwners.Array(), TEXT(", "));
    }
    FMessageDialog::Open(EAppMsgType::Ok, FText::FromString(Message));
    if (RevisionTools)
    {
        RevisionTools->NotifyProjectChanged(TEXT("advisory-reservation"));
    }
}

void FCyRevisionEditorModule::ReleaseAssets(TArray<FAssetData> Assets)
{
    const FCyRevisionPresenceLocation Location = ResolvePresenceLocation();
    FGuid OwnerId;
    FString OwnerName;
    GetOwnerIdentity(OwnerId, OwnerName);
    const TArray<TSharedPtr<FCyRevisionSoftReservation>> Existing = ReadReservations(Location);
    int32 Removed = 0;

    for (const TSharedPtr<FCyRevisionSoftReservation>& Reservation : Existing)
    {
        FGuid ExistingOwnerId;
        if (!FGuid::Parse(Reservation->OwnerId, ExistingOwnerId) || ExistingOwnerId != OwnerId)
        {
            continue;
        }

        const bool bSelected = Assets.ContainsByPredicate([&Reservation](const FAssetData& Asset)
        {
            return Reservation->AssetPath.Equals(Asset.PackageName.ToString(), ESearchCase::IgnoreCase);
        });
        if (bSelected && IFileManager::Get().Delete(*Reservation->SourcePath, false, true, true))
        {
            Removed++;
        }
    }

    RefreshReservationWindow();
    FMessageDialog::Open(
        EAppMsgType::Ok,
        FText::Format(LOCTEXT("ReleasedCount", "{0} personal advisory marker(s) released."), Removed));
    if (RevisionTools)
    {
        RevisionTools->NotifyProjectChanged(TEXT("advisory-release"));
    }
}

void FCyRevisionEditorModule::ShowCollaborationWindow(ECyRevisionCollaborationView InitialView)
{
    if (InitialView == ECyRevisionCollaborationView::WorkInProgress)
    {
        ShowReservations();
        return;
    }

    CollaborationView = InitialView;
    if (const TSharedPtr<SWindow> ExistingWindow = CollaborationWindow.Pin())
    {
        ExistingWindow->BringToFront(true);
        ApplyCollaborationFilter();
        RefreshCollaborationWindow();
        return;
    }

    TSharedRef<SWindow> Window = SNew(SWindow)
        .Title(LOCTEXT("CollaborationWindowTitle", "CyRevision - Locks & Work in progress"))
        .ClientSize(FVector2D(1060.0f, 620.0f))
        .SupportsMaximize(true)
        .SupportsMinimize(true);
    CollaborationWindow = Window;
    Window->SetOnWindowClosed(FOnWindowClosed::CreateLambda([this](const TSharedRef<SWindow>&)
    {
        CollaborationList.Reset();
        CollaborationStatusText.Reset();
        CollaborationWindow.Reset();
        CollaborationItems.Reset();
        CollaborationLockItems.Reset();
        bCollaborationRefreshInProgress = false;
    }));

    Window->SetContent(
        SNew(SBorder)
        .Padding(12.0f)
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot()
            .AutoHeight()
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot()
                .FillWidth(1.0f)
                [
                    SNew(STextBlock)
                    .Text(LOCTEXT("CollaborationHeading", "Project collaboration state"))
                    .Font(FCoreStyle::GetDefaultFontStyle("Bold", 18))
                ]
                + SHorizontalBox::Slot()
                .AutoWidth()
                .Padding(4.0f, 0.0f)
                [
                    SNew(SButton)
                    .Text(LOCTEXT("AllLocksTab", "All locks"))
                    .ToolTipText(LOCTEXT("AllLocksTabTooltip", "Show every Git LFS lock for the project."))
                    .OnClicked_Lambda([this]()
                    {
                        SetCollaborationView(ECyRevisionCollaborationView::AllLocks);
                        return FReply::Handled();
                    })
                ]
                + SHorizontalBox::Slot()
                .AutoWidth()
                .Padding(4.0f, 0.0f)
                [
                    SNew(SButton)
                    .Text(LOCTEXT("MyLocksTab", "My locks"))
                    .ToolTipText(LOCTEXT("MyLocksTabTooltip", "Show locks owned by the current Git LFS identity."))
                    .OnClicked_Lambda([this]()
                    {
                        SetCollaborationView(ECyRevisionCollaborationView::MyLocks);
                        return FReply::Handled();
                    })
                ]
                + SHorizontalBox::Slot()
                .AutoWidth()
                .Padding(4.0f, 0.0f)
                [
                    SNew(SButton)
                    .Text(LOCTEXT("WipTab", "Work in progress"))
                    .ToolTipText(LOCTEXT("WipTabTooltip", "Open the non-blocking work-in-progress presence list."))
                    .OnClicked_Lambda([this]()
                    {
                        SetCollaborationView(ECyRevisionCollaborationView::WorkInProgress);
                        return FReply::Handled();
                    })
                ]
                + SHorizontalBox::Slot()
                .AutoWidth()
                .Padding(12.0f, 0.0f, 0.0f, 0.0f)
                [
                    SNew(SButton)
                    .Text(LOCTEXT("RefreshCollaboration", "Refresh"))
                    .OnClicked_Lambda([this]()
                    {
                        RefreshCollaborationWindow();
                        return FReply::Handled();
                    })
                ]
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0.0f, 6.0f, 0.0f, 8.0f)
            [
                SAssignNew(CollaborationStatusText, STextBlock)
                .Text(LOCTEXT("CollaborationLoading", "Loading Git LFS locks..."))
                .AutoWrapText(true)
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(4.0f, 0.0f, 4.0f, 4.0f)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot().FillWidth(3.0f)[SNew(STextBlock).Text(LOCTEXT("LockFileColumn", "File"))]
                + SHorizontalBox::Slot().FillWidth(1.3f)[SNew(STextBlock).Text(LOCTEXT("LockOwnerColumn", "Locked by"))]
                + SHorizontalBox::Slot().FillWidth(1.4f)[SNew(STextBlock).Text(LOCTEXT("LockDateColumn", "Date"))]
                + SHorizontalBox::Slot().FillWidth(1.0f)[SNew(STextBlock).Text(LOCTEXT("LockIdColumn", "Lock ID"))]
                + SHorizontalBox::Slot().FillWidth(0.55f)[SNew(STextBlock).Text(LOCTEXT("LockMineColumn", "Owner"))]
            ]
            + SVerticalBox::Slot()
            .FillHeight(1.0f)
            [
                SAssignNew(CollaborationList, SListView<TSharedPtr<FCyRevisionCollaborationItem>>)
                .ListItemsSource(&CollaborationItems)
                .SelectionMode(ESelectionMode::Multi)
                .OnGenerateRow_Lambda([](
                    TSharedPtr<FCyRevisionCollaborationItem> Item,
                    const TSharedRef<STableViewBase>& OwnerTable)
                {
                    return SNew(STableRow<TSharedPtr<FCyRevisionCollaborationItem>>, OwnerTable)
                        .Padding(FMargin(3.0f, 3.0f))
                        [
                            SNew(SHorizontalBox)
                            + SHorizontalBox::Slot().FillWidth(3.0f)
                            [SNew(STextBlock).Text(FText::FromString(Item->File))]
                            + SHorizontalBox::Slot().FillWidth(1.3f)
                            [SNew(STextBlock).Text(FText::FromString(Item->Owner))]
                            + SHorizontalBox::Slot().FillWidth(1.4f)
                            [SNew(STextBlock).Text(FText::FromString(Item->Updated))]
                            + SHorizontalBox::Slot().FillWidth(1.0f)
                            [SNew(STextBlock).Text(FText::FromString(Item->Id))]
                            + SHorizontalBox::Slot().FillWidth(0.55f)
                            [SNew(STextBlock).Text(Item->bMine ? LOCTEXT("MineLock", "Mine") : LOCTEXT("TeamLock", "Team"))]
                        ];
                })
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0.0f, 8.0f, 0.0f, 0.0f)
            [
                SNew(STextBlock)
                .Text(LOCTEXT(
                    "LocksSafetyReminder",
                    "Lock lists are read-only here. Use the Content Browser > CyRevision submenu to lock or normally unlock selected assets; another user's lock is never force-removed."))
                .AutoWrapText(true)
            ]
        ]);

    FSlateApplication::Get().AddWindow(Window);
    ApplyCollaborationFilter();
    RefreshCollaborationWindow();
}

void FCyRevisionEditorModule::SetCollaborationView(ECyRevisionCollaborationView View)
{
    if (View == ECyRevisionCollaborationView::WorkInProgress)
    {
        ShowReservations();
        return;
    }

    CollaborationView = View;
    ApplyCollaborationFilter();
}

void FCyRevisionEditorModule::RefreshCollaborationWindow()
{
    if (!RevisionTools || bCollaborationRefreshInProgress || !CollaborationWindow.IsValid())
    {
        return;
    }

    bCollaborationRefreshInProgress = true;
    if (CollaborationStatusText.IsValid())
    {
        CollaborationStatusText->SetText(LOCTEXT("LoadingLocksStatus", "Loading Git LFS locks from the remote server..."));
    }

    const TWeakPtr<SWindow> ExpectedWindow = CollaborationWindow;
    RevisionTools->QueryLfsLocksAsync(
        [ExpectedWindow](TArray<FCyRevisionLfsLock> Locks, FString Error)
        {
            FCyRevisionEditorModule* Module = FModuleManager::GetModulePtr<FCyRevisionEditorModule>(TEXT("CyRevisionEditor"));
            if (!Module || !ExpectedWindow.IsValid() || Module->CollaborationWindow.Pin() != ExpectedWindow.Pin())
            {
                return;
            }

            TArray<TSharedPtr<FCyRevisionCollaborationItem>> Items;
            Items.Reserve(Locks.Num());
            for (FCyRevisionLfsLock& Lock : Locks)
            {
                TSharedPtr<FCyRevisionCollaborationItem> Item = MakeShared<FCyRevisionCollaborationItem>();
                Item->Id = MoveTemp(Lock.Id);
                Item->File = MoveTemp(Lock.Path);
                Item->Owner = MoveTemp(Lock.Owner);
                Item->Updated = MoveTemp(Lock.LockedAt);
                Item->bMine = Lock.bMine;
                Items.Add(MoveTemp(Item));
            }
            Module->ApplyLfsLocks(MoveTemp(Items), Error);
        });
}

void FCyRevisionEditorModule::ApplyLfsLocks(
    TArray<TSharedPtr<FCyRevisionCollaborationItem>> Locks,
    const FString& Error)
{
    bCollaborationRefreshInProgress = false;
    CollaborationLockItems = MoveTemp(Locks);
    ApplyCollaborationFilter();

    if (CollaborationStatusText.IsValid())
    {
        int32 MineCount = 0;
        for (const TSharedPtr<FCyRevisionCollaborationItem>& Item : CollaborationLockItems)
        {
            MineCount += Item->bMine ? 1 : 0;
        }
        FString Status = FString::Printf(
            TEXT("%d project lock(s) - %d owned by me - %d owned by teammates"),
            CollaborationLockItems.Num(),
            MineCount,
            CollaborationLockItems.Num() - MineCount);
        if (!Error.TrimStartAndEnd().IsEmpty())
        {
            Status += TEXT("\n") + Error.TrimStartAndEnd();
        }
        CollaborationStatusText->SetText(FText::FromString(Status));
    }
}

void FCyRevisionEditorModule::ApplyCollaborationFilter()
{
    CollaborationItems.Reset();
    for (const TSharedPtr<FCyRevisionCollaborationItem>& Item : CollaborationLockItems)
    {
        if (CollaborationView == ECyRevisionCollaborationView::AllLocks || Item->bMine)
        {
            CollaborationItems.Add(Item);
        }
    }
    if (CollaborationList.IsValid())
    {
        CollaborationList->RequestListRefresh();
    }
}

void FCyRevisionEditorModule::ShowReservations()
{
    if (const TSharedPtr<SWindow> ExistingWindow = ReservationWindow.Pin())
    {
        ExistingWindow->BringToFront(true);
        RefreshReservationWindow();
        return;
    }

    TSharedRef<SWindow> Window = SNew(SWindow)
        .Title(LOCTEXT("ReservationWindowTitle", "CyRevision — Advisory reservations"))
        .ClientSize(FVector2D(980.0f, 520.0f))
        .SupportsMaximize(true)
        .SupportsMinimize(true);
    ReservationWindow = Window;
    Window->SetOnWindowClosed(FOnWindowClosed::CreateLambda([this](const TSharedRef<SWindow>&)
    {
        ReservationList.Reset();
        ReservationStatusText.Reset();
        ReservationWindow.Reset();
    }));

    Window->SetContent(
        SNew(SBorder)
        .Padding(14.0f)
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot()
            .AutoHeight()
            [
                SNew(STextBlock)
                .Text(LOCTEXT("ReservationHeading", "Who is working on what?"))
                .Font(FCoreStyle::GetDefaultFontStyle("Bold", 18))
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0.0f, 4.0f, 0.0f, 10.0f)
            [
                SAssignNew(ReservationStatusText, STextBlock)
                .AutoWrapText(true)
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0.0f, 0.0f, 0.0f, 6.0f)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot().FillWidth(2.3f)[SNew(STextBlock).Text(LOCTEXT("AssetColumn", "Asset"))]
                + SHorizontalBox::Slot().FillWidth(1.2f)[SNew(STextBlock).Text(LOCTEXT("OwnerColumn", "Person"))]
                + SHorizontalBox::Slot().FillWidth(1.2f)[SNew(STextBlock).Text(LOCTEXT("MachineColumn", "Machine"))]
                + SHorizontalBox::Slot().FillWidth(1.1f)[SNew(STextBlock).Text(LOCTEXT("UpdatedColumn", "Updated"))]
                + SHorizontalBox::Slot().FillWidth(0.7f)[SNew(STextBlock).Text(LOCTEXT("StateColumn", "State"))]
            ]
            + SVerticalBox::Slot()
            .FillHeight(1.0f)
            [
                SAssignNew(ReservationList, SListView<TSharedPtr<FCyRevisionSoftReservation>>)
                .ListItemsSource(&ReservationItems)
                .OnGenerateRow_Lambda([this](
                    TSharedPtr<FCyRevisionSoftReservation> Item,
                    const TSharedRef<STableViewBase>& OwnerTable)
                {
                    return GenerateReservationRow(Item, OwnerTable);
                })
                .SelectionMode(ESelectionMode::None)
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0.0f, 10.0f, 0.0f, 0.0f)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot()
                .AutoWidth()
                [
                    SNew(SButton)
                    .Text(LOCTEXT("RefreshReservations", "Refresh"))
                    .OnClicked_Lambda([this]()
                    {
                        RefreshReservationWindow();
                        return FReply::Handled();
                    })
                ]
                + SHorizontalBox::Slot()
                .FillWidth(1.0f)
                .Padding(14.0f, 4.0f, 0.0f, 0.0f)
                [
                    SNew(STextBlock)
                    .Text(LOCTEXT(
                        "NonBlockingReminder",
                        "Information only: no checkout, Git LFS lock, or protected write."))
                    .AutoWrapText(true)
                ]
            ]
        ]);

    FSlateApplication::Get().AddWindow(Window);
    RefreshReservationWindow();
}

void FCyRevisionEditorModule::RefreshReservationWindow()
{
    const FCyRevisionPresenceLocation Location = ResolvePresenceLocation();
    ReservationItems = ReadReservations(Location);
    if (ReservationList.IsValid())
    {
        ReservationList->RequestListRefresh();
    }

    if (ReservationStatusText.IsValid())
    {
        int32 Active = 0;
        for (const TSharedPtr<FCyRevisionSoftReservation>& Item : ReservationItems)
        {
            Active += Item->bExpired ? 0 : 1;
        }
        const int32 Expired = ReservationItems.Num() - Active;
        ReservationStatusText->SetText(FText::FromString(FString::Printf(
            TEXT("%d actif(s) · %d expiré(s) · %s\n%s"),
            Active,
            Expired,
            Location.bShared ? TEXT("partage prêt") : TEXT("local uniquement"),
            *Location.Detail)));
    }
}

void FCyRevisionEditorModule::RefreshOwnedReservations()
{
    const FCyRevisionPresenceLocation Location = ResolvePresenceLocation();
    FGuid OwnerId;
    FString OwnerName;
    GetOwnerIdentity(OwnerId, OwnerName);
    const FString OwnerIdText = OwnerId.ToString(EGuidFormats::DigitsWithHyphens);
    const FDateTime Now = FDateTime::UtcNow();
    const FDateTime ExpiresAt = Now + FTimespan::FromMinutes(GetReservationMinutes());

    for (const TSharedPtr<FCyRevisionSoftReservation>& Reservation : ReadReservations(Location))
    {
        FGuid ExistingOwnerId;
        if (Reservation->bExpired || !FGuid::Parse(Reservation->OwnerId, ExistingOwnerId) || ExistingOwnerId != OwnerId)
        {
            continue;
        }

        TSharedPtr<FJsonObject> Json;
        if (ReadJsonFile(Reservation->SourcePath, Json))
        {
            Json->SetStringField(TEXT("updatedAtUtc"), Now.ToIso8601());
            Json->SetStringField(TEXT("expiresAtUtc"), ExpiresAt.ToIso8601());
            Json->SetStringField(TEXT("ownerName"), OwnerName);
            Json->SetStringField(TEXT("ownerId"), OwnerIdText);
            WriteJsonAtomically(Reservation->SourcePath, Json.ToSharedRef());
        }
    }
}

bool FCyRevisionEditorModule::HandleHeartbeat(float DeltaTime)
{
    RefreshOwnedReservations();
    if (ReservationWindow.IsValid())
    {
        RefreshReservationWindow();
    }
    return true;
}

TSharedRef<ITableRow> FCyRevisionEditorModule::GenerateReservationRow(
    TSharedPtr<FCyRevisionSoftReservation> Item,
    const TSharedRef<STableViewBase>& OwnerTable)
{
    return SNew(STableRow<TSharedPtr<FCyRevisionSoftReservation>>, OwnerTable)
        .Padding(FMargin(3.0f, 5.0f))
        [
            SNew(SHorizontalBox)
            + SHorizontalBox::Slot().FillWidth(2.3f)[SNew(STextBlock).Text(FText::FromString(Item->AssetPath))]
            + SHorizontalBox::Slot().FillWidth(1.2f)[SNew(STextBlock).Text(FText::FromString(Item->OwnerName))]
            + SHorizontalBox::Slot().FillWidth(1.2f)[SNew(STextBlock).Text(FText::FromString(Item->MachineName))]
            + SHorizontalBox::Slot().FillWidth(1.1f)
            [
                SNew(STextBlock).Text(FText::FromString(Item->UpdatedAtUtc.ToString(TEXT("%d/%m %H:%M UTC"))))
            ]
            + SHorizontalBox::Slot().FillWidth(0.7f)
            [
                SNew(STextBlock).Text(Item->bExpired ? LOCTEXT("ExpiredState", "Expired") : LOCTEXT("ActiveState", "Active"))
            ]
        ];
}

IMPLEMENT_MODULE(FCyRevisionEditorModule, CyRevisionEditor)

#undef LOCTEXT_NAMESPACE
