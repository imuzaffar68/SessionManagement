using System;
using System.Configuration;
using System.Windows;
using System.Windows.Input;

namespace SessionClient
{
    public partial class AdminSettingsWindow : Window
    {
        public AdminSettingsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            txtServerAddress.Text = ConfigurationManager.AppSettings["ServerAddress"] ?? "localhost";
            txtServerPort.Text    = ConfigurationManager.AppSettings["ServerPort"]    ?? "8001";
            txtServerAddress.Focus();
            txtServerAddress.SelectAll();
        }

        private async void btnSave_Click(object sender, RoutedEventArgs e)
        {
            string address = txtServerAddress.Text.Trim();
            string port    = txtServerPort.Text.Trim();

            if (string.IsNullOrEmpty(address))
            {
                lblStatus.Text      = "Server address cannot be empty.";
                lblStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                txtServerAddress.Focus();
                return;
            }

            if (!int.TryParse(port, out int portNum) || portNum < 1 || portNum > 65535)
            {
                lblStatus.Text      = "Port must be a number between 1 and 65535.";
                lblStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                txtServerPort.Focus();
                return;
            }

            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings["ServerAddress"].Value = address;
                config.AppSettings.Settings["ServerPort"].Value    = port;
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");

                lblStatus.Text      = "Saved. Restarting…";
                lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99));

                // Give the label time to render, then close and restart
                await System.Threading.Tasks.Task.Delay(600);
                Close();

                System.Diagnostics.Process.Start(
                    System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                lblStatus.Text      = $"Save failed: {ex.Message}";
                lblStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
