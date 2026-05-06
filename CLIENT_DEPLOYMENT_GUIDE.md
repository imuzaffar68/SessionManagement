# Client PC Deployment Guide — SessionManagement

Step-by-step instructions to set up a kiosk (client) PC from scratch.
Follow sections in order. Repeat from **Step 3** for every additional client PC.

**Prerequisites:** `SessionServer` is already running on the server PC.
See `SERVER_SETUP_GUIDE.md` if the server is not set up yet.

---

## Step 1 — Requirements

| Requirement | Minimum | Notes |
|---|---|---|
| OS | Windows 10 (64-bit) | Windows 11 also supported |
| .NET Framework | 4.7.2 | Built into Windows 10 1803+; check with `winver` |
| RAM | 2 GB | 4 GB recommended |
| Webcam | Optional | Required for login image capture (FR-05) |
| Network | Wired LAN | WiFi works but wired is more stable for heartbeat |

To verify .NET 4.7.2 is installed:
```
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
```
Value `461808` or higher = 4.7.2+ ✓. If missing, download the offline installer from microsoft.com.

---

## Step 2 — Create the KioskUser Account

Running the kiosk app under a **Standard User** account is the single most
effective OS-level protection. It prevents the kiosk user from:
- Uninstalling the app
- Editing config files in the install folder
- Disabling network adapters system-wide
- Installing other software

### 2.1 Create the account

Open an **elevated** (Run as Administrator) Command Prompt:

```bat
net user KioskUser /add
net user KioskUser ""
```

> The empty password means the account has no password — auto-login works without
> prompting. Set a strong password only if you need to prevent physical access to
> the machine's login screen.

### 2.2 Ensure the account is a Standard User (NOT Admin)

```bat
net localgroup Administrators KioskUser /delete
```

If the command says "The member specified was not found" that is fine — it means
the account was never in the Administrators group.

### 2.3 Set auto-login as KioskUser on boot

Run `netplwiz` → select **KioskUser** → uncheck **"Users must enter a user name
and password to use this computer"** → click OK → enter the KioskUser password
(blank if you left it empty above).

> After this, the PC boots directly into the KioskUser desktop with no login prompt.

---

## Step 3 — Install SessionClient

### Option A — Inno Setup installer (recommended for production)

1. Copy `SessionManagement-Setup.exe` to the client PC.
2. Right-click → **Run as administrator**.
3. Select profile: **Client PC**.
4. Fill in the wizard pages:
   | Page | Value | Notes |
   |---|---|---|
   | Server Address | `192.168.x.x` | LAN IP of the server PC |
   | Server Port | `8001` | Only change if server port was changed |
   | Machine Name | `Computer 01` | Shown in admin Clients tab |
   | Location / Seat | `Row A – Seat 1` | Physical location of this PC |
   | IT Admin PIN | `****` | Must match PIN set during server install |
5. Click **Install**. The installer will:
   - Copy files to `C:\ICSSMS\SessionClient\`
   - Write all wizard values to `SessionClient.exe.config`
   - Add SessionClient to the KioskUser startup folder

### Option B — Manual install (development / testing)

1. Copy the `SessionClient\bin\Release\` folder to `C:\ICSSMS\SessionClient\`
2. Open `C:\ICSSMS\SessionClient\SessionClient.exe.config` in Notepad as Administrator
3. Set the following values:
   ```xml
   <add key="ServerAddress"     value="192.168.x.x"/>
   <add key="ServerPort"        value="8001"/>
   <add key="ClientMachineName" value="Computer 01"/>
   <add key="ClientLocation"    value="Row A – Seat 1"/>
   <add key="AdminSettingsPin"  value="1234"/>
   <add key="EnableKioskMode"   value="true"/>
   ```

---

## Step 4 — Add SessionClient to KioskUser Startup

Skip this step if you used the Inno Setup installer (it does this automatically).

1. Open File Explorer and navigate to:
   ```
   C:\Users\KioskUser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup
   ```
2. Create a shortcut to `C:\ICSSMS\SessionClient\SessionClient.exe`
3. Name the shortcut `SessionClient`

**Test:** Log off your admin account → log in as KioskUser → SessionClient should
start automatically and show the splash screen.

---

## Step 5 — Change the Default Admin PIN

> **This is mandatory.** The default PIN `1234` must be changed before the café
> opens to customers.

The PIN protects the hidden IT settings dialog (`Ctrl+Alt+Shift+S`) and the
kiosk close shortcut (`Ctrl+Alt+Shift+Q`).

**Recommended:** use the same PIN on every client PC and the admin PC so you
only need to remember one code.

### How to change the PIN

Edit `C:\ICSSMS\SessionClient\SessionClient.exe.config` (requires Windows admin
account — KioskUser cannot modify this file):

```xml
<add key="AdminSettingsPin" value="YOUR_NEW_PIN"/>
```

Or use the Inno Setup installer which sets it via a wizard page at install time.

---

## Step 6 — Verify Kiosk Mode is Enabled

Open `SessionClient.exe.config` and confirm:

```xml
<add key="EnableKioskMode" value="true"/>
```

With kiosk mode enabled the app will:
- Run fullscreen with no title bar
- Block `Alt+F4`, `Win`, `Alt+Tab`, `Ctrl+Shift+Esc` during the login screen
- Prevent the window from being closed without IT admin PIN

> Set to `false` only on development machines or trusted staff PCs.

---

## Step 7 — Verify the Full Setup

Run this checklist after setting up each client PC:

- [ ] PC boots directly into KioskUser desktop (no login prompt)
- [ ] SessionClient starts automatically on boot
- [ ] Splash screen connects to server (green dot appears)
- [ ] Login panel shows in fullscreen kiosk mode
- [ ] `Win` key is blocked on the login screen (try pressing it)
- [ ] `Alt+F4` is blocked on the login screen
- [ ] A registered user can log in and start a session
- [ ] Session appears in SessionAdmin → Active Sessions tab
- [ ] `Ctrl+Alt+Shift+S` opens PIN dialog then settings dialog
- [ ] `Ctrl+Alt+Shift+Q` opens PIN dialog then closes app gracefully
- [ ] Webcam captures image at login (check SessionAdmin → Active Sessions → image column)

---

## Step 8 — IT Admin Shortcuts Reference

| Shortcut | Works on | Action | PIN required |
|---|---|---|---|
| `Ctrl+Alt+Shift+S` | Splash screen | Change server IP/port → auto-restart | Yes |
| `Ctrl+Alt+Shift+S` | Main window | Change server IP/port → auto-restart | Yes |
| `Ctrl+Alt+Shift+Q` | Main window (kiosk) | Graceful close (ends session + billing) | Yes |
| `✕` button | Splash screen | Close app (pre-login, no session running) | No |

---

## Step 9 — Post-Deployment Changes (No Reinstall Needed)

| Change needed | How to do it |
|---|---|
| Server IP changed | Press `Ctrl+Alt+Shift+S` → enter PIN → type new IP → Save & Restart |
| Rename this machine | SessionAdmin → Clients tab → Edit button |
| Change machine location | SessionAdmin → Clients tab → Edit button |
| Change IT admin PIN | Edit `SessionClient.exe.config` as Windows admin |
| Disable this machine temporarily | SessionAdmin → Clients tab → toggle IsActive |

---

## Troubleshooting

| Problem | Likely cause | Fix |
|---|---|---|
| Splash shows "Connection failed" | Wrong `ServerAddress` or server not running | `Ctrl+Alt+Shift+S` → update IP; verify server is running |
| App does not start on boot | Shortcut missing from KioskUser Startup folder | Add shortcut (Step 4) |
| Kiosk mode not fullscreen | `EnableKioskMode=false` | Set to `true` in config |
| `Ctrl+Alt+Shift+S` does nothing | Window does not have keyboard focus | Click on the app window first |
| PIN dialog appears but correct PIN rejected | `AdminSettingsPin` mismatch | Check config value; ensure no leading/trailing spaces |
| Webcam not capturing | No webcam attached or driver missing | Install webcam driver; `EnableImageCapture=false` to disable |
| Session not appearing in admin | Client registered with wrong `ClientCode` | Check `ClientCode` in config; should be `CL001` sentinel |

