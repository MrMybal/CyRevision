#pragma once

#include "CoreMinimal.h"
#include "Containers/Ticker.h"
#include "Modules/ModuleManager.h"

class ITableRow;
class STableViewBase;
class STextBlock;
class SWindow;
template <typename ItemType> class SListView;
struct FAssetData;
struct FCyRevisionSoftReservation;
struct FToolMenuSection;
class FCyRevisionRevisionTools;
class FCyRevisionSwarmTools;

class FCyRevisionEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    void RegisterMenus();
    void AddAssetContextEntries(FToolMenuSection& Section);
    void OpenCyRevision() const;
    void ShowRevisionDashboard();
    void TestCyRevisionConnection();
    void MarkAssetsInProgress(TArray<FAssetData> Assets);
    void ReleaseAssets(TArray<FAssetData> Assets);
    void ShowReservations();
    void RefreshReservationWindow();
    void RefreshOwnedReservations();
    bool HandleHeartbeat(float DeltaTime);
    TSharedRef<ITableRow> GenerateReservationRow(
        TSharedPtr<FCyRevisionSoftReservation> Item,
        const TSharedRef<STableViewBase>& OwnerTable);

    TArray<TSharedPtr<FCyRevisionSoftReservation>> ReservationItems;
    TWeakPtr<SWindow> ReservationWindow;
    TSharedPtr<SListView<TSharedPtr<FCyRevisionSoftReservation>>> ReservationList;
    TSharedPtr<STextBlock> ReservationStatusText;
    TUniquePtr<FCyRevisionRevisionTools> RevisionTools;
    TUniquePtr<FCyRevisionSwarmTools> SwarmTools;
    FTSTicker::FDelegateHandle HeartbeatHandle;
};
