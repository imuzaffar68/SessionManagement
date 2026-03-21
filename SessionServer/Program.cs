using System;
using System.ServiceModel;
using SessionManagement.WCF;
using System.Configuration;

class Program
{
    static void Main(string[] args)
    {
        string conn = ConfigurationManager.ConnectionStrings["SessionManagementDB"]?.ConnectionString
                      ?? throw new ConfigurationErrorsException("Missing SessionManagementDB");
        // Use the service type, add endpoints programmatically
        // The service contract requires a duplex (callback) channel. BasicHttpBinding does not support duplex.
        // Use a duplex-capable binding like NetTcpBinding (recommended for intranet) or WSDualHttpBinding for HTTP duplex.
        var baseAddress = new Uri("net.tcp://localhost:8001/SessionService");
        using (var host = new ServiceHost(typeof(SessionService), baseAddress))
        {
            var binding = new NetTcpBinding(SecurityMode.None);
            // configure binding for sessions/duplex if needed
            binding.ReceiveTimeout = TimeSpan.FromMinutes(10);
            binding.SendTimeout = TimeSpan.FromMinutes(10);

            host.AddServiceEndpoint(typeof(ISessionService), binding, "");
            host.Open();
            Console.WriteLine("Session Management Service is running...");
            Console.WriteLine("Press Enter to stop the service.");
            Console.ReadLine();
            host.Close();
        }
    }
}

// C# - DatabaseHelper constructor (safe)
public class DatabaseHelper
{
    private string connectionString;

    public DatabaseHelper()
    {
        var cs = ConfigurationManager.ConnectionStrings["SessionManagementDB"];
        if (cs == null)
            throw new ConfigurationErrorsException("Missing connection string 'SessionManagementDB' in application configuration.");
        connectionString = cs.ConnectionString;
    }

    public DatabaseHelper(string connectionString)
    {
        this.connectionString = connectionString;
    }
}

// C# - new SessionService ctor
public class SessionService : ISessionService
{
    private readonly DatabaseHelper dbHelper;

    public SessionService()
    {
        var cs = ConfigurationManager.ConnectionStrings["SessionManagementDB"]?.ConnectionString;
        if (string.IsNullOrEmpty(cs)) throw new ConfigurationErrorsException("Missing 'SessionManagementDB' connection string in host config.");
        dbHelper = new DatabaseHelper(cs);
    }

    public SessionService(string connectionString)
    {
        dbHelper = new DatabaseHelper(connectionString);
    }

    public bool AcknowledgeAlert(int alertId, int adminUserId)
    {
        throw new NotImplementedException();
    }

    public AuthenticationResponse AuthenticateUser(string username, string password, string clientCode)
    {
        throw new NotImplementedException();
    }

    public decimal CalculateSessionBilling(int sessionId)
    {
        throw new NotImplementedException();
    }

    public string DownloadLoginImage(int sessionId)
    {
        throw new NotImplementedException();
    }

    public bool EndSession(int sessionId, string terminationType)
    {
        throw new NotImplementedException();
    }

    public SessionInfo[] GetActiveSessions()
    {
        throw new NotImplementedException();
    }

    public ClientInfo[] GetAllClients()
    {
        throw new NotImplementedException();
    }

    public decimal GetCurrentBillingRate()
    {
        throw new NotImplementedException();
    }

    public SessionInfo GetSessionInfo(int sessionId)
    {
        throw new NotImplementedException();
    }

    public ReportData GetSessionReport(DateTime fromDate, DateTime toDate)
    {
        throw new NotImplementedException();
    }

    public AlertInfo[] GetUnacknowledgedAlerts()
    {
        throw new NotImplementedException();
    }

    public bool LogSecurityAlert(int sessionId, int userId, string alertType, string description, string severity)
    {
        throw new NotImplementedException();
    }

    public bool RegisterClient(string clientCode, string machineName, string ipAddress, string macAddress)
    {
        throw new NotImplementedException();
    }

    public SessionStartResponse StartSession(int userId, string clientCode, int durationMinutes)
    {
        throw new NotImplementedException();
    }

    public void SubscribeForNotifications(string clientCode)
    {
        throw new NotImplementedException();
    }

    public void UnsubscribeFromNotifications(string clientCode)
    {
        throw new NotImplementedException();
    }

    public bool UpdateClientStatus(string clientCode, string status)
    {
        throw new NotImplementedException();
    }

    public bool UploadLoginImage(int sessionId, int userId, string imageBase64)
    {
        throw new NotImplementedException();
    }

    public bool ValidateSession(string sessionToken)
    {
        throw new NotImplementedException();
    }
}