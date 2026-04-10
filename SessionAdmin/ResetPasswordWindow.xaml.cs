using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SessionAdmin
{
    public partial class ResetPasswordWindow : Window
    {
        public string NewPassword { get; private set; }
        private bool _passwordVisible;
        private bool _confirmPasswordVisible;

        public ResetPasswordWindow(string username)
        {
            InitializeComponent();
            txtUsername.Text = username;
            Loaded += (_, __) =>
            {
                HookPasswordPlaceholder(txtPassword);
                HookPasswordPlaceholder(txtConfirmPassword);
            };
        }

        private static void HookPasswordPlaceholder(PasswordBox pb)
        {
            pb.PasswordChanged += (s, _) =>
            {
                var box = (PasswordBox)s;
                var ph = FindVisualChild<TextBlock>(box, "Placeholder");
                if (ph == null) return;
                if (box.Password.Length > 0)
                    ph.Visibility = Visibility.Collapsed;
                else
                    ph.ClearValue(UIElement.VisibilityProperty);
            };
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name) return t;
                var found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void btnShowPassword_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            if (_passwordVisible)
            {
                txtPasswordPlain.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordPlain.Visibility = Visibility.Visible;
                btnShowPassword.Content = "🙈";
            }
            else
            {
                txtPassword.Password = txtPasswordPlain.Text;
                txtPasswordPlain.Visibility = Visibility.Collapsed;
                txtPassword.Visibility = Visibility.Visible;
                btnShowPassword.Content = "👁";
            }
        }

        private void btnShowConfirmPassword_Click(object sender, RoutedEventArgs e)
        {
            _confirmPasswordVisible = !_confirmPasswordVisible;
            if (_confirmPasswordVisible)
            {
                txtConfirmPasswordPlain.Text = txtConfirmPassword.Password;
                txtConfirmPassword.Visibility = Visibility.Collapsed;
                txtConfirmPasswordPlain.Visibility = Visibility.Visible;
                btnShowConfirmPassword.Content = "🙈";
            }
            else
            {
                txtConfirmPassword.Password = txtConfirmPasswordPlain.Text;
                txtConfirmPasswordPlain.Visibility = Visibility.Collapsed;
                txtConfirmPassword.Visibility = Visibility.Visible;
                btnShowConfirmPassword.Content = "👁";
            }
        }

        private void btnGenerate_Click(object sender, RoutedEventArgs e)
        {
            const string defaultPwd = "User@123456";
            txtPassword.Password = defaultPwd;
            txtPasswordPlain.Text = defaultPwd;
            txtConfirmPassword.Password = defaultPwd;
            txtConfirmPasswordPlain.Text = defaultPwd;
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            string password = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;
            string confirm = _confirmPasswordVisible ? txtConfirmPasswordPlain.Text : txtConfirmPassword.Password;

            lblErrorBorder.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(password))
            { ShowError("Password cannot be empty."); return; }

            if (password != confirm)
            { ShowError("Passwords do not match."); return; }

            if (password.Length < 8)
            { ShowError("Password must be at least 8 characters."); return; }

            bool hasUpper = false, hasLower = false, hasDigit = false;
            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                if (char.IsLower(c)) hasLower = true;
                if (char.IsDigit(c)) hasDigit = true;
            }

            if (!hasUpper || !hasLower || !hasDigit)
            { ShowError("Password must contain uppercase, lowercase, and a digit."); return; }

            NewPassword = password;
            DialogResult = true;
            Close();
        }

        private void ShowError(string msg)
        {
            lblError.Text = msg;
            lblErrorBorder.Visibility = Visibility.Visible;
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }

        private void btnWindowClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
