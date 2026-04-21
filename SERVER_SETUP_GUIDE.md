# Server Setup Guide — SessionManagement

Step-by-step instructions to get `SessionServer` running on a fresh Windows PC.
Follow sections in order: Dependencies → Database → Configuration → Firewall → Run.

---

## Step 1 — Required Dependencies

Install these on the **server PC only** before doing anything else.

### 1.1 SQL Server (choose one)

| Edition | Cost | Best for | Download |
|---|---|---|---|
| **SQL Server Express 2019** | Free | Small café (≤ 10 GB data) | microsoft.com/en-us/sql-server/sql-server-downloads |
| SQL Server Developer 2019 | Free | Development / testing only | same page |
| SQL Server Standard / Enterprise | Paid | Large deployment | — |

> **Recommended for this project:** SQL Server Express 2019 — free, no licence,
> sufficient for a cyber café with tens of machines and months of billing data.

During installation:
- Choose **"New SQL Server stand-alone installation"**
- Instance name: leave as default → **`SQLEXPRESS`**
- Authentication mode: **Windows Authentication** (matches the connection string)
- Note the instance name shown at the end of setup — you will need it in Step 3.

### 1.2 SQL Server Management Studio (SSMS) — optional but recommended

Free GUI tool for running the database script and checking data.  
Download from the same Microsoft SQL Server downloads page.  
Version: SSMS 19 or later.

### 1.3 .NET Framework 4.7.2

The server app targets `.NETFramework,Version=v4.7.2`.

- **Windows 10 / 11:** already installed (built in).
- **Windows Server 2016 / 2019:** install via Windows Update or download
  the offline installer from microsoft.com (search ".NET Framework 4.7.2 offline installer").

To verify it is installed:
```
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
```
Value `461808` or higher = 4.7.2 or later. ✓

---

## Step 2 — Create the Database

The file `SessionManagement.sql` (project root) creates the database, all
tables, stored procedures, and seed data from scratch. It is safe to re-run —
it drops and recreates everything.

### Option A — SSMS (recommended)

1. Open SSMS → connect to `localhost\SQLEXPRESS` using Windows Authentication.
2. File → Open → `SessionManagement.sql`.
3. Click **Execute** (or press `F5`).
4. Confirm the Messages pane shows no errors.
5. In Object Explorer refresh and verify `ClientServerSessionDB` appears.

### Option B — Command line (sqlcmd)

```bat
sqlcmd -S localhost\SQLEXPRESS -E -i "C:\Path\To\SessionManagement.sql"
```

`-E` = Windows Authentication (no username/password needed).

### Verify the database was created

```sql
USE ClientServerSessionDB;
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME;
```

Expected tables:
- tblAlert, tblActivityType, tblBillingRate, tblBillingRecord,
  tblClientMachine, tblLoginAttempt, tblSession, tblSessionImage,
  tblSystemLog, tblUser

---

## Step 3 — Configure the Connection String

Open `SessionServer\App.config` and find this line:

```xml
<add name="SessionManagementDB"
     connectionString="Data Source=localhost\SQLEXPRESS;Initial Catalog=ClientServerSessionDB;Integrated Security=True;"
     providerName="System.Data.SqlClient" />
```

### Common adjustments

| Your SQL Server setup | Change `Data Source=` to |
|---|---|
| Express (default install) — **no change needed** | `localhost\SQLEXPRESS` |
| Full SQL Server (default instance) | `localhost` |
| Named instance other than SQLEXPRESS | `localhost\YOURINSTANCENAME` |
| Remote SQL Server on another PC | `192.168.1.X\SQLEXPRESS` |

### Finding your instance name

```bat
sqlcmd -L
```

Or in SSMS the instance name appears in the "Server name" box on the login dialog.

### Integrated Security vs SQL login

The current connection string uses `Integrated Security=True` (Windows
Authentication). This means **the Windows account that runs `SessionServer.exe`
must have access to SQL Server**.

Default behaviour: SQL Server Express grants access to the local Administrators
group automatically, so running the server as any admin account works out of
the box.

If you need a SQL username/password instead (e.g. the server runs as a service
account), replace `Integrated Security=True` with:
```
User ID=yourUser;Password=yourPassword;
```
> Never commit a password to source control. Use a separate config file or
> environment variable in that case.

---

## Step 4 — Windows Firewall (required for LAN clients)

Client PCs connect to the server on TCP port **8001**. Open that port once on
the server PC:

```bat
netsh advfirewall firewall add rule ^
      name="SessionService" ^
      dir=in action=allow protocol=TCP localport=8001
```

Run this in an **elevated** (Run as Administrator) command prompt.

To verify the rule was added:
```bat
netsh advfirewall firewall show rule name="SessionService"
```

> If you change `ServerPort` in `App.config`, replace `8001` above with the new port.

---

## Step 5 — Run the Server

1. Build the solution in Visual Studio (`Ctrl+Shift+B`) in **Release** mode.
2. Navigate to `SessionServer\bin\Release\`.
3. Run `SessionServer.exe` — **right-click → Run as Administrator** (required
   to register WCF endpoints).
4. The console window should show:
   ```
   SessionService is running at net.tcp://localhost:8001/SessionService
   Press Enter to stop the service.
   ```
5. On client PCs, set `ServerAddress` in `SessionClient.exe.config` to this
   server PC's LAN IP address (e.g. `192.168.1.10`).

### Auto-start on boot (optional)

To start the server automatically when Windows boots, create a scheduled task:

```bat
schtasks /create /tn "SessionServer" /tr "C:\Path\To\SessionServer.exe" ^
         /sc onstart /ru SYSTEM /rl HIGHEST /f
```

Or use NSSM (Non-Sucking Service Manager) to wrap it as a proper Windows Service.

---

## Step 6 — Verify Everything Works

Run this checklist after first setup:

- [ ] SQL Server service is running (`services.msc` → SQL Server (SQLEXPRESS))
- [ ] `ClientServerSessionDB` exists in SSMS Object Explorer
- [ ] `SessionServer.exe` console shows the listening message
- [ ] Firewall rule for port 8001 is present
- [ ] From a client PC, `SessionClient.exe` connects without error
- [ ] Admin logs in via `SessionAdmin.exe` successfully

---

## Troubleshooting

| Error | Likely cause | Fix |
|---|---|---|
| `Cannot open database "ClientServerSessionDB"` | SQL script not run | Run `SessionManagement.sql` (Step 2) |
| `A network-related error occurred` | Wrong instance name in connection string | Check `Data Source=` (Step 3) |
| `Login failed for user 'NT AUTHORITY\SYSTEM'` | Service account has no SQL access | Grant login in SSMS → Security → Logins |
| `AddressAlreadyInUseException` on port 8001 | Another app using the port | Change `ServerPort` in App.config and update firewall rule |
| Client gets `EndpointNotFoundException` | Firewall blocking port 8001 | Re-run netsh command in Step 4 |
| `System.UriFormatException` | `ListenAddress` set to `"+"` | Keep `ListenAddress=localhost` — do not change it |
