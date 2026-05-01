using System;
using System.Windows;
using System.Windows.Input;

namespace SessionAdmin
{
    public partial class EditMachineWindow : Window
    {
        private readonly Func<string, string, bool> _onSave;

        /// <summary>
        /// onSave delegate: (machineName, location) → true on success.
        /// Called inside the window so the form stays open on failure.
        /// </summary>
        public EditMachineWindow(ClientVM client, Func<string, string, bool> onSave)
        {
            InitializeComponent();
            _onSave            = onSave;
            lblTitle.Text      = $"Edit Machine — {client.ClientId}";
            txtMachineName.Text = client.MachineName ?? "";
            txtLocation.Text   = client.Location    ?? "";
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Clear previous errors
            errMachineName.Visibility = Visibility.Collapsed;
            lblErrorBorder.Visibility = Visibility.Collapsed;

            string name = txtMachineName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                errMachineName.Text       = "Machine name is required.";
                errMachineName.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                bool ok = _onSave(name, txtLocation.Text.Trim());
                if (ok)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    // SP returned 0 — machine not found or sp not yet deployed.
                    ShowError(
                        "Could not save changes.\n" +
                        "• Run SessionManagement.sql if sp_UpdateClientMachineInfo is missing.\n" +
                        "• Refresh the Clients list and try again if the machine was removed.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Server error: {ex.Message}");
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
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

        private void ShowError(string message)
        {
            lblError.Text             = message;
            lblErrorBorder.Visibility = Visibility.Visible;
        }
    }
}
