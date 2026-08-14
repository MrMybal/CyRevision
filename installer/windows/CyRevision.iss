#ifndef AppVersion
  #define AppVersion "0.1.12"
#endif

#ifndef SourceDir
  #error SourceDir must point to the self-contained CyRevision publish directory.
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif

#define RepositoryRoot "..\.."
#define AppExecutable "CyRevision.Desktop.exe"

[Setup]
AppId={{7E79EA0B-3AB0-45D7-A1A2-A7585A0604D5}
AppName=CyRevision
AppVersion={#AppVersion}
AppVerName=CyRevision {#AppVersion} Alpha
AppPublisher=CyRevision
AppPublisherURL=https://github.com/MrMybal/CyRevision
AppSupportURL=https://github.com/MrMybal/CyRevision/issues
AppUpdatesURL=https://github.com/MrMybal/CyRevision/releases
DefaultDirName={autopf}\CyRevision
DefaultGroupName=CyRevision
AllowNoIcons=yes
LicenseFile={#RepositoryRoot}\LICENSE
SetupIconFile={#RepositoryRoot}\src\CyRevision.Desktop\Assets\Branding\cyrevision.ico
UninstallDisplayIcon={app}\{#AppExecutable}
OutputDir={#OutputDir}
OutputBaseFilename=CyRevision-Setup-{#AppVersion}-win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
DisableProgramGroupPage=auto
DisableReadyMemo=no
DisableWelcomePage=no
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CyRevision"; Filename: "{app}\{#AppExecutable}"
Name: "{autodesktop}\CyRevision"; Filename: "{app}\{#AppExecutable}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExecutable}"; Description: "{cm:LaunchProgram,CyRevision}"; Flags: nowait postinstall skipifsilent
