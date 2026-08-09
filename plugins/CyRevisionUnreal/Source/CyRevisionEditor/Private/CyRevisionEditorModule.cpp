#include "CyRevisionEditorModule.h"

#include "AssetRegistry/AssetData.h"
#include "ContentBrowserMenuContexts.h"
#include "Dom/JsonObject.h"
#include "Framework/Application/SlateApplication.h"
#include "Framework/Commands/UIAction.h"
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
#include "ToolMenus.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SScrollBox.h"
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
    UToolMenus::RegisterStartupCallback(
        FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FCyRevisionEditorModule::RegisterMenus));
    HeartbeatHandle = FTSTicker::GetCoreTicker().AddTicker(
        FTickerDelegate::CreateRaw(this, &FCyRevisionEditorModule::HandleHeartbeat),
        60.0f);
}

void FCyRevisionEditorModule::ShutdownModule()
{
    if (HeartbeatHandle.IsValid())
    {
        FTSTicker::GetCoreTicker().RemoveTicker(HeartbeatHandle);
        HeartbeatHandle.Reset();
    }
    UToolMenus::UnRegisterStartupCallback(this);
    UToolMenus::UnregisterOwner(this);
    ReservationList.Reset();
    ReservationStatusText.Reset();
    ReservationWindow.Reset();
}

void FCyRevisionEditorModule::RegisterMenus()
{
    FToolMenuOwnerScoped OwnerScoped(this);

    UToolMenu* ToolsMenu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Tools"));
    FToolMenuSection& Section = ToolsMenu->FindOrAddSection(TEXT("CyRevision"));
    Section.AddMenuEntry(
        TEXT("OpenCyRevision"),
        LOCTEXT("OpenCyRevisionLabel", "Ouvrir dans CyRevision"),
        LOCTEXT("OpenCyRevisionTooltip", "Ouvre le client CyRevision externe sur le projet Unreal courant."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::OpenCyRevision)));
    Section.AddMenuEntry(
        TEXT("ShowCyRevisionReservations"),
        LOCTEXT("ShowReservationsLabel", "Réservations souples"),
        LOCTEXT("ShowReservationsTooltip", "Affiche qui travaille sur quels assets, sans checkout ni verrou."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ShowReservations)));

    UToolMenu* AssetMenu = UToolMenus::Get()->ExtendMenu(TEXT("ContentBrowser.AssetContextMenu"));
    FToolMenuSection& AssetSection = AssetMenu->FindOrAddSection(TEXT("CyRevision"));
    AssetSection.AddDynamicEntry(
        TEXT("CyRevisionSoftReservationActions"),
        FNewToolMenuSectionDelegate::CreateRaw(this, &FCyRevisionEditorModule::AddAssetContextEntries));
}

void FCyRevisionEditorModule::AddAssetContextEntries(FToolMenuSection& Section)
{
    const UContentBrowserAssetContextMenuContext* Context = Section.FindContext<UContentBrowserAssetContextMenuContext>();
    if (!Context || Context->SelectedAssets.IsEmpty())
    {
        return;
    }

    const TArray<FAssetData> SelectedAssets = Context->SelectedAssets;
    Section.AddMenuEntry(
        TEXT("CyRevisionMarkInProgress"),
        LOCTEXT("MarkInProgressLabel", "Signaler : je travaille dessus"),
        LOCTEXT(
            "MarkInProgressTooltip",
            "Crée une réservation informative. L'asset reste modifiable et aucun checkout ou verrou LFS n'est créé."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateLambda([this, SelectedAssets]() { MarkAssetsInProgress(SelectedAssets); })));
    Section.AddMenuEntry(
        TEXT("CyRevisionReleaseAdvisory"),
        LOCTEXT("ReleaseAdvisoryLabel", "Libérer mon signalement"),
        LOCTEXT("ReleaseAdvisoryTooltip", "Supprime uniquement vos propres signalements pour les assets sélectionnés."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateLambda([this, SelectedAssets]() { ReleaseAssets(SelectedAssets); })));
    Section.AddMenuEntry(
        TEXT("CyRevisionViewAdvisories"),
        LOCTEXT("ViewAdvisoriesLabel", "Voir les réservations souples"),
        LOCTEXT("ViewAdvisoriesTooltip", "Affiche tous les signalements actifs et expirés du projet."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::ShowReservations)));
}

void FCyRevisionEditorModule::OpenCyRevision() const
{
    FString ExecutablePath;
    GConfig->GetString(TEXT("CyRevision"), TEXT("ExecutablePath"), ExecutablePath, GEditorPerProjectIni);
    if (ExecutablePath.IsEmpty() || !FPaths::FileExists(ExecutablePath))
    {
        FMessageDialog::Open(
            EAppMsgType::Ok,
            LOCTEXT(
                "MissingExecutable",
                "Configurez [CyRevision] ExecutablePath dans Config/DefaultEditorPerProjectUserSettings.ini."));
        return;
    }

    const FString ProjectDirectory = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
    const FString Arguments = FString::Printf(TEXT("--project=\"%s\""), *ProjectDirectory);
    FProcHandle Process = FPlatformProcess::CreateProc(
        *ExecutablePath,
        *Arguments,
        true,
        false,
        false,
        nullptr,
        0,
        nullptr,
        nullptr);
    if (!Process.IsValid())
    {
        FMessageDialog::Open(EAppMsgType::Ok, LOCTEXT("LaunchFailed", "CyRevision n'a pas pu être démarré."));
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
    if (!ConflictingOwners.IsEmpty())
    {
        Message += TEXT("\n\nDéjà signalé(s) par : ");
        Message += FString::Join(ConflictingOwners.Array(), TEXT(", "));
    }
    FMessageDialog::Open(EAppMsgType::Ok, FText::FromString(Message));
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
        FText::Format(LOCTEXT("ReleasedCount", "{0} signalement(s) personnel(s) libéré(s)."), Removed));
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
        .Title(LOCTEXT("ReservationWindowTitle", "CyRevision — Réservations souples"))
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
                .Text(LOCTEXT("ReservationHeading", "Qui travaille sur quoi ?"))
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
                + SHorizontalBox::Slot().FillWidth(1.2f)[SNew(STextBlock).Text(LOCTEXT("OwnerColumn", "Personne"))]
                + SHorizontalBox::Slot().FillWidth(1.2f)[SNew(STextBlock).Text(LOCTEXT("MachineColumn", "Machine"))]
                + SHorizontalBox::Slot().FillWidth(1.1f)[SNew(STextBlock).Text(LOCTEXT("UpdatedColumn", "Actualisé"))]
                + SHorizontalBox::Slot().FillWidth(0.7f)[SNew(STextBlock).Text(LOCTEXT("StateColumn", "État"))]
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
                    .Text(LOCTEXT("RefreshReservations", "Actualiser"))
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
                        "Information uniquement : aucun checkout, aucun verrou Git LFS, aucune écriture protégée."))
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
                SNew(STextBlock).Text(Item->bExpired ? LOCTEXT("ExpiredState", "Expirée") : LOCTEXT("ActiveState", "En cours"))
            ]
        ];
}

IMPLEMENT_MODULE(FCyRevisionEditorModule, CyRevisionEditor)

#undef LOCTEXT_NAMESPACE
