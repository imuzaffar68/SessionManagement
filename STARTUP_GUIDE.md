# 🚀 STARTUP GUIDE - Session Management System

## ✅ Status: READY TO START

All build errors fixed and WCF configuration corrected.

---

## 📋 Pre-Startup Checklist

- ✅ Solution builds successfully
- ✅ SessionManagement.Shared.dll generated
- ✅ WCF port aligned to 8001
- ✅ App.config files updated (both client and admin)
- ✅ Binding configuration correct
- ✅ Ready to start services

---

## 🎯 Startup Sequence (IMPORTANT: Follow in Order)

### ⏱️ Total Time: 3-5 minutes

---

## STEP 1: Verify Database ⏰ (1 minute)

```sql
-- Open SQL Server Management Studio
-- Run this query to verify database:

SELECT 
    'Database' AS [Object Type],
    name AS [Name]
FROM sys.databases
WHERE name = 'ClientServerSessionDB'

-- Expected Result: 1 row with ClientServerSessionDB
```

If database doesn't exist, run:
```
Execute: SessionManagement.sql (in SQL Server Management Studio)
Wait for: "Database Setup Complete" message
```

---

## STEP 2: Start SessionServer ⏰ (1 minute)

### Option A: Visual Studio
```
1. Open Visual Studio
2. Open project: SessionServer
3. Set as Startup Project (right-click → Set as Startup Project)
4. Press F5 (or Ctrl+F5 for Debug/Release)
5. IMPORTANT: Keep the console window open
```

### Option B: Command Line
```powershell
cd "C:\Users\Muzaffar Iqbal\source\repos\imuzaffar68\SessionManagement\SessionServer"
dotnet bin\Debug\SessionServer.exe
```

### Expected Output:
```
Session Management Service is running...
Press Enter to stop the service.
```

✅ **DO NOT PROCEED UNTIL YOU SEE THIS MESSAGE**

### Verify Port is Listening
```powershell
netstat -ano | findstr :8001

# Expected output:
# TCP    127.0.0.1:8001         LISTENING    [PID]
```

---

## STEP 3: Start SessionClient ⏰ (1 minute)

### Option A: Visual Studio (New Instance)
```
1. Open NEW Visual Studio instance (File → New → Instance)
2. Open project: SessionClient
3. Press F5
4. Wait 2-3 seconds for window to load
5. Verify NO error dialog appears
```

### Option B: Command Line (New Terminal)
```powershell
cd "C:\Users\Muzaffar Iqbal\source\repos\imuzaffar68\SessionManagement\SessionClient"
dotnet bin\Debug\SessionClient.exe
```

### Expected Result:
```
✅ Login window appears
✅ No "Connection Error" dialog
✅ Can enter username and password
```

### Test Login
```
Username: admin
Password: Admin@123456

Or use:
Username: user1
Password: User1@123456
```

---

## STEP 4: Start SessionAdmin (Optional) ⏰ (1 minute)

### Option A: Visual Studio (New Instance)
```
1. Open NEW Visual Studio instance
2. Open project: SessionAdmin
3. Press F5
4. Wait 2-3 seconds for window to load
5. Verify NO error dialog appears
```

### Option B: Command Line (New Terminal)
```powershell
cd "C:\Users\Muzaffar Iqbal\source\repos\imuzaffar68\SessionManagement\SessionAdmin"
dotnet bin\Debug\SessionAdmin.exe
```

### Expected Result:
```
✅ Admin dashboard appears
✅ No "Connection Error" dialog
✅ Can see admin options
```

---

## ✅ Success Verification

### SessionServer Console
```
✅ Shows: "Session Management Service is running..."
✅ No errors
✅ Console stays open
```

### SessionClient Window
```
✅ Login form visible
✅ No error dialogs
✅ Can type credentials
✅ Connection status: Connected
```

### SessionAdmin Window
```
✅ Admin interface visible
✅ No error dialogs
✅ Dashboard loads
✅ Can see options
```

---

## 🧪 Quick Test After Startup

### In SessionClient:
```
1. Enter admin / Admin@123456
2. Click Login
3. Verify no errors
4. Session should start successfully
```

### In SessionAdmin:
```
1. Enter admin / Admin@123456
2. Click Login
3. View Active Sessions (should be empty or show client session)
4. Monitor page should show activity
```

---

## ⚠️ If You Get "Connection Error"

### Immediate Actions:
1. ✅ Verify SessionServer console shows "Service is running..."
2. ✅ Check firewall allows port 8001
3. ✅ Verify App.config endpoint is "net.tcp://localhost:8001"
4. ✅ Check no other app is using port 8001

### Quick Fixes:
```powershell
# Check port 8001 status
netstat -ano | findstr :8001

# If port is in use, find and kill process
Get-NetTCPConnection -LocalPort 8001 | Get-Process
taskkill /PID [PID] /F

# Restart SessionServer
# (Close SessionServer console and start again)
```

### If Problem Persists:
- Read: WCF_CONNECTION_ERROR_FIX.md (troubleshooting guide)
- Check firewall rules
- Try different port (edit Program.cs and App.config)
- Check Windows Event Viewer for errors

---

## 🔄 Typical Workflow

### Session 1: Client User Logs In
```
1. SessionClient window opens login form
2. User enters: user1 / User1@123456
3. Click Login
4. Session starts
5. App shows session active
6. Billing starts calculating
```

### Session 2: Admin Monitors
```
1. SessionAdmin window shows dashboard
2. Admin enters: admin / Admin@123456
3. Admin sees active session from client
4. Admin can view real-time billing
5. Admin can view security alerts
```

---

## 🛑 Stopping Services

### To Stop SessionServer:
```
1. Go to SessionServer console window
2. Press Enter (as shown in console)
3. Service will stop gracefully
4. Console window closes
```

### To Stop SessionClient:
```
1. Close SessionClient window (X button)
2. Or press Ctrl+C in command line
```

### To Stop SessionAdmin:
```
1. Close SessionAdmin window (X button)
2. Or press Ctrl+C in command line
```

---

## 🔐 Default Test Credentials

```
admin    / Admin@123456
user1    / User1@123456
user2    / User2@123456
user3    / User3@123456
```

**Default role:** Admin for 'admin', ClientUser for others

---

## 📊 System Requirements

| Requirement | Status | Notes |
|-------------|--------|-------|
| .NET Framework 4.7.2 | ✅ Required | Already installed |
| SQL Server (any edition) | ✅ Required | LocalDB or Express |
| Visual Studio or .NET CLI | ✅ Required | For running apps |
| Port 8001 (TCP) | ✅ Required | WCF service |
| Admin privileges (optional) | ⚠️ Recommended | For firewall rules |

---

## 🌐 Network Configuration

```
localhost (127.0.0.1):
├── Port 8001 (net.tcp)
│   ├── SessionServer: Listening
│   ├── SessionClient: Connecting
│   └── SessionAdmin: Connecting
```

**Note:** All communication is local (localhost) for testing

---

## 📈 Performance Notes

```
Expected Performance:
- Login: < 2 seconds
- Session start: < 1 second
- Billing calculation: < 100ms
- Alert processing: Real-time
- UI refresh: 2-5 second intervals
```

If slower:
- Check database query performance
- Verify network latency
- Check system resource usage
- Review SQL Server indexes

---

## 🔍 Monitoring Tips

### Check Active Sessions (SQL)
```sql
SELECT * FROM dbo.tblSession WHERE Status = 'Active'
```

### Check System Logs (SQL)
```sql
SELECT TOP 50 * FROM dbo.tblSystemLog 
ORDER BY LogedAt DESC
```

### Check Login Attempts (SQL)
```sql
SELECT TOP 20 * FROM dbo.tblLoginAttempt 
ORDER BY AttemptedAt DESC
```

---

## 💾 Database Backup (After Testing)

```sql
-- Backup database before deployment
BACKUP DATABASE ClientServerSessionDB 
TO DISK = 'C:\Backups\ClientServerSessionDB.bak'
```

---

## 📝 Troubleshooting Quick Links

- **Connection Error:** WCF_CONNECTION_ERROR_FIX.md
- **Build Issues:** BUILD_RESOLUTION_REPORT.md
- **Database Setup:** DATABASE_SETUP_AND_CHANGES.md
- **Test Credentials:** TEST_CREDENTIALS.md
- **Getting Started:** QUICK_START.md

---

## ✨ Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| "Unable to connect" error | Check SessionServer is running on port 8001 |
| Long startup time (>10sec) | Check database query performance |
| Login fails | Verify credentials (admin / Admin@123456) |
| No active sessions shown | Check if client actually logged in |
| Firewall blocks connection | Add port 8001 to firewall exceptions |
| Port 8001 already in use | Kill conflicting process or use different port |

---

## 🎯 Next Steps After Startup

1. ✅ Verify all three apps running without errors
2. ✅ Test login with different users
3. ✅ Create a test session
4. ✅ Monitor session in admin panel
5. ✅ Check billing calculations
6. ✅ Review system logs
7. ✅ Test alert functionality
8. ✅ Verify database operations

---

## 🎓 Training Workflow

### For Developers:
1. Start SessionServer
2. Start SessionClient
3. Create test session
4. View session data
5. Check database directly
6. Monitor logs in SessionAdmin

### For Users:
1. Start SessionClient
2. Enter credentials
3. Create session
4. Use application normally
5. Watch billing accumulate

### For Admins:
1. Start SessionAdmin
2. Monitor active sessions
3. Review alerts
4. Check billing
5. View reports

---

## 🔧 Configuration Files Reference

### SessionServer\Program.cs
```csharp
var baseAddress = new Uri("net.tcp://localhost:8001/SessionService");
// Server listens on this address
```

### SessionClient\App.config
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" />
<!-- Client connects to this address -->
```

### SessionAdmin\App.config
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" />
<!-- Admin connects to this address -->
```

---

## ✅ Startup Checklist

Before starting, verify all are ready:

- [ ] Database ClientServerSessionDB exists
- [ ] Port 8001 is available
- [ ] SessionServer ready to start
- [ ] SessionClient ready to start
- [ ] SessionAdmin ready to start
- [ ] Credentials available (admin / Admin@123456)
- [ ] All three projects built successfully

---

## 🎉 Ready to Go!

**Status: ✅ READY FOR STARTUP**

**Next Action:** Follow startup sequence above

**Estimated Time:** 3-5 minutes

**Questions?** See WCF_CONNECTION_ERROR_FIX.md

---

**Last Updated:** 2024
**Version:** 1.0
**Status:** Ready
