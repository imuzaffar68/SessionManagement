using System;
using System.Configuration;
using System.ServiceModel;
using System.ServiceModel.Description;
using SessionManagement.Security;
using SessionManagement.WCF;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            string connString = ConfigurationManager.ConnectionStrings["SessionManagementDB"]?.ConnectionString
                          ?? throw new ConfigurationErrorsException("Missing SessionManagementDB");

            var baseAddress = new Uri("net.tcp://localhost:8001/SessionService");
            using (var host = new ServiceHost(typeof(SessionService), baseAddress))
            {
                var binding = new NetTcpBinding(SecurityMode.None);
                binding.ReceiveTimeout = TimeSpan.FromMinutes(10);
                binding.SendTimeout = TimeSpan.FromMinutes(10);

                host.AddServiceEndpoint(typeof(ISessionService), binding, "");

                // Enable detailed exception information for debugging
                var debugBehavior = host.Description.Behaviors.Find<ServiceDebugBehavior>();
                if (debugBehavior == null)
                {
                    debugBehavior = new ServiceDebugBehavior();
                    host.Description.Behaviors.Add(debugBehavior);
                }
                debugBehavior.IncludeExceptionDetailInFaults = true;

                // Generate and verify password hashes for all test users
                //string passwordAdmin = AuthenticationHelper.HashPassword("Admin@123456");
                //string passwordUser1 = AuthenticationHelper.HashPassword("User1@123456");
                //string passwordUser2 = AuthenticationHelper.HashPassword("User2@123456");
                //string passwordUser3 = AuthenticationHelper.HashPassword("User3@123456");

                //Console.WriteLine("\n=== TEST USER CREDENTIALS ===");
                //Console.WriteLine($"admin:     Admin@123456");
                //Console.WriteLine($"user1:     User1@123456");
                //Console.WriteLine($"user2:     User2@123456");
                //Console.WriteLine($"user3:     User3@123456");
                //Console.WriteLine("\n=== PASSWORD HASHES (Use these in database UPDATE statements) ===");
                //Console.WriteLine($"UPDATE tblUser SET PasswordHash = '{passwordAdmin}' WHERE Username = 'admin';");
                //Console.WriteLine($"UPDATE tblUser SET PasswordHash = '{passwordUser1}' WHERE Username = 'user1';");
                //Console.WriteLine($"UPDATE tblUser SET PasswordHash = '{passwordUser2}' WHERE Username = 'user2';");
                //Console.WriteLine($"UPDATE tblUser SET PasswordHash = '{passwordUser3}' WHERE Username = 'user3';");
                //Console.WriteLine("=== VERIFICATION TEST ===");
                //Console.WriteLine($"admin password verify: {AuthenticationHelper.VerifyPassword("Admin@123456", passwordAdmin)}");
                //Console.WriteLine($"user1 password verify: {AuthenticationHelper.VerifyPassword("User1@123456", passwordUser1)}");
                //Console.WriteLine($"user2 password verify: {AuthenticationHelper.VerifyPassword("User2@123456", passwordUser2)}");
                //Console.WriteLine($"user3 password verify: {AuthenticationHelper.VerifyPassword("User3@123456", passwordUser3)}");
                //Console.WriteLine("=====================================\n");

                host.Open();
                Console.WriteLine("Session Management Service is running on net.tcp://localhost:8001/SessionService");
                //Console.WriteLine("passwordUser1" + passwordUser1);
                //Console.WriteLine("passwordUser2" + passwordUser2);
                //Console.WriteLine("passwordUser2" + passwordUser3);
                //Console.WriteLine("passwordAdmin" + passwordAdmin);
                Console.WriteLine("Press Enter to stop the service.");
                Console.ReadLine();
                host.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }
    }
}