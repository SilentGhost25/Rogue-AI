; Script generated for Inno Setup 6+
; ROGUE AI — The Breach Protocol: Organizer Command Center (HOST ONLY)
; Includes Control Plane, Breach Engine, Attack Packs, Reference Suites, and Admin Dashboards

#define MyAppName "Rogue AI - Organizer Command Center"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Almatron Defense Labs"
#define MyAppURL "https://github.com/almatron/rogue-ai"

[Setup]
AppId={{5E2338A1-4B2E-4D21-8E8B-B65B5CF32E99}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\RogueAI-Host
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=RogueAI-Host-Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Control Plane Server
Source: "..\server\ControlPlane\bin\Debug\net9.0\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,secrets\*,data\*,*.db,*.sqlite,*.log,*.pem,*.key,*desk_tokens_slips*,*Production*,*Local*"
; Breach Evaluation Engine
Source: "..\engine\*"; DestDir: "{app}\engine"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc,*.pem,*.key,*.db,*desk_tokens_slips*"
; Attack Packs & Reference Suites
Source: "..\attacks\*"; DestDir: "{app}\attacks"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc,*.pem,*.key,*.db"
; Base Website for Ephemeral Tests
Source: "..\site\*"; DestDir: "{app}\site"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.sqlite,*.log,node_modules\*"
; Runner script
Source: "..\run.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\run.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\server\ControlPlane.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\server\ControlPlane.exe"; Tasks: desktopicon
