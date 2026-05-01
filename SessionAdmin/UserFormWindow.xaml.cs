using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SessionAdmin
{
    public partial class UserFormWindow : Window
    {
        public string Username             { get; private set; }
        public string FullName             { get; private set; }
        public string Password             { get; private set; }
        public string Phone                { get; private set; }
        public string Address              { get; private set; }
        public string ProfilePictureBase64 { get; private set; }

        private bool _passwordVisible;
        private bool _isEditMode;

        // onSave: receives this window, returns null on success or an error message string.
        // onToast: called with a success message after successful save (before Close).
        private readonly Func<UserFormWindow, string> _onSave;
        private readonly Action<string>               _onToast;

        // Add mode
        public UserFormWindow(Func<UserFormWindow, string> onSave, Action<string> onToast = null)
        {
            InitializeComponent();
            _onSave  = onSave;
            _onToast = onToast;
            lblFormTitle.Text    = "Add Client User";
            lblFormSubtitle.Text = "Register a new client account";
            btnSubmit.Content    = "+ Add User";
            Loaded += (_, __) => HookPasswordPlaceholder(txtPassword);
        }

        // Edit mode
        public UserFormWindow(UserVM user, Func<UserFormWindow, string> onSave, Action<string> onToast = null)
        {
            InitializeComponent();
            _onSave     = onSave;
            _onToast    = onToast;
            _isEditMode = true;
            lblFormTitle.Text    = "Edit Client User";
            lblFormSubtitle.Text = $"Editing: {user.Username}";
            btnSubmit.Content    = "↑ Save Changes";

            // Show read-only username, hide editable textbox
            txtUsername.Visibility        = Visibility.Collapsed;
            pnlUsernameReadonly.Visibility = Visibility.Visible;
            lblUsernameReadonly.Text       = user.Username;

            // Hide password section and its inline error in edit mode
            pnlPasswordSection.Visibility = Visibility.Collapsed;

            // Pre-fill editable fields
            txtFullName.Text = user.FullName ?? "";
            txtPhone.Text    = user.Phone    ?? "";
            txtAddress.Text  = user.Address  ?? "";

            // Pre-fill profile picture if present
            if (!string.IsNullOrEmpty(user.ProfilePictureBase64))
            {
                ProfilePictureBase64 = user.ProfilePictureBase64;
                SetPhotoPreview(user.ProfilePictureBase64);
            }
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

        private void btnGenerate_Click(object sender, RoutedEventArgs e)
        {
            string auto = "User@123456";
            txtPassword.Password = auto;
            if (_passwordVisible) txtPasswordPlain.Text = auto;
        }

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            // Clear previous inline and banner errors before re-validating
            ClearErrors();

            bool valid = true;

            if (!_isEditMode)
            {
                if (string.IsNullOrWhiteSpace(txtUsername.Text))
                { SetFieldError(errUsername, "Username is required."); valid = false; }

                string pass = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;
                if (string.IsNullOrEmpty(pass))
                { SetFieldError(errPassword, "Password is required."); valid = false; }
                else
                    Password = pass;
            }

            if (string.IsNullOrWhiteSpace(txtFullName.Text))
            { SetFieldError(errFullName, "Full name is required."); valid = false; }

            if (!valid) return;

            // Collect all output values
            if (!_isEditMode) Username = txtUsername.Text.Trim();
            FullName = txtFullName.Text.Trim();
            Phone    = txtPhone.Text.Trim();
            Address  = txtAddress.Text.Trim();

            // Call the save delegate — null return means success
            try
            {
                string error = _onSave?.Invoke(this);
                if (error != null) { ShowError(error); return; }
            }
            catch (Exception ex) { ShowError($"Server error: {ex.Message}"); return; }

            _onToast?.Invoke(_isEditMode
                ? $"User '{Username}' updated successfully."
                : $"User '{Username}' registered successfully.");
            DialogResult = true;
            Close();
        }

        private void ClearErrors()
        {
            lblErrorBorder.Visibility = Visibility.Collapsed;
            errUsername.Visibility    = Visibility.Collapsed;
            errFullName.Visibility    = Visibility.Collapsed;
            errPassword.Visibility    = Visibility.Collapsed;
        }

        private static void SetFieldError(TextBlock lbl, string message)
        {
            lbl.Text       = message;
            lbl.Visibility = Visibility.Visible;
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

        private void btnUploadPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Title  = "Select Profile Photo"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                // Resize to 128×128 and encode as JPEG base64
                var src = new BitmapImage(new Uri(dlg.FileName));
                var scaled = new TransformedBitmap(src,
                    new ScaleTransform(128.0 / src.PixelWidth, 128.0 / src.PixelHeight));
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(scaled));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ProfilePictureBase64 = Convert.ToBase64String(ms.ToArray());
                }
                SetPhotoPreview(ProfilePictureBase64);
            }
            catch (Exception ex) { ShowError($"Failed to load photo: {ex.Message}"); }
        }

        private void SetPhotoPreview(string base64)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                imgPhotoPreview.Source         = bmp;
                imgPhotoPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch { /* keep placeholder */ }
        }

        private void ShowError(string msg)
        {
            lblError.Text = msg;
            lblErrorBorder.Visibility = Visibility.Visible;
        }
    }
}
