# Intelligent Client-Server Session Management System (ICSSMS)

**Developer:** Muzaffar Iqbal (BC240212887)
**Supervisor:** Asim Mehmood — Virtual University of Pakistan
**Course:** CS619 Final Year Project — Spring 2026

---

## What Is This?

A LAN-based desktop system for managing user sessions in shared computer environments
(internet cafes, kiosks, learning labs). It handles authentication, time tracking,
webcam identity capture, billing, illegal activity detection, and real-time admin monitoring.

Three applications work together over a local network:

| Application | Role |
|---|---|
| **SessionServer** | Background WCF service — hosts all business logic and database operations |
| **SessionAdmin** | Admin dashboard — monitoring, billing, alerts, user management, reports |
| **SessionClient** | Kiosk app — end-user login, session timer, webcam capture |

---

## Quick Start (First Time)

### Step 1 — Run the Installer

1. Run `SessionManagement-Setup.exe` as Administrator
2. Select your profile:
   - **Server PC** — on the machine that will run SessionServer and SessionAdmin
   - **Client PC** — on each kiosk machine (repeat for every seat)
   - **Admin Only** — on a separate admin laptop
   - **Full** — everything on one PC (for testing/demo)
3. Fill in the wizard pages and click Install

> The installer creates the database, opens the firewall port, and registers
> SessionServer as an auto-start scheduled task.

### Step 2 — Start SessionServer

Double-click `SessionServer.exe` (or it starts automatically on next boot).
Wait for: `Service listening on net.tcp://localhost:8001/SessionService`

### Step 3 — Open SessionAdmin

Log in with the default admin credentials:

| Field | Value |
|---|---|
| Username | `Admin` |
| Password | `Admin@123` |

> **Change this password immediately** — click the lock icon in the title bar.

### Step 4 — Open SessionClient (kiosk)

Log in with any of the pre-seeded test accounts:

| Username | Password | Full Name |
|---|---|---|
| `sukaina` | `Sukaina@123` | Sukaina Muzaffar |
| `bisma` | `Bisma@123` | Bisma Noor |
| `jannat` | `Jannat@123` | Jannat Fatima |
| `adan` | `Adan@123` | Adan Fatima |

Select a session duration and click **Start Session** to begin.

---

## IT Admin Shortcuts (Kiosk PC)

These shortcuts require the IT Admin PIN (default: `1234` — **change before deployment**).

| Shortcut | Where | Action |
|---|---|---|
| `Ctrl+Alt+Shift+S` | SessionClient or SessionAdmin | Open server IP/port configuration dialog |
| `Ctrl+Alt+Shift+Q` | SessionClient (kiosk mode only) | Gracefully close the kiosk app with billing cleanup |

> The PIN is set during installation. To change it after installation, edit
> `SessionClient.exe.config` → `AdminSettingsPin` key (requires Windows admin account).

---

## Default Credentials Summary

| Account | Username | Password | Notes |
|---|---|---|---|
| Administrator | `Admin` | `Admin@123` | Change immediately after first login |
| Test Client 1 | `sukaina` | `Sukaina@123` | Demo account — reset via SessionAdmin |
| Test Client 2 | `bisma` | `Bisma@123` | Demo account — reset via SessionAdmin |
| Test Client 3 | `jannat` | `Jannat@123` | Demo account — reset via SessionAdmin |
| Test Client 4 | `adan` | `Adan@123` | Demo account — reset via SessionAdmin |
| IT Admin PIN | — | `1234` | Protects Ctrl+Alt+Shift+S/Q — change before deployment |

---

## Detailed Documentation

| Guide | Contents |
|---|---|
| `SERVER_SETUP_GUIDE.md` | Full server setup, firewall, auto-start, server migration, admin password recovery |
| `developer\CLIENT_DEPLOYMENT_GUIDE.md` | Kiosk PC setup, KioskUser account, startup configuration |
| `developer\USER_MANAGEMENT.md` | How to register, edit, reset password, enable/disable client users |
| `developer\BILLING_RATE_MANAGEMENT.md` | How to create and manage billing rates |
| `developer\BILLING_RATES_USER_GUIDE.md` | Billing rate reference for café operators |

---

## Manual Database Setup (Without Installer)

If you are not using the installer, run the SQL script manually:

```bat
sqlcmd -S localhost\SQLEXPRESS -E -i "SessionManagement_Setup.sql"
```

This creates the database, all tables, stored procedures, and seed data
(Admin user + 4 test client users + default billing rate).

---

## Project Structure

```
SessionManagement.sln
├── SessionManagement.Shared\   — WCF contracts, database, security, webcam
├── SessionServer\              — WCF service host (console app)
├── SessionClient\              — Kiosk WPF application
├── SessionAdmin\               — Admin dashboard WPF application
├── SessionManagement_Setup.sql — Production database script (schema + seed, IF NOT EXISTS safe)
├── developer\
│   ├── SessionManagement.sql   — Full development script (drops and recreates everything)
│   ├── CLIENT_DEPLOYMENT_GUIDE.md
│   ├── USER_MANAGEMENT.md
│   ├── BILLING_RATE_MANAGEMENT.md
│   └── BILLING_RATES_USER_GUIDE.md
└── SessionManagement-Setup.iss — Inno Setup installer script
```

---

## Technology Stack

| Component | Technology |
|---|---|
| Language | C# 7.3 / .NET Framework 4.7.2 |
| UI | WPF (XAML) |
| Communication | WCF Net.TCP Duplex (port 8001) |
| Database | SQL Server Express 2019 |
| Password Security | BCrypt.Net-Next (Work Factor 12) |
| Webcam | AForge.NET 2.2.5 |
| Installer | Inno Setup 6 |

---

*Group ID: F25PROJECT8E326 | Supervisor: Asim Mehmood | Virtual University of Pakistan | Spring 2026*
