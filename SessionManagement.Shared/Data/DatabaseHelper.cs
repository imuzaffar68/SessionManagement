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

        public DataRow AuthenticateUser(string username, string passwordHash)
        {
            string query = @"SELECT UserId, Username, FullName, Email, UserType, IsActive 
                           FROM tblUser 
                           WHERE Username = @Username AND PasswordHash = @PasswordHash AND IsActive = 1";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Username", username);
                        cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);

                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);

                            if (dt.Rows.Count > 0)
                            {
                                LogLoginAttempt(username, null, true, null);
                                return dt.Rows[0];
                            }
                            else
                            {
                                LogLoginAttempt(username, null, false, "Invalid credentials");
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

        public bool CreateUser(string username, string passwordHash, string fullName, string userType, string email = null, string phone = null)
        {
            string query = @"INSERT INTO tblUser (Username, PasswordHash, FullName, UserType, Email, PhoneNumber)
                           VALUES (@Username, @PasswordHash, @FullName, @UserType, @Email, @Phone)";

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
                        cmd.Parameters.AddWithValue("@UserType", userType);
                        cmd.Parameters.AddWithValue("@Email", (object)email ?? DBNull.Value);
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

        private void LogLoginAttempt(string username, int? clientId, bool isSuccessful, string failureReason)
        {
            string query = @"INSERT INTO tblLoginAttempt (Username, ClientId, IsSuccessful, FailureReason)
                           VALUES (@Username, @ClientId, @IsSuccessful, @FailureReason)";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Username", username);
                        cmd.Parameters.AddWithValue("@ClientId", (object)clientId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IsSuccessful", isSuccessful);
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

        public int StartSession(int userId, int clientId, int selectedDuration)
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
                        cmd.Parameters.AddWithValue("@ClientId", clientId);
                        cmd.Parameters.AddWithValue("@SelectedDuration", selectedDuration);

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
                        cmd.Parameters.AddWithValue("@TerminationType", terminationType);

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
                           FROM tblSession s
                           INNER JOIN tblUser u ON s.UserId = u.UserId
                           INNER JOIN tblClientMachine c ON s.ClientId = c.ClientId
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
            string query = @"SELECT ClientId, ClientCode, MachineName, IpAddress, MacAddress, 
                           Status, LastActiveTime FROM tblClientMachine ORDER BY ClientCode";

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

        public bool UpdateClientStatus(int clientId, string status)
        {
            string query = @"UPDATE tblClientMachine 
                           SET Status = @Status, LastActiveTime = GETDATE() 
                           WHERE ClientId = @ClientId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ClientId", clientId);
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
            string query = "SELECT ClientId FROM tblClientMachine WHERE ClientCode = @ClientCode";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ClientCode", clientCode);
                        object result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetClientIdByCode", ex);
                return 0;
            }
        }

        #endregion

        #region Image Management

        public bool SaveLoginImage(int sessionId, int userId, byte[] imageData, string imagePath, string imageStatus)
        {
            string query = @"INSERT INTO tblLoginImage (SessionId, UserId, ImagePath, ImageData, ImageStatus)
                           VALUES (@SessionId, @UserId, @ImagePath, @ImageData, @ImageStatus)";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@ImagePath", (object)imagePath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ImageData", (object)imageData ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ImageStatus", imageStatus);
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
            string query = "SELECT ImageData FROM tblLoginImage WHERE SessionId = @SessionId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        object result = cmd.ExecuteScalar();
                        return result != DBNull.Value ? (byte[])result : null;
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

        public bool LogSecurityAlert(int? sessionId, int? userId, int? clientId,
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
                        cmd.Parameters.AddWithValue("@SessionId", (object)sessionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ClientId", (object)clientId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@AlertType", alertType);
                        cmd.Parameters.AddWithValue("@AlertDescription", alertDescription);
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
            string query = @"SELECT a.*, u.Username, c.ClientCode 
                           FROM tblAlert a
                           LEFT JOIN tblUser u ON a.UserId = u.UserId
                           LEFT JOIN tblClientMachine c ON a.ClientId = c.ClientId
                           WHERE IsAcknowledged = 0
                           ORDER BY AlertTimestamp DESC";

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

        public bool AcknowledgeAlert(int alertId, int acknowledgedBy)
        {
            string query = @"UPDATE tblAlert 
                           SET IsAcknowledged = 1, AcknowledgedBy = @AcknowledgedBy, 
                               AcknowledgedDate = GETDATE()
                           WHERE AlertId = @AlertId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@AlertId", alertId);
                        cmd.Parameters.AddWithValue("@AcknowledgedBy", acknowledgedBy);
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
            string query = @"SELECT BillingAmount FROM tblBillingRecord WHERE SessionId = @SessionId";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
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
            string query = @"SELECT TOP 1 RatePerMinute FROM tblBillingRate 
                           WHERE IsActive = 1 ORDER BY EffectiveDate DESC";

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
            string query = @"SELECT * FROM vw_SessionReport 
                           WHERE StartTime >= @FromDate AND StartTime <= @ToDate
                           ORDER BY StartTime DESC";

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

        public void LogSystemEvent(int? sessionId, int? userId, int? clientId,
            string logType, string logMessage, string logLevel)
        {
            string query = @"INSERT INTO tblSystemLog (SessionId, UserId, ClientId, LogType, LogMessage, LogLevel)
                           VALUES (@SessionId, @UserId, @ClientId, @LogType, @LogMessage, @LogLevel)";

            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", (object)sessionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ClientId", (object)clientId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@LogType", logType);
                        cmd.Parameters.AddWithValue("@LogMessage", logMessage);
                        cmd.Parameters.AddWithValue("@LogLevel", logLevel);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                // Cannot log to database, write to event log or file
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