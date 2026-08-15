#pragma once

#include "CoreMinimal.h"
#include "Containers/Ticker.h"
#include "CyRevisionEngineCompatibility.h"
#include "Modules/ModuleManager.h"

class ITableRow;
class FSlateStyleSet;
class STableViewBase;
class STextBlock;
class SWindow;
template <typename ItemType> class SListView;
struct FAssetData;
struct FCyRevisionCollaborationItem;
struct FCyRevisionSoftReservation;
struct FToolMenuSection;
class UToolMenu;
class FCyRevisionRevisionTools;
class FCyRevisionSourceControlProvider;
class FCyRevisionSwarmTools;

enum class ECyRevisionCollaborationView : uint8
{
    AllLocks,
    MyLocks,
    WorkInProgress
};

class FCyRevisionEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    void RegisterMenus();
    void RegisterStyle();
    void UnregisterStyle();
    void BuildCyRevisionMenu(UToolMenu* Menu);
    void AddToolbarEntry(FToolMenuSection& Section);
    void AddAssetContextEntries(FToolMenuSection& Section);
    void AddAssetContextSubMenu(UToolMenu* Menu, TArray<FAssetData> SelectedAssets);
    void OpenCyRevision() const;
    void ShowRevisionDashboard();
    void TestCyRevisionConnection();
    void ToggleToolbarEnabled();
    void ToggleToolbarLabel();
    bool IsToolbarEnabled() const;
    bool IsToolbarLabelVisible() const;
    void MarkAssetsInProgress(TArray<FAssetData> Assets);
    void ReleaseAssets(TArray<FAssetData> Assets);
    void ShowReservations();
    void ShowCollaborationWindow(ECyRevisionCollaborationView InitialView);
    void SetCollaborationView(ECyRevisionCollaborationView View);
    void RefreshCollaborationWindow();
    void ApplyCollaborationFilter();
    void ApplyLfsLocks(TArray<TSharedPtr<FCyRevisionCollaborationItem>> Locks, const FString& Error);
    void SetSelectedAssetsLfsLock(TArray<FAssetData> Assets, bool bLock);
    void RefreshReservationWindow();
    void RefreshOwnedReservations();
    bool HandleHeartbeat(float DeltaTime);
    TSharedRef<ITableRow> GenerateReservationRow(
        TSharedPtr<FCyRevisionSoftReservation> Item,
        const TSharedRef<STableViewBase>& OwnerTable);

    TArray<TSharedPtr<FCyRevisionSoftReservation>> ReservationItems;
    TArray<TSharedPtr<FCyRevisionCollaborationItem>> CollaborationLockItems;
    TArray<TSharedPtr<FCyRevisionCollaborationItem>> CollaborationWipItems;
    TArray<TSharedPtr<FCyRevisionCollaborationItem>> CollaborationItems;
    TWeakPtr<SWindow> ReservationWindow;
    TSharedPtr<SListView<TSharedPtr<FCyRevisionSoftReservation>>> ReservationList;
    TSharedPtr<STextBlock> ReservationStatusText;
    TWeakPtr<SWindow> CollaborationWindow;
    TSharedPtr<SListView<TSharedPtr<FCyRevisionCollaborationItem>>> CollaborationList;
    TSharedPtr<STextBlock> CollaborationStatusText;
    ECyRevisionCollaborationView CollaborationView = ECyRevisionCollaborationView::AllLocks;
    bool bCollaborationRefreshInProgress = false;
    TSharedPtr<FSlateStyleSet> Style;
    TUniquePtr<FCyRevisionRevisionTools> RevisionTools;
    TUniquePtr<FCyRevisionSourceControlProvider> SourceControlProvider;
    TUniquePtr<FCyRevisionSwarmTools> SwarmTools;
#if CYREVISION_UE5
    FTSTicker::FDelegateHandle HeartbeatHandle;
#else
    FTicker::FDelegateHandle HeartbeatHandle;
#endif
};
