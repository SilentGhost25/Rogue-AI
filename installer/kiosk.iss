; Script generated for Inno Setup 6+
; ROGUE AI — The Breach Protocol: Participant Kiosk Setup Installer (KIOSK ONLY)
; ZERO reference solutions, ZERO attacks, ZERO engine code, ZERO admin windows

#define MyAppName "Rogue AI - Defense Studio"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Almatron Defense Labs"
#define MyAppURL "https://github.com/almatron/rogue-ai"
#define MyAppExeName "LabKiosk.exe"

[Setup]
AppId={{D41D8CD9-8F00-4B00-8F9E-3E4C02A86B91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\RogueAI-Kiosk
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=RogueAI-Kiosk-Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Kiosk WPF Application (Compiled in Release with KIOSK_ONLY define)
Source: "..\kiosk\LabKiosk\bin\KioskRelease\net9.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,AdminWindow.*"
; Default Kiosk Configuration
Source: "..\kiosk\LabKiosk\kiosk.json"; DestDir: "{app}"; Flags: ignoreversion
; Target Website Baseline Template (Harbour Notes)
Source: "..\site\*"; DestDir: "{app}\site"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "node_modules\*,*.db,*.sqlite,*.log"

[Icons]
; Participant Defense Studio Shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent
