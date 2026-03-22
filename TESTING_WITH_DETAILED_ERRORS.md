# Testing with Detailed Error Reporting - Complete Guide

## Summary of Changes Made

### 1. SessionServer\App.config
Added WCF service behavior to enable detailed exception details:
```xml
<system.serviceModel>
    <behaviors>
        <serviceBehaviors>
            <behavior>
                <serviceDebug includeExceptionDetailInFaults="true" />
            </behavior>
        </serviceBehaviors>
    </behaviors>
</system.serviceModel>
```

### 2. SessionServer\Program.cs
Added code to ensure the service behavior is applied at runtime:
```csharp
// Enable detailed exception information for debugging
var debugBehavior = host.Description.Behaviors.Find<ServiceDebugBehavior>();
if (debugBehavior == null)
{
    debugBehavior = new ServiceDebugBehavior();
    host.Description.Behaviors.Add(debugBehavior);
}
debugBehavior.IncludeExceptionDetailInFaults = true;
```

### 3. Build Status
✅ Solution successfully compiled with all changes

## What This Enables

Instead of generic error messages like:
```
FaultException: The server was unable to process the request due to an internal error
```

You'll now see detailed exceptions like:
```
FaultException: System.Data.SqlClient.SqlException: Invalid column name 'UserType' in table 'tblUser'
```

This tells us **exactly** what needs to be fixed.

## Complete Testing Procedure

### Phase 1: Setup (5 minutes)

**1. Close all running processes**
- Stop SessionClient (Shift+F5 if debugging)
- Stop SessionServer (Ctrl+C in console)
- Wait 2-3 seconds

**2. Clean rebuild**
```
Visual Studio Menu:
Build → Clean Solution (wait for completion)
Build → Build Solution (wait for "Build succeeded")
```

**3. Start SessionServer**
```
Option A - Direct run (easiest):
  - Right-click on SessionServer project → Set as Startup Project
  - Press F5
  - Console window appears with "Session Management Service is running..."

Option B - Run from folder:
  - Open CMD/PowerShell in: SessionServer\bin\Debug\
  - Run: .\SessionServer.exe
  - Console shows: "Session Management Service is running..."
```

Leave SessionServer running in its console window.

### Phase 2: Testing (2 minutes)

**4. Switch to SessionClient and start debugging**
```
Visual Studio:
- Right-click on SessionClient project → Set as Startup Project
- Press F5 to start debugging
- SessionClient window should appear
```

**5. Test Authentication**
```
In SessionClient Login Window:
- Username: admin
- Password: Admin@123456
- Click "Login" or press Enter
```

**6. Monitor Debug Output**
```
Visual Studio:
- View → Debug Output (or Ctrl+Alt+O)
- Watch for either:
  ✅ SUCCESS: No FaultException, login succeeds
  ❌ FAILURE: FaultException with detailed error message
```

### Phase 3: Interpreting Results

#### If Login Succeeds (Green Light)
```
Debug Output shows:
- No FaultException errors
- SessionClient window shows main interface or next screen
- Status bar shows "Connected" or similar

Action: The core authentication is working!
Next Step: Test if Subscribe and UpdateClientStatus work
```

#### If Still Getting FaultException
Look for one of these patterns in the debug output:

**Pattern 1: Column Not Found**
```
System.Data.SqlClient.SqlException: Invalid column name 'XXX'
```
→ Schema mismatch, we need to update the query to use correct column name

**Pattern 2: Table Not Found**
```
System.Data.SqlClient.SqlException: Invalid object name 'dbo.tblXXX'
```
→ Table name is wrong, need to verify table name in schema

**Pattern 3: Conversion Error**
```
System.InvalidCastException: Unable to cast object
```
→ Trying to convert data to wrong type, likely due to column mismatch

**Pattern 4: Connection String Issue**
```
System.Data.SqlClient.SqlException: Cannot open database 'XXX' requested by the login
```
→ Database name or connection string is wrong

### Phase 4: Common Fixes

Once you identify the error, common fixes include:

1. **Update DatabaseHelper.cs** - Fix column names in SQL queries
2. **Update connection string** - If database name is different
3. **Re-run database script** - If tables don't exist
4. **Rebuild solution** - After any code changes

## If It Works

If authentication succeeds and login works:
1. The UserType→Role fix was successful
2. All subsequent tests should proceed
3. Note any other FaultExceptions for Subscribe or UpdateClientStatus methods
4. Report those for the next round of fixes

## If You Get Connection Refused

```
System.ServiceModel.EndpointNotFoundException: 
Could not connect to net.tcp://localhost:8001/SessionService
```

→ SessionServer isn't running or isn't listening on port 8001
   - Check SessionServer console is showing "running"
   - Verify Program.cs has correct port (8001)
   - Try: netstat -ano | findstr :8001

## Critical Checklist Before Testing

- [ ] SessionServer is running in a console window (not visual studio debugger yet)
- [ ] SessionClient is being debugged (F5, not just Run)
- [ ] Debug Output window is open and visible
- [ ] Both App.configs have updated endpoints (port 8001)
- [ ] ReliableSession is disabled in both App.configs
- [ ] Build succeeded with no errors
- [ ] You're testing with credentials: admin / Admin@123456

## Next Steps After Testing

**Once you have the detailed error message**, share it and we can:
1. Identify exactly what field/table is causing the issue
2. Update the corresponding code
3. Recompile and retest
4. Verify fix works

---

**Ready to test? Follow the Complete Testing Procedure above!**
