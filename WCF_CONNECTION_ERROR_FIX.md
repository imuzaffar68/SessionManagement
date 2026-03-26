# WCF Connection Error - Resolution Guide

## 🔴 Error: "Unable to connect to server. Please check network connection and try again."

This error occurs when SessionClient or SessionAdmin cannot connect to SessionServer.

---

## ✅ QUICK FIX (Just Applied)

**Issue Found:** Port mismatch in WCF configuration
- ❌ SessionServer: `net.tcp://localhost:8001/SessionService`
- ❌ SessionClient/Admin: `net.tcp://localhost:8080/SessionService`

**Fix Applied:**
- ✅ Updated SessionClient App.config → Port 8001
- ✅ Updated SessionAdmin App.config → Port 8001
- ✅ Both now match SessionServer port

---

## 🚀 Correct Startup Sequence

### Step 1: Verify SessionServer is Running
```
1. Open SessionServer project
2. Press F5 or Ctrl+F5 to start
3. Verify console output:
   "Session Management Service is running..."
4. Do NOT close this console window
```

**Expected Output:**
```
Session Management Service is running...
Press Enter to stop the service.
```

### Step 2: Check Network Port
```powershell
# Verify port 8001 is listening
netstat -ano | findstr :8001

# Expected output:
# TCP    127.0.0.1:8001    LISTENING    [PID]
```

### Step 3: Start SessionClient
```
1. Open NEW Visual Studio instance (or new terminal)
2. Open SessionClient project
3. Press F5 to start
4. Wait 2-3 seconds for connection
5. Should see login window (no error)
```

### Step 4: Start SessionAdmin
```
1. Open NEW Visual Studio instance (or new terminal)
2. Open SessionAdmin project
3. Press F5 to start
4. Wait 2-3 seconds for connection
5. Should see admin dashboard
```

---

## 🔧 Configuration Verification

### SessionServer\Program.cs
```csharp
var baseAddress = new Uri("net.tcp://localhost:8001/SessionService");
// ✅ CORRECT
```

### SessionClient\App.config
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" ... />
<!-- ✅ NOW CORRECT (was 8080) -->
```

### SessionAdmin\App.config
```xml
<endpoint address="net.tcp://localhost:8001/SessionService" ... />
<!-- ✅ NOW CORRECT (was 8080) -->
```

---

## 🆘 Troubleshooting Steps

### Issue 1: "Unable to connect to server" Immediately
**Possible Causes:**
1. ❌ SessionServer not running
2. ❌ Port 8001 not listening
3. ❌ Firewall blocking port
4. ❌ Wrong port in config

**Solutions:**
```
1. Verify SessionServer console shows "Service is running..."
2. Run: netstat -ano | findstr :8001
3. Check Windows Firewall allows TCP 8001
4. Verify App.config endpoint address is "net.tcp://localhost:8001"
```

### Issue 2: Connection Timeout (Takes 30+ seconds then fails)
**Possible Causes:**
1. ❌ SessionServer crashed
2. ❌ Network configuration issue
3. ❌ Binding incompatibility

**Solutions:**
```
1. Check SessionServer console for errors
2. Verify NetTcpBinding config in both client and server
3. Verify reliableSession is enabled
4. Check for exceptions in Visual Studio Output window
```

### Issue 3: "Unable to connect" After Initial Success
**Possible Causes:**
1. ❌ SessionServer crashed
2. ❌ Network disconnected
3. ❌ Session timeout

**Solutions:**
```
1. Restart SessionServer
2. Check console for errors
3. Verify database connection is still active
4. Restart client applications
```

---

## 📋 Pre-Startup Checklist

Before starting applications, verify:

- [ ] SessionManagementDB database created
- [ ] SQL Server running and accessible
- [ ] Port 8001 not in use (netstat -ano | findstr :8001)
- [ ] Firewall allows TCP port 8001
- [ ] SessionServer app.config has correct binding
- [ ] SessionClient app.config has correct binding (port 8001)
- [ ] SessionAdmin app.config has correct binding (port 8001)
- [ ] Visual Studio debugger is not already using port
- [ ] No other app using localhost:8001

---

## 🔌 Port Conflicts

### Check if Port 8001 is Already in Use
```powershell
# List all ports in use
netstat -ano | findstr :8001

# If something is using it:
# Find the PID and task name
tasklist | findstr [PID]

# Kill the process (if safe)
taskkill /PID [PID] /F
```

### If Port 8001 is Occupied
```powershell
# Check what's using port 8001
Get-NetTCPConnection -LocalPort 8001 | Get-Process

# Kill conflicting process
Stop-Process -Name [ProcessName] -Force
```

---

## 🔌 Enable net.tcp Protocol (Windows)

If net.tcp is not enabled:

### Windows 10/11:
```
1. Go to: Control Panel → Programs → Programs and Features
2. Click: Turn Windows features on or off
3. Expand: .NET Framework 4.7.2 Advanced Services
4. Check: WCF Services → TCP Port Sharing
5. Click OK and restart computer
```

### Via PowerShell (Admin):
```powershell
# Enable net.tcp protocol support
Enable-NetAdapter -Name * -Confirm:$false
```

---

## 🐛 Debug Connection Issues

### Enable Verbose Logging
In SessionClient\App.config:
```xml
<system.diagnostics>
  <trace autoflush="true">
    <listeners>
      <add name="textWriterTraceListener" 
           type="System.Diagnostics.TextWriterTraceListener" 
           initializeData="Client_Trace.log" />
    </listeners>
  </trace>
</system.diagnostics>
```

### View Output Window Logs
```
In Visual Studio:
1. Debug → Windows → Output
2. Show output from: Debug
3. Look for WCF errors
```

### Check Event Viewer
```
1. Run: eventvwr.msc
2. Go to: Windows Logs → System
3. Look for errors from .NET Framework
4. Look for WCF or TCP errors
```

---

## ✅ Verification Steps

### Step 1: SessionServer Verification
```
Expected when SessionServer starts:
✅ Console window shows: "Session Management Service is running..."
✅ netstat shows: TCP 127.0.0.1:8001 LISTENING
✅ No errors in output console
```

### Step 2: SessionClient Verification
```
Expected when SessionClient starts:
✅ No connection error dialog
✅ Login window appears
✅ Can enter credentials
✅ Database queries work
```

### Step 3: SessionAdmin Verification
```
Expected when SessionAdmin starts:
✅ No connection error dialog
✅ Admin dashboard appears
✅ Can see active sessions (if any)
✅ Can view alerts
```

---

## 📊 Configuration Summary

| Component | Setting | Value | Status |
|-----------|---------|-------|--------|
| SessionServer | Base Address | net.tcp://localhost:8001 | ✅ |
| SessionServer | Binding | NetTcpBinding | ✅ |
| SessionServer | Port | 8001 | ✅ |
| SessionClient | Endpoint | net.tcp://localhost:8001 | ✅ FIXED |
| SessionClient | Binding | netTcpBinding | ✅ |
| SessionAdmin | Endpoint | net.tcp://localhost:8001 | ✅ FIXED |
| SessionAdmin | Binding | netTcpBinding | ✅ |

---

## 🚀 Step-by-Step Startup

### Terminal 1: Start SessionServer
```powershell
cd C:\Users\Muzaffar Iqbal\source\repos\imuzaffar68\SessionManagement\SessionServer
dotnet bin\Debug\SessionServer.exe
# OR in Visual Studio: F5
```

**Wait for:** "Session Management Service is running..."

### Terminal 2: Start SessionClient
```powershell
cd C:\Users\Muzaffar Iqbal\source\repos\imuzaffar68\SessionManagement\SessionClient
dotnet bin\Debug\SessionClient.exe
# OR in Visual Studio: F5
```

**Wait for:** Login window appears (no error)

### Terminal 3: Start SessionAdmin
```powershell
cd C:\Users\Muzaffar Iqbal\source\repos\imuzaffar68\SessionManagement\SessionAdmin
dotnet bin\Debug\SessionAdmin.exe
# OR in Visual Studio: F5
```

**Wait for:** Admin window appears (no error)

---

## ⚠️ Common Mistakes

❌ **Mistake 1:** Starting client before server
```
✅ FIX: Always start SessionServer first, wait for console message
```

❌ **Mistake 2:** Closing SessionServer console window
```
✅ FIX: Keep SessionServer running in dedicated window/terminal
```

❌ **Mistake 3:** Not updating both client configs
```
✅ FIX: Update BOTH SessionClient AND SessionAdmin app.config
```

❌ **Mistake 4:** Firewall blocking port
```
✅ FIX: Add TCP port 8001 to firewall exceptions
```

❌ **Mistake 5:** Database not created
```
✅ FIX: Run SessionManagement.sql first
```

---

## 📞 Quick Troubleshooting Commands

```powershell
# Check if port 8001 is listening
netstat -ano | findstr :8001

# Check what's using the port
Get-NetTCPConnection -LocalPort 8001 | Get-Process

# Check firewall rule for port 8001
netsh advfirewall firewall show rule name="WCF 8001"

# View System Event Log
Get-EventLog -LogName System -Newest 20

# View Application Event Log
Get-EventLog -LogName Application -Newest 20 | where {$_.Source -like "*WCF*"}

# Flush TCP connections (if stuck)
netsh winsock reset catalog
```

---

## ✨ Success Indicators

When everything is working correctly:

```
✅ SessionServer Console Output:
   Session Management Service is running...
   Press Enter to stop the service.

✅ SessionClient:
   - No error dialog
   - Login window appears
   - Can enter credentials
   - No connection timeouts

✅ SessionAdmin:
   - No error dialog
   - Admin dashboard appears
   - Can view sessions/alerts
   - No connection timeouts

✅ Network:
   - netstat shows port 8001 LISTENING
   - No firewall blocks
   - Latency < 100ms
```

---

## 🎯 After Fix - Next Steps

1. ✅ Rebuild solution (Ctrl+Shift+B)
2. ✅ Start SessionServer (F5 in SessionServer project)
3. ✅ Wait 2 seconds for startup message
4. ✅ Start SessionClient in new instance (F5)
5. ✅ Verify no connection error
6. ✅ Log in with credentials (admin / Admin@123456)
7. ✅ Start SessionAdmin in new instance (F5)
8. ✅ Verify no connection error

---

## 📚 Related Documents

- QUICK_START.md - Getting started guide
- DATABASE_SETUP_AND_CHANGES.md - Database setup
- TEST_CREDENTIALS.md - Login credentials

---

**Status:** ✅ Configuration Fixed (Port 8001 Aligned)
**Next Action:** Rebuild and restart applications
