#include "CyRevisionSourceControlProvider.h"

#include "CyRevisionSourceControlRevision.h"
#include "CyRevisionSourceControlState.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformProcess.h"
#include "Misc/Paths.h"
#include "SourceControlHelpers.h"
#include "SourceControlOperations.h"

#if SOURCE_CONTROL_WITH_SLATE
#include "Styling/CoreStyle.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/Text/STextBlock.h"
#endif

#define LOCTEXT_NAMESPACE "CyRevisionSourceControlProvider"

namespace
{
const FName CyRevisionProviderName(TEXT("CyRevision"));

bool IsConflictStatus(TCHAR IndexState, TCHAR WorkTreeState)
{
    return IndexState == TEXT('U') || WorkTreeState == TEXT('U') ||
        (IndexState == TEXT('A') && WorkTreeState == TEXT('A')) ||
        (IndexState == TEXT('D') && WorkTreeState == TEXT('D'));
}
}

void FCyRevisionSourceControlProvider::Init(bool)
{
    RefreshConnection();
}

void FCyRevisionSourceControlProvider::Close()
{
    StateCache.Empty();
    bGitAvailable = false;
    bRepositoryFound = false;
    RepositoryRoot.Empty();
    BranchName.Empty();
    RemoteUrl.Empty();
    UserName.Empty();
    GitVersion.Empty();
}

const FName& FCyRevisionSourceControlProvider::GetName() const
{
    return CyRevisionProviderName;
}

FText FCyRevisionSourceControlProvider::GetStatusText() const
{
    if (!bGitAvailable)
    {
        return LOCTEXT("GitUnavailable", "Git was not found. Install Git or configure CYREVISION_GIT_PATH.");
    }
    if (!bRepositoryFound)
    {
        return LOCTEXT("RepositoryUnavailable", "The Unreal project is not inside a Git repository yet. Create or add it in CyRevision, then reconnect.");
    }

    FFormatNamedArguments Arguments;
    Arguments.Add(TEXT("Repository"), FText::FromString(RepositoryRoot));
    Arguments.Add(TEXT("Branch"), FText::FromString(BranchName.IsEmpty() ? TEXT("detached HEAD") : BranchName));
    Arguments.Add(TEXT("Remote"), FText::FromString(RemoteUrl.IsEmpty() ? TEXT("local only") : RemoteUrl));
    Arguments.Add(TEXT("User"), FText::FromString(UserName));
    return FText::Format(
        LOCTEXT("ProviderStatus", "Repository: {Repository}\nBranch: {Branch}\nRemote: {Remote}\nUser: {User}"),
        Arguments);
}

#if CYREVISION_UE5_3_OR_LATER
TMap<ISourceControlProvider::EStatus, FString> FCyRevisionSourceControlProvider::GetStatus() const
{
    TMap<EStatus, FString> Result;
    Result.Add(EStatus::Enabled, IsEnabled() ? TEXT("Yes") : TEXT("No"));
    Result.Add(EStatus::Connected, IsAvailable() ? TEXT("Yes") : TEXT("No"));
    Result.Add(EStatus::Repository, RepositoryRoot);
    Result.Add(EStatus::Remote, RemoteUrl);
    Result.Add(EStatus::Branch, BranchName);
    Result.Add(EStatus::User, UserName);
    Result.Add(EStatus::ScmVersion, GitVersion);
    Result.Add(EStatus::PluginVersion, TEXT("0.7.0"));
    return Result;
}
#endif

bool FCyRevisionSourceControlProvider::IsEnabled() const { return bRepositoryFound; }
bool FCyRevisionSourceControlProvider::IsAvailable() const { return bRepositoryFound; }

ECommandResult::Type FCyRevisionSourceControlProvider::GetState(
    const TArray<FString>& InFiles,
    TArray<FSourceControlStateRef>& OutState,
    EStateCacheUsage::Type InStateCacheUsage)
{
    if (!bRepositoryFound)
    {
        return ECommandResult::Failed;
    }

    const TArray<FString> AbsoluteFiles = SourceControlHelpers::AbsoluteFilenames(InFiles);
    if (InStateCacheUsage == EStateCacheUsage::ForceUpdate)
    {
        RefreshStates(AbsoluteFiles);
    }

    for (const FString& File : AbsoluteFiles)
    {
        TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe> State = GetStateInternal(File);
        if (State->IsUnknown())
        {
            QueryFileState(State.Get());
        }
        OutState.Add(State);
    }
    return ECommandResult::Succeeded;
}

#if CYREVISION_UE5
ECommandResult::Type FCyRevisionSourceControlProvider::GetState(
    const TArray<FSourceControlChangelistRef>&,
    TArray<FSourceControlChangelistStateRef>&,
    EStateCacheUsage::Type)
{
    return ECommandResult::Failed;
}
#endif

TArray<FSourceControlStateRef> FCyRevisionSourceControlProvider::GetCachedStateByPredicate(
    TFunctionRef<bool(const FSourceControlStateRef&)> Predicate) const
{
    TArray<FSourceControlStateRef> Result;
    for (const TPair<FString, TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe>>& Entry : StateCache)
    {
        FSourceControlStateRef State = Entry.Value;
        if (Predicate(State))
        {
            Result.Add(State);
        }
    }
    return Result;
}

FDelegateHandle FCyRevisionSourceControlProvider::RegisterSourceControlStateChanged_Handle(
    const FSourceControlStateChanged::FDelegate& SourceControlStateChanged)
{
    return OnSourceControlStateChanged.Add(SourceControlStateChanged);
}

void FCyRevisionSourceControlProvider::UnregisterSourceControlStateChanged_Handle(FDelegateHandle Handle)
{
    OnSourceControlStateChanged.Remove(Handle);
}

#if CYREVISION_UE5
ECommandResult::Type FCyRevisionSourceControlProvider::Execute(
    const FSourceControlOperationRef& InOperation,
    FSourceControlChangelistPtr,
    const TArray<FString>& InFiles,
    EConcurrency::Type,
    const FSourceControlOperationComplete& InOperationCompleteDelegate)
#else
ECommandResult::Type FCyRevisionSourceControlProvider::Execute(
    const TSharedRef<ISourceControlOperation, ESPMode::ThreadSafe>& InOperation,
    const TArray<FString>& InFiles,
    EConcurrency::Type,
    const FSourceControlOperationComplete& InOperationCompleteDelegate)
#endif
{
    ECommandResult::Type Result = ECommandResult::Failed;
    const FName OperationName = InOperation->GetName();
    const TArray<FString> AbsoluteFiles = SourceControlHelpers::AbsoluteFilenames(InFiles);
    if (OperationName == FName(TEXT("Connect")))
    {
        Result = RefreshConnection() ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (OperationName == FName(TEXT("UpdateStatus")) && bRepositoryFound)
    {
        const bool bStatesUpdated = RefreshStates(AbsoluteFiles);
        const TSharedRef<FUpdateStatus, ESPMode::ThreadSafe> Update = StaticCastSharedRef<FUpdateStatus>(InOperation);
        const bool bHistoryUpdated = !Update->ShouldUpdateHistory() || RefreshHistories(AbsoluteFiles);
        Result = bStatesUpdated && bHistoryUpdated ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (OperationName == FName(TEXT("CheckOut")) && bRepositoryFound)
    {
        // CyRevision is intentionally checkout-less. Returning success lets Unreal save the
        // selected assets without creating a mandatory lock or prompting on every save.
        Result = ECommandResult::Succeeded;
    }
    else if (OperationName == FName(TEXT("MarkForAdd")) && bRepositoryFound)
    {
        Result = RunGitForFiles(TEXT("add --"), AbsoluteFiles)
            ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (OperationName == FName(TEXT("Delete")) && bRepositoryFound)
    {
        Result = RunGitForFiles(TEXT("add -A --"), AbsoluteFiles)
            ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (OperationName == FName(TEXT("Revert")) && bRepositoryFound)
    {
        Result = RunGitForFiles(TEXT("restore --source=HEAD --staged --worktree --"), AbsoluteFiles)
            ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (OperationName == FName(TEXT("Sync")) && bRepositoryFound)
    {
        const TSharedRef<FSync, ESPMode::ThreadSafe> Sync = StaticCastSharedRef<FSync>(InOperation);
        const FString Revision = Sync->GetRevision().IsEmpty() ? TEXT("HEAD") : Sync->GetRevision();
        Result = RunGitForFiles(TEXT("restore --source=") + QuoteArgument(Revision) + TEXT(" --worktree --"), AbsoluteFiles)
            ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (OperationName == FName(TEXT("CheckIn")) && bRepositoryFound)
    {
        const TSharedRef<FCheckIn, ESPMode::ThreadSafe> CheckIn = StaticCastSharedRef<FCheckIn>(InOperation);
        FString Output;
        const FString Message = CheckIn->GetDescription().ToString().TrimStartAndEnd();
        const bool bStaged = !Message.IsEmpty() && RunGitForFiles(TEXT("add -A --"), AbsoluteFiles);
        const bool bCommitted = bStaged && RunGit(
            RepositoryRoot,
            TEXT("commit -m ") + QuoteArgument(Message),
            Output);
        if (bCommitted)
        {
            CheckIn->SetSuccessMessage(LOCTEXT("CommitSucceeded", "Git commit created locally."));
            RefreshStates(AbsoluteFiles);
        }
        Result = bCommitted ? ECommandResult::Succeeded : ECommandResult::Failed;
    }

    InOperationCompleteDelegate.ExecuteIfBound(InOperation, Result);
    return Result;
}

#if CYREVISION_UE5_3_OR_LATER
bool FCyRevisionSourceControlProvider::CanExecuteOperation(const FSourceControlOperationRef& InOperation) const
{
    const FName Name = InOperation->GetName();
    return Name == FName(TEXT("Connect")) ||
        Name == FName(TEXT("UpdateStatus")) ||
        Name == FName(TEXT("CheckOut")) ||
        Name == FName(TEXT("MarkForAdd")) ||
        Name == FName(TEXT("Delete")) ||
        Name == FName(TEXT("Revert")) ||
        Name == FName(TEXT("Sync")) ||
        Name == FName(TEXT("CheckIn"));
}
#endif

#if CYREVISION_UE5
bool FCyRevisionSourceControlProvider::CanCancelOperation(const FSourceControlOperationRef&) const { return false; }
void FCyRevisionSourceControlProvider::CancelOperation(const FSourceControlOperationRef&) {}
#else
bool FCyRevisionSourceControlProvider::CanCancelOperation(
    const TSharedRef<ISourceControlOperation, ESPMode::ThreadSafe>&) const { return false; }
void FCyRevisionSourceControlProvider::CancelOperation(
    const TSharedRef<ISourceControlOperation, ESPMode::ThreadSafe>&) {}
#endif
bool FCyRevisionSourceControlProvider::UsesLocalReadOnlyState() const { return false; }
bool FCyRevisionSourceControlProvider::UsesChangelists() const { return false; }
#if CYREVISION_UE5
bool FCyRevisionSourceControlProvider::UsesUncontrolledChangelists() const { return true; }
#endif
bool FCyRevisionSourceControlProvider::UsesCheckout() const { return false; }
#if CYREVISION_UE5
bool FCyRevisionSourceControlProvider::UsesFileRevisions() const { return true; }
bool FCyRevisionSourceControlProvider::UsesSnapshots() const { return false; }
#if CYREVISION_UE5_8_OR_LATER
bool FCyRevisionSourceControlProvider::UsesSoftRevertOnDelete() const { return false; }
#endif
bool FCyRevisionSourceControlProvider::AllowsDiffAgainstDepot() const { return true; }
#if CYREVISION_UE5_8_OR_LATER
TOptional<bool> FCyRevisionSourceControlProvider::HasChangesToSync() const { return {}; }

TOptional<bool> FCyRevisionSourceControlProvider::HasChangesToCheckIn() const
{
    const TOptional<int> ChangeCount = CountLocalChanges();
    return ChangeCount.IsSet() ? TOptional<bool>(ChangeCount.GetValue() > 0) : TOptional<bool>();
}
#else
TOptional<bool> FCyRevisionSourceControlProvider::IsAtLatestRevision() const { return {}; }

TOptional<int> FCyRevisionSourceControlProvider::GetNumLocalChanges() const
{
    return CountLocalChanges();
}
#endif
#endif

TOptional<int> FCyRevisionSourceControlProvider::CountLocalChanges() const
{
    if (!bRepositoryFound)
    {
        return {};
    }
    FString Output;
    if (!RunGit(RepositoryRoot, TEXT("status --porcelain=v1 --untracked-files=all"), Output))
    {
        return {};
    }
    TArray<FString> Lines;
    Output.ParseIntoArrayLines(Lines, true);
    return Lines.Num();
}

void FCyRevisionSourceControlProvider::Tick() {}
TArray<TSharedRef<ISourceControlLabel>> FCyRevisionSourceControlProvider::GetLabels(const FString&) const { return {}; }
#if CYREVISION_UE5
TArray<FSourceControlChangelistRef> FCyRevisionSourceControlProvider::GetChangelists(EStateCacheUsage::Type) { return {}; }
#endif

#if SOURCE_CONTROL_WITH_SLATE
TSharedRef<SWidget> FCyRevisionSourceControlProvider::MakeSettingsWidget() const
{
    return SNew(SBorder)
        .Padding(8.0f)
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot()
            .AutoHeight()
            [
                SNew(STextBlock)
                .Text(LOCTEXT("SettingsHeading", "CyRevision — Git without mandatory checkout"))
                .Font(FCoreStyle::GetDefaultFontStyle(TEXT("Bold"), 12))
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0.0f, 6.0f, 0.0f, 8.0f)
            [
                SNew(STextBlock)
                .Text_Lambda([this]() { return GetStatusText(); })
                .AutoWrapText(true)
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            [
                SNew(STextBlock)
                .Text(LOCTEXT(
                    "SettingsSafety",
                    "This provider reports local Git file states and never creates a checkout lock. Use Tools > CyRevision for commits, history, LFS locks, advisory reservations and the external client."))
                .AutoWrapText(true)
            ]
        ];
}
#endif

bool FCyRevisionSourceControlProvider::RefreshConnection()
{
    StateCache.Empty();
    bRepositoryFound = false;
    RepositoryRoot.Empty();
    BranchName.Empty();
    RemoteUrl.Empty();
    UserName.Empty();

    FString VersionOutput;
    bGitAvailable = RunGit(FPaths::ProjectDir(), TEXT("--version"), VersionOutput);
    VersionOutput.TrimStartAndEndInline();
    GitVersion = VersionOutput;
    if (!bGitAvailable)
    {
        return false;
    }

    FString RootOutput;
    if (!RunGit(FPaths::ProjectDir(), TEXT("rev-parse --show-toplevel"), RootOutput))
    {
        return false;
    }
    RootOutput.TrimStartAndEndInline();
    RepositoryRoot = FPaths::ConvertRelativePathToFull(RootOutput);
    FPaths::NormalizeDirectoryName(RepositoryRoot);
    bRepositoryFound = !RepositoryRoot.IsEmpty();
    if (!bRepositoryFound)
    {
        return false;
    }

    RunGit(RepositoryRoot, TEXT("branch --show-current"), BranchName);
    BranchName.TrimStartAndEndInline();
    RunGit(RepositoryRoot, TEXT("remote get-url origin"), RemoteUrl);
    RemoteUrl.TrimStartAndEndInline();
    RunGit(RepositoryRoot, TEXT("config user.name"), UserName);
    UserName.TrimStartAndEndInline();
    return true;
}

bool FCyRevisionSourceControlProvider::RefreshStates(const TArray<FString>& InFiles)
{
    if (!bRepositoryFound)
    {
        return false;
    }
    for (const FString& File : InFiles)
    {
        QueryFileState(GetStateInternal(File).Get());
    }
    OnSourceControlStateChanged.Broadcast();
    return true;
}

bool FCyRevisionSourceControlProvider::RefreshHistories(const TArray<FString>& InFiles)
{
    if (!bRepositoryFound)
    {
        return false;
    }
    bool bSucceeded = true;
    for (const FString& File : InFiles)
    {
        if (IFileManager::Get().DirectoryExists(*File))
        {
            continue;
        }
        bSucceeded = QueryFileHistory(GetStateInternal(File).Get()) && bSucceeded;
    }
    OnSourceControlStateChanged.Broadcast();
    return bSucceeded;
}

bool FCyRevisionSourceControlProvider::QueryFileHistory(FCyRevisionSourceControlState& State) const
{
    FString RelativeFilename;
    if (!MakeRepositoryRelative(State.GetFilename(), RelativeFilename))
    {
        State.SetHistory({});
        return false;
    }

    FString Output;
    const FString Arguments = FString::Printf(
        TEXT("log -100 --follow --date=iso-strict --format=%%H%%x1f%%an%%x1f%%aI%%x1f%%s%%x1e -- %s"),
        *QuoteArgument(RelativeFilename));
    if (!RunGit(RepositoryRoot, Arguments, Output))
    {
        State.SetHistory({});
        return false;
    }

    FString RecordSeparator;
    RecordSeparator.AppendChar(0x1e);
    FString FieldSeparator;
    FieldSeparator.AppendChar(0x1f);
    TArray<FString> Records;
    Output.ParseIntoArray(Records, *RecordSeparator, true);
    TArray<TSharedRef<ISourceControlRevision, ESPMode::ThreadSafe>> History;
    int32 Number = Records.Num();
    const int64 CurrentSize = IFileManager::Get().FileSize(*State.GetFilename());
    for (FString Record : Records)
    {
        Record.TrimStartAndEndInline();
        TArray<FString> Fields;
        Record.ParseIntoArray(Fields, *FieldSeparator, false);
        if (Fields.Num() < 4 || Fields[0].IsEmpty())
        {
            continue;
        }
        FDateTime Date = FDateTime::MinValue();
        FDateTime::ParseIso8601(*Fields[2], Date);
        History.Add(MakeShared<FCyRevisionSourceControlRevision, ESPMode::ThreadSafe>(
            State.GetFilename(),
            RepositoryRoot,
            RelativeFilename,
            Fields[0],
            Fields[3],
            Fields[1],
            TEXT("edit"),
            Date,
            Number--,
            CurrentSize > 0 ? static_cast<int32>(FMath::Min<int64>(CurrentSize, MAX_int32)) : 0));
    }
    State.SetHistory(MoveTemp(History));
    return true;
}

bool FCyRevisionSourceControlProvider::RunGitForFiles(
    const FString& Prefix,
    const TArray<FString>& InFiles,
    const FString& Suffix) const
{
    bool bSucceeded = true;
    for (const FString& File : InFiles)
    {
        FString RelativeFilename;
        if (!MakeRepositoryRelative(File, RelativeFilename))
        {
            bSucceeded = false;
            continue;
        }
        FString Output;
        const FString Arguments = Prefix + TEXT(" ") + QuoteArgument(RelativeFilename) + Suffix;
        bSucceeded = RunGit(RepositoryRoot, Arguments, Output) && bSucceeded;
    }
    return bSucceeded;
}

bool FCyRevisionSourceControlProvider::MakeRepositoryRelative(
    const FString& Filename,
    FString& OutRelativeFilename) const
{
    OutRelativeFilename = FPaths::ConvertRelativePathToFull(Filename);
    FPaths::NormalizeFilename(OutRelativeFilename);
    if (!FPaths::MakePathRelativeTo(OutRelativeFilename, *RepositoryRoot) || OutRelativeFilename.StartsWith(TEXT("..")))
    {
        return false;
    }
    FPaths::NormalizeFilename(OutRelativeFilename);
    return !OutRelativeFilename.IsEmpty();
}

TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe>
FCyRevisionSourceControlProvider::GetStateInternal(const FString& Filename)
{
    FString AbsoluteFilename = FPaths::ConvertRelativePathToFull(Filename);
    FPaths::NormalizeFilename(AbsoluteFilename);
    if (TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe>* Existing = StateCache.Find(AbsoluteFilename))
    {
        return *Existing;
    }
    TSharedRef<FCyRevisionSourceControlState, ESPMode::ThreadSafe> State =
        MakeShared<FCyRevisionSourceControlState, ESPMode::ThreadSafe>(AbsoluteFilename);
    StateCache.Add(AbsoluteFilename, State);
    return State;
}

void FCyRevisionSourceControlProvider::QueryFileState(FCyRevisionSourceControlState& State) const
{
    FString RelativeFilename;
    if (!MakeRepositoryRelative(State.GetFilename(), RelativeFilename))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Unknown);
        return;
    }

    FString StatusOutput;
    const FString StatusArguments = FString::Printf(
        TEXT("status --porcelain=v1 --untracked-files=all -- %s"),
        *QuoteArgument(RelativeFilename));
    if (!RunGit(RepositoryRoot, StatusArguments, StatusOutput))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Unknown);
        return;
    }
    StatusOutput.TrimEndInline();
    if (StatusOutput.IsEmpty())
    {
        FString IgnoreOutput;
        const FString IgnoreArguments = FString::Printf(TEXT("check-ignore -- %s"), *QuoteArgument(RelativeFilename));
        State.SetWorkingCopyState(RunGit(RepositoryRoot, IgnoreArguments, IgnoreOutput)
            ? ECyRevisionWorkingCopyState::Ignored
            : ECyRevisionWorkingCopyState::Unchanged);
        return;
    }

    const TCHAR IndexState = StatusOutput.Len() > 0 ? StatusOutput[0] : TEXT(' ');
    const TCHAR WorkTreeState = StatusOutput.Len() > 1 ? StatusOutput[1] : TEXT(' ');
    if (IndexState == TEXT('?') && WorkTreeState == TEXT('?'))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::NotControlled);
    }
    else if (IndexState == TEXT('!') && WorkTreeState == TEXT('!'))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Ignored);
    }
    else if (IsConflictStatus(IndexState, WorkTreeState))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Conflicted);
    }
    else if (IndexState == TEXT('R') || WorkTreeState == TEXT('R'))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Renamed);
    }
    else if (IndexState == TEXT('D') || WorkTreeState == TEXT('D'))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Deleted);
    }
    else if (IndexState == TEXT('A') || WorkTreeState == TEXT('A'))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Added);
    }
    else
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Modified);
    }
}

bool FCyRevisionSourceControlProvider::RunGit(
    const FString& WorkingDirectory,
    const FString& Arguments,
    FString& OutStdOut,
    FString* OutStdErr) const
{
    FString GitBinary = FPlatformMisc::GetEnvironmentVariable(TEXT("CYREVISION_GIT_PATH"));
    if (GitBinary.IsEmpty())
    {
#if PLATFORM_WINDOWS
        GitBinary = TEXT("git.exe");
#else
        GitBinary = TEXT("git");
#endif
    }

    const FString Parameters = FString::Printf(
        TEXT("-C %s %s"),
        *QuoteArgument(FPaths::ConvertRelativePathToFull(WorkingDirectory)),
        *Arguments);
    FString ErrorOutput;
    int32 ReturnCode = INDEX_NONE;
    const bool bStarted = FPlatformProcess::ExecProcess(
        *GitBinary,
        *Parameters,
        &ReturnCode,
        &OutStdOut,
        OutStdErr ? OutStdErr : &ErrorOutput,
        *WorkingDirectory);
    return bStarted && ReturnCode == 0;
}

FString FCyRevisionSourceControlProvider::QuoteArgument(const FString& Argument)
{
    FString Escaped = Argument;
    Escaped.ReplaceInline(TEXT("\""), TEXT("\\\""));
    return FString::Printf(TEXT("\"%s\""), *Escaped);
}

#undef LOCTEXT_NAMESPACE
