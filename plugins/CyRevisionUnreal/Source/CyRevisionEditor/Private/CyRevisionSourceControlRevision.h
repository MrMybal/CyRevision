#pragma once

#include "CoreMinimal.h"
#include "ISourceControlRevision.h"

/**
 * Immutable Git revision exposed to Unreal's native revision-history and asset-diff tools.
 * File extraction is binary-safe, including .uasset/.umap payloads.
 */
class FCyRevisionSourceControlRevision final : public ISourceControlRevision
{
public:
    FCyRevisionSourceControlRevision(
        FString InFilename,
        FString InRepositoryRoot,
        FString InRelativeFilename,
        FString InRevision,
        FString InDescription,
        FString InUserName,
        FString InAction,
        FDateTime InDate,
        int32 InRevisionNumber,
        int32 InFileSize);

    virtual bool Get(FString& InOutFilename, EConcurrency::Type InConcurrency = EConcurrency::Synchronous) const override;
    virtual bool GetAnnotated(TArray<FAnnotationLine>& OutLines) const override;
    virtual bool GetAnnotated(FString& InOutFilename) const override;
    virtual const FString& GetFilename() const override;
    virtual int32 GetRevisionNumber() const override;
    virtual const FString& GetRevision() const override;
    virtual const FString& GetDescription() const override;
    virtual const FString& GetUserName() const override;
    virtual const FString& GetClientSpec() const override;
    virtual const FString& GetAction() const override;
    virtual TSharedPtr<ISourceControlRevision, ESPMode::ThreadSafe> GetBranchSource() const override;
    virtual const FDateTime& GetDate() const override;
    virtual int32 GetCheckInIdentifier() const override;
    virtual int32 GetFileSize() const override;

    static FString QuoteArgument(const FString& Value);

private:
    bool ReadGitObject(TArray<uint8>& OutData) const;
    bool ResolveLfsObject(const TArray<uint8>& PointerData, TArray<uint8>& OutData) const;

    FString Filename;
    FString RepositoryRoot;
    FString RelativeFilename;
    FString Revision;
    FString Description;
    FString UserName;
    FString ClientSpec;
    FString Action;
    FDateTime Date;
    int32 RevisionNumber = 0;
    int32 FileSize = 0;
};
