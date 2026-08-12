#include "CyRevisionRevisionTools.h"

#include "Dom/JsonObject.h"
#include "Framework/Application/SlateApplication.h"
#include "HAL/PlatformProcess.h"
#include "HttpModule.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/FileHelper.h"
#include "Misc/MessageDialog.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/SWindow.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "FCyRevisionRevisionTools"

namespace
{
struct FCyRevisionBridgeSettings
{
    FString Url;
    FString Token;
    FString ExecutablePath;
};

bool LoadBridgeSettings(FCyRevisionBridgeSettings& OutSettings)
{
    const FString SettingsPath = FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("CyRevision"), TEXT("bridge.json"));
    FString JsonText;
    if (FFileHelper::LoadFileToString(JsonText, *SettingsPath))
    {
        TSharedPtr<FJsonObject> Json;
        const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonText);
        if (FJsonSerializer::Deserialize(Reader, Json) && Json.IsValid())
        {
            Json->TryGetStringField(TEXT("url"), OutSettings.Url);
            Json->TryGetStringField(TEXT("token"), OutSettings.Token);
            Json->TryGetStringField(TEXT("executablePath"), OutSettings.ExecutablePath);
        }
    }

    if (OutSettings.ExecutablePath.IsEmpty())
    {
        GConfig->GetString(TEXT("CyRevision"), TEXT("ExecutablePath"), OutSettings.ExecutablePath, GEditorPerProjectIni);
    }
    if (OutSettings.Url.IsEmpty())
    {
        GConfig->GetString(TEXT("CyRevision"), TEXT("BridgeUrl"), OutSettings.Url, GEditorPerProjectIni);
    }
    if (OutSettings.Token.IsEmpty())
    {
        GConfig->GetString(TEXT("CyRevision"), TEXT("BridgeToken"), OutSettings.Token, GEditorPerProjectIni);
    }
    return !OutSettings.Url.IsEmpty() && !OutSettings.Token.IsEmpty();
}

FString EscapeGitArgument(FString Value)
{
    Value.ReplaceInline(TEXT("\\"), TEXT("\\\\"));
    Value.ReplaceInline(TEXT("\""), TEXT("\\\""));
    Value.ReplaceInline(TEXT("\r"), TEXT(" "));
    Value.ReplaceInline(TEXT("\n"), TEXT(" "));
    return FString::Printf(TEXT("\"%s\""), *Value);
}

FString JoinUrl(FString Base, const FString& Relative)
{
    if (!Base.EndsWith(TEXT("/")))
    {
        Base += TEXT("/");
    }
    return Base + Relative;
}
}

void FCyRevisionRevisionTools::ShowDashboard()
{
    if (const TSharedPtr<SWindow> Existing = DashboardWindow.Pin())
    {
        Existing->BringToFront(true);
        RefreshDashboard();
        TestConnection(false);
        return;
    }

    TSharedRef<SWindow> Window = SNew(SWindow)
        .Title(LOCTEXT("DashboardTitle", "CyRevision — Revision Dashboard"))
        .ClientSize(FVector2D(1040.0f, 680.0f))
        .SupportsMaximize(true)
        .SupportsMinimize(true);
    DashboardWindow = Window;
    Window->SetOnWindowClosed(FOnWindowClosed::CreateLambda([this](const TSharedRef<SWindow>&)
    {
        RepositoryStatusText.Reset();
        RevisionHistoryText.Reset();
        ConnectionStatusText.Reset();
        CommitMessageText.Reset();
        DashboardWindow.Reset();
    }));

    Window->SetContent(
        SNew(SBorder)
        .Padding(14.0f)
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot().AutoHeight()
            [
                SNew(STextBlock)
                .Text(LOCTEXT("DashboardHeading", "Project revisions"))
                .Font(FCoreStyle::GetDefaultFontStyle("Bold", 18))
            ]
            + SVerticalBox::Slot().AutoHeight().Padding(0.0f, 4.0f, 0.0f, 10.0f)
            [
                SAssignNew(ConnectionStatusText, STextBlock)
                .Text(LOCTEXT("ConnectionPending", "CyRevision connection not checked."))
                .AutoWrapText(true)
            ]
            + SVerticalBox::Slot().AutoHeight().Padding(0.0f, 0.0f, 0.0f, 10.0f)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 6.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Refresh", "Refresh")).OnClicked_Lambda([this]()
                {
                    RefreshDashboard();
                    TestConnection(false);
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 6.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("StageAll", "Stage all")).OnClicked_Lambda([this]()
                {
                    RunGitAction(TEXT("add --all"), TEXT("stage-all"));
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().FillWidth(1.0f).Padding(0.0f, 0.0f, 6.0f, 0.0f)
                [SAssignNew(CommitMessageText, SEditableTextBox).HintText(LOCTEXT("CommitHint", "Commit message"))]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 12.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Commit", "Commit")).OnClicked_Lambda([this]()
                {
                    Commit();
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 6.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Fetch", "Fetch")).OnClicked_Lambda([this]()
                {
                    RunGitAction(TEXT("fetch"), TEXT("fetch"));
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 6.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Pull", "Pull")).OnClicked_Lambda([this]()
                {
                    RunGitAction(TEXT("pull"), TEXT("pull"));
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth().Padding(0.0f, 0.0f, 6.0f, 0.0f)
                [SNew(SButton).Text(LOCTEXT("Push", "Push")).OnClicked_Lambda([this]()
                {
                    RunGitAction(TEXT("push"), TEXT("push"));
                    return FReply::Handled();
                })]
                + SHorizontalBox::Slot().AutoWidth()
                [SNew(SButton).Text(LOCTEXT("OpenClient", "Open CyRevision")).OnClicked_Lambda([this]()
                {
                    OpenCyRevision();
                    return FReply::Handled();
                })]
            ]
            + SVerticalBox::Slot().FillHeight(0.42f).Padding(0.0f, 0.0f, 0.0f, 8.0f)
            [
                SNew(SBorder).Padding(10.0f)
                [SNew(SScrollBox) + SScrollBox::Slot()
                    [SAssignNew(RepositoryStatusText, STextBlock).AutoWrapText(false)]]
            ]
            + SVerticalBox::Slot().FillHeight(0.58f)
            [
                SNew(SBorder).Padding(10.0f)
                [SNew(SScrollBox) + SScrollBox::Slot()
                    [SAssignNew(RevisionHistoryText, STextBlock).AutoWrapText(false)]]
            ]
        ]);

    FSlateApplication::Get().AddWindow(Window);
    RefreshDashboard();
    TestConnection(false);
}

void FCyRevisionRevisionTools::OpenCyRevision() const
{
    FCyRevisionBridgeSettings Settings;
    LoadBridgeSettings(Settings);
    if (Settings.ExecutablePath.IsEmpty() || !FPaths::FileExists(Settings.ExecutablePath))
    {
        FMessageDialog::Open(
            EAppMsgType::Ok,
            LOCTEXT("MissingExecutable", "Install or configure CyRevisionUnreal from the CyRevision Plugins page first."));
        return;
    }

    const FString Arguments = FString::Printf(TEXT("--project=\"%s\""), *GetProjectDirectory());
    if (!FPlatformProcess::CreateProc(
            *Settings.ExecutablePath, *Arguments, true, false, false, nullptr, 0, nullptr, nullptr).IsValid())
    {
        FMessageDialog::Open(EAppMsgType::Ok, LOCTEXT("LaunchFailed", "CyRevision could not be started."));
    }
}

void FCyRevisionRevisionTools::TestConnection(bool bShowDialog)
{
    FCyRevisionBridgeSettings Settings;
    if (!LoadBridgeSettings(Settings))
    {
        const FText Message = LOCTEXT("NotConfigured", "CyRevision connection is not configured. The local revision tools remain available.");
        if (ConnectionStatusText.IsValid())
        {
            ConnectionStatusText->SetText(Message);
        }
        if (bShowDialog)
        {
            FMessageDialog::Open(EAppMsgType::Ok, Message);
        }
        return;
    }

    const TWeakPtr<STextBlock> WeakStatus = ConnectionStatusText;
    TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
    Request->SetURL(JoinUrl(Settings.Url, TEXT("status")));
    Request->SetVerb(TEXT("GET"));
    Request->SetHeader(TEXT("Authorization"), TEXT("Bearer ") + Settings.Token);
    Request->OnProcessRequestComplete().BindLambda(
        [WeakStatus, bShowDialog](FHttpRequestPtr, FHttpResponsePtr Response, bool bSucceeded)
        {
            const bool bConnected = bSucceeded && Response.IsValid() && Response->GetResponseCode() == 200;
            const FText Message = bConnected
                ? LOCTEXT("Connected", "Connected to CyRevision. Extended Git/LFS/Sync/backup tools are available in the desktop client.")
                : LOCTEXT("Disconnected", "CyRevision is not reachable. Autonomous revision tools remain available.");
            if (const TSharedPtr<STextBlock> Status = WeakStatus.Pin())
            {
                Status->SetText(Message);
            }
            if (bShowDialog)
            {
                FMessageDialog::Open(EAppMsgType::Ok, Message);
            }
        });
    Request->ProcessRequest();
}

void FCyRevisionRevisionTools::NotifyProjectChanged(const FString& Action) const
{
    FCyRevisionBridgeSettings Settings;
    if (!LoadBridgeSettings(Settings))
    {
        return;
    }

    TSharedRef<FJsonObject> Json = MakeShared<FJsonObject>();
    Json->SetStringField(TEXT("action"), Action);
    Json->SetStringField(TEXT("projectRoot"), GetProjectDirectory());
    FString Body;
    FJsonSerializer::Serialize(Json, TJsonWriterFactory<>::Create(&Body));

    TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
    Request->SetURL(JoinUrl(Settings.Url, TEXT("notify")));
    Request->SetVerb(TEXT("POST"));
    Request->SetHeader(TEXT("Authorization"), TEXT("Bearer ") + Settings.Token);
    Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
    Request->SetContentAsString(Body);
    Request->ProcessRequest();
}

void FCyRevisionRevisionTools::Shutdown()
{
    if (const TSharedPtr<SWindow> Window = DashboardWindow.Pin())
    {
        Window->RequestDestroyWindow();
    }
    RepositoryStatusText.Reset();
    RevisionHistoryText.Reset();
    ConnectionStatusText.Reset();
    CommitMessageText.Reset();
    DashboardWindow.Reset();
}

bool FCyRevisionRevisionTools::RunGit(const FString& Command, FString& Output, FString& Error) const
{
    FString GitExecutable = TEXT("git");
    GConfig->GetString(TEXT("CyRevision"), TEXT("GitExecutable"), GitExecutable, GEditorPerProjectIni);
    if (GitExecutable.IsEmpty())
    {
        GitExecutable = TEXT("git");
    }

    const FString Arguments = FString::Printf(TEXT("-C %s %s"), *EscapeGitArgument(GetProjectDirectory()), *Command);
    int32 ReturnCode = INDEX_NONE;
    const bool bStarted = FPlatformProcess::ExecProcess(*GitExecutable, *Arguments, &ReturnCode, &Output, &Error);
    return bStarted && ReturnCode == 0;
}

void FCyRevisionRevisionTools::RunGitAction(const FString& Command, const FString& Action)
{
    FString Output;
    FString Error;
    if (!RunGit(Command, Output, Error))
    {
        FMessageDialog::Open(EAppMsgType::Ok, FText::FromString(Error.IsEmpty() ? TEXT("Git command failed.") : Error));
        return;
    }
    NotifyProjectChanged(Action);
    RefreshDashboard();
}

void FCyRevisionRevisionTools::Commit()
{
    const FString Message = CommitMessageText.IsValid() ? CommitMessageText->GetText().ToString().TrimStartAndEnd() : FString();
    if (Message.IsEmpty())
    {
        FMessageDialog::Open(EAppMsgType::Ok, LOCTEXT("CommitMessageRequired", "Enter a commit message first."));
        return;
    }
    RunGitAction(TEXT("commit -m ") + EscapeGitArgument(Message), TEXT("commit"));
    if (CommitMessageText.IsValid())
    {
        CommitMessageText->SetText(FText::GetEmpty());
    }
}

void FCyRevisionRevisionTools::RefreshDashboard()
{
    FString Status;
    FString StatusError;
    if (!RunGit(TEXT("status --short --branch"), Status, StatusError))
    {
        Status = StatusError.IsEmpty() ? TEXT("This Unreal project is not a Git repository or Git is unavailable.") : StatusError;
    }
    if (RepositoryStatusText.IsValid())
    {
        RepositoryStatusText->SetText(FText::FromString(Status.IsEmpty() ? TEXT("Working tree clean.") : Status));
    }

    FString History;
    FString HistoryError;
    if (!RunGit(TEXT("log -30 --date=short --pretty=format:\"%h  %ad  %an  %s\""), History, HistoryError))
    {
        History = HistoryError;
    }
    if (RevisionHistoryText.IsValid())
    {
        RevisionHistoryText->SetText(FText::FromString(History.IsEmpty() ? TEXT("No revisions found.") : History));
    }
}

FString FCyRevisionRevisionTools::GetProjectDirectory() const
{
    FString Directory = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
    FPaths::NormalizeDirectoryName(Directory);
    return Directory;
}

#undef LOCTEXT_NAMESPACE
