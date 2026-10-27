#define MyAppName "QuickNotch"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "QuickNotch"
#define MyAppExeName "QuickNotch.exe"

[Setup]
AppId={{8F9E3A2C-5B1D-4E7A-9C3B-2D4E6F8A1B0C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer
OutputBaseFilename=QuickNotch_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:AutoStartProgram,{#MyAppName}}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish_v2\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillQuickNotch"

