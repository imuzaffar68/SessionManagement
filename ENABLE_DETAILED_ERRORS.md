# CRITICAL: Enable Detailed Error Reporting and Restart Services

## What Changed
✅ **SessionServer\App.config** - Added `<serviceDebug includeExceptionDetailInFaults="true"/>`
✅ **SessionServer\Program.cs** - Added code to enable `IncludeExceptionDetailInFaults` behavior
✅ **Build** - Recompiled successfully

This enables the server to send detailed exception information back to the client instead of generic "internal error" messages.

## Actions Required (IMPORTANT - Do These In Order)

### Step 1: Close SessionClient Debugger
- If SessionClient is still running in the debugger, stop it (Shift+F5 or click Stop)
- Wait for it to fully close

### Step 2: Close SessionServer
- Go to the SessionServer console window
- Press `Ctrl+C` to stop the service
- Wait for the console to show it has stopped
- You can close the console window or leave it open

### Step 3: Clean and Rebuild
- In Visual Studio, go to menu: **Build → Clean Solution**
- Wait for clean to complete
- Then: **Build → Build Solution** (or press Ctrl+Shift+B)
- Verify: "Build succeeded" message appears

### Step 4: Restart SessionServer
**Option A - If you have SessionServer as a startup project:**
- Press F5 or click "Start Debugging"
- SessionServer console should appear with message: `Session Management Service is running...`

**Option B - If you're running it manually:**
- Open Command Prompt or PowerShell in the SessionServer\bin\Debug directory
- Run: `SessionServer.exe`
- You should see: `Session Management Service is running...`

### Step 5: Start SessionClient Debugging
- With SessionServer running in its console window, go back to Visual Studio
- Set SessionClient as the startup project
- Press F5 to start debugging SessionClient
- The application window should appear

### Step 6: Test Authentication
In the SessionClient login window:
- **Username:** `admin`
- **Password:** `Admin@123456`
- Click **Login** button

## What To Expect

### Best Case (Problem Fixed):
```
No FaultException errors in debug output
Login succeeds
Main window appears
Session starts normally
```

### If Still Failing (Will See Different Error):
The debug output will now show DETAILED exception information instead of generic "internal error". Look for messages like:
- `System.Data.SqlClient.SqlException` - Database connection or query error
- `Column 'XXX' invalid in select list` - Schema column mismatch
- `The key does not exist in the index.` - Accessing wrong column name
- `NullReferenceException` - Missing data

This detailed error tells us exactly what to fix.

## Debug Output Investigation

When you test, monitor the **Debug Output** window (View → Debug Output or Ctrl+Alt+O):

### If you see FaultException still:
Look for a longer error message that starts with something like:
```
System.ServiceModel.FaultException: [full exception details from server]
```

**Copy that entire error message** - it will tell us exactly what's wrong.

### Common Errors to Look For:
- `Column '...' invalid in select list` → Schema column mismatch
- `Cannot find table or view '...'` → Wrong table name
- `Conversion failed` → Data type mismatch in DataRow access
- `Column index out of range` → Accessing a column that doesn't exist

## If Database Connection Issue

The detailed errors might reveal it's a connection string problem. Check:

**In SessionServer\App.config - Connection String:**
```xml
<add name="SessionManagementDB"
     connectionString="Data Source=localhost\SQLEXPRESS;Initial Catalog=ClientServerSessionDB;Integrated Security=True;"
     providerName="System.Data.SqlClient" />
```

Verify:
- `Data Source=localhost\SQLEXPRESS` - Does your SQL Server use SQLEXPRESS? 
  - If not, change to your instance name (check SQL Server Configuration Manager)
- `Initial Catalog=ClientServerSessionDB` - Is the database named exactly this?
  - If different, change accordingly
- `Integrated Security=True` - Are you using Windows authentication?
  - If using SQL authentication, change to: `User ID=sa;Password=YourPassword;`

## Next Steps After Testing

1. **If login succeeds**: Test will proceed to SubscribeForNotifications and UpdateClientStatus methods
2. **If errors change**: Let me know the EXACT error message from the detailed fault exception
3. **If connection fails**: Check connection string matches your SQL Server setup

---

**Ready to proceed? Follow steps 1-6 above, then report what happens!**
