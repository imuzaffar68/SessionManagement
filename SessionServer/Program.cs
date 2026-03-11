using System;
using System.ServiceModel;
using SessionManagement.WCF;

class Program
{
    static void Main(string[] args)
    {
        using (ServiceHost host = new ServiceHost(typeof(SessionService)))
        {
            try
            {
                host.Open();
                Console.WriteLine("Session Management Service is running...");
                Console.WriteLine("Press Enter to stop the service.");
                Console.ReadLine();
                host.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.ReadLine();
            }
        }
    }
}