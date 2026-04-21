# Pending Tasks — SessionManagement

Items listed here are planned but not yet implemented. Touch this file when you
pick up a task, mark it done, and add implementation notes below the checklist.

---

## 1. Inno Setup — ServerAddress Wizard Page

**Status:** Not started  
**Effort:** Low (Inno Setup script change only, no C# code)

### What to do
Add a third wizard input page to the Inno Setup installer so the IT admin types
the server IP address once during installation. The installer writes it directly
to `SessionClient.exe.config` — no manual file editing after deployment.

### Code snippet (add alongside existing ClientMachineName / ClientLocation writes)
```pascal
// In the [Code] section of your .iss file
// DO NOT write ClientCode — leave the sentinel "CL001"
SetIniValue(ExpandConstant('{app}\SessionClient.exe.config'),
            'appSettings', 'ServerAddress',
            ServerAddressPage.Values[0]);
```

### Wizard page order
1. ClientMachineName — "Machine Name" (already exists)
2. ClientLocation    — "Location / Seat" (already exists)
3. **ServerAddress  — "Server IP Address"** ← add this page

### Notes
- Default value shown in the input box: `192.168.1.1`
- Add a label: *"Enter the LAN IP address of the PC running SessionServer."*
- Validation: reject empty string; optionally validate IPv4 format.

---

## 2. Hidden Admin Settings Dialog (Ctrl+Alt+Shift+S)

**Status:** Not started  
**Effort:** Medium (~1–2 hours, C# + XAML)

### What to do
Add a secret keyboard shortcut inside `SessionClient` that opens a restricted
settings dialog — visible only to the IT person standing at the machine, never
to end users. Useful when the server IP changes after deployment without
reinstalling the app.

### Shortcut
`Ctrl + Alt + Shift + S`

### Dialog contents (minimum)
| Field | AppSettings key | Notes |
|---|---|---|
| Server Address | `ServerAddress` | IP or hostname of SessionServer |
| Server Port | `ServerPort` | Default 8001 |

### Save mechanism
Use `ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)`
to write back to `SessionClient.exe.config` at runtime.

> **Important:** This requires the app to be running as a user with write
> permission to the install folder. Under a Standard User account with the app
> installed in `Program Files`, a UAC prompt or a separate admin-launch flow
> will be needed. Simplest workaround: install to a non-protected folder
> (e.g. `C:\NetCafe\SessionClient`) and grant the kiosk user write access to
> that folder only.

### Where to add the hook
`SessionClient/MainWindow.xaml.cs` — in the existing `KeyDown` or low-level
keyboard hook handler, check for the `Ctrl+Alt+Shift+S` combination and open
the settings `Window`.

---

## 3. Kiosk Keyboard Hook Extension

**Status:** Not started  
**Effort:** Low (~20 lines, C# only)

### What to do
Extend the existing low-level keyboard hook in `SessionClient/MainWindow.xaml.cs`
to block the shortcuts listed below. The hook already blocks `Alt+F4`; these
additions follow the same pattern. All blocks are **active only when
`EnableKioskMode=true`**.

### Shortcuts to block
| Shortcut | Opens | Risk |
|---|---|---|
| `Win` key | Start menu | Access to all apps |
| `Win + R` | Run dialog | Launch any executable |
| `Win + E` | File Explorer | Browse and edit files |
| `Win + D` | Show Desktop | Escape the kiosk window |
| `Ctrl + Shift + Esc` | Task Manager | Kill the client process |

### Notes
- `Ctrl+Alt+Delete` cannot be intercepted by a user-mode hook — block it via
  Group Policy (`DisableLockWorkstation` + `DisableTaskMgr` registry keys) or
  accept it as a known gap for the current project scope.
- The secret admin shortcut (`Ctrl+Alt+Shift+S` from Task 2) must be
  **whitelisted** in this hook so it still fires in kiosk mode.

---

## 4. Standard User Account — Deployment Step (No Code Required)

**Status:** Documentation only — add to deployment guide when writing it  
**Effort:** Zero (Windows setup, not code)

### What to do
Run the SessionClient kiosk under a non-admin Windows user account. This single
OS-level step blocks the most damaging user actions with no code changes:

| Blocked action | Why it's blocked |
|---|---|
| Uninstall the app | Requires admin rights |
| Edit config in `Program Files` | Requires admin rights |
| Disable network adapter (system-wide) | Requires admin rights |
| Install new software | Requires admin rights |

### Setup steps (document in deployment guide)
1. On each client PC, create a Windows user account named e.g. `KioskUser`
   — type: **Standard User** (not Administrator).
2. Set Windows to auto-login as `KioskUser` on boot
   (`netplwiz` → uncheck "Users must enter a username and password").
3. Add `SessionClient.exe` to the Startup folder for `KioskUser`.
4. Optionally set the desktop wallpaper and remove all desktop icons for
   `KioskUser` so the kiosk window is the only thing visible.

---

## 5. Hybrid Inno Setup Installer

**Status:** Not started  
**Effort:** High (~3–4 hours, Inno Setup Pascal script)

### Concept
One `.exe` installer for the entire product. The user picks a **preset profile**
on the Components page and the wizard shows only the pages relevant to that
profile. No confusion about which installer file goes on which PC.

### Preset profiles

| Profile | Installs | Target PC |
|---|---|---|
| **Server PC** | SessionServer + SessionAdmin | Owner / manager machine |
| **Admin Only** | SessionAdmin only | Separate admin laptop (optional) |
| **Client PC** | SessionClient only | Every kiosk seat |
| **Full** | Everything | Development / testing |

### Installer file structure (`.iss`)

```
[Setup]
AppName=NetCafe Session Management
AppVersion=1.0
DefaultDirName=C:\NetCafe\SessionManagement
DefaultGroupName=NetCafe

[Types]
Name: server;  Description: "Server PC  (Server + Admin)"
Name: admin;   Description: "Admin Only (remote admin laptop)"
Name: client;  Description: "Client PC  (Kiosk)"
Name: full;    Description: "Full Install (development)"

[Components]
Name: server_svc;  Description: "Session Server (WCF Service)";  Types: server full
Name: admin_ui;    Description: "Session Admin (Management UI)"; Types: server admin full
Name: client_ui;   Description: "Session Client (Kiosk)";        Types: client full

[Files]
; ── Shared DLLs (all components) ──────────────────────────────────────
Source: "bin\Release\SessionManagement.Shared.dll";     DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\AForge.dll";                       DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\AForge.Video.dll";                 DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\AForge.Video.DirectShow.dll";      DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\BCrypt.Net-Next.dll";              DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\System.Buffers.dll";               DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\System.Memory.dll";                DestDir: "{app}"; Components: server_svc admin_ui client_ui
Source: "bin\Release\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Components: server_svc admin_ui client_ui

; ── Server-only DLL ────────────────────────────────────────────────────
Source: "bin\Release\System.Numerics.Vectors.dll";      DestDir: "{app}"; Components: server_svc

; ── Admin + Client DLL ─────────────────────────────────────────────────
Source: "bin\Release\System.Configuration.ConfigurationManager.dll"; DestDir: "{app}"; Components: admin_ui client_ui

; ── Executables ────────────────────────────────────────────────────────
Source: "SessionServer\bin\Release\SessionServer.exe";          DestDir: "{app}"; Components: server_svc
Source: "SessionServer\bin\Release\SessionServer.exe.config";   DestDir: "{app}"; Components: server_svc
Source: "SessionAdmin\bin\Release\SessionAdmin.exe";            DestDir: "{app}"; Components: admin_ui
Source: "SessionAdmin\bin\Release\SessionAdmin.exe.config";     DestDir: "{app}"; Components: admin_ui
Source: "SessionClient\bin\Release\SessionClient.exe";          DestDir: "{app}"; Components: client_ui
Source: "SessionClient\bin\Release\SessionClient.exe.config";   DestDir: "{app}"; Components: client_ui

; ── SQL script (server only) ───────────────────────────────────────────
Source: "SessionManagement.sql"; DestDir: "{app}"; Components: server_svc

[Dirs]
; Server image/log folders
Name: "{commonappdata}\SessionManagement\Images";     Components: server_svc
Name: "{commonappdata}\SessionManagement\ProfilePics"; Components: server_svc
Name: "{commonappdata}\SessionManagement\Logs";        Components: server_svc
; Client image/log folders
Name: "{app}\Images"; Components: client_ui
Name: "{app}\Logs";   Components: client_ui

[Icons]
Name: "{group}\Session Admin";  Filename: "{app}\SessionAdmin.exe";  Components: admin_ui
Name: "{group}\Session Server"; Filename: "{app}\SessionServer.exe"; Components: server_svc
Name: "{group}\Session Client"; Filename: "{app}\SessionClient.exe"; Components: client_ui
```

### Wizard pages (conditional — [Code] Pascal section)

| Page | Shown when | Writes to |
|---|---|---|
| SQL Server instance name | `server_svc` selected | `SessionServer.exe.config` → `Data Source=` |
| Admin password (first-run) | `server_svc` selected | DB via sqlcmd seed script |
| Server IP address | `client_ui` selected | `SessionClient.exe.config` → `ServerAddress` |
| Machine Name | `client_ui` selected | `SessionClient.exe.config` → `ClientMachineName` |
| Location / Seat | `client_ui` selected | `SessionClient.exe.config` → `ClientLocation` |

### Post-install actions ([Run] section, conditional)

```ini
[Run]
; 1. Create database (server only)
Filename: "{sys}\cmd.exe";
Parameters: "/c sqlcmd -S ""{code:GetSqlInstance}"" -E -i ""{app}\SessionManagement.sql""";
StatusMsg: "Creating database...";
Components: server_svc;
Flags: runhidden

; 2. Open firewall port 8001 (server only)
Filename: "{sys}\netsh.exe";
Parameters: "advfirewall firewall add rule name=""SessionService"" dir=in action=allow protocol=TCP localport=8001";
StatusMsg: "Configuring firewall...";
Components: server_svc;
Flags: runhidden

; 3. Create scheduled task for auto-start (server only)
Filename: "{sys}\schtasks.exe";
Parameters: "/create /tn ""SessionServer"" /tr ""{app}\SessionServer.exe"" /sc onstart /ru SYSTEM /rl HIGHEST /f";
StatusMsg: "Registering auto-start...";
Components: server_svc;
Flags: runhidden
```

### Prerequisite checks ([Code] section)

```pascal
// .NET 4.7.2 check — registry key Release >= 461808
function IsDotNet472Installed: Boolean;
var value: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', value)
    and (value >= 461808);
end;

// SQL Server check — look for sqlcmd.exe on PATH or common install paths
function IsSqlServerInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe'))
         or FileExists('C:\Program Files\Microsoft SQL Server\110\Tools\Binn\sqlcmd.exe');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet472Installed then begin
    MsgBox('.NET Framework 4.7.2 or later is required.'#13#10 +
           'Download from: microsoft.com/download/dotnet-framework', mbError, MB_OK);
    Result := False; Exit;
  end;
  // SQL Server check is deferred to component selection — only fail if server component picked
end;
```

### Config file write helpers ([Code] section)

```pascal
// Replaces the Data Source value in SessionServer.exe.config
procedure WriteConnectionString(instance: String);
var path, content: String;
begin
  path := ExpandConstant('{app}\SessionServer.exe.config');
  LoadStringFromFile(path, content);
  // Replace whatever Data Source= value is there with the one the admin typed
  // (simple string replacement — works because the config has exactly one connectionString)
  StringChangeEx(content, 'localhost\SQLEXPRESS', instance, True);
  SaveStringToFile(path, content, False);
end;
```

### Notes
- Build in **Release** mode before running Inno Setup (`Ctrl+Shift+B` → Release).
- The `.iss` file goes in the solution root alongside `SessionManagement.sql`.
- sqlcmd.exe must be on the server PC's PATH. If not found, the DB step silently
  fails — add a post-install check that queries `sys.databases` to verify.
- Tasks 1 (ServerAddress page) and 2 (hidden settings dialog) are superseded by
  this installer — the wizard page covers the install-time need; the hidden dialog
  covers post-deployment changes.

---

## Summary

| # | Task | Type | Effort | Dependency |
|---|---|---|---|---|
| 1 | Inno Setup ServerAddress page | Installer script | Low | Superseded by Task 5 |
| 2 | Hidden admin settings dialog | C# + XAML | Medium | None |
| 3 | Keyboard hook extension | C# | Low | None |
| 4 | Standard User account docs | Deployment guide | Zero | None |
| 5 | Hybrid Inno Setup installer | Inno Setup `.iss` | High | Build in Release first |

Start with Task 3 (keyboard hook) — pure C#, no external tools needed.  
Task 5 (installer) is last — do it after the app is feature-complete and builds clean in Release.
