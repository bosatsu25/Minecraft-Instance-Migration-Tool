#ifndef AppVersion
  #error AppVersion must be defined by the release build.
#endif
#ifndef PublishDir
  #error PublishDir must be defined by the release build.
#endif
#ifndef PackageDir
  #error PackageDir must be defined by the release build.
#endif
#ifndef AppIconPath
  #error AppIconPath must be defined by the release build.
#endif

#define ProductName "Minecraft Instance Migration Tool"
#define ExecutableName "MinecraftInstanceMigrationTool.exe"

[Setup]
AppId={{B8C724E4-E02C-4FDD-A3D3-4600AF40B402}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=bosatsuKing
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#PackageDir}
OutputBaseFilename=MinecraftInstanceMigrationTool-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#ExecutableName}
UninstallDisplayName={#ProductName}
SetupIconFile={#AppIconPath}
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#FileVersion}
VersionInfoProductName={#ProductName}
VersionInfoDescription={#ProductName} installer
VersionInfoCompany=bosatsuKing

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#ProductName}"; Filename: "{app}\{#ExecutableName}"; IconFilename: "{app}\{#ExecutableName}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\{#ExecutableName}"; IconFilename: "{app}\{#ExecutableName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExecutableName}"; Description: "Launch {#ProductName}"; Flags: nowait postinstall skipifsilent
