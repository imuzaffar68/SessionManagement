using System;
using System.Configuration;
using System.ServiceModel;
using System.ServiceModel.Description;
using SessionManagement.WCF;

namespace SessionManagement.Client
{
    /// <summary>
    /// WCF duplex client proxy.
    /// Used by both SessionClient and SessionAdmin.
    /// EnsureConnection() auto-reconnects on dead channels (NFR-03).
    /// </summary>
    public sealed class SessionServiceClient : IDisposable
    {
        private DuplexChannelFactory<ISessionService> _factory;
        private ISessionService                       _proxy;
        private SessionCallbackHandler                _handler;
        private bool                                  _connected;

        // ── Server-push events ────────────────────────────────────
        public event EventHandler<SessionTerminatedEventArgs> SessionTerminated;
        public event EventHandler<TimeWarningEventArgs>        TimeWarning;
        public event EventHandler<ServerMessageEventArgs>      ServerMessage;

        public bool IsConnected => _connected;

        // ─────────────────────────────────────────────────────────
        //  Connection management
        // ─────────────────────────────────────────────────────────

        public bool Connect()
        {
            if (_connected) return true;
            try
            {
                _handler = new SessionCallbackHandler();
                _handler.SessionTerminated += (s, e) => SessionTerminated?.Invoke(this, e);
                _handler.TimeWarning       += (s, e) => TimeWarning?.Invoke(this, e);
                _handler.ServerMessage     += (s, e) => ServerMessage?.Invoke(this, e);

                var ctx = new InstanceContext(_handler);
                _factory = new DuplexChannelFactory<ISessionService>(
                    ctx, "SessionServiceEndpoint");

                // Ensure callbacks do not marshal to UI thread
                foreach (var op in _factory.Endpoint.Contract.Operations)
                {
                    var attr = op.Behaviors.Find<CallbackBehaviorAttribute>();
                    if (attr != null) attr.UseSynchronizationContext = false;
                }

                _proxy = _factory.CreateChannel();
                ((IClientChannel)_proxy).Open();

                _connected = true;
                return true;
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
                _connected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                var ch = _proxy as IClientChannel;
                if (ch?.State == CommunicationState.Opened) ch.Close();
                _factory?.Close();
            }
            catch { /* best-effort */ }
            finally { _connected = false; }
        }

        private bool EnsureConnection()
        {
            if (!_connected) return Connect();
            var ch = _proxy as IClientChannel;
            if (ch == null || ch.State != CommunicationState.Opened)
            { _connected = false; return Connect(); }
            return true;
        }

        // ─────────────────────────────────────────────────────────
        //  UC-01 / UC-09  —  Authentication
        // ─────────────────────────────────────────────────────────

        public AuthenticationResponse AuthenticateUser(
            string username, string password, string clientCode)
        {
            if (!EnsureConnection())
                return new AuthenticationResponse
                { IsAuthenticated = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.AuthenticateUser(username, password, clientCode); }
            catch (Exception ex)
            {
                Log($"AuthenticateUser: {ex.Message}");
                return new AuthenticationResponse
                { IsAuthenticated = false, ErrorMessage = $"Connection error: {ex.Message}" };
            }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-02  —  Start Session
        // ─────────────────────────────────────────────────────────

        public SessionStartResponse StartSession(
            int userId, string clientCode, int durationMinutes)
        {
            if (!EnsureConnection())
                return new SessionStartResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.StartSession(userId, clientCode, durationMinutes); }
            catch (Exception ex)
            {
                Log($"StartSession: {ex.Message}");
                return new SessionStartResponse
                { Success = false, ErrorMessage = $"Connection error: {ex.Message}" };
            }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-07 / UC-08 / UC-14  —  End Session
        // ─────────────────────────────────────────────────────────

        public bool EndSession(int sessionId, string terminationType)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.EndSession(sessionId, terminationType); }
            catch (Exception ex) { Log($"EndSession: {ex.Message}"); return false; }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-06 / UC-10  —  Session Info
        // ─────────────────────────────────────────────────────────

        public SessionInfo GetSessionInfo(int sessionId)
        {
            if (!EnsureConnection()) return null;
            try { return _proxy.GetSessionInfo(sessionId); }
            catch (Exception ex) { Log($"GetSessionInfo: {ex.Message}"); return null; }
        }

        public SessionInfo[] GetActiveSessions()
        {
            if (!EnsureConnection()) return Array.Empty<SessionInfo>();
            try { return _proxy.GetActiveSessions(); }
            catch (Exception ex)
            { Log($"GetActiveSessions: {ex.Message}"); return Array.Empty<SessionInfo>(); }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-05 / UC-12  —  Images
        // ─────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────
        //  UC-11  —  Client Machines
        // ─────────────────────────────────────────────────────────

        public bool RegisterClient(string clientCode, string machineName,
            string ipAddress, string macAddress)
        {
            if (!EnsureConnection()) return false;
            try
            { return _proxy.RegisterClient(clientCode, machineName, ipAddress, macAddress); }
            catch (Exception ex) { Log($"RegisterClient: {ex.Message}"); return false; }
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

        public ClientInfo[] GetAllClients()
        {
            if (!EnsureConnection()) return Array.Empty<ClientInfo>();
            try { return _proxy.GetAllClients(); }
            catch (Exception ex)
            { Log($"GetAllClients: {ex.Message}"); return Array.Empty<ClientInfo>(); }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-03  —  User Registration
        // ─────────────────────────────────────────────────────────

        public UserRegistrationResponse RegisterClientUser(
            string username, string fullName, string password,
            string phone, string address, int adminUserId,
            string profilePictureBase64 = null)
        {
            if (!EnsureConnection())
                return new UserRegistrationResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try
            { return _proxy.RegisterClientUser(username, fullName, password, phone, address, adminUserId, profilePictureBase64); }
            catch (Exception ex)
            { Log($"RegisterClientUser: {ex.Message}");
              return new UserRegistrationResponse
              { Success = false, ErrorMessage = $"Connection error: {ex.Message}" }; }
        }

        public UserInfo[] GetAllClientUsers()
        {
            if (!EnsureConnection()) return Array.Empty<UserInfo>();
            try { return _proxy.GetAllClientUsers(); }
            catch (Exception ex)
            { Log($"GetAllClientUsers: {ex.Message}"); return Array.Empty<UserInfo>(); }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-16 / UC-17  —  Alerts
        // ─────────────────────────────────────────────────────────

        public bool LogSecurityAlert(int sessionId, int userId,
            string alertType, string description, string severity)
        {
            if (!EnsureConnection()) return false;
            try
            { return _proxy.LogSecurityAlert(sessionId, userId, alertType, description, severity); }
            catch (Exception ex) { Log($"LogSecurityAlert: {ex.Message}"); return false; }
        }

        public AlertInfo[] GetUnacknowledgedAlerts()
        {
            if (!EnsureConnection()) return Array.Empty<AlertInfo>();
            try { return _proxy.GetUnacknowledgedAlerts(); }
            catch (Exception ex)
            { Log($"GetUnacknowledgedAlerts: {ex.Message}"); return Array.Empty<AlertInfo>(); }
        }

        public bool AcknowledgeAlert(int alertId, int adminUserId)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.AcknowledgeAlert(alertId, adminUserId); }
            catch (Exception ex) { Log($"AcknowledgeAlert: {ex.Message}"); return false; }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-13  —  Billing
        // ─────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────
        //  UC-15  —  System Logs
        // ─────────────────────────────────────────────────────────

        public SystemLogInfo[] GetSystemLogs(
            DateTime fromDate, DateTime toDate, string category)
        {
            if (!EnsureConnection()) return Array.Empty<SystemLogInfo>();
            try { return _proxy.GetSystemLogs(fromDate, toDate, category); }
            catch (Exception ex)
            { Log($"GetSystemLogs: {ex.Message}"); return Array.Empty<SystemLogInfo>(); }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-18  —  Reports
        // ─────────────────────────────────────────────────────────

        public ReportData GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            if (!EnsureConnection()) return new ReportData();
            try { return _proxy.GetSessionReport(fromDate, toDate); }
            catch (Exception ex)
            { Log($"GetSessionReport: {ex.Message}"); return new ReportData(); }
        }

        // ─────────────────────────────────────────────────────────
        //  Duplex Subscriptions
        // ─────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────
        //  IDisposable
        // ─────────────────────────────────────────────────────────

        public void Dispose() => Disconnect();

        private static void Log(string msg)
            => System.Diagnostics.Debug.WriteLine($"[SessionServiceClient] {msg}");
    }

    // ── Callback handler ──────────────────────────────────────────

    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant, UseSynchronizationContext = false)]
    internal sealed class SessionCallbackHandler : ISessionServiceCallback
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
            // Always marshal to UI thread if needed
            if (System.Windows.Application.Current != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    ServerMessage?.Invoke(this,
                        new ServerMessageEventArgs { Message = message, Timestamp = DateTime.Now }));
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
}
