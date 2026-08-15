#include "CyRevisionAssetInspectCommandlet.h"

#include "AssetRegistry/AssetData.h"
#include "Dom/JsonObject.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "Engine/Blueprint.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "Engine/Texture2D.h"
#include "ImageUtils.h"
#include "Misc/FileHelper.h"
#include "Misc/ObjectThumbnail.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "ObjectTools.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "ShaderCompiler.h"
#include "RenderingThread.h"
#include "UObject/Package.h"
#include "UObject/UObjectHash.h"

namespace CyRevisionAssetInspect
{
    constexpr int32 MaximumBlueprintNodes = 10000;
    constexpr int32 MaximumBlueprintPins = 60000;

    struct FGraphDescriptor
    {
        FString Kind;
        UEdGraph* Graph = nullptr;
    };

    static bool SaveManifest(const TSharedRef<FJsonObject>& Manifest, const FString& OutputDirectory)
    {
        FString Json;
        const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Json);
        if (!FJsonSerializer::Serialize(Manifest, Writer))
        {
            return false;
        }
        return FFileHelper::SaveStringToFile(
            Json,
            *FPaths::Combine(OutputDirectory, TEXT("inspection.json")),
            FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
    }

    static bool WriteThumbnailPng(
        const FObjectThumbnail& Thumbnail,
        const FString& OutputDirectory,
        const int32 Resolution)
    {
        const int32 SourceWidth = Thumbnail.GetImageWidth();
        const int32 SourceHeight = Thumbnail.GetImageHeight();
        const TArray<uint8>& RawData = Thumbnail.GetUncompressedImageData();
        if (SourceWidth <= 0 || SourceHeight <= 0 || RawData.Num() != SourceWidth * SourceHeight * 4)
        {
            return false;
        }

        TArray<FColor> SourceColors;
        SourceColors.SetNumUninitialized(SourceWidth * SourceHeight);
        FMemory::Memcpy(SourceColors.GetData(), RawData.GetData(), RawData.Num());
        TArray<FColor> OutputColors;
        if (SourceWidth == Resolution && SourceHeight == Resolution)
        {
            OutputColors = MoveTemp(SourceColors);
        }
        else
        {
            FImageUtils::ImageResize(
                SourceWidth,
                SourceHeight,
                SourceColors,
                Resolution,
                Resolution,
                OutputColors,
                true,
                true);
        }

        TArray<uint8> PngData;
        FImageUtils::ThumbnailCompressImageArray(Resolution, Resolution, OutputColors, PngData);
        return PngData.Num() > 0 && FFileHelper::SaveArrayToFile(
            PngData,
            *FPaths::Combine(OutputDirectory, TEXT("preview.png")));
    }

    static bool SaveThumbnail(
        UObject* Asset,
        const FString& OutputDirectory,
        const int32 Resolution,
        const bool bAllowRenderFallback)
    {
        // Most Unreal packages already contain the same thumbnail shown by the Content Browser.
        // Reusing it avoids engine startup rendering and shader compilation, then we normalize it
        // to the resolution requested by CyRevision.
        FObjectThumbnail StoredThumbnail;
        if (ThumbnailTools::LoadThumbnailFromPackage(FAssetData(Asset), StoredThumbnail) &&
            WriteThumbnailPng(StoredThumbnail, OutputDirectory, Resolution))
        {
            return true;
        }

        // Blueprint semantic inspection and package metadata do not need a render device. This
        // keeps the default headless pass fast and, critically, avoids starting a full farm of
        // ShaderCompileWorker processes merely to inspect a Blueprint.
        if (!bAllowRenderFallback)
        {
            return false;
        }

        // Thumbnail renderers may queue material shaders while the asset is loading. A commandlet
        // does not have the regular editor tick loop, so wait explicitly or the first capture can
        // be a valid but entirely black PNG.
        if (GShaderCompilingManager != nullptr)
        {
            GShaderCompilingManager->FinishAllCompilation();
        }
        FlushRenderingCommands();

        FObjectThumbnail Thumbnail;
        ThumbnailTools::RenderThumbnail(
            Asset,
            static_cast<uint32>(Resolution),
            static_cast<uint32>(Resolution),
            ThumbnailTools::EThumbnailTextureFlushMode::NeverFlush,
            nullptr,
            &Thumbnail);

        return WriteThumbnailPng(Thumbnail, OutputDirectory, Resolution);
    }

    static void AddVector(const TSharedRef<FJsonObject>& Manifest, const TCHAR* Prefix, const FVector& Value)
    {
        Manifest->SetNumberField(FString(Prefix) + TEXT("X"), Value.X);
        Manifest->SetNumberField(FString(Prefix) + TEXT("Y"), Value.Y);
        Manifest->SetNumberField(FString(Prefix) + TEXT("Z"), Value.Z);
    }

    static FString NodeKey(const UEdGraphNode* Node)
    {
        if (Node == nullptr)
        {
            return TEXT("missing-node");
        }
        return Node->NodeGuid.IsValid()
            ? Node->NodeGuid.ToString(EGuidFormats::DigitsWithHyphensLower)
            : FString::Printf(TEXT("name:%s"), *Node->GetName());
    }

    static FString PinTypeText(const FEdGraphPinType& PinType)
    {
        FString Result = PinType.PinCategory.ToString();
        if (!PinType.PinSubCategory.IsNone())
        {
            Result += TEXT(":") + PinType.PinSubCategory.ToString();
        }
        if (const UObject* SubCategoryObject = PinType.PinSubCategoryObject.Get())
        {
            Result += TEXT(":") + SubCategoryObject->GetPathName();
        }
        switch (PinType.ContainerType)
        {
        case EPinContainerType::Array:
            Result = TEXT("Array<") + Result + TEXT(">");
            break;
        case EPinContainerType::Set:
            Result = TEXT("Set<") + Result + TEXT(">");
            break;
        case EPinContainerType::Map:
            Result = TEXT("Map<") + Result + TEXT(">");
            break;
        default:
            break;
        }
        if (PinType.bIsReference)
        {
            Result += TEXT("&");
        }
        if (PinType.bIsConst)
        {
            Result = TEXT("const ") + Result;
        }
        return Result;
    }

    static FString PinDefaultText(const UEdGraphPin* Pin)
    {
        if (Pin == nullptr)
        {
            return FString();
        }
        if (!Pin->DefaultValue.IsEmpty())
        {
            return Pin->DefaultValue;
        }
        if (Pin->DefaultObject != nullptr)
        {
            return Pin->DefaultObject->GetPathName();
        }
        return Pin->DefaultTextValue.ToString();
    }

    template <typename GraphArrayType>
    static void AddGraphSet(
        const GraphArrayType& Source,
        const TCHAR* Kind,
        TArray<FGraphDescriptor>& Destination)
    {
        for (UEdGraph* Graph : Source)
        {
            if (Graph != nullptr)
            {
                Destination.Add({Kind, Graph});
            }
        }
    }

    static TSharedRef<FJsonObject> DescribePin(const UEdGraphPin* Pin, const int32 PinIndex)
    {
        const TSharedRef<FJsonObject> Description = MakeShared<FJsonObject>();
        const FString Direction = Pin->Direction == EGPD_Input ? TEXT("Input") : TEXT("Output");
        Description->SetStringField(TEXT("key"), FString::Printf(
            TEXT("%s:%s:%d"),
            *Direction,
            *Pin->PinName.ToString(),
            PinIndex));
        Description->SetStringField(TEXT("name"), Pin->PinName.ToString());
        Description->SetStringField(TEXT("direction"), Direction);
        Description->SetStringField(TEXT("type"), PinTypeText(Pin->PinType));
        Description->SetStringField(TEXT("default"), PinDefaultText(Pin));

        TArray<FString> LinkKeys;
        for (const UEdGraphPin* LinkedPin : Pin->LinkedTo)
        {
            if (LinkedPin == nullptr)
            {
                continue;
            }
            LinkKeys.Add(FString::Printf(
                TEXT("%s:%s:%s"),
                *NodeKey(LinkedPin->GetOwningNodeUnchecked()),
                LinkedPin->Direction == EGPD_Input ? TEXT("Input") : TEXT("Output"),
                *LinkedPin->PinName.ToString()));
        }
        LinkKeys.Sort();
        TArray<TSharedPtr<FJsonValue>> Links;
        for (const FString& Link : LinkKeys)
        {
            Links.Add(MakeShared<FJsonValueString>(Link));
        }
        Description->SetArrayField(TEXT("links"), Links);
        return Description;
    }

    static TSharedRef<FJsonObject> DescribeNode(
        const UEdGraphNode* Node,
        int32& TotalPins,
        bool& bPinsTruncated)
    {
        const TSharedRef<FJsonObject> Description = MakeShared<FJsonObject>();
        Description->SetStringField(TEXT("key"), NodeKey(Node));
        Description->SetStringField(TEXT("guid"), Node->NodeGuid.IsValid()
            ? Node->NodeGuid.ToString(EGuidFormats::DigitsWithHyphensLower)
            : FString());
        Description->SetStringField(TEXT("name"), Node->GetName());
        Description->SetStringField(TEXT("class"), Node->GetClass()->GetPathName());
        Description->SetStringField(TEXT("title"), Node->GetNodeTitle(ENodeTitleType::ListView).ToString());
        Description->SetNumberField(TEXT("x"), Node->NodePosX);
        Description->SetNumberField(TEXT("y"), Node->NodePosY);

        TArray<TSharedPtr<FJsonValue>> Pins;
        for (int32 Index = 0; Index < Node->Pins.Num(); ++Index)
        {
            const UEdGraphPin* Pin = Node->Pins[Index];
            if (Pin == nullptr)
            {
                continue;
            }
            if (TotalPins >= MaximumBlueprintPins)
            {
                bPinsTruncated = true;
                break;
            }
            Pins.Add(MakeShared<FJsonValueObject>(DescribePin(Pin, Index)));
            ++TotalPins;
        }
        Description->SetArrayField(TEXT("pins"), Pins);
        return Description;
    }

    static void AddBlueprintManifest(const UBlueprint* Blueprint, const TSharedRef<FJsonObject>& Manifest)
    {
        const TSharedRef<FJsonObject> BlueprintJson = MakeShared<FJsonObject>();
        BlueprintJson->SetStringField(
            TEXT("parentClass"),
            Blueprint->ParentClass ? Blueprint->ParentClass->GetPathName() : TEXT("None"));

        TArray<const FBPVariableDescription*> SortedVariables;
        for (const FBPVariableDescription& Variable : Blueprint->NewVariables)
        {
            SortedVariables.Add(&Variable);
        }
        SortedVariables.Sort([](const FBPVariableDescription& Left, const FBPVariableDescription& Right)
        {
            const FString LeftKey = Left.VarGuid.IsValid()
                ? Left.VarGuid.ToString(EGuidFormats::DigitsWithHyphensLower)
                : Left.VarName.ToString();
            const FString RightKey = Right.VarGuid.IsValid()
                ? Right.VarGuid.ToString(EGuidFormats::DigitsWithHyphensLower)
                : Right.VarName.ToString();
            return LeftKey < RightKey;
        });

        TArray<TSharedPtr<FJsonValue>> Variables;
        for (const FBPVariableDescription* Variable : SortedVariables)
        {
            const TSharedRef<FJsonObject> VariableJson = MakeShared<FJsonObject>();
            VariableJson->SetStringField(TEXT("key"), Variable->VarGuid.IsValid()
                ? Variable->VarGuid.ToString(EGuidFormats::DigitsWithHyphensLower)
                : FString(TEXT("name:")) + Variable->VarName.ToString());
            VariableJson->SetStringField(TEXT("guid"), Variable->VarGuid.IsValid()
                ? Variable->VarGuid.ToString(EGuidFormats::DigitsWithHyphensLower)
                : FString());
            VariableJson->SetStringField(TEXT("name"), Variable->VarName.ToString());
            VariableJson->SetStringField(TEXT("friendlyName"), Variable->FriendlyName);
            VariableJson->SetStringField(TEXT("type"), PinTypeText(Variable->VarType));
            VariableJson->SetStringField(TEXT("default"), Variable->DefaultValue);
            VariableJson->SetStringField(TEXT("category"), Variable->Category.ToString());
            VariableJson->SetStringField(TEXT("repNotify"), Variable->RepNotifyFunc.ToString());
            Variables.Add(MakeShared<FJsonValueObject>(VariableJson));
        }
        BlueprintJson->SetArrayField(TEXT("variables"), Variables);

        TArray<FGraphDescriptor> Graphs;
#if WITH_EDITORONLY_DATA
        AddGraphSet(Blueprint->UbergraphPages, TEXT("Event"), Graphs);
        AddGraphSet(Blueprint->FunctionGraphs, TEXT("Function"), Graphs);
        AddGraphSet(Blueprint->MacroGraphs, TEXT("Macro"), Graphs);
        AddGraphSet(Blueprint->DelegateSignatureGraphs, TEXT("Delegate"), Graphs);
#endif
        Graphs.Sort([](const FGraphDescriptor& Left, const FGraphDescriptor& Right)
        {
            const FString LeftKey = Left.Kind + TEXT(":") + Left.Graph->GetName();
            const FString RightKey = Right.Kind + TEXT(":") + Right.Graph->GetName();
            return LeftKey < RightKey;
        });

        int32 TotalNodes = 0;
        int32 TotalPins = 0;
        bool bNodesTruncated = false;
        bool bPinsTruncated = false;
        TArray<TSharedPtr<FJsonValue>> GraphValues;
        for (const FGraphDescriptor& GraphDescriptor : Graphs)
        {
            const TSharedRef<FJsonObject> GraphJson = MakeShared<FJsonObject>();
            GraphJson->SetStringField(TEXT("key"), GraphDescriptor.Kind + TEXT(":") + GraphDescriptor.Graph->GetName());
            GraphJson->SetStringField(TEXT("name"), GraphDescriptor.Graph->GetName());
            GraphJson->SetStringField(TEXT("kind"), GraphDescriptor.Kind);
            GraphJson->SetStringField(TEXT("schema"), GraphDescriptor.Graph->GetSchema()
                ? GraphDescriptor.Graph->GetSchema()->GetClass()->GetPathName()
                : TEXT("None"));

            TArray<UEdGraphNode*> SortedNodes;
            for (UEdGraphNode* Node : GraphDescriptor.Graph->Nodes)
            {
                if (Node != nullptr)
                {
                    SortedNodes.Add(Node);
                }
            }
            SortedNodes.Sort([](const UEdGraphNode& Left, const UEdGraphNode& Right)
            {
                return NodeKey(&Left) < NodeKey(&Right);
            });

            TArray<TSharedPtr<FJsonValue>> Nodes;
            for (const UEdGraphNode* Node : SortedNodes)
            {
                if (TotalNodes >= MaximumBlueprintNodes)
                {
                    bNodesTruncated = true;
                    break;
                }
                Nodes.Add(MakeShared<FJsonValueObject>(DescribeNode(Node, TotalPins, bPinsTruncated)));
                ++TotalNodes;
            }
            GraphJson->SetArrayField(TEXT("nodes"), Nodes);
            GraphValues.Add(MakeShared<FJsonValueObject>(GraphJson));
            if (bNodesTruncated)
            {
                break;
            }
        }
        BlueprintJson->SetArrayField(TEXT("graphs"), GraphValues);
        BlueprintJson->SetNumberField(TEXT("graphCount"), GraphValues.Num());
        BlueprintJson->SetNumberField(TEXT("nodeCount"), TotalNodes);
        BlueprintJson->SetNumberField(TEXT("pinCount"), TotalPins);
        BlueprintJson->SetBoolField(TEXT("nodesTruncated"), bNodesTruncated);
        BlueprintJson->SetBoolField(TEXT("pinsTruncated"), bPinsTruncated);
        Manifest->SetObjectField(TEXT("blueprint"), BlueprintJson);
    }

    static UObject* LoadRequestedAsset(const FString& AssetPath, const FString& PackageFile)
    {
        if (!AssetPath.IsEmpty())
        {
            return StaticLoadObject(UObject::StaticClass(), nullptr, *AssetPath);
        }

        const FString FullPackageFile = FPaths::ConvertRelativePathToFull(PackageFile);
        UPackage* Package = LoadPackage(nullptr, *FullPackageFile, LOAD_None);
        if (Package == nullptr)
        {
            return nullptr;
        }

        TArray<UObject*> Objects;
        GetObjectsWithOuter(Package, Objects, false);
        Objects.Sort([](const UObject& Left, const UObject& Right)
        {
            return Left.GetPathName() < Right.GetPathName();
        });
        for (UObject* Object : Objects)
        {
            if (Object != nullptr && Object->IsA<UBlueprint>())
            {
                return Object;
            }
        }
        for (UObject* Object : Objects)
        {
            if (Object != nullptr && Object->IsAsset())
            {
                return Object;
            }
        }
        return Objects.Num() > 0 ? Objects[0] : nullptr;
    }
}

UCyRevisionAssetInspectCommandlet::UCyRevisionAssetInspectCommandlet()
{
    IsClient = false;
    IsEditor = true;
    LogToConsole = true;
    ShowErrorCount = true;
}

int32 UCyRevisionAssetInspectCommandlet::Main(const FString& Params)
{
    FString AssetPath;
    FString PackageFile;
    FString OutputDirectory;
    int32 Resolution = 512;
    int32 RenderMesh = 1;
    int32 RenderThumbnail = 0;
    FParse::Value(*Params, TEXT("Asset="), AssetPath);
    FParse::Value(*Params, TEXT("PackageFile="), PackageFile);
    FParse::Value(*Params, TEXT("Output="), OutputDirectory);
    FParse::Value(*Params, TEXT("Resolution="), Resolution);
    FParse::Value(*Params, TEXT("RenderMesh="), RenderMesh);
    FParse::Value(*Params, TEXT("RenderThumbnail="), RenderThumbnail);
    Resolution = FMath::Clamp(Resolution, 128, 2048);

    if ((AssetPath.IsEmpty() && PackageFile.IsEmpty()) || OutputDirectory.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("CyRevisionAssetInspect requires -Asset or -PackageFile, plus -Output."));
        return 2;
    }

    OutputDirectory = FPaths::ConvertRelativePathToFull(OutputDirectory);
    IFileManager::Get().MakeDirectory(*OutputDirectory, true);
    UObject* Asset = CyRevisionAssetInspect::LoadRequestedAsset(AssetPath, PackageFile);
    if (Asset == nullptr)
    {
        UE_LOG(LogTemp, Error, TEXT("CyRevision could not load asset %s%s"), *AssetPath, *PackageFile);
        return 3;
    }

    const TSharedRef<FJsonObject> Manifest = MakeShared<FJsonObject>();
    Manifest->SetNumberField(TEXT("schemaVersion"), 2);
    Manifest->SetStringField(TEXT("asset"), Asset->GetPathName());
    Manifest->SetStringField(TEXT("name"), Asset->GetName());
    Manifest->SetStringField(TEXT("class"), Asset->GetClass()->GetName());
    Manifest->SetStringField(TEXT("package"), Asset->GetOutermost()->GetName());
    Manifest->SetNumberField(TEXT("previewResolution"), Resolution);

    bool bMesh = false;
    if (const UStaticMesh* StaticMesh = Cast<UStaticMesh>(Asset))
    {
        bMesh = true;
        Manifest->SetStringField(TEXT("assetKind"), TEXT("Static mesh"));
        Manifest->SetNumberField(TEXT("lodCount"), StaticMesh->GetNumLODs());
        Manifest->SetNumberField(TEXT("materialSlots"), StaticMesh->GetStaticMaterials().Num());
        const FBoxSphereBounds Bounds = StaticMesh->GetBounds();
        CyRevisionAssetInspect::AddVector(Manifest, TEXT("boundsExtent"), Bounds.BoxExtent);
        Manifest->SetNumberField(TEXT("boundsRadius"), Bounds.SphereRadius);
    }
    else if (const USkeletalMesh* SkeletalMesh = Cast<USkeletalMesh>(Asset))
    {
        bMesh = true;
        Manifest->SetStringField(TEXT("assetKind"), TEXT("Skeletal mesh"));
        Manifest->SetNumberField(TEXT("lodCount"), SkeletalMesh->GetLODNum());
        Manifest->SetNumberField(TEXT("materialSlots"), SkeletalMesh->GetMaterials().Num());
        Manifest->SetStringField(
            TEXT("skeleton"),
            SkeletalMesh->GetSkeleton() ? SkeletalMesh->GetSkeleton()->GetPathName() : TEXT("None"));
        const FBoxSphereBounds Bounds = SkeletalMesh->GetBounds();
        CyRevisionAssetInspect::AddVector(Manifest, TEXT("boundsExtent"), Bounds.BoxExtent);
        Manifest->SetNumberField(TEXT("boundsRadius"), Bounds.SphereRadius);
    }
    else if (const UTexture2D* Texture = Cast<UTexture2D>(Asset))
    {
        Manifest->SetStringField(TEXT("assetKind"), TEXT("Texture"));
        Manifest->SetNumberField(TEXT("width"), Texture->GetSizeX());
        Manifest->SetNumberField(TEXT("height"), Texture->GetSizeY());
    }
    else if (const UBlueprint* Blueprint = Cast<UBlueprint>(Asset))
    {
        Manifest->SetStringField(TEXT("assetKind"), TEXT("Blueprint"));
        CyRevisionAssetInspect::AddBlueprintManifest(Blueprint, Manifest);
    }
    else
    {
        Manifest->SetStringField(TEXT("assetKind"), TEXT("Unreal asset"));
    }

    bool bThumbnailWritten = false;
    if (!bMesh || RenderMesh != 0)
    {
        bThumbnailWritten = CyRevisionAssetInspect::SaveThumbnail(
            Asset,
            OutputDirectory,
            Resolution,
            RenderThumbnail != 0);
    }
    Manifest->SetBoolField(TEXT("thumbnailWritten"), bThumbnailWritten);
    Manifest->SetBoolField(TEXT("renderAttempted"), RenderThumbnail != 0);
    if (!CyRevisionAssetInspect::SaveManifest(Manifest, OutputDirectory))
    {
        UE_LOG(LogTemp, Error, TEXT("CyRevision could not write inspection.json to %s"), *OutputDirectory);
        return 4;
    }

    UE_LOG(LogTemp, Display, TEXT("CyRevision inspected %s (%s), thumbnail=%s"),
        *Asset->GetPathName(),
        *Asset->GetClass()->GetName(),
        bThumbnailWritten ? TEXT("yes") : TEXT("no"));
    return 0;
}
