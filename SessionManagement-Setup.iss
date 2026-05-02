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

#define AppName    "NetCafe Session Management"
#define AppVersion "1.0"
#define AppPublisher "BC240212887"
#define InstallDir "C:\NetCafe\SessionManagement"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppId={{F25PROJECT8E326-CAFE-0001-0000-000000000001}
DefaultDirName={#InstallDir}
DefaultGroupName=NetCafe
OutputDir=Output
OutputBaseFilename=SessionManagement-Setup-v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=admin
SetupIconFile=SessionClient\app.ico
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
Name: "{commondesktop}\Session Admin"; Filename: "{app}\SessionAdmin.exe"; Components: admin_ui

; ── Post-install actions ────────────────────────────────────────────────────
[Run]

; 1. Create the database (server only)
Filename: "{sys}\cmd.exe";
  Parameters: "/c sqlcmd -S ""{code:GetSqlInstance}"" -E -i ""{app}\SessionManagement_Setup.sql"" > ""{app}\Logs\db_setup.log"" 2>&1";
  StatusMsg: "Creating database...";
  Components: server_svc;
  Flags: runhidden waituntilterminated

; 2. Open firewall port (server only)
Filename: "{sys}\netsh.exe";
  Parameters: "advfirewall firewall add rule name=""NetCafe SessionService"" dir=in action=allow protocol=TCP localport={code:GetServerPort}";
  StatusMsg: "Configuring firewall...";
  Components: server_svc;
  Flags: runhidden

; 3. Register SessionServer as a scheduled task for auto-start (server only)
Filename: "{sys}\schtasks.exe";
  Parameters: "/create /tn ""NetCafe\SessionServer"" /tr ""{app}\SessionServer.exe"" /sc onstart /ru SYSTEM /rl HIGHEST /f";
  StatusMsg: "Registering auto-start...";
  Components: server_svc;
  Flags: runhidden

; 4. Add SessionClient to KioskUser startup folder (client only)
Filename: "{sys}\cmd.exe";
  Parameters: "/c mklink ""{userappdata}\Microsoft\Windows\Start Menu\Programs\Startup\SessionClient.lnk"" ""{app}\SessionClient.exe""";
  StatusMsg: "Configuring auto-start for kiosk user...";
  Components: client_ui;
  Flags: runhidden

; 5. Launch Session Admin after install (server/admin profiles)
Filename: "{app}\SessionAdmin.exe";
  Description: "Launch Session Admin now";
  Components: admin_ui;
  Flags: nowait postinstall skipifsilent

; ── Pascal code section ──────────────────────────────────────────────────────
[Code]

{ ── Shared variables ─────────────────────────────────────────────────────── }
var
  { Server wizard pages }
  SqlInstancePage  : TInputQueryWizardPage;
  ServerPortPage   : TInputQueryWizardPage;
  AdminPinPageSrv  : TInputQueryWizardPage;

  { Client wizard pages }
  ServerAddrPage   : TInputQueryWizardPage;
  MachineNamePage  : TInputQueryWizardPage;
  LocationPage     : TInputQueryWizardPage;
  AdminPinPageCli  : TInputQueryWizardPage;


{ ── Helpers ──────────────────────────────────────────────────────────────── }
function GetSqlInstance(Param: String): String;
begin
  if SqlInstancePage <> nil then
    Result := SqlInstancePage.Values[0]
  else
    Result := 'localhost\SQLEXPRESS';
end;

function GetServerPort(Param: String): String;
begin
  if ServerPortPage <> nil then
    Result := ServerPortPage.Values[0]
  else
    Result := '8001';
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

function IsServerComponentSelected(): Boolean;
begin
  Result := IsComponentSelected('server_svc');
end;

function IsClientComponentSelected(): Boolean;
begin
  Result := IsComponentSelected('client_ui');
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
procedure InitializeWizard();
begin
  { ── Server pages ───────────────────────────────────────────────────────── }
  SqlInstancePage := CreateInputQueryPage(wpSelectComponents,
    'SQL Server Instance',
    'Enter the SQL Server instance name for this PC.',
    'Example: localhost\SQLEXPRESS  or  localhost  (for full SQL Server)');
  SqlInstancePage.Add('SQL Server instance:', False);
  SqlInstancePage.Values[0] := 'localhost\SQLEXPRESS';

  ServerPortPage := CreateInputQueryPage(SqlInstancePage.ID,
    'WCF Service Port',
    'Enter the port SessionServer will listen on.',
    'Default is 8001. Only change if that port is already in use.');
  ServerPortPage.Add('Server port:', False);
  ServerPortPage.Values[0] := '8001';

  AdminPinPageSrv := CreateInputQueryPage(ServerPortPage.ID,
    'IT Admin PIN',
    'Set the PIN that protects the hidden network settings dialog (Ctrl+Alt+Shift+S).',
    'Use the same PIN on all client PCs so you only need to remember one code.' + #13#10 +
    'Minimum 4 characters. Default "1234" — you MUST change this.');
  AdminPinPageSrv.Add('IT Admin PIN:', True);
  AdminPinPageSrv.Values[0] := '';

  { ── Client pages ───────────────────────────────────────────────────────── }
  ServerAddrPage := CreateInputQueryPage(wpSelectComponents,
    'Server Address',
    'Enter the LAN IP address of the PC running SessionServer.',
    'Example: 192.168.1.10' + #13#10 +
    'For development on the same PC, use: localhost');
  ServerAddrPage.Add('Server IP address:', False);
  ServerAddrPage.Values[0] := 'localhost';

  MachineNamePage := CreateInputQueryPage(ServerAddrPage.ID,
    'Machine Name',
    'Enter a display name for this kiosk PC.',
    'This name appears in the SessionAdmin Clients tab.' + #13#10 +
    'Example: Computer 01');
  MachineNamePage.Add('Machine name:', False);
  MachineNamePage.Values[0] := 'Computer 01';

  LocationPage := CreateInputQueryPage(MachineNamePage.ID,
    'Seat Location',
    'Enter the physical location of this kiosk seat.',
    'Example: Row A - Seat 1');
  LocationPage.Add('Location / Seat:', False);
  LocationPage.Values[0] := 'Row A - Seat 1';

  AdminPinPageCli := CreateInputQueryPage(LocationPage.ID,
    'IT Admin PIN',
    'Set the PIN that protects the hidden network settings dialog (Ctrl+Alt+Shift+S).',
    'Use the same PIN as set on the server PC.' + #13#10 +
    'Minimum 4 characters. Default "1234" — you MUST change this.');
  AdminPinPageCli.Add('IT Admin PIN:', True);
  AdminPinPageCli.Values[0] := '';
end;


{ ── ShouldSkipPage — show pages only for the relevant component profile ──── }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  { Server-only pages }
  if (PageID = SqlInstancePage.ID) or
     (PageID = ServerPortPage.ID)  or
     (PageID = AdminPinPageSrv.ID) then
    Result := not IsServerComponentSelected();

  { Client-only pages }
  if (PageID = ServerAddrPage.ID)  or
     (PageID = MachineNamePage.ID) or
     (PageID = LocationPage.ID)    or
     (PageID = AdminPinPageCli.ID) then
    Result := not IsClientComponentSelected();
end;


{ ── Validation ───────────────────────────────────────────────────────────── }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Port: Integer;
begin
  Result := True;

  { Validate server port }
  if CurPageID = ServerPortPage.ID then
  begin
    if not TryStrToInt(ServerPortPage.Values[0], Port)
       or (Port < 1) or (Port > 65535) then
    begin
      MsgBox('Port must be a number between 1 and 65535.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate server admin PIN }
  if CurPageID = AdminPinPageSrv.ID then
  begin
    if Length(AdminPinPageSrv.Values[0]) < 4 then
    begin
      MsgBox('IT Admin PIN must be at least 4 characters.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate server address (client page) }
  if CurPageID = ServerAddrPage.ID then
  begin
    if Trim(ServerAddrPage.Values[0]) = '' then
    begin
      MsgBox('Server address cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate machine name }
  if CurPageID = MachineNamePage.ID then
  begin
    if Trim(MachineNamePage.Values[0]) = '' then
    begin
      MsgBox('Machine name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Validate client admin PIN }
  if CurPageID = AdminPinPageCli.ID then
  begin
    if Length(AdminPinPageCli.Values[0]) < 4 then
    begin
      MsgBox('IT Admin PIN must be at least 4 characters.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Warn if SQL Server not found when server component selected }
  if (CurPageID = SqlInstancePage.ID) and IsServerComponentSelected() then
  begin
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
end;


{ ── WriteConfigValue — replaces a key value in a .exe.config XML file ────── }
procedure WriteConfigValue(ConfigFile, Key, Value: String);
var
  Content: String;
  OldStr, NewStr: String;
begin
  if not LoadStringFromFile(ConfigFile, Content) then Exit;

  OldStr := 'key="' + Key + '" value="';
  { Find existing value and replace it }
  if Pos(OldStr, Content) > 0 then
  begin
    NewStr := OldStr + Value + '"';
    { Replace old key="X" value="oldValue" with new value }
    StringChangeEx(Content, OldStr,
                   '<<PLACEHOLDER_' + Key + '>>', False);
    { Now we need a smarter replacement — find the full attribute pair }
  end;
  { Use SetIniValue which handles .config files (XML with ini-like appSettings) }
  SetIniValue(ConfigFile, 'appSettings', Key, Value);
end;


{ ── CurStepChanged — write all wizard values to config files ─────────────── }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ServerConfig, AdminConfig, ClientConfig: String;
  Pin: String;
begin
  if CurStep <> ssPostInstall then Exit;

  ServerConfig := ExpandConstant('{app}\SessionServer.exe.config');
  AdminConfig  := ExpandConstant('{app}\SessionAdmin.exe.config');
  ClientConfig := ExpandConstant('{app}\SessionClient.exe.config');

  { ── Write server / admin settings ──────────────────────────────────────── }
  if IsServerComponentSelected() then
  begin
    { Connection string: replace SQLEXPRESS instance name }
    if FileExists(ServerConfig) then
    begin
      var Content: String;
      if LoadStringFromFile(ServerConfig, Content) then
      begin
        StringChangeEx(Content, 'localhost\SQLEXPRESS',
                       SqlInstancePage.Values[0], False);
        StringChangeEx(Content, 'value="8001"',
                       'value="' + ServerPortPage.Values[0] + '"', False);
        SaveStringToFile(ServerConfig, Content, False);
      end;
    end;

    Pin := AdminPinPageSrv.Values[0];
    if (Pin <> '') and FileExists(AdminConfig) then
      SetIniValue(AdminConfig, 'appSettings', 'AdminSettingsPin', Pin);
  end;

  { ── Write client settings ───────────────────────────────────────────────── }
  if IsClientComponentSelected() and FileExists(ClientConfig) then
  begin
    SetIniValue(ClientConfig, 'appSettings', 'ServerAddress',
                ServerAddrPage.Values[0]);
    SetIniValue(ClientConfig, 'appSettings', 'ServerPort',
                ServerPortPage.Values[0]);
    SetIniValue(ClientConfig, 'appSettings', 'ClientMachineName',
                MachineNamePage.Values[0]);
    SetIniValue(ClientConfig, 'appSettings', 'ClientLocation',
                LocationPage.Values[0]);

    Pin := AdminPinPageCli.Values[0];
    if Pin <> '' then
      SetIniValue(ClientConfig, 'appSettings', 'AdminSettingsPin', Pin);
  end;
end;
