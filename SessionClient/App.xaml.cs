using System.Windows;
using SessionManagement.UI;

namespace SessionClient
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            ToastHelper.EnsureRegistered(ToastHelper.ClientAppId, "NetCafé Session Client");
            var splash = new SplashWindow();
            MainWindow = splash;
            splash.Show();
        }
    }
}
