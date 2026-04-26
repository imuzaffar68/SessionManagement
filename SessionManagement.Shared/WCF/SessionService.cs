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

        // ── Pending terminations for clients with no live callback channel ──
        // Populated by EndSession() when the target client is not in _subs.
        // Drained by SubscribeForNotifications() the moment the client reconnects.
        private static readonly ConcurrentDictionary<string, (int SessionId, string Reason)>
            _pendingTerminations = new ConcurrentDictionary<string, (int, string)>(
                StringComparer.OrdinalIgnoreCase);

        // ── One-shot auth tokens: token → userId ─────────────────────
        // Stored on AuthenticateUser success, consumed atomically on StartSession.
        // Instance field is correct here — singleton service means one instance per host.
        private readonly ConcurrentDictionary<string, int> _tokenStore =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        // ── server-side session expiry (every 30 s) ───────────────
        private readonly Timer _sessionExpiryTimer;

        // ── server-side offline detection (every 60 s) ────────────
        private readonly Timer _clientOfflineTimer;

        // ── image storage path ────────────────────────────────────
        private readonly string _imgPath;

        // ─────────────────────────────────────────────────────────
        public SessionService()
        {
            _db = new DatabaseHelper();
            // Use %PROGRAMDATA% so the server can write images even when installed under
            // C:\Program Files (which requires elevation for writes to BaseDirectory).
            _imgPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SessionManagement", "Images");
            Directory.CreateDirectory(_imgPath);

            _sessionExpiryTimer = new Timer(ServerExpiryScan, null,
                TimeSpan.FromSeconds(ServiceConstants.ExpiryCheckIntervalSeconds),
                TimeSpan.FromSeconds(ServiceConstants.ExpiryCheckIntervalSeconds));

            _clientOfflineTimer = new Timer(OfflineDetectionScan, null,
                TimeSpan.FromSeconds(ServiceConstants.OfflineCheckIntervalSeconds),
                TimeSpan.FromSeconds(ServiceConstants.OfflineCheckIntervalSeconds));

            // After a server power-cut, all client machines have stale LastSeenAt
            // from before the crash.  Stamp them now so the first OfflineDetectionScan
            // (60 s from now) does not immediately kill every active client session.
            _db.RefreshLastSeenForActiveMachines();
            PurgeOldImages();

            int logDays = 180;
            string logCfg = ConfigurationManager.AppSettings["LogRetentionDays"];
            if (!string.IsNullOrEmpty(logCfg)) int.TryParse(logCfg, out logDays);
            _db.PurgeOldLogs(logDays);

            _db.WriteSystemLog(null, null, null, null,
                "System", "ServiceStart", "SessionService started", "Server");
        }

        // ─────────────────────────────────────────────────────────
        //  Image cleanup
        // ─────────────────────────────────────────────────────────

        private void PurgeOldImages()
        {
            try
            {
                int days = 90;
                string cfg = ConfigurationManager.AppSettings["ImageRetentionDays"];
                if (!string.IsNullOrEmpty(cfg)) int.TryParse(cfg, out days);
                if (days <= 0) return;   // 0 = disabled

                DateTime cutoff = DateTime.Now.AddDays(-days);
                int deleted = 0;

                foreach (string folder in new[] { _imgPath,
                    Path.Combine(Path.GetDirectoryName(_imgPath), "ProfilePics") })
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        if (File.GetCreationTime(file) < cutoff)
                        {
                            try { File.Delete(file); deleted++; }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[PurgeImages] Could not delete {file}: {ex.Message}");
                            }
                        }
                    }
                }

                if (deleted > 0)
                    _db.WriteSystemLog(null, null, null, null,
                        "System", "ImagePurge",
                        $"Purged {deleted} image file(s) older than {days} days", "Server");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PurgeImages] {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Input validation helper
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the string is non-null, non-whitespace, and within maxLen.
        /// Keeps every service method's guard a one-liner.
        /// </summary>
        private static bool IsValidString(string value, int maxLen = 255)
            => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLen;

        // ═══════════════════════════════════════════════════════════
        //  UC-01 / UC-09  —  AUTHENTICATION
        //  SEQ-01: Client→Server→DB→Server→Client
        // ═══════════════════════════════════════════════════════════

        public AuthenticationResponse AuthenticateUser(
            string username, string password, string clientCode)
        {
            if (!IsValidString(username) || !IsValidString(password) || !IsValidString(clientCode, 50))
                return new AuthenticationResponse
                { IsAuthenticated = false, ErrorMessage = "Invalid input." };

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

                // Check if machine is active (blocked machines cannot start sessions)
                if (!_db.IsClientMachineActive(machineId) && !isAdminLogin && machineId != 0)
                    return Fail("This machine is not available for use. Please contact your administrator.");

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

                string picPath = user["ProfilePicturePath"]?.ToString();

                string token = AuthenticationHelper.GenerateSessionToken();
                _tokenStore[token] = userId;   // overwrite if same user re-authenticates before starting a session

                return new AuthenticationResponse
                {
                    IsAuthenticated      = true,
                    UserId               = userId,
                    Username             = user["Username"].ToString(),
                    FullName             = user["FullName"]?.ToString() ?? "",
                    UserType             = user["Role"].ToString(),
                    SessionToken         = token,
                    ProfilePictureBase64 = LoadProfilePicture(picPath)
                };
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, isAdminLogin ? (int?)null : machineId, null,
                    "Auth", ExceptionCategory(ex), $"AuthenticateUser: {ex.Message}", "Server");
                return Fail("Authentication service error. Please try again.");
            }
        }

        public bool ValidateSession(string sessionToken)
            => !string.IsNullOrWhiteSpace(sessionToken)
               && _tokenStore.ContainsKey(sessionToken);

        // ═══════════════════════════════════════════════════════════
        //  UC-02  —  START SESSION
        //  SEQ-02: confirm duration → create session → notify admin → log
        // ═══════════════════════════════════════════════════════════

        public SessionStartResponse StartSession(
            int userId, string clientCode, int durationMinutes, string sessionToken)
        {
            if (userId <= 0 || !IsValidString(clientCode, 50) || durationMinutes <= 0 || durationMinutes > 1440)
                return new SessionStartResponse
                { Success = false, ErrorMessage = "Invalid input." };

            // Atomically validate + consume token — TryRemove prevents replay attacks
            if (!_tokenStore.TryRemove(sessionToken ?? string.Empty, out int tokenUserId)
                || tokenUserId != userId)
                return new SessionStartResponse
                { Success = false, ErrorMessage = "SESSION_TOKEN_EXPIRED" };

            try
            {
                int machineId = _db.GetClientMachineIdByCode(clientCode);
                if (machineId == 0)
                {
                    _db.WriteSystemLog(null, userId, null, null,
                        "Session", "SessionStartFailed",
                        $"StartSession rejected: machine '{clientCode}' not registered", "Server");
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "Client machine not registered." };
                }

                // Check if machine is active (blocked machines cannot start sessions)
                if (!_db.IsClientMachineActive(machineId))
                {
                    _db.WriteSystemLog(null, userId, machineId, null,
                        "Session", "SessionStartFailed",
                        $"StartSession rejected: machine '{clientCode}' is disabled", "Server");
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "This machine is not available for use. Please contact your administrator." };
                }

                // SEQ-02 step 2: INSERT tblSession via sp_StartSession.
                // SP returns: positive = new SessionId, -1 = user conflict, -2 = machine conflict, 0 = error.
                int sessionId = _db.StartSession(userId, machineId, durationMinutes);
                if (sessionId == -1)
                {
                    _db.WriteSystemLog(null, userId, machineId, null,
                        "Session", "SessionStartFailed",
                        $"StartSession rejected: user {userId} already has an active session", "Server");
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "You already have an active session on another machine." };
                }
                if (sessionId == -2)
                {
                    _db.WriteSystemLog(null, userId, machineId, null,
                        "Session", "SessionStartFailed",
                        $"StartSession rejected: machine '{clientCode}' already has an active session", "Server");
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "This machine already has an active session." };
                }
                if (sessionId <= 0)
                {
                    _db.WriteSystemLog(null, userId, machineId, null,
                        "Session", "SessionStartFailed",
                        $"StartSession failed: sp_StartSession returned {sessionId} for machine '{clientCode}'", "Server");
                    return new SessionStartResponse
                    { Success = false, ErrorMessage = "Failed to create session record." };
                }

                // SEQ-02 step 3: mark machine Active
                _db.UpdateClientMachineStatus(machineId, "Active");

                DateTime start = DateTime.Now;
                DateTime end   = start.AddMinutes(durationMinutes);

                // SEQ-02 step 4: Session log //commented due to duplicate log in procedure
                //_db.WriteSystemLog(sessionId, userId, machineId, null,
                //    "Session", "SessionStarted",
                //    $"Session {sessionId} started — {durationMinutes} min on {clientCode}",
                //    "Server");

                // SEQ-02 step 5: push to all subscribed admins (FR-06 real-time monitor)
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    Broadcast(cb => cb.OnServerMessage(
                        $"SESSION_STARTED:{clientCode} — {durationMinutes} min (Session #{sessionId})")));

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
                _db.LogSystemEvent(null, userId, null, ExceptionCategory(ex),
                    $"StartSession: {ex.Message}", "Error");
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
            if (sessionId <= 0 || !IsValidString(terminationType, 50))
                return false;

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

                // step 4: Session log written by sp_EndSession (atomic with the transaction)

                // SEQ-14 step 5: push termination to the client machine (WCF callback).
                // If the client has no live subscription (e.g. mid-reconnect after a server
                // restart), store the event so SubscribeForNotifications() replays it the
                // instant the client re-subscribes — guaranteeing zero missed terminations.
                if (_subs.ContainsKey(code))
                {
                    int capturedSid    = sessionId;
                    string capturedRsn = reason;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                        Notify(code, cb => cb.OnSessionTerminated(capturedSid, capturedRsn)));
                }
                else
                {
                    _pendingTerminations[code] = (sessionId, reason);
                }

                // step 6: notify all admins so dashboard refreshes in real-time
                string username = row["Username"]?.ToString() ?? "?";
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    Broadcast(cb => cb.OnServerMessage(
                        $"SESSION_ENDED:{code} [{username}] — {reason} (Session #{sessionId})")));

                return true;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(sessionId, null, null, ExceptionCategory(ex),
                    $"EndSession: {ex.Message}", "Error");
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
                DataTable dt = _db.GetActiveSessions();
                var list     = new List<SessionInfo>();
                foreach (DataRow r in dt.Rows)
                    list.Add(Map(r));
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

        /// <summary>
        /// Registers a new machine or refreshes its network identity on reconnect.
        /// <para>
        /// clientCode is auto-derived by the client as "CL-" + MAC address
        /// (ResolveClientCode() in SessionClient\MainWindow.xaml.cs).
        /// machineName and location come from App.config keys ClientMachineName /
        /// ClientLocation, which are written by the Inno Setup installer wizard.
        /// On subsequent reconnects the values are re-sent, but the SP only writes
        /// them on first INSERT — admin renames via UpdateClientMachineInfo persist.
        /// </para>
        /// </summary>
        public bool RegisterClient(string clientCode, string machineName,
            string ipAddress, string macAddress, string location)
        {
            if (!IsValidString(clientCode, 50) || !IsValidString(machineName))
                return false;

            try
            {
                // Check before upsert so we can broadcast only on genuine first-time registration
                bool isNew = _db.GetClientMachineIdByCode(clientCode) == 0;

                int id = _db.RegisterOrUpdateClient(clientCode, machineName,
                    ipAddress, macAddress, location);
                // Log written by sp_RegisterClient (atomic with the transaction)

                // Notify all connected admins only when a brand-new machine appears
                if (isNew && id > 0)
                    Broadcast(cb => cb.OnServerMessage(
                        $"CLIENT_REGISTERED:{clientCode} ({machineName})"));

                return id > 0;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "ClientRegisterErr", ex.Message, "Error");
                return false;
            }
        }

        /// <summary>
        /// Admin-only: rename a machine or change its physical location from
        /// SessionAdmin without touching the client PC.
        /// Updates MachineName and Location in tblClientMachine — the only path
        /// that may do so after first registration (sp_RegisterClient UPDATE path
        /// deliberately skips these columns so this change survives client reboots).
        /// </summary>
        public bool UpdateClientMachineInfo(string clientCode, string machineName, string location)
        {
            if (!IsValidString(clientCode, 50) || !IsValidString(machineName))
                return false;

            try
            {
                return _db.UpdateClientMachineInfo(clientCode, machineName, location);
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "UpdateClientMachineInfoErr", ex.Message, "Error");
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

        public bool UpdateClientMachineIsActive(string clientCode, bool isActive)
        {
            try
            {
                int id = _db.GetClientMachineIdByCode(clientCode);
                if (id == 0) return false;
                bool ok = _db.UpdateClientMachineIsActive(id, isActive);
                if (ok)
                    _db.WriteSystemLog(null, null, id, null,
                        "System", isActive ? "MachineEnabled" : "MachineDisabled",
                        $"Machine '{clientCode}' {(isActive ? "enabled" : "disabled")} by admin",
                        "Server");
                return ok;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "UpdateClientIsActiveErr", ex.Message, "Error");
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
                        ClientId         = Convert.ToInt32(r["ClientMachineId"]),
                        ClientCode       = r["ClientCode"].ToString(),
                        MachineName      = r["MachineName"].ToString(),
                        IpAddress        = r["IPAddress"].ToString(),
                        MacAddress       = r["MACAddress"]?.ToString(),
                        Location         = r["Location"]?.ToString(),
                        IsActive         = (bool)r["IsActive"],
                        Status           = r["Status"].ToString(),
                        LastActiveTime   = r["LastSeenAt"] != DBNull.Value
                                           ? Convert.ToDateTime(r["LastSeenAt"])
                                           : (DateTime?)null,
                        CurrentUser      = r["CurrentUsername"]?.ToString(),
                        MissedHeartbeats = r["MissedHeartbeats"] != DBNull.Value
                                           ? Convert.ToInt32(r["MissedHeartbeats"])
                                           : 0
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
            if (!IsValidString(alertType, 100) || !IsValidString(description) || !IsValidString(severity, 20))
                return false;

            try
            {
                // SEQ-16 step 2: look up machine for complete alert row
                var   row       = _db.GetSessionById(sessionId);
                int?  machineId = row != null
                                  ? (int?)Convert.ToInt32(row["ClientMachineId"])
                                  : null;
                string code     = row?["ClientCode"]?.ToString();

                // SEQ-16 step 3: sp_LogSecurityAlert (writes tblAlert + tblSystemLog)
                int alertId = _db.InsertSecurityAlert(alertType,
                    sessionId == 0 ? (int?)null : sessionId,
                    machineId, userId, description, severity);

                if (alertId > 0)
                {
                    // Mark notified BEFORE the async broadcast so the DB is always
                    // consistent even if the thread pool task is delayed.
                    _db.MarkAlertNotifiedToAdmin(alertId);

                    // SEQ-17 step 1: real-time push to all subscribed admins (FR-14).
                    // MUST be async (thread pool) — NOT synchronous.
                    // LogSecurityAlert is invoked on the client's UI thread.  A
                    // synchronous Broadcast here acquires the admin channel's TCP send
                    // lock, which is also held by the admin's data-refresh WCF calls.
                    // That lock contention blocks the server from returning this call,
                    // which blocks the client UI thread, which starves the heartbeat
                    // DispatcherTimer — causing false-positive offline detection.
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                        Broadcast(cb => cb.OnServerMessage(
                            $"[{severity}] ALERT from {code ?? "??"}: {alertType} — {description}")));
                }
                return alertId > 0;
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
            catch { return 0m; }
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
                        SelectedDuration = r["ActualDurationMinutes"] != DBNull.Value
                                           ? Convert.ToInt32(r["ActualDurationMinutes"]) : 0,
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
                bool isNewSubscription = !_subs.ContainsKey(clientCode);
                _subs[clientCode] = cb;

                // RC-4: replay any termination that fired while this client had no
                // subscription channel (e.g. admin terminated during server-restart window).
                if (_pendingTerminations.TryRemove(clientCode, out var pending))
                {
                    var replayCb     = cb;
                    int replaySid    = pending.SessionId;
                    string replayRsn = pending.Reason;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { replayCb.OnSessionTerminated(replaySid, replayRsn); }
                        catch (Exception) { /* best-effort — client also validates via GetSessionInfo */ }
                    });

                    _db.WriteSystemLog(null, null, null, null,
                        "System", "TerminationReplayed",
                        $"Replayed OnSessionTerminated (session {replaySid}) to {clientCode} on reconnect",
                        "Server");
                }

                if (isNewSubscription)
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
        //  HEARTBEAT  (FR-HB: liveness signalling)
        // ═══════════════════════════════════════════════════════════

        public void Heartbeat(string clientCode)
        {
            try
            {
                // Read status BEFORE updating — used for recovery detection below.
                string previousStatus = _db.GetClientStatus(clientCode);

                // Touch LastSeenAt and reset MissedHeartbeats counter.
                _db.UpdateClientLastSeen(clientCode);

                // Recovery path: if this heartbeat arrives after the machine was
                // prematurely marked Offline (grace counter exhausted before the
                // delayed heartbeat got through), restore its status and notify admins.
                if (string.Equals(previousStatus, "Offline", StringComparison.OrdinalIgnoreCase))
                {
                    int machineId = _db.GetClientMachineIdByCode(clientCode);
                    if (machineId != 0)
                    {
                        // RC-3: restore to Active if the machine still has a running session
                        // (client kept its timer going during the outage), otherwise Idle.
                        // Unconditionally using "Idle" was wrong — it showed the machine as
                        // available for new sessions while the old session was still live.
                        var activeSessions = _db.GetActiveSessionsByMachine(machineId);
                        string recoveryStatus = activeSessions.Rows.Count > 0 ? "Active" : "Idle";
                        _db.UpdateClientMachineStatus(machineId, recoveryStatus);
                    }

                    _db.WriteSystemLog(null, null,
                        machineId != 0 ? (int?)machineId : null, null,
                        "System", "MachineOnline",
                        $"Machine {clientCode} came back online — late heartbeat received after offline marking",
                        "Server");

                    ThreadPool.QueueUserWorkItem(_ =>
                        Broadcast(cb => cb.OnServerMessage(
                            $"MACHINE_ONLINE:{clientCode} — Machine is back online")));
                }
            }
            catch { /* best-effort; must not throw on the client */ }
        }

        // ═══════════════════════════════════════════════════════════
        //  ORPHAN SESSION MANAGEMENT
        //  Called at client startup (before RegisterClient) so that
        //  LastSeenAt in the DB still holds the pre-crash heartbeat time.
        // ═══════════════════════════════════════════════════════════

        public int TerminateOrphanSession(string clientCode)
        {
            try
            {
                // LastSeenAt is read NOW, before the client calls RegisterClient()
                // which would overwrite it with the restart time.
                // This gives us the best available proxy for when the machine crashed.
                DateTime? lastSeen = _db.GetClientLastSeen(clientCode);

                int count = _db.TerminateOrphanSessionsForMachine(clientCode, lastSeen);

                if (count > 0)
                {
                    _db.WriteSystemLog(null, null, null, null,
                        "Session", "OrphanTerminated",
                        $"{count} orphan session(s) terminated for {clientCode} " +
                        $"(effective end: {lastSeen?.ToString("HH:mm:ss") ?? "now"})",
                        "Server");

                    ThreadPool.QueueUserWorkItem(_ =>
                        Broadcast(cb => cb.OnServerMessage(
                            $"Orphan cleanup: {count} session(s) terminated on {clientCode}. Refresh dashboard.")));
                }

                return count;
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "OrphanTerminateErr", ex.Message, "Error");
                return 0;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  BACKGROUND: server-side offline detection (every 60 s)
        //  Any machine with LastSeenAt < NOW-90s that is not 'Offline'
        //  is marked Offline and its active session is auto-terminated.
        // ═══════════════════════════════════════════════════════════

        private void OfflineDetectionScan(object state)
        {
            try
            {
                System.Data.DataTable stale = _db.MarkStaleClientsOffline(thresholdSeconds: ServiceConstants.OfflineThresholdSeconds);
                if (stale.Rows.Count == 0) return;

                foreach (System.Data.DataRow r in stale.Rows)
                {
                    int    machineId  = Convert.ToInt32(r["ClientMachineId"]);
                    string clientCode = r["ClientCode"].ToString();

                    // remove dead WCF callback channel if still registered
                    ISessionServiceCallback removed;
                    _subs.TryRemove(clientCode, out removed);

                    // auto-terminate any active session on this machine
                    System.Data.DataTable activeSessions = _db.GetActiveSessionsByMachine(machineId);
                    foreach (System.Data.DataRow sr in activeSessions.Rows)
                    {
                        int sessionId = Convert.ToInt32(sr["SessionId"]);
                        _db.EndSession(sessionId, "Crash");
                        _db.WriteSystemLog(sessionId, null, machineId, null,
                            "Session", "CrashTermination",
                            $"Session {sessionId} auto-terminated: machine {clientCode} went offline",
                            "Server");
                    }

                    _db.WriteSystemLog(null, null, machineId, null,
                        "System", "MachineOffline",
                        $"Machine {clientCode} marked Offline — missed heartbeat", "Server");
                }

                ThreadPool.QueueUserWorkItem(__ =>
                    Broadcast(cb => cb.OnServerMessage(
                        $"{stale.Rows.Count} machine(s) went offline. Refresh client list.")));
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "OfflineDetectionErr", ex.Message, "Error");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  BACKGROUND: server-side auto-expiry  (FR-09 / NFR-03)
        // ═══════════════════════════════════════════════════════════

        private void ServerExpiryScan(object _)
        {
            try
            {
                var expiredIds = _db.AutoExpireOverdueSessionsWithIds();
                if (expiredIds.Count > 0)
                {
                    foreach (var sessionId in expiredIds)
                    {
                        var row = _db.GetSessionById(sessionId);
                        int? userId = row != null ? (int?)Convert.ToInt32(row["UserId"]) : null;
                        int? machineId = row != null ? (int?)Convert.ToInt32(row["ClientMachineId"]) : null;
                        _db.WriteSystemLog(sessionId, userId, machineId, null,
                            "Session", "AutoExpiry",
                            $"Session {sessionId} auto-expired by server", "Server");
                    }
                    System.Threading.ThreadPool.QueueUserWorkItem(__ =>
                    Broadcast(cb => cb.OnServerMessage(
                        $"{expiredIds.Count} session(s) auto-expired. Refresh dashboard.")));
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
        //  UC-03  —  USER REGISTRATION (ADMIN)
        //  SEQ-03: Admin enters user details + password → validate → 
        //          hash password → insert user record → log
        // ═══════════════════════════════════════════════════════════

        public UserRegistrationResponse RegisterClientUser(
            string username, string fullName, string password,
            string phone, string address, int adminUserId,
            string profilePictureBase64)
        {
            try
            {
                // SEQ-03 step 1: validate inputs
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    return new UserRegistrationResponse
                    {
                        Success = false,
                        ErrorMessage = "Username and password are required."
                    };
                }

                if (string.IsNullOrWhiteSpace(fullName))
                {
                    return new UserRegistrationResponse
                    {
                        Success = false,
                        ErrorMessage = "Full name is required."
                    };
                }

                if (username.Length < 3 || username.Length > 50)
                {
                    return new UserRegistrationResponse
                    {
                        Success = false,
                        ErrorMessage = "Username must be between 3 and 50 characters."
                    };
                }

                if (password.Length < 6)
                {
                    return new UserRegistrationResponse
                    {
                        Success = false,
                        ErrorMessage = "Password must be at least 6 characters."
                    };
                }

                // SEQ-03 step 2: check for duplicate username
                DataRow existing = _db.GetUserByUsername(username);
                if (existing != null)
                {
                    return new UserRegistrationResponse
                    {
                        Success = false,
                        ErrorMessage = "Username already exists. Please choose a different username."
                    };
                }

                // SEQ-03 step 3: hash password using BCrypt
                string passwordHash = AuthenticationHelper.HashPassword(password);

                // SEQ-03 step 4: insert user into database
                int userId = _db.RegisterClientUser(username, fullName, passwordHash,
                    phone, address, adminUserId);

                if (userId <= 0)
                {
                    return new UserRegistrationResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to register user. Please try again."
                    };
                }

                // Save profile picture if provided, then store path via the same SP used for updates
                if (!string.IsNullOrEmpty(profilePictureBase64))
                {
                    string picPath = SaveProfilePicture(userId, profilePictureBase64);
                    if (picPath != null) _db.UpdateClientUser(userId, fullName, phone, address, picPath);
                }

                // SEQ-03 step 5: log the registration
                _db.WriteSystemLog(null, userId, null, adminUserId,
                    "Auth", "UserRegistered",
                    $"New ClientUser '{username}' registered by admin {adminUserId}", "Server");

                return new UserRegistrationResponse
                {
                    Success = true,
                    UserId = userId,
                    Username = username
                };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "UserRegisterErr", ex.Message, "Error");
                return new UserRegistrationResponse
                {
                    Success = false,
                    ErrorMessage = "User registration failed. Please try again."
                };
            }
        }

        public UserInfo[] GetAllClientUsers()
        {
            try
            {
                DataTable dt = _db.GetAllClientUsers();
                var list = new List<UserInfo>();
                foreach (DataRow r in dt.Rows)
                {
                    string picPath = r["ProfilePicturePath"]?.ToString();
                    list.Add(new UserInfo
                    {
                        UserId               = Convert.ToInt32(r["UserId"]),
                        Username             = r["Username"].ToString(),
                        FullName             = r["FullName"]?.ToString() ?? "",
                        Phone                = r["Phone"]?.ToString() ?? "",
                        Address              = r["Address"]?.ToString() ?? "",
                        Status               = r["Status"].ToString(),
                        Role                 = r["Role"].ToString(),
                        CreatedAt            = Convert.ToDateTime(r["CreatedAt"]),
                        LastLoginAt          = r["LastLoginAt"] != DBNull.Value
                                              ? (DateTime?)Convert.ToDateTime(r["LastLoginAt"]) : null,
                        ProfilePictureBase64 = LoadProfilePicture(picPath)
                    });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "GetUsersErr", ex.Message, "Error");
                return Array.Empty<UserInfo>();
            }
        }

        public UserUpdateResponse UpdateClientUser(int userId, string fullName,
            string phone, string address, int adminUserId, string profilePictureBase64)
        {
            try
            {
                // Save picture first to get the path, then pass it in the single DB call.
                // SP uses CASE WHEN so null path = keep existing.
                string picPath = null;
                if (!string.IsNullOrEmpty(profilePictureBase64))
                    picPath = SaveProfilePicture(userId, profilePictureBase64);

                bool ok = _db.UpdateClientUser(userId, fullName, phone, address, picPath);
                if (!ok)
                {
                    return new UserUpdateResponse
                    {
                        Success = false,
                        UserId = userId,
                        ErrorMessage = "Failed to update user. User may not exist."
                    };
                }

                _db.WriteSystemLog(null, userId, null, adminUserId,
                    "Auth", "UserUpdated",
                    $"ClientUser {userId} updated by admin {adminUserId}", "Server");

                return new UserUpdateResponse { Success = true, UserId = userId };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "UserUpdateErr", ex.Message, "Error");
                return new UserUpdateResponse
                {
                    Success = false,
                    UserId = userId,
                    ErrorMessage = "User update failed. Please try again."
                };
            }
        }

        public UserDeleteResponse DeleteClientUser(int userId, int adminUserId)
        {
            try
            {
                int result = _db.DeleteClientUser(userId);
                if (result == -1)
                    return new UserDeleteResponse
                    {
                        Success = false,
                        UserId = userId,
                        ErrorMessage = "Cannot delete — this user has session history. Disable the account instead."
                    };
                if (result <= 0)
                    return new UserDeleteResponse
                    {
                        Success = false,
                        UserId = userId,
                        ErrorMessage = "User not found or could not be deleted."
                    };

                _db.WriteSystemLog(null, userId, null, adminUserId,
                    "Auth", "UserDeleted",
                    $"ClientUser {userId} deleted by admin {adminUserId}", "Server");

                return new UserDeleteResponse { Success = true, UserId = userId };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "UserDeleteErr", ex.Message, "Error");
                return new UserDeleteResponse
                {
                    Success = false,
                    UserId = userId,
                    ErrorMessage = "Delete failed. Please try again."
                };
            }
        }

        private string SaveProfilePicture(int userId, string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SessionManagement", "ProfilePics");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, $"{userId}.jpg");
                System.IO.File.WriteAllBytes(path, Convert.FromBase64String(base64));
                return path;
            }
            catch { return null; }
        }

        private string LoadProfilePicture(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            try { return Convert.ToBase64String(System.IO.File.ReadAllBytes(path)); }
            catch { return null; }
        }

        public BillingRecordInfo[] GetBillingRecords(bool unpaidOnly)
        {
            try
            {
                DataTable dt = _db.GetBillingRecords(unpaidOnly);
                var list = new List<BillingRecordInfo>();
                foreach (DataRow r in dt.Rows)
                {
                    list.Add(new BillingRecordInfo
                    {
                        BillingRecordId   = Convert.ToInt32(r["BillingRecordId"]),
                        SessionId         = Convert.ToInt32(r["SessionId"]),
                        Username          = r["Username"]?.ToString() ?? "",
                        FullName          = r["FullName"]?.ToString() ?? "",
                        MachineCode       = r["MachineCode"]?.ToString() ?? "",
                        BillableMinutes   = Convert.ToInt32(r["BillableMinutes"]),
                        Amount            = Convert.ToDecimal(r["Amount"]),
                        Currency          = r["Currency"]?.ToString() ?? "",
                        CalculatedAt      = Convert.ToDateTime(r["CalculatedAt"]),
                        IsPaid            = Convert.ToBoolean(r["IsPaid"]),
                        PaidAt            = r["PaidAt"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["PaidAt"]) : null,
                        ReceivedByAdminId = r["ReceivedByAdminId"] != DBNull.Value ? (int?)Convert.ToInt32(r["ReceivedByAdminId"]) : null
                    });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "GetBillingRecordsErr", ex.Message, "Error");
                return Array.Empty<BillingRecordInfo>();
            }
        }

        public bool MarkBillingRecordPaid(int billingRecordId, int adminUserId)
        {
            int result = _db.MarkBillingRecordPaid(billingRecordId, adminUserId);
            return result == 1;
        }

        public PasswordResetResponse ResetClientUserPassword(int userId,
            string newPassword, int adminUserId)
        {
            try
            {
                // Hash the new password
                string passwordHash = AuthenticationHelper.HashPassword(newPassword);

                bool ok = _db.ResetUserPassword(userId, passwordHash);
                if (!ok)
                {
                    return new PasswordResetResponse
                    {
                        Success = false,
                        UserId = userId,
                        ErrorMessage = "Failed to reset password. User may not exist."
                    };
                }

                _db.WriteSystemLog(null, userId, null, adminUserId,
                    "Auth", "PasswordReset",
                    $"ClientUser {userId} password reset by admin {adminUserId}", "Server");

                return new PasswordResetResponse
                {
                    Success = true,
                    UserId = userId
                };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "PasswordResetErr", ex.Message, "Error");
                return new PasswordResetResponse
                {
                    Success = false,
                    UserId = userId,
                    ErrorMessage = "Password reset failed. Please try again."
                };
            }
        }

        public UserStatusToggleResponse ToggleUserStatus(int userId, int adminUserId)
        {
            try
            {
                // Get current user status
                DataRow user = _db.GetUserById(userId);
                if (user == null)
                {
                    return new UserStatusToggleResponse
                    {
                        Success = false,
                        UserId = userId,
                        ErrorMessage = "User not found."
                    };
                }

                string currentStatus = user["Status"].ToString();
                string newStatus = currentStatus == "Active" ? "Disabled" : "Active";

                bool ok = _db.UpdateUserStatus(userId, newStatus);
                if (!ok)
                {
                    return new UserStatusToggleResponse
                    {
                        Success = false,
                        UserId = userId,
                        ErrorMessage = "Failed to update user status."
                    };
                }

                _db.WriteSystemLog(null, userId, null, adminUserId,
                    "Auth", "UserStatusChanged",
                    $"ClientUser {userId} status changed to {newStatus} by admin {adminUserId}", "Server");

                return new UserStatusToggleResponse
                {
                    Success = true,
                    UserId = userId,
                    NewStatus = newStatus
                };
            }
            catch (Exception ex)
            {
                _db.LogSystemEvent(null, null, null, "StatusToggleErr", ex.Message, "Error");
                return new UserStatusToggleResponse
                {
                    Success = false,
                    UserId = userId,
                    ErrorMessage = "Status update failed. Please try again."
                };
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
                CurrentBilling   = elapsed * rate,
                ImagePath        = r.Table.Columns.Contains("ImagePath") && r["ImagePath"] != DBNull.Value
                                   ? r["ImagePath"].ToString() : null
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

        /// <summary>Push to ONE subscriber; remove and mark Offline on dead channel.</summary>
        private void Notify(string code, Action<ISessionServiceCallback> act)
        {
            if (code == null) return;
            if (_subs.TryGetValue(code, out var cb))
                try { act(cb); }
                catch (Exception)
                {
                    // RC-5: catch all WCF exceptions (CommunicationException, TimeoutException,
                    // ObjectDisposedException) — not just CommunicationException — to prevent
                    // unhandled exceptions on the ThreadPool from terminating the host process.
                    _subs.TryRemove(code, out _);
                    try { _db.UpdateClientMachineStatus(_db.GetClientMachineIdByCode(code), "Offline"); }
                    catch { /* best-effort */ }
                }
        }

        /// <summary>
        /// Push to admin subscribers only (keys starting with "ADMIN").
        /// Client machines are always notified via Notify(code, ...) — never via Broadcast.
        /// Keeping Broadcast admin-only prevents a deadlock where a client's duplex channel
        /// is blocked waiting for a service-call reply while the server simultaneously tries
        /// to push a callback on that same channel.
        /// </summary>
        private void Broadcast(Action<ISessionServiceCallback> act)
        {
            foreach (var key in _subs.Keys
                                     .Where(k => k.StartsWith("ADMIN", StringComparison.OrdinalIgnoreCase))
                                     .ToList())
                if (_subs.TryGetValue(key, out var cb))
                    try { act(cb); }
                    catch (Exception)
                    {
                        // RC-5: catch all exceptions so a dead admin channel never crashes the host.
                        _subs.TryRemove(key, out _);
                    }
        }

        public void Dispose()
        {
            _sessionExpiryTimer?.Dispose();
            _clientOfflineTimer?.Dispose();
            _db.WriteSystemLog(null, null, null, null,
                "System", "ServiceStop", "SessionService stopped", "Server");
        }

        // ═══════════════════════════════════════════════════════════
        //  BILLING RATE MANAGEMENT
        // ═══════════════════════════════════════════════════════════

        public BillingRateInfo[] GetAllBillingRates()
        {
            try
            {
                return _db.GetAllBillingRates();
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, null, null,
                    "System", "Error", $"GetAllBillingRates: {ex.Message}", "Server");
                return Array.Empty<BillingRateInfo>();
            }
        }

        public int InsertBillingRate(string name, decimal ratePerMinute, string currency,
            DateTime? effectiveFrom, DateTime? effectiveTo, bool isDefault, int adminUserId, string notes)
        {
            try
            {
                // Duplicate name (-2) and date-range overlap (-3) are checked inside sp_InsertBillingRate.
                // SP sets @NewBillingRateId to -2 or -3 and returns early; CATCH returns -1 for unexpected errors.
                return _db.InsertBillingRate(name, ratePerMinute, currency,
                    effectiveFrom, effectiveTo, isDefault, adminUserId, notes);
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, null, adminUserId,
                    "Billing", "Error", $"InsertBillingRate: {ex.Message}", "Server");
                return -1;
            }
        }

        public bool UpdateBillingRate(int billingRateId, string name, decimal ratePerMinute,
            string currency, DateTime? effectiveFrom, DateTime? effectiveTo, bool isActive, bool isDefault, string notes)
        {
            try
            {
                // Duplicate name and date-range overlap are checked inside sp_UpdateBillingRate (SELECT 0; RETURN).
                return _db.UpdateBillingRate(billingRateId, name, ratePerMinute,
                    currency, effectiveFrom, effectiveTo, isActive, isDefault, notes);
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, null, null,
                    "Billing", "Error", $"UpdateBillingRate: {ex.Message}", "Server");
                return false;
            }
        }

        public bool DeleteBillingRate(int billingRateId)
        {
            try
            {
                return _db.DeleteBillingRate(billingRateId);
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, null, null,
                    "Billing", "Error", $"DeleteBillingRate: {ex.Message}", "Server");
                return false;
            }
        }

        public bool SetDefaultBillingRate(int billingRateId)
        {
            try
            {
                return _db.SetDefaultBillingRate(billingRateId);
            }
            catch (Exception ex)
            {
                _db.WriteSystemLog(null, null, null, null,
                    "Billing", "Error", $"SetDefaultBillingRate: {ex.Message}", "Server");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Exception categorization helper  (H-6)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Classifies an exception so log records distinguish infrastructure
        /// failures (DB down, network) from code bugs (null ref, argument).
        /// Use the returned tag as the Type field in WriteSystemLog calls.
        ///
        ///   SqlException        → "DBError"       (infra — DB/SP failure)
        ///   TimeoutException    → "Timeout"        (infra — slow query / network)
        ///   NullReferenceEx     → "CodeBug"        (bug — unexpected null)
        ///   ArgumentEx          → "CodeBug"        (bug — bad internal call)
        ///   anything else       → "ServiceError"   (unknown — investigate)
        ///
        /// Pattern: replace the generic catch body with
        ///   string tag = ExceptionCategory(ex);
        ///   _db.WriteSystemLog(..., "System", tag, $"Method: {ex.Message}", "Server");
        /// </summary>
        private static string ExceptionCategory(Exception ex)
        {
            if (ex is System.Data.SqlClient.SqlException)    return "DBError";
            if (ex is TimeoutException)                      return "Timeout";
            if (ex is NullReferenceException
             || ex is ArgumentException
             || ex is InvalidOperationException)             return "CodeBug";
            return "ServiceError";
        }
    }
}
