using System.Windows;

namespace SessionAdmin
{
    public partial class EditUserWindow : Window
    {
        public string FullName { get; private set; }
        public string Phone { get; private set; }
        public string Address { get; private set; }

        public EditUserWindow(UserVM user)
        {
            InitializeComponent();
            txtUsername.Text = user.Username;
            txtFullName.Text = user.FullName;
            txtPhone.Text = user.Phone;
            txtAddress.Text = user.Address;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            string fullName = txtFullName.Text.Trim();
            string phone = txtPhone.Text.Trim();
            string address = txtAddress.Text.Trim();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                lblError.Text = "Full Name is required.";
                lblError.Visibility = Visibility.Visible;
                return;
            }

            FullName = fullName;
            Phone = phone;
            Address = address;

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
