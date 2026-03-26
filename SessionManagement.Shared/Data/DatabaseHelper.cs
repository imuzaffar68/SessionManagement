using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace SessionManagement.Data
{
    public class DatabaseHelper
    {
        private readonly string connectionString;

        public DatabaseHelper()
        {
            connectionString = ConfigurationManager.ConnectionStrings["SessionManagementDB"].ConnectionString;
        }

        public DatabaseHelper(string connString)
        {
            connectionString = connString;
        }

        #region Connection Management

        private SqlConnection GetConnection()
        {
            return new SqlConnection(connectionString);
        }

        public bool TestConnection()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError("TestConnection", ex);
                return false;
            }
        }

        #endregion

        #region User Authentication

        public DataRow AuthenticateUser(string username)
        {
            string query = @"SELECT UserId, Username, FullName, Role, Status, PasswordHash
                           FROM dbo.tblUser 
                           WHERE Username = @Username AND Status = 'Active'";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Username", username);

                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);

                            if (dt.Rows.Count > 0)
                            {
                                return dt.Rows[0];
                            }
                            else
                            {
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("AuthenticateUser", ex);
                return null;
            }
        }

        public bool CreateUser(string username, string passwordHash, string fullName, string role, string phone = null)
        {
            string query = @"INSERT INTO dbo.tblUser (Username, PasswordHash, FullName, Role, Status, Phone)
                           VALUES (@Username, @PasswordHash, @FullName, @Role, 'Active', @Phone)";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Username", username);
                        cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                        cmd.Parameters.AddWithValue("@FullName", fullName);
                        cmd.Parameters.AddWithValue("@Role", role);
                        cmd.Parameters.AddWithValue("@Phone", (object)phone ?? DBNull.Value);

                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("CreateUser", ex);
                return false;
            }
        }

        private void LogLoginAttempt(string username, int? clientMachineId, bool isSuccessful, string failureReason)
        {
            string query = @"INSERT INTO dbo.tblLoginAttempt (ClientMachineId, UserId, UsernameEntered, AttemptedAt, IsSuccess, FailureReason)
                           SELECT @ClientMachineId, UserId, @UsernameEntered, GETDATE(), @IsSuccess, @FailureReason
                           FROM dbo.tblUser WHERE Username = @Username
                           UNION ALL
                           SELECT @ClientMachineId, NULL, @UsernameEntered, GETDATE(), @IsSuccess, @FailureReason
                           WHERE NOT EXISTS (SELECT 1 FROM dbo.tblUser WHERE Username = @Username)";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ClientMachineId", (object)clientMachineId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Username", username);
                        cmd.Parameters.AddWithValue("@UsernameEntered", username);
                        cmd.Parameters.AddWithValue("@IsSuccess", isSuccessful);
                        cmd.Parameters.AddWithValue("@FailureReason", (object)failureReason ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("LogLoginAttempt", ex);
            }
        }

        #endregion

        #region Session Management

        public int StartSession(int userId, int clientMachineId, int selectedDuration)
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("sp_StartSession", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@ClientMachineId", clientMachineId);
                        cmd.Parameters.AddWithValue("@SelectedDurationMinutes", selectedDuration);

                        object result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("StartSession", ex);
                return 0;
            }
        }

        public bool EndSession(int sessionId, string terminationType)
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("sp_EndSession", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        cmd.Parameters.AddWithValue("@TerminationReason", terminationType);

                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("EndSession", ex);
                return false;
            }
        }

        public DataTable GetActiveSessions()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("sp_GetActiveSessions", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            return dt;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetActiveSessions", ex);
                return new DataTable();
            }
        }

        public DataRow GetSessionById(int sessionId)
        {
            string query = @"SELECT s.*, u.Username, u.FullName, c.ClientCode, c.MachineName
                           FROM dbo.tblSession s
                           INNER JOIN dbo.tblUser u ON s.UserId = u.UserId
                           INNER JOIN dbo.tblClientMachine c ON s.ClientMachineId = c.ClientMachineId
                           WHERE s.SessionId = @SessionId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            return dt.Rows.Count > 0 ? dt.Rows[0] : null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetSessionById", ex);
                return null;
            }
        }

        #endregion

        #region Client Management

        public DataTable GetAllClients()
        {
            string query = @"SELECT ClientMachineId, ClientCode, MachineName, IPAddress, MACAddress, 
                           Status, LastSeenAt FROM dbo.tblClientMachine ORDER BY ClientCode";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            return dt;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetAllClients", ex);
                return new DataTable();
            }
        }

        public bool UpdateClientStatus(int clientMachineId, string status)
        {
            string query = @"UPDATE dbo.tblClientMachine 
                           SET Status = @Status, LastSeenAt = GETDATE() 
                           WHERE ClientMachineId = @ClientMachineId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ClientMachineId", clientMachineId);
                        cmd.Parameters.AddWithValue("@Status", status);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("UpdateClientStatus", ex);
                return false;
            }
        }

        public int GetClientIdByCode(string clientCode)
        {
            string query = "SELECT ClientMachineId FROM dbo.tblClientMachine WHERE ClientCode = @ClientCode";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ClientCode", clientCode);
                        object result = cmd.ExecuteScalar();
                        int clientId = result != null ? Convert.ToInt32(result) : 0;
                        System.Diagnostics.Debug.WriteLine($"[DB] GetClientIdByCode - Code: {clientCode}, Result: {clientId}");
                        return clientId;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] GetClientIdByCode ERROR - Code: {clientCode}, Error: {ex.Message}");
                LogError("GetClientIdByCode", ex);
                return 0;
            }
        }

        #endregion

        #region Image Management

        public bool SaveLoginImage(int sessionId, int userId, byte[] imageData, string imagePath, string imageStatus)
        {
            string query = @"INSERT INTO dbo.tblSessionImage (SessionId, CaptureStatus, UploadStatus, ImagePath, Notes)
                           VALUES (@SessionId, @CaptureStatus, @UploadStatus, @ImagePath, @Notes)";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        cmd.Parameters.AddWithValue("@CaptureStatus", imageStatus);
                        cmd.Parameters.AddWithValue("@UploadStatus", "Pending");
                        cmd.Parameters.AddWithValue("@ImagePath", (object)imagePath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Notes", (object)"Image uploaded by user" ?? DBNull.Value);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("SaveLoginImage", ex);
                return false;
            }
        }

        public byte[] GetLoginImage(int sessionId)
        {
            string query = "SELECT ImagePath FROM dbo.tblSessionImage WHERE SessionId = @SessionId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        object result = cmd.ExecuteScalar();
                        return result != DBNull.Value ? System.IO.File.ReadAllBytes(result.ToString()) : null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetLoginImage", ex);
                return null;
            }
        }

        #endregion

        #region Alerts Management

        public bool LogSecurityAlert(int? sessionId, int? userId, int? clientMachineId,
            string alertType, string alertDescription, string severity)
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("sp_LogSecurityAlert", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@ActivityTypeName", alertType);
                        cmd.Parameters.AddWithValue("@SessionId", (object)sessionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ClientMachineId", (object)clientMachineId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Details", alertDescription);
                        cmd.Parameters.AddWithValue("@Severity", severity);

                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("LogSecurityAlert", ex);
                return false;
            }
        }

        public DataTable GetUnacknowledgedAlerts()
        {
            string query = @"SELECT a.AlertId, a.ActivityTypeId, a.SessionId, a.ClientMachineId, a.UserId, 
                           a.DetectedAt, a.Severity, a.Details, a.Status,
                           u.Username, c.ClientCode 
                           FROM dbo.tblAlert a
                           LEFT JOIN dbo.tblUser u ON a.UserId = u.UserId
                           LEFT JOIN dbo.tblClientMachine c ON a.ClientMachineId = c.ClientMachineId
                           WHERE a.IsAcknowledged = 0
                           ORDER BY a.DetectedAt DESC";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            return dt;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetUnacknowledgedAlerts", ex);
                return new DataTable();
            }
        }

        public bool AcknowledgeAlert(int alertId, int acknowledgedByAdminUserId)
        {
            string query = @"UPDATE dbo.tblAlert 
                           SET IsAcknowledged = 1, AcknowledgedByAdminUserId = @AdminUserId, 
                               AcknowledgedAt = GETDATE(), Status = 'Acknowledged'
                           WHERE AlertId = @AlertId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@AlertId", alertId);
                        cmd.Parameters.AddWithValue("@AdminUserId", acknowledgedByAdminUserId);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("AcknowledgeAlert", ex);
                return false;
            }
        }

        #endregion

        #region Billing

        public decimal CalculateBilling(int sessionId)
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("sp_CalculateSessionBilling", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        object result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToDecimal(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("CalculateBilling", ex);
                return 0;
            }
        }

        public decimal GetCurrentRate()
        {
            string query = @"SELECT TOP 1 RatePerMinute FROM dbo.tblBillingRate 
                           WHERE IsActive = 1 AND IsDefault = 1
                           ORDER BY CreatedAt DESC";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        object result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToDecimal(result) : 0.05m; // Default rate
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetCurrentRate", ex);
                return 0.05m;
            }
        }

        #endregion

        #region Reports

        public DataTable GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            string query = @"SELECT * FROM dbo.vw_SessionReport 
                           WHERE StartedAt >= @FromDate AND StartedAt <= @ToDate
                           ORDER BY StartedAt DESC";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@FromDate", fromDate);
                        cmd.Parameters.AddWithValue("@ToDate", toDate.AddDays(1).AddSeconds(-1));
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            return dt;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetSessionReport", ex);
                return new DataTable();
            }
        }

        #endregion

        #region System Logging

        public void LogSystemEvent(int? sessionId, int? userId, int? clientMachineId,
            string logType, string logMessage, string logLevel)
        {
            string query = @"INSERT INTO dbo.tblSystemLog (SessionId, UserId, ClientMachineId, Category, Type, Message, Source)
                           VALUES (@SessionId, @UserId, @ClientMachineId, @Category, @Type, @Message, 'Server')";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", (object)sessionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ClientMachineId", (object)clientMachineId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Category", logLevel);  // Category should be the level (Info, Warning, Error)
                        cmd.Parameters.AddWithValue("@Type", logType);       // Type should be the log type (AuthenticationError, etc)
                        cmd.Parameters.AddWithValue("@Message", logMessage);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogSystemEvent Error: {ex.Message}");
            }
        }

        private void LogError(string methodName, Exception ex)
        {
            string errorMessage = $"Error in {methodName}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(errorMessage);

            try
            {
                LogSystemEvent(null, null, null, "Error", errorMessage, "Error");
            }
            catch
            {
                // Ignore if logging fails
            }
        }

        #endregion
    }
}