#pragma once

#include "CoreMinimal.h"
#include "Commandlets/Commandlet.h"
#include "CyRevisionAssetInspectCommandlet.generated.h"

/**
 * On-demand asset inspector used by the optional CyRevision desktop preview provider.
 * It writes a small JSON manifest and, when supported, a neutral thumbnail without
 * requiring the interactive Unreal Editor to remain open.
 */
UCLASS()
class CYREVISIONEDITOR_API UCyRevisionAssetInspectCommandlet final : public UCommandlet
{
    GENERATED_BODY()

public:
    UCyRevisionAssetInspectCommandlet();

    virtual int32 Main(const FString& Params) override;
};
