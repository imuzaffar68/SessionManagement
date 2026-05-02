using System;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceModel;
using System.ServiceModel.Description;
using SessionManagement.WCF;
using static SessionManagement.WCF.ServiceConstants;

/// <summary>
/// SessionServer — WCF service host.
/// Hosts SessionService on net.tcp://localhost:8001/SessionService.
/// All business logic lives in SessionManagement.Shared.
/// </summary>
class Program
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr LoadImage(IntPtr h, string name, uint type, int cx, int cy, uint load);
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();

    static void Main(string[] args)
    {
        Console.Title = "Session Management Server";
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            IntPtr hwnd  = GetConsoleWindow();
            IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x10);
            SendMessage(hwnd, 0x0080, (IntPtr)1, hIcon);
            SendMessage(hwnd, 0x0080, (IntPtr)0, hIcon);
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("============================================================");
        Console.WriteLine("  Intelligent Client-Server Session Management System");
        Console.WriteLine("  Server v1.0");
        Console.WriteLine("============================================================");
        Console.ResetColor();

#if DEBUG
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[WARN] DEBUG build — WCF transport security is DISABLED (SecurityMode.None).");
        Console.WriteLine("       Do NOT deploy this build in production. Use Release mode.");
        Console.ResetColor();
        Console.WriteLine();
#endif

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
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Cannot connect to database 'ClientServerSessionDB'.");
            Console.WriteLine();
            Console.WriteLine("  ── Option A: Fresh installation (no existing data) ──");
            Console.WriteLine("  Run this command to create the database from scratch:");
            Console.WriteLine(@"  sqlcmd -S localhost\SQLEXPRESS -E -i ""SessionManagement_Setup.sql""");
            Console.WriteLine();
            Console.WriteLine("  ── Option B: Replacing server PC (migrate existing data) ──");
            Console.WriteLine("  1. Copy your .bak backup file from the old server PC.");
            Console.WriteLine("  2. Open SSMS → right-click Databases → Restore Database.");
            Console.WriteLine("  3. Select your .bak file and click OK.");
            Console.WriteLine("  4. Start SessionServer again once restore completes.");
            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
            return;
        }

        // ── WCF Service Host ──────────────────────────────────────
        string listenHost = ConfigurationManager.AppSettings["ListenAddress"] ?? "localhost";
        string listenPort = ConfigurationManager.AppSettings["ServerPort"]    ?? "8001";
        var baseAddress = new Uri($"net.tcp://{listenHost}:{listenPort}/SessionService");

        // Create the singleton instance explicitly so we can call Dispose() after host.Close().
        // ServiceHost(typeof(T)) creates the instance internally and never disposes it,
        // leaving _sessionExpiryTimer and _clientOfflineTimer running after shutdown.
        var service = new SessionService();
        using (var host = new ServiceHost(service, baseAddress))
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
                    MaxReceivedMessageSize = WcfMaxMessageBytes,
                    MaxBufferSize          = WcfMaxMessageBytes,
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

                // Throttling is unconditional — resource limits apply in Debug and Release.
                // ServiceDebugBehavior above is #if DEBUG because exposing fault details
                // is dev-only; throttling is a correctness concern in both environments.
                var throttle = host.Description.Behaviors
                                   .Find<ServiceThrottlingBehavior>()
                               ?? new ServiceThrottlingBehavior();
                throttle.MaxConcurrentCalls     = MaxConcurrentCalls;
                throttle.MaxConcurrentInstances = 1;   // singleton service
                throttle.MaxConcurrentSessions  = MaxConcurrentSessions;
                if (!host.Description.Behaviors.Contains(throttle))
                    host.Description.Behaviors.Add(throttle);

                host.Open();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[WCF] Service listening on: {baseAddress}");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("  Server is running.  Press Ctrl+C to stop.");
                Console.WriteLine();

                var exit = new System.Threading.ManualResetEventSlim(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; exit.Set(); };
                exit.Wait();

                host.Close();
                service.Dispose();  // stops _sessionExpiryTimer and _clientOfflineTimer
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
