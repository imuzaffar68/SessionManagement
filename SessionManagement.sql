/* ============================================================
   Session Management Database - Complete Setup Script
   SQL Server 2019 Compatible
   ============================================================ */

-- Create database if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ClientServerSessionDB')
BEGIN
    CREATE DATABASE ClientServerSessionDB;
END
GO

USE ClientServerSessionDB;
GO

/* ============================================================
   PART 1: DROP EXISTING OBJECTS (if any)
   ============================================================ */

-- Drop stored procedures
IF OBJECT_ID('sp_StartSession', 'P') IS NOT NULL DROP PROCEDURE sp_StartSession;
GO
IF OBJECT_ID('sp_EndSession', 'P') IS NOT NULL DROP PROCEDURE sp_EndSession;
GO
IF OBJECT_ID('sp_GetActiveSessions', 'P') IS NOT NULL DROP PROCEDURE sp_GetActiveSessions;
GO
IF OBJECT_ID('sp_LogSecurityAlert', 'P') IS NOT NULL DROP PROCEDURE sp_LogSecurityAlert;
GO
IF OBJECT_ID('sp_CalculateSessionBilling', 'P') IS NOT NULL DROP PROCEDURE sp_CalculateSessionBilling;
GO
IF OBJECT_ID('sp_FinalizeSessionBilling', 'P') IS NOT NULL DROP PROCEDURE sp_FinalizeSessionBilling;
GO
IF OBJECT_ID('sp_RegisterClient', 'P') IS NOT NULL DROP PROCEDURE sp_RegisterClient;
GO

-- Drop views
IF OBJECT_ID('vw_SessionReport', 'V') IS NOT NULL DROP VIEW vw_SessionReport;
GO
IF OBJECT_ID('vw_ActiveSessionsSummary', 'V') IS NOT NULL DROP VIEW vw_ActiveSessionsSummary;
GO

-- Drop tables in reverse order (respecting foreign keys)
IF OBJECT_ID('dbo.tblSystemLog', 'U') IS NOT NULL DROP TABLE dbo.tblSystemLog;
GO
IF OBJECT_ID('dbo.tblLoginAttempt', 'U') IS NOT NULL DROP TABLE dbo.tblLoginAttempt;
GO
IF OBJECT_ID('dbo.tblAlert', 'U') IS NOT NULL DROP TABLE dbo.tblAlert;
GO
IF OBJECT_ID('dbo.tblActivityType', 'U') IS NOT NULL DROP TABLE dbo.tblActivityType;
GO
IF OBJECT_ID('dbo.tblBillingRecord', 'U') IS NOT NULL DROP TABLE dbo.tblBillingRecord;
GO
IF OBJECT_ID('dbo.tblBillingRate', 'U') IS NOT NULL DROP TABLE dbo.tblBillingRate;
GO
IF OBJECT_ID('dbo.tblSessionImage', 'U') IS NOT NULL DROP TABLE dbo.tblSessionImage;
GO
IF OBJECT_ID('dbo.tblSession', 'U') IS NOT NULL DROP TABLE dbo.tblSession;
GO
IF OBJECT_ID('dbo.tblClientMachine', 'U') IS NOT NULL DROP TABLE dbo.tblClientMachine;
GO
IF OBJECT_ID('dbo.tblUser', 'U') IS NOT NULL DROP TABLE dbo.tblUser;
GO

/* ============================================================
   PART 2: CREATE TABLES
   ============================================================ */

-- 1) tblUser
CREATE TABLE dbo.tblUser (
    UserId            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Username          NVARCHAR(50) NOT NULL,
    PasswordHash      NVARCHAR(255) NOT NULL,
    FullName          NVARCHAR(100) NULL,
    Role              NVARCHAR(20) NOT NULL,  -- Admin / ClientUser
    Status            NVARCHAR(20) NOT NULL,  -- Active / Blocked / Disabled
    Phone             NVARCHAR(30) NULL,
    Address           NVARCHAR(200) NULL,
    CreatedByUserId   INT NULL,
    CreatedAt         DATETIME NOT NULL CONSTRAINT DF_tblUser_CreatedAt DEFAULT (GETDATE()),
    LastLoginAt       DATETIME NULL,
    CONSTRAINT UQ_tblUser_Username UNIQUE (Username),
    CONSTRAINT CK_tblUser_Role CHECK (Role IN ('Admin','ClientUser')),
    CONSTRAINT CK_tblUser_Status CHECK (Status IN ('Active','Blocked','Disabled'))
);
GO

ALTER TABLE dbo.tblUser
ADD CONSTRAINT FK_tblUser_CreatedByUser
FOREIGN KEY (CreatedByUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- 2) tblClientMachine
CREATE TABLE dbo.tblClientMachine (
    ClientMachineId   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClientCode        NVARCHAR(50) NOT NULL,
    MachineName       NVARCHAR(50) NOT NULL,
    IPAddress         NVARCHAR(45) NOT NULL,
    MACAddress        NVARCHAR(50) NULL,
    Location          NVARCHAR(100) NULL,
    Status            NVARCHAR(20) NOT NULL,  -- Idle / Active / Offline
    LastSeenAt        DATETIME NOT NULL CONSTRAINT DF_tblClientMachine_LastSeenAt DEFAULT (GETDATE()),
    IsActive          BIT NOT NULL CONSTRAINT DF_tblClientMachine_IsActive DEFAULT (1),
    CONSTRAINT UQ_tblClientMachine_IP UNIQUE (IPAddress),
    CONSTRAINT UQ_tblClientMachine_ClientCode UNIQUE (ClientCode),
    CONSTRAINT CK_tblClientMachine_Status CHECK (Status IN ('Idle','Active','Offline'))
);
GO

CREATE INDEX IX_tblClientMachine_ClientCode ON dbo.tblClientMachine(ClientCode);
GO

-- 3) tblSession
CREATE TABLE dbo.tblSession (
    SessionId                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId                   INT NOT NULL,
    ClientMachineId          INT NOT NULL,
    LoginAt                  DATETIME NOT NULL CONSTRAINT DF_tblSession_LoginAt DEFAULT (GETDATE()),
    StartedAt                DATETIME NOT NULL CONSTRAINT DF_tblSession_StartTime DEFAULT (GETDATE()),
    SelectedDurationMinutes  INT NOT NULL,
    ExpectedEndAt            AS (DATEADD(MINUTE, SelectedDurationMinutes, StartedAt)),
    EndedAt                  DATETIME NULL,
    ActualDurationMinutes    INT NULL,
    Status                   NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    TerminationReason        NVARCHAR(30) NULL,
    CONSTRAINT CK_tblSession_TerminationReason CHECK (
        TerminationReason IS NULL OR TerminationReason IN ('AutoExpiry','AdminTerminate','UserLogout','SystemError','Crash')
    ),
    CONSTRAINT CK_tblSession_Status CHECK (
        Status IN ('Pending','Active','Completed','Expired','Terminated','Cancelled')
    )
);
GO

ALTER TABLE dbo.tblSession
ADD CONSTRAINT FK_tblSession_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSession
ADD CONSTRAINT FK_tblSession_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblSession_UserId ON dbo.tblSession(UserId);
CREATE INDEX IX_tblSession_ClientMachineId ON dbo.tblSession(ClientMachineId);
CREATE INDEX IX_tblSession_Status ON dbo.tblSession(Status);
GO

-- 4) tblSessionImage
CREATE TABLE dbo.tblSessionImage (
    SessionImageId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId          INT NOT NULL,
    CapturedAt         DATETIME NOT NULL CONSTRAINT DF_tblSessionImage_CapturedAt DEFAULT (GETDATE()),
    CaptureStatus      NVARCHAR(30) NOT NULL, -- Captured/CameraUnavailable/Skipped/Failed
    UploadStatus       NVARCHAR(20) NOT NULL, -- Sent/Pending/Failed
    ImagePath          NVARCHAR(500) NOT NULL,
    Notes              NVARCHAR(500) NULL,
    CONSTRAINT CK_tblSessionImage_CaptureStatus CHECK (
        CaptureStatus IN ('Captured','CameraUnavailable','Skipped','Failed')
    ),
    CONSTRAINT CK_tblSessionImage_UploadStatus CHECK (
        UploadStatus IN ('Sent','Pending','Failed')
    ),
    CONSTRAINT UQ_tblSessionImage_Session UNIQUE (SessionId)
);
GO

ALTER TABLE dbo.tblSessionImage
ADD CONSTRAINT FK_tblSessionImage_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- 5) tblBillingRate
CREATE TABLE dbo.tblBillingRate (
    BillingRateId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name               NVARCHAR(100) NOT NULL,
    RatePerMinute      DECIMAL(10,2) NOT NULL,
    Currency           NVARCHAR(10) NOT NULL,
    EffectiveFrom      DATE NULL,
    EffectiveTo        DATE NULL,
    IsActive           BIT NOT NULL CONSTRAINT DF_tblBillingRate_IsActive DEFAULT (1),
    IsDefault          BIT NOT NULL CONSTRAINT DF_tblBillingRate_IsDefault DEFAULT (0),
    SetByAdminUserId   INT NULL,
    Notes              NVARCHAR(500) NULL,
    CreatedAt          DATETIME NOT NULL CONSTRAINT DF_tblBillingRate_CreatedAt DEFAULT (GETDATE())
);
GO

ALTER TABLE dbo.tblBillingRate
ADD CONSTRAINT FK_tblBillingRate_SetByAdmin
FOREIGN KEY (SetByAdminUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

-- 6) tblBillingRecord
CREATE TABLE dbo.tblBillingRecord (
    BillingRecordId    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId          INT NOT NULL,
    BillingRateId      INT NOT NULL,
    BillableMinutes    INT NOT NULL,
    Amount             DECIMAL(10,2) NOT NULL,
    CalculatedAt       DATETIME NOT NULL CONSTRAINT DF_tblBillingRecord_CalculatedAt DEFAULT (GETDATE()),
    Status             NVARCHAR(20) NOT NULL DEFAULT 'Running',
    Remarks            NVARCHAR(500) NULL,
    CONSTRAINT CK_tblBillingRecord_Status CHECK (Status IN ('Finalized', 'Running')),
    CONSTRAINT UQ_tblBillingRecord_Session UNIQUE (SessionId)
);
GO

ALTER TABLE dbo.tblBillingRecord
ADD CONSTRAINT FK_tblBillingRecord_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE CASCADE ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblBillingRecord
ADD CONSTRAINT FK_tblBillingRecord_tblBillingRate
FOREIGN KEY (BillingRateId) REFERENCES dbo.tblBillingRate(BillingRateId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblBillingRecord_BillingRateId ON dbo.tblBillingRecord(BillingRateId);
GO

-- 7) tblActivityType
CREATE TABLE dbo.tblActivityType (
    ActivityTypeId        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name                  NVARCHAR(100) NOT NULL,
    Description           NVARCHAR(500) NULL,
    DefaultSeverity       NVARCHAR(10) NOT NULL,
    IsActive              BIT NOT NULL CONSTRAINT DF_tblActivityType_IsActive DEFAULT (1),
    CONSTRAINT CK_tblActivityType_Severity CHECK (DefaultSeverity IN ('Low','Medium','High'))
);
GO

-- 8) tblAlert
CREATE TABLE dbo.tblAlert (
    AlertId                 INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ActivityTypeId          INT NOT NULL,
    SessionId               INT NULL,
    ClientMachineId         INT NOT NULL,
    UserId                  INT NULL,
    DetectedAt              DATETIME NOT NULL CONSTRAINT DF_tblAlert_DetectedAt DEFAULT (GETDATE()),
    Severity                NVARCHAR(10) NOT NULL,
    Status                  NVARCHAR(20) NOT NULL DEFAULT 'New',
    Details                 NVARCHAR(1000) NOT NULL,
    IsNotifiedToAdmin       BIT NOT NULL CONSTRAINT DF_tblAlert_IsNotified DEFAULT (0),
    IsAcknowledged          BIT NOT NULL CONSTRAINT DF_tblAlert_IsAcknowledged DEFAULT (0),
    AcknowledgedByAdminUserId INT NULL,
    AcknowledgedAt          DATETIME NULL,
    CONSTRAINT CK_tblAlert_Severity CHECK (Severity IN ('Low','Medium','High')),
    CONSTRAINT CK_tblAlert_Status CHECK (Status IN ('New','Acknowledged','Resolved','Closed'))
);
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblActivityType
FOREIGN KEY (ActivityTypeId) REFERENCES dbo.tblActivityType(ActivityTypeId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_AckAdmin
FOREIGN KEY (AcknowledgedByAdminUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblAlert_SessionId ON dbo.tblAlert(SessionId);
CREATE INDEX IX_tblAlert_ClientMachineId ON dbo.tblAlert(ClientMachineId);
CREATE INDEX IX_tblAlert_ActivityTypeId ON dbo.tblAlert(ActivityTypeId);
GO

-- 9) tblLoginAttempt
CREATE TABLE dbo.tblLoginAttempt (
    LoginAttemptId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClientMachineId     INT NOT NULL,
    UserId              INT NULL,
    UsernameEntered     NVARCHAR(50) NOT NULL,
    AttemptedAt         DATETIME NOT NULL CONSTRAINT DF_tblLoginAttempt_AttemptedAt DEFAULT (GETDATE()),
    IsSuccess           BIT NOT NULL,
    FailureReason       NVARCHAR(30) NULL
);
GO

ALTER TABLE dbo.tblLoginAttempt
ADD CONSTRAINT FK_tblLoginAttempt_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblLoginAttempt
ADD CONSTRAINT FK_tblLoginAttempt_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblLoginAttempt_ClientMachineId ON dbo.tblLoginAttempt(ClientMachineId);
CREATE INDEX IX_tblLoginAttempt_UserId ON dbo.tblLoginAttempt(UserId);
GO

-- 10) tblSystemLog
CREATE TABLE dbo.tblSystemLog (
    SystemLogId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    LogedAt           DATETIME NOT NULL CONSTRAINT DF_tblSystemLog_LogedAt DEFAULT (GETDATE()),
    Category          NVARCHAR(20) NOT NULL,
    Type              NVARCHAR(50) NOT NULL,
    Message           NVARCHAR(2000) NOT NULL,
    Source            NVARCHAR(10) NULL,
    SessionId         INT NULL,
    UserId            INT NULL,
    ClientMachineId   INT NULL,
    AdminUserId       INT NULL,
    CONSTRAINT CK_tblSystemLog_Category CHECK (Category IN ('Auth','Session','Billing','Security','System')),
    CONSTRAINT CK_tblSystemLog_Source CHECK (Source IS NULL OR Source IN ('Client','Server'))
);
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE SET NULL ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_AdminUser
FOREIGN KEY (AdminUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblSystemLog_SessionId ON dbo.tblSystemLog(SessionId);
CREATE INDEX IX_tblSystemLog_UserId ON dbo.tblSystemLog(UserId);
CREATE INDEX IX_tblSystemLog_ClientMachineId ON dbo.tblSystemLog(ClientMachineId);
GO

/* ============================================================
   PART 3: CREATE STORED PROCEDURES
   ============================================================ */

-- sp_StartSession
CREATE PROCEDURE sp_StartSession
    @UserId INT,
    @ClientMachineId INT,
    @SelectedDurationMinutes INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SessionId INT;

    BEGIN TRY
        INSERT INTO dbo.tblSession 
        (UserId, ClientMachineId, LoginAt, StartedAt, SelectedDurationMinutes, Status)
        VALUES 
        (@UserId, @ClientMachineId, GETDATE(), GETDATE(), @SelectedDurationMinutes, 'Active');

        SET @SessionId = SCOPE_IDENTITY();

        -- Log system event
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES 
        ('Session', 'StartSession', 'Session started for ' + CAST(@SelectedDurationMinutes AS NVARCHAR(10)) + ' minutes', 
         'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT @SessionId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source)
        VALUES 
        ('System', 'Error', 'Error in sp_StartSession: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0;
    END CATCH
END;
GO

-- sp_EndSession
CREATE PROCEDURE sp_EndSession
    @SessionId INT,
    @TerminationReason NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId INT, @ClientMachineId INT;

    BEGIN TRY
        SELECT @UserId = UserId, @ClientMachineId = ClientMachineId 
        FROM dbo.tblSession 
        WHERE SessionId = @SessionId;

        UPDATE dbo.tblSession 
        SET EndedAt = GETDATE(), 
            Status = 'Terminated', 
            TerminationReason = @TerminationReason,
            ActualDurationMinutes = DATEDIFF(MINUTE, StartedAt, GETDATE())
        WHERE SessionId = @SessionId;

        -- Log system event
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES 
        ('Session', 'EndSession', 'Session terminated - Reason: ' + @TerminationReason, 
         'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT 1;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source)
        VALUES 
        ('System', 'Error', 'Error in sp_EndSession: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0;
    END CATCH
END;
GO

-- sp_GetActiveSessions
CREATE PROCEDURE sp_GetActiveSessions
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        s.SessionId,
        s.UserId,
        u.Username,
        u.FullName,
        s.ClientMachineId,
        c.ClientCode,
        c.MachineName,
        s.StartedAt,
        s.SelectedDurationMinutes,
        s.ExpectedEndAt,
        s.Status,
        DATEDIFF(MINUTE, GETDATE(), s.ExpectedEndAt) AS RemainingMinutes,
        br.BillingRateId,
        br.RatePerMinute,
        DATEDIFF(MINUTE, s.StartedAt, GETDATE()) AS ElapsedMinutes,
        CAST(DATEDIFF(MINUTE, s.StartedAt, GETDATE()) * br.RatePerMinute AS DECIMAL(10,2)) AS CurrentBilling
    FROM dbo.tblSession s
    INNER JOIN dbo.tblUser u ON s.UserId = u.UserId
    INNER JOIN dbo.tblClientMachine c ON s.ClientMachineId = c.ClientMachineId
    LEFT JOIN dbo.tblBillingRate br ON br.IsActive = 1 AND br.IsDefault = 1
    WHERE s.Status = 'Active'
    ORDER BY s.StartedAt DESC;
END;
GO

-- sp_LogSecurityAlert
CREATE PROCEDURE sp_LogSecurityAlert
    @ActivityTypeName NVARCHAR(100),
    @SessionId INT = NULL,
    @ClientMachineId INT = NULL,
    @UserId INT = NULL,
    @Details NVARCHAR(1000),
    @Severity NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ActivityTypeId INT;

    BEGIN TRY
        -- Get or create activity type
        SELECT @ActivityTypeId = ActivityTypeId 
        FROM dbo.tblActivityType 
        WHERE Name = @ActivityTypeName;

        IF @ActivityTypeId IS NULL
        BEGIN
            INSERT INTO dbo.tblActivityType (Name, DefaultSeverity, IsActive)
            VALUES (@ActivityTypeName, @Severity, 1);
            SET @ActivityTypeId = SCOPE_IDENTITY();
        END

        -- Insert alert
        INSERT INTO dbo.tblAlert 
        (ActivityTypeId, SessionId, ClientMachineId, UserId, DetectedAt, Severity, Status, Details)
        VALUES 
        (@ActivityTypeId, @SessionId, @ClientMachineId, @UserId, GETDATE(), @Severity, 'New', @Details);

        -- Log system event
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES 
        ('Security', 'Alert', 'Security Alert: ' + @ActivityTypeName + ' - ' + @Details, 
         'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT 1;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source)
        VALUES 
        ('System', 'Error', 'Error in sp_LogSecurityAlert: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0;
    END CATCH
END;
GO

-- sp_CalculateSessionBilling
CREATE PROCEDURE sp_CalculateSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BillingRateId INT, @RatePerMinute DECIMAL(10,2), @ElapsedMinutes INT, @Amount DECIMAL(10,2);

    BEGIN TRY
        -- Get session details
        SELECT @ElapsedMinutes = DATEDIFF(MINUTE, StartedAt, GETDATE())
        FROM dbo.tblSession 
        WHERE SessionId = @SessionId;

        -- Get active billing rate
        SELECT @BillingRateId = BillingRateId, @RatePerMinute = RatePerMinute
        FROM dbo.tblBillingRate 
        WHERE IsActive = 1 AND IsDefault = 1;

        IF @BillingRateId IS NULL
        BEGIN
            SELECT @BillingRateId = BillingRateId, @RatePerMinute = RatePerMinute
            FROM dbo.tblBillingRate 
            WHERE IsActive = 1
            ORDER BY CreatedAt DESC;
        END

        SET @Amount = @ElapsedMinutes * @RatePerMinute;

        -- Update or insert billing record
        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE SessionId = @SessionId)
        BEGIN
            UPDATE dbo.tblBillingRecord 
            SET BillableMinutes = @ElapsedMinutes, Amount = @Amount
            WHERE SessionId = @SessionId;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tblBillingRecord 
            (SessionId, BillingRateId, BillableMinutes, Amount, Status)
            VALUES 
            (@SessionId, @BillingRateId, @ElapsedMinutes, @Amount, 'Running');
        END

        SELECT @Amount;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source, SessionId)
        VALUES 
        ('Billing', 'Error', 'Error in sp_CalculateSessionBilling: ' + ERROR_MESSAGE(), 'Server', @SessionId);

        SELECT 0;
    END CATCH
END;
GO

-- sp_FinalizeSessionBilling
CREATE PROCEDURE sp_FinalizeSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        UPDATE dbo.tblBillingRecord 
        SET Status = 'Finalized'
        WHERE SessionId = @SessionId;

        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source, SessionId)
        VALUES 
        ('Billing', 'BillingFinalized', 'Session billing finalized', 'Server', @SessionId);

        SELECT 1;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source, SessionId)
        VALUES 
        ('System', 'Error', 'Error in sp_FinalizeSessionBilling: ' + ERROR_MESSAGE(), 'Server', @SessionId);

        SELECT 0;
    END CATCH
END;
GO

-- sp_RegisterClient
CREATE PROCEDURE sp_RegisterClient
    @ClientCode NVARCHAR(50),
    @MachineName NVARCHAR(50),
    @IPAddress NVARCHAR(45),
    @MACAddress NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        -- Check if client already exists
        IF EXISTS (SELECT 1 FROM dbo.tblClientMachine WHERE ClientCode = @ClientCode)
        BEGIN
            UPDATE dbo.tblClientMachine 
            SET Status = 'Idle', LastSeenAt = GETDATE()
            WHERE ClientCode = @ClientCode;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tblClientMachine 
            (ClientCode, MachineName, IPAddress, MACAddress, Status, IsActive)
            VALUES 
            (@ClientCode, @MachineName, @IPAddress, @MACAddress, 'Idle', 1);
        END

        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source)
        VALUES 
        ('System', 'ClientRegistration', 'Client ' + @ClientCode + ' registered/updated', 'Server');

        SELECT 1;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog 
        (Category, Type, Message, Source)
        VALUES 
        ('System', 'Error', 'Error in sp_RegisterClient: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0;
    END CATCH
END;
GO

/* ============================================================
   PART 4: CREATE VIEWS
   ============================================================ */

-- vw_SessionReport
CREATE VIEW vw_SessionReport AS
SELECT 
    s.SessionId,
    s.UserId,
    u.Username,
    u.FullName,
    s.ClientMachineId,
    c.ClientCode,
    c.MachineName,
    s.StartedAt,
    s.EndedAt,
    s.SelectedDurationMinutes,
    s.ActualDurationMinutes,
    s.Status,
    s.TerminationReason,
    br.RatePerMinute,
    bil.Amount AS BillingAmount,
    bil.Status AS BillingStatus
FROM dbo.tblSession s
INNER JOIN dbo.tblUser u ON s.UserId = u.UserId
INNER JOIN dbo.tblClientMachine c ON s.ClientMachineId = c.ClientMachineId
LEFT JOIN dbo.tblBillingRate br ON br.IsActive = 1
LEFT JOIN dbo.tblBillingRecord bil ON s.SessionId = bil.SessionId;
GO

-- vw_ActiveSessionsSummary
CREATE VIEW vw_ActiveSessionsSummary AS
SELECT 
    COUNT(*) AS TotalActiveSessions,
    COUNT(DISTINCT s.UserId) AS UniqueUsers,
    COUNT(DISTINCT s.ClientMachineId) AS ActiveClients,
    SUM(CAST(DATEDIFF(MINUTE, s.StartedAt, GETDATE()) * br.RatePerMinute AS DECIMAL(10,2))) AS TotalCurrentBilling
FROM dbo.tblSession s
LEFT JOIN dbo.tblBillingRate br ON br.IsActive = 1 AND br.IsDefault = 1
WHERE s.Status = 'Active';
GO

/* ============================================================
   PART 5: INSERT SEED DATA
   ============================================================ */

-- Admin User
-- Password: Admin@123456
-- Hash generated with: BCrypt.Net-Next (WorkFactor=12)
INSERT INTO dbo.tblUser 
(Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedAt)
VALUES 
('admin', '$2a$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jKMm2', 'System Administrator', 'Admin', 'Active', '1234567890', 'Admin Office', GETDATE());

-- Sample Users
-- Password: User1@123456
-- Password: User2@123456
-- Password: User3@123456
-- Hash generated with: BCrypt.Net-Next (WorkFactor=12)
INSERT INTO dbo.tblUser 
(Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedAt)
VALUES 
('user1', '$2a$12$HNu1AEwqg7FaRJx0vxFPauZMvAiEYJdM9k4kqJxVz1nH7L5nVJyR.', 'John Doe', 'ClientUser', 'Active', '1111111111', '123 Main St', GETDATE()),
('user2', '$2a$12$kCvZqVz.QNSHpI2kbDJbvOCYvN5qQXcnCn7OPdJvWvhDQSoWVJIui', 'Jane Smith', 'ClientUser', 'Active', '2222222222', '456 Oak Ave', GETDATE()),
('user3', '$2a$12$pVS9HB0VJcbQGGYO7jLDyuS3Z8x9n2B7CmKPpZwWQNvJhFkXLJG4u', 'Bob Johnson', 'ClientUser', 'Active', '3333333333', '789 Pine Rd', GETDATE());

-- Client Machines
INSERT INTO dbo.tblClientMachine 
(ClientCode, MachineName, IPAddress, MACAddress, Location, Status, IsActive)
VALUES 
('CLIENT001', 'WORKSTATION01', '192.168.1.10', '00:1A:2B:3C:4D:5E', 'Floor 1', 'Idle', 1),
('CLIENT002', 'WORKSTATION02', '192.168.1.11', '00:1A:2B:3C:4D:5F', 'Floor 1', 'Idle', 1),
('CLIENT003', 'WORKSTATION03', '192.168.1.12', '00:1A:2B:3C:4D:60', 'Floor 2', 'Idle', 1);

-- Billing Rates
INSERT INTO dbo.tblBillingRate 
(Name, RatePerMinute, Currency, IsActive, IsDefault)
VALUES 
('Standard Rate', 0.50, 'USD', 1, 1),
('Premium Rate', 1.00, 'USD', 1, 0),
('Discount Rate', 0.25, 'USD', 1, 0);

-- Activity Types
INSERT INTO dbo.tblActivityType 
(Name, Description, DefaultSeverity, IsActive)
VALUES 
('UnauthorizedAccess', 'Attempt to access unauthorized resources', 'High', 1),
('SessionExpired', 'Session expired due to timeout', 'Medium', 1),
('LoginFailure', 'Failed login attempt', 'Medium', 1),
('DataTransfer', 'Large data transfer detected', 'Low', 1),
('SystemError', 'System error occurred', 'High', 1),
('ConfigChange', 'Configuration change detected', 'Medium', 1);

/* ============================================================
   SUMMARY
   ============================================================ */
SELECT 
    'Database Setup Complete' AS Status,
    (SELECT COUNT(*) FROM dbo.tblUser) AS TotalUsers,
    (SELECT COUNT(*) FROM dbo.tblClientMachine) AS TotalClients,
    (SELECT COUNT(*) FROM dbo.tblBillingRate) AS TotalBillingRates,
    (SELECT COUNT(*) FROM dbo.tblActivityType) AS TotalActivityTypes;
GO
