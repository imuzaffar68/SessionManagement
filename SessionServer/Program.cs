using System;
using System.Configuration;
using System.ServiceModel;
using System.ServiceModel.Description;
using SessionManagement.WCF;

/// <summary>
/// SessionServer — WCF service host.
/// Hosts SessionService on net.tcp://localhost:8001/SessionService.
/// All business logic lives in SessionManagement.Shared.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.Title = "Session Management Server";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("============================================================");
        Console.WriteLine("  Intelligent Client-Server Session Management System");
        Console.WriteLine("  Server v1.0");
        Console.WriteLine("============================================================");
        Console.ResetColor();

        // ── Verify DB connection before opening WCF ───────────────
        try
        {
            string cs = ConfigurationManager
                .ConnectionStrings["SessionManagementDB"]?.ConnectionString
                ?? throw new ConfigurationErrorsException(
                    "Missing 'SessionManagementDB' connection string in App.config");

            var db = new SessionManagement.Data.DatabaseHelper(cs);
            if (!db.TestConnection())
                throw new Exception("Cannot connect to database. Check connection string.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DB]  Database connection OK");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[DB]  ERROR: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
            return;
        }

        // ── WCF Service Host ──────────────────────────────────────
        string listenHost = ConfigurationManager.AppSettings["ListenAddress"] ?? "localhost";
        string listenPort = ConfigurationManager.AppSettings["ServerPort"]    ?? "8001";
        var baseAddress = new Uri($"net.tcp://{listenHost}:{listenPort}/SessionService");

        using (var host = new ServiceHost(typeof(SessionService), baseAddress))
        {
            try
            {
                // NetTcpBinding — required for duplex (callback) channels
#if DEBUG
                var binding = new NetTcpBinding(SecurityMode.None)
#else
                var binding = new NetTcpBinding(SecurityMode.Transport)
#endif
                {
                    MaxReceivedMessageSize = 20_971_520,  // 20 MB (for image payloads)
                    MaxBufferSize          = 20_971_520,
                    ReceiveTimeout         = TimeSpan.FromMinutes(20),
                    SendTimeout            = TimeSpan.FromMinutes(20),
                    OpenTimeout            = TimeSpan.FromMinutes(1),
                    CloseTimeout           = TimeSpan.FromSeconds(30)
                };
                binding.ReliableSession.Enabled = false;  // must be off for duplex

                host.AddServiceEndpoint(typeof(ISessionService), binding, "");

                var debug = host.Description.Behaviors.Find<ServiceDebugBehavior>()
                            ?? new ServiceDebugBehavior();
#if DEBUG
                debug.IncludeExceptionDetailInFaults = true;   // dev only — never expose in Release
#else
                debug.IncludeExceptionDetailInFaults = false;
#endif
                if (!host.Description.Behaviors.Contains(debug))
                    host.Description.Behaviors.Add(debug);

                // Throttling: support up to 100 concurrent client connections
                var throttle = host.Description.Behaviors
                                   .Find<ServiceThrottlingBehavior>()
                               ?? new ServiceThrottlingBehavior();
                throttle.MaxConcurrentCalls     = 100;
                throttle.MaxConcurrentInstances = 1;   // singleton
                throttle.MaxConcurrentSessions  = 100;
                if (!host.Description.Behaviors.Contains(throttle))
                    host.Description.Behaviors.Add(throttle);

                host.Open();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[WCF] Service listening on: {baseAddress}");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("  Server is running.  Press Enter to stop.");
                Console.WriteLine();

                Console.ReadLine();

                host.Close();
                Console.WriteLine("[WCF] Service stopped gracefully.");
            }
            catch (AddressAlreadyInUseException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[WCF] ERROR: Port 8001 is already in use.");
                Console.WriteLine("      Stop the other process and try again.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[WCF] ERROR: {ex.Message}");
                Console.ResetColor();
            }
        }

        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
    }
}
