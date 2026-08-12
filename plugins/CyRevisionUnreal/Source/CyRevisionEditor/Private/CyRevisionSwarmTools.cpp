#include "CyRevisionSwarmTools.h"

#include "Framework/Application/SlateApplication.h"
#include "HAL/PlatformFileManager.h"
#include "HAL/PlatformProcess.h"
#include "IPAddress.h"
#include "SocketSubsystem.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/MessageDialog.h"
#include "Misc/Paths.h"
#include "Sockets.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/SWindow.h"
#include "Widgets/Text/STextBlock.h"
#include "XmlFile.h"

#define LOCTEXT_NAMESPACE "FCyRevisionSwarmTools"

namespace
{
FXmlNode* FindXmlNode(FXmlNode* Node, const FString& Tag)
{
    if (!Node)
    {
        return nullptr;
    }
    if (Node->GetTag().Equals(Tag, ESearchCase::IgnoreCase))
    {
        return Node;
    }
    for (FXmlNode* Child : Node->GetChildrenNodes())
    {
        if (FXmlNode* Found = FindXmlNode(Child, Tag))
        {
            return Found;
        }
    }
    return nullptr;
}

FString GetConfiguredExecutable()
{
    FString Executable;
    GConfig->GetString(TEXT("CyRevision"), TEXT("ExecutablePath"), Executable, GEditorPerProjectIni);
    return Executable;
}
}

void FCyRevisionSwarmTools::Show()
{
    if (const TSharedPtr<SWindow> Existing = Window.Pin())
    {
        Existing->BringToFront(true);
        return;
    }

    FString SavedCoordinator;
    GConfig->GetString(TEXT("CyRevisionSwarm"), TEXT("CoordinatorHost"), SavedCoordinator, GEditorPerProjectIni);
    TSharedRef<SWindow> NewWindow = SNew(SWindow)
        .Title(LOCTEXT("WindowTitle", "CyRevision — Swarm over VPN"))
        .ClientSize(FVector2D(760.0f, 560.0f))
        .SupportsMaximize(true)
        .SupportsMinimize(true);
    Window = NewWindow;
    NewWindow->SetOnWindowClosed(FOnWindowClosed::CreateLambda([this](const TSharedRef<SWindow>&)
    {
        CoordinatorHostText.Reset();
        StatusText.Reset();
        Window.Reset();
    }));

    NewWindow->SetContent(
        SNew(SBorder).Padding(14.0f)
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot().AutoHeight()
            [
                SNew(STextBlock)
                .Text(LOCTEXT("Heading", "Unreal Swarm session over WireGuard"))
                .Font(FCoreStyle::GetDefaultFontStyle("Bold", 18))
            ]
            + SVerticalBox::Slot().AutoHeight().Padding(0.0f, 4.0f, 0.0f, 12.0f)
            [
                SNew(STextBlock)
                .Text(LOCTEXT(
                    "Description",
                    "Standalone tools configure and launch Swarm. CyRevision adds VPN peer management, project-only firewall/DNS changes, and complete connection diagnostics."))
                .AutoWrapText(true)
            ]
            + SVerticalBox::Slot().AutoHeight()
            [SNew(STextBlock).Text(LOCTEXT("CoordinatorLabel", "Coordinator VPN IPv4 or local DNS alias"))]
            + SVerticalBox::Slot().AutoHeight().Padding(0.0f, 4.0f, 0.0f, 10.0f)
            [
                SAssignNew(CoordinatorHostText, SEditableTextBox)
                .Text(FText::FromString(SavedCoordinator))
                .HintText(LOCTEXT("CoordinatorHint", "10.80.40.1 or cyrev-swarm-project"))
            ]
            + SVerticalBox::Slot().AutoHeight().Padding(0.0f, 0.0f, 0.0f, 10.0f)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 7.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Save", "Save Agent configuration")).OnClicked_Lambda([this]()
                {
                    SaveAgentConfiguration();
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 7.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Test", "Test TCP 8008/8009")).OnClicked_Lambda([this]()
                {
                    TestCoordinator();
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 7.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("LaunchAgent", "Launch Agent")).OnClicked_Lambda([this]()
                {
                    LaunchAgent();
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 7.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("LaunchCoordinator", "Launch Coordinator")).OnClicked_Lambda([this]()
                {
                    LaunchCoordinator();
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth()
                [SNew(SButton).Text(LOCTEXT("OpenCyRevision", "Open full setup in CyRevision")).OnClicked_Lambda([this]()
                {
                    OpenCyRevision();
                    return FReply::Handled();
                })]
            ]
            + SVerticalBox::Slot().AutoHeight().Padding(0.0f, 0.0f, 0.0f, 10.0f)
            [
                SAssignNew(StatusText, STextBlock)
                .Text(LOCTEXT("NotTested", "Swarm connection has not been tested."))
                .AutoWrapText(true)
            ]
            + SVerticalBox::Slot().FillHeight(1.0f)
            [
                SNew(SBorder).Padding(10.0f)
                [
                    SNew(SScrollBox)
                    + SScrollBox::Slot()
                    [
                        SNew(STextBlock)
                        .Text(LOCTEXT(
                            "Guide",
                            "If CyRevision cannot apply an automatic repair:\n\n"
                            "1. Start the project WireGuard tunnel and confirm that the Coordinator VPN address answers.\n"
                            "2. On every Windows computer, allow inbound TCP 8008 and 8009 only from the WireGuard project subnet. Never forward these ports on the modem/router.\n"
                            "3. Start SwarmCoordinator.exe on the host. Start SwarmAgent.exe on every worker.\n"
                            "4. In Swarm Agent > Settings, set CoordinatorRemotingHost to the Coordinator VPN IPv4 or its CyRevision local alias.\n"
                            "5. Keep AgentGroupName and AllowedRemoteAgentGroup compatible across the farm. Disable Standalone Mode.\n"
                            "6. If a port test is refused, close duplicate Swarm instances and check which process owns TCP 8008/8009.\n\n"
                            "The desktop assistant can create/remove project-owned Windows Firewall and local hosts-file entries, back up SwarmAgent.Options.xml, test WireGuard handshakes, DNS, both TCP ports, and show the exact failed step."))
                        .AutoWrapText(true)
                    ]
                ]
            ]
        ]);

    FSlateApplication::Get().AddWindow(NewWindow);
}

void FCyRevisionSwarmTools::SaveAgentConfiguration()
{
#if !PLATFORM_WINDOWS
    SetStatus(TEXT("Unreal Swarm is currently Windows-only. This machine can still participate in the WireGuard network."));
    return;
#else
    const FString Host = GetCoordinatorHost();
    if (Host.IsEmpty())
    {
        SetStatus(TEXT("Enter the Coordinator VPN IPv4 or CyRevision local alias first."));
        return;
    }
    GConfig->SetString(TEXT("CyRevisionSwarm"), TEXT("CoordinatorHost"), *Host, GEditorPerProjectIni);
    GConfig->Flush(false, GEditorPerProjectIni);

    const FString Options = GetOptionsPath();
    if (!FPaths::FileExists(Options))
    {
        SetStatus(FString::Printf(
            TEXT("Saved for this Unreal project. SwarmAgent.Options.xml was not found at %s. Start Agent once, save Settings, close it, then retry or use CyRevision to select the file."),
            *Options));
        return;
    }
    FXmlFile Xml(Options, EConstructMethod::ConstructFromFile);
    if (!Xml.IsValid())
    {
        SetStatus(TEXT("The detected SwarmAgent.Options.xml is invalid or locked. Close Swarm Agent and retry."));
        return;
    }
    FXmlNode* Coordinator = FindXmlNode(Xml.GetRootNode(), TEXT("CoordinatorRemotingHost"));
    if (!Coordinator)
    {
        SetStatus(TEXT("CoordinatorRemotingHost is missing. Open Swarm Agent Settings, save once, close Agent, and retry."));
        return;
    }
    const FString Backup = Options + TEXT(".cyrevision.bak");
    IPlatformFile& Files = FPlatformFileManager::Get().GetPlatformFile();
    Files.CopyFile(*Backup, *Options);
    Coordinator->SetContent(Host);
    if (!Xml.Save(Options))
    {
        SetStatus(TEXT("Could not save SwarmAgent.Options.xml. Restore the .cyrevision.bak copy and use the desktop diagnostic."));
        return;
    }
    SetStatus(FString::Printf(TEXT("CoordinatorRemotingHost=%s saved. Backup: %s"), *Host, *Backup));
#endif
}

void FCyRevisionSwarmTools::LaunchAgent()
{
    const FString Path = GetAgentPath();
    if (!FPaths::FileExists(Path) || !FPlatformProcess::CreateProc(*Path, TEXT(""), true, false, false, nullptr, 0, nullptr, nullptr).IsValid())
    {
        SetStatus(FString::Printf(TEXT("Swarm Agent could not be launched from %s"), *Path));
        return;
    }
    SetStatus(TEXT("Swarm Agent launched."));
}

void FCyRevisionSwarmTools::LaunchCoordinator()
{
    const FString Path = GetCoordinatorPath();
    if (!FPaths::FileExists(Path) || !FPlatformProcess::CreateProc(*Path, TEXT(""), true, false, false, nullptr, 0, nullptr, nullptr).IsValid())
    {
        SetStatus(FString::Printf(TEXT("Swarm Coordinator could not be launched from %s"), *Path));
        return;
    }
    SetStatus(TEXT("Swarm Coordinator launched. Run the TCP test after it is listening."));
}

void FCyRevisionSwarmTools::TestCoordinator()
{
#if !PLATFORM_WINDOWS
    SetStatus(TEXT("Swarm port tests are intended for Windows Swarm nodes."));
#else
    const FString Host = GetCoordinatorHost();
    ISocketSubsystem* Sockets = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
    if (!Sockets || Host.IsEmpty())
    {
        SetStatus(TEXT("Enter a Coordinator VPN IPv4 or alias first."));
        return;
    }
    FAddressInfoResult Addresses = Sockets->GetAddressInfo(*Host, nullptr, EAddressInfoFlags::Default, NAME_None);
    if (Addresses.Results.IsEmpty())
    {
        SetStatus(TEXT("Coordinator name did not resolve. Use its VPN IPv4 or apply the CyRevision local alias."));
        return;
    }
    TArray<FString> Results;
    for (const int32 Port : {8008, 8009})
    {
        TSharedPtr<FInternetAddr> Address = Addresses.Results[0].Address->Clone();
        Address->SetPort(Port);
        FSocket* Socket = Sockets->CreateSocket(NAME_Stream, TEXT("CyRevisionSwarmTest"), Address->GetProtocolType());
        bool bConnected = false;
        if (Socket)
        {
            Socket->SetNonBlocking(true);
            Socket->Connect(*Address);
            Socket->Wait(ESocketWaitConditions::WaitForWrite, FTimespan::FromSeconds(2.0));
            bConnected = Socket->GetConnectionState() == SCS_Connected;
        }
        if (Socket)
        {
            Socket->Close();
            Sockets->DestroySocket(Socket);
        }
        Results.Add(FString::Printf(TEXT("TCP %d: %s"), Port, bConnected ? TEXT("connected") : TEXT("refused/unreachable")));
    }
    SetStatus(FString::Join(Results, TEXT(" · ")) +
        TEXT(". If refused: start Coordinator/Agent, verify the WireGuard handshake and restrict Windows Firewall rules to the VPN subnet."));
#endif
}

void FCyRevisionSwarmTools::OpenCyRevision() const
{
    const FString Executable = GetConfiguredExecutable();
    if (!FPaths::FileExists(Executable))
    {
        FMessageDialog::Open(EAppMsgType::Ok, LOCTEXT("MissingClient", "Install/configure the CyRevision integration from the desktop Plugins page first."));
        return;
    }
    FString Project = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
    FPaths::NormalizeDirectoryName(Project);
    const FString Arguments = FString::Printf(TEXT("--project=\"%s\""), *Project);
    FPlatformProcess::CreateProc(*Executable, *Arguments, true, false, false, nullptr, 0, nullptr, nullptr);
}

void FCyRevisionSwarmTools::SetStatus(const FString& Text)
{
    if (StatusText.IsValid())
    {
        StatusText->SetText(FText::FromString(Text));
    }
}

FString FCyRevisionSwarmTools::GetCoordinatorHost() const
{
    return CoordinatorHostText.IsValid() ? CoordinatorHostText->GetText().ToString().TrimStartAndEnd() : FString();
}

FString FCyRevisionSwarmTools::GetAgentPath() const
{
    return FPaths::Combine(FPaths::EngineDir(), TEXT("Binaries"), TEXT("DotNET"), TEXT("SwarmAgent.exe"));
}

FString FCyRevisionSwarmTools::GetCoordinatorPath() const
{
    return FPaths::Combine(FPaths::EngineDir(), TEXT("Binaries"), TEXT("DotNET"), TEXT("SwarmCoordinator.exe"));
}

FString FCyRevisionSwarmTools::GetOptionsPath() const
{
    return FPaths::Combine(FPaths::EngineDir(), TEXT("Binaries"), TEXT("DotNET"), TEXT("SwarmAgent.Options.xml"));
}

void FCyRevisionSwarmTools::Shutdown()
{
    if (const TSharedPtr<SWindow> Existing = Window.Pin())
    {
        Existing->RequestDestroyWindow();
    }
    CoordinatorHostText.Reset();
    StatusText.Reset();
    Window.Reset();
}

#undef LOCTEXT_NAMESPACE
