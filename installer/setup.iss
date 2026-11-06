; Inno Setup 6 script for ROGUE AI: The Breach Protocol
; Produces a single, self-contained setup file: dist\RogueAI-Setup.exe
; Packages: .NET 9 LabKiosk, ControlPlane, Python engine, Node.js, Harbour Notes site, and all 10 attacks.

#define AppName "ROGUE AI: The Breach Protocol"
#define AppGroupName "ROGUE AI - The Breach Protocol"
#define AppVersion "1.0.0"
#define AppPublisher "ROGUE AI Event"
#define DefaultDirName "{autopf64}\RogueAI"

[Setup]
AppId={{6F2D1C9A-3B7E-4C58-9A41-0D5E7B8C1F23}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={#DefaultDirName}
DefaultGroupName={#AppGroupName}
OutputDir=..\dist
OutputBaseFilename=RogueAI-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\LabKiosk.exe
ChangesEnvironment=yes
AllowNoIcons=yes

[CustomMessages]
RolePageTitle=Select Computer Role
RolePageLabel=Select how this computer will be used in the Breach Protocol event:
RoleParticipant=Participant Kiosk (connects to an organizer across the LAN)
RoleOrganizer=Organizer Station (runs control plane, attack engine, and admin command center)
RoleBoth=Both / Standalone (organizer + participant kiosk on this computer)
IpPageTitle=Organizer Connection
IpPageLabel=Enter the Organizer Station's LAN IP address:
IpPageSubLabel=Enter the physical LAN IP shown in the Organizer Command Center title bar (e.g. 192.168.1.50 or 172.110.2.75). Do NOT use virtual Hyper-V/WSL addresses.
IpEditPrompt=Organizer LAN IP:

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "C:\BreachWorkspace"; Permissions: users-modify

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Participant shortcuts
Name: "{group}\Breach Protocol Kiosk"; Filename: "{app}\LabKiosk.exe"; WorkingDir: "{app}"; Check: IsParticipantMode
Name: "{commondesktop}\Breach Protocol Kiosk"; Filename: "{app}\LabKiosk.exe"; WorkingDir: "{app}"; Check: IsParticipantMode

; Organizer shortcuts
Name: "{group}\Breach Protocol Organizer (Admin)"; Filename: "{app}\LabKiosk.exe"; Parameters: "--host"; WorkingDir: "{app}"; Check: IsOrganizerMode
Name: "{commondesktop}\Breach Protocol Organizer (Admin)"; Filename: "{app}\LabKiosk.exe"; Parameters: "--host"; WorkingDir: "{app}"; Check: IsOrganizerMode

; Uninstaller
Name: "{group}\Uninstall {#AppGroupName}"; Filename: "{uninstallexe}"

[Code]
var
  RolePage: TInputOptionWizardPage;
  IpPage: TInputQueryWizardPage;
  SelectedRole: string;
  OrganizerIp: string;

function IsOrganizerMode: boolean;
begin
  Result := (SelectedRole = 'organizer') or (SelectedRole = 'both');
end;

function IsParticipantMode: boolean;
begin
  Result := (SelectedRole = 'participant') or (SelectedRole = 'both');
end;

function IsParticipantOnly: boolean;
begin
  Result := (SelectedRole = 'participant');
end;

function CanRunFirewall: boolean;
begin
  Result := IsOrganizerMode;
end;

function CheckWebView2Installed: boolean;
var
  installed: boolean;
  versionStr: string;
begin
  installed := RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', versionStr) or
               RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', versionStr) or
               RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', versionStr);
  Result := installed and (versionStr <> '');
end;

function CheckGitInstalled: boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('cmd.exe', '/c git --version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function CheckOpencodeInstalled: boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('cmd.exe', '/c opencode --version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function CheckAgyInstalled: boolean;
var
  UserProfile, AgyExe: string;
begin
  UserProfile := GetEnv('USERPROFILE');
  AgyExe := UserProfile + '\AppData\Local\agy\bin\agy.exe';
  Result := FileExists(AgyExe);
end;

procedure ProvisionCliTools;
var
  AppDir, NodeDir, NpmCmd, UserProfile, LocalAppData, AgyDir, BundledAgy: string;
  ResultCode: Integer;
begin
  AppDir := ExpandConstant('{app}');
  NodeDir := AppDir + '\node';
  NpmCmd := NodeDir + '\npm.cmd';
  UserProfile := GetEnv('USERPROFILE');
  LocalAppData := GetEnv('LOCALAPPDATA');

  // 1. Provision OpenCode if not present
  if not CheckOpencodeInstalled then
  begin
    if FileExists(NpmCmd) then
    begin
      Exec('cmd.exe', '/c ""' + NpmCmd + '"" install -g opencode', NodeDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;

  // 2. Provision agy if bundled in {app}\tools\agy
  AgyDir := LocalAppData + '\agy\bin';
  BundledAgy := AppDir + '\tools\agy\agy.exe';
  if FileExists(BundledAgy) and (not FileExists(AgyDir + '\agy.exe')) then
  begin
    ForceDirectories(AgyDir);
    Exec('cmd.exe', '/c copy /y ""' + BundledAgy + '"" ""' + AgyDir + '\agy.exe""', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function GetServerUrl: string;
begin
  if (SelectedRole = 'organizer') or (SelectedRole = 'both') then
    Result := 'http://127.0.0.1:5080'
  else if OrganizerIp <> '' then
    Result := 'http://' + OrganizerIp + ':5080'
  else
    Result := 'http://127.0.0.1:5080';
end;

function EscapeJson(S: string): string;
begin
  StringChangeEx(S, '\', '\\', True);
  Result := S;
end;

procedure WriteKioskJson;
var
  JsonContent: string;
  JsonFile: string;
  ServerUrl: string;
  AppDir: string;
  NodeExe: string;
  SiteTemplate: string;
begin
  ServerUrl := GetServerUrl;
  AppDir := ExpandConstant('{app}');
  
  NodeExe := EscapeJson(AppDir + '\node\node.exe');
  SiteTemplate := EscapeJson(AppDir + '\site');

  JsonContent := '{' + #13#10 +
                 '  "ServerUrl": "' + ServerUrl + '",' + #13#10 +
                 '  "SiteUrl": "http://127.0.0.1:3000",' + #13#10 +
                 '  "WorkspacePath": "C:\\BreachWorkspace",' + #13#10 +
                 '  "SiteTemplatePath": "' + SiteTemplate + '",' + #13#10 +
                 '  "NodePath": "' + NodeExe + '",' + #13#10 +
                 '  "AgyPath": "%LOCALAPPDATA%\\agy\\bin\\agy.exe",' + #13#10 +
                 '  "OpenCodePath": "opencode",' + #13#10 +
                 '  "OpenCodeModel": "",' + #13#10 +
                 '  "OpenCodePort": 4096,' + #13#10 +
                 '  "OpenCodeCap": 30' + #13#10 +
                 '}';

  JsonFile := ExpandConstant('{app}\kiosk.json');
  SaveStringToFile(JsonFile, JsonContent, False);
end;

procedure InitializeWizard;
begin
  SelectedRole := 'participant';

  RolePage := CreateInputOptionPage(wpWelcome,
    CustomMessage('RolePageTitle'),
    CustomMessage('RolePageLabel'),
    'Choose the role for this installation:',
    True, False);
  RolePage.Add(CustomMessage('RoleParticipant'));
  RolePage.Add(CustomMessage('RoleOrganizer'));
  RolePage.Add(CustomMessage('RoleBoth'));
  RolePage.SelectedValueIndex := 0;

  IpPage := CreateInputQueryPage(RolePage.ID,
    CustomMessage('IpPageTitle'),
    CustomMessage('IpPageLabel'),
    CustomMessage('IpPageSubLabel'));
  IpPage.Add(CustomMessage('IpEditPrompt'), False);
  IpPage.Values[0] := '';
end;

function NextButtonClick(CurPageID: Integer): boolean;
var
  selectedIdx: Integer;
begin
  Result := True;
  if CurPageID = RolePage.ID then
  begin
    selectedIdx := RolePage.SelectedValueIndex;
    if selectedIdx = 0 then
      SelectedRole := 'participant'
    else if selectedIdx = 1 then
      SelectedRole := 'organizer'
    else
      SelectedRole := 'both';
  end
  else if CurPageID = IpPage.ID then
  begin
    OrganizerIp := Trim(IpPage.Values[0]);
  end;
end;

function ShouldSkipPage(PageID: Integer): boolean;
begin
  Result := False;
  if PageID = IpPage.ID then
  begin
    if SelectedRole <> 'participant' then
      Result := True;
  end;
end;

procedure ConfigureAgySettings;
var
  UserProfile, GeminiDir, SettingsFile, Content: string;
begin
  UserProfile := GetEnv('USERPROFILE');
  if UserProfile = '' then Exit;
  GeminiDir := UserProfile + '\.gemini\antigravity-cli';
  if not DirExists(GeminiDir) then
    ForceDirectories(GeminiDir);
  SettingsFile := GeminiDir + '\settings.json';
  if not FileExists(SettingsFile) then
  begin
    Content := '{' + #13#10 +
               '  "colorScheme": "dark",' + #13#10 +
               '  "trustedWorkspaces": [' + #13#10 +
               '    "C:\\BreachWorkspace",' + #13#10 +
               '    "c:\\breachworkspace"' + #13#10 +
               '  ]' + #13#10 +
               '}';
    SaveStringToFile(SettingsFile, Content, False);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteKioskJson;
    ConfigureAgySettings;
    ProvisionCliTools;

    // Verify prerequisites and advise the user if anything is missing
    if not CheckWebView2Installed then
    begin
      MsgBox('Note: Microsoft Edge WebView2 Runtime was not detected on this system.' + #13#10 +
             'ROGUE AI uses WebView2 for the website pane and terminal tabs.' + #13#10 +
             'If the app fails to start, please install the Microsoft Edge WebView2 Evergreen Runtime.',
             mbInformation, MB_OK);
    end;

    if IsOrganizerMode and (not CheckGitInstalled) then
    begin
      MsgBox('Note: Git was not found in your system PATH.' + #13#10 +
             'The Organizer mode runs a server-owned Git engine to track participant workspaces and commits.' + #13#10 +
             'Please make sure Git for Windows is installed so the attack engine can operate properly.',
             mbInformation, MB_OK);
    end;
  end;
end;

[Run]
; Firewall entries for Organizer / Host
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""RogueAI ControlPlane"" dir=in action=allow protocol=TCP localport=5080 profile=any"; Flags: runhidden skipifdoesntexist; Check: CanRunFirewall; StatusMsg: "Configuring firewall for control plane (port 5080)..."
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""RogueAI ControlPlane EXE"" dir=in action=allow program=""{app}\server\ControlPlane.exe"" enable=yes profile=any"; Flags: runhidden skipifdoesntexist; Check: CanRunFirewall; StatusMsg: "Configuring application firewall for ControlPlane..."
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""RogueAI Engine"" dir=in action=allow protocol=TCP localport=8000 profile=any"; Flags: runhidden skipifdoesntexist; Check: CanRunFirewall; StatusMsg: "Configuring firewall for attack engine (port 8000)..."

; Optional post-install launches
Filename: "{app}\LabKiosk.exe"; Description: "Launch Breach Protocol Kiosk"; Flags: nowait postinstall skipifsilent; Check: IsParticipantOnly
Filename: "{app}\LabKiosk.exe"; Parameters: "--host"; Description: "Launch Organizer Command Center"; Flags: nowait postinstall skipifsilent; Check: IsOrganizerMode

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""RogueAI ControlPlane"""; Flags: runhidden; RunOnceId: "DelRogueAiFw5080"
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""RogueAI ControlPlane EXE"""; Flags: runhidden; RunOnceId: "DelRogueAiFwExe"
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""RogueAI Engine"""; Flags: runhidden; RunOnceId: "DelRogueAiFw8000"
