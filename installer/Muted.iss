; Inno Setup script for Muted.
; Built from the self-contained publish output at artifacts\Muted-win-x64,
; produced by scripts\publish.ps1 -SelfContained. Run scripts\build-installer.ps1
; to publish and compile in one step.

#define MyAppName "Muted"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "Muted"
#define MyAppExeName "Muted.exe"
#define MyPublishDir "..\artifacts\Muted-win-x64"

[Setup]
AppId={{B3B7E6C1-6E6A-4C6B-9C1E-7B6E7E9A0F3D}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputBaseFilename=Muted-Setup-{#MyAppVersion}
OutputDir=..\artifacts
SetupIconFile=..\icon.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Start {#MyAppName} automatically when Windows starts"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Flags: nowait; Check: RestartMuted

[Code]
function RestartMuted: Boolean;
begin
  Result := ExpandConstant('{param:RESTARTMUTED|0}') = '1';
end;
