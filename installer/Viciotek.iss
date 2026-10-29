#ifndef AppVersion
  #error AppVersion is required; use tools/Build-Installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputRoot
  #error OutputRoot is required
#endif
#ifdef SmokeRoot
  #define ProductId "{FE3F00D7-5FA5-43F9-9C75-399A946BFE71}"
  #define DesktopRoot SmokeRoot + "\desktop"
  #define ProgramsRoot SmokeRoot + "\start-menu"
  #define OutputSuffix "-smoke"
#else
  #define ProductId "{B1C8653B-30A1-4A5E-B196-CA59DDA44A56}"
  #define DesktopRoot "{autodesktop}"
  #define ProgramsRoot "{userprograms}\VICIOTEK"
  #define OutputSuffix ""
#endif

[Setup]
AppId={{#ProductId}
AppName=VICIOTEK
AppVersion={#AppVersion}
AppPublisher=Lucas Siano
AppPublisherURL=https://github.com/luction97/Viciotek
AppSupportURL=https://github.com/luction97/Viciotek/issues
VersionInfoCopyright=Copyright (C) 2026 Lucas Siano. Todos los derechos reservados.
DefaultDirName={localappdata}\Programs\VICIOTEK
DefaultGroupName=VICIOTEK
DisableProgramGroupPage=yes
AllowNoIcons=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
MinVersion=10.0.22000
AllowRootDirectory=no
AllowUNCPath=no
OutputDir={#OutputRoot}
OutputBaseFilename=Viciotek-{#AppVersion}-setup-win-x64{#OutputSuffix}
SetupIconFile=..\src\Viciotek\Assets\viciotek.ico
UninstallDisplayIcon={app}\Viciotek.exe
WizardStyle=modern dynamic
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
AppMutex=Local\Viciotek.Application.Running
SetupMutex=Local\Viciotek.Setup
InfoBeforeFile=..\COPYRIGHT.md
#ifdef SmokeRoot
CreateUninstallRegKey=no
UsePreviousAppDir=no
#endif

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Unconditional: the desktop shortcut is created even in a silent installation.
Name: "{#DesktopRoot}\VICIOTEK"; Filename: "{app}\Viciotek.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Viciotek.exe"; Comment: "Biblioteca personal de videojuegos."
Name: "{#ProgramsRoot}\VICIOTEK"; Filename: "{app}\Viciotek.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Viciotek.exe"

[Run]
Filename: "{app}\Viciotek.exe"; Description: "Abrir VICIOTEK"; Flags: nowait postinstall skipifsilent unchecked

; No InstallDelete/UninstallDelete rules: personal data and unknown files are never removed.
