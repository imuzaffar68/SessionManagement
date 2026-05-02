using System;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace SessionManagement.WCF
{
    /// <summary>
    /// WCF duplex service contract.
    /// Every [OperationContract] maps to one or more sequence-diagram steps.
    /// CallbackContract = ISessionServiceCallback enables server→client push (FR-14).
    /// </summary>
    [ServiceContract(CallbackContract = typeof(ISessionServiceCallback))]
    public interface ISessionService
    {

        #region UC-01 / UC-09  Authentication
        /// <summary>SEQ-01/09: Authenticate a user; returns role for role check.</summary>
        [OperationContract]
        AuthenticationResponse AuthenticateUser(string username, string password, string clientCode);

        [OperationContract]
        bool ValidateSession(string sessionToken);


        #endregion

        #region UC-02  Start Session
        /// <summary>SEQ-02: Create session record; returns SessionId + server times.</summary>
        [OperationContract]
        SessionStartResponse StartSession(int userId, string clientCode, int durationMinutes, string sessionToken);


        #endregion

        #region UC-07 / UC-08 / UC-14  End Session
        /// <summary>SEQ-07/08/14: End + finalize billing atomically; pushes callback.</summary>
        [OperationContract]
        bool EndSession(int sessionId, string terminationType);


        #endregion

        #region UC-06 / UC-10  Get Session Info
        [OperationContract]
        SessionInfo GetSessionInfo(int sessionId);

        /// <summary>SEQ-10: All active sessions with live remaining/billing.</summary>
        [OperationContract]
        SessionInfo[] GetActiveSessions();


        #endregion

        #region UC-04 / UC-05 / UC-12  Images
        /// <summary>SEQ-05: Receive Base64 image, save to disk, update tblSessionImage.</summary>
        [OperationContract]
        bool UploadLoginImage(int sessionId, int userId, string imageBase64);

        /// <summary>SEQ-12: Return stored image as Base64 for admin viewer.</summary>
        [OperationContract]
        string DownloadLoginImage(int sessionId);


        #endregion

        #region UC-11  Client Machines
        /// <summary>
        /// Called at client startup; registers a new machine (first connect) or
        /// refreshes its network identity (reconnect).
        /// <para>
        /// clientCode is auto-derived by ResolveClientCode() in SessionClient as
        /// "CL-" + MAC address — it is never set by the installer.
        /// machineName and location come from App.config keys ClientMachineName /
        /// ClientLocation, which are written by the Inno Setup installer wizard.
        /// MachineName and Location are only stored on first INSERT; subsequent
        /// reconnects preserve any admin renames done via UpdateClientMachineInfo.
        /// </para>
        /// </summary>
        [OperationContract]
        bool RegisterClient(string clientCode, string machineName,
                            string ipAddress, string macAddress, string location);

        /// <summary>
        /// Admin renames or relocates a machine from SessionAdmin (Edit button).
        /// This is the only path that overwrites MachineName / Location after
        /// first registration — client reconnects deliberately skip those columns.
        /// </summary>
        [OperationContract]
        bool UpdateClientMachineInfo(string clientCode, string machineName, string location);

        [OperationContract]
        bool UpdateClientStatus(string clientCode, string status);

        /// <summary>Update IsActive status of a client machine.</summary>
        [OperationContract]
        bool UpdateClientMachineIsActive(string clientCode, bool isActive);

        /// <summary>SEQ-11: All machines with current session user.</summary>
        [OperationContract]
        ClientInfo[] GetAllClients();


        #endregion

        #region UC-03  User Registration ├
        // ── UC-03  User Registration ├──────────────────────────────

        /// <summary>SEQ-03: Admin registers a new ClientUser with password.</summary>
        [OperationContract]
        UserRegistrationResponse RegisterClientUser(string username, string fullName,
                                                     string password, string phone,
                                                     string address, int adminUserId,
                                                     string profilePictureBase64);

        /// <summary>Get all registered client users.</summary>
        [OperationContract]
        UserInfo[] GetAllClientUsers();

        /// <summary>Update ClientUser profile information (FullName, Phone, Address, Photo).</summary>
        [OperationContract]
        UserUpdateResponse UpdateClientUser(int userId, string fullName, string phone,
                                            string address, int adminUserId,
                                            string profilePictureBase64);

        /// <summary>Hard-delete a ClientUser if they have no session history; returns -1 if blocked by FK.</summary>
        [OperationContract]
        UserDeleteResponse DeleteClientUser(int userId, int adminUserId);

        /// <summary>Reset ClientUser password to a new value.</summary>
        [OperationContract]
        PasswordResetResponse ResetClientUserPassword(int userId, string newPassword, int adminUserId);

        /// <summary>Change the logged-in admin's own password (verifies current password first).</summary>
        [OperationContract]
        AdminPasswordChangeResponse ChangeAdminPassword(int adminUserId, string currentPassword, string newPassword);

        /// <summary>Toggle ClientUser account status (Active ↔ Disabled).</summary>
        [OperationContract]
        UserStatusToggleResponse ToggleUserStatus(int userId, int adminUserId);


        #endregion

        #region UC-16 / UC-17  Security Alerts
        /// <summary>SEQ-16: Log alert + push real-time notification to admins (FR-14).</summary>
        [OperationContract]
        bool LogSecurityAlert(int sessionId, int userId,
                              string alertType, string description, string severity);

        /// <summary>SEQ-17: All unacknowledged alerts for dashboard.</summary>
        [OperationContract]
        AlertInfo[] GetUnacknowledgedAlerts();

        /// <summary>SEQ-17: Admin acknowledges; writes AcknowledgedByAdminUserId.</summary>
        [OperationContract]
        bool AcknowledgeAlert(int alertId, int adminUserId);


        #endregion

        #region UC-07 / UC-13  Billing
        [OperationContract]
        decimal GetCurrentBillingRate();

        [OperationContract]
        decimal CalculateSessionBilling(int sessionId);


        #endregion

        #region PAYMENT COLLECTION
        /// <summary>All billing records (finalized sessions). unpaidOnly=true limits to IsPaid=0.</summary>
        [OperationContract]
        BillingRecordInfo[] GetBillingRecords(bool unpaidOnly);

        /// <summary>Mark a finalized billing record as paid; records admin and timestamp.</summary>
        [OperationContract]
        bool MarkBillingRecordPaid(int billingRecordId, int adminUserId);


        #endregion

        #region BILLING RATE MANAGEMENT
        [OperationContract]
        BillingRateInfo[] GetAllBillingRates();

        [OperationContract]
        int InsertBillingRate(string name, decimal ratePerMinute, string currency,
            System.DateTime? effectiveFrom, System.DateTime? effectiveTo, bool isDefault, int adminUserId, string notes);

        [OperationContract]
        bool UpdateBillingRate(int billingRateId, string name, decimal ratePerMinute,
            string currency, System.DateTime? effectiveFrom, System.DateTime? effectiveTo, bool isActive, bool isDefault, string notes);

        [OperationContract]
        bool DeleteBillingRate(int billingRateId);

        [OperationContract]
        bool SetDefaultBillingRate(int billingRateId);


        #endregion

        #region UC-15  Session Logs
        /// <summary>
        /// SEQ-15: Returns tblSystemLog entries for the given date range and
        /// optional category filter (Auth|Session|Billing|Security|System|null=all).
        /// </summary>
        [OperationContract]
        SystemLogInfo[] GetSystemLogs(DateTime fromDate, DateTime toDate, string category);


        #endregion

        #region UC-18  Reports
        [OperationContract]
        ReportData GetSessionReport(DateTime fromDate, DateTime toDate);


        #endregion

        #region Duplex subscriptions
        [OperationContract]
        void SubscribeForNotifications(string clientCode);

        [OperationContract]
        void UnsubscribeFromNotifications(string clientCode);


        #endregion

        #region Heartbeat
        /// <summary>
        /// Client calls this every 30 s to prove it is alive.
        /// Server stamps tblClientMachine.LastSeenAt = GETDATE().
        /// A background scan marks machines Offline if not heard from in 90 s.
        /// </summary>
        [OperationContract]
        void Heartbeat(string clientCode);


        #endregion

        #region Orphan Session Management
        /// <summary>
        /// Called by the client at startup BEFORE RegisterClient().
        /// Ends any Active session left on this machine from a previous
        /// crash or power-cut, billing for actual elapsed time using
        /// tblClientMachine.LastSeenAt as the effective session-end proxy.
        /// Returns the number of orphan sessions terminated (0 = clean state).
        /// </summary>
        [OperationContract]
        int TerminateOrphanSession(string clientCode);

    }

    /// <summary>
    /// Server → client callback contract.
    /// All methods are one-way (fire-and-forget) so the server is never blocked.
    /// </summary>
    public interface ISessionServiceCallback
    {
        /// <summary>SEQ-14: Server pushes session termination to the client machine.</summary>
        [OperationContract(IsOneWay = true)]
        void OnSessionTerminated(int sessionId, string reason);

        /// <summary>SEQ-06 opt: server warns client N minutes before expiry.</summary>
        [OperationContract(IsOneWay = true)]
        void OnTimeWarning(int sessionId, int remainingMinutes);

        /// <summary>FR-14: generic server-to-client broadcast (alerts, refresh hints).</summary>
        [OperationContract(IsOneWay = true)]
        void OnServerMessage(string message);
    }


    #endregion

    #region Data Contracts
    [DataContract]
    public class AuthenticationResponse
    {
        [DataMember] public bool   IsAuthenticated      { get; set; }
        [DataMember] public int    UserId               { get; set; }
        [DataMember] public string Username             { get; set; }
        [DataMember] public string FullName             { get; set; }
        /// <summary>"Admin" or "ClientUser" — mapped from tblUser.Role.</summary>
        [DataMember] public string UserType             { get; set; }
        [DataMember] public string SessionToken         { get; set; }
        [DataMember] public string ErrorMessage         { get; set; }
        [DataMember] public string ProfilePictureBase64 { get; set; }
    }

    [DataContract]
    public class SessionStartResponse
    {
        [DataMember] public bool     Success         { get; set; }
        [DataMember] public int      SessionId       { get; set; }
        [DataMember] public DateTime StartTime       { get; set; }
        /// <summary>Server-computed: StartTime + SelectedDurationMinutes.</summary>
        [DataMember] public DateTime ExpectedEndTime { get; set; }
        [DataMember] public string   ErrorMessage    { get; set; }
    }

    [DataContract]
    public class SessionInfo
    {
        [DataMember] public int      SessionId        { get; set; }
        [DataMember] public int      UserId           { get; set; }
        [DataMember] public string   Username         { get; set; }
        [DataMember] public string   FullName         { get; set; }
        [DataMember] public string   ClientCode       { get; set; }
        [DataMember] public string   MachineName      { get; set; }
        [DataMember] public DateTime StartTime        { get; set; }
        [DataMember] public int      SelectedDuration { get; set; }
        [DataMember] public DateTime ExpectedEndTime  { get; set; }
        [DataMember] public string   SessionStatus    { get; set; }
        [DataMember] public int      RemainingMinutes { get; set; }
        [DataMember] public decimal  CurrentBilling   { get; set; }
        [DataMember] public string   ImagePath        { get; set; }
    }

    [DataContract]
    public class ClientInfo
    {
        [DataMember] public int       ClientId          { get; set; }
        [DataMember] public string    ClientCode        { get; set; }
        [DataMember] public string    MachineName       { get; set; }
        [DataMember] public string    IpAddress         { get; set; }
        [DataMember] public string    MacAddress        { get; set; }
        [DataMember] public string    Location          { get; set; }
        [DataMember] public bool      IsActive          { get; set; }
        [DataMember] public string    Status            { get; set; }
        [DataMember] public DateTime? LastActiveTime    { get; set; }
        [DataMember] public string    CurrentUser       { get; set; }
        [DataMember] public int       MissedHeartbeats  { get; set; }
    }

    [DataContract]
    public class AlertInfo
    {
        [DataMember] public int      AlertId     { get; set; }
        [DataMember] public int?     SessionId   { get; set; }
        [DataMember] public string   Username    { get; set; }
        [DataMember] public string   ClientCode  { get; set; }
        [DataMember] public string   AlertType   { get; set; }
        [DataMember] public string   Description { get; set; }
        [DataMember] public DateTime Timestamp   { get; set; }
        [DataMember] public string   Severity    { get; set; }
    }

    [DataContract]
    public class SystemLogInfo
    {
        [DataMember] public int      LogId        { get; set; }
        [DataMember] public DateTime LoggedAt     { get; set; }
        [DataMember] public string   Category     { get; set; }
        [DataMember] public string   Type         { get; set; }
        [DataMember] public string   Message      { get; set; }
        [DataMember] public string   Source       { get; set; }
        [DataMember] public int?     SessionId    { get; set; }
        [DataMember] public string   Username     { get; set; }
        [DataMember] public string   ClientCode   { get; set; }
    }

    [DataContract]
    public class ReportData
    {
        [DataMember] public int           TotalSessions { get; set; }
        [DataMember] public decimal       TotalRevenue  { get; set; }
        [DataMember] public double        TotalHours    { get; set; }
        [DataMember] public int           TotalAlerts   { get; set; }
        [DataMember] public SessionInfo[] Sessions      { get; set; }
        [DataMember] public DateTime      FromDate      { get; set; }
        [DataMember] public DateTime      ToDate        { get; set; }
    }

    [DataContract]
    public class UserRegistrationResponse
    {
        [DataMember] public bool   Success      { get; set; }
        [DataMember] public int    UserId       { get; set; }
        [DataMember] public string Username     { get; set; }
        [DataMember] public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class UserInfo
    {
        [DataMember] public int      UserId               { get; set; }
        [DataMember] public string   Username             { get; set; }
        [DataMember] public string   FullName             { get; set; }
        [DataMember] public string   Phone                { get; set; }
        [DataMember] public string   Address              { get; set; }
        [DataMember] public string   Status               { get; set; }
        [DataMember] public string   Role                 { get; set; }
        [DataMember] public DateTime CreatedAt            { get; set; }
        [DataMember] public DateTime? LastLoginAt         { get; set; }
        [DataMember] public string   ProfilePictureBase64 { get; set; }
    }

    [DataContract]
    public class UserDeleteResponse
    {
        [DataMember] public bool   Success      { get; set; }
        [DataMember] public int    UserId       { get; set; }
        [DataMember] public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class BillingRecordInfo
    {
        [DataMember] public int       BillingRecordId      { get; set; }
        [DataMember] public int       SessionId            { get; set; }
        [DataMember] public string    Username             { get; set; }
        [DataMember] public string    FullName             { get; set; }
        [DataMember] public string    MachineCode          { get; set; }
        [DataMember] public int       BillableMinutes      { get; set; }
        [DataMember] public decimal   Amount               { get; set; }
        [DataMember] public string    Currency             { get; set; }
        [DataMember] public DateTime  CalculatedAt         { get; set; }
        [DataMember] public bool      IsPaid               { get; set; }
        [DataMember] public DateTime? PaidAt               { get; set; }
        [DataMember] public int?      ReceivedByAdminId    { get; set; }
    }

    [DataContract]
    public class BillingRateInfo
    {
        [DataMember] public int BillingRateId { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public decimal RatePerMinute { get; set; }
        [DataMember] public string Currency { get; set; }
        [DataMember] public DateTime? EffectiveFrom { get; set; }
        [DataMember] public DateTime? EffectiveTo { get; set; }
        [DataMember] public bool IsActive { get; set; }
        [DataMember] public bool IsDefault { get; set; }
        [DataMember] public DateTime CreatedAt { get; set; }
        [DataMember] public string Notes { get; set; }
    }

    [DataContract]
    public class UserUpdateResponse
    {
        [DataMember] public bool   Success      { get; set; }
        [DataMember] public int    UserId       { get; set; }
        [DataMember] public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class PasswordResetResponse
    {
        [DataMember] public bool   Success      { get; set; }
        [DataMember] public int    UserId       { get; set; }
        [DataMember] public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class AdminPasswordChangeResponse
    {
        [DataMember] public bool   Success      { get; set; }
        [DataMember] public string ErrorMessage { get; set; }
    }

    [DataContract]
    public class UserStatusToggleResponse
    {
        [DataMember] public bool   Success      { get; set; }
        [DataMember] public int    UserId       { get; set; }
        [DataMember] public string NewStatus    { get; set; }
        [DataMember] public string ErrorMessage { get; set; }
    }

    #endregion
}