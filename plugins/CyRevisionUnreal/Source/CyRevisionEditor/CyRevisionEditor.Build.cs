using UnrealBuildTool;

public class CyRevisionEditor : ModuleRules
{
    public CyRevisionEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core" });
        PrivateDependencyModuleNames.AddRange(new[]
        {
            "ApplicationCore",
            "AssetRegistry",
            "ContentBrowser",
            "CoreUObject",
            "Engine",
            "HTTP",
            "InputCore",
            "Json",
            "Projects",
            "Slate",
            "SlateCore",
            "ToolMenus",
            "UnrealEd"
        });
    }
}
