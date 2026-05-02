# User Management — Admin Guide

## Overview

The **User Management** tab in the Admin Dashboard covers the full lifecycle of ClientUser accounts:
- **Register** new users who will use the kiosk PCs
- **Edit** user profile details (Full Name, Phone, Address)
- **Reset Password** with validation and confirmation
- **Toggle Status** between Active and Disabled

All operations require admin login and are logged to `tblSystemLog`.

---

## Part 1 — Register New Users

### How to Register

1. Login to SessionAdmin → go to **User Management** tab
2. Fill the registration form:

| Field | Required | Notes |
|---|---|---|
| Username | Yes | 3–50 characters, must be unique |
| Full Name | No | Optional display name |
| Password | Yes | Min 6 characters (or use Generate button) |
| Phone | No | Optional |
| Address | No | Optional |

3. **Quick option:** Click **Generate Pwd (User@123456)** to auto-fill the default password
4. Click **Register User**
5. Success message shows the new UserId — form clears automatically
6. New user appears in the user list below

### After Registration

- User account created in `tblUser` with Role = `ClientUser`, Status = `Active`
- Password stored as BCrypt hash (WorkFactor 12) — never plain text
- Registration logged: Category `Auth`, Type `UserRegistered`
- New user can immediately log in via `SessionClient` with their credentials

### Giving Credentials to the User

```
Username: john.doe
Password: User@123456  (or the custom password you set)
```

User opens SessionClient → enters credentials → selects duration → starts session.

---

## Part 2 — Manage Existing Users

Each row in the user list has three inline action buttons:

| Icon | Color | Action |
|---|---|---|
| **✎** | Blue | Edit user profile (Full Name, Phone, Address) |
| **🔑** | Purple | Reset password |
| **⊕** | Green | Toggle status (Active ↔ Disabled) |

### Edit User Profile

1. Click **✎** on the user row
2. Modify **Full Name**, **Phone**, or **Address** (Username is read-only)
3. Click **Save** — success message appears in the status panel below the list

### Reset Password

1. Click **🔑** on the user row
2. Enter **New Password** and **Confirm Password**
3. Requirements: 8+ characters, uppercase, lowercase, digit
4. Quick option: click **Generate (User@123456)** for default password
5. Click **Reset Password**

### Toggle User Status

1. Click **⊕** on the user row
2. Confirm the status change in the popup
3. Status switches:
   - **Active → Disabled** — user cannot authenticate (login blocked immediately)
   - **Disabled → Active** — user can log in again

> Users cannot be deleted due to foreign key constraints (sessions, billing records, login attempts all reference the user). Use Disabled to prevent login while keeping history intact.

---

## Business Rules

### Status Values
| Status | Effect |
|---|---|
| `Active` | User can log in and start sessions |
| `Disabled` | Authentication blocked — cannot log in |
| `Blocked` | Reserved for security-triggered blocks (repeated failed logins) |

### Password Requirements (Reset dialog)
- Minimum 8 characters
- At least one uppercase letter (A–Z)
- At least one lowercase letter (a–z)
- At least one digit (0–9)
- No maximum length

### Registration Validation
- Username: 3–50 characters, unique across all users
- Password: minimum 6 characters (stricter 8-char rule applies at reset)
- All validation runs server-side before any DB write

---

## Audit Trail

All operations are logged to **Session Logs** tab (Category: `Auth`):

| Operation | Log Type |
|---|---|
| New user registered | `UserRegistered` |
| Profile updated | `UserUpdated` |
| Password reset | `PasswordReset` |
| Status toggled | `UserStatusChanged` |

Each entry records the admin user ID and timestamp.
To view: Session Logs tab → set Category to `Auth` → click **Load Logs**.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| Tab | Move between fields |
| Enter | Submit dialog |
| Escape | Cancel / close dialog |

---

## Troubleshooting

| Problem | Solution |
|---|---|
| "Duplicate username" error | Choose a different username |
| Dialog won't open | Verify server connection is active; check status panel for errors |
| User doesn't appear after registration | Click **Refresh** to reload the list |
| Can't log in after password reset | Check password is exact (case-sensitive); verify status is `Active` |
| "Not connected to server" | Ensure `SessionServer.exe` is running on the configured endpoint |

---

## Technical Reference

### Architecture

```
Admin UI (WPF)
    └── SessionServiceClient (proxy)
            └── WCF SessionService
                    └── DatabaseHelper
                            └── SQL Server (tblUser, tblSystemLog)
```

### WCF Operation Contracts

```csharp
// Registration
UserRegistrationResponse RegisterClientUser(
    string username, string fullName, string password,
    string phone, string address, int adminUserId);

UserInfo[] GetAllClientUsers();

// Management
UserUpdateResponse       UpdateClientUser(int userId, string fullName, string phone, string address, int adminUserId);
PasswordResetResponse    ResetClientUserPassword(int userId, string newPassword, int adminUserId);
UserStatusToggleResponse ToggleUserStatus(int userId, int adminUserId);
```

### Response Objects

All management methods return a response object with:
- `Success` (bool) — operation succeeded
- `UserId` (int) — affected user
- `ErrorMessage` (string) — details on failure
- `NewStatus` (string) — for `ToggleUserStatus`

### Database Schema — tblUser

```
UserId           INT IDENTITY PRIMARY KEY
Username         NVARCHAR(50) UNIQUE NOT NULL
PasswordHash     NVARCHAR(255) NOT NULL
FullName         NVARCHAR(100) NULL
Role             NVARCHAR(20)  -- 'Admin' | 'ClientUser'
Status           NVARCHAR(20)  -- 'Active' | 'Blocked' | 'Disabled'
Phone            NVARCHAR(30)  NULL
Address          NVARCHAR(200) NULL
CreatedByUserId  INT FK → tblUser (NULL)
CreatedAt        DATETIME DEFAULT GETDATE()
LastLoginAt      DATETIME NULL
```

### Files Modified

| File | Change |
|---|---|
| `SessionManagement.Shared\WCF\IsessionService.cs` | Added 5 operation contracts + data contracts |
| `SessionManagement.Shared\WCF\SessionService.cs` | Implemented all 5 methods |
| `SessionManagement.Shared\Data\DatabaseHelper.cs` | Added RegisterClientUser, GetAllClientUsers, UpdateClientUser, ResetUserPassword, UpdateUserStatus, GetUserById |
| `SessionAdmin\MainWindow.xaml` | Added User Management tab (registration form + user list DataGrid + inline action buttons) |
| `SessionAdmin\MainWindow.xaml.cs` | Added UserVM, event handlers, LoadClientUsers |
| `SessionAdmin\EditUserWindow.xaml/.cs` | New dialog — edit profile |
| `SessionAdmin\ResetPasswordWindow.xaml/.cs` | New dialog — reset password |
| `SessionAdmin\SessionServiceClient.cs` | Added 5 proxy methods |
