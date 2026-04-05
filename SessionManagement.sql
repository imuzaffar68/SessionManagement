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
    FullName          NVARCHAR(100) NULL,
    Role              NVARCHAR(20) NOT NULL,
    Status            NVARCHAR(20) NOT NULL,
    Phone             NVARCHAR(30) NULL,
    Address           NVARCHAR(200) NULL,
    CreatedByUserId   INT NULL,
    CreatedAt         DATETIME NOT NULL CONSTRAINT DF_tblUser_CreatedAt DEFAULT (GETDATE()),
    LastLoginAt       DATETIME NULL,
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
    CONSTRAINT UQ_tblClientMachine_IP UNIQUE (IPAddress),
    CONSTRAINT UQ_tblClientMachine_ClientCode UNIQUE (ClientCode), --v2
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

        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES
            ('Security', 'Alert',
             'Security Alert: ' + @ActivityTypeName + ' - ' + @Details,
             'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog
            (Category, Type, Message, Source)
        VALUES
            ('System', 'Error', 'Error in sp_LogSecurityAlert: ' + ERROR_MESSAGE(), 'Server');

        SELECT 0 AS Result;
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
        IF EXISTS (
            SELECT 1
            FROM dbo.tblClientMachine
            WHERE IPAddress = @IPAddress
              AND ClientCode <> @ClientCode
        )
        BEGIN
            RAISERROR('IPAddress already assigned to another client.', 16, 1);
            RETURN;
        END;

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
    @Username NVARCHAR(50),
    @PasswordHash NVARCHAR(255),
    @FullName NVARCHAR(100) = NULL,
    @Phone NVARCHAR(30) = NULL,
    @Address NVARCHAR(200) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId INT;

    BEGIN TRY
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

            INSERT INTO dbo.tblSystemLog
            (
                Category, Type, Message, Source
            )
            VALUES
            (
                'User',
                'Duplicate',
                'Duplicate username attempt: "' + @Username + '"',
                'Server'
            );
        END
        ELSE
        BEGIN
            SELECT 0 AS UserId;

            INSERT INTO dbo.tblSystemLog
            (
                Category, Type, Message, Source
            )
            VALUES
            (
                'System',
                'Error',
                'sp_RegisterClientUser: ' + ERROR_MESSAGE(),
                'Server'
            );
        END
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

-- Billing Rates
INSERT INTO dbo.tblBillingRate
    (Name, RatePerMinute, Currency, IsActive, IsDefault)
VALUES
    ('Standard Rate', 0.50, 'USD', 1, 1),
    ('Premium Rate', 1.00, 'USD', 1, 0),
    ('Discount Rate', 0.25, 'USD', 1, 0);
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
    @FullName     NVARCHAR(100) = NULL,
    @Phone        NVARCHAR(30)  = NULL,
    @Address      NVARCHAR(200) = NULL,
    @AdminUserId  INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId INT;
    BEGIN TRY
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
            VALUES ('Auth', 'DuplicateUser',           -- fixed: was 'User'
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