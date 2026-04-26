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

**Status:** ✅ ALREADY DONE — close this task  
**Effort:** Zero

### Finding (from code review)
After reading `SessionClient/MainWindow.xaml.cs:147–166`, all listed shortcuts
are already blocked:

```csharp
if (vk == VK_LWIN || vk == VK_RWIN) return (IntPtr)1;  // blocks Win key
// → Win+R, Win+E, Win+D are all blocked because Win key itself is swallowed
if (ctrl && shift && vk == VK_ESCAPE) return (IntPtr)1; // Ctrl+Shift+Esc blocked
if (alt  && vk == VK_F4)             return (IntPtr)1;  // Alt+F4 blocked
if (alt  && vk == VK_TAB)            return (IntPtr)1;  // Alt+Tab blocked
```

The hook only activates during `!_sessionActive` (login/idle screen). During an
active session the hook passes everything through — **this is intentional**: the
paying user gets full computer access. This is correct cyber café behavior.

### Note on Ctrl+Alt+Delete
Cannot be intercepted by a user-mode hook. Accepted as a known gap.
Mitigation: Standard User account (Task 4) reduces the damage even if pressed.

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

---

## 6. B-1 — Currency Display Hardcoded as PKR

**Status:** Not started  
**Effort:** Low (2–3 lines, C# only)  
**Priority:** Medium — functional bug visible to users

### Problem
`App.config` has `BillingCurrency=USD` but the UI hardcodes `"PKR"` in two places:

- `SessionClient/MainWindow.xaml.cs:1177` → `lblSummaryAmount.Text = $"PKR {amount:F2}"`
- `SessionClient/MainWindow.xaml.cs:1054` → `lblCurrentBilling.Text = $"${amount:F2}"`

The config key `BillingCurrency` is never read for display purposes.

### Fix
Read the currency symbol from config at startup and use it in both labels:
```csharp
string _currency = ConfigurationManager.AppSettings["BillingCurrency"] ?? "PKR";
// then:
lblSummaryAmount.Text   = $"{_currency} {amount:F2}";
lblCurrentBilling.Text  = $"{_currency} {amount:F2}";
```

Also update `App.config` default value from `USD` to `PKR` to match the project's
actual target market (Pakistan).

---

## 7. B-2 — MinSessionDuration Default Mismatch

**Status:** Not started  
**Effort:** Trivial (1 line)  
**Priority:** Low

### Problem
`SessionClient/MainWindow.xaml.cs:1486` defaults `mn = 15` in code:
```csharp
int mn = 15, mx = 480;
```
But `App.config` has `MinSessionDuration=1`. If the config key is ever missing,
the minimum silently jumps from 1 to 15 minutes.

### Fix
Change the in-code default to match config:
```csharp
int mn = 1, mx = 480;
```

---

## 8. B-3 — ValidateSession Is a Stub (Dead Code)

**Status:** Not started  
**Effort:** Low  
**Priority:** Low

### Problem
`SessionManagement.Shared/WCF/SessionService.cs:211`:
```csharp
public bool ValidateSession(string sessionToken)
    => !string.IsNullOrWhiteSpace(sessionToken);
```
This accepts any non-empty string as valid. The session token returned by
`AuthenticateUser` is generated but never stored server-side, so real validation
is impossible. The client code does not call `ValidateSession` at all — the token
is ignored after login.

### Fix options (choose one)
1. **Remove it** — delete `ValidateSession` from the interface and implementation.
   Cleanest option since the LAN-only threat model does not require token auth.
2. **Implement it properly** — store tokens in a `ConcurrentDictionary<string, int>`
   (token → userId) and validate on each sensitive call. Significant effort.

**Recommended:** Option 1 — remove. Add a comment to the interface explaining
that WCF channel security + BCrypt password auth is sufficient for a LAN system.

---

## 9. B-4 — Server Broadcast Messages Are Invisible

**Status:** Not started  
**Effort:** Low (XAML label + 2 lines C#)  
**Priority:** Low

### Problem
`OnServerMessage` in both `SessionClient/MainWindow.xaml.cs` and
`SessionAdmin/MainWindow.xaml.cs` only calls `Debug.WriteLine`. Any message the
server broadcasts via `Broadcast(cb => cb.OnServerMessage(...))` — including the
repeated-login-failure alert — is invisible to the end user and the admin.

### Fix — SessionAdmin
Wire `OnServerMessage` to a status bar or toast notification:
```csharp
private void OnServerMessage(object sender, ServerMessageEventArgs e)
{
    Dispatcher.BeginInvoke(new Action(() =>
        ToastHelper.Show(ToastHelper.AdminAppId, "Server Notice", e.Message)));
}
```

### Fix — SessionClient
Same pattern — show as a toast so the user sees admin messages even when
the session panel is visible.

---

## 10. D-1 — SQL Script Drops All Data (Production Risk)

**Status:** Not started  
**Effort:** Medium (SQL script change)  
**Priority:** Medium

### Problem
`SessionManagement.sql` drops and recreates every table unconditionally:
```sql
IF OBJECT_ID('dbo.tblUser', 'U') IS NOT NULL DROP TABLE dbo.tblUser;
```
Running the script on a live café database destroys all session history,
billing records, and user accounts.

### Fix
Add a data-presence guard before each DROP so re-running the script on an
existing database is safe:
```sql
-- Only drop if empty (first-time setup)
IF OBJECT_ID('dbo.tblSession', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.tblSession)
   DROP TABLE dbo.tblSession;
```
Or better: split into two scripts:
- `SessionManagement_Setup.sql` — initial creation (safe to run once)
- `SessionManagement_Reset.sql` — drops everything (dev/test only, clearly named)

---

## 11. D-2 — No Startup Config Validation

**Status:** Not started  
**Effort:** Low (~15 lines, C# in Program.cs)  
**Priority:** Medium

### Problem
`SessionServer/Program.cs` does not validate the connection string at startup.
If `App.config` has a wrong SQL Server instance name the server starts, prints
"SessionService is running", and then crashes on the first client connection
attempt with an unhelpful `SqlException`.

### Fix
Add a DB connectivity test in `Program.cs` before `ServiceHost.Open()`:
```csharp
var db = new DatabaseHelper();
if (!db.TestConnection())
{
    Console.WriteLine("ERROR: Cannot connect to database. Check App.config connection string.");
    Console.WriteLine("Press Enter to exit.");
    Console.ReadLine();
    return;
}
```
`DatabaseHelper.TestConnection()` already exists — just needs to be called.

---

## 12. D-3 — Server Kept Alive by Console.ReadLine() (Fragile)

**Status:** Not started  
**Effort:** Trivial (1 line change)  
**Priority:** Medium

### Problem
`SessionServer/Program.cs` uses `Console.ReadLine()` to keep the process alive.
Two failure modes:
1. Someone accidentally presses Enter in the console → entire café goes offline.
2. Running as a Windows Service or scheduled task → console is not attached,
   `ReadLine()` returns `null` immediately → server exits silently.

### Fix
Replace `Console.ReadLine()` with an infinite sleep that still allows Ctrl+C:
```csharp
Console.WriteLine("SessionService is running. Press Ctrl+C to stop.");
var exit = new System.Threading.ManualResetEventSlim(false);
Console.CancelKeyPress += (s, e) => { e.Cancel = true; exit.Set(); };
exit.Wait();
```
This blocks until Ctrl+C is pressed — immune to accidental Enter, and works
correctly when no console is attached (the process simply waits).

---

## 13. D-4 — Session Images Accumulate Without Cleanup

**Status:** Not started  
**Effort:** Low  
**Priority:** Low

### Problem
Login images are saved to `%PROGRAMDATA%\SessionManagement\Images` and never
deleted. A café running for months with webcam enabled will gradually fill the
server's disk.

### Fix
Add a cleanup sweep in `SessionService` constructor (or as a daily timer) that
deletes image files older than N days:
```csharp
// Delete images older than 90 days
foreach (var f in Directory.GetFiles(_imgPath, "*.jpg"))
    if (File.GetCreationTime(f) < DateTime.Now.AddDays(-90))
        File.Delete(f);
```
Make the retention period configurable via `App.config`:
```xml
<add key="ImageRetentionDays" value="90"/>
```

---

## 14. D-5 — tblSystemLog Grows Unbounded

**Status:** Not started  
**Effort:** Low (one SQL stored procedure)  
**Priority:** Low

### Problem
`tblSystemLog` is written on every auth, session start/end, and security event.
There is no archiving or purge job. The table will grow indefinitely.

### Fix
Add a stored procedure `sp_PurgeOldLogs` and call it on server startup:
```sql
CREATE PROCEDURE dbo.sp_PurgeOldLogs @RetentionDays INT = 180
AS
BEGIN
    DELETE FROM dbo.tblSystemLog
    WHERE LoggedAt < DATEADD(DAY, -@RetentionDays, GETDATE());
END
```
Call from `SessionService` constructor after DB startup log:
```csharp
_db.PurgeOldLogs(retentionDays: 180);
```

---

## Summary

| # | Task | Type | Priority | Effort | Status |
|---|---|---|---|---|---|
| 1 | Inno Setup ServerAddress page | Installer | — | Low | Superseded by Task 5 |
| 2 | Hidden admin settings dialog | C# + XAML | Low | Medium | Not started |
| 3 | Keyboard hook extension | C# | — | — | ✅ Already done |
| 4 | Standard User account docs | Deployment | Low | Zero | Not started |
| 5 | Hybrid Inno Setup installer | Inno Setup | Low | High | Not started |
| 6 | B-1 Currency hardcoded PKR | C# bug fix | Medium | Low | ✅ Done |
| 7 | B-2 MinSessionDuration default | C# bug fix | Low | Trivial | ✅ Done |
| 8 | B-3 ValidateSession stub | C# cleanup | Low | Low | ✅ Done |
| 9 | B-4 Server broadcast invisible | C# + XAML | Low | Low | ✅ Done |
| 10 | D-1 SQL script drops live data | SQL | Medium | Medium | ✅ Done |
| 11 | D-2 No startup config validation | C# | Medium | Low | ✅ Done |
| 12 | D-3 Console.ReadLine() fragile | C# | Medium | Trivial | ✅ Done |
| 13 | D-4 Image files accumulate | C# | Low | Low | ✅ Done |
| 14 | D-5 tblSystemLog unbounded | SQL | Low | Low | ✅ Done |

---

## 15. Code Regions — DatabaseHelper / SessionService / IllegalActivityDetectionService

**Status:** Not started  
**Effort:** Low (mechanical, no logic change)  
**Priority:** Low — IDE quality / maintainability

### Problem
`SessionClient/MainWindow.xaml.cs` and `SessionAdmin/MainWindow.xaml.cs` use
proper C# `#region` / `#endregion` directives — sections collapse in Visual
Studio. The three files below use `═══════` comment separators only:

| File | Current structure | Missing |
|---|---|---|
| `SessionManagement.Shared/Data/DatabaseHelper.cs` | `// ═══ UC-01 ═══` comments | `#region` |
| `SessionManagement.Shared/WCF/SessionService.cs` | `// ═══ UC-01 ═══` comments | `#region` |
| `SessionManagement.Shared/Security/IllegalActivityDetectionService.cs` | `// ═════` comments | `#region` |

### Fix
Replace each `// ═══ SECTION ═══` block header with:
```csharp
#region UC-01 / UC-09 — Authentication
// ... code ...
#endregion
```
No logic changes — purely structural.

---

## 16. DatabaseHelper — Split / Inefficient DB Calls

**Status:** Not started  
**Effort:** Medium (SQL + C# changes)  
**Priority:** Medium — correctness and performance

### Problem A — `RegisterOrUpdateClient` makes 2 round-trips

`DatabaseHelper.cs:697–718`: calls `sp_RegisterClient` SP then immediately calls
`GetClientMachineIdByCode` in a second DB round-trip to return the machine ID.

```csharp
cmd.ExecuteScalar();                          // round-trip 1: register
return GetClientMachineIdByCode(clientCode);  // round-trip 2: fetch the ID
```

**Fix:** Add `@ClientMachineId INT OUTPUT` to `sp_RegisterClient` and return it
directly. One round-trip instead of two:
```csharp
var outParam = new SqlParameter("@ClientMachineId", SqlDbType.Int)
    { Direction = ParameterDirection.Output };
cmd.Parameters.Add(outParam);
cmd.ExecuteNonQuery();
return outParam.Value != DBNull.Value ? Convert.ToInt32(outParam.Value) : 0;
```

### Problem B — `AutoExpireOverdueSessionsWithIds` loops N individual UPDATEs

`DatabaseHelper.cs:250–288`: SELECTs all overdue session IDs, then runs a
separate `UPDATE` for each one in a `foreach` loop — N database round-trips.

**Fix:** Replace with a single `UPDATE ... OUTPUT INSERTED.SessionId`:
```sql
UPDATE dbo.tblSession
SET    Status = 'Expired', EndedAt = GETDATE(),
       ActualDurationMinutes = SelectedDurationMinutes,
       TerminationReason = 'AutoExpiry'
OUTPUT INSERTED.SessionId
WHERE  Status = 'Active' AND ExpectedEndAt < GETDATE()
```
Read the `OUTPUT` rows into a list — one round-trip returns both the updated
count and the IDs.

### Problem C — `MarkStaleClientsOffline` has no transaction between stepA and stepB

`DatabaseHelper.cs:839–876`: Two commands (increment `MissedHeartbeats`, then
mark `Offline`) run on the same connection but without a `BEGIN TRANSACTION`.
If the process crashes between them, heartbeat counters are incremented but
machines are never marked offline — they accumulate stale counters indefinitely.

**Fix:** Wrap both commands in an explicit transaction:
```csharp
c.Open();
using (var tx = c.BeginTransaction())
{
    // stepA — increment miss counters
    using (var cmdA = new SqlCommand(stepA, c, tx)) { ... }
    // stepB — mark offline + return rows
    using (var cmdB = new SqlCommand(stepB, c, tx)) { ... }
    tx.Commit();
}
```

---

## 17. XAML File Organization — SessionAdmin Dialogs

**Status:** Decision made — no action needed  
**Effort:** N/A

### Decision: Keep files flat (do not create subfolders)

**SessionClient** — 4 windows (MainWindow, SplashWindow, FloatingTimerWindow,
App). Flat is appropriate at this scale.

**SessionAdmin** — 9 windows. The 6 dialog windows
(`AppDialogWindow`, `BillingRateFormWindow`, `EditMachineWindow`,
`EditUserWindow`, `ResetPasswordWindow`, `UserFormWindow`) could logically go
into a `Views\Dialogs\` subfolder. However:

- Moving XAML files in WPF requires updating **every** `x:Class` attribute and
  the `.csproj` `<Compile>` / `<Page>` entries.
- The default WPF namespace (`namespace SessionAdmin`) would need to become
  `namespace SessionAdmin.Dialogs` — a breaking change that cascades into every
  `new EditMachineWindow()` call site in `MainWindow.xaml.cs`.
- For a 9-file project the flat layout is still manageable in Solution Explorer.

**Conclusion:** The complexity and risk of moving files outweigh the benefit.
Leave flat. Revisit only if the file count grows significantly (15+).

---

## Summary

| # | Task | Type | Priority | Effort | Status |
|---|---|---|---|---|---|
| 1 | Inno Setup ServerAddress page | Installer | — | Low | Superseded by Task 5 |
| 2 | Hidden admin settings dialog | C# + XAML | Low | Medium | Not started |
| 3 | Keyboard hook extension | C# | — | — | ✅ Already done |
| 4 | Standard User account docs | Deployment | Low | Zero | Not started |
| 5 | Hybrid Inno Setup installer | Inno Setup | Low | High | Not started |
| 6 | B-1 Currency hardcoded PKR | C# bug fix | Medium | Low | ✅ Done |
| 7 | B-2 MinSessionDuration default | C# bug fix | Low | Trivial | ✅ Done |
| 8 | B-3 ValidateSession stub | C# cleanup | Low | Low | ✅ Done |
| 9 | B-4 Server broadcast invisible | C# + XAML | Low | Low | ✅ Done |
| 10 | D-1 SQL script drops live data | SQL | Medium | Medium | ✅ Done |
| 11 | D-2 No startup config validation | C# | Medium | Low | ✅ Done |
| 12 | D-3 Console.ReadLine() fragile | C# | Medium | Trivial | ✅ Done |
| 13 | D-4 Image files accumulate | C# | Low | Low | ✅ Done |
| 14 | D-5 tblSystemLog unbounded | SQL | Low | Low | ✅ Done |
| 15 | Regions in shared files | C# style | Low | Low | Not started |
| 16 | DatabaseHelper split DB calls | C# + SQL | Medium | Medium | ✅ Done |
| 17 | XAML folder organization | Architecture | — | — | ✅ Decision: keep flat |

### Recommended order
1. **Task 12** — D-3 server keep-alive (1 line, prevents accidental shutdown)
2. **Task 11** — D-2 config validation (clear error on misconfigured DB)
3. **Task 6** — B-1 currency fix (visible to users)
4. **Task 16** — DatabaseHelper split DB calls (correctness)
5. **Task 10** — D-1 SQL script safety (prevents data loss on reinstall)
6. **Tasks 7–9, 13–15** — low priority cleanup
7. **Tasks 2, 4, 5** — deployment / installer work (do last, after app is stable)
