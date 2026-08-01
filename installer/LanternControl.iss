#define MyAppName "LANtern Control"
#define MyAppVersion "0.1.1"
#define MyAppPublisher "LANtern Control"
#define MyAppExeName "LANtern Control.exe"

[Setup]
AppId={{04C973A1-E90F-4E07-9C93-C3EF9C189A66}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/humane125/LANtern-Control
AppSupportURL=https://github.com/humane125/LANtern-Control/issues
DefaultDirName={localappdata}\Programs\LANtern Control
DefaultGroupName=LANtern Control
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\outputs
OutputBaseFilename=LANtern-Control-Setup-v{#MyAppVersion}
SetupIconFile=..\src\Lantern.App\Assets\RedWatcher.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: startmenu; Description: "Create a &Start Menu shortcut"; GroupDescription: "Shortcuts:"
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\release\LANtern Control.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LANtern Control"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{autodesktop}\LANtern Control"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch LANtern Control"; WorkingDir: "{app}"; Verb: "runas"; Flags: postinstall shellexec skipifsilent
