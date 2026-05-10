; ============================================================
;  SessionManagement — Hybrid Inno Setup Installer
;  Version : 1.0
;
;  PROFILES
;    Server PC  : SessionServer + SessionAdmin + DB setup + firewall
;    Admin Only : SessionAdmin only (separate admin laptop)
;    Client PC  : SessionClient only (each kiosk seat)
;    Full       : Everything (development / testing)
;
;  BEFORE RUNNING
;    1. Build the solution in Release mode (Ctrl+Shift+B → Release)
;    2. Ensure Inno Setup 6+ is installed
;    3. Run this script from the solution root folder
; ============================================================

#define AppName    "Intelligent Client-Server Session Management System"
#define AppVersion "1.0.0.0"
#define AppPublisher "Muzaffar Iqbal (BC240212887) — Virtual University of Pakistan"
#define InstallDir "C:\\ICSSMS"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppId={{F25PROJECT8E326-0C55-0001-0000-000000000001}
DefaultDirName={#InstallDir}
DefaultGroupName=ICSSMS
OutputDir=Output
OutputBaseFilename=SessionManagement-Setup-v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=admin
CloseApplications=yes
VersionInfoVersion=1.0.0.0
SetupIconFile=SessionClient\app.ico
UninstallDisplayIcon={app}\app.ico
LicenseFile=LICENSE.txt

; ── Profile presets ─────────────────────────────────────────────────────────
[Types]
Name: "server";  Description: "Server PC   (SessionServer + SessionAdmin)"
Name: "admin";   Description: "Admin Only  (SessionAdmin on separate laptop)"
Name: "client";  Description: "Client PC   (Kiosk — one per seat)"
Name: "full";    Description: "Full Install (development / testing)"

; ── Components ──────────────────────────────────────────────────────────────
[Components]
Name: "server_svc"; Description: "Session Server  (WCF background service)"; Types: server full
Name: "admin_ui";   Description: "Session Admin   (management console)";      Types: server admin full
Name: "client_ui";  Description: "Session Client  (kiosk app)";               Types: client full

; ── Directories ─────────────────────────────────────────────────────────────
[Dirs]
; Server image / profile-pic / log folders
Name: "{commonappdata}\SessionManagement\Images";     Components: server_svc
Name: "{commonappdata}\SessionManagement\ProfilePics"; Components: server_svc
Name: "{commonappdata}\SessionManagement\Logs";        Components: server_svc
Name: "{app}\Logs";                                    Components: server_svc

; Client image / log folders
Name: "{app}\Images"; Components: client_ui
Name: "{app}\Logs";   Components: client_ui

; ── Files ───────────────────────────────────────────────────────────────────
[Files]

; ── Shared DLLs (needed by all three apps) ──────────────────────────────────
Source: "SessionManagement.Shared\bin\Release\SessionManagement.Shared.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\AForge.dll";                   DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\AForge.Video.dll";             DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\AForge.Video.DirectShow.dll";  DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\BCrypt.Net-Next.dll";          DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\System.Buffers.dll";           DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\System.Memory.dll";            DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui
Source: "SessionManagement.Shared\bin\Release\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: server_svc admin_ui client_ui

; ── Server-only DLLs ─────────────────────────────────────────────────────────
Source: "SessionServer\bin\Release\System.Numerics.Vectors.dll";           DestDir: "{app}"; Flags: ignoreversion; Components: server_svc
Source: "SessionManagement.Shared\bin\Release\System.Data.SqlClient.dll";  DestDir: "{app}"; Flags: ignoreversion; Components: server_svc
Source: "SessionManagement.Shared\bin\Release\System.Drawing.Common.dll";  DestDir: "{app}"; Flags: ignoreversion; Components: server_svc

; ── Admin + Client DLL ───────────────────────────────────────────────────────
Source: "SessionAdmin\bin\Release\System.Configuration.ConfigurationManager.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: admin_ui client_ui

; ── SessionServer ─────────────────────────────────────────────────────────────
Source: "SessionServer\bin\Release\SessionServer.exe";        DestDir: "{app}"; Flags: ignoreversion; Components: server_svc
Source: "SessionServer\bin\Release\SessionServer.exe.config"; DestDir: "{app}"; Flags: ignoreversion; Components: server_svc
Source: "SessionServer\app.ico";                              DestDir: "{app}"; Flags: ignoreversion; Components: server_svc

; ── SessionAdmin ──────────────────────────────────────────────────────────────
Source: "SessionAdmin\bin\Release\SessionAdmin.exe";        DestDir: "{app}"; Flags: ignoreversion; Components: admin_ui
Source: "SessionAdmin\bin\Release\SessionAdmin.exe.config"; DestDir: "{app}"; Flags: ignoreversion; Components: admin_ui
Source: "SessionAdmin\app.ico";                             DestDir: "{app}"; Flags: ignoreversion; Components: admin_ui

; ── SessionClient ─────────────────────────────────────────────────────────────
Source: "SessionClient\bin\Release\SessionClient.exe";        DestDir: "{app}"; Flags: ignoreversion; Components: client_ui
Source: "SessionClient\bin\Release\SessionClient.exe.config"; DestDir: "{app}"; Flags: ignoreversion; Components: client_ui
Source: "SessionClient\app.ico";                              DestDir: "{app}"; Flags: ignoreversion; Components: client_ui

; ── SQL setup script (server only) ────────────────────────────────────────────
Source: "SessionManagement_Setup.sql"; DestDir: "{app}"; Flags: ignoreversion; Components: server_svc

; ── Icons / Shortcuts ───────────────────────────────────────────────────────
[Icons]
Name: "{group}\Session Admin";   Filename: "{app}\SessionAdmin.exe";  Components: admin_ui
Name: "{group}\Session Server";  Filename: "{app}\SessionServer.exe"; Components: server_svc
Name: "{group}\Session Client";  Filename: "{app}\SessionClient.exe"; Components: client_ui
Name: "{commondesktop}\Session Admin";  Filename: "{app}\SessionAdmin.exe";  Components: admin_ui
Name: "{commondesktop}\Session Server"; Filename: "{app}\SessionServer.exe"; Components: server_svc
Name: "{commondesktop}\Session Client"; Filename: "{app}\SessionClient.exe"; Components: client_ui

; ── Post-install actions ────────────────────────────────────────────────────
[Run]

; 1. Create database, tables, stored procedures, and seed data.
Filename: "{sys}\cmd.exe"; Parameters: "/c sqlcmd -S ""{code:GetSqlInstance}"" -E -i ""{app}\SessionManagement_Setup.sql"" > ""{app}\Logs\db_setup.log"" 2>&1"; StatusMsg: "Setting up database..."; Components: server_svc; Flags: runhidden waituntilterminated; Check: ShouldCreateDatabase

; 2. Open firewall port (server only)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""ICSSMS SessionService"" dir=in action=allow protocol=TCP localport={code:GetServerPort}"; StatusMsg: "Configuring firewall..."; Components: server_svc; Flags: runhidden

; 3. Register SessionServer as a scheduled task for auto-start (server only)
Filename: "{sys}\schtasks.exe"; Parameters: "/create /tn ""ICSSMS\SessionServer"" /tr ""{app}\SessionServer.exe"" /sc onstart /ru SYSTEM /rl HIGHEST /f"; StatusMsg: "Registering auto-start..."; Components: server_svc; Flags: runhidden

; 4. Add SessionClient to KioskUser startup folder (client only)
Filename: "{sys}\cmd.exe"; Parameters: "/c mklink ""{userappdata}\Microsoft\Windows\Start Menu\Programs\Startup\SessionClient.lnk"" ""{app}\SessionClient.exe"""; StatusMsg: "Configuring auto-start for kiosk user..."; Components: client_ui; Flags: runhidden

; 5. Launch Session Server after install (server profile)
Filename: "{app}\SessionServer.exe"; Description: "Launch Session Server now"; Components: server_svc; Flags: nowait postinstall skipifsilent

; 6. Launch Session Admin after install (server/admin profiles)
Filename: "{app}\SessionAdmin.exe"; Description: "Launch Session Admin now"; Components: admin_ui; Flags: nowait postinstall skipifsilent

; 7. Launch Session Client after install (client profile)
Filename: "{app}\SessionClient.exe"; Description: "Launch Session Client now"; Components: client_ui; Flags: nowait postinstall skipifsilent

; ── Uninstall: stop scheduled task and firewall rule (server only) ───────────
[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""ICSSMS\SessionServer"" /f"; Flags: runhidden; RunOnceId: "DelTask"; Components: server_svc
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ICSSMS SessionService"""; Flags: runhidden; RunOnceId: "DelFirewall"; Components: server_svc

; ── Uninstall: remove leftover files and folders not tracked by installer ────
[UninstallDelete]
Type: filesandordirs; Name: "{app}\Logs";                                                                              Components: server_svc
Type: filesandordirs; Name: "{app}\Images";                                                                            Components: client_ui
Type: filesandordirs; Name: "{commonappdata}\SessionManagement";                                                       Components: server_svc
Type: files;          Name: "{userappdata}\Microsoft\Windows\Start Menu\Programs\Startup\SessionClient.lnk";          Components: client_ui
Type: filesandordirs; Name: "{app}"

; ── Pascal code section ──────────────────────────────────────────────────────
[Code]

{ ── Shared variables ─────────────────────────────────────────────────────── }
var
  { Page 1 — Server PC: SQL instance (field 0) + WCF port (field 1) }
  SqlInstancePage  : TInputQueryWizardPage;
  AdminPinPageSrv  : TInputQueryWizardPage;   { Page 3 — Session Admin PIN }
  CreateDbCheckBox : TNewCheckBox;

  { Page 2 — Admin-only / Client: Server IP (field 0) + port (field 1) }
  ServerAddrPage   : TInputQueryWizardPage;
  { Page 4 — Client: machine name (field 0) + location (field 1) }
  MachineInfoPage  : TInputQueryWizardPage;
  AdminPinPageCli  : TInputQueryWizardPage;   { Page 5 — Session Client PIN }


{ ── Component helpers (declared first — called by helpers below) ─────────── }
function IsServerComponentSelected(): Boolean;
begin
  Result := IsComponentSelected('server_svc');
end;

function IsClientComponentSelected(): Boolean;
begin
  Result := IsComponentSelected('client_ui');
end;

function IsAdminComponentSelected(): Boolean;
begin
  Result := IsComponentSelected('admin_ui');
end;


{ ── Prerequisite checks ──────────────────────────────────────────────────── }
function IsDotNet472Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', Release) and (Release >= 461808);
end;

function IsSqlCmdAvailable(): Boolean;
begin
  Result := FileExists('C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe')
         or FileExists('C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\130\Tools\Binn\sqlcmd.exe')
         or FileExists('C:\Program Files\Microsoft SQL Server\110\Tools\Binn\sqlcmd.exe');
end;


{ ── Helpers ──────────────────────────────────────────────────────────────── }
function ShouldCreateDatabase(): Boolean;
begin
  Result := IsServerComponentSelected() and
            ((CreateDbCheckBox = nil) or CreateDbCheckBox.Checked);
end;

function GetSqlInstance(Param: String): String;
begin
  if SqlInstancePage <> nil then
    Result := SqlInstancePage.Values[0]
  else
    Result := 'localhost\SQLEXPRESS';
end;

function GetServerPort(Param: String): String;
begin
  if SqlInstancePage <> nil then
    Result := SqlInstancePage.Values[1]
  else
    Result := '8001';
end;


{ ── InitializeSetup — global prerequisite check ──────────────────────────── }
function InitializeSetup(): Boolean;
begin
  Result := True;

  if not IsDotNet472Installed() then
  begin
    MsgBox(
      '.NET Framework 4.7.2 or later is required.' + #13#10 +
      'Download the offline installer from:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet-framework/net472',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;
end;


{ ── InitializeWizard — create all conditional pages ─────────────────────── }
{ Page chain (all profiles share one linear chain; ShouldSkipPage hides inapplicable pages):
    SqlInstancePage → ServerAddrPage → AdminPinPageSrv → MachineInfoPage → AdminPinPageCli
  This guarantees Server → Admin → Client order regardless of which pages are visible. }
procedure InitializeWizard();
begin
  { ── Page 1: Session Server — Database & Port (server_svc only) ──────────── }
  SqlInstancePage := CreateInputQueryPage(wpSelectComponents,
    'Session Server — Database & Port',
    'Configure the SQL Server connection and WCF service port for Session Server.',
    'SQL instance example: localhost\SQLEXPRESS  or  localhost' + #13#10 +
    'Leave port as 8001 unless that port is already in use.');
  SqlInstancePage.Add('SQL Server instance:', False);
  SqlInstancePage.Add('WCF service port:', False);
  SqlInstancePage.Values[0] := 'localhost\SQLEXPRESS';
  SqlInstancePage.Values[1] := '8001';

  CreateDbCheckBox := TNewCheckBox.Create(SqlInstancePage);
  CreateDbCheckBox.Parent  := SqlInstancePage.Surface;
  CreateDbCheckBox.Caption := 'Create fresh database during installation' + #13#10 +
    'Uncheck if you are restoring an existing backup from your old server.';
  CreateDbCheckBox.Checked := True;
  CreateDbCheckBox.Left    := 0;
  CreateDbCheckBox.Top     := SqlInstancePage.Edits[1].Top +
                               SqlInstancePage.Edits[1].Height + 16;
  CreateDbCheckBox.Width   := SqlInstancePage.SurfaceWidth;
  CreateDbCheckBox.Height  := 40;

  { ── Page 2: Session Server — Connection Details (admin-only / client-only) ─ }
  ServerAddrPage := CreateInputQueryPage(SqlInstancePage.ID,
    'Session Server — Connection Details',
    'Enter the IP address and port of the PC running Session Server.',
    'IP example: 192.168.1.10   Port default: 8001' + #13#10 +
    'For development on the same PC use: localhost');
  ServerAddrPage.Add('Server IP address:', False);
  ServerAddrPage.Add('Server port:', False);
  ServerAddrPage.Values[0] := 'localhost';
  ServerAddrPage.Values[1] := '8001';

  { ── Page 3: Session Admin — IT Admin PIN (admin_ui) ─────────────────────── }
  AdminPinPageSrv := CreateInputQueryPage(ServerAddrPage.ID,
    'Session Admin — IT Admin PIN',
    'Set the PIN that protects the hidden admin settings dialog in Session Admin (Ctrl+Alt+Shift+S).',
    'Use the same PIN on all client PCs so you only need to remember one code.' + #13#10 +
    'Minimum 4 characters. Default "1234" — you MUST change this.');
  AdminPinPageSrv.Add('IT Admin PIN:', True);
  AdminPinPageSrv.Values[0] := '';

  { ── Page 4: Session Client — Machine Identity (client_ui) ───────────────── }
  MachineInfoPage := CreateInputQueryPage(AdminPinPageSrv.ID,
    'Session Client — Machine Identity',
    'Enter the display name and seat location for this kiosk PC.',
    'These details appear in the Session Admin → Clients tab.' + #13#10 +
    'Example name: Computer 01   Example location: Row A - Seat 1');
  MachineInfoPage.Add('Machine name:', False);
  MachineInfoPage.Add('Location / Seat:', False);
  MachineInfoPage.Values[0] := 'Computer 01';
  MachineInfoPage.Values[1] := 'Row A - Seat 1';

  { ── Page 5: Session Client — IT Admin PIN (client_ui) ───────────────────── }
  AdminPinPageCli := CreateInputQueryPage(MachineInfoPage.ID,
    'Session Client — IT Admin PIN',
    'Set the PIN that protects the hidden admin settings dialog in Session Client (Ctrl+Alt+Shift+S).',
    'Use the same PIN as set on the server PC.' + #13#10 +
    'Minimum 4 characters. Default "1234" — you MUST change this.');
  AdminPinPageCli.Add('IT Admin PIN:', True);
  AdminPinPageCli.Values[0] := '';
end;


{ ── ShouldSkipPage — show pages only for the relevant component profile ──── }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  { DB + Port: server PC only — database lives on server }
  if PageID = SqlInstancePage.ID then
    Result := not IsServerComponentSelected();

  { Server IP + Port: needed when server is NOT on this PC
    — Admin-only (separate laptop needs server IP)
    — Client-only (kiosk needs server IP)
    — Skip for Server PC and Full (server is local → localhost used automatically) }
  if PageID = ServerAddrPage.ID then
    Result := IsServerComponentSelected() or
              not (IsClientComponentSelected() or IsAdminComponentSelected());

  { Admin PIN: shown whenever admin_ui is installed (Server PC + Admin Only + Full) }
  if PageID = AdminPinPageSrv.ID then
    Result := not IsAdminComponentSelected();

  { Machine identity + Client PIN: kiosk client only }
  if (PageID = MachineInfoPage.ID) or
     (PageID = AdminPinPageCli.ID) then
    Result := not IsClientComponentSelected();
end;


{ ── Validation ───────────────────────────────────────────────────────────── }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Port: Integer;
begin
  Result := True;

  { Validate server configuration page (SQL instance + port) }
  if CurPageID = SqlInstancePage.ID then
  begin
    if Trim(SqlInstancePage.Values[0]) = '' then
    begin
      MsgBox('SQL Server instance cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    Port := StrToIntDef(SqlInstancePage.Values[1], 0);
    if (Port < 1) or (Port > 65535) then
    begin
      MsgBox('Port must be a number between 1 and 65535.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if not IsSqlCmdAvailable() then
    begin
      if MsgBox(
        'sqlcmd.exe was not found. The database setup step may fail.' + #13#10 +
        'Install SQL Server Express first, then re-run this installer.' + #13#10#13#10 +
        'Continue anyway?',
        mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;

  { Validate server address + port (admin-only / client page) }
  if CurPageID = ServerAddrPage.ID then
  begin
    if Trim(ServerAddrPage.Values[0]) = '' then
    begin
      MsgBox('Server address cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    Port := StrToIntDef(ServerAddrPage.Values[1], 0);
    if (Port < 1) or (Port > 65535) then
    begin
      MsgBox('Port must be a number between 1 and 65535.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate Session Admin PIN }
  if CurPageID = AdminPinPageSrv.ID then
  begin
    if Length(AdminPinPageSrv.Values[0]) < 4 then
    begin
      MsgBox('IT Admin PIN must be at least 4 characters.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate machine name + location }
  if CurPageID = MachineInfoPage.ID then
  begin
    if Trim(MachineInfoPage.Values[0]) = '' then
    begin
      MsgBox('Machine name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(MachineInfoPage.Values[1]) = '' then
    begin
      MsgBox('Location / Seat cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate Session Client PIN }
  if CurPageID = AdminPinPageCli.ID then
  begin
    if Length(AdminPinPageCli.Values[0]) < 4 then
    begin
      MsgBox('IT Admin PIN must be at least 4 characters.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;


{ ── SetConfigValue — sets a key=value pair in a .exe.config appSettings XML ─ }
procedure SetConfigValue(ConfigFile, Key, NewValue: String);
var
  Raw: AnsiString;
  S, Tag, Left, Right: String;
  P, Q: Integer;
begin
  if not FileExists(ConfigFile) then Exit;
  if not LoadStringFromFile(ConfigFile, Raw) then Exit;
  S   := String(Raw);
  Tag := 'key="' + Key + '" value="';
  P   := Pos(Tag, S);
  if P = 0 then Exit;
  P := P + Length(Tag);
  Q := P;
  while (Q <= Length(S)) and (S[Q] <> '"') do
    Q := Q + 1;
  Left  := Copy(S, 1, P - 1);
  Right := Copy(S, Q, Length(S));
  SaveStringToFile(ConfigFile, AnsiString(Left + NewValue + Right), False);
end;


{ ── CurStepChanged — write all wizard values to config files ─────────────── }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ServerConfig, AdminConfig, ClientConfig: String;
  Pin: String;
  RawContent: AnsiString;
  Content: String;
begin
  if CurStep <> ssPostInstall then Exit;

  ServerConfig := ExpandConstant('{app}\SessionServer.exe.config');
  AdminConfig  := ExpandConstant('{app}\SessionAdmin.exe.config');
  ClientConfig := ExpandConstant('{app}\SessionClient.exe.config');

  { ── Write server settings (server_svc only) ─────────────────────────────── }
  if IsServerComponentSelected() then
  begin
    if FileExists(ServerConfig) then
    begin
      if LoadStringFromFile(ServerConfig, RawContent) then
      begin
        Content := String(RawContent);
        StringChangeEx(Content, 'localhost\SQLEXPRESS',
                       SqlInstancePage.Values[0], False);
        StringChangeEx(Content, 'value="8001"',
                       'value="' + SqlInstancePage.Values[1] + '"', False);
        SaveStringToFile(ServerConfig, AnsiString(Content), False);
      end;
    end;
  end;

  { ── Write admin settings (admin_ui — Server PC + Admin Only + Full) ──────── }
  if IsAdminComponentSelected() and FileExists(AdminConfig) then
  begin
    Pin := AdminPinPageSrv.Values[0];
    if Pin <> '' then
      SetConfigValue(AdminConfig, 'AdminSettingsPin', Pin);

    { Admin-only on separate laptop: must point to remote server IP }
    if not IsServerComponentSelected() then
    begin
      SetConfigValue(AdminConfig, 'ServerAddress', ServerAddrPage.Values[0]);
      SetConfigValue(AdminConfig, 'ServerPort',    ServerAddrPage.Values[1]);
    end;
  end;

  { ── Write client settings (client_ui) ───────────────────────────────────── }
  if IsClientComponentSelected() and FileExists(ClientConfig) then
  begin
    { Full install: server on same PC — always use localhost }
    if IsServerComponentSelected() then
    begin
      SetConfigValue(ClientConfig, 'ServerAddress', 'localhost');
      SetConfigValue(ClientConfig, 'ServerPort',    SqlInstancePage.Values[1]);
    end
    else
    begin
      SetConfigValue(ClientConfig, 'ServerAddress', ServerAddrPage.Values[0]);
      SetConfigValue(ClientConfig, 'ServerPort',    ServerAddrPage.Values[1]);
    end;
    SetConfigValue(ClientConfig, 'ClientMachineName', MachineInfoPage.Values[0]);
    SetConfigValue(ClientConfig, 'ClientLocation',    MachineInfoPage.Values[1]);

    Pin := AdminPinPageCli.Values[0];
    if Pin <> '' then
      SetConfigValue(ClientConfig, 'AdminSettingsPin', Pin);
  end;
end;


{ ── CurUninstallStepChanged — force-close all apps before files are removed ─ }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im SessionServer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im SessionAdmin.exe',  '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im SessionClient.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
