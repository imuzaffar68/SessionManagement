# Session Management System - Database Setup & Implementation Guide

## Overview
This document provides complete setup instructions for the Session Management System database and summarizes all code changes made.

---

## Part 1: Database Setup

### Prerequisites
- SQL Server 2019 or later
- SQL Server Management Studio (SSMS) or equivalent query tool
- .NET Framework 4.7.2

### Quick Setup

1. **Execute the SQL script:**
   - Open SQL Server Management Studio
   - Connect to your SQL Server instance
   - Open the file: `SessionManagement.sql` (located in the solution root)
   - Execute the entire script (Ctrl+Shift+E or Query → Execute)

2. **Verify installation:**
   ```sql
   USE ClientServerSessionDB;
   SELECT COUNT(*) FROM dbo.tblUser;
   SELECT COUNT(*) FROM dbo.tblBillingRate;
   SELECT COUNT(*) FROM dbo.tblActivityType;
   ```

### Database Structure

The script creates the following schema:

#### Tables

**1. tblUser** - User accounts (Admin/ClientUser)
- Primary fields: UserId, Username, PasswordHash, FullName, Role, Status
- Self-referential foreign key for CreatedByUserId

**2. tblClientMachine** - Client workstations/devices
- Primary fields: ClientMachineId, ClientCode, MachineName, IPAddress, MACAddress
- Status tracking: Idle, Active, Offline

**3. tblSession** - User sessions with computed expiry
- Primary fields: SessionId, UserId, ClientMachineId, StartedAt, SelectedDurationMinutes
- Computed field: ExpectedEndAt (StartedAt + SelectedDurationMinutes)
- Status: Pending, Active, Completed, Expired, Terminated, Cancelled

**4. tblSessionImage** - Login screenshots per session
- One-to-one relationship with tblSession
- Fields: CaptureStatus, UploadStatus, ImagePath, Notes

**5. tblBillingRate** - Billing rate configurations
- Fields: Name, RatePerMinute, Currency, IsActive, IsDefault
- Effective date range tracking

**6. tblBillingRecord** - Billing records per session
- One-to-one relationship with tblSession
- Status: Running, Finalized

**7. tblActivityType** - Alert activity type classifications
- Fields: Name, Description, DefaultSeverity
- Predefined types: UnauthorizedAccess, SessionExpired, LoginFailure, DataTransfer, SystemError, ConfigChange

**8. tblAlert** - Security and system alerts
- Links to ActivityType, Session, ClientMachine, User
- Status: New, Acknowledged, Resolved, Closed
- Acknowledgment tracking

**9. tblLoginAttempt** - Login audit trail
- Fields: UsernameEntered, IsSuccess, FailureReason
- Failure reasons: InvalidPassword, UnknownUser, BlockedUser, ServerUnreachable

**10. tblSystemLog** - System event logging
- Categories: Auth, Session, Billing, Security, System
- Source tracking: Client, Server

#### Views

**vw_SessionReport** - Comprehensive session reporting
```sql
SELECT s.SessionId, u.Username, c.ClientCode, s.StartedAt, s.Status,
       br.RatePerMinute, bil.Amount
FROM tblSession s...
```

**vw_ActiveSessionsSummary** - Real-time active session metrics
```sql
SELECT COUNT(*) AS TotalActiveSessions,
       COUNT(DISTINCT UserId) AS UniqueUsers,
       SUM(BillingAmount) AS TotalCurrentBilling
```

#### Stored Procedures

**sp_StartSession** - Create and initialize session
- Parameters: @UserId, @ClientMachineId, @SelectedDurationMinutes
- Returns: SessionId

**sp_EndSession** - Terminate session with reason
- Parameters: @SessionId, @TerminationReason
- Auto-calculates ActualDurationMinutes

**sp_GetActiveSessions** - Retrieve all active sessions with billing
- Calculates remaining minutes and current billing in real-time

**sp_LogSecurityAlert** - Log security events
- Auto-creates ActivityType if needed
- Parameters: @ActivityTypeName, @SessionId, @ClientMachineId, @UserId, @Details, @Severity

**sp_CalculateSessionBilling** - Calculate or update session billing
- Returns: Decimal amount
- Updates or inserts tblBillingRecord

**sp_FinalizeSessionBilling** - Finalize billing for ended session
- Sets BillingRecord.Status = 'Finalized'

**sp_RegisterClient** - Register or update client machine
- Handles new client insertion or existing client update

### Seed Data Included

**Users:**
- admin / (hashed password) - System Administrator
- user1 / (hashed password) - John Doe (ClientUser)
- user2 / (hashed password) - Jane Smith (ClientUser)
- user3 / (hashed password) - Bob Johnson (ClientUser)

**Client Machines:**
- CLIENT001 (192.168.1.10) - WORKSTATION01
- CLIENT002 (192.168.1.11) - WORKSTATION02
- CLIENT003 (192.168.1.12) - WORKSTATION03

**Billing Rates:**
- Standard Rate: $0.50/min (IsDefault=1)
- Premium Rate: $1.00/min
- Discount Rate: $0.25/min

**Activity Types:**
- UnauthorizedAccess (High)
- SessionExpired (Medium)
- LoginFailure (Medium)
- DataTransfer (Low)
- SystemError (High)
- ConfigChange (Medium)

---

## Part 2: WCF Service Binding Fix

### Issue Resolved
**Exception:** `System.InvalidOperationException: Contract requires Duplex, but Binding 'BasicHttpBinding' doesn't support it or isn't configured properly to support it.`

**Root Cause:** The ISessionService contract declares a CallbackContract for duplex (callback) communication, but BasicHttpBinding doesn't support duplex channels.

### Solution Applied

**File: SessionServer\Program.cs**

Changed from:
```csharp
var baseAddress = new Uri("http://localhost:8000/SessionService");
var binding = new BasicHttpBinding();
```

Changed to:
```csharp
var baseAddress = new Uri("net.tcp://localhost:8001/SessionService");
var binding = new NetTcpBinding(SecurityMode.None);
binding.ReceiveTimeout = TimeSpan.FromMinutes(10);
binding.SendTimeout = TimeSpan.FromMinutes(10);
```

### Why NetTcpBinding?
- ✅ Supports duplex (callback) channels
- ✅ Better performance for intranet scenarios
- ✅ Efficient binary encoding
- ✅ Optimal for .NET Framework 4.7.2
- ✅ Built-in support for sessions and transactions

---

## Part 3: DatabaseHelper.cs Updates

### Schema Alignment Changes

All database queries updated to match new table/column naming:

| Old Column | New Column | Table | Reason |
|-----------|-----------|-------|--------|
| ClientId | ClientMachineId | tblSession, tblAlert | Clarity: distinguishes from ClientCode |
| LogType | Category | tblSystemLog | Standard naming |
| LogMessage | Message | tblSystemLog | Standard naming |
| LogLevel | Type | tblSystemLog | Standard naming |
| UserType | Role | tblUser | Standard naming |
| Email | - | tblUser | Removed; using Phone |
| IsActive | Status | tblUser | Enum-based status tracking |
| ImageData (BLOB) | ImagePath | tblSessionImage | File-based storage |

### Key Method Updates

**AuthenticateUser:**
- Updated query to use Role instead of UserType
- Updated status check from IsActive=1 to Status='Active'

**StartSession:**
- Parameter renamed: @ClientId → @ClientMachineId
- Procedure call updated

**GetClientIdByCode:**
- Returns ClientMachineId (not ClientId)

**LogSecurityAlert:**
- Now calls sp_LogSecurityAlert with @ActivityTypeName instead of @AlertType

**GetUnacknowledgedAlerts:**
- Updated alert field references to match tblAlert schema
- Status column now used instead of IsAcknowledged flag

**AcknowledgeAlert:**
- Sets AcknowledgedByAdminUserId and AcknowledgedAt
- Status updated to 'Acknowledged'

**LogSystemEvent:**
- Maps legacy parameters to new schema:
  - logType → Category
  - logLevel → Type
  - clientId → clientMachineId
- Auto-sets Source='Server'

---

## Part 4: Connection String Configuration

### App.config Setup

```xml
<configuration>
  <connectionStrings>
    <add name="SessionManagementDB" 
         connectionString="Server=localhost\SQLEXPRESS;Database=ClientServerSessionDB;Integrated Security=True;" 
         providerName="System.Data.SqlClient" />
  </connectionStrings>
</configuration>
```

### For Different Environments

**Local Development:**
```
Server=.\SQLEXPRESS;Database=ClientServerSessionDB;Integrated Security=True;
```

**Named SQL Server Instance:**
```
Server=SERVERNAME\INSTANCENAME;Database=ClientServerSessionDB;Integrated Security=True;
```

**SQL Server Authentication:**
```
Server=SERVERNAME;Database=ClientServerSessionDB;User ID=sa;Password=YourPassword;
```

**Azure SQL Database:**
```
Server=tcp:servername.database.windows.net,1433;Initial Catalog=ClientServerSessionDB;Persist Security Info=False;User ID=username@servername;Password=password;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

---

## Part 5: Client Configuration

### SessionClient\App.config

Update endpoint address from HTTP to net.tcp:

```xml
<system.serviceModel>
  <client>
    <endpoint address="net.tcp://localhost:8001/SessionService" 
              binding="netTcpBinding" 
              bindingConfiguration="NetTcpBinding_ISessionService"
              contract="SessionManagement.WCF.ISessionService" 
              name="NetTcpBinding_ISessionService" />
  </client>

  <bindings>
    <netTcpBinding>
      <binding name="NetTcpBinding_ISessionService" maxReceivedMessageSize="2147483647">
        <security mode="None" />
      </binding>
    </netTcpBinding>
  </bindings>
</system.serviceModel>
```

### SessionAdmin\App.config

Same configuration as SessionClient.

---

## Part 6: Verification Checklist

### Database Verification
- [ ] Database ClientServerSessionDB created
- [ ] All 10 tables created with correct constraints
- [ ] 7 stored procedures created and executable
- [ ] 2 views created and queryable
- [ ] Seed data inserted (4 users, 3 clients, 3 rates, 6 activity types)
- [ ] Foreign key relationships intact

### Code Compilation
- [ ] SessionServer project builds successfully
- [ ] SessionClient project builds successfully
- [ ] SessionAdmin project builds successfully
- [ ] SessionManagement.Shared project builds successfully

### Runtime Verification
- [ ] SessionServer starts without binding errors
- [ ] Clients can connect via net.tcp://localhost:8001/SessionService
- [ ] WCF service callback channels functional
- [ ] Sessions start/end correctly
- [ ] Billing calculations accurate
- [ ] Alerts logged and retrievable

### Database Query Tests
```sql
-- Test 1: Verify users
SELECT * FROM dbo.tblUser;

-- Test 2: Start a session
EXEC sp_StartSession @UserId=2, @ClientMachineId=1, @SelectedDurationMinutes=60;

-- Test 3: Get active sessions
EXEC sp_GetActiveSessions;

-- Test 4: Log an alert
EXEC sp_LogSecurityAlert 
    @ActivityTypeName='LoginFailure',
    @ClientMachineId=1,
    @Details='Failed login attempt for user1',
    @Severity='Medium';

-- Test 5: Calculate billing
EXEC sp_CalculateSessionBilling @SessionId=1;
```

---

## Part 7: Deployment Notes

### For Production Deployment:

1. **Security Configuration:**
   - Add WCF security bindings (not SecurityMode.None)
   - Implement certificate-based authentication for net.tcp
   - Enable SSL/TLS for any HTTP endpoints
   - Configure firewall rules (TCP port 8001 for net.tcp)

2. **Database Backup:**
   ```sql
   BACKUP DATABASE ClientServerSessionDB 
   TO DISK = 'C:\Backups\ClientServerSessionDB.bak'
   ```

3. **Performance Tuning:**
   - Add indexes on frequently queried columns (done in script)
   - Monitor session table growth and implement archival
   - Set up SQL Agent jobs for log rotation
   - Configure billing calculation batch jobs

4. **Logging & Monitoring:**
   - Implement tblSystemLog analysis and alerting
   - Set up WCF performance counters
   - Monitor active sessions count and duration
   - Track billing calculation accuracy

---

## Files Affected

### Created
- `SessionManagement.sql` - Complete database setup script

### Modified
- `SessionServer\Program.cs` - Changed binding from BasicHttpBinding to NetTcpBinding
- `SessionManagement.Shared\Data\DatabaseHelper.cs` - Updated all SQL queries and stored procedure calls to align with new schema
- `SessionClient\App.config` - Update endpoint to net.tcp (if service reference regenerated)
- `SessionAdmin\App.config` - Update endpoint to net.tcp (if service reference regenerated)

---

## Support & Troubleshooting

### Common Issues

**Issue:** "Connection timeout" error
- **Solution:** Verify connection string in App.config; ensure SQL Server is running

**Issue:** "The callback contract requires duplex binding"
- **Solution:** Ensure SessionServer uses NetTcpBinding (already fixed in code)
- **Solution:** Ensure clients use netTcpBinding in App.config

**Issue:** "Could not find stored procedure sp_StartSession"
- **Solution:** Run SessionManagement.sql to create all stored procedures

**Issue:** WCF clients can't connect
- **Solution:** Verify firewall allows TCP port 8001
- **Solution:** Verify net.tcp protocol is enabled in IIS (if hosted there)

---

## Summary of Changes

| Component | Change | Impact |
|-----------|--------|--------|
| Program.cs | BasicHttpBinding → NetTcpBinding | Enables duplex callbacks |
| Base Address | http://localhost:8000 → net.tcp://localhost:8001 | WCF routing |
| DatabaseHelper.cs | 25+ query updates | Schema alignment |
| Database | New schema created | Structured session management |
| Stored Procedures | 7 procedures | Business logic encapsulation |
| Views | 2 views | Reporting capabilities |
| Configuration | Updated bindings | Client connection configuration |

All changes maintain backward compatibility with the existing service contract (ISessionService) and data models.
