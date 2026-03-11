using System;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace SessionManagement.WCF
{
    /// <summary>
    /// WCF Service Contract for Client-Server Communication
    /// </summary>
    [ServiceContract(CallbackContract = typeof(ISessionServiceCallback))]
    public interface ISessionService
    {
        #region Authentication

        [OperationContract]
        AuthenticationResponse AuthenticateUser(string username, string password, string clientCode);

        [OperationContract]
        bool ValidateSession(string sessionToken);

        #endregion

        #region Session Management

        [OperationContract]
        SessionStartResponse StartSession(int userId, string clientCode, int durationMinutes);

        [OperationContract]
        bool EndSession(int sessionId, string terminationType);

        [OperationContract]
        SessionInfo GetSessionInfo(int sessionId);

        [OperationContract]
        SessionInfo[] GetActiveSessions();

        #endregion

        #region Image Transfer

        [OperationContract]
        bool UploadLoginImage(int sessionId, int userId, string imageBase64);

        [OperationContract]
        string DownloadLoginImage(int sessionId);

        #endregion

        #region Client Management

        [OperationContract]
        bool RegisterClient(string clientCode, string machineName, string ipAddress, string macAddress);

        [OperationContract]
        bool UpdateClientStatus(string clientCode, string status);

        [OperationContract]
        ClientInfo[] GetAllClients();

        #endregion

        #region Alerts & Monitoring

        [OperationContract]
        bool LogSecurityAlert(int sessionId, int userId, string alertType, string description, string severity);

        [OperationContract]
        AlertInfo[] GetUnacknowledgedAlerts();

        [OperationContract]
        bool AcknowledgeAlert(int alertId, int adminUserId);

        #endregion

        #region Billing

        [OperationContract]
        decimal GetCurrentBillingRate();

        [OperationContract]
        decimal CalculateSessionBilling(int sessionId);

        #endregion

        #region Reports

        [OperationContract]
        ReportData GetSessionReport(DateTime fromDate, DateTime toDate);

        #endregion

        #region Client Registration (Duplex Communication)

        [OperationContract]
        void SubscribeForNotifications(string clientCode);

        [OperationContract]
        void UnsubscribeFromNotifications(string clientCode);

        #endregion
    }

    /// <summary>
    /// Callback contract for server-to-client notifications
    /// </summary>
    public interface ISessionServiceCallback
    {
        [OperationContract(IsOneWay = true)]
        void OnSessionTerminated(int sessionId, string reason);

        [OperationContract(IsOneWay = true)]
        void OnTimeWarning(int sessionId, int remainingMinutes);

        [OperationContract(IsOneWay = true)]
        void OnServerMessage(string message);
    }

    #region Data Contracts

    [DataContract]
    public class AuthenticationResponse
    {
        [DataMember]
        public bool IsAuthenticated { get; set; }

        [DataMember]
        public int UserId { get; set; }

        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string FullName { get; set; }

        [DataMember]
        public string UserType { get; set; }

        [DataMember]
        public string SessionToken { get; set; }

        [DataMember]
        public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class SessionStartResponse
    {
        [DataMember]
        public bool Success { get; set; }

        [DataMember]
        public int SessionId { get; set; }

        [DataMember]
        public DateTime StartTime { get; set; }

        [DataMember]
        public DateTime ExpectedEndTime { get; set; }

        [DataMember]
        public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class SessionInfo
    {
        [DataMember]
        public int SessionId { get; set; }

        [DataMember]
        public int UserId { get; set; }

        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string FullName { get; set; }

        [DataMember]
        public string ClientCode { get; set; }

        [DataMember]
        public string MachineName { get; set; }

        [DataMember]
        public DateTime StartTime { get; set; }

        [DataMember]
        public int SelectedDuration { get; set; }

        [DataMember]
        public DateTime ExpectedEndTime { get; set; }

        [DataMember]
        public string SessionStatus { get; set; }

        [DataMember]
        public int RemainingMinutes { get; set; }

        [DataMember]
        public decimal CurrentBilling { get; set; }
    }

    [DataContract]
    public class ClientInfo
    {
        [DataMember]
        public int ClientId { get; set; }

        [DataMember]
        public string ClientCode { get; set; }

        [DataMember]
        public string MachineName { get; set; }

        [DataMember]
        public string IpAddress { get; set; }

        [DataMember]
        public string MacAddress { get; set; }

        [DataMember]
        public string Status { get; set; }

        [DataMember]
        public DateTime? LastActiveTime { get; set; }

        [DataMember]
        public string CurrentUser { get; set; }
    }

    [DataContract]
    public class AlertInfo
    {
        [DataMember]
        public int AlertId { get; set; }

        [DataMember]
        public int? SessionId { get; set; }

        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string ClientCode { get; set; }

        [DataMember]
        public string AlertType { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public DateTime Timestamp { get; set; }

        [DataMember]
        public string Severity { get; set; }
    }

    [DataContract]
    public class ReportData
    {
        [DataMember]
        public int TotalSessions { get; set; }

        [DataMember]
        public decimal TotalRevenue { get; set; }

        [DataMember]
        public double TotalHours { get; set; }

        [DataMember]
        public int TotalAlerts { get; set; }

        [DataMember]
        public SessionInfo[] Sessions { get; set; }

        [DataMember]
        public DateTime FromDate { get; set; }

        [DataMember]
        public DateTime ToDate { get; set; }
    }

    #endregion
}