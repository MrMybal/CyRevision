#pragma once

#include "CoreMinimal.h"

class SEditableTextBox;
class STextBlock;
class SWindow;

class FCyRevisionSwarmTools
{
public:
    void Show();
    void Shutdown();

private:
    void SaveAgentConfiguration();
    void LaunchAgent();
    void LaunchCoordinator();
    void TestCoordinator();
    void OpenCyRevision() const;
    void SetStatus(const FString& Text);
    FString GetCoordinatorHost() const;
    FString GetAgentPath() const;
    FString GetCoordinatorPath() const;
    FString GetOptionsPath() const;

    TWeakPtr<SWindow> Window;
    TSharedPtr<SEditableTextBox> CoordinatorHostText;
    TSharedPtr<STextBlock> StatusText;
};
