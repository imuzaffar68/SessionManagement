# Test Credentials for Session Management System

## ⚠️ IMPORTANT: FOR DEVELOPMENT/TESTING ONLY
These credentials are for testing purposes only. **Do NOT use in production.**

---

## User Accounts

### Admin Account
| Field | Value |
|-------|-------|
| **Username** | `admin` |
| **Password** | `Admin@123456` |
| **Role** | Administrator |
| **Full Name** | System Administrator |

### Test User 1
| Field | Value |
|-------|-------|
| **Username** | `user1` |
| **Password** | `User1@123456` |
| **Role** | ClientUser |
| **Full Name** | John Doe |

### Test User 2
| Field | Value |
|-------|-------|
| **Username** | `user2` |
| **Password** | `User2@123456` |
| **Role** | ClientUser |
| **Full Name** | Jane Smith |

### Test User 3
| Field | Value |
|-------|-------|
| **Username** | `user3` |
| **Password** | `User3@123456` |
| **Role** | ClientUser |
| **Full Name** | Bob Johnson |

---

## Client Machines (Predefined)

| Client Code | Machine Name | IP Address | Location |
|------------|--------------|-----------|----------|
| CLIENT001 | WORKSTATION01 | 192.168.1.10 | Floor 1 |
| CLIENT002 | WORKSTATION02 | 192.168.1.11 | Floor 1 |
| CLIENT003 | WORKSTATION03 | 192.168.1.12 | Floor 2 |

---

## Billing Rates (Predefined)

| Name | Rate | Currency | Default |
|------|------|----------|---------|
| Standard Rate | $0.50/min | USD | ✅ Yes |
| Premium Rate | $1.00/min | USD | ❌ No |
| Discount Rate | $0.25/min | USD | ❌ No |

---

## Activity Types (Predefined)

| Type | Severity | Description |
|------|----------|-------------|
| UnauthorizedAccess | High | Attempt to access unauthorized resources |
| SessionExpired | Medium | Session expired due to timeout |
| LoginFailure | Medium | Failed login attempt |
| DataTransfer | Low | Large data transfer detected |
| SystemError | High | System error occurred |
| ConfigChange | Medium | Configuration change detected |

---

## Testing Workflow

### 1. Start SessionServer
```
dotnet SessionServer.csproj
```
Expected output:
```
Session Management Service is running...
Press Enter to stop the service.
```

### 2. Connect from SessionClient
- Use `admin` / `Admin@123456` to log in as administrator
- Use `user1` / `User1@123456` to log in as regular user

### 3. Test Session Creation
1. Log in as user1
2. Select a client machine (CLIENT001, CLIENT002, or CLIENT003)
3. Select session duration (e.g., 60 minutes)
4. Session should activate and billing should start

### 4. Monitor from SessionAdmin
- View active sessions
- View alerts and security logs
- Acknowledge alerts
- View billing records

---

## Password Requirements

Passwords must:
- Be at least 6 characters long
- Contain uppercase and lowercase letters
- Contain numbers and special characters (recommended for production)

### Generating New Password Hashes

If you need to change passwords, use the PasswordHashGenerator utility:

1. Open `SessionManagement.Shared\Security\PasswordHashGenerator.cs`
2. Update the password values in the `passwords` array
3. Run the utility to generate new hashes
4. Copy the hashes into `SessionManagement.sql`
5. Re-run the database setup script

---

## Security Best Practices (Production)

- ✅ Use strong passwords (minimum 12 characters, mixed case, numbers, symbols)
- ✅ Implement password expiration policies
- ✅ Store credentials securely (use Azure Key Vault or similar)
- ✅ Enable multi-factor authentication (MFA)
- ✅ Implement role-based access control (RBAC)
- ✅ Audit all login attempts
- ✅ Use HTTPS/TLS for all communications
- ✅ Never hardcode credentials in source code
- ✅ Implement rate limiting on login attempts
- ✅ Use certificate-based authentication for services

---

## Resetting a User Password

To reset a user password in the database:

```sql
-- Generate new hash using PasswordHashGenerator utility
UPDATE dbo.tblUser 
SET PasswordHash = 'NEW_HASH_HERE'
WHERE Username = 'username';
```

---

## Support

For password-related issues:
1. Check that passwords are entered correctly (case-sensitive)
2. Verify the user account status is 'Active'
3. Check tblLoginAttempt table for failed login logs
4. Review tblSystemLog for authentication errors

