#include "CyRevisionSourceControlRevision.h"

#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformProcess.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

namespace
{
bool RunGitBinary(
    const FString& RepositoryRoot,
    const FString& Arguments,
    TArray<uint8>& OutData)
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

    void* ReadPipe = nullptr;
    void* WritePipe = nullptr;
    if (!FPlatformProcess::CreatePipe(ReadPipe, WritePipe))
    {
        return false;
    }

    const FString Parameters = FString::Printf(
        TEXT("-C %s %s"),
        *FCyRevisionSourceControlRevision::QuoteArgument(RepositoryRoot),
        *Arguments);
    FProcHandle Process = FPlatformProcess::CreateProc(
        *GitBinary,
        *Parameters,
        false,
        true,
        true,
        nullptr,
        0,
        *RepositoryRoot,
        WritePipe);
    if (!Process.IsValid())
    {
        FPlatformProcess::ClosePipe(ReadPipe, WritePipe);
        return false;
    }

    while (FPlatformProcess::IsProcRunning(Process))
    {
        FPlatformProcess::ReadPipeToArray(ReadPipe, OutData);
        FPlatformProcess::Sleep(0.005f);
    }
    FPlatformProcess::ReadPipeToArray(ReadPipe, OutData);

    int32 ReturnCode = INDEX_NONE;
    FPlatformProcess::GetProcReturnCode(Process, &ReturnCode);
    FPlatformProcess::CloseProc(Process);
    FPlatformProcess::ClosePipe(ReadPipe, WritePipe);
    return ReturnCode == 0;
}

bool ParseLfsOid(const TArray<uint8>& PointerData, FString& OutOid)
{
    if (PointerData.Num() == 0 || PointerData.Num() > 4096)
    {
        return false;
    }
    FUTF8ToTCHAR Converted(reinterpret_cast<const ANSICHAR*>(PointerData.GetData()), PointerData.Num());
    const FString Pointer(Converted.Length(), Converted.Get());
    if (!Pointer.StartsWith(TEXT("version https://git-lfs.github.com/spec/v1")))
    {
        return false;
    }
    const FString Marker(TEXT("oid sha256:"));
    const int32 MarkerIndex = Pointer.Find(Marker);
    if (MarkerIndex == INDEX_NONE)
    {
        return false;
    }
    int32 EndIndex = Pointer.Find(TEXT("\n"), ESearchCase::CaseSensitive, ESearchDir::FromStart, MarkerIndex);
    if (EndIndex == INDEX_NONE)
    {
        EndIndex = Pointer.Len();
    }
    OutOid = Pointer.Mid(MarkerIndex + Marker.Len(), EndIndex - MarkerIndex - Marker.Len()).TrimStartAndEnd();
    return OutOid.Len() == 64;
}
}

FCyRevisionSourceControlRevision::FCyRevisionSourceControlRevision(
    FString InFilename,
    FString InRepositoryRoot,
    FString InRelativeFilename,
    FString InRevision,
    FString InDescription,
    FString InUserName,
    FString InAction,
    FDateTime InDate,
    int32 InRevisionNumber,
    int32 InFileSize)
    : Filename(MoveTemp(InFilename))
    , RepositoryRoot(MoveTemp(InRepositoryRoot))
    , RelativeFilename(MoveTemp(InRelativeFilename))
    , Revision(MoveTemp(InRevision))
    , Description(MoveTemp(InDescription))
    , UserName(MoveTemp(InUserName))
    , Action(MoveTemp(InAction))
    , Date(InDate)
    , RevisionNumber(InRevisionNumber)
    , FileSize(InFileSize)
{
}

bool FCyRevisionSourceControlRevision::Get(FString& InOutFilename, EConcurrency::Type) const
{
    TArray<uint8> Data;
    if (!ReadGitObject(Data))
    {
        return false;
    }

    TArray<uint8> LfsData;
    if (ResolveLfsObject(Data, LfsData))
    {
        Data = MoveTemp(LfsData);
    }

    if (InOutFilename.IsEmpty())
    {
        const FString Extension = FPaths::GetExtension(Filename, true);
        const FString TempDirectory = FPaths::Combine(FPaths::ProjectIntermediateDir(), TEXT("CyRevision"), TEXT("History"));
        IFileManager::Get().MakeDirectory(*TempDirectory, true);
        InOutFilename = FPaths::CreateTempFilename(*TempDirectory, TEXT("Revision-"), *Extension);
    }
    else
    {
        IFileManager::Get().MakeDirectory(*FPaths::GetPath(InOutFilename), true);
    }
    return FFileHelper::SaveArrayToFile(Data, *InOutFilename);
}

bool FCyRevisionSourceControlRevision::GetAnnotated(TArray<FAnnotationLine>& OutLines) const
{
    FString TempFilename;
    if (!Get(TempFilename))
    {
        return false;
    }
    TArray<FString> Lines;
    if (!FFileHelper::LoadFileToStringArray(Lines, *TempFilename))
    {
        return false;
    }
    for (const FString& Line : Lines)
    {
        OutLines.Emplace(GetCheckInIdentifier(), UserName, Line);
    }
    return true;
}

bool FCyRevisionSourceControlRevision::GetAnnotated(FString& InOutFilename) const
{
    return Get(InOutFilename);
}

const FString& FCyRevisionSourceControlRevision::GetFilename() const { return Filename; }
int32 FCyRevisionSourceControlRevision::GetRevisionNumber() const { return RevisionNumber; }
const FString& FCyRevisionSourceControlRevision::GetRevision() const { return Revision; }
const FString& FCyRevisionSourceControlRevision::GetDescription() const { return Description; }
const FString& FCyRevisionSourceControlRevision::GetUserName() const { return UserName; }
const FString& FCyRevisionSourceControlRevision::GetClientSpec() const { return ClientSpec; }
const FString& FCyRevisionSourceControlRevision::GetAction() const { return Action; }
TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> FCyRevisionSourceControlRevision::GetBranchSource() const { return nullptr; }
const FDateTime& FCyRevisionSourceControlRevision::GetDate() const { return Date; }
int32 FCyRevisionSourceControlRevision::GetCheckInIdentifier() const { return static_cast<int32>(GetTypeHash(Revision) & MAX_int32); }
int32 FCyRevisionSourceControlRevision::GetFileSize() const { return FileSize; }

bool FCyRevisionSourceControlRevision::ReadGitObject(TArray<uint8>& OutData) const
{
    const FString Spec = Revision + TEXT(":") + RelativeFilename;
    return RunGitBinary(RepositoryRoot, TEXT("cat-file blob ") + QuoteArgument(Spec), OutData);
}

bool FCyRevisionSourceControlRevision::ResolveLfsObject(
    const TArray<uint8>& PointerData,
    TArray<uint8>& OutData) const
{
    FString Oid;
    if (!ParseLfsOid(PointerData, Oid))
    {
        return false;
    }

    FString CommonDirectoryOutput;
    int32 ReturnCode = INDEX_NONE;
    FString Error;
    FString GitBinary = FPlatformMisc::GetEnvironmentVariable(TEXT("CYREVISION_GIT_PATH"));
    if (GitBinary.IsEmpty())
    {
#if PLATFORM_WINDOWS
        GitBinary = TEXT("git.exe");
#else
        GitBinary = TEXT("git");
#endif
    }
    const FString Parameters = FString::Printf(TEXT("-C %s rev-parse --git-common-dir"), *QuoteArgument(RepositoryRoot));
    if (!FPlatformProcess::ExecProcess(*GitBinary, *Parameters, &ReturnCode, &CommonDirectoryOutput, &Error) || ReturnCode != 0)
    {
        return false;
    }
    CommonDirectoryOutput.TrimStartAndEndInline();
    const FString CommonDirectory = FPaths::IsRelative(CommonDirectoryOutput)
        ? FPaths::ConvertRelativePathToFull(RepositoryRoot, CommonDirectoryOutput)
        : CommonDirectoryOutput;
    const FString ObjectPath = FPaths::Combine(
        CommonDirectory,
        TEXT("lfs"),
        TEXT("objects"),
        Oid.Left(2),
        Oid.Mid(2, 2),
        Oid);
    return FFileHelper::LoadFileToArray(OutData, *ObjectPath);
}

FString FCyRevisionSourceControlRevision::QuoteArgument(const FString& Value)
{
    FString Escaped = Value;
    Escaped.ReplaceInline(TEXT("\\"), TEXT("\\\\"));
    Escaped.ReplaceInline(TEXT("\""), TEXT("\\\""));
    return FString::Printf(TEXT("\"%s\""), *Escaped);
}
