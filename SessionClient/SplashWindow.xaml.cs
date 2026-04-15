using System;
using System.Threading.Tasks;
using System.Windows;
using SessionManagement.Client;
using SessionManagement.Media;

namespace SessionClient
{
    public partial class SplashWindow : Window
    {
        private bool _retrying;

        public SplashWindow()
        {
            InitializeComponent();
            Loaded += async (_, __) => await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            if (_retrying) return;
            _retrying = true;

            btnRetry.Visibility = Visibility.Collapsed;
            SetStatus("Connecting to server…");

            SessionServiceClient svc = null;
            WebcamHelper         cam = null;
            bool connected           = false;
            Exception error          = null;

            try
            {
                svc       = new SessionServiceClient();
                connected = await Task.Run(() => svc.Connect());

                if (connected)
                {
                    SetStatus("Initializing webcam…");
                    cam = new WebcamHelper();
                    await Task.Delay(200);
                }
            }
            catch (Exception ex)
            {
                error     = ex;
                connected = false;
            }

            _retrying = false;

            if (connected)
            {
                SetStatus("Ready — launching…");
                await Task.Delay(300);

                var main = new MainWindow(svc, cam);
                Application.Current.MainWindow = main;
                main.Show();
                Close();
            }
            else
            {
                string detail = error?.Message ?? "Server unreachable.";
                SetStatus($"⚠  Connection failed — {detail}");
                btnRetry.Visibility = Visibility.Visible;
            }
        }

        private void SetStatus(string text) => lblStatus.Text = text;

        private async void btnRetry_Click(object sender, RoutedEventArgs e)
            => await ConnectAsync();
    }
}
