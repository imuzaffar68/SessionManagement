# Test Cases — Task B-3: Session Token Validation
## Changes tested
| File | What changed |
|---|---|
| `SessionService.cs` | `_tokenStore` dict added; `AuthenticateUser` stores token; `ValidateSession` checks dict; `StartSession` requires + consumes token atomically |
| `IsessionService.cs` | `StartSession` contract adds `string sessionToken` parameter |
| `SessionServiceClientBase.cs` | `SessionToken` property added; proxy injects it into every `StartSession` call |
| `SessionClient/MainWindow.xaml.cs` | Token stored after login; `SESSION_TOKEN_EXPIRED` handled; token cleared on logout/termination |

**Setup:** Build in Debug. Run `SessionServer.exe`, then `SessionClient.exe`. A registered ClientUser account must exist.

---

## Group 1 — Happy Path (must pass before all other tests)

### TC-001 Normal login → start → end flow
**Pre-condition:** Server running, ClientUser account active.  
**Steps:**
1. Open SessionClient → login with valid username + password
2. Select any duration preset (e.g. 30 min)
3. Click **Start Session**
4. Verify session panel appears with countdown timer
5. Click **End Session** → confirm dialog → click Yes

**Expected:**
- Step 1: Duration panel appears (no error)
- Step 3: Session panel appears; no "Session Expired" dialog
- Step 5: Summary panel shows elapsed time and amount; login panel restores

**Pass criteria:** Full flow completes without any token-related error message.

---

### TC-002 Custom duration → start → auto end
**Steps:**
1. Login with valid credentials
2. Click **Custom** duration button → type `1` → click Start Session
3. Wait 60 seconds

**Expected:**
- Session starts (token consumed, sessionId active)
- After 60 s: summary panel appears ("Your session time has expired")
- Login panel restores

---

## Group 2 — Token Storage (AuthenticateUser)

### TC-003 Token is generated and stored on each login
**Steps:**
1. Login → reach duration panel
2. Click **Cancel** (back to login)
3. Login again with same credentials
4. Click **Start Session**

**Expected:**
- Session starts successfully on second login
- No "Session Expired" dialog (second login produced a fresh token)

**What this verifies:** `_tokenStore[token] = userId` overwrites the previous token correctly; the new token is valid.

---

### TC-004 Re-login before starting replaces old token
**Steps:**
1. Login (token A stored on server)
2. Without starting a session, cancel back to login panel
3. Login again (token B stored; token A still in dict — bounded leak, acceptable)
4. Start session

**Expected:** Session starts using token B. No error.

---

## Group 3 — Token Validation (StartSession gate)

### TC-005 Null token rejected
> **Note:** This cannot be triggered via the normal UI (the client always sets `SessionToken`). Test via WCF Test Client or by temporarily commenting out `_svc.SessionToken = resp.SessionToken` in `MainWindow.xaml.cs` before this test, then restoring.

**Steps (code-level):**
1. Temporarily remove `_svc.SessionToken = resp.SessionToken` from `btnLogin_Click`
2. Rebuild and run
3. Login → click Start Session

**Expected:**
- "Your login session expired. Please sign in again." dialog appears
- Login panel restores (not a generic error — the specific `SESSION_TOKEN_EXPIRED` branch fires)

**Restore:** Re-add `_svc.SessionToken = resp.SessionToken` after test.

---

### TC-006 Wrong token (forged string) rejected
**Steps (code-level):**
1. In `SessionServiceClientBase.StartSession`, temporarily change `SessionToken ?? string.Empty` to `"FORGED_TOKEN_12345"`
2. Rebuild and run
3. Login → Start Session

**Expected:** "Your login session expired. Please sign in again." dialog. Login panel shown.

**Restore:** Revert the temporary change.

---

### TC-007 Token for different userId rejected
**Steps (code-level):**
1. Add a temp store call in `SessionService.AuthenticateUser` that overwrites with wrong userId:  
   `_tokenStore[token] = userId + 999;`
2. Rebuild and run
3. Login → Start Session

**Expected:** "Your login session expired." dialog (tokenUserId ≠ userId check fails).

**Restore:** Revert.

---

### TC-008 Token is one-shot — replay rejected
**Steps:**
1. Login → Start Session → session starts successfully (token consumed)
2. End session → returns to login panel
3. Without logging in again, trigger `StartSession` manually

> **UI limitation:** The UI blocks Start Session without going through login again. Verify indirectly:
> - After session ends and login panel shows, the **Start Session** button is not visible (duration panel is hidden)
> - Attempting to navigate to duration panel programmatically without re-login would use `_svc.SessionToken = null` (cleared in `ResetToLogin`) → server rejects it

**Expected:** The UI naturally prevents replay because `ResetToLogin` sets `_svc.SessionToken = null` and hides the duration panel.

**What this verifies:** `TryRemove` consumes the token; `ResetToLogin` clears client-side token.

---

## Group 4 — ValidateSession (now checks _tokenStore)

### TC-009 ValidateSession returns true for a valid stored token
**Steps (via SessionAdmin or WCF test):**
1. Login via SessionClient (token stored server-side)
2. Before clicking Start Session, call `ValidateSession(tokenValue)` from SessionAdmin or test harness

**Expected:** Returns `true`.

**Simplified proxy:** In SessionAdmin console, call `_svc.ValidateSession("some_token")` — should return `false` (token not in store). Log in via SessionClient first, capture the token from `AuthenticateUser` response, then call `ValidateSession(capturedToken)` → should return `true`.

---

### TC-010 ValidateSession returns false for unknown token
**Steps:** Call `ValidateSession("random_garbage_string")`.  
**Expected:** Returns `false`.

---

### TC-011 ValidateSession returns false after token is consumed
**Steps:**
1. Login via SessionClient
2. Start Session (token consumed via `TryRemove`)
3. Call `ValidateSession(originalToken)`

**Expected:** Returns `false` (token no longer in `_tokenStore`).

**What this verifies:** `TryRemove` in `StartSession` removes the token before returning success.

---

## Group 5 — Server Restart During Duration Selection

### TC-012 Server restarts while user is on duration panel
**Steps:**
1. Login → duration panel appears (token stored on server)
2. Stop `SessionServer.exe` (Ctrl+C)
3. Wait 3–5 seconds
4. Restart `SessionServer.exe`
5. Wait for client to reconnect (green dot appears)
6. Click **Start Session**

**Expected:**
- "Your login session expired. Please sign in again." dialog
- Login panel restores (not a generic crash/error)
- `_svc.SessionToken` is set to null (no stale token left)

**What this verifies:** `SESSION_TOKEN_EXPIRED` branch in `btnStartSession_Click` handles server restart gracefully.

---

### TC-013 Server restarts mid-session (token already consumed)
**Steps:**
1. Login → Start Session (token consumed)
2. Stop `SessionServer.exe`
3. Verify client shows "⚠ Server offline — your session continues locally"
4. Restart `SessionServer.exe`
5. Wait for reconnect

**Expected:**
- Session timer continues during server downtime
- On reconnect: toast "Server Reconnected" + session validated via `GetSessionInfo`
- No token-related error (token was consumed at start; sessionId takes over)

---

## Group 6 — Token Cleared on Logout / Admin Termination

### TC-014 ResetToLogin clears token
**Steps:**
1. Login → duration panel (token stored in `_svc.SessionToken`)
2. Click Cancel → login panel appears

**Verify (code inspection):**
- `ResetToLogin()` calls `_svc.SessionToken = null`
- Subsequent `StartSession` (if somehow called) would send empty token → server rejects

**Expected:** After cancel, `_svc.SessionToken` is null.  
**Observable effect:** If user clicks Start Session via custom navigation (dev only) → "Session Expired" dialog.

---

### TC-015 Admin terminates session — token cleared on client
**Steps:**
1. Login via SessionClient → Start Session
2. In SessionAdmin → Active Sessions tab → select the session → click **Terminate**
3. Observe SessionClient

**Expected:**
- "Your session was terminated by the administrator." dialog on client
- Summary panel shown, then login panel
- `_svc.SessionToken = null` set in `OnSessionTerminated` callback
- No stale token remains

---

### TC-016 Auto session expiry — token already consumed, no issue
**Steps:**
1. Login → Start Session with 1-minute custom duration
2. Wait for auto-expiry

**Expected:**
- Session ends automatically (timer hits 00:00:00)
- Summary panel shown
- Login panel restores
- No token error (token was consumed at `StartSession`, not needed for `EndSession`)

---

## Group 7 — Admin App Unaffected

### TC-017 Admin login and all admin operations work normally
**Steps:**
1. Run `SessionAdmin.exe`
2. Login as admin
3. Browse Active Sessions, Clients, Users, Billing, Logs tabs
4. Terminate a client session from admin

**Expected:** All admin operations work. `SessionToken` property is `null` for admin (admin never calls `StartSession`). No errors.

**What this verifies:** The `SessionToken` property defaulting to `null` with `SessionToken ?? string.Empty` in the base class proxy means admin never sends a token and never triggers the gate (admin has no `StartSession` in its UI flow).

---

## Group 8 — Edge Cases

### TC-018 Two users logged in on different machines simultaneously
**Steps:**
1. Run two `SessionClient.exe` instances (if testing on same machine, change `ClientCode` in App.config for the second)
2. Login with User A on instance 1
3. Login with User B on instance 2
4. Start session on both

**Expected:** Both sessions start independently. Tokens are keyed by token string (unique 256-bit random), not by userId, so no collision.

---

### TC-019 Same user logs in twice simultaneously (two machines)
**Steps:**
1. Login with UserA on Machine 1 (token A in `_tokenStore`)
2. Login with UserA on Machine 2 (token B in `_tokenStore`; `_tokenStore[tokenB] = userId`)
3. Start session on Machine 1
4. Start session on Machine 2

**Expected:**
- Machine 1 session starts successfully (token A consumed)
- Machine 2: `sp_StartSession` returns `-1` (user already has an active session) → "You already have an active session on another machine."
- Token B is consumed on attempt (TryRemove fires) — this is intentional; user must re-login if they cancel the error

---

## Test Execution Checklist

| TC | Description | Result | Notes |
|---|---|---|---|
| TC-001 | Normal login → start → end | ☐ Pass / ☐ Fail | |
| TC-002 | Custom 1-min → auto-end | ☐ Pass / ☐ Fail | |
| TC-003 | Token generated each login | ☐ Pass / ☐ Fail | |
| TC-004 | Re-login replaces token | ☐ Pass / ☐ Fail | |
| TC-005 | Null token rejected | ☐ Pass / ☐ Fail | Code mod needed |
| TC-006 | Forged token rejected | ☐ Pass / ☐ Fail | Code mod needed |
| TC-007 | Wrong userId rejected | ☐ Pass / ☐ Fail | Code mod needed |
| TC-008 | One-shot — replay rejected | ☐ Pass / ☐ Fail | UI-level only |
| TC-009 | ValidateSession true for valid token | ☐ Pass / ☐ Fail | |
| TC-010 | ValidateSession false for unknown | ☐ Pass / ☐ Fail | |
| TC-011 | ValidateSession false after consumed | ☐ Pass / ☐ Fail | |
| TC-012 | Server restart during duration selection | ☐ Pass / ☐ Fail | |
| TC-013 | Server restart mid-session | ☐ Pass / ☐ Fail | |
| TC-014 | ResetToLogin clears token | ☐ Pass / ☐ Fail | Code inspection |
| TC-015 | Admin terminate → token cleared | ☐ Pass / ☐ Fail | |
| TC-016 | Auto-expiry → no token error | ☐ Pass / ☐ Fail | |
| TC-017 | Admin app unaffected | ☐ Pass / ☐ Fail | |
| TC-018 | Two different users simultaneously | ☐ Pass / ☐ Fail | |
| TC-019 | Same user on two machines | ☐ Pass / ☐ Fail | |

**Critical path (must pass):** TC-001, TC-002, TC-012, TC-015, TC-017  
**Security path (token gate):** TC-005, TC-006, TC-007, TC-008, TC-011
