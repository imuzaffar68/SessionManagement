# Next Steps: Rebuild Solution and Test Authentication Fix

## Status
✅ **Code Fix Applied**: SessionService.cs has been updated (userRow["UserType"] → userRow["Role"])
✅ **Build Successful**: Solution compiles without errors
⏳ **Runtime Deployment Pending**: Need to restart services to apply the compiled fix

## Immediate Actions Required

### 1. Stop Current Debug Session
- Close the SessionClient debugger window (if running)
- Do NOT close SessionServer yet - we'll do that next

### 2. Restart SessionServer
**In SessionServer console window:**
- Press `Ctrl+C` to stop the current SessionServer instance
- Wait for it to fully stop (you should see "The session management service has stopped" or similar message)
- Scroll up to clear the old output
- Press `Enter` or run the application again

**Expected output when server restarts:**
```
Session Management Service is running on net.tcp://localhost:8001/SessionService
Press Enter to stop the service...
```

### 3. Relaunch SessionClient in Debug Mode
- Press F5 or click "Start Debugging" to launch SessionClient
- The application window should appear without the "Connection Error" dialog

### 4. Test Authentication
**In SessionClient:**
- Username: `admin`
- Password: `Admin@123456`
- Click "Login" or press Enter

**Expected Results:**
- ✅ No FaultException error in debug output
- ✅ Client connects successfully
- ✅ Session starts
- ✅ MainWindow appears (or appropriate next screen)

## What Was Fixed
The SessionService.cs `AuthenticateUser` method was referencing the wrong database column:
- **Before**: `userRow["UserType"]` - This column doesn't exist in the database
- **After**: `userRow["Role"]` - Correct column name from tblUser table

When the client tried to authenticate, the database returned a valid row, but SessionService tried to access a non-existent column, causing an exception that was wrapped in a FaultException.

## If Authentication Still Fails

If you still see FaultException errors after restarting, check:

### Debug Output to Review:
1. Look for specific column name errors in the debug output
2. Check for "Key not found" exceptions
3. Look for "The server was unable to process the request" messages

### Quick Diagnostics:
- Open SessionServer console and look for detailed error messages
- Check if the error mentions "UpdateClientStatus" or "SubscribeForNotifications"
- These methods may have other schema alignment issues

### Enable Detailed Error Reporting (if needed):
If errors persist, we can enable `IncludeExceptionDetailInFaults` in the WCF binding to get detailed server-side exception information.

## Test Credentials
All test users have BCrypt-hashed passwords (cost factor 12):
- **admin** / `Admin@123456` - Administrator role
- **user1** / `User1@123456` - User role
- **user2** / `User2@123456` - User role  
- **user3** / `User3@123456` - User role

## Expected Workflow After Fix
1. SessionClient connects to SessionServer on port 8001 ✅ (already working)
2. User enters credentials
3. SessionService.AuthenticateUser runs and queries database ✅ (should work now)
4. Returns AuthenticationResponse with Role correctly mapped ✅ (should work now)
5. Session created and billing starts
6. MainWindow displays active session

## If You Encounter Other Service Method Failures

The remaining FaultException errors mentioned in debug output were from:
- `SubscribeForNotifications` - Duplex callback subscription
- `UpdateClientStatus` - Client status update in database

These will be tested once authentication succeeds. If they fail, they likely have similar schema mismatches that we'll need to fix.

---

**Ready to proceed? Follow the steps above and report back with the authentication test result!**
