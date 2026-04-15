/* ============================================================
   Client Server Session DB (SQL Server 2019 compatible)
   ============================================================ */

-- Create database if it does not exist
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'ClientServerSessionDB')
BEGIN
    CREATE DATABASE ClientServerSessionDB;
END;
GO

USE ClientServerSessionDB;
GO

/* ============================================================
   PART 1: DROP EXISTING OBJECTS
   ============================================================ */

-- Drop stored procedures
IF OBJECT_ID('dbo.sp_StartSession', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_StartSession;
GO
IF OBJECT_ID('dbo.sp_EndSession', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_EndSession;
GO
IF OBJECT_ID('dbo.sp_GetBillingRecords', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_GetBillingRecords;
GO
IF OBJECT_ID('dbo.sp_MarkBillingRecordPaid', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_MarkBillingRecordPaid;
GO
IF OBJECT_ID('dbo.sp_GetActiveSessions', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_GetActiveSessions;
GO
IF OBJECT_ID('dbo.sp_LogSecurityAlert', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_LogSecurityAlert;
GO
IF OBJECT_ID('dbo.sp_CalculateSessionBilling', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_CalculateSessionBilling;
GO
IF OBJECT_ID('dbo.sp_FinalizeSessionBilling', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_FinalizeSessionBilling;
GO
IF OBJECT_ID('dbo.sp_RegisterClient', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_RegisterClient;
GO
IF OBJECT_ID('dbo.sp_RegisterClientUser', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_RegisterClientUser;
GO
IF OBJECT_ID('dbo.sp_InsertBillingRate', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_InsertBillingRate;
GO
IF OBJECT_ID('dbo.sp_UpdateBillingRate', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_UpdateBillingRate;
GO
IF OBJECT_ID('dbo.sp_DeleteBillingRate', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_DeleteBillingRate;
GO
IF OBJECT_ID('dbo.sp_GetAllBillingRates', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_GetAllBillingRates;
GO
IF OBJECT_ID('dbo.sp_SetDefaultBillingRate', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_SetDefaultBillingRate;
GO

-- Drop views
IF OBJECT_ID('dbo.vw_SessionReport', 'V') IS NOT NULL DROP VIEW dbo.vw_SessionReport;
GO
IF OBJECT_ID('dbo.vw_ActiveSessionsSummary', 'V') IS NOT NULL DROP VIEW dbo.vw_ActiveSessionsSummary;
GO

-- Drop tables in reverse dependency order
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
    FullName          NVARCHAR(100) NOT NULL,
    Role              NVARCHAR(20) NOT NULL,
    Status            NVARCHAR(20) NOT NULL,
    Phone             NVARCHAR(30) NULL,
    Address           NVARCHAR(200) NULL,
    CreatedByUserId   INT NULL,
    CreatedAt         DATETIME NOT NULL CONSTRAINT DF_tblUser_CreatedAt DEFAULT (GETDATE()),
    LastLoginAt          DATETIME NULL,
    ProfilePicturePath   NVARCHAR(500) NULL,
    CONSTRAINT UQ_tblUser_Username UNIQUE (Username),
    CONSTRAINT CK_tblUser_Role CHECK (Role IN ('Admin', 'ClientUser')),
    CONSTRAINT CK_tblUser_Status CHECK (Status IN ('Active', 'Blocked', 'Disabled'))
);
GO

ALTER TABLE dbo.tblUser
ADD CONSTRAINT FK_tblUser_CreatedByUser
FOREIGN KEY (CreatedByUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO


-- 2) tblClientMachine
CREATE TABLE dbo.tblClientMachine (
    ClientMachineId   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClientCode        NVARCHAR(50) NOT NULL, --v2
    MachineName       NVARCHAR(50) NOT NULL,
    IPAddress         NVARCHAR(45) NOT NULL,
    MACAddress        NVARCHAR(50) NULL,
    Location          NVARCHAR(100) NULL,
    Status            NVARCHAR(20) NOT NULL,
    LastSeenAt        DATETIME NOT NULL CONSTRAINT DF_tblClientMachine_LastSeenAt DEFAULT (GETDATE()),
    IsActive          BIT NOT NULL CONSTRAINT DF_tblClientMachine_IsActive DEFAULT (1),
    MissedHeartbeats  INT NOT NULL CONSTRAINT DF_tblClientMachine_MissedHeartbeats DEFAULT (0),
    -- IPAddress has no unique constraint: DHCP reassignment and reinstalls
    -- can produce the same IP on a different machine. ClientCode (MAC-derived) is the unique key.
    CONSTRAINT UQ_tblClientMachine_ClientCode UNIQUE (ClientCode),
    CONSTRAINT CK_tblClientMachine_Status CHECK (Status IN ('Idle', 'Active', 'Offline'))
);
GO

CREATE INDEX IX_tblClientMachine_ClientCode ON dbo.tblClientMachine(ClientCode); --v2
GO


-- 3) tblSession
CREATE TABLE dbo.tblSession (
    SessionId                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId                   INT NOT NULL,
    ClientMachineId          INT NOT NULL,
    LoginAt                  DATETIME NOT NULL CONSTRAINT DF_tblSession_LoginAt DEFAULT (GETDATE()),
    StartedAt                DATETIME NOT NULL CONSTRAINT DF_tblSession_StartedAt DEFAULT (GETDATE()),
    SelectedDurationMinutes  INT NOT NULL,
    ExpectedEndAt            AS (DATEADD(MINUTE, SelectedDurationMinutes, StartedAt)),
    EndedAt                  DATETIME NULL,
    ActualDurationMinutes    INT NULL,
    Status                   NVARCHAR(20) NOT NULL,
    TerminationReason        NVARCHAR(30) NULL,
    CONSTRAINT CK_tblSession_Status CHECK (
        Status IN ('Pending', 'Active', 'Completed', 'Expired', 'Terminated', 'Cancelled')
    ),
    CONSTRAINT CK_tblSession_TerminationReason CHECK (
        TerminationReason IS NULL OR
        TerminationReason IN ('AutoExpiry', 'AdminTerminate', 'UserLogout', 'SystemError', 'Crash')
    )
);
GO

ALTER TABLE dbo.tblSession
ADD CONSTRAINT FK_tblSession_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSession
ADD CONSTRAINT FK_tblSession_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblSession_UserId ON dbo.tblSession(UserId);
GO
CREATE INDEX IX_tblSession_ClientMachineId ON dbo.tblSession(ClientMachineId);
GO
CREATE INDEX IX_tblSession_Status ON dbo.tblSession(Status);
GO


-- 4) tblSessionImage
CREATE TABLE dbo.tblSessionImage (
    SessionImageId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId          INT NOT NULL,
    CapturedAt         DATETIME NOT NULL CONSTRAINT DF_tblSessionImage_CapturedAt DEFAULT (GETDATE()),
    CaptureStatus      NVARCHAR(30) NOT NULL,
    UploadStatus       NVARCHAR(20) NOT NULL,
    ImagePath          NVARCHAR(500) NULL,--NVARCHAR(500) NOT NULL, --v2
    Notes              NVARCHAR(500) NULL,
    CONSTRAINT CK_tblSessionImage_CaptureStatus CHECK (
        CaptureStatus IN ('Captured', 'CameraUnavailable', 'Skipped', 'Failed')
    ),
    CONSTRAINT CK_tblSessionImage_UploadStatus CHECK (
        UploadStatus IN ('Sent', 'Pending', 'Failed')
    ),
    CONSTRAINT UQ_tblSessionImage_Session UNIQUE (SessionId)
);
GO

ALTER TABLE dbo.tblSessionImage
ADD CONSTRAINT FK_tblSessionImage_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE CASCADE
ON UPDATE NO ACTION;
GO


-- 5) tblBillingRate
CREATE TABLE dbo.tblBillingRate (
    BillingRateId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name               NVARCHAR(100) NOT NULL CONSTRAINT UQ_tblBillingRate_Name UNIQUE,
    RatePerMinute      DECIMAL(10,2) NOT NULL,
    Currency           NVARCHAR(10) NOT NULL,
    EffectiveFrom      DATE NOT NULL,             -- mandatory: required for date-based rate resolution
    EffectiveTo        DATE NULL,                 -- NULL = open-ended (no expiry)
    IsActive           BIT NOT NULL CONSTRAINT DF_tblBillingRate_IsActive DEFAULT (1),
    IsDefault          BIT NOT NULL CONSTRAINT DF_tblBillingRate_IsDefault DEFAULT (0),
    SetByAdminUserId   INT NULL,
    Notes              NVARCHAR(500) NULL,
    CreatedAt          DATETIME NOT NULL CONSTRAINT DF_tblBillingRate_CreatedAt DEFAULT (GETDATE())
);
GO

-- Index to speed up date-range resolution queries in GetCurrentBillingRate
CREATE INDEX IX_tblBillingRate_DateRange
    ON dbo.tblBillingRate (EffectiveFrom, EffectiveTo, IsActive, Currency);
GO

ALTER TABLE dbo.tblBillingRate
ADD CONSTRAINT FK_tblBillingRate_SetByAdmin
FOREIGN KEY (SetByAdminUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO


-- 6) tblBillingRecord
CREATE TABLE dbo.tblBillingRecord (
    BillingRecordId    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId          INT NOT NULL,
    BillingRateId      INT NOT NULL,
    BillableMinutes    INT NOT NULL,
    Amount             DECIMAL(10,2) NOT NULL,
    CalculatedAt       DATETIME NOT NULL CONSTRAINT DF_tblBillingRecord_CalculatedAt DEFAULT (GETDATE()),
    Status             NVARCHAR(20) NOT NULL CONSTRAINT DF_tblBillingRecord_Status DEFAULT ('Running'),
    Remarks            NVARCHAR(500) NULL,
    IsPaid             BIT NOT NULL CONSTRAINT DF_tblBillingRecord_IsPaid DEFAULT (0),
    PaidAt             DATETIME NULL,
    ReceivedByAdminId  INT NULL,
    CONSTRAINT CK_tblBillingRecord_Status CHECK (Status IN ('Finalized', 'Running')),
    CONSTRAINT UQ_tblBillingRecord_Session UNIQUE (SessionId)
);
GO

ALTER TABLE dbo.tblBillingRecord
ADD CONSTRAINT FK_tblBillingRecord_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE CASCADE
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblBillingRecord
ADD CONSTRAINT FK_tblBillingRecord_tblBillingRate
FOREIGN KEY (BillingRateId) REFERENCES dbo.tblBillingRate(BillingRateId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblBillingRecord_BillingRateId ON dbo.tblBillingRecord(BillingRateId);
GO


-- 7) tblActivityType
CREATE TABLE dbo.tblActivityType (
    ActivityTypeId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name               NVARCHAR(100) NOT NULL,
    Description        NVARCHAR(500) NULL,
    DefaultSeverity    NVARCHAR(10) NOT NULL,
    IsActive           BIT NOT NULL CONSTRAINT DF_tblActivityType_IsActive DEFAULT (1),
    CONSTRAINT CK_tblActivityType_Severity CHECK (DefaultSeverity IN ('Low', 'Medium', 'High'))
);
GO


-- 8) tblAlert
CREATE TABLE dbo.tblAlert (
    AlertId                   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ActivityTypeId            INT NOT NULL,
    SessionId                 INT NULL,
    ClientMachineId           INT NOT NULL,
    UserId                    INT NULL,
    DetectedAt                DATETIME NOT NULL CONSTRAINT DF_tblAlert_DetectedAt DEFAULT (GETDATE()),
    Severity                  NVARCHAR(10) NOT NULL,
    Status                    NVARCHAR(20) NOT NULL CONSTRAINT DF_tblAlert_Status DEFAULT ('New'),
    Details                   NVARCHAR(1000) NOT NULL,
    IsNotifiedToAdmin         BIT NOT NULL CONSTRAINT DF_tblAlert_IsNotified DEFAULT (0),
    IsAcknowledged            BIT NOT NULL CONSTRAINT DF_tblAlert_IsAcknowledged DEFAULT (0),
    AcknowledgedByAdminUserId INT NULL,
    AcknowledgedAt            DATETIME NULL,
    CONSTRAINT CK_tblAlert_Severity CHECK (Severity IN ('Low', 'Medium', 'High')),
    CONSTRAINT CK_tblAlert_Status CHECK (Status IN ('New', 'Acknowledged', 'Resolved', 'Closed'))
);
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblActivityType
FOREIGN KEY (ActivityTypeId) REFERENCES dbo.tblActivityType(ActivityTypeId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblAlert
ADD CONSTRAINT FK_tblAlert_AckAdmin
FOREIGN KEY (AcknowledgedByAdminUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblAlert_SessionId ON dbo.tblAlert(SessionId);
GO
CREATE INDEX IX_tblAlert_ClientMachineId ON dbo.tblAlert(ClientMachineId);
GO
CREATE INDEX IX_tblAlert_ActivityTypeId ON dbo.tblAlert(ActivityTypeId);
GO


-- 9) tblLoginAttempt
CREATE TABLE dbo.tblLoginAttempt (
    LoginAttemptId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClientMachineId     INT NULL, --ClientMachineId     INT NOT NULL, --v2
    UserId              INT NULL,
    UsernameEntered     NVARCHAR(50) NOT NULL,
    AttemptedAt         DATETIME NOT NULL CONSTRAINT DF_tblLoginAttempt_AttemptedAt DEFAULT (GETDATE()),
    IsSuccess           BIT NOT NULL,
    FailureReason       NVARCHAR(30) NULL -- InvalidPassword/UnknownUser/BlockedUser/ServerUnreachable
);
GO

ALTER TABLE dbo.tblLoginAttempt
ADD CONSTRAINT FK_tblLoginAttempt_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblLoginAttempt
ADD CONSTRAINT FK_tblLoginAttempt_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblLoginAttempt_ClientMachineId ON dbo.tblLoginAttempt(ClientMachineId);
GO
CREATE INDEX IX_tblLoginAttempt_UserId ON dbo.tblLoginAttempt(UserId);
GO


-- 10) tblSystemLog
CREATE TABLE dbo.tblSystemLog (
    SystemLogId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    LoggedAt          DATETIME NOT NULL CONSTRAINT DF_tblSystemLog_LoggedAt DEFAULT (GETDATE()),
    Category          NVARCHAR(20) NOT NULL,
    Type              NVARCHAR(50) NOT NULL, -- Login/StartSession/Termination/BillingFinalized/AlertGenerated/Error/SyncIssue...
    Message           NVARCHAR(2000) NOT NULL,
    Source            NVARCHAR(10) NULL,
    SessionId         INT NULL,
    UserId            INT NULL,
    ClientMachineId   INT NULL,
    AdminUserId       INT NULL,
    CONSTRAINT CK_tblSystemLog_Category CHECK (Category IN ('Auth', 'Session', 'Billing', 'Security', 'System')),
    CONSTRAINT CK_tblSystemLog_Source CHECK (Source IS NULL OR Source IN ('Client', 'Server'))
);
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_tblSession
FOREIGN KEY (SessionId) REFERENCES dbo.tblSession(SessionId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_tblUser
FOREIGN KEY (UserId) REFERENCES dbo.tblUser(UserId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_tblClientMachine
FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine(ClientMachineId)
ON DELETE SET NULL
ON UPDATE NO ACTION;
GO

ALTER TABLE dbo.tblSystemLog
ADD CONSTRAINT FK_tblSystemLog_AdminUser
FOREIGN KEY (AdminUserId) REFERENCES dbo.tblUser(UserId)
ON DELETE NO ACTION
ON UPDATE NO ACTION;
GO

CREATE INDEX IX_tblSystemLog_SessionId ON dbo.tblSystemLog(SessionId);
GO
CREATE INDEX IX_tblSystemLog_UserId ON dbo.tblSystemLog(UserId);
GO
CREATE INDEX IX_tblSystemLog_ClientMachineId ON dbo.tblSystemLog(ClientMachineId);
GO


/* ============================================================
   PART 3: CREATE STORED PROCEDURES
   ============================================================ */

-- sp_StartSession
CREATE PROCEDURE dbo.sp_StartSession
    @UserId INT,
    @ClientMachineId INT,
    @SelectedDurationMinutes INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SessionId INT;

    BEGIN TRY
        -- Guard: user already has an active session
        IF EXISTS (
            SELECT 1 FROM dbo.tblSession
            WHERE  UserId = @UserId AND Status = 'Active'
        )
        BEGIN
            SELECT -1 AS SessionId;  -- user conflict
            RETURN;
        END

        -- Guard: machine already has an active session
        IF EXISTS (
            SELECT 1 FROM dbo.tblSession
            WHERE  ClientMachineId = @ClientMachineId AND Status = 'Active'
        )
        BEGIN
            SELECT -2 AS SessionId;  -- machine conflict
            RETURN;
        END

        INSERT INTO dbo.tblSession
            (UserId, ClientMachineId, LoginAt, StartedAt, SelectedDurationMinutes, Status)
        VALUES
            (@UserId, @ClientMachineId, GETDATE(), GETDATE(), @SelectedDurationMinutes, 'Active');

        SET @SessionId = SCOPE_IDENTITY();

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES
            ('Session', 'StartSession',
             'Session started for ' + CAST(@SelectedDurationMinutes AS NVARCHAR(10)) + ' minutes',
             'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT @SessionId AS SessionId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'Error in sp_StartSession: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0 AS SessionId;
    END CATCH
END;
GO


-- sp_EndSession
CREATE PROCEDURE dbo.sp_EndSession
    @SessionId INT,
    @TerminationReason NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId INT,
            @MachineId INT,
            @NewStatus NVARCHAR(20);

    BEGIN TRY
        SELECT
            @UserId = UserId,
            @MachineId = ClientMachineId
        FROM dbo.tblSession
        WHERE SessionId = @SessionId;

        SET @NewStatus =
            CASE @TerminationReason
                WHEN 'AdminTerminate' THEN 'Terminated'
                WHEN 'AutoExpiry' THEN 'Expired'
                WHEN 'UserLogout' THEN 'Completed'
                WHEN 'Crash' THEN 'Terminated'
                WHEN 'SystemError' THEN 'Terminated'
                ELSE 'Completed'
            END;

        UPDATE dbo.tblSession
        SET EndedAt = GETDATE(),
            Status = @NewStatus,
            TerminationReason = @TerminationReason,
            ActualDurationMinutes = DATEDIFF(MINUTE, StartedAt, GETDATE())
        WHERE SessionId = @SessionId;

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES
            ('Session', 'SessionEnded',
             'Session ' + CAST(@SessionId AS NVARCHAR(20)) + ' ended - ' + @TerminationReason,
             'Server', @SessionId, @UserId, @MachineId);

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'sp_EndSession: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0 AS Result;
    END CATCH
END;
GO


-- sp_GetBillingRecords
CREATE PROCEDURE dbo.sp_GetBillingRecords
    @UnpaidOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        br.BillingRecordId,
        br.SessionId,
        u.Username,
        u.FullName,
        cm.ClientCode      AS MachineCode,
        br.BillableMinutes,
        br.Amount,
        brate.Currency,
        br.CalculatedAt,
        br.IsPaid,
        br.PaidAt,
        br.ReceivedByAdminId
    FROM dbo.tblBillingRecord br
    INNER JOIN dbo.tblSession       s     ON s.SessionId          = br.SessionId
    INNER JOIN dbo.tblUser          u     ON u.UserId              = s.UserId
    INNER JOIN dbo.tblClientMachine cm    ON cm.ClientMachineId    = s.ClientMachineId
    INNER JOIN dbo.tblBillingRate   brate ON brate.BillingRateId   = br.BillingRateId
    WHERE br.Status = 'Finalized'
      AND (@UnpaidOnly = 0 OR br.IsPaid = 0)
    ORDER BY br.CalculatedAt DESC;
END;
GO

-- sp_MarkBillingRecordPaid
CREATE PROCEDURE dbo.sp_MarkBillingRecordPaid
    @BillingRecordId INT,
    @AdminUserId     INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE BillingRecordId = @BillingRecordId)
        BEGIN
            SELECT 0 AS Result; RETURN;
        END;

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE BillingRecordId = @BillingRecordId AND IsPaid = 1)
        BEGIN
            SELECT -1 AS Result; RETURN;
        END;

        UPDATE dbo.tblBillingRecord
        SET    IsPaid            = 1,
               PaidAt            = GETDATE(),
               ReceivedByAdminId = @AdminUserId
        WHERE  BillingRecordId = @BillingRecordId;

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Info',
            'BillingRecord ' + CAST(@BillingRecordId AS VARCHAR) +
            ' marked paid by admin ' + CAST(@AdminUserId AS VARCHAR), 'Server');

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_MarkBillingRecordPaid: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END;
GO


-- sp_GetActiveSessions
CREATE PROCEDURE dbo.sp_GetActiveSessions
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
        CAST(DATEDIFF(MINUTE, s.StartedAt, GETDATE()) * br.RatePerMinute AS DECIMAL(10,2)) AS CurrentBilling,
        img.ImagePath
    FROM dbo.tblSession s
    INNER JOIN dbo.tblUser u ON s.UserId = u.UserId
    INNER JOIN dbo.tblClientMachine c ON s.ClientMachineId = c.ClientMachineId
    LEFT JOIN dbo.tblBillingRate br ON br.IsActive = 1 AND br.IsDefault = 1
    LEFT JOIN dbo.tblSessionImage img ON img.SessionId = s.SessionId
                                     AND img.CaptureStatus = 'Captured'
    WHERE s.Status = 'Active'
    ORDER BY s.StartedAt DESC;
END;
GO


-- sp_LogSecurityAlert
CREATE PROCEDURE dbo.sp_LogSecurityAlert
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
        SELECT @ActivityTypeId = ActivityTypeId
        FROM dbo.tblActivityType
        WHERE Name = @ActivityTypeName;

        IF @ActivityTypeId IS NULL
        BEGIN
            INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
            VALUES (@ActivityTypeName, NULL, @Severity, 1);

            SET @ActivityTypeId = SCOPE_IDENTITY();
        END;

        INSERT INTO dbo.tblAlert
            (ActivityTypeId, SessionId, ClientMachineId, UserId, DetectedAt, Severity, Status, Details)
        VALUES
            (@ActivityTypeId, @SessionId, @ClientMachineId, @UserId, GETDATE(), @Severity, 'New', @Details);

        DECLARE @AlertId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES
            ('Security', 'Alert',
             'Security Alert: ' + @ActivityTypeName + ' - ' + @Details,
             'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT @AlertId AS AlertId;   -- returns inserted AlertId (used to set IsNotifiedToAdmin)
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'Error in sp_LogSecurityAlert: ' + ERROR_MESSAGE(), 'Server');

        SELECT -1 AS AlertId;
    END CATCH
END;
GO


-- sp_CalculateSessionBilling
CREATE PROCEDURE dbo.sp_CalculateSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BillingRateId INT,
            @RatePerMinute DECIMAL(10,2),
            @ElapsedMinutes INT,
            @Amount DECIMAL(10,2);

    BEGIN TRY
        SELECT @ElapsedMinutes = DATEDIFF(MINUTE, StartedAt, GETDATE())
        FROM dbo.tblSession
        WHERE SessionId = @SessionId;

        SELECT @BillingRateId = BillingRateId,
               @RatePerMinute = RatePerMinute
        FROM dbo.tblBillingRate
        WHERE IsActive = 1
          AND IsDefault = 1;

        IF @BillingRateId IS NULL
        BEGIN
            SELECT TOP 1
                @BillingRateId = BillingRateId,
                @RatePerMinute = RatePerMinute
            FROM dbo.tblBillingRate
            WHERE IsActive = 1
            ORDER BY CreatedAt DESC;
        END;

        SET @Amount = ISNULL(@ElapsedMinutes, 0) * ISNULL(@RatePerMinute, 0);

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE SessionId = @SessionId)
        BEGIN
            UPDATE dbo.tblBillingRecord
            SET BillableMinutes = @ElapsedMinutes,
                Amount = @Amount,
                BillingRateId = @BillingRateId,
                CalculatedAt = GETDATE()
            WHERE SessionId = @SessionId;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tblBillingRecord
                (SessionId, BillingRateId, BillableMinutes, Amount, Status)
            VALUES
                (@SessionId, @BillingRateId, @ElapsedMinutes, @Amount, 'Running');
        END;

        SELECT @Amount AS Amount;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId)
        VALUES
            ('Billing', 'Error', 'Error in sp_CalculateSessionBilling: ' + ERROR_MESSAGE(), 'Server', @SessionId);

        SELECT 0 AS Amount;
    END CATCH
END;
GO


-- sp_FinalizeSessionBilling
CREATE PROCEDURE dbo.sp_FinalizeSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        UPDATE dbo.tblBillingRecord
        SET Status = 'Finalized',
            CalculatedAt = GETDATE()
        WHERE SessionId = @SessionId;

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId)
        VALUES
            ('Billing', 'BillingFinalized', 'Session billing finalized', 'Server', @SessionId);

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId)
        VALUES
            ('System', 'Error', 'Error in sp_FinalizeSessionBilling: ' + ERROR_MESSAGE(), 'Server', @SessionId);

        SELECT 0 AS Result;
    END CATCH
END;
GO


-- sp_RegisterClient
CREATE PROCEDURE dbo.sp_RegisterClient
    @ClientCode NVARCHAR(50),
    @MachineName NVARCHAR(50),
    @IPAddress NVARCHAR(45),
    @MACAddress NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MachineId INT;

    BEGIN TRY
        -- IPAddress is informational only — uniqueness is not enforced.
        -- ClientCode (derived from MAC) is the true unique identifier per machine.

        IF EXISTS (SELECT 1 FROM dbo.tblClientMachine WHERE ClientCode = @ClientCode)
        BEGIN
            UPDATE dbo.tblClientMachine
            SET MachineName = @MachineName,
                IPAddress = @IPAddress,
                MACAddress = ISNULL(@MACAddress, MACAddress),
                Status = 'Idle',
                LastSeenAt = GETDATE()
            WHERE ClientCode = @ClientCode;

            SELECT @MachineId = ClientMachineId
            FROM dbo.tblClientMachine
            WHERE ClientCode = @ClientCode;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tblClientMachine
                (ClientCode, MachineName, IPAddress, MACAddress, Status, IsActive)
            VALUES
                (@ClientCode, @MachineName, @IPAddress, @MACAddress, 'Idle', 1);

            SET @MachineId = SCOPE_IDENTITY();
        END;

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, ClientMachineId)
        VALUES
            ('System', 'ClientRegistered',
             'Client ' + @ClientCode + ' (' + @MachineName + ') registered/updated',
             'Server', @MachineId);

        SELECT @MachineId AS ClientMachineId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'sp_RegisterClient: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0 AS ClientMachineId;
    END CATCH
END;
GO
--sp_RegisterClientUser
CREATE OR ALTER PROCEDURE dbo.sp_RegisterClientUser
    @Username     NVARCHAR(50),
    @PasswordHash NVARCHAR(255),
    @FullName     NVARCHAR(100),
    @Phone        NVARCHAR(30)  = NULL,
    @Address      NVARCHAR(200) = NULL,
    @AdminUserId  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId INT;

    BEGIN TRY
        -- Validate required fields
        IF LTRIM(RTRIM(ISNULL(@FullName, ''))) = ''
            RAISERROR('Full name is required.', 16, 1);

        -- Insert user
        INSERT INTO dbo.tblUser
        (
            Username, PasswordHash, FullName, Role, Status,
            Phone, Address, CreatedByUserId
        )
        VALUES
        (
            @Username, @PasswordHash, @FullName,
            'ClientUser', 'Active',
            @Phone, @Address, @AdminUserId
        );

        SET @UserId = SCOPE_IDENTITY();

        -- Return new user id
        SELECT @UserId AS UserId;
    END TRY
    BEGIN CATCH
        -- Duplicate username
        IF ERROR_NUMBER() IN (2627, 2601)
        BEGIN
            SELECT -1 AS UserId;
            INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
            VALUES ('Auth', 'DuplicateUser',
                'Duplicate username attempt: "' + @Username + '"', 'Server');
        END
        ELSE
        BEGIN
            SELECT 0 AS UserId;
            INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
            VALUES ('System', 'Error',
                'sp_RegisterClientUser: ' + ERROR_MESSAGE(), 'Server');
        END
    END CATCH
END
GO

-- sp_UpdateClientUser
CREATE OR ALTER PROCEDURE dbo.sp_UpdateClientUser
    @UserId             INT,
    @FullName           NVARCHAR(100),
    @Phone              NVARCHAR(30)   = NULL,
    @Address            NVARCHAR(200)  = NULL,
    @ProfilePicturePath NVARCHAR(500)  = NULL   -- NULL = keep existing path
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        -- Validate required fields
        IF LTRIM(RTRIM(ISNULL(@FullName, ''))) = ''
            RAISERROR('Full name is required.', 16, 1);

        UPDATE dbo.tblUser
        SET    FullName           = @FullName,
               Phone              = @Phone,
               Address            = @Address,
               ProfilePicturePath = CASE
                                        WHEN @ProfilePicturePath IS NOT NULL
                                        THEN @ProfilePicturePath
                                        ELSE ProfilePicturePath
                                    END
        WHERE  UserId = @UserId AND Role = 'ClientUser';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error',
            'sp_UpdateClientUser: ' + ERROR_MESSAGE(), 'Server');
        THROW;
    END CATCH
END
GO


-- sp_DeleteClientUser
CREATE OR ALTER PROCEDURE dbo.sp_DeleteClientUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        -- Block delete if user has any session history (FK integrity)
        IF EXISTS (SELECT 1 FROM dbo.tblSession WHERE UserId = @UserId)
        BEGIN
            SELECT -1 AS Result;  -- has sessions
            RETURN;
        END

        DELETE FROM dbo.tblUser
        WHERE  UserId = @UserId AND Role = 'ClientUser';

        SELECT @@ROWCOUNT AS Result;  -- 1 = deleted, 0 = not found
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error',
            'sp_DeleteClientUser: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END
GO


/* ============================================================
   PART 4: CREATE VIEWS
   ============================================================ */

-- vw_SessionReport
CREATE VIEW dbo.vw_SessionReport
AS
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
    bil.BillingRateId,
    bil.BillableMinutes,
    bil.Amount AS BillingAmount,
    bil.Status AS BillingStatus
FROM dbo.tblSession s
INNER JOIN dbo.tblUser u
    ON u.UserId = s.UserId
INNER JOIN dbo.tblClientMachine c
    ON c.ClientMachineId = s.ClientMachineId
LEFT JOIN dbo.tblBillingRecord bil
    ON bil.SessionId = s.SessionId
LEFT JOIN dbo.tblBillingRate br
    ON br.BillingRateId = bil.BillingRateId;
GO


-- vw_ActiveSessionsSummary
CREATE VIEW dbo.vw_ActiveSessionsSummary
AS
SELECT
    COUNT(*) AS TotalActiveSessions,
    COUNT(DISTINCT s.UserId) AS UniqueUsers,
    COUNT(DISTINCT s.ClientMachineId) AS ActiveClients,
    SUM(CAST(DATEDIFF(MINUTE, s.StartedAt, GETDATE()) * ISNULL(br.RatePerMinute, 0) AS DECIMAL(10,2))) AS TotalCurrentBilling
FROM dbo.tblSession s
LEFT JOIN dbo.tblBillingRate br
    ON br.IsActive = 1 AND br.IsDefault = 1
WHERE s.Status = 'Active';
GO


/* ============================================================
   PART 5: INSERT SEED DATA
   ============================================================ */
   -- Admin User
-- Password: Admin@123456
-- Client Users
-- Password: User1@123456
-- Password: User2@123456
-- Password: User3@123456
-- Hash generated with: BCrypt.Net-Next (WorkFactor=12)

-- Admin User
INSERT INTO dbo.tblUser
    (Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedAt)
VALUES
    ('Admin', '$2a$12$cidj..ohW.bgKXVPBdVyH.VbvmIrOxVmFGqV3Y/lZDGC0utA685vm', 'System Administrator', 'Admin', 'Active', '1234567890', 'Admin Office', GETDATE());
GO

-- Sample Users
INSERT INTO dbo.tblUser
    (Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedAt)
VALUES
    ('User1', '$2a$12$NS2X3ReuBdhFzxdrmy03XuTNUhu.nNyOsgWEL4eAVH3bC46Ua2rWW', 'John Doe', 'ClientUser', 'Active', '1111111111', '123 Main St', GETDATE()),
    ('User2', '$2a$12$Y69.hwCn9FHHa8lHLyI.XOxPzCd8YdJY1vSchS9U7k10N5/bdpAYC', 'Jane Smith', 'ClientUser', 'Active', '2222222222', '456 Oak Ave', GETDATE()),
    ('User3', '$2a$12$5nX6ETKBzk9d8yHfJMN65uNr6Gk4lhxr4siODdQxbj42/Ewpgn9k6', 'Bob Johnson', 'ClientUser', 'Active', '3333333333', '789 Pine Rd', GETDATE());
GO

-- Client Machines
INSERT INTO dbo.tblClientMachine
    (ClientCode, MachineName, IPAddress, MACAddress, Location, Status, IsActive)
VALUES
    ('CL001', 'CLIENT-PC-01', '192.168.1.100', '00:1A:2B:3C:4D:5E', 'Floor 1', 'Idle', 1),
    ('CL002', 'CLIENT-PC-02', '192.168.1.101', '00:1A:2B:3C:4D:5F', 'Floor 1', 'Idle', 1),
    ('CL003', 'CLIENT-PC-03', '192.168.1.102', '00:1A:2B:3C:4D:60', 'Floor 2', 'Idle', 1);
GO

-- Billing Rates (EffectiveFrom required; non-overlapping per currency)
-- "Standard Rate" is the current open-ended default.
-- "Discount Rate" covered a historical window (closed period — no overlap).
-- "Premium Rate" is inactive (opt-in upgrade, not date-driven) — kept for reference.
INSERT INTO dbo.tblBillingRate
    (Name, RatePerMinute, Currency, EffectiveFrom, EffectiveTo, IsActive, IsDefault)
VALUES
    ('Standard Rate', 3.00, 'PKR', '2024-01-01', NULL,         1, 1),
    ('Discount Rate', 2.00, 'PKR', '2023-01-01', '2023-12-31', 1, 0),
    ('Premium Rate',  5.00, 'PKR', '2024-01-01', NULL,         0, 0);  -- inactive; not in overlap checks
GO

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
GO

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


-- ============================================================
-- PATCH: Fix sp_RegisterClientUser CHECK constraint violation
-- The tblSystemLog.Category column only allows:
--   Auth | Session | Billing | Security | System
-- The existing procedure used 'User' which violates the CHECK.
-- ============================================================

USE ClientServerSessionDB;
GO

-- Also add the activity types needed for illegal activity detection
-- if they are not already present
IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'ProxySettingsEnabled')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('ProxySettingsEnabled', 'System proxy settings were enabled during session', 'High', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'ProxyAutoConfigDetected')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('ProxyAutoConfigDetected', 'PAC/Auto-config proxy URL detected during session', 'Medium', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'BlacklistedProcessRunning')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('BlacklistedProcessRunning', 'A blacklisted proxy/VPN process was running during session', 'High', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'VpnAdapterActive')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('VpnAdapterActive', 'A VPN/tunneling network adapter was active during session', 'High', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'RepeatedLoginFailure')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('RepeatedLoginFailure', 'Repeated failed login attempts from a client machine', 'High', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'CameraUnavailable')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('CameraUnavailable', 'Webcam was not available at login time', 'Low', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = 'ImageCaptureFailed')
    INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
    VALUES ('ImageCaptureFailed', 'Webcam capture failed after retry', 'Low', 1);
GO

-- Fix the stored procedure: change Category from 'User' to 'Auth'
-- (tblSystemLog.Category CHECK only allows Auth/Session/Billing/Security/System)
ALTER PROCEDURE dbo.sp_RegisterClientUser
    @Username     NVARCHAR(50),
    @PasswordHash NVARCHAR(255),
    @FullName     NVARCHAR(100),
    @Phone        NVARCHAR(30)  = NULL,
    @Address      NVARCHAR(200) = NULL,
    @AdminUserId  INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId INT;
    BEGIN TRY
        IF LTRIM(RTRIM(ISNULL(@FullName, ''))) = ''
            RAISERROR('Full name is required.', 16, 1);

        INSERT INTO dbo.tblUser
            (Username, PasswordHash, FullName, Role, Status,
             Phone, Address, CreatedByUserId)
        VALUES
            (@Username, @PasswordHash, @FullName, 'ClientUser', 'Active',
             @Phone, @Address, @AdminUserId);

        SET @UserId = SCOPE_IDENTITY();
        SELECT @UserId AS UserId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2627, 2601)
        BEGIN
            SELECT -1 AS UserId;
            INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
            VALUES ('Auth', 'DuplicateUser',
                'Duplicate username attempt: "' + @Username + '"', 'Server');
        END
        ELSE
        BEGIN
            SELECT 0 AS UserId;
            INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
            VALUES ('System', 'Error',
                'sp_RegisterClientUser: ' + ERROR_MESSAGE(), 'Server');
        END
    END CATCH
END
GO

-- Verification
SELECT 'PATCH COMPLETE' AS Status;
SELECT Name, DefaultSeverity FROM dbo.tblActivityType ORDER BY Name;
GO


-- ============================================================
-- Detection Activity Types PATCH
-- Run against ClientServerSessionDB after the main SQL script.
-- Adds activity types for every new detection rule.
-- ============================================================

USE ClientServerSessionDB;
GO

-- ── Helper procedure to upsert an activity type ───────────────
CREATE OR ALTER PROCEDURE dbo.sp_UpsertActivityType
    @Name           NVARCHAR(100),
    @Description    NVARCHAR(500),
    @DefaultSeverity NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = @Name)
        UPDATE dbo.tblActivityType
        SET Description    = @Description,
            DefaultSeverity = @DefaultSeverity,
            IsActive        = 1
        WHERE Name = @Name;
    ELSE
        INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
        VALUES (@Name, @Description, @DefaultSeverity, 1);
END
GO

-- ── FR-12 Rule 2 — Proxy ──────────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'ProxySettingsEnabled',
    'WinINet system proxy settings were enabled during an active session',
    'High';
GO

EXEC dbo.sp_UpsertActivityType
    'WinHttpProxyEnabled',
    'WinHTTP proxy configured (used by services and PowerShell) during session',
    'High';
GO

EXEC dbo.sp_UpsertActivityType
    'ProxyAutoConfigDetected',
    'PAC / auto-config proxy script URL set during active session',
    'Medium';
GO

-- ── FR-12 Rule 1 — VPN / Tunneling ───────────────────────────
EXEC dbo.sp_UpsertActivityType
    'VpnAdapterActive',
    'A VPN, TAP, TUN, or Wintun network adapter was active during session',
    'High';
GO

-- ── FR-12 Rule 3 — Blacklisted processes ─────────────────────
EXEC dbo.sp_UpsertActivityType
    'BlacklistedProcessRunning',
    'A blacklisted proxy, VPN, Tor, or remote-access process was running',
    'High';
GO

-- ── Rule 5 — Mobile hotspot ───────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'MobileHotspotDetected',
    'Client connected via mobile hotspot instead of authorised LAN',
    'High';
GO

-- ── Rule 6 — Network switching ────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'NetworkTypeSwitched',
    'Network connection type changed during session (e.g. Ethernet to WiFi)',
    'Medium';
GO

-- ── Rule 7 — Clock tamper ─────────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'SystemTimeTampered',
    'System clock was moved forward or backward during an active session',
    'High';
GO

-- ── Rule 8 — Remote Desktop ───────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'RemoteDesktopSessionDetected',
    'Client application is running inside an RDP / Remote Desktop session',
    'High';
GO

-- ── Rule 9 — Virtual webcam ───────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'VirtualCameraDetected',
    'A virtual or fake webcam (OBS, DroidCam, ManyCam, etc.) was detected',
    'Medium';
GO

-- ── FR-12 Rule 4 — Repeated login failures ────────────────────
EXEC dbo.sp_UpsertActivityType
    'RepeatedLoginFailure',
    'Repeated failed login attempts from a client machine in a short window',
    'High';
GO

-- ── Webcam availability ───────────────────────────────────────
EXEC dbo.sp_UpsertActivityType
    'CameraUnavailable',
    'Webcam was not available at login time (camera not connected)',
    'Low';
GO

EXEC dbo.sp_UpsertActivityType
    'ImageCaptureFailed',
    'Webcam image capture failed even after one retry',
    'Low';
GO

-- ── Verify ────────────────────────────────────────────────────
SELECT Name, DefaultSeverity, Description
FROM   dbo.tblActivityType
ORDER  BY DefaultSeverity DESC, Name;
GO

SELECT 'PATCH COMPLETE — ' + CAST(COUNT(*) AS VARCHAR) + ' activity types registered' AS Status
FROM   dbo.tblActivityType;
GO

/* ============================================================
   PART 3B: BILLING RATE MANAGEMENT PROCEDURES
   ============================================================ */

-- ═══════════════════════════════════════════════════════════
--  sp_InsertBillingRate
--  Inserts a new billing rate. If IsDefault=1, sets all others
--  to IsDefault=0 before insertion.
-- ═══════════════════════════════════════════════════════════
CREATE OR ALTER PROCEDURE dbo.sp_InsertBillingRate
    @Name             NVARCHAR(100),
    @RatePerMinute    DECIMAL(10,2),
    @Currency         NVARCHAR(10),
    @EffectiveFrom    DATE,                        -- required; NOT NULL in table
    @EffectiveTo      DATE = NULL,                 -- NULL = open-ended
    @IsDefault        BIT = 0,
    @SetByAdminUserId INT,
    @Notes            NVARCHAR(500) = NULL,
    @NewBillingRateId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @NewBillingRateId = -1;

    BEGIN TRY
        -- 1. Rate must be non-negative
        IF @RatePerMinute < 0
            RAISERROR('Rate per minute cannot be negative.', 16, 1);

        -- 2. Date range integrity
        IF @EffectiveTo IS NOT NULL AND @EffectiveTo < @EffectiveFrom
            RAISERROR('Effective To must be on or after Effective From.', 16, 1);

        -- 3. Unique name (case-insensitive)
        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  LTRIM(RTRIM(Name)) = LTRIM(RTRIM(@Name))
        )
        BEGIN
            SET @NewBillingRateId = -2;  -- duplicate name
            RETURN;
        END

        -- 4. No overlapping date ranges for the same active currency
        --    Two ranges overlap when: newFrom <= existTo  AND  newTo >= existFrom
        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  IsActive = 1
              AND  Currency  = @Currency
              AND  @EffectiveFrom <= COALESCE(EffectiveTo,   '9999-12-31')
              AND  COALESCE(@EffectiveTo, '9999-12-31') >= EffectiveFrom
        )
        BEGIN
            SET @NewBillingRateId = -3;  -- date range overlap
            RETURN;
        END

        -- 5. If setting as default, unset all others
        IF @IsDefault = 1
            UPDATE dbo.tblBillingRate SET IsDefault = 0;

        -- 6. Insert
        INSERT INTO dbo.tblBillingRate
            (Name, RatePerMinute, Currency, EffectiveFrom, EffectiveTo,
             IsActive, IsDefault, SetByAdminUserId, Notes, CreatedAt)
        VALUES
            (@Name, @RatePerMinute, @Currency, @EffectiveFrom, @EffectiveTo,
             1, @IsDefault, @SetByAdminUserId, @Notes, GETDATE());

        SET @NewBillingRateId = SCOPE_IDENTITY();
        SELECT @NewBillingRateId AS BillingRateId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error',
                'Error in sp_InsertBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT -1 AS BillingRateId;
    END CATCH
END;
GO

-- ═══════════════════════════════════════════════════════════
--  sp_UpdateBillingRate
--  Updates an existing billing rate. If IsDefault=1, unsets
--  other defaults before updating.
-- ═══════════════════════════════════════════════════════════
CREATE OR ALTER PROCEDURE dbo.sp_UpdateBillingRate
    @BillingRateId INT,
    @Name          NVARCHAR(100),
    @RatePerMinute DECIMAL(10,2),
    @Currency      NVARCHAR(10),
    @EffectiveFrom DATE,                          -- required
    @EffectiveTo   DATE = NULL,                   -- NULL = open-ended
    @IsActive      BIT,
    @IsDefault     BIT = 0,
    @Notes         NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OldIsDefault BIT;

    BEGIN TRY
        -- 1. Existence check
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId)
            RAISERROR('Billing rate not found.', 16, 1);

        -- 2. Rate must be non-negative
        IF @RatePerMinute < 0
            RAISERROR('Rate per minute cannot be negative.', 16, 1);

        -- 3. Date range integrity
        IF @EffectiveTo IS NOT NULL AND @EffectiveTo < @EffectiveFrom
            RAISERROR('Effective To must be on or after Effective From.', 16, 1);

        -- 4. Unique name, excluding self (case-insensitive)
        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  BillingRateId <> @BillingRateId
              AND  LTRIM(RTRIM(Name)) = LTRIM(RTRIM(@Name))
        )
        BEGIN
            SELECT 0 AS Result;  -- duplicate name
            RETURN;
        END

        -- 5. No overlapping date ranges for the same active currency, excluding self
        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  IsActive = 1
              AND  Currency  = @Currency
              AND  BillingRateId <> @BillingRateId
              AND  @EffectiveFrom <= COALESCE(EffectiveTo,   '9999-12-31')
              AND  COALESCE(@EffectiveTo, '9999-12-31') >= EffectiveFrom
        )
        BEGIN
            SELECT 0 AS Result;  -- date range overlap
            RETURN;
        END

        -- 6. Default flag management
        SELECT @OldIsDefault = IsDefault FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        IF @IsDefault = 1 AND @OldIsDefault = 0
            UPDATE dbo.tblBillingRate SET IsDefault = 0 WHERE BillingRateId <> @BillingRateId;

        IF @IsDefault = 0 AND @OldIsDefault = 1
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM dbo.tblBillingRate
                WHERE IsDefault = 1 AND BillingRateId <> @BillingRateId
            )
                RAISERROR('At least one default rate must remain. Cannot unset this rate as default.', 16, 1);
        END;

        -- 7. Apply update
        UPDATE dbo.tblBillingRate
        SET    Name          = @Name,
               RatePerMinute = @RatePerMinute,
               Currency      = @Currency,
               EffectiveFrom = @EffectiveFrom,
               EffectiveTo   = @EffectiveTo,
               IsActive      = @IsActive,
               IsDefault     = @IsDefault,
               Notes         = @Notes
        WHERE  BillingRateId = @BillingRateId;

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error',
                'Error in sp_UpdateBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT 0 AS Result;
    END CATCH
END;
GO


-- ═══════════════════════════════════════════════════════════
--  sp_DeleteBillingRate
--  Deletes a billing rate only if:
--  1. At least one other rate exists
--  2. If this rate is default, at least one other default exists
-- ═══════════════════════════════════════════════════════════
CREATE OR Alter PROCEDURE dbo.sp_DeleteBillingRate
    @BillingRateId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RateName NVARCHAR(100),
            @RateCount INT,
            @IsDefault BIT,
            @DefaultCount INT,
            @BillingRecordCount INT;

    BEGIN TRY
        -- Get rate details
        SELECT @RateName = Name, @IsDefault = IsDefault FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        IF @RateName IS NULL
        BEGIN
            RAISERROR('Billing rate not found.', 16, 1);
            RETURN;
        END;

        -- Count total rates
        SELECT @RateCount = COUNT(*) FROM dbo.tblBillingRate;

        -- Check if this is the only rate
        IF @RateCount = 1
        BEGIN
            RAISERROR('Cannot delete the last billing rate. At least one rate must exist.', 16, 1);
            RETURN;
        END;

        -- If this rate is default, check if another default exists
        IF @IsDefault = 1
        BEGIN
            SELECT @DefaultCount = COUNT(*) FROM dbo.tblBillingRate WHERE IsDefault = 1;

            IF @DefaultCount = 1
            BEGIN
                RAISERROR('Cannot delete the only default rate. At least one default rate must exist.', 16, 1);
                RETURN;
            END;
        END;

        -- Check if this rate is used in billing records (informational, but allow deletion)
        SELECT @BillingRecordCount = COUNT(*) FROM dbo.tblBillingRecord WHERE BillingRateId = @BillingRateId;

        -- Delete the rate
        DELETE FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        -- Log the action
        --INSERT INTO dbo.tblSystemLog
        --    (Category, Type, Message, Source)
        --VALUES
        --    ('Billing', 'RateDeleted',
        --     'Billing rate deleted: ' + @RateName + ' (was used in ' + CAST(@BillingRecordCount AS NVARCHAR(10)) + ' records)',
        --     'Admin');

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('Billing', 'Error', 'Error in sp_DeleteBillingRate: ' + ERROR_MESSAGE(), 'Admin');

        SELECT 0 AS Result;
    END CATCH
END;
GO


-- ═══════════════════════════════════════════════════════════
--  sp_GetAllBillingRates
--  Retrieves all billing rates ordered by IsDefault DESC, then by CreatedAt DESC
-- ═══════════════════════════════════════════════════════════
CREATE OR ALTER PROCEDURE dbo.sp_GetAllBillingRates
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        SELECT BillingRateId,
               Name,
               RatePerMinute,
               Currency,
               EffectiveFrom,
               EffectiveTo,
               IsActive,
               IsDefault,
               SetByAdminUserId,
               Notes,
               CreatedAt
        FROM dbo.tblBillingRate
        ORDER BY IsDefault DESC, CreatedAt DESC;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('Billing', 'Error', 'Error in sp_GetAllBillingRates: ' + ERROR_MESSAGE(), 'Admin');
    END CATCH
END;
GO


-- ═══════════════════════════════════════════════════════════
--  sp_SetDefaultBillingRate
--  Sets a specific rate as default and unsets all others
-- ═══════════════════════════════════════════════════════════
CREATE or Alter PROCEDURE dbo.sp_SetDefaultBillingRate
    @BillingRateId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RateName NVARCHAR(100);

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId)
        BEGIN
            RAISERROR('Billing rate not found.', 16, 1);
            RETURN;
        END;

        SELECT @RateName = Name FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        -- Unset all other defaults
        UPDATE dbo.tblBillingRate SET IsDefault = 0 WHERE BillingRateId <> @BillingRateId;

        -- Set this one as default
        UPDATE dbo.tblBillingRate SET IsDefault = 1 WHERE BillingRateId = @BillingRateId;

        -- Log the action
        --INSERT INTO dbo.tblSystemLog
        --    (Category, Type, Message, Source)
        --VALUES
        --    ('Billing', 'DefaultRateSet', 'Default billing rate set to: ' + @RateName, 'Admin');

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('Billing', 'Error', 'Error in sp_SetDefaultBillingRate: ' + ERROR_MESSAGE(), 'Admin');

        SELECT 0 AS Result;
    END CATCH
END;
GO

-- ============================================================
-- PATCH: FullName required — NOT NULL column + updated SPs
-- Run against ClientServerSessionDB after the main SQL script.
-- ============================================================

USE ClientServerSessionDB;
GO

-- Step 1: back-fill any NULLs before enforcing NOT NULL
UPDATE dbo.tblUser
SET    FullName = LTRIM(RTRIM(ISNULL(Username, 'Unknown')))
WHERE  FullName IS NULL OR LTRIM(RTRIM(FullName)) = '';
GO

-- Step 2: enforce NOT NULL on the column
ALTER TABLE dbo.tblUser
    ALTER COLUMN FullName NVARCHAR(100) NOT NULL;
GO

-- Step 3: update sp_RegisterClientUser (make @FullName required, add guard)
CREATE OR ALTER PROCEDURE dbo.sp_RegisterClientUser
    @Username     NVARCHAR(50),
    @PasswordHash NVARCHAR(255),
    @FullName     NVARCHAR(100),
    @Phone        NVARCHAR(30)  = NULL,
    @Address      NVARCHAR(200) = NULL,
    @AdminUserId  INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId INT;
    BEGIN TRY
        IF LTRIM(RTRIM(ISNULL(@FullName, ''))) = ''
            RAISERROR('Full name is required.', 16, 1);

        INSERT INTO dbo.tblUser
            (Username, PasswordHash, FullName, Role, Status,
             Phone, Address, CreatedByUserId)
        VALUES
            (@Username, @PasswordHash, @FullName, 'ClientUser', 'Active',
             @Phone, @Address, @AdminUserId);

        SET @UserId = SCOPE_IDENTITY();
        SELECT @UserId AS UserId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2627, 2601)
        BEGIN
            SELECT -1 AS UserId;
            INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
            VALUES ('Auth', 'DuplicateUser',
                'Duplicate username attempt: "' + @Username + '"', 'Server');
        END
        ELSE
        BEGIN
            SELECT 0 AS UserId;
            INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
            VALUES ('System', 'Error',
                'sp_RegisterClientUser: ' + ERROR_MESSAGE(), 'Server');
        END
    END CATCH
END
GO

-- Step 4: create sp_UpdateClientUser
CREATE OR ALTER PROCEDURE dbo.sp_UpdateClientUser
    @UserId             INT,
    @FullName           NVARCHAR(100),
    @Phone              NVARCHAR(30)  = NULL,
    @Address            NVARCHAR(200) = NULL,
    @ProfilePicturePath NVARCHAR(500) = NULL   -- NULL = keep existing path
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF LTRIM(RTRIM(ISNULL(@FullName, ''))) = ''
            RAISERROR('Full name is required.', 16, 1);

        UPDATE dbo.tblUser
        SET    FullName           = @FullName,
               Phone              = @Phone,
               Address            = @Address,
               ProfilePicturePath = CASE
                                        WHEN @ProfilePicturePath IS NOT NULL THEN @ProfilePicturePath
                                        ELSE ProfilePicturePath
                                    END
        WHERE  UserId = @UserId AND Role = 'ClientUser';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error',
            'sp_UpdateClientUser: ' + ERROR_MESSAGE(), 'Server');
        THROW;
    END CATCH
END
GO

SELECT 'FullName PATCH COMPLETE' AS Status;
GO


-- ============================================================
-- PATCH: ProfilePicturePath column + sp_DeleteClientUser
-- Run against ClientServerSessionDB after the main SQL script.
-- ============================================================

USE ClientServerSessionDB;
GO

-- Step 1: add ProfilePicturePath column if it does not exist
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.tblUser') AND name = 'ProfilePicturePath'
)
BEGIN
    ALTER TABLE dbo.tblUser ADD ProfilePicturePath NVARCHAR(500) NULL;
END
GO

-- Step 2: create / update sp_DeleteClientUser
CREATE OR ALTER PROCEDURE dbo.sp_DeleteClientUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF EXISTS (SELECT 1 FROM dbo.tblSession WHERE UserId = @UserId)
        BEGIN
            SELECT -1 AS Result;
            RETURN;
        END

        DELETE FROM dbo.tblUser
        WHERE  UserId = @UserId AND Role = 'ClientUser';

        SELECT @@ROWCOUNT AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error',
            'sp_DeleteClientUser: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END
GO

SELECT 'ProfilePicturePath + sp_DeleteClientUser PATCH COMPLETE' AS Status;
GO


-- ============================================================
-- PATCH: sp_StartSession — duplicate session guard
-- Run against ClientServerSessionDB after the main SQL script.
-- ============================================================

USE ClientServerSessionDB;
GO

CREATE OR ALTER PROCEDURE dbo.sp_StartSession
    @UserId INT,
    @ClientMachineId INT,
    @SelectedDurationMinutes INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SessionId INT;

    BEGIN TRY
        -- Guard: user already has an active session
        IF EXISTS (
            SELECT 1 FROM dbo.tblSession
            WHERE  UserId = @UserId AND Status = 'Active'
        )
        BEGIN
            SELECT -1 AS SessionId;
            RETURN;
        END

        -- Guard: machine already has an active session
        IF EXISTS (
            SELECT 1 FROM dbo.tblSession
            WHERE  ClientMachineId = @ClientMachineId AND Status = 'Active'
        )
        BEGIN
            SELECT -2 AS SessionId;
            RETURN;
        END

        INSERT INTO dbo.tblSession
            (UserId, ClientMachineId, LoginAt, StartedAt, SelectedDurationMinutes, Status)
        VALUES
            (@UserId, @ClientMachineId, GETDATE(), GETDATE(), @SelectedDurationMinutes, 'Active');

        SET @SessionId = SCOPE_IDENTITY();

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES
            ('Session', 'StartSession',
             'Session started for ' + CAST(@SelectedDurationMinutes AS NVARCHAR(10)) + ' minutes',
             'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT @SessionId AS SessionId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'Error in sp_StartSession: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0 AS SessionId;
    END CATCH
END;
GO

SELECT 'sp_StartSession duplicate-session guard PATCH COMPLETE' AS Status;
GO


-- ============================================================
-- PATCH: sp_InsertBillingRate / sp_UpdateBillingRate
-- Remove duplicate C#-layer checks; SPs now own the validation.
-- Run against ClientServerSessionDB after the main SQL script.
-- ============================================================

USE ClientServerSessionDB;
GO

-- sp_InsertBillingRate: dup-name returns -2 via OUTPUT param, overlap returns -3 via OUTPUT param
CREATE OR ALTER PROCEDURE dbo.sp_InsertBillingRate
    @Name             NVARCHAR(100),
    @RatePerMinute    DECIMAL(10,2),
    @Currency         NVARCHAR(10),
    @EffectiveFrom    DATE,
    @EffectiveTo      DATE = NULL,
    @IsDefault        BIT = 0,
    @SetByAdminUserId INT,
    @Notes            NVARCHAR(500) = NULL,
    @NewBillingRateId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @NewBillingRateId = -1;

    BEGIN TRY
        IF @RatePerMinute < 0
            RAISERROR('Rate per minute cannot be negative.', 16, 1);

        IF @EffectiveTo IS NOT NULL AND @EffectiveTo < @EffectiveFrom
            RAISERROR('Effective To must be on or after Effective From.', 16, 1);

        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  LTRIM(RTRIM(Name)) = LTRIM(RTRIM(@Name))
        )
        BEGIN
            SET @NewBillingRateId = -2;
            RETURN;
        END

        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  IsActive = 1
              AND  Currency  = @Currency
              AND  @EffectiveFrom <= COALESCE(EffectiveTo,   '9999-12-31')
              AND  COALESCE(@EffectiveTo, '9999-12-31') >= EffectiveFrom
        )
        BEGIN
            SET @NewBillingRateId = -3;
            RETURN;
        END

        IF @IsDefault = 1
            UPDATE dbo.tblBillingRate SET IsDefault = 0;

        INSERT INTO dbo.tblBillingRate
            (Name, RatePerMinute, Currency, EffectiveFrom, EffectiveTo,
             IsActive, IsDefault, SetByAdminUserId, Notes, CreatedAt)
        VALUES
            (@Name, @RatePerMinute, @Currency, @EffectiveFrom, @EffectiveTo,
             1, @IsDefault, @SetByAdminUserId, @Notes, GETDATE());

        SET @NewBillingRateId = SCOPE_IDENTITY();
        SELECT @NewBillingRateId AS BillingRateId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error',
                'Error in sp_InsertBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT -1 AS BillingRateId;
    END CATCH
END;
GO

-- sp_UpdateBillingRate: dup-name and overlap return SELECT 0; RETURN (no RAISERROR, no spurious Error log)
CREATE OR ALTER PROCEDURE dbo.sp_UpdateBillingRate
    @BillingRateId INT,
    @Name          NVARCHAR(100),
    @RatePerMinute DECIMAL(10,2),
    @Currency      NVARCHAR(10),
    @EffectiveFrom DATE,
    @EffectiveTo   DATE = NULL,
    @IsActive      BIT,
    @IsDefault     BIT = 0,
    @Notes         NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OldIsDefault BIT;

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId)
            RAISERROR('Billing rate not found.', 16, 1);

        IF @RatePerMinute < 0
            RAISERROR('Rate per minute cannot be negative.', 16, 1);

        IF @EffectiveTo IS NOT NULL AND @EffectiveTo < @EffectiveFrom
            RAISERROR('Effective To must be on or after Effective From.', 16, 1);

        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  BillingRateId <> @BillingRateId
              AND  LTRIM(RTRIM(Name)) = LTRIM(RTRIM(@Name))
        )
        BEGIN
            SELECT 0 AS Result;
            RETURN;
        END

        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  IsActive = 1
              AND  Currency  = @Currency
              AND  BillingRateId <> @BillingRateId
              AND  @EffectiveFrom <= COALESCE(EffectiveTo,   '9999-12-31')
              AND  COALESCE(@EffectiveTo, '9999-12-31') >= EffectiveFrom
        )
        BEGIN
            SELECT 0 AS Result;
            RETURN;
        END

        SELECT @OldIsDefault = IsDefault FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        IF @IsDefault = 1 AND @OldIsDefault = 0
            UPDATE dbo.tblBillingRate SET IsDefault = 0 WHERE BillingRateId <> @BillingRateId;

        IF @IsDefault = 0 AND @OldIsDefault = 1
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM dbo.tblBillingRate
                WHERE IsDefault = 1 AND BillingRateId <> @BillingRateId
            )
                RAISERROR('At least one default rate must remain. Cannot unset this rate as default.', 16, 1);
        END;

        UPDATE dbo.tblBillingRate
        SET    Name          = @Name,
               RatePerMinute = @RatePerMinute,
               Currency      = @Currency,
               EffectiveFrom = @EffectiveFrom,
               EffectiveTo   = @EffectiveTo,
               IsActive      = @IsActive,
               IsDefault     = @IsDefault,
               Notes         = @Notes
        WHERE  BillingRateId = @BillingRateId;

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error',
                'Error in sp_UpdateBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT 0 AS Result;
    END CATCH
END;
GO

SELECT 'Billing rate SP deduplication PATCH COMPLETE' AS Status;
GO


-- ============================================================
-- PATCH: Consolidate sp_UpdateClientUser — add ProfilePicturePath
-- Run against ClientServerSessionDB after all previous patches.
-- This supersedes the sp_UpdateClientUser defined in the FullName PATCH.
-- ============================================================
USE ClientServerSessionDB;
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateClientUser
    @UserId             INT,
    @FullName           NVARCHAR(100),
    @Phone              NVARCHAR(30)  = NULL,
    @Address            NVARCHAR(200) = NULL,
    @ProfilePicturePath NVARCHAR(500) = NULL   -- NULL = keep existing path
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF LTRIM(RTRIM(ISNULL(@FullName, ''))) = ''
            RAISERROR('Full name is required.', 16, 1);

        UPDATE dbo.tblUser
        SET    FullName           = @FullName,
               Phone              = @Phone,
               Address            = @Address,
               ProfilePicturePath = CASE
                                        WHEN @ProfilePicturePath IS NOT NULL THEN @ProfilePicturePath
                                        ELSE ProfilePicturePath
                                    END
        WHERE  UserId = @UserId AND Role = 'ClientUser';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error',
            'sp_UpdateClientUser: ' + ERROR_MESSAGE(), 'Server');
        THROW;
    END CATCH
END
GO

SELECT 'UpdateClientUser consolidation PATCH COMPLETE' AS Status;
GO


-- ============================================================
-- PATCH: Payment tracking — IsPaid / PaidAt / ReceivedByAdminId
-- Run against ClientServerSessionDB after all previous patches.
-- ============================================================
USE ClientServerSessionDB;
GO

-- Step 1: add columns to tblBillingRecord
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblBillingRecord') AND name = 'IsPaid')
    ALTER TABLE dbo.tblBillingRecord ADD IsPaid BIT NOT NULL CONSTRAINT DF_tblBillingRecord_IsPaid DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblBillingRecord') AND name = 'PaidAt')
    ALTER TABLE dbo.tblBillingRecord ADD PaidAt DATETIME NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblBillingRecord') AND name = 'ReceivedByAdminId')
    ALTER TABLE dbo.tblBillingRecord ADD ReceivedByAdminId INT NULL;
GO

-- Step 2: sp_GetBillingRecords
CREATE OR ALTER PROCEDURE dbo.sp_GetBillingRecords
    @UnpaidOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        br.BillingRecordId,
        br.SessionId,
        u.Username,
        u.FullName,
        cm.ClientCode      AS MachineCode,
        br.BillableMinutes,
        br.Amount,
        brate.Currency,
        br.CalculatedAt,
        br.IsPaid,
        br.PaidAt,
        br.ReceivedByAdminId
    FROM dbo.tblBillingRecord br
    INNER JOIN dbo.tblSession       s     ON s.SessionId          = br.SessionId
    INNER JOIN dbo.tblUser          u     ON u.UserId              = s.UserId
    INNER JOIN dbo.tblClientMachine cm    ON cm.ClientMachineId    = s.ClientMachineId
    INNER JOIN dbo.tblBillingRate   brate ON brate.BillingRateId   = br.BillingRateId
    WHERE br.Status = 'Finalized'
      AND (@UnpaidOnly = 0 OR br.IsPaid = 0)
    ORDER BY br.CalculatedAt DESC;
END;
GO

-- Step 3: sp_MarkBillingRecordPaid
CREATE OR ALTER PROCEDURE dbo.sp_MarkBillingRecordPaid
    @BillingRecordId INT,
    @AdminUserId     INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE BillingRecordId = @BillingRecordId)
        BEGIN
            SELECT 0 AS Result; RETURN;
        END;

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE BillingRecordId = @BillingRecordId AND IsPaid = 1)
        BEGIN
            SELECT -1 AS Result; RETURN;
        END;

        UPDATE dbo.tblBillingRecord
        SET    IsPaid            = 1,
               PaidAt            = GETDATE(),
               ReceivedByAdminId = @AdminUserId
        WHERE  BillingRecordId = @BillingRecordId;

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Info',
            'BillingRecord ' + CAST(@BillingRecordId AS VARCHAR) +
            ' marked paid by admin ' + CAST(@AdminUserId AS VARCHAR), 'Server');

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_MarkBillingRecordPaid: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END;
GO

SELECT 'Payment tracking PATCH COMPLETE' AS Status;
GO


-- ============================================================
-- PATCH: Orphan Session Management
--   1. Fix sp_CalculateSessionBilling to use EndedAt (when set)
--      instead of GETDATE() so orphan sessions bill for actual
--      elapsed time, not for "now".
--   2. Add sp_TerminateOrphanSessions: server-side orphan cleanup
--      called by TerminateOrphanSession WCF method on client startup.
-- Run against ClientServerSessionDB after all previous patches.
-- ============================================================
USE ClientServerSessionDB;
GO

-- Step 1: patch sp_CalculateSessionBilling
--   Old: DATEDIFF(MINUTE, StartedAt, GETDATE())
--   New: DATEDIFF(MINUTE, StartedAt, COALESCE(EndedAt, GETDATE()))
--   Effect: when EndedAt has been pre-set to the crash/orphan time,
--           billing uses that time; for in-progress sessions EndedAt
--           is NULL so GETDATE() is used as before.
CREATE OR ALTER PROCEDURE dbo.sp_CalculateSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BillingRateId  INT,
            @RatePerMinute  DECIMAL(10,2),
            @ElapsedMinutes INT,
            @Amount         DECIMAL(10,2);

    BEGIN TRY
        -- Use EndedAt when already set (orphan / crash path); GETDATE() for live sessions.
        SELECT @ElapsedMinutes = DATEDIFF(MINUTE, StartedAt, COALESCE(EndedAt, GETDATE()))
        FROM   dbo.tblSession
        WHERE  SessionId = @SessionId;

        SELECT @BillingRateId = BillingRateId,
               @RatePerMinute = RatePerMinute
        FROM   dbo.tblBillingRate
        WHERE  IsActive = 1 AND IsDefault = 1;

        IF @BillingRateId IS NULL
        BEGIN
            SELECT TOP 1
                @BillingRateId = BillingRateId,
                @RatePerMinute = RatePerMinute
            FROM dbo.tblBillingRate
            WHERE IsActive = 1
            ORDER BY CreatedAt DESC;
        END;

        SET @Amount = ISNULL(@ElapsedMinutes, 0) * ISNULL(@RatePerMinute, 0);

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE SessionId = @SessionId)
        BEGIN
            UPDATE dbo.tblBillingRecord
            SET BillableMinutes = @ElapsedMinutes,
                Amount          = @Amount,
                BillingRateId   = @BillingRateId,
                CalculatedAt    = GETDATE()
            WHERE SessionId = @SessionId;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tblBillingRecord
                (SessionId, BillingRateId, BillableMinutes, Amount, Status)
            VALUES
                (@SessionId, @BillingRateId, @ElapsedMinutes, @Amount, 'Running');
        END;

        SELECT @Amount AS Amount;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId)
        VALUES
            ('Billing', 'Error',
             'Error in sp_CalculateSessionBilling: ' + ERROR_MESSAGE(),
             'Server', @SessionId);
        SELECT 0 AS Amount;
    END CATCH
END;
GO

SELECT 'sp_CalculateSessionBilling EndedAt fix PATCH COMPLETE' AS Status;
GO

-- ============================================================
--  PATCH A: Drop IP unique constraint (informational column only)
-- ============================================================
IF EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE name = 'UQ_tblClientMachine_IP'
      AND parent_object_id = OBJECT_ID('dbo.tblClientMachine')
)
    ALTER TABLE dbo.tblClientMachine DROP CONSTRAINT UQ_tblClientMachine_IP;
GO

SELECT 'UQ_tblClientMachine_IP constraint dropped' AS Status;
GO

-- ============================================================
--  PATCH B: Rebuild sp_RegisterClient
--  - No IP uniqueness check (removed)
--  - MAC-based fallback: if a row exists with the same MAC but
--    a different ClientCode (e.g. old "CL001" vs new "CL-AABBCC..."),
--    update that row's ClientCode to the new value so the machine
--    keeps its history instead of getting a duplicate row.
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.sp_RegisterClient
    @ClientCode  NVARCHAR(50),
    @MachineName NVARCHAR(50),
    @IPAddress   NVARCHAR(45),
    @MACAddress  NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MachineId INT;

    BEGIN TRY
        -- 1. Exact ClientCode match → normal update
        IF EXISTS (SELECT 1 FROM dbo.tblClientMachine WHERE ClientCode = @ClientCode)
        BEGIN
            UPDATE dbo.tblClientMachine
            SET MachineName = @MachineName,
                IPAddress   = @IPAddress,
                MACAddress  = ISNULL(@MACAddress, MACAddress),
                Status      = 'Idle',
                LastSeenAt  = GETDATE()
            WHERE ClientCode = @ClientCode;

            SELECT @MachineId = ClientMachineId
            FROM   dbo.tblClientMachine
            WHERE  ClientCode = @ClientCode;
        END
        -- 2. Same MAC, different ClientCode → adopt the existing row and update its code.
        --    This handles machines that were previously registered with a manual code
        --    (e.g. CL001) and are now auto-generating from their MAC.
        ELSE IF @MACAddress IS NOT NULL
            AND EXISTS (SELECT 1 FROM dbo.tblClientMachine WHERE MACAddress = @MACAddress)
        BEGIN
            UPDATE dbo.tblClientMachine
            SET ClientCode  = @ClientCode,
                MachineName = @MachineName,
                IPAddress   = @IPAddress,
                Status      = 'Idle',
                LastSeenAt  = GETDATE()
            WHERE MACAddress = @MACAddress;

            SELECT @MachineId = ClientMachineId
            FROM   dbo.tblClientMachine
            WHERE  MACAddress = @MACAddress;
        END
        -- 3. Genuinely new machine → insert
        ELSE
        BEGIN
            INSERT INTO dbo.tblClientMachine
                (ClientCode, MachineName, IPAddress, MACAddress, Status, IsActive)
            VALUES
                (@ClientCode, @MachineName, @IPAddress, @MACAddress, 'Idle', 1);

            SET @MachineId = SCOPE_IDENTITY();
        END;

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, ClientMachineId)
        VALUES
            ('System', 'ClientRegistered',
             'Client ' + @ClientCode + ' (' + @MachineName + ') registered/updated',
             'Server', @MachineId);

        SELECT @MachineId AS ClientMachineId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'sp_RegisterClient: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0 AS ClientMachineId;
    END CATCH
END;
GO

SELECT 'sp_RegisterClient patch COMPLETE' AS Status;
GO

-- ============================================================
--  PATCH C: Add MissedHeartbeats column to tblClientMachine
--  Supports heartbeat grace counter: machine is only marked
--  Offline after 3 consecutive missed scans (~3 min), preventing
--  false-positive offline detection under local dev load.
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'tblClientMachine' AND COLUMN_NAME = 'MissedHeartbeats'
)
    ALTER TABLE dbo.tblClientMachine
        ADD MissedHeartbeats INT NOT NULL
            CONSTRAINT DF_tblClientMachine_MissedHeartbeats DEFAULT (0);
GO

SELECT 'PATCH C: MissedHeartbeats column added to tblClientMachine' AS Status;
GO
