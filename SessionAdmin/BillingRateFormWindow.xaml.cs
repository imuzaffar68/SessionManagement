using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SessionAdmin
{
    public partial class BillingRateFormWindow : Window
    {
        public string RateName { get; private set; }
        public decimal RatePerMinute { get; private set; }
        public string Currency { get; private set; }
        public DateTime? EffectiveFrom { get; private set; }
        public DateTime? EffectiveTo { get; private set; }
        public new bool IsActive { get; private set; }
        public bool IsDefault { get; private set; }
        public string Notes { get; private set; }

        // onSave: receives this window, returns null on success or an error message.
        private readonly Func<BillingRateFormWindow, string> _onSave;
        private readonly Action<string>                      _onToast;

        // Add mode
        public BillingRateFormWindow(Func<BillingRateFormWindow, string> onSave,
                                     Action<string> onToast = null)
        {
            InitializeComponent();
            _onSave  = onSave;
            _onToast = onToast;
            lblFormTitle.Text    = "Add Billing Rate";
            lblFormSubtitle.Text = "Configure a new billing rate";
            btnSubmit.Content    = "+ Add Rate";
            chkIsActive.IsChecked = true;
        }

        // Edit mode
        public BillingRateFormWindow(BillingRateVM rate,
                                     Func<BillingRateFormWindow, string> onSave,
                                     Action<string> onToast = null)
        {
            InitializeComponent();
            _onSave  = onSave;
            _onToast = onToast;
            lblFormTitle.Text    = "Edit Billing Rate";
            lblFormSubtitle.Text = $"Editing: {rate.Name}";
            btnSubmit.Content    = "↑ Update Rate";

            txtRateName.Text = rate.Name;
            txtRatePerMinute.Text = rate.RatePerMinute.ToString();
            cboCurrency.SelectedItem = cboCurrency.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => x.Content?.ToString() == rate.Currency)
                ?? cboCurrency.Items[0];
            dpEffectiveFrom.SelectedDate = rate.EffectiveFrom;
            dpEffectiveTo.SelectedDate   = rate.EffectiveTo;
            chkIsActive.IsChecked        = rate.IsActive  == 1;
            chkIsDefault.IsChecked       = rate.IsDefault == 1;
            txtNotes.Text                = rate.Notes ?? "";
        }

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            // Clear all inline and banner errors
            lblErrorBorder.Visibility  = Visibility.Collapsed;
            errRateName.Visibility     = Visibility.Collapsed;
            errRatePerMinute.Visibility = Visibility.Collapsed;
            errEffectiveFrom.Visibility = Visibility.Collapsed;

            bool valid = true;

            if (string.IsNullOrWhiteSpace(txtRateName.Text))
            { errRateName.Text = "Rate name is required."; errRateName.Visibility = Visibility.Visible; valid = false; }

            decimal rateVal = 0;
            if (!decimal.TryParse(txtRatePerMinute.Text, out rateVal) || rateVal < 0)
            { errRatePerMinute.Text = "Enter a valid positive number."; errRatePerMinute.Visibility = Visibility.Visible; valid = false; }

            if (!dpEffectiveFrom.SelectedDate.HasValue)
            { errEffectiveFrom.Text = "Effective From date is required."; errEffectiveFrom.Visibility = Visibility.Visible; valid = false; }

            if (dpEffectiveTo.SelectedDate.HasValue && dpEffectiveFrom.SelectedDate.HasValue &&
                dpEffectiveTo.SelectedDate.Value < dpEffectiveFrom.SelectedDate.Value)
            { ShowError("Effective To must be on or after Effective From."); valid = false; }

            if (!valid) return;

            // Collect output values
            RateName      = txtRateName.Text.Trim();
            RatePerMinute = rateVal;
            Currency      = (cboCurrency.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PKR";
            EffectiveFrom = dpEffectiveFrom.SelectedDate;
            EffectiveTo   = dpEffectiveTo.SelectedDate;
            IsActive      = chkIsActive.IsChecked  ?? true;
            IsDefault     = chkIsDefault.IsChecked ?? false;
            Notes         = txtNotes.Text.Trim();

            try
            {
                string error = _onSave?.Invoke(this);
                if (error != null) { ShowError(error); return; }
            }
            catch (Exception ex) { ShowError($"Server error: {ex.Message}"); return; }

            _onToast?.Invoke($"Billing rate '{RateName}' saved successfully.");
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
