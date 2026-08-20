#pragma once

#include "CoreMinimal.h"
#include "CyRevisionEngineCompatibility.h"
#include "ISourceControlProvider.h"

class FCyRevisionSourceControlState;

class FCyRevisionSourceControlProvider final : public ISourceControlProvider
{
public:
    virtual void Init(bool bForceConnection = true) override;
    virtual void Close() override;
    virtual const FName& GetName() const override;
    virtual FText GetStatusText() const override;
#if CYREVISION_UE5_3_OR_LATER
    virtual TMap<EStatus, FString> GetStatus() const override;
#endif
    virtual bool IsEnabled() const override;
    virtual bool IsAvailable() const override;
    virtual bool QueryStateBranchConfig(const FString& ConfigSrc, const FString& ConfigDest) override { return false; }
    virtual void RegisterStateBranches(const TArray<FString>& BranchNames, const FString& ContentRoot) override {}
    virtual int32 GetStateBranchIndex(const FString& InBranchName) const override { return INDEX_NONE; }
#if CYREVISION_UE5_7_OR_LATER
    virtual bool GetStateBranchAtIndex(int32 BranchIndex, FString& OutBranchName) const override { return false; }
#endif
    virtual ECommandResult::Type GetState(
        const TArray<FString>& InFiles,
        TArray<FSourceControlStateRef>& OutState,
        EStateCacheUsage::Type InStateCacheUsage) override;
#if CYREVISION_UE5
    virtual ECommandResult::Type GetState(
        const TArray<FSourceControlChangelistRef>& InChangelists,
        TArray<FSourceControlChangelistStateRef>& OutState,
        EStateCacheUsage::Type InStateCacheUsage) override;
#endif
    virtual TArray<FSourceControlStateRef> GetCachedStateByPredicate(
        TFunctionRef<bool(const FSourceControlStateRef&)> Predicate) const override;
    virtual FDelegateHandle RegisterSourceControlStateChanged_Handle(
        const FSourceControlStateChanged::FDelegate& SourceControlStateChanged) override;
    virtual void UnregisterSourceControlStateChanged_Handle(FDelegateHandle Handle) override;
#if CYREVISION_UE5
    virtual ECommandResult::Type Execute(
        const FSourceControlOperationRef& InOperation,
        FSourceControlChangelistPtr InChangelist,
        const TArray<FString>& InFiles,
        EConcurrency::Type InConcurrency = EConcurrency::Synchronous,
        const FSourceControlOperationComplete& InOperationCompleteDelegate = FSourceControlOperationComplete()) override;
#else
    virtual ECommandResult::Type Execute(
        const TSharedRef<ISourceControlOperation, ESPMode::ThreadSafe>& InOperation,
        const TArray<FString>& InFiles,
        EConcurrency::Type InConcurrency = EConcurrency::Synchronous,
        const FSourceControlOperationComplete& InOperationCompleteDelegate = FSourceControlOperationComplete()) override;
#endif
#if CYREVISION_UE5_3_OR_LATER
    virtual bool CanExecuteOperation(const FSourceControlOperationRef& InOperation) const override;
#endif
#if CYREVISION_UE5
    virtual bool CanCancelOperation(const FSourceControlOperationRef& InOperation) const override;
    virtual void CancelOperation(const FSourceControlOperationRef& InOperation) override;
#else
    virtual bool CanCancelOperation(const TSharedRef<ISourceControlOperation, ESPMode::ThreadSafe>& InOperation) const override;
    virtual void CancelOperation(const TSharedRef<ISourceControlOperation, ESPMode::ThreadSafe>& InOperation) override;
#endif
    virtual bool UsesLocalReadOnlyState() const override;
    virtual bool UsesChangelists() const override;
#if CYREVISION_UE5
    virtual bool UsesUncontrolledChangelists() const override;
#endif
    virtual bool UsesCheckout() const override;
#if CYREVISION_UE5
    virtual bool UsesFileRevisions() const override;
    virtual bool UsesSnapshots() const override;
#if CYREVISION_UE5_8_OR_LATER
    virtual bool UsesSoftRevertOnDelete() const override;
#endif
    virtual bool AllowsDiffAgainstDepot() const override;
#if CYREVISION_UE5_8_OR_LATER
    virtual TOptional<bool> HasChangesToSync() const override;
    virtual TOptional<bool> HasChangesToCheckIn() const override;
#else
    virtual TOptional<bool> IsAtLatestRevision() const override;
    virtual TOptional<int> GetNumLocalChanges() const override;
#endif
#endif
    virtual void Tick() override;
    virtual TArray<TSharedRef<ISourceControlLabel>> GetLabels(const FString& InMatchingSpec) const override;
#if CYREVISION_UE5
    virtual TArray<FSourceControlChangelistRef> GetChangelists(EStateCacheUsage::Type InStateCacheUsage) override;
#endif
#if SOURCE_CONTROL_WITH_SLATE
    virtual TSharedRef<SWidget> MakeSettingsWidget() const override;
#endif

private:
    bool RefreshConnection();
    bool RefreshStates(const TArray<FString>& InFiles);
    bool RefreshHistories(const TArray<FString>& InFiles);
    bool QueryFileHistory(FCyRevisionSourceControlState& State) const;
    bool RunGitForFiles(const FString& Prefix, const TArray<FString>& InFiles, const FString& Suffix = FString()) const;
    bool MakeRepositoryRelative(const FString& Filename, FString& OutRelativeFilename) const;
    TOptional<int> CountLocalChanges() const;
    TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe> GetStateInternal(const FString& Filename);
    void QueryFileState(FCyRevisionSourceControlState& State) const;
    bool RunGit(const FString& WorkingDirectory, const FString& Arguments, FString& OutStdOut, FString* OutStdErr = nullptr) const;
    static FString QuoteArgument(const FString& Argument);

    bool bGitAvailable = false;
    bool bRepositoryFound = false;
    FString RepositoryRoot;
    FString BranchName;
    FString RemoteUrl;
    FString UserName;
    FString GitVersion;
    TMap<FString, TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe>> StateCache;
    FSourceControlStateChanged OnSourceControlStateChanged;
};
