using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SessionManagement.Client;

namespace SessionAdmin
{
    public partial class SplashWindow : Window
    {
        private SessionServiceClient _svc;
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

            bool connected = false;
            Exception error   = null;

            try
            {
                _svc = new SessionServiceClient();
                connected = await Task.Run(() => _svc.Connect());
            }
            catch (Exception ex)
            {
                error = ex;
            }

            _retrying = false;

            if (connected)
            {
                SetStatus("Connected — launching admin console…");
                await Task.Delay(350);

                var main = new MainWindow(_svc);
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

        private void btnClose_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S &&
                Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
            {
                e.Handled = true;
                var pin = new AdminPinWindow { Owner = this };
                if (pin.ShowDialog() == true)
                    new AdminSettingsWindow { Owner = this }.ShowDialog();
            }
        }
    }
}
