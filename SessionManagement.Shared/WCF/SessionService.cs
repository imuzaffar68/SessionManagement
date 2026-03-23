using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.ServiceModel;
using SessionManagement.Data;
using SessionManagement.Security;

namespace SessionManagement.WCF
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple,
                     InstanceContextMode = InstanceContextMode.Single)]
    public class SessionService : ISessionService
    {
        private readonly DatabaseHelper dbHelper;
        private static Dictionary<string, ISessionServiceCallback> subscribedClients =
            new Dictionary<string, ISessionServiceCallback>();

        public SessionService()
        {
            dbHelper = new DatabaseHelper();
        }

        #region Authentication

        public AuthenticationResponse AuthenticateUser(string username, string password, string clientCode)
        {
            try
            {
                // Get user from database by username (including password hash)
                DataRow userRow = dbHelper.AuthenticateUser(username);

                if (userRow != null)
                {
                    // Verify password using BCrypt
                    string storedHash = userRow["PasswordHash"].ToString();
                    bool passwordVerified = AuthenticationHelper.VerifyPassword(password, storedHash);

                    // Log verification attempt for debugging
                    System.Diagnostics.Debug.WriteLine($"[AUTH] User: {username}, Verified: {passwordVerified}, Role: {userRow["Role"]}");

                    if (passwordVerified)
                    {
                        // Password is correct - generate session token
                        string sessionToken = AuthenticationHelper.GenerateSessionToken();

                        return new AuthenticationResponse
                        {
                            IsAuthenticated = true,
                            UserId = Convert.ToInt32(userRow["UserId"]),
                            Username = userRow["Username"].ToString(),
                            FullName = userRow["FullName"].ToString(),
                            UserType = userRow["Role"].ToString(),
                            SessionToken = sessionToken,
                            ErrorMessage = null
                        };
                    }
                    else
                    {
                        // Password doesn't match
                        System.Diagnostics.Debug.WriteLine($"[AUTH] Password verification failed for user: {username}");
                        return new AuthenticationResponse
                        {
                            IsAuthenticated = false,
                            ErrorMessage = "Invalid username or password"
                        };
                    }
                }
                else
                {
                    // User not found
                    System.Diagnostics.Debug.WriteLine($"[AUTH] User not found: {username}");
                    return new AuthenticationResponse
                    {
                        IsAuthenticated = false,
                        ErrorMessage = "Invalid username or password"
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AUTH] Exception for user {username}: {ex.Message}");
                dbHelper.LogSystemEvent(null, null, null, "AuthenticationError",
                    $"Error authenticating user {username}: {ex.Message}", "Error");

                return new AuthenticationResponse
                {
                    IsAuthenticated = false,
                    ErrorMessage = "Authentication service error. Please try again."
                };
            }
        }

        public bool ValidateSession(string sessionToken)
        {
            // In production, validate against active session tokens stored in cache/database
            return !string.IsNullOrEmpty(sessionToken);
        }

        #endregion

        #region Session Management

        public SessionStartResponse StartSession(int userId, string clientCode, int durationMinutes)
        {
            try
            {
                // Get client ID
                int clientId = dbHelper.GetClientIdByCode(clientCode);
                System.Diagnostics.Debug.WriteLine($"[SESSION] StartSession - ClientCode: {clientCode}, ClientId: {clientId}, UserId: {userId}");

                if (clientId == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SESSION] ERROR - Client not found for code: {clientCode}");
                    return new SessionStartResponse
                    {
                        Success = false,
                        ErrorMessage = "Client not found"
                    };
                }

                // Start session in database
                int sessionId = dbHelper.StartSession(userId, clientId, durationMinutes);
                System.Diagnostics.Debug.WriteLine($"[SESSION] StartSession - SessionId: {sessionId}");

                if (sessionId > 0)
                {
                    DateTime startTime = DateTime.Now;
                    DateTime expectedEndTime = startTime.AddMinutes(durationMinutes);

                    dbHelper.LogSystemEvent(sessionId, userId, clientId, "SessionStart",
                        $"Session started for {durationMinutes} minutes", "Info");

                    return new SessionStartResponse
                    {
                        Success = true,
                        SessionId = sessionId,
                        StartTime = startTime,
                        ExpectedEndTime = expectedEndTime,
                        ErrorMessage = null
                    };
                }
                else
                {
                    return new SessionStartResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to start session"
                    };
                }
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, userId, null, "SessionStartError",
                    $"Error starting session: {ex.Message}", "Error");

                return new SessionStartResponse
                {
                    Success = false,
                    ErrorMessage = "Session start failed. Please try again."
                };
            }
        }

        public bool EndSession(int sessionId, string terminationType)
        {
            try
            {
                bool success = dbHelper.EndSession(sessionId, terminationType);

                if (success)
                {
                    DataRow sessionRow = dbHelper.GetSessionById(sessionId);
                    if (sessionRow != null)
                    {
                        int userId = Convert.ToInt32(sessionRow["UserId"]);
                        int clientId = Convert.ToInt32(sessionRow["ClientMachineId"]);
                        string clientCode = sessionRow["ClientCode"].ToString();

                        dbHelper.LogSystemEvent(sessionId, userId, clientId, "SessionEnd",
                            $"Session ended - Type: {terminationType}", "Info");

                        // Notify client via callback
                        NotifyClient(clientCode, callback =>
                            callback.OnSessionTerminated(sessionId, terminationType));
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(sessionId, null, null, "SessionEndError",
                    $"Error ending session: {ex.Message}", "Error");
                return false;
            }
        }

        public SessionInfo GetSessionInfo(int sessionId)
        {
            try
            {
                DataRow sessionRow = dbHelper.GetSessionById(sessionId);
                if (sessionRow != null)
                {
                    return MapToSessionInfo(sessionRow);
                }
                return null;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(sessionId, null, null, "GetSessionInfoError",
                    $"Error retrieving session info: {ex.Message}", "Error");
                return null;
            }
        }

        public SessionInfo[] GetActiveSessions()
        {
            try
            {
                DataTable dt = dbHelper.GetActiveSessions();
                List<SessionInfo> sessions = new List<SessionInfo>();

                foreach (DataRow row in dt.Rows)
                {
                    sessions.Add(MapToSessionInfo(row));
                }

                return sessions.ToArray();
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "GetActiveSessionsError",
                    $"Error retrieving active sessions: {ex.Message}", "Error");
                return new SessionInfo[0];
            }
        }

        private SessionInfo MapToSessionInfo(DataRow row)
        {
            DateTime startTime = Convert.ToDateTime(row["StartedAt"]);
            DateTime expectedEndTime = Convert.ToDateTime(row["ExpectedEndAt"]);
            int selectedDuration = Convert.ToInt32(row["SelectedDurationMinutes"]);

            // Calculate remaining minutes
            TimeSpan remaining = expectedEndTime - DateTime.Now;
            int remainingMinutes = Math.Max(0, (int)remaining.TotalMinutes);

            // Calculate current billing
            decimal rate = dbHelper.GetCurrentRate();
            int elapsedMinutes = selectedDuration - remainingMinutes;
            decimal currentBilling = elapsedMinutes * rate;

            return new SessionInfo
            {
                SessionId = Convert.ToInt32(row["SessionId"]),
                UserId = Convert.ToInt32(row["UserId"]),
                Username = row["Username"].ToString(),
                FullName = row["FullName"].ToString(),
                ClientCode = row.Table.Columns.Contains("ClientCode") ? row["ClientCode"].ToString() : "",
                MachineName = row.Table.Columns.Contains("MachineName") ? row["MachineName"].ToString() : "",
                StartTime = startTime,
                SelectedDuration = selectedDuration,
                ExpectedEndTime = expectedEndTime,
                SessionStatus = row["Status"].ToString(),
                RemainingMinutes = remainingMinutes,
                CurrentBilling = currentBilling
            };
        }

        #endregion

        #region Image Transfer

        public bool UploadLoginImage(int sessionId, int userId, string imageBase64)
        {
            try
            {
                if (string.IsNullOrEmpty(imageBase64))
                {
                    dbHelper.SaveLoginImage(sessionId, userId, null, null, "Missing");
                    return false;
                }

                // Convert Base64 to byte array
                byte[] imageData = Convert.FromBase64String(imageBase64);

                // Save to database
                bool success = dbHelper.SaveLoginImage(sessionId, userId, imageData, null, "Captured");

                if (success)
                {
                    dbHelper.LogSystemEvent(sessionId, userId, null, "ImageUpload",
                        "Login image uploaded successfully", "Info");
                }

                return success;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(sessionId, userId, null, "ImageUploadError",
                    $"Error uploading image: {ex.Message}", "Error");
                return false;
            }
        }

        public string DownloadLoginImage(int sessionId)
        {
            try
            {
                byte[] imageData = dbHelper.GetLoginImage(sessionId);
                if (imageData != null && imageData.Length > 0)
                {
                    return Convert.ToBase64String(imageData);
                }
                return null;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(sessionId, null, null, "ImageDownloadError",
                    $"Error downloading image: {ex.Message}", "Error");
                return null;
            }
        }

        #endregion

        #region Client Management

        public bool RegisterClient(string clientCode, string machineName, string ipAddress, string macAddress)
        {
            try
            {
                // Check if client exists
                int existingClientId = dbHelper.GetClientIdByCode(clientCode);
                if (existingClientId > 0)
                {
                    // Update existing client
                    return dbHelper.UpdateClientStatus(existingClientId, "Online");
                }

                // Register new client would require an INSERT method in DatabaseHelper
                // For now, assume clients are pre-registered
                dbHelper.LogSystemEvent(null, null, null, "ClientRegistration",
                    $"Client {clientCode} attempted registration", "Info");

                return true;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "ClientRegistrationError",
                    $"Error registering client {clientCode}: {ex.Message}", "Error");
                return false;
            }
        }

        public bool UpdateClientStatus(string clientCode, string status)
        {
            try
            {
                int clientId = dbHelper.GetClientIdByCode(clientCode);
                if (clientId > 0)
                {
                    return dbHelper.UpdateClientStatus(clientId, status);
                }
                return false;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "ClientStatusUpdateError",
                    $"Error updating client status: {ex.Message}", "Error");
                return false;
            }
        }

        public ClientInfo[] GetAllClients()
        {
            try
            {
                DataTable dt = dbHelper.GetAllClients();
                List<ClientInfo> clients = new List<ClientInfo>();

                foreach (DataRow row in dt.Rows)
                {
                    clients.Add(new ClientInfo
                    {
                        ClientId = Convert.ToInt32(row["ClientMachineId"]),
                        ClientCode = row["ClientCode"].ToString(),
                        MachineName = row["MachineName"].ToString(),
                        IpAddress = row["IpAddress"].ToString(),
                        MacAddress = row["MacAddress"]?.ToString(),
                        Status = row["Status"].ToString(),
                        LastActiveTime = row["LastSeenAt"] != DBNull.Value
                            ? Convert.ToDateTime(row["LastSeenAt"])
                            : (DateTime?)null
                    });
                }

                return clients.ToArray();
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "GetClientsError",
                    $"Error retrieving clients: {ex.Message}", "Error");
                return new ClientInfo[0];
            }
        }

        #endregion

        #region Alerts & Monitoring

        public bool LogSecurityAlert(int sessionId, int userId, string alertType, string description, string severity)
        {
            try
            {
                DataRow sessionRow = dbHelper.GetSessionById(sessionId);
                int? clientId = null;

                if (sessionRow != null)
                {
                    clientId = Convert.ToInt32(sessionRow["ClientId"]);
                }

                bool success = dbHelper.LogSecurityAlert(sessionId, userId, clientId, alertType, description, severity);

                if (success)
                {
                    // Notify all subscribed admin clients
                    BroadcastToAllClients(callback =>
                        callback.OnServerMessage($"Security Alert: {alertType} - {description}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(sessionId, userId, null, "AlertLogError",
                    $"Error logging security alert: {ex.Message}", "Error");
                return false;
            }
        }

        public AlertInfo[] GetUnacknowledgedAlerts()
        {
            try
            {
                DataTable dt = dbHelper.GetUnacknowledgedAlerts();
                List<AlertInfo> alerts = new List<AlertInfo>();

                foreach (DataRow row in dt.Rows)
                {
                    alerts.Add(new AlertInfo
                    {
                        AlertId = Convert.ToInt32(row["AlertId"]),
                        SessionId = row["SessionId"] != DBNull.Value ? Convert.ToInt32(row["SessionId"]) : (int?)null,
                        Username = row["Username"]?.ToString(),
                        ClientCode = row["ClientCode"]?.ToString(),
                        AlertType = row["AlertType"].ToString(),
                        Description = row["AlertDescription"].ToString(),
                        Timestamp = Convert.ToDateTime(row["AlertTimestamp"]),
                        Severity = row["Severity"].ToString()
                    });
                }

                return alerts.ToArray();
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "GetAlertsError",
                    $"Error retrieving alerts: {ex.Message}", "Error");
                return new AlertInfo[0];
            }
        }

        public bool AcknowledgeAlert(int alertId, int adminUserId)
        {
            try
            {
                return dbHelper.AcknowledgeAlert(alertId, adminUserId);
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, adminUserId, null, "AcknowledgeAlertError",
                    $"Error acknowledging alert: {ex.Message}", "Error");
                return false;
            }
        }

        #endregion

        #region Billing

        public decimal GetCurrentBillingRate()
        {
            try
            {
                return dbHelper.GetCurrentRate();
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "GetRateError",
                    $"Error retrieving billing rate: {ex.Message}", "Error");
                return 0.05m; // Default rate
            }
        }

        public decimal CalculateSessionBilling(int sessionId)
        {
            try
            {
                return dbHelper.CalculateBilling(sessionId);
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(sessionId, null, null, "CalculateBillingError",
                    $"Error calculating billing: {ex.Message}", "Error");
                return 0;
            }
        }

        #endregion

        #region Reports

        public ReportData GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            try
            {
                DataTable dt = dbHelper.GetSessionReport(fromDate, toDate);

                decimal totalRevenue = 0;
                double totalHours = 0;
                List<SessionInfo> sessions = new List<SessionInfo>();

                foreach (DataRow row in dt.Rows)
                {
                    if (row["BillingAmount"] != DBNull.Value)
                    {
                        totalRevenue += Convert.ToDecimal(row["BillingAmount"]);
                    }

                    if (row["ActualDurationMinutes"] != DBNull.Value)
                    {
                        totalHours += Convert.ToInt32(row["ActualDurationMinutes"]) / 60.0;
                    }

                    // Add session to list (simplified)
                }

                return new ReportData
                {
                    TotalSessions = dt.Rows.Count,
                    TotalRevenue = totalRevenue,
                    TotalHours = totalHours,
                    FromDate = fromDate,
                    ToDate = toDate,
                    Sessions = sessions.ToArray()
                };
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "GetReportError",
                    $"Error generating report: {ex.Message}", "Error");
                return new ReportData();
            }
        }

        #endregion

        #region Duplex Communication

        public void SubscribeForNotifications(string clientCode)
        {
            try
            {
                ISessionServiceCallback callback = OperationContext.Current.GetCallbackChannel<ISessionServiceCallback>();

                lock (subscribedClients)
                {
                    if (!subscribedClients.ContainsKey(clientCode))
                    {
                        subscribedClients.Add(clientCode, callback);
                        dbHelper.LogSystemEvent(null, null, null, "ClientSubscribed",
                            $"Client {clientCode} subscribed for notifications", "Info");
                    }
                }
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "SubscribeError",
                    $"Error subscribing client: {ex.Message}", "Error");
            }
        }

        public void UnsubscribeFromNotifications(string clientCode)
        {
            try
            {
                lock (subscribedClients)
                {
                    if (subscribedClients.ContainsKey(clientCode))
                    {
                        subscribedClients.Remove(clientCode);
                        dbHelper.LogSystemEvent(null, null, null, "ClientUnsubscribed",
                            $"Client {clientCode} unsubscribed", "Info");
                    }
                }
            }
            catch (Exception ex)
            {
                dbHelper.LogSystemEvent(null, null, null, "UnsubscribeError",
                    $"Error unsubscribing client: {ex.Message}", "Error");
            }
        }

        private void NotifyClient(string clientCode, Action<ISessionServiceCallback> action)
        {
            lock (subscribedClients)
            {
                if (subscribedClients.ContainsKey(clientCode))
                {
                    try
                    {
                        action(subscribedClients[clientCode]);
                    }
                    catch
                    {
                        subscribedClients.Remove(clientCode);
                    }
                }
            }
        }

        private void BroadcastToAllClients(Action<ISessionServiceCallback> action)
        {
            lock (subscribedClients)
            {
                List<string> disconnectedClients = new List<string>();

                foreach (var kvp in subscribedClients)
                {
                    try
                    {
                        action(kvp.Value);
                    }
                    catch
                    {
                        disconnectedClients.Add(kvp.Key);
                    }
                }

                foreach (string clientCode in disconnectedClients)
                {
                    subscribedClients.Remove(clientCode);
                }
            }
        }

        #endregion
    }
}