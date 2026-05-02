using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SessionAdmin
{
    public partial class ChangePasswordWindow : Window
    {
        // onSave: (currentPassword, newPassword) → null on success or error message.
        private readonly Func<string, string, string> _onSave;
        private readonly Action<string>               _onToast;

        private bool _currentVisible;
        private bool _newVisible;
        private bool _confirmVisible;

        public ChangePasswordWindow(string adminUsername,
            Func<string, string, string> onSave, Action<string> onToast = null)
        {
            InitializeComponent();
            _onSave  = onSave;
            _onToast = onToast;
            lblAdminName.Text = adminUsername;
            Loaded += (_, __) =>
            {
                HookPasswordPlaceholder(txtCurrentPassword);
                HookPasswordPlaceholder(txtNewPassword);
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
                ph.Visibility = box.Password.Length > 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
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

        private void btnShowCurrent_Click(object sender, RoutedEventArgs e)
            => ToggleVisibility(ref _currentVisible, txtCurrentPassword, txtCurrentPasswordPlain, btnShowCurrent);

        private void btnShowNew_Click(object sender, RoutedEventArgs e)
            => ToggleVisibility(ref _newVisible, txtNewPassword, txtNewPasswordPlain, btnShowNew);

        private void btnShowConfirm_Click(object sender, RoutedEventArgs e)
            => ToggleVisibility(ref _confirmVisible, txtConfirmPassword, txtConfirmPasswordPlain, btnShowConfirm);

        private static void ToggleVisibility(ref bool visible,
            PasswordBox pb, TextBox tb, Button btn)
        {
            visible = !visible;
            if (visible)
            {
                tb.Text = pb.Password;
                pb.Visibility = Visibility.Collapsed;
                tb.Visibility = Visibility.Visible;
                btn.Content = "🙈";
            }
            else
            {
                pb.Password = tb.Text;
                tb.Visibility = Visibility.Collapsed;
                pb.Visibility = Visibility.Visible;
                btn.Content = "👁";
            }
        }

        private void btnChange_Click(object sender, RoutedEventArgs e)
        {
            lblErrorBorder.Visibility = Visibility.Collapsed;
            errCurrent.Visibility     = Visibility.Collapsed;
            errNew.Visibility         = Visibility.Collapsed;
            errConfirm.Visibility     = Visibility.Collapsed;

            string current = _currentVisible ? txtCurrentPasswordPlain.Text : txtCurrentPassword.Password;
            string newPwd  = _newVisible     ? txtNewPasswordPlain.Text     : txtNewPassword.Password;
            string confirm = _confirmVisible ? txtConfirmPasswordPlain.Text  : txtConfirmPassword.Password;

            bool valid = true;

            if (string.IsNullOrWhiteSpace(current))
            { errCurrent.Text = "Current password is required."; errCurrent.Visibility = Visibility.Visible; valid = false; }

            if (string.IsNullOrWhiteSpace(newPwd))
            { errNew.Text = "New password is required."; errNew.Visibility = Visibility.Visible; valid = false; }
            else if (newPwd.Length < 8)
            { errNew.Text = "Must be at least 8 characters."; errNew.Visibility = Visibility.Visible; valid = false; }
            else
            {
                bool hasUpper = false, hasLower = false, hasDigit = false;
                foreach (char c in newPwd)
                {
                    if (char.IsUpper(c)) hasUpper = true;
                    if (char.IsLower(c)) hasLower = true;
                    if (char.IsDigit(c)) hasDigit = true;
                }
                if (!hasUpper || !hasLower || !hasDigit)
                { errNew.Text = "Must contain uppercase, lowercase, and a digit."; errNew.Visibility = Visibility.Visible; valid = false; }
            }

            if (!string.IsNullOrWhiteSpace(newPwd) && newPwd != confirm)
            { errConfirm.Text = "Passwords do not match."; errConfirm.Visibility = Visibility.Visible; valid = false; }

            if (!valid) return;

            try
            {
                string error = _onSave?.Invoke(current, newPwd);
                if (error != null) { ShowError(error); return; }
            }
            catch (Exception ex) { ShowError($"Server error: {ex.Message}"); return; }

            _onToast?.Invoke("Admin password changed successfully.");
            DialogResult = true;
            Close();
        }

        private void ShowError(string msg)
        {
            lblError.Text             = msg;
            lblErrorBorder.Visibility = Visibility.Visible;
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }

        private void btnWindowClose_Click(object sender, RoutedEventArgs e)
        { DialogResult = false; Close(); }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        { DialogResult = false; Close(); }
    }
}
