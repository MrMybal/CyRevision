#pragma once

#include "CoreMinimal.h"
#include "CyRevisionEngineCompatibility.h"
#include "ISourceControlState.h"

enum class ECyRevisionWorkingCopyState : uint8
{
    Unknown,
    Unchanged,
    Added,
    Deleted,
    Modified,
    Renamed,
    Conflicted,
    NotControlled,
    Ignored
};

class FCyRevisionSourceControlState final : public ISourceControlState
{
public:
    explicit FCyRevisionSourceControlState(FString InFilename);

    void SetWorkingCopyState(ECyRevisionWorkingCopyState InState);
    void SetHistory(TArray<TSharedRef<ISourceControlRevision, ESPMode::ThreadSafe>> InHistory);

    virtual int32 GetHistorySize() const override;
    virtual TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> GetHistoryItem(int32 HistoryIndex) const override;
    virtual TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FindHistoryRevision(int32 RevisionNumber) const override;
    virtual TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FindHistoryRevision(const FString& InRevision) const override;
#if CYREVISION_UE4 || (ENGINE_MAJOR_VERSION == 5 && ENGINE_MINOR_VERSION <= 2)
    virtual TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> GetBaseRevForMerge() const override;
#endif
#if CYREVISION_UE5
    virtual TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> GetCurrentRevision() const override;
#if SOURCE_CONTROL_WITH_SLATE
    virtual FSlateIcon GetIcon() const override;
#endif
#else
    virtual FName GetIconName() const override;
    virtual FName GetSmallIconName() const override;
#endif
    virtual FText GetDisplayName() const override;
    virtual FText GetDisplayTooltip() const override;
    virtual const FString& GetFilename() const override;
    virtual const FDateTime& GetTimeStamp() const override;
    virtual bool CanCheckIn() const override;
    virtual bool CanCheckout() const override;
    virtual bool IsCheckedOut() const override;
    virtual bool IsCheckedOutOther(FString* Who = nullptr) const override;
    virtual bool IsCheckedOutInOtherBranch(const FString& CurrentBranch = FString()) const override;
    virtual bool IsModifiedInOtherBranch(const FString& CurrentBranch = FString()) const override;
    virtual bool IsCheckedOutOrModifiedInOtherBranch(const FString& CurrentBranch = FString()) const override;
    virtual TArray<FString> GetCheckedOutBranches() const override;
    virtual FString GetOtherUserBranchCheckedOuts() const override;
    virtual bool GetOtherBranchHeadModification(FString& HeadBranchOut, FString& ActionOut, int32& HeadChangeListOut) const override;
    virtual bool IsCurrent() const override;
    virtual bool IsSourceControlled() const override;
    virtual bool IsAdded() const override;
    virtual bool IsDeleted() const override;
    virtual bool IsIgnored() const override;
    virtual bool CanEdit() const override;
    virtual bool CanDelete() const override;
    virtual bool IsUnknown() const override;
    virtual bool IsModified() const override;
    virtual bool CanAdd() const override;
    virtual bool IsConflicted() const override;
    virtual bool CanRevert() const override;

private:
    FString Filename;
    FDateTime TimeStamp;
    ECyRevisionWorkingCopyState WorkingCopyState = ECyRevisionWorkingCopyState::Unknown;
    TArray<TSharedRef<ISourceControlRevision, ESPMode::ThreadSafe>> History;
};
