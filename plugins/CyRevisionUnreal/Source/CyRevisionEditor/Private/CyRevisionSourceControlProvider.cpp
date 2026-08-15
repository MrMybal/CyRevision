#include "CyRevisionSourceControlProvider.h"

#include "CyRevisionSourceControlState.h"
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
    if (InOperation->GetName() == FName(TEXT("Connect")))
    {
        Result = RefreshConnection() ? ECommandResult::Succeeded : ECommandResult::Failed;
    }
    else if (InOperation->GetName() == FName(TEXT("UpdateStatus")) && bRepositoryFound)
    {
        Result = RefreshStates(SourceControlHelpers::AbsoluteFilenames(InFiles))
            ? ECommandResult::Succeeded
            : ECommandResult::Failed;
    }

    InOperationCompleteDelegate.ExecuteIfBound(InOperation, Result);
    return Result;
}

#if CYREVISION_UE5_3_OR_LATER
bool FCyRevisionSourceControlProvider::CanExecuteOperation(const FSourceControlOperationRef& InOperation) const
{
    return InOperation->GetName() == FName(TEXT("Connect")) ||
        InOperation->GetName() == FName(TEXT("UpdateStatus"));
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
bool FCyRevisionSourceControlProvider::UsesFileRevisions() const { return false; }
bool FCyRevisionSourceControlProvider::UsesSnapshots() const { return false; }
#if CYREVISION_UE5_8_OR_LATER
bool FCyRevisionSourceControlProvider::UsesSoftRevertOnDelete() const { return false; }
#endif
bool FCyRevisionSourceControlProvider::AllowsDiffAgainstDepot() const { return false; }
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
    FString RelativeFilename = State.GetFilename();
    if (!FPaths::MakePathRelativeTo(RelativeFilename, *RepositoryRoot) || RelativeFilename.StartsWith(TEXT("..")))
    {
        State.SetWorkingCopyState(ECyRevisionWorkingCopyState::Unknown);
        return;
    }
    FPaths::NormalizeFilename(RelativeFilename);

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
