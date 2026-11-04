; Script generated for Inno Setup 6+
; ROGUE AI — The Breach Protocol: Unified Setup Installer

#define MyAppName "Rogue AI - The Breach Protocol"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Almatron Defense Labs"
#define MyAppURL "https://github.com/almatron/rogue-ai"
#define MyAppExeName "LabKiosk.exe"

[Setup]
AppId={{9F76E9B4-4B2E-4D21-8E8B-B65B5CF32E11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\RogueAI
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=RogueAI-Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "organizericon"; Description: "Create Organizer Command Center shortcut"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Kiosk WPF Application & Dependencies
Source: "..\kiosk\LabKiosk\bin\Debug\net9.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Default Kiosk Settings
Source: "..\kiosk\LabKiosk\kiosk.json"; DestDir: "{app}"; Flags: ignoreversion
; Target Website (Harbour Notes including bundled node_modules)
Source: "..\site\*"; DestDir: "{app}\site"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.sqlite,*.log"
; Control Plane Server
Source: "..\server\ControlPlane\bin\Debug\net9.0\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs
; Engine and Attack Packs
Source: "..\engine\*"; DestDir: "{app}\engine"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc,*.db"
Source: "..\attacks\*"; DestDir: "{app}\attacks"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc"

[Icons]
; Participant Mode
Name: "{group}\Rogue AI - Participant Defense Studio"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Rogue AI - Participant Defense Studio"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; Organizer Command Mode
Name: "{group}\Rogue AI - Organizer Command Center"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"
Name: "{autodesktop}\Rogue AI - Organizer Command Center"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"; Tasks: organizericon

[Run]
Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent

