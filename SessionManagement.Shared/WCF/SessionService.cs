using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using SessionManagement.Data;
using SessionManagement.Security;

namespace SessionManagement.WCF
{
    /// <summary>
    /// WCF service implementation.  Every public method follows the matching
    /// sequence diagram exactly: validate → DB read/write → callback → log.
    /// </summary>
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple,
                     InstanceContextMode = InstanceContextMode.Single)]
    public class SessionService : ISessionService, IDisposable
    {
        // ── dependencies ──────────────────────────────────────────
        private readonly DatabaseHelper _db;

        // ── WCF duplex subscriptions: clientCode → callback ───────
        private static readonly ConcurrentDictionary<string, ISessionServiceCallback>
            _subs = new ConcurrentDictionary<string, ISessionServiceCallback>(
                StringComparer.OrdinalIgnoreCase);

        // ── server-side session expiry (every 30 s) ───────────────
        private readonly Timer _expiryTimer;

        // ── image storage path ────────────────────────────────────
        private readonly string _imgPath;

        // ─────────────────────────────────────────────────────────
        public SessionService()
        {
            _db = new DatabaseHelper();
            _imgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");
            Directory.CreateDirectory(_imgPath);

            _expiryTimer = new Timer(ServerExpiryScan, null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            _db.WriteSystemLog(null, null, null, null,
                "System", "ServiceStart", "SessionService started", "Server");
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-01 / UC-09  —  AUTHENTICATION
        //  SEQ-01: Client→Server→DB→Server→Client
        // ═══════════════════════════════════════════════════════════

        public AuthenticationResponse AuthenticateUser(
            string username, string password, string clientCode)
        {
            // SEQ-01 step 1: resolve machine id (needed for LoginAttempt row)
            int machineId = _db.GetClientMachineIdByCode(clientCode);
            bool isAdminLogin = string.Equals(clientCode, "ADMIN", StringComparison.OrdinalIgnoreCase);

            try
            {
                // SEQ-01 step 3: query DB for user row
                DataRow user = _db.GetUserByUsername(username);

                if (user == null)
                {
                    // Only log login attempt if not admin login and machineId is valid
                    if (!isAdminLogin && machineId != 0)
                        _db.InsertLoginAttempt(machineId, null, username, false, "UnknownUser");

                    // Log Auth event (set clientMachineId to null for admin login)
                    _db.WriteSystemLog(null, null, isAdminLogin ? (int?)null : machineId, null,
                        "Auth", "LoginFailed",
                        $"Unknown user '{username}' from client {clientCode}", "Server");

                    return Fail("Invalid username or password.");
                }

                int    userId     = Convert.ToInt32(user["UserId"]);
                string hash       = user["PasswordHash"].ToString();
                string status     = user["Role"].ToString();       // Role column = 'Admin'|'ClientUser'
                string userStatus = user["Status"].ToString();     // Status column = 'Active'|'Blocked'|'Disabled'

                // SEQ-01 step 3b: BCrypt verify
                bool verified = AuthenticationHelper.VerifyPassword(password, hash);

                if (!verified)
                {
                    if (!isAdminLogin && machineId != 0)
                        _db.InsertLoginAttempt(machineId, userId, username, false, "InvalidPassword");

                    _db.WriteSystemLog(null, userId, isAdminLogin ? (int?)null : machineId, null,
                        "Auth", "LoginFailed",
                        $"Bad password for '{username}'", "Server");

                    // FR-12: check for repeated failures → alert
                    if (!isAdminLogin && machineId != 0)
                    {
                        int fails = _db.CountRecentFailedLogins(machineId, withinMinutes: 10);
                        if (fails >= 3)
                        {
                            _db.InsertSecurityAlert("RepeatedLoginFailure", null,
                                machineId, userId,
                                $"{fails} failed attempts in 10 min on {clientCode}", "High");

                            // FR-14: push real-time alert to all admins
                            Broadcast(cb => cb.OnServerMessage(
                                $"[High] ALERT: RepeatedLoginFailure on {clientCode} — {fails} attempts"));
                        }
                    }

                    return Fail("Invalid username or password.");
                }

                // SEQ-01 step 3c: status checks
                if (userStatus == "Blocked")
                {
                    if (!isAdminLogin && machineId != 0)
                        _db.InsertLoginAttempt(machineId, userId, username, false, "BlockedUser");
                    _db.WriteSystemLog(null, userId, isAdminLogin ? (int?)null : machineId, null,
                        "Auth", "LoginBlocked",
                        $"Blocked user '{username}' attempted login", "Server");
                    return Fail("Your account has been blocked. Contact the administrator.");
                }

                if (userStatus == "Disabled")
                {
                    if (!isAdminLogin && machineId != 0)
                        _db.InsertLoginAttempt(machineId, userId, username, false, "BlockedUser");
                    return Fail("Your account is disabled.");
                }

                // SEQ-01 step 4 (success): write LoginAttempt + update LastLoginAt
                if (!isAdminLogin && machineId != 0)
                    _db.InsertLoginAttempt(machineId, userId, username, true);
                _db.UpdateLastLogin(userId);

                // SEQ-01 step 5: Auth log
                _db.WriteSystemLog(null, userId, isAdminLogin ? (int?)null : machineId, null,
                    "Auth", "LoginSuccess",
                    $"User '{username}' authenticated on {clientCode}", "Server");

                return new AuthenticationResponse
                {
                    IsAuthenticated = true,
                    UserId          = userId,
                    Username        = user["Username"].ToString(),
                    FullName        = user["FullName"]?.ToString() ?? "",
                    UserType        = user["Role"].ToString(),
                    SessionToken    = AuthenticationHelper.GenerateSessionToken()
                };
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, isAdminLogin ? (int?)null : machineId, null,
                    "Auth", "LoginError", ex.Message, "Server");
                return Fail("Authentication service error. Please try again.");
            }
        }

        public bool ValidateSession(string sessionToken)
            => !string.IsNullOrWhiteSpace(sessionToken);

        // ═══════════════════════════════════════════════════════════
        //  UC-02  —  START SESSION
        //  SEQ-02: confirm duration → create session → notify admin → log
        // ═══════════════════════════════════════════════════════════

        public SessionStartResponse StartSession(
            int userId, string clientCode, int durationMinutes)
        {
            try
            {
                int machineId = _db.GetClientMachineIdByCode(clientCode);
                if (machineId == 0)
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "Client machine not registered." };

                // SEQ-02 step 2: INSERT tblSession (sp_StartSession also writes SystemLog)
                int sessionId = _db.StartSession(userId, machineId, durationMinutes);
                if (sessionId == 0)
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "Failed to create session record." };

                // SEQ-02 step 3: mark machine Active
                _db.UpdateClientMachineStatus(machineId, "Active");

                DateTime start = DateTime.Now;
                DateTime end   = start.AddMinutes(durationMinutes);

                // SEQ-02 step 4: Session log
                _db.WriteSystemLog(sessionId, userId, machineId, null,
                    "Session", "SessionStarted",
                    $"Session {sessionId} started — {durationMinutes} min on {clientCode}",
                    "Server");

                // SEQ-02 step 5: push to all subscribed admins (FR-06 real-time monitor)
                Broadcast(cb => cb.OnServerMessage(
                    $"New session: {clientCode} | {durationMinutes} min | SessionId={sessionId}"));

                return new SessionStartResponse
                {
                    Success         = true,
                    SessionId       = sessionId,
                    StartTime       = start,
                    ExpectedEndTime = end
                };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, userId, null, "SessionStartError", ex.Message, "Error");
                return new SessionStartResponse
                { Success = false, ErrorMessage = "Session start failed. Please retry." };
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-07 / UC-08 / UC-14  —  END SESSION
        //  SEQ-07/08/14: end + finalize billing atomically → callback → log
        // ═══════════════════════════════════════════════════════════

        public bool EndSession(int sessionId, string terminationType)
        {
            try
            {
                DataRow row = _db.GetSessionById(sessionId);
                if (row == null) return false;

                int    userId    = Convert.ToInt32(row["UserId"]);
                int    machineId = Convert.ToInt32(row["ClientMachineId"]);
                string code      = row["ClientCode"].ToString();

                string reason = MapTerminationReason(terminationType);

                // SEQ-07/08/14 step 2: atomic end + billing finalize (NFR-14)
                bool ok = _db.EndSession(sessionId, reason);
                if (!ok) return false;

                // step 3: machine back to Idle
                _db.UpdateClientMachineStatus(machineId, "Idle");

                // step 4: Session log
                _db.WriteSystemLog(sessionId, userId, machineId, null,
                    "Session", "SessionEnded",
                    $"Session {sessionId} ended — reason: {reason}", "Server");

                // SEQ-14 step 5: push termination to the client machine (WCF callback)
                Notify(code, cb => cb.OnSessionTerminated(sessionId, reason));

                return true;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(sessionId, null, null, "SessionEndError", ex.Message, "Error");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-06 / UC-10  —  GET SESSION INFO / ACTIVE SESSIONS
        // ═══════════════════════════════════════════════════════════

        public SessionInfo GetSessionInfo(int sessionId)
        {
            try
            {
                var row = _db.GetSessionById(sessionId);
                return row != null ? Map(row) : null;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(sessionId, null, null, "GetSessionErr", ex.Message, "Error");
                return null;
            }
        }

        public SessionInfo[] GetActiveSessions()
        {
            try
            {
                DataTable dt  = _db.GetActiveSessions();
                var list = new List<SessionInfo>();
                foreach (DataRow r in dt.Rows) list.Add(Map(r));
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "GetActiveSessionsErr", ex.Message, "Error");
                return Array.Empty<SessionInfo>();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-04 / UC-05 / UC-12  —  IMAGE CAPTURE & TRANSFER
        //  SEQ-05: receive Base64 → save to disk → update tblSessionImage → log
        // ═══════════════════════════════════════════════════════════

        public bool UploadLoginImage(int sessionId, int userId, string imageBase64)
        {
            // SEQ-05 step 1: validate payload
            if (string.IsNullOrWhiteSpace(imageBase64))
            {
                _db.UpsertSessionImage(sessionId, "Failed", "Failed", null,
                    "Empty Base64 payload received by server");
                _db.WriteSystemLog(sessionId, userId, null, null,
                    "Session", "ImageUploadFailed", "Empty image payload", "Server");
                return false;
            }

            try
            {
                byte[]  bytes    = Convert.FromBase64String(imageBase64);
                string  fileName = $"s{sessionId}_u{userId}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string  fullPath = Path.Combine(_imgPath, fileName);

                // SEQ-05 step 2: save JPEG to disk
                File.WriteAllBytes(fullPath, bytes);

                // SEQ-05 step 3: write tblSessionImage (CaptureStatus=Captured, UploadStatus=Sent)
                _db.UpsertSessionImage(sessionId, "Captured", "Sent", fullPath);

                // SEQ-05 step 4: Session log
                _db.WriteSystemLog(sessionId, userId, null, null,
                    "Session", "ImageUploaded",
                    $"Login image saved: {fileName}", "Server");
                return true;
            }
            catch (Exception ex)
            {
                _db.UpsertSessionImage(sessionId, "Failed", "Failed", null, ex.Message);
                _db.WriteSystemLog(sessionId, userId, null, null,
                    "Session", "ImageUploadError", ex.Message, "Server");
                return false;
            }
        }

        /// <summary>UC-12: admin downloads image as Base64.</summary>
        public string DownloadLoginImage(int sessionId)
        {
            try
            {
                string path = _db.GetSessionImagePath(sessionId);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return Convert.ToBase64String(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(sessionId, null, null, "ImageDownloadErr", ex.Message, "Error");
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-11  —  CLIENT MACHINE MANAGEMENT
        // ═══════════════════════════════════════════════════════════

        public bool RegisterClient(string clientCode, string machineName,
            string ipAddress, string macAddress)
        {
            try
            {
                int id = _db.RegisterOrUpdateClient(clientCode, machineName,
                    ipAddress, macAddress);
                _db.WriteSystemLog(null, null, id, null,
                    "System", "ClientRegistered",
                    $"Client {clientCode} ({machineName}) registered/updated", "Server");
                return id > 0;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "ClientRegisterErr", ex.Message, "Error");
                return false;
            }
        }

        public bool UpdateClientStatus(string clientCode, string status)
        {
            try
            {
                int id = _db.GetClientMachineIdByCode(clientCode);
                if (id == 0) return false;
                return _db.UpdateClientMachineStatus(id, status);
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "UpdateClientStatusErr", ex.Message, "Error");
                return false;
            }
        }

        public ClientInfo[] GetAllClients()
        {
            try
            {
                DataTable dt   = _db.GetAllClientMachines();
                var list = new List<ClientInfo>();
                foreach (DataRow r in dt.Rows)
                {
                    list.Add(new ClientInfo
                    {
                        ClientId      = Convert.ToInt32(r["ClientMachineId"]),
                        ClientCode    = r["ClientCode"].ToString(),
                        MachineName   = r["MachineName"].ToString(),
                        IpAddress     = r["IPAddress"].ToString(),
                        MacAddress    = r["MACAddress"]?.ToString(),
                        Status        = r["Status"].ToString(),
                        LastActiveTime= r["LastSeenAt"] != DBNull.Value
                                        ? Convert.ToDateTime(r["LastSeenAt"])
                                        : (DateTime?)null,
                        CurrentUser   = r["CurrentUsername"]?.ToString()
                    });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "GetClientsErr", ex.Message, "Error");
                return Array.Empty<ClientInfo>();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-16 / UC-17  —  SECURITY ALERTS
        //  SEQ-16: detect → log alert → notify admin (real-time)
        //  SEQ-17: admin receives & acknowledges
        // ═══════════════════════════════════════════════════════════

        public bool LogSecurityAlert(int sessionId, int userId,
            string alertType, string description, string severity)
        {
            try
            {
                // SEQ-16 step 2: look up machine for complete alert row
                var   row       = _db.GetSessionById(sessionId);
                int?  machineId = row != null
                                  ? (int?)Convert.ToInt32(row["ClientMachineId"])
                                  : null;
                string code     = row?["ClientCode"]?.ToString();

                // SEQ-16 step 3: sp_LogSecurityAlert (writes tblAlert + tblSystemLog)
                bool ok = _db.InsertSecurityAlert(alertType,
                    sessionId == 0 ? (int?)null : sessionId,
                    machineId, userId, description, severity);

                if (ok)
                {
                    // SEQ-17 step 1: real-time push to all subscribed admins (FR-14)
                    Broadcast(cb => cb.OnServerMessage(
                        $"[{severity}] ALERT from {code ?? "??"}: {alertType} — {description}"));
                }
                return ok;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(sessionId, userId, null, "AlertLogErr", ex.Message, "Error");
                return false;
            }
        }

        public AlertInfo[] GetUnacknowledgedAlerts()
        {
            try
            {
                DataTable dt   = _db.GetUnacknowledgedAlerts();
                var list = new List<AlertInfo>();
                foreach (DataRow r in dt.Rows)
                {
                    list.Add(new AlertInfo
                    {
                        AlertId     = Convert.ToInt32(r["AlertId"]),
                        SessionId   = r["SessionId"] != DBNull.Value
                                      ? (int?)Convert.ToInt32(r["SessionId"]) : null,
                        Username    = r["Username"]?.ToString(),
                        ClientCode  = r["ClientCode"]?.ToString(),
                        AlertType   = r["ActivityTypeName"].ToString(),
                        Description = r["Details"].ToString(),
                        Timestamp   = Convert.ToDateTime(r["DetectedAt"]),
                        Severity    = r["Severity"].ToString()
                    });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "GetAlertsErr", ex.Message, "Error");
                return Array.Empty<AlertInfo>();
            }
        }

        public bool AcknowledgeAlert(int alertId, int adminUserId)
        {
            try
            {
                bool ok = _db.AcknowledgeAlert(alertId, adminUserId);
                if (ok)
                    _db.WriteSystemLog(null, adminUserId, null, adminUserId,
                        "Security", "AlertAcknowledged",
                        $"Alert {alertId} acknowledged by admin {adminUserId}", "Server");
                return ok;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, adminUserId, null, "AckAlertErr", ex.Message, "Error");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-07 / UC-13  —  BILLING
        // ═══════════════════════════════════════════════════════════

        public decimal GetCurrentBillingRate()
        {
            try { return _db.GetCurrentBillingRate(); }
            catch { return 0.50m; }
        }

        public decimal CalculateSessionBilling(int sessionId)
        {
            try { return _db.CalculateRunningBilling(sessionId); }
            catch (Exception ex)
            {
                _db.LogSystemEvent(sessionId, null, null, "BillingCalcErr", ex.Message, "Error");
                return 0m;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-18  —  REPORTS
        // ═══════════════════════════════════════════════════════════

        public ReportData GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            try
            {
                DataTable dt      = _db.GetSessionReport(fromDate, toDate);
                decimal   revenue = 0m;
                double    hours   = 0;
                var sessions = new List<SessionInfo>();

                foreach (DataRow r in dt.Rows)
                {
                    if (r["BillingAmount"] != DBNull.Value)
                        revenue += Convert.ToDecimal(r["BillingAmount"]);
                    if (r["ActualDurationMinutes"] != DBNull.Value)
                        hours += Convert.ToInt32(r["ActualDurationMinutes"]) / 60.0;

                    sessions.Add(new SessionInfo
                    {
                        SessionId        = Convert.ToInt32(r["SessionId"]),
                        UserId           = Convert.ToInt32(r["UserId"]),
                        Username         = r["Username"].ToString(),
                        FullName         = r["FullName"].ToString(),
                        ClientCode       = r["ClientCode"].ToString(),
                        MachineName      = r["MachineName"].ToString(),
                        StartTime        = Convert.ToDateTime(r["StartedAt"]),
                        SelectedDuration = r["SelectedDurationMinutes"] != DBNull.Value
                                           ? Convert.ToInt32(r["SelectedDurationMinutes"]) : 0,
                        SessionStatus    = r["Status"].ToString(),
                        CurrentBilling   = r["BillingAmount"] != DBNull.Value
                                           ? Convert.ToDecimal(r["BillingAmount"]) : 0m
                    });
                }

                return new ReportData
                {
                    TotalSessions = dt.Rows.Count,
                    TotalRevenue  = revenue,
                    TotalHours    = hours,
                    FromDate      = fromDate,
                    ToDate        = toDate,
                    Sessions      = sessions.ToArray()
                };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "ReportErr", ex.Message, "Error");
                return new ReportData();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  DUPLEX SUBSCRIPTIONS  (NFR-02 real-time updates)
        // ═══════════════════════════════════════════════════════════

        public void SubscribeForNotifications(string clientCode)
        {
            try
            {
                var cb = OperationContext.Current
                             .GetCallbackChannel<ISessionServiceCallback>();
                _subs[clientCode] = cb;
                _db.WriteSystemLog(null, null, null, null,
                    "System", "ClientSubscribed",
                    $"Client {clientCode} subscribed for notifications", "Server");
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "SubscribeErr", ex.Message, "Error");
            }
        }

        public void UnsubscribeFromNotifications(string clientCode)
        {
            _subs.TryRemove(clientCode, out _);
            _db.WriteSystemLog(null, null, null, null,
                "System", "ClientUnsubscribed",
                $"Client {clientCode} unsubscribed", "Server");
        }

        // ═══════════════════════════════════════════════════════════
        //  BACKGROUND: server-side auto-expiry  (FR-09 / NFR-03)
        // ═══════════════════════════════════════════════════════════

        private void ServerExpiryScan(object _)
        {
            try
            {
                int n = _db.AutoExpireOverdueSessions();
                if (n > 0)
                {
                    _db.WriteSystemLog(null, null, null, null,
                        "Session", "AutoExpiry",
                        $"{n} session(s) auto-expired by server", "Server");

                    Broadcast(cb => cb.OnServerMessage(
                        $"{n} session(s) auto-expired. Refresh dashboard."));
                }
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "AutoExpiryErr", ex.Message, "Error");
            }
        }
        // ═══════════════════════════════════════════════════════════
        //  ADD THIS METHOD BLOCK to SessionService.cs
        //  Place it in the "UC-15 / UC-18  —  REPORTS" region,
        //  directly before GetSessionReport().
        //
        //  This completes UC-15 (View Session Logs / History).
        //  SEQ-15: Admin sets date+category → GetSystemLogs →
        //          DB queries tblSystemLog → returns list → Admin displays.
        // ═══════════════════════════════════════════════════════════

        /*
            In SessionService.cs, inside the class body, add:
        */

        public SystemLogInfo[] GetSystemLogs(DateTime fromDate, DateTime toDate, string category)
        {
            try
            {
                // null category = "All" (no filter)
                string cat = (category == "All" || string.IsNullOrWhiteSpace(category))
                             ? null : category;

                DataTable dt = _db.GetSystemLogs(fromDate, toDate, cat);

                var list = new System.Collections.Generic.List<SystemLogInfo>();
                foreach (DataRow r in dt.Rows)
                {
                    list.Add(new SystemLogInfo
                    {
                        LogId = Convert.ToInt32(r["SystemLogId"]),
                        LoggedAt = Convert.ToDateTime(r["LogedAt"]),
                        Category = r["Category"].ToString(),
                        Type = r["Type"].ToString(),
                        Message = r["Message"].ToString(),
                        Source = r["Source"]?.ToString(),
                        SessionId = r["SessionId"] != DBNull.Value
                                     ? (int?)Convert.ToInt32(r["SessionId"]) : null,
                        Username = r["Username"]?.ToString(),
                        ClientCode = r["ClientCode"]?.ToString()
                    });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "GetSystemLogsErr", ex.Message, "Error");
                return Array.Empty<SystemLogInfo>();
            }
        }
        // ═══════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a SessionInfo from a DataRow that must contain at minimum:
        /// SessionId, UserId, StartedAt, SelectedDurationMinutes, Status.
        /// ExpectedEndAt may be a computed column or absent (we compute it here).
        /// </summary>
        private SessionInfo Map(DataRow r)
        {
            DateTime start = Convert.ToDateTime(r["StartedAt"]);
            int      sel   = Convert.ToInt32(r["SelectedDurationMinutes"]);

            DateTime expectedEnd;
            if (r.Table.Columns.Contains("ExpectedEndAt") && r["ExpectedEndAt"] != DBNull.Value)
                expectedEnd = Convert.ToDateTime(r["ExpectedEndAt"]);
            else
                expectedEnd = start.AddMinutes(sel);

            int remaining = Math.Max(0, (int)(expectedEnd - DateTime.Now).TotalMinutes);
            int elapsed   = sel - remaining;

            decimal rate    = r.Table.Columns.Contains("RatePerMinute")
                              && r["RatePerMinute"] != DBNull.Value
                              ? Convert.ToDecimal(r["RatePerMinute"])
                              : _db.GetCurrentBillingRate();

            return new SessionInfo
            {
                SessionId        = Convert.ToInt32(r["SessionId"]),
                UserId           = Convert.ToInt32(r["UserId"]),
                Username         = r.Table.Columns.Contains("Username")
                                   ? r["Username"]?.ToString() : "",
                FullName         = r.Table.Columns.Contains("FullName")
                                   ? r["FullName"]?.ToString() : "",
                ClientCode       = r.Table.Columns.Contains("ClientCode")
                                   ? r["ClientCode"]?.ToString() : "",
                MachineName      = r.Table.Columns.Contains("MachineName")
                                   ? r["MachineName"]?.ToString() : "",
                StartTime        = start,
                SelectedDuration = sel,
                ExpectedEndTime  = expectedEnd,
                SessionStatus    = r["Status"].ToString(),
                RemainingMinutes = remaining,
                CurrentBilling   = elapsed * rate
            };
        }

        private static string MapTerminationReason(string t)
        {
            switch (t)
            {
                case "Admin":
                    return "AdminTerminate";
                case "Auto":
                    return "AutoExpiry";
                case "Manual":
                    return "UserLogout";
                case "Crash":
                    return "Crash";
                default:
                    return "UserLogout";
            }
        }

        private static AuthenticationResponse Fail(string msg)
            => new AuthenticationResponse { IsAuthenticated = false, ErrorMessage = msg };

        /// <summary>Push to ONE subscriber; remove on dead channel.</summary>
        private static void Notify(string code, Action<ISessionServiceCallback> act)
        {
            if (code == null) return;
            if (_subs.TryGetValue(code, out var cb))
                try { act(cb); }
                catch { _subs.TryRemove(code, out _); }
        }

        /// <summary>Push to ALL subscribers; prune dead channels.</summary>
        private static void Broadcast(Action<ISessionServiceCallback> act)
        {
            foreach (var key in _subs.Keys.ToList())
                if (_subs.TryGetValue(key, out var cb))
                    try { act(cb); }
                    catch { _subs.TryRemove(key, out _); }
        }

        public void Dispose()
        {
            _expiryTimer?.Dispose();
            _db.WriteSystemLog(null, null, null, null,
                "System", "ServiceStop", "SessionService stopped", "Server");
        }
    }
}
