; Script generated for Inno Setup 6+
; ROGUE AI — The Breach Protocol: Unified Setup Installer (Single Installer for Everything)

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
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full Suite (Recommended - Organizer Host + Participant Kiosk - Single-Machine Arena)"
Name: "participant"; Description: "Participant Defense Studio (Participant Laptops - Connect to Arena Host)"
Name: "host"; Description: "Organizer Server Only (Control Plane + Evaluation Engine)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "participant"; Description: "Participant Defense Studio (LabKiosk + AEGIS Site)"; Types: full participant custom
Name: "host"; Description: "Organizer Host Mode (Control Plane + Attack Engine + Command Center)"; Types: full host custom

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
Name: "{app}\attacks"; Permissions: admins-full; Components: host
Name: "{app}\server"; Permissions: users-readexec; Components: host
Name: "{app}\engine"; Permissions: users-readexec; Components: host
Name: "{app}\site"; Permissions: users-readexec
Name: "{localappdata}\BreachKiosk"; Permissions: users-full
Name: "{localappdata}\BreachKiosk\host"; Permissions: users-full
Name: "{localappdata}\BreachKiosk\host\workspaces"; Permissions: users-full
Name: "{localappdata}\BreachKiosk\host\backups"; Permissions: users-full
Name: "{drive:{app}}\BreachWorkspace"; Permissions: users-full
Name: "C:\BreachWorkspace"; Permissions: users-full

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
; Engine and Attack Packs (Host Mode - all 11 attacks, references and test suites strictly purged for security)
Source: "..\engine\*"; DestDir: "{app}\engine"; Components: host; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc,*.db,tests\*,*\tests\*,*\tests\*.*"
Source: "..\attacks\*"; DestDir: "{app}\attacks"; Components: host; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc,*\reference\*,*\reference\*.*,*\reference,reference\*,*\hints.yaml,hints.yaml,*\tests\test_*.py,*.git*"
; Uninstaller Helper Script
Source: "uninstall.bat"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Guarantee a clean, untouched base app workspace, templates, and host database on install/reinstall
Type: filesandordirs; Name: "{drive:{app}}\BreachWorkspace"
Type: filesandordirs; Name: "C:\BreachWorkspace"
Type: filesandordirs; Name: "D:\BreachWorkspace"
Type: filesandordirs; Name: "{app}\site"
Type: filesandordirs; Name: "{app}\site-template"
Type: filesandordirs; Name: "{app}\attacks"
Type: filesandordirs; Name: "{app}\workspace"
Type: filesandordirs; Name: "{app}\server\Data"
Type: filesandordirs; Name: "{app}\server\data"
Type: files; Name: "{localappdata}\BreachKiosk\site.db*"
Type: files; Name: "{localappdata}\BreachKiosk\kiosk.json"
Type: files; Name: "{localappdata}\BreachKiosk\registration.json"
Type: files; Name: "{localappdata}\BreachKiosk\desk.json"
Type: files; Name: "{localappdata}\BreachKiosk\active_session.json"
Type: filesandordirs; Name: "{localappdata}\BreachKiosk\webview"
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
Type: filesandordirs; Name: "{drive:{app}}\BreachWorkspace"
Type: filesandordirs; Name: "{drive:{app}}\BreachSandboxes"
Type: filesandordirs; Name: "{drive:{app}}\BreachSecrets"
Type: filesandordirs; Name: "C:\BreachWorkspace"
Type: filesandordirs; Name: "C:\BreachSandboxes"
Type: filesandordirs; Name: "C:\BreachSecrets"
Type: filesandordirs; Name: "D:\BreachWorkspace"
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
Type: filesandordirs; Name: "{userprograms}\ROGUE AI*"
Type: filesandordirs; Name: "{commonprograms}\ROGUE AI*"
Type: dirifempty; Name: "{app}"

[Icons]
; Participant Mode
Name: "{group}\Rogue AI - Participant"; Filename: "{app}\{#MyAppExeName}"; Components: participant
Name: "{autodesktop}\Rogue AI - Participant"; Filename: "{app}\{#MyAppExeName}"; Components: participant; Tasks: desktopicon
; Organizer Host Mode
Name: "{group}\Rogue AI - Organizer Host"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"; Components: host
Name: "{autodesktop}\Rogue AI - Organizer Host"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--host"; Components: host; Tasks: desktopicon
; Uninstaller
Name: "{group}\Uninstall Rogue AI"; Filename: "{uninstallexe}"
Name: "{group}\Cleanup and Uninstall Utility"; Filename: "{app}\uninstall.bat"

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
  ServerPage.Add('Participant Desk Identifier (enter DESK-01..50 or AUTO for collision-free auto lease):', False);
  ServerPage.Values[0] := 'AUTO';
  ServerPage.Values[1] := 'AUTO';
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
  ServerUrlVal, DeskIdVal, TargetWsVal: string;
  UserKioskDir, UserKioskJson: string;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    TargetWsVal := ExpandConstant('{drive:{app}}\BreachWorkspace');
    StringChangeEx(TargetWsVal, '\', '\\', True);

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
    if (CompareText(DeskIdVal, 'AUTO') = 0) or (DeskIdVal = '') then
      DeskIdVal := 'AUTO';
    if ExpandConstant('{param:DESKID|}') <> '' then
      DeskIdVal := Trim(ExpandConstant('{param:DESKID|}'));

    JsonContent := '{' + #13#10 +
      '  "ServerUrl": "' + ServerUrlVal + '",' + #13#10 +
      '  "SiteUrl": "http://127.0.0.1:3000",' + #13#10 +
      '  "WorkspacePath": "' + TargetWsVal + '",' + #13#10 +
      '  "DeskId": "' + DeskIdVal + '",' + #13#10 +
      '  "DeskToken": ""' + #13#10 +
      '}';

    // Save base config in installation directory
    SaveStringToFile(ExpandConstant('{app}\kiosk.json'), JsonContent, False);

    // Also populate %LOCALAPPDATA%\BreachKiosk\kiosk.json user overlay
    UserKioskDir := ExpandConstant('{localappdata}\BreachKiosk');
    ForceDirectories(UserKioskDir);
    UserKioskJson := UserKioskDir + '\kiosk.json';
    SaveStringToFile(UserKioskJson, JsonContent, False);

    // Configure Windows Firewall allow rules for LabKiosk
    Exec('netsh.exe', 'advfirewall firewall add rule name="RogueAI-LabKiosk-LAN" dir=in action=allow program="' + ExpandConstant('{app}\{#MyAppExeName}') + '" enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh.exe', 'advfirewall firewall add rule name="RogueAI-LabKiosk-LAN-Out" dir=out action=allow program="' + ExpandConstant('{app}\{#MyAppExeName}') + '" enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // If Host component selected, configure Host server firewall ports and lock down attacks directory ACLs
    if WizardIsComponentSelected('host') then
    begin
      Exec('netsh.exe', 'advfirewall firewall add rule name="RogueAI-ControlPlane-5080" dir=in action=allow protocol=TCP localport=5080 enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec('netsh.exe', 'advfirewall firewall add rule name="RogueAI-Engine-8000" dir=in action=allow protocol=TCP localport=8000 enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec('netsh.exe', 'advfirewall firewall add rule name="RogueAI-Discovery-5081" dir=in action=allow protocol=UDP localport=5081 enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

      // Ensure LocalAppData BreachKiosk has full access for current and standard users
      Exec('icacls.exe', '"' + ExpandConstant('{localappdata}\BreachKiosk') + '" /grant "Users":(OI)(CI)F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Terminate running processes so [InstallDelete] can cleanly wipe any previous workspace/DB state
  Exec(ExpandConstant('{cmd}'), '/c taskkill /F /IM LabKiosk.exe /IM ControlPlane.exe /IM opencode.exe /IM agy.exe /IM node.exe /IM python.exe /IM uvicorn.exe /T >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Purge legacy installation registry entries (e.g. from Nov 6 build)
  RegDeleteKeyIncludingSubkeys(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F2D1C9A-3B7E-4C58-9A41-0D5E7B8C1F23}_is1');
  RegDeleteKeyIncludingSubkeys(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{6F2D1C9A-3B7E-4C58-9A41-0D5E7B8C1F23}_is1');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F2D1C9A-3B7E-4C58-9A41-0D5E7B8C1F23}_is1');

  // Purge all legacy desktop shortcuts across standard desktop, OneDrive desktop, and Public desktop
  DeleteFile(ExpandConstant('{commondesktop}\Breach Protocol Kiosk.lnk'));
  DeleteFile(ExpandConstant('{commondesktop}\Breach Protocol Organizer (Admin).lnk'));
  DeleteFile(ExpandConstant('{userdesktop}\Breach Protocol Kiosk.lnk'));
  DeleteFile(ExpandConstant('{userdesktop}\Breach Protocol Organizer (Admin).lnk'));
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Get-ChildItem -Path @($env:USERPROFILE + ''\Desktop'', $env:USERPROFILE + ''\OneDrive\Desktop'', ''C:\Users\Public\Desktop'', ''C:\Users\dell\OneDrive\Desktop'', ''C:\Users\dell\Desktop'') -Filter ''*Breach*.lnk'' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; Get-ChildItem -Path @($env:APPDATA + ''\Microsoft\Windows\Start Menu\Programs'', $env:PROGRAMDATA + ''\Microsoft\Windows\Start Menu\Programs'') -Filter ''*Breach*.lnk'' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Purge legacy Start Menu folders
  DelTree(ExpandConstant('{commonprograms}\ROGUE AI - The Breach Protocol'), True, True, True);
  DelTree(ExpandConstant('{userprograms}\ROGUE AI - The Breach Protocol'), True, True, True);

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
    // Run deep forensic uninstaller script to remove sandboxes, runner accounts, and firewall rules
    PsScript := ExpandConstant('{app}\scripts\uninstall-complete.ps1');
    if FileExists(PsScript) then
    begin
      Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + PsScript + '" -Silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
