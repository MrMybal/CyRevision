#include "CyRevisionSourceControlState.h"
#include "ISourceControlRevision.h"

#if SOURCE_CONTROL_WITH_SLATE
#include "RevisionControlStyle/RevisionControlStyle.h"
#include "Textures/SlateIcon.h"
#endif

#define LOCTEXT_NAMESPACE "CyRevisionSourceControlState"

FCyRevisionSourceControlState::FCyRevisionSourceControlState(FString InFilename)
    : Filename(MoveTemp(InFilename))
    , TimeStamp(FDateTime::UtcNow())
{
}

void FCyRevisionSourceControlState::SetWorkingCopyState(ECyRevisionWorkingCopyState InState)
{
    WorkingCopyState = InState;
    TimeStamp = FDateTime::UtcNow();
}

void FCyRevisionSourceControlState::SetHistory(TArray<TSharedRef<ISourceControlRevision, ESPMode::ThreadSafe>> InHistory)
{
    History = MoveTemp(InHistory);
    TimeStamp = FDateTime::UtcNow();
}

int32 FCyRevisionSourceControlState::GetHistorySize() const { return History.Num(); }
TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FCyRevisionSourceControlState::GetHistoryItem(int32 Index) const
{
    if (History.IsValidIndex(Index))
    {
        return History[Index];
    }
    return nullptr;
}
TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FCyRevisionSourceControlState::FindHistoryRevision(int32 Number) const
{
    for (const TSharedRef<ISourceControlRevision, ESPMode::ThreadSafe>& Item : History)
    {
        if (Item->GetRevisionNumber() == Number || Item->GetCheckInIdentifier() == Number) return Item;
    }
    return nullptr;
}
TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FCyRevisionSourceControlState::FindHistoryRevision(const FString& Revision) const
{
    for (const TSharedRef<ISourceControlRevision, ESPMode::ThreadSafe>& Item : History)
    {
        if (Item->GetRevision().Equals(Revision, ESearchCase::IgnoreCase) ||
            Item->GetRevision().StartsWith(Revision, ESearchCase::IgnoreCase)) return Item;
    }
    return nullptr;
}
#if CYREVISION_UE4 || (ENGINE_MAJOR_VERSION == 5 && ENGINE_MINOR_VERSION <= 2)
TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FCyRevisionSourceControlState::GetBaseRevForMerge() const
{
    if (History.Num() > 1)
    {
        return History[1];
    }
    return nullptr;
}
#endif
#if CYREVISION_UE5
TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FCyRevisionSourceControlState::GetCurrentRevision() const
{
    if (History.Num() > 0)
    {
        return History[0];
    }
    return nullptr;
}

#if SOURCE_CONTROL_WITH_SLATE
FSlateIcon FCyRevisionSourceControlState::GetIcon() const
{
    const FName StyleName = FRevisionControlStyleManager::GetStyleSetName();
    switch (WorkingCopyState)
    {
    case ECyRevisionWorkingCopyState::Modified:
        return FSlateIcon(StyleName, "RevisionControl.CheckedOut");
    case ECyRevisionWorkingCopyState::Added:
        return FSlateIcon(StyleName, "RevisionControl.OpenForAdd");
    case ECyRevisionWorkingCopyState::Deleted:
        return FSlateIcon(StyleName, "RevisionControl.MarkedForDelete");
    case ECyRevisionWorkingCopyState::Renamed:
        return FSlateIcon(StyleName, "RevisionControl.Branched");
    case ECyRevisionWorkingCopyState::Conflicted:
        return FSlateIcon(StyleName, "RevisionControl.Conflicted");
    case ECyRevisionWorkingCopyState::NotControlled:
        return FSlateIcon(StyleName, "RevisionControl.NotInDepot");
    default:
        return FSlateIcon();
    }
}
#endif
#else
FName FCyRevisionSourceControlState::GetIconName() const
{
    switch (WorkingCopyState)
    {
    case ECyRevisionWorkingCopyState::Modified: return FName(TEXT("Subversion.CheckedOut"));
    case ECyRevisionWorkingCopyState::Added: return FName(TEXT("Subversion.OpenForAdd"));
    case ECyRevisionWorkingCopyState::Deleted: return FName(TEXT("Subversion.MarkedForDelete"));
    case ECyRevisionWorkingCopyState::Renamed: return FName(TEXT("Subversion.Branched"));
    case ECyRevisionWorkingCopyState::Conflicted: return FName(TEXT("Subversion.NotAtHeadRevision"));
    case ECyRevisionWorkingCopyState::NotControlled: return FName(TEXT("Subversion.NotInDepot"));
    default: return NAME_None;
    }
}

FName FCyRevisionSourceControlState::GetSmallIconName() const
{
    const FName IconName = GetIconName();
    return IconName.IsNone() ? NAME_None : FName(*(IconName.ToString() + TEXT("_Small")));
}
#endif

FText FCyRevisionSourceControlState::GetDisplayName() const
{
    switch (WorkingCopyState)
    {
    case ECyRevisionWorkingCopyState::Unchanged: return LOCTEXT("Unchanged", "Unchanged");
    case ECyRevisionWorkingCopyState::Added: return LOCTEXT("Added", "Added");
    case ECyRevisionWorkingCopyState::Deleted: return LOCTEXT("Deleted", "Deleted");
    case ECyRevisionWorkingCopyState::Modified: return LOCTEXT("Modified", "Modified");
    case ECyRevisionWorkingCopyState::Renamed: return LOCTEXT("Renamed", "Renamed");
    case ECyRevisionWorkingCopyState::Conflicted: return LOCTEXT("Conflicted", "Conflicted");
    case ECyRevisionWorkingCopyState::NotControlled: return LOCTEXT("NotControlled", "Not under revision control");
    case ECyRevisionWorkingCopyState::Ignored: return LOCTEXT("Ignored", "Ignored");
    default: return LOCTEXT("Unknown", "Unknown");
    }
}

FText FCyRevisionSourceControlState::GetDisplayTooltip() const
{
    switch (WorkingCopyState)
    {
    case ECyRevisionWorkingCopyState::Unchanged: return LOCTEXT("UnchangedTooltip", "The file matches the local Git revision.");
    case ECyRevisionWorkingCopyState::Added: return LOCTEXT("AddedTooltip", "The file is staged or scheduled for addition in Git.");
    case ECyRevisionWorkingCopyState::Deleted: return LOCTEXT("DeletedTooltip", "The file is deleted in the Git working tree.");
    case ECyRevisionWorkingCopyState::Modified: return LOCTEXT("ModifiedTooltip", "The file contains local Git changes.");
    case ECyRevisionWorkingCopyState::Renamed: return LOCTEXT("RenamedTooltip", "Git reports the file as renamed.");
    case ECyRevisionWorkingCopyState::Conflicted: return LOCTEXT("ConflictedTooltip", "The file has an unresolved Git conflict.");
    case ECyRevisionWorkingCopyState::NotControlled: return LOCTEXT("NotControlledTooltip", "The file is not tracked by Git.");
    case ECyRevisionWorkingCopyState::Ignored: return LOCTEXT("IgnoredTooltip", "The file is ignored by Git.");
    default: return LOCTEXT("UnknownTooltip", "The Git state is not known yet.");
    }
}

const FString& FCyRevisionSourceControlState::GetFilename() const { return Filename; }
const FDateTime& FCyRevisionSourceControlState::GetTimeStamp() const { return TimeStamp; }

bool FCyRevisionSourceControlState::CanCheckIn() const
{
    return IsAdded() || IsDeleted() || IsModified() || IsConflicted();
}

bool FCyRevisionSourceControlState::CanCheckout() const { return false; }
bool FCyRevisionSourceControlState::IsCheckedOut() const
{
    // Git has no exclusive checkout step: every tracked working-tree file is already locally
    // editable. Reporting false here makes Unreal display its Perforce-style "Check Out Assets"
    // dialog on every save even though CyRevision deliberately uses an advisory workflow.
    return IsSourceControlled();
}
bool FCyRevisionSourceControlState::IsCheckedOutOther(FString*) const { return false; }
bool FCyRevisionSourceControlState::IsCheckedOutInOtherBranch(const FString&) const { return false; }
bool FCyRevisionSourceControlState::IsModifiedInOtherBranch(const FString&) const { return false; }
bool FCyRevisionSourceControlState::IsCheckedOutOrModifiedInOtherBranch(const FString&) const { return false; }
TArray<FString> FCyRevisionSourceControlState::GetCheckedOutBranches() const { return {}; }
FString FCyRevisionSourceControlState::GetOtherUserBranchCheckedOuts() const { return {}; }
bool FCyRevisionSourceControlState::GetOtherBranchHeadModification(FString&, FString&, int32&) const { return false; }
bool FCyRevisionSourceControlState::IsCurrent() const { return true; }

bool FCyRevisionSourceControlState::IsSourceControlled() const
{
    return WorkingCopyState != ECyRevisionWorkingCopyState::Unknown &&
        WorkingCopyState != ECyRevisionWorkingCopyState::NotControlled &&
        WorkingCopyState != ECyRevisionWorkingCopyState::Ignored;
}

bool FCyRevisionSourceControlState::IsAdded() const { return WorkingCopyState == ECyRevisionWorkingCopyState::Added; }
bool FCyRevisionSourceControlState::IsDeleted() const { return WorkingCopyState == ECyRevisionWorkingCopyState::Deleted; }
bool FCyRevisionSourceControlState::IsIgnored() const { return WorkingCopyState == ECyRevisionWorkingCopyState::Ignored; }
bool FCyRevisionSourceControlState::CanEdit() const { return true; }
bool FCyRevisionSourceControlState::CanDelete() const { return IsSourceControlled(); }
bool FCyRevisionSourceControlState::IsUnknown() const { return WorkingCopyState == ECyRevisionWorkingCopyState::Unknown; }

bool FCyRevisionSourceControlState::IsModified() const
{
    return WorkingCopyState == ECyRevisionWorkingCopyState::Added ||
        WorkingCopyState == ECyRevisionWorkingCopyState::Deleted ||
        WorkingCopyState == ECyRevisionWorkingCopyState::Modified ||
        WorkingCopyState == ECyRevisionWorkingCopyState::Renamed ||
        WorkingCopyState == ECyRevisionWorkingCopyState::Conflicted;
}

bool FCyRevisionSourceControlState::CanAdd() const { return WorkingCopyState == ECyRevisionWorkingCopyState::NotControlled; }
bool FCyRevisionSourceControlState::IsConflicted() const { return WorkingCopyState == ECyRevisionWorkingCopyState::Conflicted; }
bool FCyRevisionSourceControlState::CanRevert() const { return IsModified(); }

#undef LOCTEXT_NAMESPACE
