# Project Gaps — Intelligent Client-Server Session Management System
## Final Year Project Review
*Context: Academic demonstration project. Gaps rated against project requirements.*

---

## 🔴 Must Fix

- **[SECURITY]** As a café owner, I need all WCF traffic between client PCs and the
  server to be encrypted so that an attacker on the same LAN cannot capture session
  tokens, passwords, or billing amounts using a packet sniffer like Wireshark.
  - `SessionClient/App.config` and `SessionAdmin/App.config` both have
    `<security mode="None"/>` — fix to `Transport` to match the server's Release build.

- **[LOGGING]** As an IT admin, I need the server to write logs to a local file when
  the database is unavailable so that authentication attempts, billing events, and
  security alerts are never silently lost during a DB outage.
  - `EnableLogging` and `LogPath` keys exist in `App.config` but are never read in
    code — either implement file logging or remove the dead keys.

- **[DATA SAFETY]** As a café owner, I need a minimum log retention floor enforced in
  code so that a misconfigured `LogRetentionDays=1` cannot permanently wipe the entire
  audit trail on the next server restart.
  - `sp_PurgeOldLogs` has no minimum guard — add
    `IF @RetentionDays < 30 SET @RetentionDays = 30` at the top of the stored procedure.

- **[RELIABILITY]** As a café owner, I need the server's session expiry and offline
  detection timers to be properly stopped and disposed when the server shuts down so
  that resources are not leaked on every restart.
  - `ServiceHost.Close()` does not call `SessionService.Dispose()` — timers
    `_sessionExpiryTimer` and `_clientOfflineTimer` are never stopped.

- **[DEPLOYMENT]** As an IT admin, I need a working Inno Setup `.iss` installer script
  in the repository so that client and server PCs can be deployed consistently without
  manual config file editing.
  - `PENDING_TASKS.md` Task 5 documents the full structure — script has not been
    written yet.

---

## 🔵 Optional (Extra polish / viva talking points)

- **[SECURITY]** Store `AdminSettingsPin` as a BCrypt hash instead of plaintext —
  `AuthenticationHelper.HashPassword()` already exists.

- **[SECURITY]** Enforce KioskUser file-system ACL so config file is unreadable —
  `icacls C:\NetCafe\SessionClient\SessionClient.exe.config /deny KioskUser:(R)`.

- **[CODE QUALITY]** Replace `goto TryCapture` in `CaptureImageAsync()` with a
  `while` loop — only `goto` in the codebase.

- **[CODE QUALITY]** Wrap `SqlDataAdapter` in `using` blocks in `DatabaseHelper.cs`
  — follows the same cleanup pattern as `SqlConnection` and `SqlCommand`.

- **[TESTING]** Execute and record results for the 48 test cases from the Design
  Document — provides evidence for the examiner during viva.

- **[OBSERVABILITY]** Use meaningful Git commit messages (`fix:`, `feat:`) so the
  examiner reviewing commit history sees clear development progression.

- **[SCALABILITY]** Add a brief documentation note on the theoretical machine limit
  (50–100 concurrent clients, single server) as a viva talking point.

- **[MONITORING]** Add a simple file-based fallback log for DB outages — even a plain
  `File.AppendAllText` prevents a complete audit blackout during demo.

---

## ✅ Already Well Handled (Mention in viva)

- BCrypt password hashing with work factor 12
- One-shot session token with atomic `TryRemove` — prevents replay attacks
- Orphan session recovery — bills accurately using last heartbeat timestamp
- WCF duplex callbacks for real-time server→client push
- `#if DEBUG / #else` guards for exception detail and security mode
- Idempotent SQL setup script (`SessionManagement_Setup.sql`)
- Image and log retention with configurable cleanup on server startup
- Kiosk keyboard hook whitelisting admin shortcuts before block checks
- `ConcurrentDictionary` used correctly for all shared server-side state
- Comprehensive documentation: `SERVER_SETUP_GUIDE.md`, `CLIENT_DEPLOYMENT_GUIDE.md`

---

## Summary

| Priority | Count |
|---|---|
| 🔴 Must Fix | 5 |
| 🔵 Optional | 8 |
| ✅ Already Done | 10+ |
