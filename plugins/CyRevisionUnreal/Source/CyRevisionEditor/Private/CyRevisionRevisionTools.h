#pragma once

#include "CoreMinimal.h"

class SEditableTextBox;
class STextBlock;
class SWindow;

struct FCyRevisionLfsLock
{
    FString Id;
    FString Path;
    FString Owner;
    FString LockedAt;
    bool bMine = false;
};

class FCyRevisionRevisionTools
{
public:
    void ShowDashboard();
    void OpenCyRevision() const;
    void TestConnection(bool bShowDialog = true);
    void NotifyProjectChanged(const FString& Action) const;
    void QueryLfsLocksAsync(TFunction<void(TArray<FCyRevisionLfsLock>, FString)> Completion) const;
    void SetLfsLockState(const TArray<FString>& RelativePaths, bool bLock);
    void Shutdown();

private:
    bool RunGit(const FString& Command, FString& Output, FString& Error) const;
    void RunGitAction(const FString& Command, const FString& Action);
    void Commit();
    void RefreshDashboard();
    FString GetProjectDirectory() const;

    TWeakPtr<SWindow> DashboardWindow;
    TSharedPtr<STextBlock> RepositoryStatusText;
    TSharedPtr<STextBlock> RevisionHistoryText;
    TSharedPtr<STextBlock> ConnectionStatusText;
    TSharedPtr<SEditableTextBox> CommitMessageText;
};
