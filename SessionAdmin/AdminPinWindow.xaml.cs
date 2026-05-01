using System.Configuration;
using System.Windows;
using System.Windows.Input;

namespace SessionAdmin
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
            string expected = ConfigurationManager.AppSettings["AdminSettingsPin"] ?? "1234";
            if (txtPin.Password == expected) { DialogResult = true; Close(); }
            else
            {
                // Silent clear — no "wrong PIN" feedback to prevent brute-force guessing.
                txtPin.Clear();
                txtPin.Focus();
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
