using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Collections.Generic;
using SessionManagement.WCF;

namespace SessionManagement.Data
{
    /// <summary>
    /// Central data-access layer.
    /// Every public method maps to one or more database steps in the sequence diagrams.
    /// All column/table names match ClientServerSessionDB exactly.
    /// </summary>
    public class DatabaseHelper
    {
        private readonly string _cs;

        public DatabaseHelper()
            => _cs = ConfigurationManager.ConnectionStrings["SessionManagementDB"].ConnectionString;

        public DatabaseHelper(string cs) => _cs = cs;

        private SqlConnection Conn() => new SqlConnection(_cs);

        public bool TestConnection()
        {
            try { using (var c = Conn()) { c.Open(); return true; } }
            catch { return false; }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-01 / UC-09  —  AUTHENTICATION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// SEQ-01 step 3: fetch user row by username so caller can BCrypt-verify.
        /// Returns null when user does not exist (any status).
        /// </summary>
        public DataRow GetUserByUsername(string username)
        {
            const string sql = @"
                SELECT UserId, Username, PasswordHash, FullName, Role, Status
                FROM   dbo.tblUser
                WHERE  Username = @Username";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@Username", username);
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt.Rows.Count > 0 ? dt.Rows[0] : null;
                }
            }
            catch (Exception ex) { LogError("GetUserByUsername", ex); return null; }
        }

        /// <summary>
        /// Fetch user row by UserId.
        /// Returns null when user does not exist.
        /// </summary>
        public DataRow GetUserById(int userId)
        {
            const string sql = @"
                SELECT UserId, Username, PasswordHash, FullName, Role, Status, Phone, Address, CreatedAt, LastLoginAt
                FROM   dbo.tblUser
                WHERE  UserId = @UserId";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt.Rows.Count > 0 ? dt.Rows[0] : null;
                }
            }
            catch (Exception ex) { LogError("GetUserById", ex); return null; }
        }

        /// <summary>
        /// SEQ-01 step 4b: stamp LastLoginAt after successful login.
        /// </summary>
        public void UpdateLastLogin(int userId)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand(
                    "UPDATE dbo.tblUser SET LastLoginAt = GETDATE() WHERE UserId = @UserId", c))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    c.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { LogError("UpdateLastLogin", ex); }
        }

        /// <summary>
        /// SEQ-01 step 4: insert tblLoginAttempt for every attempt (success or failure).
        /// clientMachineId = 0 only when machine is completely unknown.
        /// </summary>
        public void InsertLoginAttempt(int clientMachineId, int? userId,
            string usernameEntered, bool isSuccess, string failureReason = null)
        {
            const string sql = @"
                INSERT INTO dbo.tblLoginAttempt
                    (ClientMachineId, UserId, UsernameEntered, AttemptedAt, IsSuccess, FailureReason)
                VALUES
                    (@ClientMachineId, @UserId, @UsernameEntered, GETDATE(), @IsSuccess, @FailureReason)";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@ClientMachineId", clientMachineId);
                    cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UsernameEntered", usernameEntered);
                    cmd.Parameters.AddWithValue("@IsSuccess", isSuccess);
                    cmd.Parameters.AddWithValue("@FailureReason", (object)failureReason ?? DBNull.Value);
                    c.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { LogError("InsertLoginAttempt", ex); }
        }

        /// <summary>
        /// FR-12: count recent failures from a machine (for RepeatedLoginFailure alert).
        /// </summary>
        public int CountRecentFailedLogins(int clientMachineId, int withinMinutes = 10)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM   dbo.tblLoginAttempt
                WHERE  ClientMachineId = @ClientMachineId
                  AND  IsSuccess       = 0
                  AND  AttemptedAt    >= DATEADD(MINUTE, -@Minutes, GETDATE())";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@ClientMachineId", clientMachineId);
                    cmd.Parameters.AddWithValue("@Minutes", withinMinutes);
                    c.Open();
                    return (int)cmd.ExecuteScalar();
                }
            }
            catch (Exception ex) { LogError("CountRecentFailedLogins", ex); return 0; }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-02  —  START SESSION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// SEQ-02: calls sp_StartSession → inserts tblSession (Status=Active) + logs.
        /// Returns new SessionId (0 on failure).
        /// </summary>
        public int StartSession(int userId, int clientMachineId, int durationMinutes)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_StartSession", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@ClientMachineId", clientMachineId);
                    cmd.Parameters.AddWithValue("@SelectedDurationMinutes", durationMinutes);
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null ? Convert.ToInt32(r) : 0;
                }
            }
            catch (Exception ex) { LogError("StartSession", ex); return 0; }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-07 / UC-08 / UC-14  —  END SESSION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Atomically: sp_EndSession → sp_CalculateSessionBilling → sp_FinalizeSessionBilling.
        /// NFR-14: all three in one transaction — partial writes not allowed.
        /// </summary>
        public bool EndSession(int sessionId, string terminationReason)
        {
            try
            {
                using (var c = Conn())
                {
                    c.Open();
                    using (var tx = c.BeginTransaction())
                    {
                        try
                        {
                            ExecSP(c, tx, "sp_EndSession",
                                P("@SessionId", sessionId),
                                P("@TerminationReason", terminationReason));

                            ExecSP(c, tx, "sp_CalculateSessionBilling",
                                P("@SessionId", sessionId));

                            ExecSP(c, tx, "sp_FinalizeSessionBilling",
                                P("@SessionId", sessionId));

                            tx.Commit();
                            return true;
                        }
                        catch { tx.Rollback(); throw; }
                    }
                }
            }
            catch (Exception ex) { LogError("EndSession", ex); return false; }
        }

        /// <summary>
        /// Server background timer: mark overdue Active sessions as Expired.
        /// Returns number of sessions expired.
        /// </summary>
        public int AutoExpireOverdueSessions()
        {
            const string sql = @"
                UPDATE dbo.tblSession
                SET    Status                 = 'Expired',
                       EndedAt               = GETDATE(),
                       ActualDurationMinutes = SelectedDurationMinutes,
                       TerminationReason     = 'AutoExpiry'
                WHERE  Status        = 'Active'
                  AND  ExpectedEndAt < GETDATE()";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                { c.Open(); return cmd.ExecuteNonQuery(); }
            }
            catch (Exception ex) { LogError("AutoExpireOverdueSessions", ex); return 0; }
        }

        /// <summary>
        /// Marks overdue Active sessions as Expired and returns their SessionIds.
        /// </summary>
        public List<int> AutoExpireOverdueSessionsWithIds()
        {
            const string selectSql = @"
                SELECT SessionId FROM dbo.tblSession
                WHERE Status = 'Active' AND ExpectedEndAt < GETDATE()";
            const string updateSql = @"
                UPDATE dbo.tblSession
                SET    Status                 = 'Expired',
                       EndedAt               = GETDATE(),
                       ActualDurationMinutes = SelectedDurationMinutes,
                       TerminationReason     = 'AutoExpiry'
                WHERE  SessionId = @SessionId";
            var expiredIds = new List<int>();
            try
            {
                using (var c = Conn())
                {
                    c.Open();
                    // Get all expired session IDs
                    using (var selectCmd = new SqlCommand(selectSql, c))
                    using (var reader = selectCmd.ExecuteReader())
                    {
                        while (reader.Read())
                            expiredIds.Add(reader.GetInt32(0));
                    }
                    // Expire each session individually
                    foreach (var sessionId in expiredIds)
                    {
                        using (var updateCmd = new SqlCommand(updateSql, c))
                        {
                            updateCmd.Parameters.AddWithValue("@SessionId", sessionId);
                            updateCmd.ExecuteNonQuery();
                        }
                    }
                }
                return expiredIds;
            }
            catch (Exception ex) { LogError("AutoExpireOverdueSessionsWithIds", ex); return new List<int>(); }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-06 / UC-10  —  GET SESSION INFO / ACTIVE SESSIONS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Returns one session row with user + machine + timing columns.
        /// Used to build SessionInfo and to obtain ClientCode for WCF callbacks.
        /// </summary>
        public DataRow GetSessionById(int sessionId)
        {
            const string sql = @"
                SELECT s.SessionId, s.UserId, s.ClientMachineId,
                       s.StartedAt, s.SelectedDurationMinutes, s.ExpectedEndAt,
                       s.EndedAt,  s.ActualDurationMinutes,
                       s.Status,   s.TerminationReason,
                       u.Username, u.FullName,
                       c.ClientCode, c.MachineName
                FROM   dbo.tblSession       s
                JOIN   dbo.tblUser          u ON u.UserId          = s.UserId
                JOIN   dbo.tblClientMachine c ON c.ClientMachineId = s.ClientMachineId
                WHERE  s.SessionId = @SessionId";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt.Rows.Count > 0 ? dt.Rows[0] : null;
                }
            }
            catch (Exception ex) { LogError("GetSessionById", ex); return null; }
        }

        /// <summary>
        /// UC-10: sp_GetActiveSessions — returns all active sessions
        /// with real-time RemainingMinutes and CurrentBilling.
        /// </summary>
        public DataTable GetActiveSessions()
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_GetActiveSessions", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex) { LogError("GetActiveSessions", ex); return new DataTable(); }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-04 / UC-05 / UC-12  —  SESSION IMAGES
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// UC-05: upsert tblSessionImage (one row per session).
        /// CaptureStatus: Captured | CameraUnavailable | Skipped | Failed
        /// UploadStatus:  Sent | Pending | Failed
        /// </summary>
        public bool UpsertSessionImage(int sessionId, string captureStatus,
            string uploadStatus, string imagePath, string notes = null)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM dbo.tblSessionImage WHERE SessionId = @SessionId)
                    UPDATE dbo.tblSessionImage
                    SET    CaptureStatus = @CaptureStatus,
                           UploadStatus  = @UploadStatus,
                           ImagePath     = @ImagePath,
                           Notes         = @Notes
                    WHERE  SessionId = @SessionId
                ELSE
                    INSERT INTO dbo.tblSessionImage
                        (SessionId, CapturedAt, CaptureStatus, UploadStatus, ImagePath, Notes)
                    VALUES
                        (@SessionId, GETDATE(), @CaptureStatus, @UploadStatus, @ImagePath, @Notes)";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    cmd.Parameters.AddWithValue("@CaptureStatus", captureStatus);
                    cmd.Parameters.AddWithValue("@UploadStatus", uploadStatus);
                    cmd.Parameters.AddWithValue("@ImagePath", (object)imagePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Notes", (object)notes ?? DBNull.Value);
                    c.Open();
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex) { LogError("UpsertSessionImage", ex); return false; }
        }

        /// <summary>UC-12: returns the file-system path stored for a session image.</summary>
        public string GetSessionImagePath(int sessionId)
        {
            const string sql = "SELECT ImagePath FROM dbo.tblSessionImage WHERE SessionId = @SessionId";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    return (r != null && r != DBNull.Value) ? r.ToString() : null;
                }
            }
            catch (Exception ex) { LogError("GetSessionImagePath", ex); return null; }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-07 / UC-13  —  BILLING
        // ═══════════════════════════════════════════════════════════

        /// <summary>Calls sp_CalculateSessionBilling; returns running amount.</summary>
        public decimal CalculateRunningBilling(int sessionId)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_CalculateSessionBilling", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null ? Convert.ToDecimal(r) : 0m;
                }
            }
            catch (Exception ex) { LogError("CalculateRunningBilling", ex); return 0m; }
        }

        /// <summary>UC-13: current default rate per minute from tblBillingRate.</summary>
        public decimal GetCurrentBillingRate()
        {
            const string sqlDefault = @"
                SELECT TOP 1 RatePerMinute FROM dbo.tblBillingRate
                WHERE  IsActive = 1 AND IsDefault = 1 ORDER BY CreatedAt DESC";
            const string sqlAny = @"
                SELECT TOP 1 RatePerMinute FROM dbo.tblBillingRate
                WHERE  IsActive = 1 ORDER BY CreatedAt DESC";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sqlDefault, c))
                {
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    if (r != null) return Convert.ToDecimal(r);
                    cmd.CommandText = sqlAny;
                    r = cmd.ExecuteScalar();
                    return r != null ? Convert.ToDecimal(r) : 0.50m;
                }
            }
            catch (Exception ex) { LogError("GetCurrentBillingRate", ex); return 0.50m; }
        }

        /// <summary>UC-13: finalized billing amount once session is closed.</summary>
        public decimal GetFinalBillingAmount(int sessionId)
        {
            const string sql = "SELECT Amount FROM dbo.tblBillingRecord WHERE SessionId = @SessionId";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null ? Convert.ToDecimal(r) : 0m;
                }
            }
            catch (Exception ex) { LogError("GetFinalBillingAmount", ex); return 0m; }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-11  —  CLIENT MACHINES
        // ═══════════════════════════════════════════════════════════

        /// <summary>Returns ClientMachineId for a ClientCode, or 0 if not found.</summary>
        public int GetClientMachineIdByCode(string clientCode)
        {
            const string sql = "SELECT ClientMachineId FROM dbo.tblClientMachine WHERE ClientCode = @Code";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@Code", clientCode);
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null ? Convert.ToInt32(r) : 0;
                }
            }
            catch (Exception ex) { LogError("GetClientMachineIdByCode", ex); return 0; }
        }


        /// <summary>Check if a client machine is active (IsActive = 1).</summary>
        public bool IsClientMachineActive(int clientMachineId)
        {
            const string sql = "SELECT IsActive FROM dbo.tblClientMachine WHERE ClientMachineId = @Id";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@Id", clientMachineId);
                    c.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null && Convert.ToBoolean(r);
                }
            }
            catch (Exception ex) { LogError("IsClientMachineActive", ex); return false; }
        }

        /// <summary>
        /// sp_RegisterClient: upserts the machine row and returns its ClientMachineId.
        /// Called at client startup so the machine appears in the admin dashboard immediately.
        /// </summary>
        public int RegisterOrUpdateClient(string clientCode, string machineName,
            string ipAddress, string macAddress = null)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_RegisterClient", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@ClientCode", clientCode);
                    cmd.Parameters.AddWithValue("@MachineName", machineName);
                    cmd.Parameters.AddWithValue("@IPAddress", ipAddress);
                    cmd.Parameters.AddWithValue("@MACAddress", (object)macAddress ?? DBNull.Value);
                    c.Open();
                    cmd.ExecuteScalar();   // returns 1/0 success flag
                }
                return GetClientMachineIdByCode(clientCode);
            }
            catch (Exception ex) { LogError("RegisterOrUpdateClient", ex); return 0; }
        }

        /// <summary>Update IsActive status on a client machine row.</summary>
        public bool UpdateClientMachineIsActive(int clientMachineId, bool isActive)
        {
            const string sql = @"
                UPDATE dbo.tblClientMachine
                SET    IsActive = @IsActive
                WHERE  ClientMachineId = @Id";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@Id", clientMachineId);
                    cmd.Parameters.AddWithValue("@IsActive", isActive);
                    c.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex) { LogError("UpdateClientMachineIsActive", ex); return false; }
        }

        /// <summary>Update Status + LastSeenAt on a client machine row.</summary>
        public bool UpdateClientMachineStatus(int clientMachineId, string status)
        {
            const string sql = @"
                UPDATE dbo.tblClientMachine
                SET    Status = @Status, LastSeenAt = GETDATE()
                WHERE  ClientMachineId = @Id";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@Id", clientMachineId);
                    cmd.Parameters.AddWithValue("@Status", status);
                    c.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex) { LogError("UpdateClientMachineStatus", ex); return false; }
        }

        /// <summary>UC-11: all client machines + current session user (if any).</summary>
        public DataTable GetAllClientMachines()
        {
            const string sql = @"
                SELECT c.ClientMachineId, c.ClientCode, c.MachineName,
                       c.IPAddress, c.MACAddress, c.Location,
                       c.Status, c.LastSeenAt, c.IsActive,
                       u.Fullname +' (' +u.Username+')' AS CurrentUsername
                FROM   dbo.tblClientMachine c
                LEFT JOIN dbo.tblSession s
                       ON s.ClientMachineId = c.ClientMachineId AND s.Status = 'Active'
                LEFT JOIN dbo.tblUser u ON u.UserId = s.UserId
                ORDER  BY c.ClientCode";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex) { LogError("GetAllClientMachines", ex); return new DataTable(); }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-16 / UC-17  —  ALERTS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Calls sp_LogSecurityAlert → inserts tblAlert + tblSystemLog.
        /// SEQ-16/17: alert generated, persisted, admin notified.
        /// </summary>
        public bool InsertSecurityAlert(string activityTypeName, int? sessionId,
            int? clientMachineId, int? userId, string details, string severity)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_LogSecurityAlert", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@ActivityTypeName", activityTypeName);
                    cmd.Parameters.AddWithValue("@SessionId", (object)sessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ClientMachineId", (object)clientMachineId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Details", details);
                    cmd.Parameters.AddWithValue("@Severity", severity);
                    c.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch (Exception ex) { LogError("InsertSecurityAlert", ex); return false; }
        }

        /// <summary>UC-17: all unacknowledged alerts with ActivityType name.</summary>
        public DataTable GetUnacknowledgedAlerts()
        {
            const string sql = @"
                SELECT a.AlertId, a.SessionId, a.DetectedAt, a.Severity,
                       a.Status,  a.Details, a.IsAcknowledged,
                       at.Name    AS ActivityTypeName,
                       u.Username,
                       c.ClientCode
                FROM   dbo.tblAlert        a
                JOIN   dbo.tblActivityType at ON at.ActivityTypeId   = a.ActivityTypeId
                LEFT JOIN dbo.tblUser        u ON u.UserId            = a.UserId
                LEFT JOIN dbo.tblClientMachine c ON c.ClientMachineId = a.ClientMachineId
                WHERE  a.IsAcknowledged = 0
                ORDER  BY a.DetectedAt DESC";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex) { LogError("GetUnacknowledgedAlerts", ex); return new DataTable(); }
        }

        /// <summary>UC-17: admin acknowledges an alert; sets AcknowledgedByAdminUserId.</summary>
        public bool AcknowledgeAlert(int alertId, int adminUserId)
        {
            const string sql = @"
                UPDATE dbo.tblAlert
                SET    IsAcknowledged            = 1,
                       AcknowledgedByAdminUserId = @Admin,
                       AcknowledgedAt            = GETDATE(),
                       Status                    = 'Acknowledged'
                WHERE  AlertId = @AlertId";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@AlertId", alertId);
                    cmd.Parameters.AddWithValue("@Admin", adminUserId);
                    c.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex) { LogError("AcknowledgeAlert", ex); return false; }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-15 / UC-18  —  LOGS & REPORTS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Write a structured row to tblSystemLog.
        /// Category MUST be: Auth | Session | Billing | Security | System
        /// Source  MUST be:  Client | Server  (or null = Server)
        /// This method NEVER throws — logging failures only go to Debug output.
        /// </summary>
        public void WriteSystemLog(int? sessionId, int? userId, int? clientMachineId,
            int? adminUserId, string category, string type, string message,
            string source = "Server")
        {
            // Enforce DB CHECK constraint values
            string[] validCats = { "Auth", "Session", "Billing", "Security", "System" };
            if (Array.IndexOf(validCats, category) < 0) category = "System";
            if (source != "Client" && source != "Server") source = "Server";

            const string sql = @"
                INSERT INTO dbo.tblSystemLog
                    (LogedAt, Category, Type, Message, Source,
                     SessionId, UserId, ClientMachineId, AdminUserId)
                VALUES
                    (GETDATE(), @Category, @Type, @Message, @Source,
                     @SessionId, @UserId, @ClientMachineId, @AdminUserId)";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@Category", category);
                    cmd.Parameters.AddWithValue("@Type", type);
                    cmd.Parameters.AddWithValue("@Message", message);
                    cmd.Parameters.AddWithValue("@Source", source);
                    cmd.Parameters.AddWithValue("@SessionId", (object)sessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ClientMachineId", (object)clientMachineId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AdminUserId", (object)adminUserId ?? DBNull.Value);
                    c.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WriteSystemLog FAILED] {ex.Message}");
            }
        }

        /// <summary>Convenience overload — maps level string to Category.</summary>
        public void LogSystemEvent(int? sessionId, int? userId, int? clientMachineId,
            string type, string message, string level)
        {
            string cat = level == "Error" ? "System"
                       : level == "Warning" ? "Security"
                       : "System";
            WriteSystemLog(sessionId, userId, clientMachineId, null, cat, type, message);
        }

        /// <summary>UC-18: session + billing report from vw_SessionReport.</summary>
        public DataTable GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            const string sql = @"
                SELECT * FROM dbo.vw_SessionReport
                WHERE  StartedAt >= @From AND StartedAt <= @To
                ORDER  BY StartedAt DESC";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@From", fromDate.Date);
                    cmd.Parameters.AddWithValue("@To", toDate.Date.AddDays(1).AddSeconds(-1));
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex) { LogError("GetSessionReport", ex); return new DataTable(); }
        }

        /// <summary>UC-15: system-log entries for the Session Logs viewer.</summary>
        public DataTable GetSystemLogs(DateTime fromDate, DateTime toDate, string category = null)
        {
            const string sql = @"
                SELECT sl.SystemLogId, sl.LogedAt, sl.Category, sl.Type,
                       sl.Message, sl.Source, sl.SessionId, sl.ClientMachineId,
                       u.Username, c.ClientCode
                FROM   dbo.tblSystemLog    sl
                LEFT JOIN dbo.tblUser          u ON u.UserId          = sl.UserId
                LEFT JOIN dbo.tblClientMachine c ON c.ClientMachineId = sl.ClientMachineId
                WHERE  sl.LogedAt >= @From AND sl.LogedAt <= @To
                  AND  (@Category IS NULL OR sl.Category = @Category)
                ORDER  BY sl.LogedAt DESC";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@From", fromDate.Date);
                    cmd.Parameters.AddWithValue("@To", toDate.Date.AddDays(1).AddSeconds(-1));
                    cmd.Parameters.AddWithValue("@Category", (object)category ?? DBNull.Value);
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex) { LogError("GetSystemLogs", ex); return new DataTable(); }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-03  —  USER REGISTRATION (ADMIN)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// SEQ-03: Admin registers a new ClientUser.
        /// Returns UserId if successful, 0 if username already exists or error.
        /// </summary>
        public int RegisterClientUser(string username, string fullName,
    string passwordHash, string phone, string address, int adminUserId)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("dbo.sp_RegisterClientUser", c))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@Username", username);
                    cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                    cmd.Parameters.AddWithValue("@FullName", (object)fullName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object)phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address", (object)address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AdminUserId", adminUserId);

                    c.Open();

                    var result = cmd.ExecuteScalar();

                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch (Exception ex)
            {
                LogError("RegisterClientUser", ex);
                return 0;
            }
        }

        /// <summary>
        /// Get all ClientUser accounts (exclude admins).
        /// </summary>
        public DataTable GetAllClientUsers()
        {
            const string sql = @"
                SELECT UserId, Username, FullName, Phone, Address, Status, 
                       Role, CreatedAt, LastLoginAt
                FROM   dbo.tblUser
                WHERE  Role = 'ClientUser'
                ORDER  BY CreatedAt DESC";
            try
            {
                using (var c = Conn()) using (var cmd = new SqlCommand(sql, c))
                {
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex) { LogError("GetAllClientUsers", ex); return new DataTable(); }
        }

        /// <summary>
        /// Update ClientUser details (FullName, Phone, Address).
        /// </summary>
        public bool UpdateClientUser(int userId, string fullName, string phone, string address)
        {
            const string sql = @"
                UPDATE dbo.tblUser
                SET    FullName = @FullName,
                       Phone = @Phone,
                       Address = @Address
                WHERE  UserId = @UserId AND Role = 'ClientUser'";
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@FullName", (object)fullName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object)phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address", (object)address ?? DBNull.Value);
                    c.Open();
                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex) { LogError("UpdateClientUser", ex); return false; }
        }

        /// <summary>
        /// Reset user password to a new hash.
        /// </summary>
        public bool ResetUserPassword(int userId, string newPasswordHash)
        {
            const string sql = @"
                UPDATE dbo.tblUser
                SET    PasswordHash = @PasswordHash
                WHERE  UserId = @UserId AND Role = 'ClientUser'";
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@PasswordHash", newPasswordHash);
                    c.Open();
                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex) { LogError("ResetUserPassword", ex); return false; }
        }

        /// <summary>
        /// Update user account status (Active, Blocked, Disabled).
        /// </summary>
        public bool UpdateUserStatus(int userId, string newStatus)
        {
            const string sql = @"
                UPDATE dbo.tblUser
                SET    Status = @Status
                WHERE  UserId = @UserId AND Role = 'ClientUser'";
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand(sql, c))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@Status", newStatus);
                    c.Open();
                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex) { LogError("UpdateUserStatus", ex); return false; }
        }

        // ═══════════════════════════════════════════════════════════
        //  BILLING RATE MANAGEMENT
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Get all billing rates from database.
        /// Returns array of BillingRateInfo DTOs.
        /// </summary>
        public BillingRateInfo[] GetAllBillingRates()
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_GetAllBillingRates", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    c.Open();
                    var dt = new DataTable();
                    new SqlDataAdapter(cmd).Fill(dt);
                    
                    var rates = new List<BillingRateInfo>();
                    foreach (DataRow row in dt.Rows)
                    {
                        rates.Add(new BillingRateInfo
                        {
                            BillingRateId = (int)row["BillingRateId"],
                            Name = row["Name"].ToString(),
                            RatePerMinute = (decimal)row["RatePerMinute"],
                            Currency = row["Currency"].ToString(),
                            EffectiveFrom = row["EffectiveFrom"] != DBNull.Value ? (DateTime?)row["EffectiveFrom"] : null,
                            EffectiveTo = row["EffectiveTo"] != DBNull.Value ? (DateTime?)row["EffectiveTo"] : null,
                            IsActive = (bool)row["IsActive"],
                            IsDefault = (bool)row["IsDefault"],
                            CreatedAt = (DateTime)row["CreatedAt"],
                            Notes = row["Notes"] != DBNull.Value ? row["Notes"].ToString() : null
                        });
                    }
                    
                    return rates.ToArray();
                }
            }
            catch (Exception ex) { LogError("GetAllBillingRates", ex); return new BillingRateInfo[0]; }
        }

        /// <summary>
        /// Insert a new billing rate.
        /// If isDefault=true, automatically unsets all other defaults.
        /// Returns the new BillingRateId or -1 on error.
        /// </summary>
        public int InsertBillingRate(string name, decimal ratePerMinute, string currency,
            DateTime? effectiveFrom, DateTime? effectiveTo, bool isDefault, int adminUserId, string notes = null)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_InsertBillingRate", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@RatePerMinute", ratePerMinute);
                    cmd.Parameters.AddWithValue("@Currency", currency);
                    cmd.Parameters.AddWithValue("@EffectiveFrom", (object)effectiveFrom ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EffectiveTo", (object)effectiveTo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IsDefault", isDefault);
                    cmd.Parameters.AddWithValue("@SetByAdminUserId", adminUserId);
                    cmd.Parameters.AddWithValue("@Notes", (object)notes ?? DBNull.Value);

                    var outParam = new SqlParameter("@NewBillingRateId", SqlDbType.Int)
                    { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(outParam);

                    c.Open();
                    cmd.ExecuteNonQuery();

                    var result = outParam.Value;
                    return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : -1;
                }
            }
            catch (Exception ex) { LogError("InsertBillingRate", ex); return -1; }
        }

        /// <summary>
        /// Update an existing billing rate.
        /// If isDefault=true, automatically unsets all other defaults.
        /// If isDefault=false, ensures at least one other default exists.
        /// Returns true on success.
        /// </summary>
        public bool UpdateBillingRate(int billingRateId, string name, decimal ratePerMinute,
            string currency, DateTime? effectiveFrom, DateTime? effectiveTo, bool isActive, bool isDefault, string notes = null)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_UpdateBillingRate", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@BillingRateId", billingRateId);
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@RatePerMinute", ratePerMinute);
                    cmd.Parameters.AddWithValue("@Currency", currency);
                    cmd.Parameters.AddWithValue("@EffectiveFrom", (object)effectiveFrom ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EffectiveTo", (object)effectiveTo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IsActive", isActive);
                    cmd.Parameters.AddWithValue("@IsDefault", isDefault);
                    cmd.Parameters.AddWithValue("@Notes", (object)notes ?? DBNull.Value);

                    c.Open();
                    var result = cmd.ExecuteScalar();
                    return (result != null && Convert.ToInt32(result) == 1);
                }
            }
            catch (Exception ex) { LogError("UpdateBillingRate", ex); return false; }
        }

        /// <summary>
        /// Delete a billing rate if conditions are met:
        /// - At least one other rate exists
        /// - If this is the default, at least one other default exists
        /// Returns true on success, false on error or validation failure.
        /// </summary>
        public bool DeleteBillingRate(int billingRateId)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_DeleteBillingRate", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@BillingRateId", billingRateId);
                    c.Open();
                    var result = cmd.ExecuteScalar();
                    return (result != null && Convert.ToInt32(result) == 1);
                }
            }
            catch (Exception ex) { LogError("DeleteBillingRate", ex); return false; }
        }

        /// <summary>
        /// Set a specific billing rate as the default.
        /// Automatically unsets all other defaults.
        /// Returns true on success.
        /// </summary>
        public bool SetDefaultBillingRate(int billingRateId)
        {
            try
            {
                using (var c = Conn())
                using (var cmd = new SqlCommand("sp_SetDefaultBillingRate", c)
                { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@BillingRateId", billingRateId);
                    c.Open();
                    var result = cmd.ExecuteScalar();
                    return (result != null && Convert.ToInt32(result) == 1);
                }
            }
            catch (Exception ex) { LogError("SetDefaultBillingRate", ex); return false; }
        }

        // ═══════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════

        private static SqlParameter P(string name, object value)
            => new SqlParameter(name, value ?? DBNull.Value);

        private static void ExecSP(SqlConnection c, SqlTransaction tx,
            string spName, params SqlParameter[] parms)
        {
            using (var cmd = new SqlCommand(spName, c, tx)
            { CommandType = CommandType.StoredProcedure })
            {
                foreach (var p in parms) cmd.Parameters.Add(p);
                cmd.ExecuteNonQuery();
            }
        }

        private void LogError(string method, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DB.{method}] {ex.Message}");
            try { WriteSystemLog(null, null, null, null, "System", "DBError",
                $"DB.{method}: {ex.Message}"); }
            catch { /* swallow */ }
        }
    }
}
