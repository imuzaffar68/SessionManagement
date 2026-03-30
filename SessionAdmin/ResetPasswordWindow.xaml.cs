using System.Windows;

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
            txtPassword.Password = "User@123456";
            txtPasswordPlain.Text = "User@123456";
            txtConfirmPassword.Password = "User@123456";
            txtConfirmPasswordPlain.Text = "User@123456";
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            string password = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;
            string confirmPassword = _confirmPasswordVisible ? txtConfirmPasswordPlain.Text : txtConfirmPassword.Password;

            if (string.IsNullOrWhiteSpace(password))
            {
                lblError.Text = "Password cannot be empty.";
                lblError.Visibility = Visibility.Visible;
                return;
            }

            if (password != confirmPassword)
            {
                lblError.Text = "Passwords do not match.";
                lblError.Visibility = Visibility.Visible;
                return;
            }

            if (password.Length < 8)
            {
                lblError.Text = "Password must be at least 8 characters long.";
                lblError.Visibility = Visibility.Visible;
                return;
            }

            // Check for uppercase, lowercase, and digits
            bool hasUpper = false, hasLower = false, hasDigit = false;
            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                if (char.IsLower(c)) hasLower = true;
                if (char.IsDigit(c)) hasDigit = true;
            }

            if (!hasUpper || !hasLower || !hasDigit)
            {
                lblError.Text = "Password must contain uppercase, lowercase, and digits.";
                lblError.Visibility = Visibility.Visible;
                return;
            }

            NewPassword = password;
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
