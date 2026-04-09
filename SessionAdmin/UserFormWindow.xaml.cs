using System.Windows;

namespace SessionAdmin
{
    public partial class UserFormWindow : Window
    {
        public string Username { get; private set; }
        public string FullName { get; private set; }
        public string Password { get; private set; }
        public string Phone { get; private set; }
        public string Address { get; private set; }

        private bool _passwordVisible;
        private bool _isEditMode;

        // Add mode
        public UserFormWindow()
        {
            InitializeComponent();
            lblFormTitle.Text = "Add Client User";
            lblFormSubtitle.Text = "Register a new client account";
            btnSubmit.Content = "+ Add User";
        }

        // Edit mode
        public UserFormWindow(UserVM user)
        {
            InitializeComponent();
            _isEditMode = true;
            lblFormTitle.Text = "Edit Client User";
            lblFormSubtitle.Text = $"Editing: {user.Username}";
            btnSubmit.Content = "↑ Save Changes";

            // Show read-only username, hide editable textbox
            txtUsername.Visibility = Visibility.Collapsed;
            pnlUsernameReadonly.Visibility = Visibility.Visible;
            lblUsernameReadonly.Text = user.Username;

            // Hide password section
            pnlPasswordSection.Visibility = Visibility.Collapsed;

            // Pre-fill editable fields
            txtFullName.Text = user.FullName ?? "";
            txtPhone.Text = user.Phone ?? "";
            txtAddress.Text = user.Address ?? "";
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
            if (!_isEditMode)
            {
                string user = txtUsername.Text.Trim();
                string pass = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;

                if (string.IsNullOrWhiteSpace(user))
                { ShowError("Username is required."); return; }
                if (string.IsNullOrEmpty(pass))
                { ShowError("Password is required."); return; }

                Username = user;
                Password = pass;
            }

            FullName = txtFullName.Text.Trim();
            Phone = txtPhone.Text.Trim();
            Address = txtAddress.Text.Trim();

            DialogResult = true;
            Close();
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

        private void ShowError(string msg)
        {
            lblError.Text = msg;
            lblErrorBorder.Visibility = Visibility.Visible;
        }
    }
}
