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

## Summary

| # | Task | Type | Effort | Dependency |
|---|---|---|---|---|
| 1 | Inno Setup ServerAddress page | Installer script | Low | None |
| 2 | Hidden admin settings dialog | C# + XAML | Medium | None |
| 3 | Keyboard hook extension | C# | Low | None |
| 4 | Standard User account docs | Deployment guide | Zero | None |

Tasks 1, 3, and 4 are independent and can be done in any order.  
Task 2 (settings dialog) pairs well with Task 1 — implement together if the
installer approach is not sufficient on its own.
