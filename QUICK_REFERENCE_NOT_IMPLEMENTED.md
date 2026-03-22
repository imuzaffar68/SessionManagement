# QUICK REFERENCE - What "Method or Operation is Not Implemented" Means

## In WCF Terms

When you get this error:
```
FaultException: The method or operation is not implemented.
```

It means WCF is saying: "I found the method you're trying to call, but the server says it's not implemented."

## Why This Happens

### Scenario 1: Stale .exe (MOST LIKELY)
- You changed the code
- You rebuilt in Visual Studio  
- BUT you didn't restart the SessionServer.exe
- SessionServer is still running old code that doesn't have the methods

**Fix:** Restart SessionServer console with the new build

### Scenario 2: Method Not Marked [OperationContract]
- The method exists in the interface
- But it's not marked with `[OperationContract]` attribute
- WCF hides it from the service

**Check:** All methods in ISessionService.cs should have `[OperationContract]` above them

### Scenario 3: Service Not Properly Hosted
- The ServiceHost didn't properly register the service methods
- Usually happens if there's an error during service startup

**Check:** SessionServer console for startup errors

## The Test

Open a PowerShell and run:
```powershell
netstat -ano | findstr :8001
```

If you see `LISTENING`, the server is running.
If you don't see anything, server crashed or isn't listening.

## The Most Common Fix (95% of cases)

1. Stop SessionServer (Ctrl+C in console)
2. In Visual Studio: Build → Rebuild Solution
3. Start SessionServer again (F5)
4. Test SessionClient again

That's it.

## Why You're Getting This Error NOW

You just had detailed error reporting enabled. Before, you got generic "internal error" messages. Now WCF is telling you exactly what's wrong: "method not implemented".

This is actually GOOD because it tells us the methods exist but aren't accessible. It means you're very close to working code!

## Next Test Steps

1. Follow the fix in CRITICAL_FIX_METHOD_NOT_IMPLEMENTED.md
2. If you still get this error, it means one of scenarios 2 or 3 above
3. If you get a DIFFERENT error, that's progress!
4. Report that new error and we'll fix it
