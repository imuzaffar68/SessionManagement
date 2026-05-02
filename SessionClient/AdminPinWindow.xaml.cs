using System.Configuration;
using System.Windows;
using System.Windows.Input;
using SessionManagement.Security;

namespace SessionClient
{
    public partial class AdminPinWindow : Window
    {
        public AdminPinWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => txtPin.Focus();
        }

        private void btnVerify_Click(object sender, RoutedEventArgs e) => Verify();

        private void txtPin_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Verify();
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        }

        private void Verify()
        {
            string stored  = ConfigurationManager.AppSettings["AdminSettingsPin"] ?? "1234";
            string entered = txtPin.Password;

            bool match = (stored.StartsWith("$2a$") || stored.StartsWith("$2b$"))
                ? AuthenticationHelper.VerifyPassword(entered, stored)
                : entered == stored;

            // Auto-upgrade plaintext PIN to BCrypt hash on first successful use.
            if (match && !stored.StartsWith("$2"))
            {
                try
                {
                    var cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    cfg.AppSettings.Settings["AdminSettingsPin"].Value =
                        AuthenticationHelper.HashPassword(entered);
                    cfg.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("appSettings");
                }
                catch { /* best-effort upgrade — plaintext fallback remains safe via OS ACL */ }
            }

            if (match) { DialogResult = true; Close(); }
            else
            {
                // Silent clear — no "wrong PIN" message so a kiosk user who stumbles on the
                // shortcut gets no feedback that this is a guessable entry point.
                txtPin.Clear();
                txtPin.Focus();
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
