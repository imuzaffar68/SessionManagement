# Detailed Diagnostic Guide - Session Management System

## Current Status Summary

### ✅ Completed Fixes
1. **Build Errors** - Fixed 5 compilation errors (BCrypt.Net integration)
2. **Port Configuration** - Fixed port mismatch (8080 → 8001)
3. **ReliableSession** - Disabled for duplex callback compatibility
4. **WCF Connection** - SessionClient can now connect to SessionServer
5. **Column Reference** - Fixed UserType → Role in SessionService.cs
6. **Error Reporting** - Enabled detailed exception information in server

### ⏳ In Progress
- Runtime service method errors (Subscribe, UpdateClientStatus, Authentication)
- Detailed error reporting being activated to identify root causes

### 📊 Architecture Overview

```
SessionClient (WPF App)
    ↓ (net.tcp://localhost:8001/SessionService)
SessionServer (WCF Host)
    ↓ (SQL Queries)
ClientServerSessionDB (SQL Server)
```

**Connection Flow:**
1. SessionClient connects to SessionServer on port 8001
2. SessionServer creates WCF duplex binding for callbacks
3. SessionServer queries database for authentication
4. Database returns user info
5. SessionService maps data to AuthenticationResponse
6. Client receives response and starts session

**Potential Failure Points:**
- Port not listening (networking)
- Connection string incorrect (database connection)
- Database doesn't exist or is offline (database availability)
- Table/column names don't match (schema alignment)
- Data type conversions fail (type mismatch)

## Database Connection String Reference

### SessionServer\App.config Current Setting:
```xml
<add name="SessionManagementDB"
     connectionString="Data Source=localhost\SQLEXPRESS;Initial Catalog=ClientServerSessionDB;Integrated Security=True;"
     providerName="System.Data.SqlClient" />
```

### Components Explained:
- **Data Source=localhost\SQLEXPRESS** 
  - Connects to SQL Server Express on local machine
  - Change if: Using SQL Server 2019+ or different instance name

- **Initial Catalog=ClientServerSessionDB**
  - Database name
  - Change if: Database is named differently

- **Integrated Security=True**
  - Uses Windows authentication (logged-in user's credentials)
  - Change to: `User ID=sa;Password=YourPassword;` for SQL authentication

### Alternative Connection Strings:

**For SQL Server (Full Edition):**
```
Data Source=localhost\MSSQLSERVER;Initial Catalog=ClientServerSessionDB;Integrated Security=True;
```

**For SQL Authentication:**
```
Data Source=localhost\SQLEXPRESS;Initial Catalog=ClientServerSessionDB;User ID=sa;Password=YourPassword;Connect Timeout=30;Encrypt=False;TrustServerCertificate=True
```

**For Remote Server:**
```
Data Source=SERVER_IP_OR_NAME;Initial Catalog=ClientServerSessionDB;Integrated Security=True;Connect Timeout=30;
```

## Port Configuration Reference

### SessionServer - Port 8001
**File:** SessionServer\Program.cs
```csharp
var baseAddress = new Uri("net.tcp://localhost:8001/SessionService");
```

**Status Check Command:**
```powershell
netstat -ano | findstr :8001
```

Expected output if running:
```
TCP    0.0.0.0:8001           0.0.0.0:0              LISTENING       [PID]
```

### SessionClient - Port Configuration
**File:** SessionClient\App.config
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" ... />
```

**AppSettings Reference:**
```xml
<add key="ServerAddress" value="localhost"/>
<add key="ServerPort" value="8001"/>
```

## WCF Configuration Reference

### Binding Configuration (SessionClient\App.config)
```xml
<binding name="DuplexBinding"
         maxReceivedMessageSize="2147483647"
         maxBufferSize="2147483647"
         receiveTimeout="00:20:00"
         sendTimeout="00:20:00"
         openTimeout="00:05:00"
         closeTimeout="00:01:00">

    <security mode="None"/>
    <reliableSession enabled="false"/>
</binding>
```

**Key Settings:**
- `maxReceivedMessageSize` - Allows messages up to 2GB
- `receiveTimeout` - 20 minutes before timeout
- `security mode="None"` - Development only (no encryption)
- `reliableSession enabled="false"` - Disabled for duplex compatibility

### Service Behavior Configuration
Both SessionServer and SessionClient have:
```xml
<serviceDebug includeExceptionDetailInFaults="true"/>
```

This enables detailed error information for debugging.

## Authentication Flow - Detailed

### Step 1: Client Sends Credentials
```csharp
// SessionClient calls:
sessionService.AuthenticateUser("admin", "Admin@123456", "CL001");
```

### Step 2: Server Hashes Password
```csharp
// SessionService.AuthenticateUser does:
string passwordHash = AuthenticationHelper.HashPassword("Admin@123456");
// Produces: $2a$12$...hash... (BCrypt format)
```

### Step 3: Database Query
```sql
SELECT UserId, Username, FullName, Role, Status 
FROM dbo.tblUser 
WHERE Username = 'admin' 
AND PasswordHash = '$2a$12$...hash...' 
AND Status = 'Active'
```

**What Can Go Wrong Here:**
- Column names incorrect (UserType vs Role) ✅ Fixed
- Table name wrong (tblUser vs tblUsers)
- Username not found
- Password hash mismatch
- User status not "Active"

### Step 4: Map Database Row to Response
```csharp
UserType = userRow["Role"].ToString(),  // ✅ Changed from "UserType"
```

**What Can Go Wrong Here:**
- Column "Role" doesn't exist
- Data type conversion fails (e.g., int to string)
- NULL value causing exception

### Step 5: Return to Client
```csharp
new AuthenticationResponse
{
    IsAuthenticated = true,
    UserId = 123,
    Username = "admin",
    FullName = "Administrator",
    UserType = "Admin",
    SessionToken = "token...",
    ErrorMessage = null
}
```

## Test Credentials - Current Setup

```
Username: admin
Password: Admin@123456
Role: Admin
Status: Active

Username: user1
Password: User1@123456
Role: User
Status: Active

Username: user2
Password: User2@123456
Role: User
Status: Active

Username: user3
Password: User3@123456
Role: User
Status: Active
```

**Password Hashing Algorithm:** BCrypt.Net-Next v4.1.0
- Cost Factor: 12 (2^12 = 4096 iterations)
- Format: $2a$12$[salt][hash]
- Security: Industry-standard, resistant to GPU attacks

## Common Error Messages and Solutions

### Error: "Cannot open database 'ClientServerSessionDB'"
**Cause:** Database doesn't exist
**Solution:** 
1. Run SessionManagement.sql script in SQL Server Management Studio
2. Verify database name in connection string

### Error: "Invalid column name 'UserType'"
**Cause:** Column doesn't exist (we fixed this from UserType → Role)
**Solution:** 
1. Verify actual column names in database using SQL query
2. Update DatabaseHelper.cs to use correct column name

### Error: "Could not connect to net.tcp://localhost:8001/SessionService"
**Cause:** SessionServer not running or wrong port
**Solution:**
1. Verify SessionServer console shows "running"
2. Check port 8001 listening: `netstat -ano | findstr :8001`
3. Verify Program.cs has correct port number
4. Check Windows Firewall isn't blocking port 8001

### Error: "The server was unable to process the request due to an internal error"
**Cause:** Generic error (now we have detailed reporting enabled)
**Solution:**
1. Look for detailed error message in debug output
2. Identify specific cause (schema, connection, etc.)
3. Fix specific issue and retest

### Error: "WCF failed to establish secure communication"
**Cause:** ReliableSession conflict
**Solution:** Verified fixed - ReliableSession disabled in both configs

## Debugging Checklist

Before testing, verify:

- [ ] **Server Running**: SessionServer console visible and shows "running"
- [ ] **Port Listening**: `netstat -ano | findstr :8001` shows LISTENING
- [ ] **Connection String**: Correct database name in App.config
- [ ] **Database Exists**: `ClientServerSessionDB` created with all tables
- [ ] **Test User Exists**: `admin` user in `tblUser` table
- [ ] **Port Config**: SessionClient endpoint = `net.tcp://localhost:8001/SessionService`
- [ ] **ReliableSession**: Disabled in both App.configs
- [ ] **Error Reporting**: `includeExceptionDetailInFaults="true"` in both configs
- [ ] **Build**: No errors, all projects compiled

## Performance Tuning (If Needed Later)

### Connection Pooling (App.config)
Add to connection string:
```
Pooling=true;Min Pool Size=5;Max Pool Size=100;
```

### Service Throttling (SessionServer config)
```xml
<serviceThrottling maxConcurrentCalls="100"
                   maxConcurrentInstances="100"
                   maxConcurrentSessions="100"/>
```

### Session Timeout (Program.cs)
```csharp
binding.ReceiveTimeout = TimeSpan.FromMinutes(30);  // Adjust as needed
```

## Next Steps

1. **Test Authentication** with the new detailed error reporting enabled
2. **Capture the exact error message** if it fails
3. **Share the error message** so we can identify and fix the issue
4. **Rebuild and retest** after each fix
5. **Verify end-to-end workflow** once authentication works

---

**Reference this guide when testing and troubleshooting!**
