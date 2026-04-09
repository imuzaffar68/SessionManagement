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

        // Add mode
        public BillingRateFormWindow()
        {
            InitializeComponent();
            lblFormTitle.Text = "Add Billing Rate";
            lblFormSubtitle.Text = "Configure a new billing rate";
            btnSubmit.Content = "+ Add Rate";
            chkIsActive.IsChecked = true;
        }

        // Edit mode
        public BillingRateFormWindow(BillingRateVM rate)
        {
            InitializeComponent();
            lblFormTitle.Text = "Edit Billing Rate";
            lblFormSubtitle.Text = $"Editing: {rate.Name}";
            btnSubmit.Content = "↑ Update Rate";

            txtRateName.Text = rate.Name;
            txtRatePerMinute.Text = rate.RatePerMinute.ToString();
            cboCurrency.SelectedItem = cboCurrency.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => x.Content?.ToString() == rate.Currency)
                ?? cboCurrency.Items[0];
            dpEffectiveFrom.SelectedDate = rate.EffectiveFrom;
            dpEffectiveTo.SelectedDate = rate.EffectiveTo;
            chkIsActive.IsChecked = rate.IsActive == 1;
            chkIsDefault.IsChecked = rate.IsDefault == 1;
            txtNotes.Text = rate.Notes ?? "";
        }

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRateName.Text))
            { ShowError("Rate name is required."); return; }
            if (!decimal.TryParse(txtRatePerMinute.Text, out decimal rate) || rate < 0)
            { ShowError("Rate must be a valid positive number."); return; }

            RateName = txtRateName.Text.Trim();
            RatePerMinute = rate;
            Currency = (cboCurrency.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "USD";
            EffectiveFrom = dpEffectiveFrom.SelectedDate;
            EffectiveTo = dpEffectiveTo.SelectedDate;
            IsActive = chkIsActive.IsChecked ?? true;
            IsDefault = chkIsDefault.IsChecked ?? false;
            Notes = txtNotes.Text.Trim();

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
