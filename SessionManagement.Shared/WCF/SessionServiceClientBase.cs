using System;
using System.Configuration;
using System.ServiceModel;
using System.ServiceModel.Description;

namespace SessionManagement.Client
{
    /// <summary>
    /// Single source-of-truth for the WCF duplex proxy.
    /// Contains all connection management and every service operation shared between
    /// SessionClient and SessionAdmin.  Admin-only operations live in
    /// SessionAdmin/SessionServiceClient.cs which inherits this class.
    ///
    /// Previously each project had its own full copy (~430 / ~550 lines) that had
    /// already diverged (missing callback loop in Admin).  Consolidating here means
    /// any fix or new operation is written once.
    /// </summary>
    public class SessionServiceClientBase : IDisposable
    {
        private   DuplexChannelFactory<WCF.ISessionService> _factory;
        protected WCF.ISessionService                        _proxy;
        private   SessionCallbackHandler                     _handler;
        private   bool                                       _connected;

        // Serialises Connect() / EnsureConnection() so concurrent background calls
        // (poll timer + heartbeat + login) never race on the proxy/factory fields.
        private readonly object _connectLock = new object();

        // Tracks last failed connect attempt so we don't hammer the TCP stack
        // (and block the UI thread) more than once per cooldown window.
        private DateTime _lastConnectAttempt = DateTime.MinValue;
        private const int ConnectCooldownSeconds = 10;

        // Maximum time to wait for a channel Open() before giving up.
        // Default WCF value is 1 minute; 5 s is enough for a LAN server.
        private const int ConnectTimeoutSeconds = 5;

        // ── Server-push events ────────────────────────────────────
        public event EventHandler<SessionTerminatedEventArgs> SessionTerminated;
        public event EventHandler<TimeWarningEventArgs>        TimeWarning;
        public event EventHandler<ServerMessageEventArgs>      ServerMessage;

        public bool IsConnected => _connected;

        /// <summary>
        /// One-shot auth token received from AuthenticateUser.
        /// Set by the caller immediately after successful login.
        /// Passed transparently to StartSession and cleared on logout/session-end.
        /// </summary>
        public string SessionToken { get; set; }

        /// <summary>
        /// True only when the underlying WCF channel is currently Open.
        /// Does NOT attempt reconnection — safe to call from the UI thread
        /// without any risk of blocking on a TCP timeout.
        /// Use this instead of IsConnected wherever a reconnect must not be triggered.
        /// </summary>
        public bool IsChannelReady
            => _connected && (_proxy as IClientChannel)?.State == CommunicationState.Opened;


        #region Connection management
        public bool Connect()
        {
            if (_connected) return true;

            // Cooldown: avoid blocking the UI thread on every heartbeat tick when the
            // server is known to be unreachable.  One genuine attempt per cooldown window
            // is enough; the rest return false instantly.
            if ((DateTime.Now - _lastConnectAttempt).TotalSeconds < ConnectCooldownSeconds)
                return false;
            _lastConnectAttempt = DateTime.Now;

            // Abort any previously faulted channel/factory before creating a new one.
            AbortChannel();

            try
            {
                _handler = new SessionCallbackHandler();
                _handler.SessionTerminated += (s, e) => SessionTerminated?.Invoke(this, e);
                _handler.TimeWarning       += (s, e) => TimeWarning?.Invoke(this, e);
                _handler.ServerMessage     += (s, e) => ServerMessage?.Invoke(this, e);

                var ctx = new InstanceContext(_handler);
                _factory = new DuplexChannelFactory<WCF.ISessionService>(
                    ctx, "SessionServiceEndpoint");

                // Override address from AppSettings — changing ServerAddress in App.config
                // is the only step needed to point this app at a different server PC.
                _factory.Endpoint.Address =
                    new EndpointAddress(ServiceConfiguration.GetServiceAddress());

                // Cap Open() — default is 1 minute, freezes UI thread when server unreachable.
                _factory.Endpoint.Binding.OpenTimeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds);
                // Cap Send() — default is 20 minutes.  A LAN call should complete in < 5 s;
                // bounding at 10 s means a mid-call server crash unblocks the UI in ≤ 10 s.
                _factory.Endpoint.Binding.SendTimeout = TimeSpan.FromSeconds(10);

                // Prevent callback threads from marshalling to the UI thread.
                // Without this, a WCF callback arriving while the UI thread is blocked on
                // an outgoing WCF call (e.g. Heartbeat) causes a deadlock.
                foreach (var op in _factory.Endpoint.Contract.Operations)
                {
                    var attr = op.Behaviors.Find<CallbackBehaviorAttribute>();
                    if (attr != null) attr.UseSynchronizationContext = false;
                }

                _proxy = _factory.CreateChannel();
                ((IClientChannel)_proxy).Open();

                _connected = true;
                // Reset cooldown on success so the next failure starts a fresh window.
                _lastConnectAttempt = DateTime.MinValue;
                return true;
            }
            catch (EndpointNotFoundException ex)
            {
                // Server process is not running or the port is blocked.
                Log($"Server unreachable ({ServiceConfiguration.GetServiceAddress()}): {ex.Message}");
                AbortChannel();
                _connected = false;
                return false;
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.GetType().Name} — {ex.Message}");
                AbortChannel();
                _connected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            AbortChannel();
            _connected = false;
        }

        /// <summary>
        /// Aborts the proxy and factory without throwing.
        /// Must be used instead of Close() whenever the channel may be in a Faulted state —
        /// calling Close() on a faulted channel throws CommunicationObjectFaultedException.
        /// </summary>
        private void AbortChannel()
        {
            try { (_proxy as IClientChannel)?.Abort(); } catch { }
            try { _factory?.Abort(); } catch { }
            _proxy   = null;
            _factory = null;
        }

        public bool EnsureConnection()
        {
            lock (_connectLock)
            {
                if (!_connected) return Connect();
                var ch = _proxy as IClientChannel;
                if (ch == null || ch.State != CommunicationState.Opened)
                {
                    _connected = false;
                    return Connect();
                }
                return true;
            }
        }


        #endregion

        #region UC-01 / UC-09  —  Authentication
        public WCF.AuthenticationResponse AuthenticateUser(
            string username, string password, string clientCode)
        {
            if (!EnsureConnection())
                return new WCF.AuthenticationResponse
                { IsAuthenticated = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.AuthenticateUser(username, password, clientCode); }
            catch (Exception ex)
            {
                Log($"AuthenticateUser: {ex.Message}");
                return new WCF.AuthenticationResponse
                { IsAuthenticated = false, ErrorMessage = $"Connection error: {ex.Message}" };
            }
        }

        public bool ValidateSession(string sessionToken)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.ValidateSession(sessionToken); }
            catch (Exception ex) { Log($"ValidateSession: {ex.Message}"); return false; }
        }


        #endregion

        #region UC-02  —  Start Session
        public WCF.SessionStartResponse StartSession(
            int userId, string clientCode, int durationMinutes)
        {
            if (!EnsureConnection())
                return new WCF.SessionStartResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.StartSession(userId, clientCode, durationMinutes, SessionToken ?? string.Empty); }
            catch (Exception ex)
            {
                Log($"StartSession: {ex.Message}");
                return new WCF.SessionStartResponse
                { Success = false, ErrorMessage = $"Connection error: {ex.Message}" };
            }
        }


        #endregion

        #region UC-07 / UC-08 / UC-14  —  End Session
        public bool EndSession(int sessionId, string terminationType)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.EndSession(sessionId, terminationType); }
            catch (Exception ex) { Log($"EndSession: {ex.Message}"); return false; }
        }


        #endregion

        #region UC-06 / UC-10  —  Session Info
        public WCF.SessionInfo GetSessionInfo(int sessionId)
        {
            if (!EnsureConnection()) return null;
            try { return _proxy.GetSessionInfo(sessionId); }
            catch (Exception ex) { Log($"GetSessionInfo: {ex.Message}"); return null; }
        }

        public WCF.SessionInfo[] GetActiveSessions()
        {
            if (!EnsureConnection()) return Array.Empty<WCF.SessionInfo>();
            try { return _proxy.GetActiveSessions(); }
            catch (Exception ex)
            { Log($"GetActiveSessions: {ex.Message}"); return Array.Empty<WCF.SessionInfo>(); }
        }


        #endregion

        #region UC-05 / UC-12  —  Images
        public bool UploadLoginImage(int sessionId, int userId, string imageBase64)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.UploadLoginImage(sessionId, userId, imageBase64); }
            catch (Exception ex) { Log($"UploadLoginImage: {ex.Message}"); return false; }
        }

        public string DownloadLoginImage(int sessionId)
        {
            if (!EnsureConnection()) return null;
            try { return _proxy.DownloadLoginImage(sessionId); }
            catch (Exception ex) { Log($"DownloadLoginImage: {ex.Message}"); return null; }
        }


        #endregion

        #region UC-11  —  Client Machines
        public bool RegisterClient(string clientCode, string machineName,
            string ipAddress, string macAddress, string location)
        {
            if (!EnsureConnection()) return false;
            try
            { return _proxy.RegisterClient(clientCode, machineName, ipAddress, macAddress, location); }
            catch (Exception ex) { Log($"RegisterClient: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Admin-only: rename a machine or update its physical location from SessionAdmin.
        /// Calls sp_UpdateClientMachineInfo via the WCF service; the change persists
        /// across client reboots because sp_RegisterClient UPDATE path skips these columns.
        /// </summary>
        public bool UpdateClientMachineInfo(string clientCode, string machineName, string location)
        {
            if (!EnsureConnection()) throw new InvalidOperationException("Not connected to server.");
            // Re-throw so the caller (btnEditMachine_Click) can show the real error message.
            // Common failures: server not restarted after code update (ActionNotSupportedException),
            // or sp_UpdateClientMachineInfo not yet deployed to the database.
            return _proxy.UpdateClientMachineInfo(clientCode, machineName, location);
        }

        public bool UpdateClientStatus(string clientCode, string status)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.UpdateClientStatus(clientCode, status); }
            catch (Exception ex) { Log($"UpdateClientStatus: {ex.Message}"); return false; }
        }

        public bool UpdateClientMachineIsActive(string clientCode, bool isActive)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.UpdateClientMachineIsActive(clientCode, isActive); }
            catch (Exception ex) { Log($"UpdateClientMachineIsActive: {ex.Message}"); return false; }
        }

        public WCF.ClientInfo[] GetAllClients()
        {
            if (!EnsureConnection()) return Array.Empty<WCF.ClientInfo>();
            try { return _proxy.GetAllClients(); }
            catch (Exception ex)
            { Log($"GetAllClients: {ex.Message}"); return Array.Empty<WCF.ClientInfo>(); }
        }


        #endregion

        #region UC-03  —  User Registration
        public WCF.UserRegistrationResponse RegisterClientUser(
            string username, string fullName, string password,
            string phone, string address, int adminUserId,
            string profilePictureBase64 = null)
        {
            if (!EnsureConnection())
                return new WCF.UserRegistrationResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try
            { return _proxy.RegisterClientUser(username, fullName, password,
                phone, address, adminUserId, profilePictureBase64); }
            catch (Exception ex)
            { Log($"RegisterClientUser: {ex.Message}");
              return new WCF.UserRegistrationResponse
              { Success = false, ErrorMessage = $"Connection error: {ex.Message}" }; }
        }

        public WCF.UserInfo[] GetAllClientUsers()
        {
            if (!EnsureConnection()) return Array.Empty<WCF.UserInfo>();
            try { return _proxy.GetAllClientUsers(); }
            catch (Exception ex)
            { Log($"GetAllClientUsers: {ex.Message}"); return Array.Empty<WCF.UserInfo>(); }
        }


        #endregion

        #region UC-16 / UC-17  —  Alerts
        public bool LogSecurityAlert(int sessionId, int userId,
            string alertType, string description, string severity)
        {
            if (!EnsureConnection()) return false;
            try
            { return _proxy.LogSecurityAlert(sessionId, userId, alertType, description, severity); }
            catch (Exception ex) { Log($"LogSecurityAlert: {ex.Message}"); return false; }
        }

        public WCF.AlertInfo[] GetUnacknowledgedAlerts()
        {
            if (!EnsureConnection()) return Array.Empty<WCF.AlertInfo>();
            try { return _proxy.GetUnacknowledgedAlerts(); }
            catch (Exception ex)
            { Log($"GetUnacknowledgedAlerts: {ex.Message}"); return Array.Empty<WCF.AlertInfo>(); }
        }

        public bool AcknowledgeAlert(int alertId, int adminUserId)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.AcknowledgeAlert(alertId, adminUserId); }
            catch (Exception ex) { Log($"AcknowledgeAlert: {ex.Message}"); return false; }
        }


        #endregion

        #region UC-13  —  Billing
        public decimal GetCurrentBillingRate()
        {
            if (!EnsureConnection()) return 0m;
            try { return _proxy.GetCurrentBillingRate(); }
            catch (Exception ex) { Log($"GetCurrentBillingRate: {ex.Message}"); return 0m; }
        }

        public decimal CalculateSessionBilling(int sessionId)
        {
            if (!EnsureConnection()) return 0m;
            try { return _proxy.CalculateSessionBilling(sessionId); }
            catch (Exception ex) { Log($"CalculateSessionBilling: {ex.Message}"); return 0m; }
        }


        #endregion

        #region UC-15  —  System Logs
        public WCF.SystemLogInfo[] GetSystemLogs(
            DateTime fromDate, DateTime toDate, string category)
        {
            if (!EnsureConnection()) return Array.Empty<WCF.SystemLogInfo>();
            try { return _proxy.GetSystemLogs(fromDate, toDate, category); }
            catch (Exception ex)
            { Log($"GetSystemLogs: {ex.Message}"); return Array.Empty<WCF.SystemLogInfo>(); }
        }


        #endregion

        #region UC-18  —  Reports
        public WCF.ReportData GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            if (!EnsureConnection()) return new WCF.ReportData();
            try { return _proxy.GetSessionReport(fromDate, toDate); }
            catch (Exception ex)
            { Log($"GetSessionReport: {ex.Message}"); return new WCF.ReportData(); }
        }


        #endregion

        #region Duplex Subscriptions + Heartbeat
        public void SubscribeForNotifications(string clientCode)
        {
            if (!EnsureConnection()) return;
            try { _proxy.SubscribeForNotifications(clientCode); }
            catch (Exception ex) { Log($"Subscribe: {ex.Message}"); }
        }

        public void UnsubscribeFromNotifications(string clientCode)
        {
            if (!EnsureConnection()) return;
            try { _proxy.UnsubscribeFromNotifications(clientCode); }
            catch (Exception ex) { Log($"Unsubscribe: {ex.Message}"); }
        }

        public void Heartbeat(string clientCode)
        {
            if (!EnsureConnection()) return;
            try { _proxy.Heartbeat(clientCode); }
            catch (Exception ex) { Log($"Heartbeat: {ex.Message}"); }
        }

        public int TerminateOrphanSession(string clientCode)
        {
            if (!EnsureConnection()) return 0;
            try { return _proxy.TerminateOrphanSession(clientCode); }
            catch (Exception ex) { Log($"TerminateOrphanSession: {ex.Message}"); return 0; }
        }


        #endregion

        #region IDisposable
        public void Dispose() => Disconnect();

        protected static void Log(string msg)
            => System.Diagnostics.Debug.WriteLine($"[SessionServiceClient] {msg}");
    }

    // ── Callback handler ──────────────────────────────────────────

    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant, UseSynchronizationContext = false)]
    internal sealed class SessionCallbackHandler : WCF.ISessionServiceCallback
    {
        public event EventHandler<SessionTerminatedEventArgs> SessionTerminated;
        public event EventHandler<TimeWarningEventArgs>        TimeWarning;
        public event EventHandler<ServerMessageEventArgs>      ServerMessage;

        public void OnSessionTerminated(int sessionId, string reason)
            => SessionTerminated?.Invoke(this,
               new SessionTerminatedEventArgs { SessionId = sessionId, Reason = reason,
                   Timestamp = DateTime.Now });

        public void OnTimeWarning(int sessionId, int remainingMinutes)
            => TimeWarning?.Invoke(this,
               new TimeWarningEventArgs { SessionId = sessionId,
                   RemainingMinutes = remainingMinutes, Timestamp = DateTime.Now });

        public void OnServerMessage(string message)
        {
            // BeginInvoke (fire-and-forget) prevents a deadlock where the WCF callback thread
            // waits for the UI thread, while the UI thread is blocked on an outgoing WCF call.
            if (System.Windows.Application.Current != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => ServerMessage?.Invoke(this,
                        new ServerMessageEventArgs { Message = message, Timestamp = DateTime.Now })));
            }
            else
            {
                ServerMessage?.Invoke(this,
                   new ServerMessageEventArgs { Message = message, Timestamp = DateTime.Now });
            }
        }
    }

    // ── Event args ────────────────────────────────────────────────

    public class SessionTerminatedEventArgs : EventArgs
    {
        public int      SessionId { get; set; }
        public string   Reason    { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TimeWarningEventArgs : EventArgs
    {
        public int      SessionId        { get; set; }
        public int      RemainingMinutes { get; set; }
        public DateTime Timestamp        { get; set; }
    }

    public class ServerMessageEventArgs : EventArgs
    {
        public string   Message   { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // ── Configuration helper ──────────────────────────────────────

    public static class ServiceConfiguration
    {
        public static string ServerAddress
            => ConfigurationManager.AppSettings["ServerAddress"] ?? "localhost";
        public static string ServerPort
            => ConfigurationManager.AppSettings["ServerPort"] ?? "8001";
        public static string ClientCode
            => ConfigurationManager.AppSettings["ClientCode"] ?? "CL001";
        public static string ClientMachineName
            => ConfigurationManager.AppSettings["ClientMachineName"] ?? Environment.MachineName;

        public static string GetServiceAddress()
            => $"net.tcp://{ServerAddress}:{ServerPort}/SessionService";
    }

        #endregion
}