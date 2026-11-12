; Script generated for Inno Setup 6+
; ROGUE AI — The Breach Protocol: Unified Setup Installer (Participant + Host Mode)

#define MyAppName "Rogue AI - The Breach Protocol"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Almatron Defense Labs"
#define MyAppURL "https://github.com/SilentGhost25/Rogue-AI"
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
PrivilegesRequiredOverridesAllowed=commandline

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "participant"; Description: "Participant Kiosk (Defend site & connect to Organizer Host)"
Name: "host"; Description: "Host Mode (Organizer Control Plane + Evaluation Engine)"
Name: "full"; Description: "Full Suite (Host Mode + Participant Kiosk)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "participant"; Description: "Participant Defense Studio (LabKiosk + AEGIS Site)"; Types: participant full custom
Name: "host"; Description: "Organizer Host Mode (Control Plane + Attack Engine + Command Center)"; Types: host full custom

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Kiosk WPF Application & Self-Contained .NET 9 Runtime (used by both Participant View and --host Command Center)
Source: "..\kiosk\LabKiosk\bin\SelfContained\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.WebView2\*"
; Bundled Standalone Portable Node.js Runtime & NPM Shim (Zero External Dependency)
Source: "..\vendor\nodejs\*"; DestDir: "{app}\nodejs"; Flags: ignoreversion
; Bundled OpenCode CLI & Antigravity CLI binaries (offline zero-install) + AI CLI Provisioning Script fallback
Source: "..\vendor\opencode\*"; DestDir: "{app}\opencode"; Components: participant; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\vendor\agy\*"; DestDir: "{app}\agy"; Components: participant; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\scripts\provision-ai-clis.ps1"; DestDir: "{app}\scripts"; Components: participant; Flags: ignoreversion
Source: "..\scripts\uninstall-complete.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
; Default Kiosk Settings
Source: "..\kiosk\LabKiosk\kiosk.json"; DestDir: "{app}"; Flags: ignoreversion
; Target Website (AEGIS including bundled node_modules; exclude any runtime DBs or backups)
Source: "..\site\*"; DestDir: "{app}\site"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.db-wal,*.db-shm,*.sqlite,*.log,.breach-backup\*"
; Control Plane Server (Host Mode - Self-Contained .NET 9)
Source: "..\server\ControlPlane\bin\SelfContained\*"; DestDir: "{app}\server"; Components: host; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "wwwroot\*.exe,Data\*,data\*,secrets\*"
; Engine and Attack Packs (Host Mode)
Source: "..\engine\*"; DestDir: "{app}\engine"; Components: host; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc,*.db"
Source: "..\attacks\*"; DestDir: "{app}\attacks"; Components: host; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc"
; Uninstaller Helper Script
Source: "uninstall.bat"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Guarantee a clean, untouched base app workspace, templates, and host database on install/reinstall
Type: filesandordirs; Name: "C:\BreachWorkspace"
Type: filesandordirs; Name: "{app}\site"
Type: filesandordirs; Name: "{app}\site-template"
Type: filesandordirs; Name: "{app}\attacks"
Type: filesandordirs; Name: "{app}\workspace"
Type: filesandordirs; Name: "{app}\server\Data"
Type: filesandordirs; Name: "{app}\server\data"
Type: files; Name: "{localappdata}\BreachKiosk\site.db*"
Type: filesandordirs; Name: "{localappdata}\BreachKiosk\host\workspaces"
Type: filesandordirs; Name: "{localappdata}\BreachKiosk\host\backups"
Type: files; Name: "{localappdata}\BreachKiosk\host\breach.db*"
Type: files; Name: "{localappdata}\BreachKiosk\host\controlplane.db*"
Type: files; Name: "{autodesktop}\*Breach*.lnk"
Type: files; Name: "{autodesktop}\*Rogue*.lnk"
Type: files; Name: "{commondesktop}\*Breach*.lnk"
Type: files; Name: "{commondesktop}\*Rogue*.lnk"
Type: files; Name: "{userdesktop}\*Breach*.lnk"
Type: files; Name: "{userdesktop}\*Rogue*.lnk"
Type: filesandordirs; Name: "{userprograms}\*Breach*"
Type: filesandordirs; Name: "{commonprograms}\*Breach*"
Type: filesandordirs; Name: "{userprograms}\ROGUE AI*"
Type: filesandordirs; Name: "{commonprograms}\ROGUE AI*"

[UninstallDelete]
; Clean up runtime data, workspaces, DBs, and provisioned AI CLIs on uninstall
Type: filesandordirs; Name: "C:\BreachWorkspace"
Type: filesandordirs; Name: "C:\BreachSandboxes"
Type: filesandordirs; Name: "C:\BreachSecrets"
Type: filesandordirs; Name: "{localappdata}\BreachKiosk"
Type: filesandordirs; Name: "{localappdata}\agy"
Type: files; Name: "{app}\kiosk.json"
Type: filesandordirs; Name: "{app}\agy"
Type: filesandordirs; Name: "{app}\opencode"
Type: filesandordirs; Name: "{app}\workspace"
Type: files; Name: "{autodesktop}\*Breach*.lnk"
Type: files; Name: "{autodesktop}\*Rogue*.lnk"
Type: files; Name: "{commondesktop}\*Breach*.lnk"
Type: files; Name: "{commondesktop}\*Rogue*.lnk"
Type: files; Name: "{userdesktop}\*Breach*.lnk"
Type: files; Name: "{userdesktop}\*Rogue*.lnk"
Type: filesandordirs; Name: "{userprograms}\*Breach*"
Type: filesandordirs; Name: "{commonprograms}\*Breach*"
Type: dirifempty; Name: "{app}"

[Icons]
; Participant Mode
Name: "{group}\Rogue AI - Participant Defense Studio"; Filename: "{app}\{#MyAppExeName}"; Components: participant
Name: "{autodesktop}\Rogue AI - Participant Defense Studio"; Filename: "{app}\{#MyAppExeName}"; Components: participant; Tasks: desktopicon
; Organizer Host Mode
Name: "{group}\Rogue AI - Organizer Host Mode"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"; Components: host
Name: "{autodesktop}\Rogue AI - Organizer Host Mode"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"; Components: host; Tasks: desktopicon
; Uninstaller
Name: "{group}\Uninstall Rogue AI"; Filename: "{uninstallexe}"
Name: "{group}\Cleanup and Uninstall Utility"; Filename: "{app}\uninstall.bat"
Name: "{autodesktop}\Uninstall Rogue AI"; Filename: "{uninstallexe}"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\scripts\provision-ai-clis.ps1"""; Components: participant; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"; Description: "Launch Organizer Host Mode"; Components: host; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Participant Defense Studio"; Components: participant; Flags: nowait postinstall skipifsilent

[Code]
var
  ServerPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ServerPage := CreateInputQueryPage(wpSelectComponents,
    'Organizer Server Configuration',
    'Specify the Host Server connection for this participant laptop.',
    'Keep AUTO for automatic dynamic discovery, or enter the Host IP / URL:');
  ServerPage.Add('Organizer Host IP / URL (or AUTO for dynamic discovery):', False);
  ServerPage.Add('Participant Desk Identifier (e.g. DESK-01, DESK-02):', False);
  ServerPage.Values[0] := 'AUTO';
  ServerPage.Values[1] := 'DESK-01';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = ServerPage.ID) and (not WizardIsComponentSelected('participant')) then
    Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  JsonContent: string;
  ServerUrlVal, DeskIdVal: string;
begin
  if CurStep = ssPostInstall then
  begin
    ServerUrlVal := Trim(ServerPage.Values[0]);
    if (CompareText(ServerUrlVal, 'AUTO') = 0) or (ServerUrlVal = '') then
      ServerUrlVal := 'http://127.0.0.1:5080'
    else
    begin
      if (Pos('http://', ServerUrlVal) = 0) and (Pos('https://', ServerUrlVal) = 0) then
        ServerUrlVal := 'http://' + ServerUrlVal;
      if (Pos(':', Copy(ServerUrlVal, 8, Length(ServerUrlVal))) = 0) then
        ServerUrlVal := ServerUrlVal + ':5080';
    end;

    DeskIdVal := Trim(ServerPage.Values[1]);
    if DeskIdVal = '' then DeskIdVal := 'DESK-01';

    JsonContent := '{' + #13#10 +
      '  "ServerUrl": "' + ServerUrlVal + '",' + #13#10 +
      '  "SiteUrl": "http://127.0.0.1:3000",' + #13#10 +
      '  "WorkspacePath": "C:\\BreachWorkspace",' + #13#10 +
      '  "DeskId": "' + DeskIdVal + '",' + #13#10 +
      '  "DeskToken": ""' + #13#10 +
      '}';
    SaveStringToFile(ExpandConstant('{app}\kiosk.json'), JsonContent, False);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Terminate running processes so [InstallDelete] can cleanly wipe any previous workspace/DB state
  Exec(ExpandConstant('{cmd}'), '/c taskkill /F /IM LabKiosk.exe /IM ControlPlane.exe /IM opencode.exe /IM agy.exe /IM node.exe /T >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  PsScript: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Terminate running processes cleanly so binaries and workspace handles are unlocked before removal
    Exec(ExpandConstant('{cmd}'), '/c taskkill /F /IM LabKiosk.exe /IM ControlPlane.exe /IM opencode.exe /IM agy.exe /IM node.exe /IM python.exe /IM uvicorn.exe /T >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    PsScript := ExpandConstant('{app}\scripts\uninstall-complete.ps1');
    if FileExists(PsScript) then
    begin
      Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + PsScript + '" -Silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
