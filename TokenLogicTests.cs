// Standalone test runner for Task B-3 token validation logic.
// Tests the pure in-memory token dictionary operations extracted from SessionService.
// No WCF, no DB, no config needed — compiles and runs with csc.exe alone.
//
// Run:  csc TokenLogicTests.cs && TokenLogicTests.exe

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

class TokenLogicTests
{
    // ── Extracted logic mirroring SessionService ──────────────────────────────

    static readonly ConcurrentDictionary<string, int> _tokenStore =
        new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

    static string GenerateToken()
    {
        byte[] b = new byte[32];
        using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
        return Convert.ToBase64String(b);
    }

    // Mirrors AuthenticateUser token storage
    static string StoreToken(int userId)
    {
        string token = GenerateToken();
        _tokenStore[token] = userId;
        return token;
    }

    // Mirrors ValidateSession
    static bool ValidateSession(string token)
        => !string.IsNullOrWhiteSpace(token) && _tokenStore.ContainsKey(token);

    // Mirrors StartSession token gate — returns null on success, error string on failure
    static string ConsumeToken(string token, int userId)
    {
        if (!_tokenStore.TryRemove(token ?? string.Empty, out int tokenUserId)
            || tokenUserId != userId)
            return "SESSION_TOKEN_EXPIRED";
        return null;  // success
    }

    // ── Test harness ──────────────────────────────────────────────────────────

    static int _pass = 0, _fail = 0;

    static void Assert(string tcId, string description, bool condition)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [PASS] {tcId}: {description}");
            _pass++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] {tcId}: {description}");
            _fail++;
        }
        Console.ResetColor();
    }

    static void Header(string group)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n── {group} ──────────────────────────────────────────");
        Console.ResetColor();
    }

    static void Main()
    {
        Console.WriteLine("Task B-3 Token Validation — Unit Tests");
        Console.WriteLine("========================================");

        // ── Group 2: Token Storage ────────────────────────────────────────────
        Header("Group 2: Token Storage (AuthenticateUser)");

        _tokenStore.Clear();
        string token1 = StoreToken(userId: 42);
        Assert("TC-003", "Token stored in dict after auth", _tokenStore.ContainsKey(token1));
        Assert("TC-003", "Token maps to correct userId", _tokenStore[token1] == 42);

        // TC-004: Re-login produces new token; old token stays (bounded leak)
        string token2 = StoreToken(userId: 42);
        Assert("TC-004", "Second login produces different token", token1 != token2);
        Assert("TC-004", "Both tokens present (bounded leak until server restart)",
            _tokenStore.ContainsKey(token1) && _tokenStore.ContainsKey(token2));

        // Re-auth with same user overwrites if same token unlikely but possible
        _tokenStore.Clear();
        string token3 = StoreToken(userId: 99);
        Assert("TC-003b", "Token for userId 99 stored correctly", _tokenStore[token3] == 99);

        // ── Group 3: Token Validation (StartSession gate) ─────────────────────
        Header("Group 3: Token Validation (StartSession gate)");

        _tokenStore.Clear();
        string validToken = StoreToken(userId: 5);

        // TC-005: Null token rejected
        Assert("TC-005", "Null token → SESSION_TOKEN_EXPIRED",
            ConsumeToken(null, 5) == "SESSION_TOKEN_EXPIRED");

        // TC-006: Wrong/forged token rejected
        Assert("TC-006", "Forged token → SESSION_TOKEN_EXPIRED",
            ConsumeToken("FORGED_TOKEN_12345", 5) == "SESSION_TOKEN_EXPIRED");

        // TC-007: Correct token but wrong userId rejected
        Assert("TC-007", "Token with wrong userId → SESSION_TOKEN_EXPIRED",
            ConsumeToken(validToken, userId: 999) == "SESSION_TOKEN_EXPIRED");

        // After wrong userId attempt, token should still be gone (TryRemove consumed it)
        Assert("TC-007b", "Token removed even on userId mismatch (no replay possible)",
            !_tokenStore.ContainsKey(validToken));

        // TC-008: Valid token consumed successfully
        _tokenStore.Clear();
        string oneShot = StoreToken(userId: 7);
        Assert("TC-008a", "Valid token consumed → no error", ConsumeToken(oneShot, 7) == null);

        // TC-008: Replay attempt fails
        Assert("TC-008b", "Replay of consumed token → SESSION_TOKEN_EXPIRED",
            ConsumeToken(oneShot, 7) == "SESSION_TOKEN_EXPIRED");

        // TC-008c: Empty string rejected
        Assert("TC-008c", "Empty string → SESSION_TOKEN_EXPIRED",
            ConsumeToken(string.Empty, 7) == "SESSION_TOKEN_EXPIRED");

        // ── Group 4: ValidateSession ──────────────────────────────────────────
        Header("Group 4: ValidateSession (now checks _tokenStore)");

        _tokenStore.Clear();
        string vsToken = StoreToken(userId: 11);

        // TC-009: Valid stored token returns true
        Assert("TC-009", "ValidateSession(valid token) → true", ValidateSession(vsToken));

        // TC-010: Unknown token returns false
        Assert("TC-010", "ValidateSession(garbage) → false", !ValidateSession("garbage_xyz"));

        // TC-010b: Null returns false
        Assert("TC-010b", "ValidateSession(null) → false", !ValidateSession(null));

        // TC-010c: Empty returns false
        Assert("TC-010c", "ValidateSession(empty) → false", !ValidateSession(string.Empty));

        // TC-011: ValidateSession false after token consumed
        ConsumeToken(vsToken, 11);  // consume it
        Assert("TC-011", "ValidateSession after TryRemove → false", !ValidateSession(vsToken));

        // ── Group 5: Server Restart Simulation ───────────────────────────────
        Header("Group 5: Server Restart Simulation");

        _tokenStore.Clear();
        string preRestartToken = StoreToken(userId: 20);

        // Simulate server restart — clear the dict (all in-memory tokens lost)
        _tokenStore.Clear();

        // Client still has the old token and tries to start a session
        Assert("TC-012", "Token from before restart → SESSION_TOKEN_EXPIRED after dict clear",
            ConsumeToken(preRestartToken, 20) == "SESSION_TOKEN_EXPIRED");

        // After getting expired error, user re-authenticates
        string postRestartToken = StoreToken(userId: 20);
        Assert("TC-012b", "New token after re-login works fine",
            ConsumeToken(postRestartToken, 20) == null);

        // ── Group 7: Multiple Users ───────────────────────────────────────────
        Header("Group 7: Multiple Users (TC-018 / TC-019 logic)");

        _tokenStore.Clear();
        string tokenA = StoreToken(userId: 100);  // User A on Machine 1
        string tokenB = StoreToken(userId: 200);  // User B on Machine 2

        Assert("TC-018a", "Token A (userId 100) stored correctly", _tokenStore[tokenA] == 100);
        Assert("TC-018b", "Token B (userId 200) stored correctly", _tokenStore[tokenB] == 200);
        Assert("TC-018c", "Token A and B are different strings", tokenA != tokenB);

        // User A starts session (consumes token A)
        Assert("TC-018d", "User A StartSession consumes token A", ConsumeToken(tokenA, 100) == null);
        // User B starts session (consumes token B) — independent, not affected
        Assert("TC-018e", "User B StartSession consumes token B independently", ConsumeToken(tokenB, 200) == null);

        // TC-019: Same user on two machines — second machine token still valid after first consumed
        _tokenStore.Clear();
        string machineToken1 = StoreToken(userId: 50);  // Machine 1 login
        string machineToken2 = StoreToken(userId: 50);  // Machine 2 login (same user)

        ConsumeToken(machineToken1, 50);  // Machine 1 starts session
        // Machine 2 token still exists — StartSession would succeed here at token level
        // (The DB sp_StartSession returns -1 for user conflict — tested at integration level)
        Assert("TC-019", "Machine 2 token still valid after Machine 1 consumed its own",
            _tokenStore.ContainsKey(machineToken2));

        // ── Results ───────────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════");
        int total = _pass + _fail;
        if (_fail == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"ALL {total} TESTS PASSED");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{_fail} of {total} TESTS FAILED");
        }
        Console.ResetColor();
        Console.WriteLine("════════════════════════════════════════");
        Console.WriteLine();

        // Tests requiring live apps (UI/integration) — documented as manual
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("MANUAL tests (require running SessionServer + SessionClient):");
        Console.WriteLine("  TC-001 Normal login → start → end flow");
        Console.WriteLine("  TC-002 Custom 1-min duration → auto-end");
        Console.WriteLine("  TC-003 Token generated each login (observable via duration panel)");
        Console.WriteLine("  TC-012 Server restart during duration selection → re-login dialog");
        Console.WriteLine("  TC-013 Server restart mid-session → orphan billing");
        Console.WriteLine("  TC-014 Cancel → ResetToLogin clears token");
        Console.WriteLine("  TC-015 Admin terminates session → token cleared on client");
        Console.WriteLine("  TC-016 Auto-expiry → no token error");
        Console.WriteLine("  TC-017 Admin app unaffected");
        Console.ResetColor();
    }
}
