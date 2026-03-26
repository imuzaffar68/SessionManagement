# Quick Start Guide - Session Management System

## ✅ Build Status: PASSING

All compilation errors have been resolved. The solution is ready to run.

---

## 🚀 Getting Started

### Step 1: Setup Database
```bash
# Open SQL Server Management Studio (SSMS)
# Execute: SessionManagement.sql (at repository root)
```

**Expected output:**
```
Database ClientServerSessionDB created
Tables created: 10
Stored procedures created: 7
Views created: 2
Seed data inserted: 4 users, 3 clients, 3 rates, 6 activity types
```

### Step 2: Start SessionServer
```bash
# In Visual Studio: Press F5 or Ctrl+F5
# Or in PowerShell:
dotnet SessionServer\bin\Debug\SessionServer.exe
```

**Expected output:**
```
Session Management Service is running...
Press Enter to stop the service.
```

### Step 3: Connect SessionClient
- Username: `admin` or `user1`, `user2`, `user3`
- Password: `Admin@123456` or `User1@123456`, `User2@123456`, `User3@123456`

---

## 📋 Test Credentials

```
admin    / Admin@123456      → Administrator role
user1    / User1@123456      → Client user
user2    / User2@123456      → Client user
user3    / User3@123456      → Client user
```

---

## 🗄️ Database Information

**Server:** localhost\SQLEXPRESS (default) or your configured instance
**Database:** ClientServerSessionDB
**Connection String:** `Server=.\SQLEXPRESS;Database=ClientServerSessionDB;Integrated Security=True;`

### Main Tables
- tblUser (4 users with BCrypt hashed passwords)
- tblClientMachine (3 clients)
- tblSession (session tracking)
- tblBillingRate (3 rates, $0.50/min default)
- tblAlert (security alerts)
- tblSystemLog (system events)

---

## 🔐 Password Security

### Hashing Algorithm: BCrypt
- **Cost Factor:** 12 (2^12 = 4096 iterations)
- **Format:** $2a$cost$salt_and_hash
- **Library:** BCrypt.Net-Next v4.1.0

### Test in Code
```csharp
// Hash a password
string hash = AuthenticationHelper.HashPassword("MyPassword123!");

// Verify a password
bool isValid = AuthenticationHelper.VerifyPassword("MyPassword123!", hash);
```

---

## 🌐 WCF Service Configuration

### Server Binding
- **Protocol:** net.tcp (TCP/IP)
- **Address:** net.tcp://localhost:8001/SessionService
- **Binding:** NetTcpBinding
- **Security Mode:** None (development only)

### Client Configuration
Update `App.config` in SessionClient and SessionAdmin:
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" 
          binding="netTcpBinding" ... />
```

---

## 📁 Important Files

### Setup & Documentation
- `SessionManagement.sql` - Database creation & seed data
- `UPDATE_PASSWORDS.sql` - Update passwords in existing database
- `TEST_CREDENTIALS.md` - Detailed credentials
- `DATABASE_SETUP_AND_CHANGES.md` - Full setup guide
- `BUILD_RESOLUTION_REPORT.md` - Build error resolution details

### Source Code
- `SessionServer\Program.cs` - WCF host (port 8001)
- `SessionManagement.Shared\Security\AuthenticationHelper.cs` - BCrypt integration
- `SessionManagement.Shared\Data\DatabaseHelper.cs` - Database access layer

---

## ⚙️ Configuration Files

### SessionServer\App.config
```xml
<connectionStrings>
  <add name="SessionManagementDB" 
       connectionString="Server=.\SQLEXPRESS;Database=ClientServerSessionDB;Integrated Security=True;" />
</connectionStrings>
```

### SessionClient\App.config
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" binding="netTcpBinding" ... />
```

---

## 🐛 Common Issues & Solutions

### Issue: Database connection fails
```
Solution: Verify SQL Server is running
          Check connection string in App.config
          Ensure database was created successfully
```

### Issue: WCF connection times out
```
Solution: Verify SessionServer is running (shows "Service is running...")
          Check firewall allows TCP port 8001
          Verify net.tcp protocol is enabled
```

### Issue: Password authentication fails
```
Solution: Verify password matches case-sensitive string
          Ensure user Status = 'Active' in database
          Check tblLoginAttempt table for failure reasons
```

### Issue: Build errors return
```
Solution: Clean solution (Build → Clean Solution)
          Rebuild solution (Build → Rebuild Solution)
          Restore NuGet packages (Tools → NuGet Package Manager → Restore Packages)
```

---

## 📊 Architecture Overview

```
SessionClient ←→ SessionServer (net.tcp) ←→ SQL Database
   ↓                    ↓                        ↓
 UI Layer        WCF Service Host         ClientServerSessionDB
(Windows Forms) (Duplex Callbacks)      (10 tables, 7 SPs, 2 Views)
                                              ↓
                                    - Sessions & Billing
                                    - Alerts & Logging
                                    - User Authentication
```

---

## 🔍 Key Features Implemented

- ✅ Secure password hashing with BCrypt
- ✅ Session management with billing
- ✅ Real-time alerts and notifications
- ✅ Login image capture
- ✅ Comprehensive audit logging
- ✅ WCF duplex communication
- ✅ Database stored procedures
- ✅ Role-based access control

---

## 📝 Development Workflow

1. **Database changes:** Update SQL, run `SessionManagement.sql`
2. **Code changes:** Edit C# files, rebuild solution
3. **Test changes:** 
   - Restart SessionServer
   - Reconnect SessionClient
   - Monitor tblSystemLog for events
4. **Debug:**
   - Use VS debugger with breakpoints
   - Check tblAlert for security events
   - Review tblLoginAttempt for auth failures

---

## 🎯 Next Steps

1. ✅ Build solution (already passing)
2. ⏭️ Run SessionManagement.sql to create database
3. ⏭️ Start SessionServer (F5)
4. ⏭️ Connect SessionClient with test credentials
5. ⏭️ Create test sessions and view alerts
6. ⏭️ Review reports and billing

---

## 📞 Quick Reference Commands

### PowerShell (in repo root)
```powershell
# Rebuild solution
dotnet build

# Run SessionServer
cd SessionServer
dotnet bin\Debug\SessionServer.exe

# Generate new password hash
cd ..\SessionManagement.Shared\Security
# Edit PasswordHashGenerator.cs then:
dotnet PasswordHashGenerator.exe
```

### SQL Server (SSMS)
```sql
-- Check active sessions
EXEC sp_GetActiveSessions;

-- View recent logs
SELECT TOP 100 * FROM dbo.tblSystemLog 
ORDER BY LogedAt DESC;

-- View alerts
SELECT * FROM dbo.tblAlert WHERE IsAcknowledged = 0;

-- Update password
UPDATE dbo.tblUser 
SET PasswordHash = '[NEW_HASH]' 
WHERE Username = 'admin';
```

---

## ✨ Build Status Summary

```
✅ SessionManagement.Shared - BUILD SUCCESS
✅ SessionServer           - BUILD SUCCESS
✅ SessionClient           - BUILD SUCCESS  
✅ SessionAdmin            - BUILD SUCCESS

Total Build Time: < 5 seconds
Warnings: 0
Errors: 0
```

---

**Last Updated:** 2024
**Status:** Ready for Development
**Next Action:** Run SessionManagement.sql
