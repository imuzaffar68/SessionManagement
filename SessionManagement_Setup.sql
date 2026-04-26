-- ============================================================
--  CLIENT-SERVER SESSION MANAGEMENT SYSTEM
--  Database Setup Script  |  SQL Server 2019+
--  Version : 2.0  |  Safe re-run at every level
-- ============================================================
--
--  USAGE GUIDE
--  ───────────────────────────────────────────────────────────
--  This script is SAFE to run multiple times.
--
--  Level 1 — Fresh install (empty server or no database)
--    Run as-is.  Everything is created from scratch.
--
--  Level 2 — Existing database with DATA (production update)
--    Run as-is.  Tables with data are NEVER dropped.
--    Stored procedures and views are updated (CREATE OR ALTER).
--    Seed rows are only inserted if they do not already exist.
--
--  Level 3 — Developer full reset (destroy all data)
--    Scroll to SECTION 8 at the bottom, uncomment the block,
--    run SECTION 8 alone first, then re-run the full script.
--
--  HOW TO RUN
--    SSMS  : Open this file → Connect → F5
--    sqlcmd: sqlcmd -S localhost\SQLEXPRESS -E -i SessionManagement_Setup.sql
--
--  ⚠  NEVER run Section 8 on a production database.
-- ============================================================

USE master;
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 1 ─ DATABASE
-- ════════════════════════════════════════════════════════════

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'ClientServerSessionDB')
BEGIN
    CREATE DATABASE ClientServerSessionDB;
    PRINT 'Database ClientServerSessionDB created.';
END
ELSE
    PRINT 'Database ClientServerSessionDB already exists — skipped.';
GO

USE ClientServerSessionDB;
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 2 ─ TABLES
--  Safe: tables are created only if they do not exist.
--  Tables that already contain data are never touched.
-- ════════════════════════════════════════════════════════════

-- ── 2.1  tblUser ─────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblUser') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblUser (
        UserId             INT           IDENTITY(1,1) NOT NULL,
        Username           NVARCHAR(50)  NOT NULL,
        PasswordHash       NVARCHAR(255) NOT NULL,
        FullName           NVARCHAR(100) NOT NULL,
        Role               NVARCHAR(20)  NOT NULL,
        Status             NVARCHAR(20)  NOT NULL,
        Phone              NVARCHAR(30)  NULL,
        Address            NVARCHAR(200) NULL,
        CreatedByUserId    INT           NULL,
        CreatedAt          DATETIME      NOT NULL CONSTRAINT DF_tblUser_CreatedAt    DEFAULT (GETDATE()),
        LastLoginAt        DATETIME      NULL,
        ProfilePicturePath NVARCHAR(500) NULL,

        CONSTRAINT PK_tblUser            PRIMARY KEY (UserId),
        CONSTRAINT UQ_tblUser_Username   UNIQUE      (Username),
        CONSTRAINT CK_tblUser_Role       CHECK (Role   IN ('Admin', 'ClientUser')),
        CONSTRAINT CK_tblUser_Status     CHECK (Status IN ('Active', 'Blocked', 'Disabled'))
    );

    -- Self-referencing FK added after table creation
    ALTER TABLE dbo.tblUser
    ADD CONSTRAINT FK_tblUser_CreatedByUser
        FOREIGN KEY (CreatedByUserId) REFERENCES dbo.tblUser (UserId)
        ON DELETE NO ACTION ON UPDATE NO ACTION;

    PRINT 'Table dbo.tblUser created.';
END
ELSE
    PRINT 'Table dbo.tblUser already exists — skipped.';
GO

-- ── 2.2  tblClientMachine ────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblClientMachine') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblClientMachine (
        ClientMachineId  INT           IDENTITY(1,1) NOT NULL,
        ClientCode       NVARCHAR(50)  NOT NULL,
        MachineName      NVARCHAR(50)  NOT NULL,
        IPAddress        NVARCHAR(45)  NOT NULL,
        MACAddress       NVARCHAR(50)  NULL,
        Location         NVARCHAR(100) NULL,
        Status           NVARCHAR(20)  NOT NULL,
        LastSeenAt       DATETIME      NOT NULL CONSTRAINT DF_tblClientMachine_LastSeenAt      DEFAULT (GETDATE()),
        IsActive         BIT           NOT NULL CONSTRAINT DF_tblClientMachine_IsActive        DEFAULT (1),
        MissedHeartbeats INT           NOT NULL CONSTRAINT DF_tblClientMachine_MissedHeartbeats DEFAULT (0),

        CONSTRAINT PK_tblClientMachine            PRIMARY KEY (ClientMachineId),
        CONSTRAINT UQ_tblClientMachine_ClientCode UNIQUE      (ClientCode),
        CONSTRAINT CK_tblClientMachine_Status     CHECK (Status IN ('Idle', 'Active', 'Offline'))
    );
    PRINT 'Table dbo.tblClientMachine created.';
END
ELSE
    PRINT 'Table dbo.tblClientMachine already exists — skipped.';
GO

-- ── 2.3  tblSession ──────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblSession') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblSession (
        SessionId               INT          IDENTITY(1,1) NOT NULL,
        UserId                  INT          NOT NULL,
        ClientMachineId         INT          NOT NULL,
        LoginAt                 DATETIME     NOT NULL CONSTRAINT DF_tblSession_LoginAt   DEFAULT (GETDATE()),
        StartedAt               DATETIME     NOT NULL CONSTRAINT DF_tblSession_StartedAt DEFAULT (GETDATE()),
        SelectedDurationMinutes INT          NOT NULL,
        ExpectedEndAt           AS (DATEADD(MINUTE, SelectedDurationMinutes, StartedAt)),
        EndedAt                 DATETIME     NULL,
        ActualDurationMinutes   INT          NULL,
        Status                  NVARCHAR(20) NOT NULL,
        TerminationReason       NVARCHAR(30) NULL,

        CONSTRAINT PK_tblSession            PRIMARY KEY (SessionId),
        CONSTRAINT CK_tblSession_Status     CHECK (Status IN ('Pending','Active','Completed','Expired','Terminated','Cancelled')),
        -- Accepts all termination codes used by client, server expiry, admin, and orphan recovery
        CONSTRAINT CK_tblSession_TermReason CHECK (
            TerminationReason IS NULL OR TerminationReason IN (
                'Auto', 'Manual', 'AutoExpiry', 'AdminTerminate',
                'UserLogout', 'SystemError', 'Crash', 'OrphanTerminated'
            )
        ),
        CONSTRAINT FK_tblSession_tblUser           FOREIGN KEY (UserId)         REFERENCES dbo.tblUser         (UserId)         ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT FK_tblSession_tblClientMachine  FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine (ClientMachineId) ON DELETE NO ACTION ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblSession created.';
END
ELSE
    PRINT 'Table dbo.tblSession already exists — skipped.';
GO

-- ── 2.4  tblSessionImage ─────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblSessionImage') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblSessionImage (
        SessionImageId INT           IDENTITY(1,1) NOT NULL,
        SessionId      INT           NOT NULL,
        CapturedAt     DATETIME      NOT NULL CONSTRAINT DF_tblSessionImage_CapturedAt DEFAULT (GETDATE()),
        CaptureStatus  NVARCHAR(30)  NOT NULL,
        UploadStatus   NVARCHAR(20)  NOT NULL,
        ImagePath      NVARCHAR(500) NULL,
        Notes          NVARCHAR(500) NULL,

        CONSTRAINT PK_tblSessionImage               PRIMARY KEY (SessionImageId),
        CONSTRAINT UQ_tblSessionImage_Session        UNIQUE      (SessionId),
        CONSTRAINT CK_tblSessionImage_CaptureStatus  CHECK (CaptureStatus IN ('Captured','CameraUnavailable','Skipped','Failed')),
        CONSTRAINT CK_tblSessionImage_UploadStatus   CHECK (UploadStatus  IN ('Sent','Pending','Failed')),
        CONSTRAINT FK_tblSessionImage_tblSession     FOREIGN KEY (SessionId) REFERENCES dbo.tblSession (SessionId) ON DELETE CASCADE ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblSessionImage created.';
END
ELSE
    PRINT 'Table dbo.tblSessionImage already exists — skipped.';
GO

-- ── 2.5  tblBillingRate ──────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblBillingRate') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblBillingRate (
        BillingRateId    INT            IDENTITY(1,1) NOT NULL,
        Name             NVARCHAR(100)  NOT NULL,
        RatePerMinute    DECIMAL(10,2)  NOT NULL,
        Currency         NVARCHAR(10)   NOT NULL,
        EffectiveFrom    DATE           NOT NULL,
        EffectiveTo      DATE           NULL,
        IsActive         BIT            NOT NULL CONSTRAINT DF_tblBillingRate_IsActive  DEFAULT (1),
        IsDefault        BIT            NOT NULL CONSTRAINT DF_tblBillingRate_IsDefault DEFAULT (0),
        SetByAdminUserId INT            NULL,
        Notes            NVARCHAR(500)  NULL,
        CreatedAt        DATETIME       NOT NULL CONSTRAINT DF_tblBillingRate_CreatedAt DEFAULT (GETDATE()),

        CONSTRAINT PK_tblBillingRate         PRIMARY KEY (BillingRateId),
        CONSTRAINT UQ_tblBillingRate_Name    UNIQUE      (Name),
        CONSTRAINT FK_tblBillingRate_SetByAdmin FOREIGN KEY (SetByAdminUserId) REFERENCES dbo.tblUser (UserId) ON DELETE SET NULL ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblBillingRate created.';
END
ELSE
    PRINT 'Table dbo.tblBillingRate already exists — skipped.';
GO

-- ── 2.6  tblBillingRecord ────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblBillingRecord') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblBillingRecord (
        BillingRecordId   INT           IDENTITY(1,1) NOT NULL,
        SessionId         INT           NOT NULL,
        BillingRateId     INT           NOT NULL,
        BillableMinutes   INT           NOT NULL,
        Amount            DECIMAL(10,2) NOT NULL,
        CalculatedAt      DATETIME      NOT NULL CONSTRAINT DF_tblBillingRecord_CalculatedAt DEFAULT (GETDATE()),
        Status            NVARCHAR(20)  NOT NULL CONSTRAINT DF_tblBillingRecord_Status       DEFAULT ('Running'),
        Remarks           NVARCHAR(500) NULL,
        IsPaid            BIT           NOT NULL CONSTRAINT DF_tblBillingRecord_IsPaid       DEFAULT (0),
        PaidAt            DATETIME      NULL,
        ReceivedByAdminId INT           NULL,

        CONSTRAINT PK_tblBillingRecord            PRIMARY KEY (BillingRecordId),
        CONSTRAINT UQ_tblBillingRecord_Session     UNIQUE      (SessionId),
        CONSTRAINT CK_tblBillingRecord_Status      CHECK (Status IN ('Running','Finalized')),
        CONSTRAINT FK_tblBillingRecord_tblSession  FOREIGN KEY (SessionId)     REFERENCES dbo.tblSession     (SessionId)     ON DELETE CASCADE   ON UPDATE NO ACTION,
        CONSTRAINT FK_tblBillingRecord_tblBillingRate FOREIGN KEY (BillingRateId) REFERENCES dbo.tblBillingRate (BillingRateId) ON DELETE NO ACTION ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblBillingRecord created.';
END
ELSE
    PRINT 'Table dbo.tblBillingRecord already exists — skipped.';
GO

-- ── 2.7  tblActivityType ─────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblActivityType') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblActivityType (
        ActivityTypeId  INT           IDENTITY(1,1) NOT NULL,
        Name            NVARCHAR(100) NOT NULL,
        Description     NVARCHAR(500) NULL,
        DefaultSeverity NVARCHAR(10)  NOT NULL,
        IsActive        BIT           NOT NULL CONSTRAINT DF_tblActivityType_IsActive DEFAULT (1),

        CONSTRAINT PK_tblActivityType          PRIMARY KEY (ActivityTypeId),
        CONSTRAINT CK_tblActivityType_Severity CHECK (DefaultSeverity IN ('Low','Medium','High'))
    );
    PRINT 'Table dbo.tblActivityType created.';
END
ELSE
    PRINT 'Table dbo.tblActivityType already exists — skipped.';
GO

-- ── 2.8  tblAlert ────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblAlert') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblAlert (
        AlertId                   INT            IDENTITY(1,1) NOT NULL,
        ActivityTypeId            INT            NOT NULL,
        SessionId                 INT            NULL,
        ClientMachineId           INT            NOT NULL,
        UserId                    INT            NULL,
        DetectedAt                DATETIME       NOT NULL CONSTRAINT DF_tblAlert_DetectedAt     DEFAULT (GETDATE()),
        Severity                  NVARCHAR(10)   NOT NULL,
        Status                    NVARCHAR(20)   NOT NULL CONSTRAINT DF_tblAlert_Status         DEFAULT ('New'),
        Details                   NVARCHAR(1000) NOT NULL,
        IsNotifiedToAdmin         BIT            NOT NULL CONSTRAINT DF_tblAlert_IsNotified     DEFAULT (0),
        IsAcknowledged            BIT            NOT NULL CONSTRAINT DF_tblAlert_IsAcknowledged DEFAULT (0),
        AcknowledgedByAdminUserId INT            NULL,
        AcknowledgedAt            DATETIME       NULL,

        CONSTRAINT PK_tblAlert                  PRIMARY KEY (AlertId),
        CONSTRAINT CK_tblAlert_Severity         CHECK (Severity IN ('Low','Medium','High')),
        CONSTRAINT CK_tblAlert_Status           CHECK (Status   IN ('New','Acknowledged','Resolved','Closed')),
        CONSTRAINT FK_tblAlert_tblActivityType  FOREIGN KEY (ActivityTypeId)            REFERENCES dbo.tblActivityType  (ActivityTypeId) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT FK_tblAlert_tblSession       FOREIGN KEY (SessionId)                 REFERENCES dbo.tblSession       (SessionId)      ON DELETE SET NULL  ON UPDATE NO ACTION,
        CONSTRAINT FK_tblAlert_tblClientMachine FOREIGN KEY (ClientMachineId)           REFERENCES dbo.tblClientMachine (ClientMachineId) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT FK_tblAlert_tblUser          FOREIGN KEY (UserId)                    REFERENCES dbo.tblUser          (UserId)         ON DELETE SET NULL  ON UPDATE NO ACTION,
        CONSTRAINT FK_tblAlert_AckAdmin         FOREIGN KEY (AcknowledgedByAdminUserId) REFERENCES dbo.tblUser          (UserId)         ON DELETE NO ACTION ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblAlert created.';
END
ELSE
    PRINT 'Table dbo.tblAlert already exists — skipped.';
GO

-- ── 2.9  tblLoginAttempt ─────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblLoginAttempt') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblLoginAttempt (
        LoginAttemptId  INT           IDENTITY(1,1) NOT NULL,
        ClientMachineId INT           NULL,
        UserId          INT           NULL,
        UsernameEntered NVARCHAR(50)  NOT NULL,
        AttemptedAt     DATETIME      NOT NULL CONSTRAINT DF_tblLoginAttempt_AttemptedAt DEFAULT (GETDATE()),
        IsSuccess       BIT           NOT NULL,
        FailureReason   NVARCHAR(30)  NULL,

        CONSTRAINT PK_tblLoginAttempt                  PRIMARY KEY (LoginAttemptId),
        CONSTRAINT FK_tblLoginAttempt_tblClientMachine FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine (ClientMachineId) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT FK_tblLoginAttempt_tblUser          FOREIGN KEY (UserId)          REFERENCES dbo.tblUser          (UserId)         ON DELETE SET NULL  ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblLoginAttempt created.';
END
ELSE
    PRINT 'Table dbo.tblLoginAttempt already exists — skipped.';
GO

-- ── 2.10  tblSystemLog ───────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tblSystemLog') AND type = 'U')
BEGIN
    CREATE TABLE dbo.tblSystemLog (
        SystemLogId     INT            IDENTITY(1,1) NOT NULL,
        LogedAt         DATETIME       NOT NULL CONSTRAINT DF_tblSystemLog_LogedAt DEFAULT (GETDATE()),
        Category        NVARCHAR(20)   NOT NULL,
        Type            NVARCHAR(50)   NOT NULL,
        Message         NVARCHAR(2000) NOT NULL,
        Source          NVARCHAR(10)   NULL,
        SessionId       INT            NULL,
        UserId          INT            NULL,
        ClientMachineId INT            NULL,
        AdminUserId     INT            NULL,

        CONSTRAINT PK_tblSystemLog           PRIMARY KEY (SystemLogId),
        CONSTRAINT CK_tblSystemLog_Category  CHECK (Category IN ('Auth','Session','Billing','Security','System')),
        CONSTRAINT CK_tblSystemLog_Source    CHECK (Source IS NULL OR Source IN ('Client','Server')),
        CONSTRAINT FK_tblSystemLog_Session   FOREIGN KEY (SessionId)       REFERENCES dbo.tblSession       (SessionId)      ON DELETE SET NULL ON UPDATE NO ACTION,
        CONSTRAINT FK_tblSystemLog_User      FOREIGN KEY (UserId)          REFERENCES dbo.tblUser          (UserId)         ON DELETE SET NULL ON UPDATE NO ACTION,
        CONSTRAINT FK_tblSystemLog_Machine   FOREIGN KEY (ClientMachineId) REFERENCES dbo.tblClientMachine (ClientMachineId) ON DELETE SET NULL ON UPDATE NO ACTION,
        CONSTRAINT FK_tblSystemLog_Admin     FOREIGN KEY (AdminUserId)     REFERENCES dbo.tblUser          (UserId)         ON DELETE NO ACTION ON UPDATE NO ACTION
    );
    PRINT 'Table dbo.tblSystemLog created.';
END
ELSE
    PRINT 'Table dbo.tblSystemLog already exists — skipped.';
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 3 ─ INDEXES
--  Safe: each index is created only if it does not exist.
-- ════════════════════════════════════════════════════════════

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblClientMachine_ClientCode'   AND object_id = OBJECT_ID('dbo.tblClientMachine'))
    CREATE INDEX IX_tblClientMachine_ClientCode    ON dbo.tblClientMachine (ClientCode);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblSession_UserId'             AND object_id = OBJECT_ID('dbo.tblSession'))
    CREATE INDEX IX_tblSession_UserId              ON dbo.tblSession       (UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblSession_ClientMachineId'    AND object_id = OBJECT_ID('dbo.tblSession'))
    CREATE INDEX IX_tblSession_ClientMachineId     ON dbo.tblSession       (ClientMachineId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblSession_Status'             AND object_id = OBJECT_ID('dbo.tblSession'))
    CREATE INDEX IX_tblSession_Status              ON dbo.tblSession       (Status);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblBillingRate_DateRange'      AND object_id = OBJECT_ID('dbo.tblBillingRate'))
    CREATE INDEX IX_tblBillingRate_DateRange       ON dbo.tblBillingRate   (EffectiveFrom, EffectiveTo, IsActive, Currency);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblBillingRecord_BillingRateId' AND object_id = OBJECT_ID('dbo.tblBillingRecord'))
    CREATE INDEX IX_tblBillingRecord_BillingRateId ON dbo.tblBillingRecord (BillingRateId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblAlert_SessionId'            AND object_id = OBJECT_ID('dbo.tblAlert'))
    CREATE INDEX IX_tblAlert_SessionId             ON dbo.tblAlert         (SessionId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblAlert_ClientMachineId'      AND object_id = OBJECT_ID('dbo.tblAlert'))
    CREATE INDEX IX_tblAlert_ClientMachineId       ON dbo.tblAlert         (ClientMachineId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblAlert_ActivityTypeId'       AND object_id = OBJECT_ID('dbo.tblAlert'))
    CREATE INDEX IX_tblAlert_ActivityTypeId        ON dbo.tblAlert         (ActivityTypeId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblLoginAttempt_ClientMachineId' AND object_id = OBJECT_ID('dbo.tblLoginAttempt'))
    CREATE INDEX IX_tblLoginAttempt_ClientMachineId ON dbo.tblLoginAttempt (ClientMachineId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblLoginAttempt_UserId'        AND object_id = OBJECT_ID('dbo.tblLoginAttempt'))
    CREATE INDEX IX_tblLoginAttempt_UserId         ON dbo.tblLoginAttempt  (UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblSystemLog_SessionId'        AND object_id = OBJECT_ID('dbo.tblSystemLog'))
    CREATE INDEX IX_tblSystemLog_SessionId         ON dbo.tblSystemLog     (SessionId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblSystemLog_UserId'           AND object_id = OBJECT_ID('dbo.tblSystemLog'))
    CREATE INDEX IX_tblSystemLog_UserId            ON dbo.tblSystemLog     (UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblSystemLog_ClientMachineId'  AND object_id = OBJECT_ID('dbo.tblSystemLog'))
    CREATE INDEX IX_tblSystemLog_ClientMachineId   ON dbo.tblSystemLog     (ClientMachineId);

PRINT 'Indexes verified / created.';
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 4 ─ VIEWS
--  CREATE OR ALTER — always safe, no data involved.
-- ════════════════════════════════════════════════════════════

-- ── 4.1  vw_SessionReport ────────────────────────────────────────────────────
CREATE OR ALTER VIEW dbo.vw_SessionReport
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
    bil.Amount          AS BillingAmount,
    bil.Status          AS BillingStatus
FROM       dbo.tblSession       s
INNER JOIN dbo.tblUser          u   ON u.UserId          = s.UserId
INNER JOIN dbo.tblClientMachine c   ON c.ClientMachineId = s.ClientMachineId
LEFT  JOIN dbo.tblBillingRecord bil ON bil.SessionId      = s.SessionId
LEFT  JOIN dbo.tblBillingRate   br  ON br.BillingRateId   = bil.BillingRateId;
GO
PRINT 'View dbo.vw_SessionReport created/updated.';
GO

-- ── 4.2  vw_ActiveSessionsSummary ────────────────────────────────────────────
CREATE OR ALTER VIEW dbo.vw_ActiveSessionsSummary
AS
SELECT
    COUNT(*)                                                          AS TotalActiveSessions,
    COUNT(DISTINCT s.UserId)                                          AS UniqueUsers,
    COUNT(DISTINCT s.ClientMachineId)                                 AS ActiveClients,
    SUM(CAST(DATEDIFF(MINUTE, s.StartedAt, GETDATE())
            * ISNULL(br.RatePerMinute, 0) AS DECIMAL(10,2)))          AS TotalCurrentBilling
FROM      dbo.tblSession    s
LEFT JOIN dbo.tblBillingRate br ON br.IsActive = 1 AND br.IsDefault = 1
WHERE s.Status = 'Active';
GO
PRINT 'View dbo.vw_ActiveSessionsSummary created/updated.';
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 5 ─ STORED PROCEDURES
--  CREATE OR ALTER — always safe, always picks up latest code.
-- ════════════════════════════════════════════════════════════

-- ── 5.1  sp_StartSession ─────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_StartSession
    @UserId                  INT,
    @ClientMachineId         INT,
    @SelectedDurationMinutes INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SessionId INT;
    BEGIN TRY
        IF EXISTS (SELECT 1 FROM dbo.tblSession WHERE UserId = @UserId AND Status = 'Active')
        BEGIN SELECT -1 AS SessionId; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.tblSession WHERE ClientMachineId = @ClientMachineId AND Status = 'Active')
        BEGIN SELECT -2 AS SessionId; RETURN; END

        INSERT INTO dbo.tblSession
            (UserId, ClientMachineId, LoginAt, StartedAt, SelectedDurationMinutes, Status)
        VALUES
            (@UserId, @ClientMachineId, GETDATE(), GETDATE(), @SelectedDurationMinutes, 'Active');

        SET @SessionId = SCOPE_IDENTITY();

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES ('Session', 'StartSession',
                'Session started for ' + CAST(@SelectedDurationMinutes AS NVARCHAR(10)) + ' minutes',
                'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT @SessionId AS SessionId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_StartSession: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS SessionId;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_StartSession created/updated.';
GO

-- ── 5.2  sp_EndSession ───────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_EndSession
    @SessionId        INT,
    @TerminationReason NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId INT, @MachineId INT, @NewStatus NVARCHAR(20);
    BEGIN TRY
        SELECT @UserId = UserId, @MachineId = ClientMachineId
        FROM   dbo.tblSession WHERE SessionId = @SessionId;

        SET @NewStatus = CASE @TerminationReason
            WHEN 'AdminTerminate'    THEN 'Terminated'
            WHEN 'AutoExpiry'        THEN 'Expired'
            WHEN 'OrphanTerminated'  THEN 'Terminated'
            WHEN 'Crash'             THEN 'Terminated'
            WHEN 'SystemError'       THEN 'Terminated'
            WHEN 'UserLogout'        THEN 'Completed'
            WHEN 'Manual'            THEN 'Completed'
            WHEN 'Auto'              THEN 'Expired'
            ELSE 'Completed'
        END;

        UPDATE dbo.tblSession
        SET    EndedAt               = GETDATE(),
               Status                = @NewStatus,
               TerminationReason     = @TerminationReason,
               ActualDurationMinutes = DATEDIFF(MINUTE, StartedAt, GETDATE())
        WHERE  SessionId = @SessionId;

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES ('Session', 'SessionEnded',
                'Session ' + CAST(@SessionId AS NVARCHAR(20)) + ' ended — ' + @TerminationReason,
                'Server', @SessionId, @UserId, @MachineId);

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_EndSession: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_EndSession created/updated.';
GO

-- ── 5.3  sp_CalculateSessionBilling ──────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_CalculateSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BillingRateId INT, @RatePerMinute DECIMAL(10,2),
            @ElapsedMinutes INT, @Amount DECIMAL(10,2);
    BEGIN TRY
        -- COALESCE(EndedAt, GETDATE()) ensures orphan sessions are billed accurately
        -- when called after EndedAt is already stamped on the row.
        SELECT @ElapsedMinutes = DATEDIFF(MINUTE, StartedAt, COALESCE(EndedAt, GETDATE()))
        FROM   dbo.tblSession WHERE SessionId = @SessionId;

        SELECT TOP 1 @BillingRateId = BillingRateId, @RatePerMinute = RatePerMinute
        FROM   dbo.tblBillingRate
        WHERE  IsActive = 1
          AND  EffectiveFrom <= CAST(GETDATE() AS DATE)
          AND  (EffectiveTo IS NULL OR EffectiveTo >= CAST(GETDATE() AS DATE))
        ORDER  BY EffectiveFrom DESC;

        IF @BillingRateId IS NULL
            SELECT TOP 1 @BillingRateId = BillingRateId, @RatePerMinute = RatePerMinute
            FROM   dbo.tblBillingRate WHERE IsActive = 1 AND IsDefault = 1
            ORDER  BY CreatedAt DESC;

        SET @Amount = ISNULL(@ElapsedMinutes, 0) * ISNULL(@RatePerMinute, 0);

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE SessionId = @SessionId)
            UPDATE dbo.tblBillingRecord
            SET    BillableMinutes = @ElapsedMinutes,
                   Amount          = @Amount,
                   BillingRateId   = @BillingRateId,
                   CalculatedAt    = GETDATE()
            WHERE  SessionId = @SessionId;
        ELSE
            INSERT INTO dbo.tblBillingRecord
                (SessionId, BillingRateId, BillableMinutes, Amount, Status)
            VALUES
                (@SessionId, @BillingRateId, @ElapsedMinutes, @Amount, 'Running');

        SELECT @Amount AS Amount;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, SessionId)
        VALUES ('Billing', 'Error', 'sp_CalculateSessionBilling: ' + ERROR_MESSAGE(), 'Server', @SessionId);
        SELECT 0 AS Amount;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_CalculateSessionBilling created/updated.';
GO

-- ── 5.4  sp_FinalizeSessionBilling ───────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_FinalizeSessionBilling
    @SessionId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        UPDATE dbo.tblBillingRecord
        SET    Status = 'Finalized', CalculatedAt = GETDATE()
        WHERE  SessionId = @SessionId;

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, SessionId)
        VALUES ('Billing', 'BillingFinalized', 'Session billing finalized', 'Server', @SessionId);

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, SessionId)
        VALUES ('System', 'Error', 'sp_FinalizeSessionBilling: ' + ERROR_MESSAGE(), 'Server', @SessionId);
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_FinalizeSessionBilling created/updated.';
GO

-- ── 5.5  sp_GetActiveSessions ────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_GetActiveSessions
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
        DATEDIFF(MINUTE, GETDATE(), s.ExpectedEndAt)                                             AS RemainingMinutes,
        br.BillingRateId,
        br.RatePerMinute,
        DATEDIFF(MINUTE, s.StartedAt, GETDATE())                                                 AS ElapsedMinutes,
        CAST(DATEDIFF(MINUTE, s.StartedAt, GETDATE()) * br.RatePerMinute AS DECIMAL(10,2))      AS CurrentBilling,
        img.ImagePath
    FROM      dbo.tblSession       s
    INNER JOIN dbo.tblUser          u   ON u.UserId          = s.UserId
    INNER JOIN dbo.tblClientMachine c   ON c.ClientMachineId = s.ClientMachineId
    LEFT  JOIN dbo.tblBillingRate   br  ON br.IsActive = 1 AND br.IsDefault = 1
    LEFT  JOIN dbo.tblSessionImage  img ON img.SessionId = s.SessionId AND img.CaptureStatus = 'Captured'
    WHERE  s.Status = 'Active'
    ORDER  BY s.StartedAt DESC;
END;
GO
PRINT 'SP dbo.sp_GetActiveSessions created/updated.';
GO

-- ── 5.6  sp_GetBillingRecords ─────────────────────────────────────────────────
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
    FROM      dbo.tblBillingRecord br
    INNER JOIN dbo.tblSession       s     ON s.SessionId       = br.SessionId
    INNER JOIN dbo.tblUser          u     ON u.UserId           = s.UserId
    INNER JOIN dbo.tblClientMachine cm    ON cm.ClientMachineId = s.ClientMachineId
    INNER JOIN dbo.tblBillingRate   brate ON brate.BillingRateId = br.BillingRateId
    WHERE  br.Status = 'Finalized'
      AND  (@UnpaidOnly = 0 OR br.IsPaid = 0)
    ORDER  BY br.CalculatedAt DESC;
END;
GO
PRINT 'SP dbo.sp_GetBillingRecords created/updated.';
GO

-- ── 5.7  sp_MarkBillingRecordPaid ────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_MarkBillingRecordPaid
    @BillingRecordId INT,
    @AdminUserId     INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE BillingRecordId = @BillingRecordId)
        BEGIN SELECT 0 AS Result; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRecord WHERE BillingRecordId = @BillingRecordId AND IsPaid = 1)
        BEGIN SELECT -1 AS Result; RETURN; END

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
PRINT 'SP dbo.sp_MarkBillingRecordPaid created/updated.';
GO

-- ── 5.8  sp_LogSecurityAlert ─────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_LogSecurityAlert
    @ActivityTypeName NVARCHAR(100),
    @SessionId        INT           = NULL,
    @ClientMachineId  INT           = NULL,
    @UserId           INT           = NULL,
    @Details          NVARCHAR(1000),
    @Severity         NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ActivityTypeId INT;
    BEGIN TRY
        SELECT @ActivityTypeId = ActivityTypeId
        FROM   dbo.tblActivityType WHERE Name = @ActivityTypeName;

        IF @ActivityTypeId IS NULL
        BEGIN
            INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
            VALUES (@ActivityTypeName, NULL, @Severity, 1);
            SET @ActivityTypeId = SCOPE_IDENTITY();
        END

        INSERT INTO dbo.tblAlert
            (ActivityTypeId, SessionId, ClientMachineId, UserId, DetectedAt, Severity, Status, Details)
        VALUES
            (@ActivityTypeId, @SessionId, @ClientMachineId, @UserId, GETDATE(), @Severity, 'New', @Details);

        DECLARE @AlertId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, SessionId, UserId, ClientMachineId)
        VALUES ('Security', 'Alert',
                'Alert: ' + @ActivityTypeName + ' — ' + @Details,
                'Server', @SessionId, @UserId, @ClientMachineId);

        SELECT @AlertId AS AlertId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_LogSecurityAlert: ' + ERROR_MESSAGE(), 'Server');
        SELECT -1 AS AlertId;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_LogSecurityAlert created/updated.';
GO

-- ── 5.9  sp_RegisterClient ───────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_RegisterClient
    @ClientCode  NVARCHAR(50),
    @MachineName NVARCHAR(100),
    @IPAddress   NVARCHAR(45),
    @MACAddress  NVARCHAR(50)  = NULL,
    @Location    NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MachineId INT;
    BEGIN TRY
        IF EXISTS (SELECT 1 FROM dbo.tblClientMachine WHERE ClientCode = @ClientCode)
        BEGIN
            UPDATE dbo.tblClientMachine
            SET    IPAddress  = @IPAddress,
                   MACAddress = ISNULL(@MACAddress, MACAddress),
                   LastSeenAt = GETDATE()
            WHERE  ClientCode = @ClientCode;

            SELECT @MachineId = ClientMachineId
            FROM   dbo.tblClientMachine WHERE ClientCode = @ClientCode;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tblClientMachine
                (ClientCode, MachineName, IPAddress, MACAddress, Location, Status, IsActive)
            VALUES
                (@ClientCode, @MachineName, @IPAddress, @MACAddress, @Location, 'Idle', 1);
            SET @MachineId = SCOPE_IDENTITY();
        END

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source, ClientMachineId)
        VALUES ('System', 'ClientRegistered',
                'Client ' + @ClientCode + ' (' + @MachineName + ') registered/updated',
                'Server', @MachineId);

        SELECT @MachineId AS ClientMachineId;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_RegisterClient: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS ClientMachineId;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_RegisterClient created/updated.';
GO

-- ── 5.10  sp_UpdateClientMachineInfo ─────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_UpdateClientMachineInfo
    @ClientCode  NVARCHAR(50),
    @MachineName NVARCHAR(100),
    @Location    NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblClientMachine WHERE ClientCode = @ClientCode)
        BEGIN SELECT 0 AS Result; RETURN; END

        UPDATE dbo.tblClientMachine
        SET    MachineName = @MachineName,
               Location   = @Location
        WHERE  ClientCode  = @ClientCode;

        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'MachineInfoUpdated',
                'Admin updated machine info for ' + @ClientCode +
                ': Name="' + @MachineName + '"' +
                ISNULL(', Location="' + @Location + '"', ''),
                'Server');

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_UpdateClientMachineInfo: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_UpdateClientMachineInfo created/updated.';
GO

-- ── 5.11  sp_RegisterClientUser ──────────────────────────────────────────────
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
            (Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedByUserId)
        VALUES
            (@Username, @PasswordHash, @FullName, 'ClientUser', 'Active', @Phone, @Address, @AdminUserId);

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
            VALUES ('System', 'Error', 'sp_RegisterClientUser: ' + ERROR_MESSAGE(), 'Server');
        END
    END CATCH
END;
GO
PRINT 'SP dbo.sp_RegisterClientUser created/updated.';
GO

-- ── 5.12  sp_UpdateClientUser ────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_UpdateClientUser
    @UserId             INT,
    @FullName           NVARCHAR(100),
    @Phone              NVARCHAR(30)  = NULL,
    @Address            NVARCHAR(200) = NULL,
    @ProfilePicturePath NVARCHAR(500) = NULL
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
               ProfilePicturePath = CASE WHEN @ProfilePicturePath IS NOT NULL
                                         THEN @ProfilePicturePath
                                         ELSE ProfilePicturePath END
        WHERE  UserId = @UserId AND Role = 'ClientUser';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_UpdateClientUser: ' + ERROR_MESSAGE(), 'Server');
        THROW;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_UpdateClientUser created/updated.';
GO

-- ── 5.13  sp_DeleteClientUser ────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_DeleteClientUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF EXISTS (SELECT 1 FROM dbo.tblSession WHERE UserId = @UserId)
        BEGIN SELECT -1 AS Result; RETURN; END

        DELETE FROM dbo.tblUser WHERE UserId = @UserId AND Role = 'ClientUser';
        SELECT @@ROWCOUNT AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'Error', 'sp_DeleteClientUser: ' + ERROR_MESSAGE(), 'Server');
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_DeleteClientUser created/updated.';
GO

-- ── 5.14  sp_InsertBillingRate ───────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_InsertBillingRate
    @Name             NVARCHAR(100),
    @RatePerMinute    DECIMAL(10,2),
    @Currency         NVARCHAR(10),
    @EffectiveFrom    DATE,
    @EffectiveTo      DATE = NULL,
    @IsDefault        BIT  = 0,
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

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE LTRIM(RTRIM(Name)) = LTRIM(RTRIM(@Name)))
        BEGIN SET @NewBillingRateId = -2; RETURN; END

        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  IsActive = 1 AND Currency = @Currency
              AND  @EffectiveFrom <= COALESCE(EffectiveTo, '9999-12-31')
              AND  COALESCE(@EffectiveTo, '9999-12-31') >= EffectiveFrom
        )
        BEGIN SET @NewBillingRateId = -3; RETURN; END

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
        VALUES ('Billing', 'Error', 'sp_InsertBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT -1 AS BillingRateId;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_InsertBillingRate created/updated.';
GO

-- ── 5.15  sp_UpdateBillingRate ───────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_UpdateBillingRate
    @BillingRateId INT,
    @Name          NVARCHAR(100),
    @RatePerMinute DECIMAL(10,2),
    @Currency      NVARCHAR(10),
    @EffectiveFrom DATE,
    @EffectiveTo   DATE = NULL,
    @IsActive      BIT,
    @IsDefault     BIT  = 0,
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

        IF EXISTS (SELECT 1 FROM dbo.tblBillingRate
                   WHERE BillingRateId <> @BillingRateId AND LTRIM(RTRIM(Name)) = LTRIM(RTRIM(@Name)))
        BEGIN SELECT 0 AS Result; RETURN; END

        IF EXISTS (
            SELECT 1 FROM dbo.tblBillingRate
            WHERE  IsActive = 1 AND Currency = @Currency AND BillingRateId <> @BillingRateId
              AND  @EffectiveFrom <= COALESCE(EffectiveTo, '9999-12-31')
              AND  COALESCE(@EffectiveTo, '9999-12-31') >= EffectiveFrom
        )
        BEGIN SELECT 0 AS Result; RETURN; END

        SELECT @OldIsDefault = IsDefault FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        IF @IsDefault = 1 AND @OldIsDefault = 0
            UPDATE dbo.tblBillingRate SET IsDefault = 0 WHERE BillingRateId <> @BillingRateId;

        IF @IsDefault = 0 AND @OldIsDefault = 1
            IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE IsDefault = 1 AND BillingRateId <> @BillingRateId)
                RAISERROR('At least one default rate must remain.', 16, 1);

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
        VALUES ('Billing', 'Error', 'sp_UpdateBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_UpdateBillingRate created/updated.';
GO

-- ── 5.16  sp_DeleteBillingRate ───────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_DeleteBillingRate
    @BillingRateId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RateName NVARCHAR(100), @IsDefault BIT, @RateCount INT, @DefaultCount INT;
    BEGIN TRY
        SELECT @RateName = Name, @IsDefault = IsDefault
        FROM   dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;

        IF @RateName IS NULL
            RAISERROR('Billing rate not found.', 16, 1);

        SELECT @RateCount = COUNT(*) FROM dbo.tblBillingRate;
        IF @RateCount = 1
            RAISERROR('Cannot delete the last billing rate.', 16, 1);

        IF @IsDefault = 1
        BEGIN
            SELECT @DefaultCount = COUNT(*) FROM dbo.tblBillingRate WHERE IsDefault = 1;
            IF @DefaultCount = 1
                RAISERROR('Cannot delete the only default rate.', 16, 1);
        END

        DELETE FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId;
        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error', 'sp_DeleteBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_DeleteBillingRate created/updated.';
GO

-- ── 5.17  sp_GetAllBillingRates ──────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_GetAllBillingRates
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT BillingRateId, Name, RatePerMinute, Currency,
               EffectiveFrom, EffectiveTo, IsActive, IsDefault,
               SetByAdminUserId, Notes, CreatedAt
        FROM   dbo.tblBillingRate
        ORDER  BY IsDefault DESC, CreatedAt DESC;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error', 'sp_GetAllBillingRates: ' + ERROR_MESSAGE(), 'Admin');
    END CATCH
END;
GO
PRINT 'SP dbo.sp_GetAllBillingRates created/updated.';
GO

-- ── 5.18  sp_SetDefaultBillingRate ───────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_SetDefaultBillingRate
    @BillingRateId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE BillingRateId = @BillingRateId)
            RAISERROR('Billing rate not found.', 16, 1);

        UPDATE dbo.tblBillingRate SET IsDefault = 0 WHERE BillingRateId <> @BillingRateId;
        UPDATE dbo.tblBillingRate SET IsDefault = 1 WHERE BillingRateId  = @BillingRateId;

        SELECT 1 AS Result;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('Billing', 'Error', 'sp_SetDefaultBillingRate: ' + ERROR_MESSAGE(), 'Admin');
        SELECT 0 AS Result;
    END CATCH
END;
GO
PRINT 'SP dbo.sp_SetDefaultBillingRate created/updated.';
GO

-- ── 5.19  sp_UpsertActivityType ──────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_UpsertActivityType
    @Name            NVARCHAR(100),
    @Description     NVARCHAR(500),
    @DefaultSeverity NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.tblActivityType WHERE Name = @Name)
        UPDATE dbo.tblActivityType
        SET    Description     = @Description,
               DefaultSeverity = @DefaultSeverity,
               IsActive        = 1
        WHERE  Name = @Name;
    ELSE
        INSERT INTO dbo.tblActivityType (Name, Description, DefaultSeverity, IsActive)
        VALUES (@Name, @Description, @DefaultSeverity, 1);
END;
GO
PRINT 'SP dbo.sp_UpsertActivityType created/updated.';
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 6 ─ SEED DATA
--  Each insert is guarded — rows are only added if absent.
--  Safe to re-run: existing rows are never duplicated.
-- ════════════════════════════════════════════════════════════

-- ── 6.1  Admin user ──────────────────────────────────────────────────────────
-- Default password: Admin@123  (BCrypt work factor 12)
-- Change this immediately after first login via SessionAdmin.
IF NOT EXISTS (SELECT 1 FROM dbo.tblUser WHERE Username = 'Admin' AND Role = 'Admin')
BEGIN
    INSERT INTO dbo.tblUser
        (Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedAt)
    VALUES
        ('Admin',
         '$2a$12$cidj..ohW.bgKXVPBdVyH.VbvmIrOxVmFGqV3Y/lZDGC0utA685vm',
         'System Administrator', 'Admin', 'Active', NULL, NULL, GETDATE());
    PRINT 'Seed: Admin user inserted.';
END
ELSE
    PRINT 'Seed: Admin user already exists — skipped.';
GO

-- ── 6.2  Default billing rate ─────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.tblBillingRate WHERE IsDefault = 1)
BEGIN
    INSERT INTO dbo.tblBillingRate
        (Name, RatePerMinute, Currency, EffectiveFrom, IsActive, IsDefault, CreatedAt)
    VALUES
        ('Standard Rate', 3.00, 'PKR', CAST(GETDATE() AS DATE), 1, 1, GETDATE());
    PRINT 'Seed: Default billing rate (3 PKR/min) inserted.';
END
ELSE
    PRINT 'Seed: Default billing rate already exists — skipped.';
GO

-- ── 6.3  Security / activity types ───────────────────────────────────────────
DECLARE @types TABLE (Name NVARCHAR(100), Description NVARCHAR(500), Severity NVARCHAR(10));
INSERT INTO @types VALUES
    ('ProxySettingsEnabled',          'System proxy settings were enabled during session',                'High'),
    ('WinHttpProxyEnabled',           'WinHTTP proxy configured during session',                         'High'),
    ('ProxyAutoConfigDetected',       'PAC/Auto-config proxy URL detected during session',               'Medium'),
    ('VpnAdapterActive',              'A VPN/tunneling network adapter was active during session',        'High'),
    ('BlacklistedProcessRunning',     'A blacklisted proxy/VPN process was running during session',      'High'),
    ('MobileHotspotDetected',         'Client connected via mobile hotspot instead of authorised LAN',   'High'),
    ('NetworkTypeSwitched',           'Network connection type changed during session',                   'Medium'),
    ('SystemTimeTampered',            'System clock was moved during an active session',                  'High'),
    ('RemoteDesktopSessionDetected',  'Client application is running inside an RDP session',              'High'),
    ('VirtualCameraDetected',         'A virtual or fake webcam was detected',                           'Medium'),
    ('RepeatedLoginFailure',          'Repeated failed login attempts from a client machine',             'High'),
    ('CameraUnavailable',             'Webcam was not available at login time',                           'Low'),
    ('ImageCaptureFailed',            'Webcam capture failed after retry',                               'Low'),
    ('UnauthorizedAccess',            'Attempt to access unauthorized resources',                         'High'),
    ('SessionExpired',                'Session expired due to timeout',                                   'Medium'),
    ('LoginFailure',                  'Failed login attempt',                                             'Medium'),
    ('DataTransfer',                  'Large data transfer detected',                                     'Low'),
    ('SystemError',                   'System error occurred',                                            'High'),
    ('ConfigChange',                  'Configuration change detected',                                    'Medium');

MERGE dbo.tblActivityType AS target
USING @types               AS source ON target.Name = source.Name
WHEN MATCHED THEN
    UPDATE SET Description = source.Description, DefaultSeverity = source.Severity, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (Name, Description, DefaultSeverity, IsActive)
    VALUES (source.Name, source.Description, source.Severity, 1);

PRINT 'Seed: Activity types merged (19 rows).';
GO

-- ── 5.20  sp_PurgeOldLogs ────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_PurgeOldLogs
    @RetentionDays INT = 180
AS
BEGIN
    SET NOCOUNT ON;
    IF @RetentionDays <= 0 RETURN;   -- 0 = disabled

    DECLARE @Cutoff  DATETIME = DATEADD(DAY, -@RetentionDays, GETDATE());
    DECLARE @Deleted INT;

    DELETE FROM dbo.tblSystemLog WHERE LogedAt < @Cutoff;
    SET @Deleted = @@ROWCOUNT;

    IF @Deleted > 0
        INSERT INTO dbo.tblSystemLog (Category, Type, Message, Source)
        VALUES ('System', 'LogPurge',
                'Purged ' + CAST(@Deleted AS VARCHAR) +
                ' log rows older than ' + CAST(@RetentionDays AS VARCHAR) + ' days',
                'Server');
END;
GO
PRINT 'SP dbo.sp_PurgeOldLogs created/updated.';
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 7 ─ VERIFICATION
--  Run this block to confirm the setup is complete.
-- ════════════════════════════════════════════════════════════

SELECT
    'Tables'           AS ObjectType,
    COUNT(*)           AS Count
FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo')
UNION ALL
SELECT 'Stored Procedures', COUNT(*)
FROM sys.procedures WHERE schema_id = SCHEMA_ID('dbo')
UNION ALL
SELECT 'Views', COUNT(*)
FROM sys.views WHERE schema_id = SCHEMA_ID('dbo')
UNION ALL
SELECT 'Indexes (non-PK)', COUNT(*)
FROM sys.indexes i
INNER JOIN sys.objects o ON o.object_id = i.object_id
WHERE o.schema_id = SCHEMA_ID('dbo') AND i.is_primary_key = 0 AND i.type > 0;

SELECT 'Admin user exists' AS Check, CAST(COUNT(*) AS VARCHAR) AS Value
FROM dbo.tblUser WHERE Role = 'Admin'
UNION ALL
SELECT 'Default billing rate', CAST(COUNT(*) AS VARCHAR)
FROM dbo.tblBillingRate WHERE IsDefault = 1
UNION ALL
SELECT 'Activity types', CAST(COUNT(*) AS VARCHAR)
FROM dbo.tblActivityType;

PRINT '=== Setup complete. See result sets above for counts. ===';
GO

-- ════════════════════════════════════════════════════════════
--  SECTION 8 ─ DEVELOPER FULL RESET  ⚠ DESTRUCTIVE ⚠
--  Drops ALL tables and ALL data.
--  NEVER run this on a production database.
--  To use: uncomment the entire block below, run ONLY this
--          section, then re-run the full script from the top.
-- ════════════════════════════════════════════════════════════

/*
USE ClientServerSessionDB;
GO

-- Drop stored procedures
DROP PROCEDURE IF EXISTS dbo.sp_FinalizeSessionBilling;
DROP PROCEDURE IF EXISTS dbo.sp_CalculateSessionBilling;
DROP PROCEDURE IF EXISTS dbo.sp_EndSession;
DROP PROCEDURE IF EXISTS dbo.sp_StartSession;
DROP PROCEDURE IF EXISTS dbo.sp_GetActiveSessions;
DROP PROCEDURE IF EXISTS dbo.sp_GetBillingRecords;
DROP PROCEDURE IF EXISTS dbo.sp_MarkBillingRecordPaid;
DROP PROCEDURE IF EXISTS dbo.sp_LogSecurityAlert;
DROP PROCEDURE IF EXISTS dbo.sp_RegisterClient;
DROP PROCEDURE IF EXISTS dbo.sp_UpdateClientMachineInfo;
DROP PROCEDURE IF EXISTS dbo.sp_RegisterClientUser;
DROP PROCEDURE IF EXISTS dbo.sp_UpdateClientUser;
DROP PROCEDURE IF EXISTS dbo.sp_DeleteClientUser;
DROP PROCEDURE IF EXISTS dbo.sp_InsertBillingRate;
DROP PROCEDURE IF EXISTS dbo.sp_UpdateBillingRate;
DROP PROCEDURE IF EXISTS dbo.sp_DeleteBillingRate;
DROP PROCEDURE IF EXISTS dbo.sp_GetAllBillingRates;
DROP PROCEDURE IF EXISTS dbo.sp_SetDefaultBillingRate;
DROP PROCEDURE IF EXISTS dbo.sp_UpsertActivityType;
GO

-- Drop views
DROP VIEW IF EXISTS dbo.vw_SessionReport;
DROP VIEW IF EXISTS dbo.vw_ActiveSessionsSummary;
GO

-- Drop tables in reverse FK dependency order
DROP TABLE IF EXISTS dbo.tblSystemLog;
DROP TABLE IF EXISTS dbo.tblLoginAttempt;
DROP TABLE IF EXISTS dbo.tblAlert;
DROP TABLE IF EXISTS dbo.tblActivityType;
DROP TABLE IF EXISTS dbo.tblBillingRecord;
DROP TABLE IF EXISTS dbo.tblBillingRate;
DROP TABLE IF EXISTS dbo.tblSessionImage;
DROP TABLE IF EXISTS dbo.tblSession;
DROP TABLE IF EXISTS dbo.tblClientMachine;
DROP TABLE IF EXISTS dbo.tblUser;
GO

PRINT '=== Developer reset complete. Re-run the full script from Section 1. ===';
GO
*/
