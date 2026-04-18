using System.Configuration;
using System.Windows;
using SessionManagement.UI;

namespace SessionClient
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            string displayName = ConfigurationManager.AppSettings["AppDisplayName"] ?? "NetCafé Session Client";
            ToastHelper.EnsureRegistered(ToastHelper.ClientAppId, displayName);
            var splash = new SplashWindow();
            MainWindow = splash;
            splash.Show();
        }
    }
}
