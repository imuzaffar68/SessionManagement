# QUICK FIX: "Client Not Found" Error (2 Minutes)

## The Problem
After picture capture, login fails with: **"Client not found"**

## The Root Cause
Database name was wrong:
- ❌ SessionClient looking in: "SessionManagementDB"
- ✅ Actual database: "ClientServerSessionDB"

## The Fix (3 Changes)

### 1. ✅ SessionClient\App.config
```
Find: Initial Catalog=SessionManagementDB
Change to: Initial Catalog=ClientServerSessionDB
```

### 2. ✅ SessionAdmin\App.config
```
Find: Initial Catalog=SessionManagementDB
Change to: Initial Catalog=ClientServerSessionDB
```

### 3. ✅ Added Debug Logging
- SessionService.cs shows [SESSION] messages
- DatabaseHelper.cs shows [DB] messages
- Will help diagnose future issues

---

## Test in 2 Minutes

### Build (30 sec)
```
Build → Rebuild Solution
```

### Run (1 min 30 sec)
```
1. F5 SessionServer
2. F5 SessionClient
3. Login: user1 / User1@123456
4. Capture image
5. Select duration
6. Start session

Expected: ✅ Works! (was "Client not found")
```

---

## What You'll See in Debug Output

**Before Fix:**
```
LogSystemEvent Error: The INSERT statement conflicted with CHECK constraint...
```

**After Fix:**
```
[DB] GetClientIdByCode - Code: CL001, Result: 1
[SESSION] StartSession - ClientCode: CL001, ClientId: 1, UserId: 2
[SESSION] StartSession - SessionId: 1
(Session starts successfully!)
```

---

## Build Status
✅ All projects build (0 errors)
✅ Ready to use

---

**Total time: 2 minutes**

**Start with: Build → Rebuild Solution**
