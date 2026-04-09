using System.Windows;

namespace SessionAdmin
{
    public partial class AddUserWindow : Window
    {
        public string Username { get; private set; }
        public string FullName { get; private set; }
        public string Password { get; private set; }
        public string Phone { get; private set; }
        public string Address { get; private set; }

        private bool _passwordVisible;

        public AddUserWindow()
        {
            InitializeComponent();
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

        private void btnGenerate_Click(object sender, RoutedEventArgs e)
        {
            string auto = "User@123456";
            txtPassword.Password = auto;
            if (_passwordVisible) txtPasswordPlain.Text = auto;
        }

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string user = txtUsername.Text.Trim();
            string pass = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;

            if (string.IsNullOrWhiteSpace(user))
            { ShowError("Username is required."); return; }
            if (string.IsNullOrEmpty(pass))
            { ShowError("Password is required."); return; }

            Username = user;
            FullName = txtFullName.Text.Trim();
            Password = pass;
            Phone = txtPhone.Text.Trim();
            Address = txtAddress.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            lblError.Text = msg;
            lblErrorBorder.Visibility = Visibility.Visible;
        }
    }
}
