#include "CyRevisionEditorModule.h"

#include "Framework/Commands/UIAction.h"
#include "HAL/PlatformProcess.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/MessageDialog.h"
#include "Misc/Paths.h"
#include "ToolMenus.h"

#define LOCTEXT_NAMESPACE "FCyRevisionEditorModule"

void FCyRevisionEditorModule::StartupModule()
{
    UToolMenus::RegisterStartupCallback(
        FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FCyRevisionEditorModule::RegisterMenus));
}

void FCyRevisionEditorModule::ShutdownModule()
{
    UToolMenus::UnRegisterStartupCallback(this);
    UToolMenus::UnregisterOwner(this);
}

void FCyRevisionEditorModule::RegisterMenus()
{
    FToolMenuOwnerScoped OwnerScoped(this);
    UToolMenu* ToolsMenu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Tools"));
    FToolMenuSection& Section = ToolsMenu->FindOrAddSection(TEXT("CyRevision"));
    Section.AddMenuEntry(
        TEXT("OpenCyRevision"),
        LOCTEXT("OpenCyRevisionLabel", "Ouvrir dans CyRevision"),
        LOCTEXT("OpenCyRevisionTooltip", "Ouvre le client CyRevision externe sur le projet Unreal courant."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FCyRevisionEditorModule::OpenCyRevision)));
}

void FCyRevisionEditorModule::OpenCyRevision() const
{
    FString ExecutablePath;
    GConfig->GetString(TEXT("CyRevision"), TEXT("ExecutablePath"), ExecutablePath, GEditorPerProjectIni);
    if (ExecutablePath.IsEmpty() || !FPaths::FileExists(ExecutablePath))
    {
        FMessageDialog::Open(
            EAppMsgType::Ok,
            LOCTEXT(
                "MissingExecutable",
                "Configurez [CyRevision] ExecutablePath dans Config/DefaultEditorPerProjectUserSettings.ini."));
        return;
    }

    const FString ProjectDirectory = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
    const FString Arguments = FString::Printf(TEXT("--project=\"%s\""), *ProjectDirectory);
    FProcHandle Process = FPlatformProcess::CreateProc(
        *ExecutablePath,
        *Arguments,
        true,
        false,
        false,
        nullptr,
        0,
        nullptr,
        nullptr);
    if (!Process.IsValid())
    {
        FMessageDialog::Open(EAppMsgType::Ok, LOCTEXT("LaunchFailed", "CyRevision n'a pas pu être démarré."));
    }
}

IMPLEMENT_MODULE(FCyRevisionEditorModule, CyRevisionEditor)

#undef LOCTEXT_NAMESPACE
