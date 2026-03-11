using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.WCF;

namespace SessionAdmin
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer refreshTimer;
        private ObservableCollection<ActiveSession> activeSessions;
        private ObservableCollection<ClientInfo> clients;
        private ObservableCollection<SecurityAlert> alerts;
        private string currentAdmin;
        private int currentAdminId;
        private SessionServiceClient serviceClient;

        public MainWindow()
        {
            InitializeComponent();
            InitializeData();
            InitializeServices();
            InitializeTimer();
        }

        private void InitializeData()
        {
            activeSessions = new ObservableCollection<ActiveSession>();
            clients = new ObservableCollection<ClientInfo>();
            alerts = new ObservableCollection<SecurityAlert>();

            dgActiveSessions.ItemsSource = activeSessions;
            dgClients.ItemsSource = clients;
            dgAlerts.ItemsSource = alerts;

            // Set default date range for reports
            dpFromDate.SelectedDate = DateTime.Today.AddMonths(-1);
            dpToDate.SelectedDate = DateTime.Today;
        }

        private void InitializeServices()
        {
            try
            {
                // Initialize WCF service client
                serviceClient = new SessionServiceClient();

                // Connect to server
                if (!serviceClient.Connect())
                {
                    MessageBox.Show("Unable to connect to server. Please check network connection and try again.",
                                  "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeTimer()
        {
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromSeconds(5);
            refreshTimer.Tick += RefreshTimer_Tick;
        }

        private void btnAdminLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtAdminUsername.Text.Trim();
            string password = txtAdminPassword.Password;

            lblAdminLoginError.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                lblAdminLoginError.Text = "Please enter both username and password";
                lblAdminLoginError.Visibility = Visibility.Visible;
                return;
            }

            // Disable button during authentication
            btnAdminLogin.IsEnabled = false;
            btnAdminLogin.Content = "Authenticating...";

            try
            {
                // UC-09: Login to Admin Panel
                var response = serviceClient.AuthenticateUser(username, password, "ADMIN");

                if (response.IsAuthenticated)
                {
                    // Verify user is admin
                    if (response.UserType != "Admin")
                    {
                        lblAdminLoginError.Text = "Access denied. Admin privileges required.";
                        lblAdminLoginError.Visibility = Visibility.Visible;
                        return;
                    }

                    currentAdmin = response.Username;
                    currentAdminId = response.UserId;
                    lblAdminUser.Text = $"Admin: {username}";

                    ShowDashboard();
                    LoadInitialData();
                    refreshTimer.Start();
                }
                else
                {
                    lblAdminLoginError.Text = response.ErrorMessage ?? "Invalid admin credentials";
                    lblAdminLoginError.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                lblAdminLoginError.Text = "Connection error. Please check server connection.";
                lblAdminLoginError.Visibility = Visibility.Visible;
                MessageBox.Show($"Authentication error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnAdminLogin.IsEnabled = true;
                btnAdminLogin.Content = "Login";
            }
        }

        private void ShowDashboard()
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
        }

        private void LoadInitialData()
        {
            try
            {
                // Load all data from server
                LoadClients();
                LoadActiveSessions();
                LoadAlerts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadClients()
        {
            clients.Clear();

            try
            {
                // UC-11: View Client List & Status
                var clientList = serviceClient.GetAllClients();

                foreach (var client in clientList)
                {
                    clients.Add(new ClientInfo
                    {
                        ClientId = client.ClientCode,
                        MachineName = client.MachineName,
                        IpAddress = client.IpAddress,
                        Status = client.Status,
                        CurrentUser = client.CurrentUser ?? "-",
                        LastActive = client.LastActiveTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading clients: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadActiveSessions()
        {
            activeSessions.Clear();

            try
            {
                // UC-10: Monitor Active Sessions
                var sessions = serviceClient.GetActiveSessions();

                foreach (var session in sessions)
                {
                    activeSessions.Add(new ActiveSession
                    {
                        ClientId = session.ClientCode,
                        Username = session.Username,
                        StartTime = session.StartTime.ToString("HH:mm:ss"),
                        Duration = $"{session.SelectedDuration} min",
                        RemainingTime = $"{session.RemainingMinutes}:00",
                        CurrentBilling = $"${session.CurrentBilling:F2}",
                        Status = session.SessionStatus
                    });
                }

                lblActiveCount.Text = activeSessions.Count.ToString();
                lblLastUpdate.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading active sessions: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAlerts()
        {
            alerts.Clear();

            try
            {
                // UC-17: Receive Security Alerts
                var alertList = serviceClient.GetUnacknowledgedAlerts();

                foreach (var alert in alertList)
                {
                    alerts.Add(new SecurityAlert
                    {
                        Timestamp = alert.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        ClientId = alert.ClientCode ?? "N/A",
                        Username = alert.Username ?? "N/A",
                        AlertType = alert.AlertType,
                        Description = alert.Description
                    });
                }

                lblAlertCount.Text = alerts.Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading alerts: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // Auto-refresh active sessions and alerts
            try
            {
                LoadActiveSessions();
                LoadAlerts();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-refresh error: {ex.Message}");
            }
        }

        private void btnRefreshSessions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadActiveSessions();
                LoadClients();
                MessageBox.Show("Data refreshed successfully", "Refresh",
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Refresh error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnTerminateSession_Click(object sender, RoutedEventArgs e)
        {
            // UC-14: Terminate Session Manually
            var button = sender as Button;
            var session = button?.DataContext as ActiveSession;

            if (session == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to terminate session for {session.Username}?",
                "Confirm Termination",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Get session info to find SessionId
                    var sessions = serviceClient.GetActiveSessions();
                    var targetSession = sessions.FirstOrDefault(s => s.Username == session.Username);

                    if (targetSession != null)
                    {
                        // Terminate session on server
                        bool success = serviceClient.EndSession(targetSession.SessionId, "Admin");

                        if (success)
                        {
                            // Remove from UI
                            activeSessions.Remove(session);
                            lblActiveCount.Text = activeSessions.Count.ToString();

                            MessageBox.Show($"Session for {session.Username} has been terminated successfully",
                                          "Session Terminated",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Information);

                            // Refresh data
                            LoadActiveSessions();
                        }
                        else
                        {
                            MessageBox.Show("Failed to terminate session. Please try again.",
                                          "Error",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error terminating session: {ex.Message}",
                                  "Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
            }
        }

        private void btnAcknowledgeAlert_Click(object sender, RoutedEventArgs e)
        {
            // UC-17: Receive Security Alerts (Acknowledge)
            var button = sender as Button;
            var alert = button?.DataContext as SecurityAlert;

            if (alert == null) return;

            try
            {
                // Get alert info to find AlertId
                var alertList = serviceClient.GetUnacknowledgedAlerts();
                var targetAlert = alertList.FirstOrDefault(a =>
                    a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") == alert.Timestamp &&
                    a.Username == alert.Username);

                if (targetAlert != null)
                {
                    // Acknowledge on server
                    bool success = serviceClient.AcknowledgeAlert(targetAlert.AlertId, currentAdminId);

                    if (success)
                    {
                        alerts.Remove(alert);
                        lblAlertCount.Text = alerts.Count.ToString();

                        MessageBox.Show("Alert acknowledged successfully", "Alert",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to acknowledge alert", "Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error acknowledging alert: {ex.Message}",
                              "Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private void btnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            // UC-18: Generate Reports
            if (dpFromDate.SelectedDate == null || dpToDate.SelectedDate == null)
            {
                MessageBox.Show("Please select both from and to dates", "Invalid Date Range",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string reportType = (cboReportType.SelectedItem as ComboBoxItem)?.Content.ToString();
            DateTime fromDate = dpFromDate.SelectedDate.Value;
            DateTime toDate = dpToDate.SelectedDate.Value;

            if (fromDate > toDate)
            {
                MessageBox.Show("From date cannot be after To date", "Invalid Date Range",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Generate report from server data
                GenerateReport(reportType, fromDate, toDate);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating report: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateReport(string reportType, DateTime fromDate, DateTime toDate)
        {
            try
            {
                // Get report data from server
                var reportData = serviceClient.GetSessionReport(fromDate, toDate);

                string report = $"=== {reportType} Report ===\n";
                report += $"Period: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}\n";
                report += $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
                report += $"Generated By: {currentAdmin}\n";
                report += new string('=', 60) + "\n\n";

                if (reportData != null)
                {
                    switch (reportType)
                    {
                        case "Session Usage":
                            report += GenerateUsageReport(reportData);
                            break;
                        case "Billing Summary":
                            report += GenerateBillingReport(reportData);
                            break;
                        case "Security Alerts":
                            report += GenerateAlertsReport(fromDate, toDate);
                            break;
                    }
                }
                else
                {
                    report += "No data available for the selected period.\n";
                }

                txtReportOutput.Text = report;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating report: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateUsageReport(ReportData data)
        {
            string report = $"Total Sessions: {data.TotalSessions}\n";
            report += $"Total Usage Hours: {data.TotalHours:F2}\n";
            report += $"Average Session Duration: {(data.TotalSessions > 0 ? data.TotalHours / data.TotalSessions : 0):F2} hours\n";
            report += $"Total Revenue: ${data.TotalRevenue:F2}\n";
            report += $"Average Revenue per Session: ${(data.TotalSessions > 0 ? data.TotalRevenue / data.TotalSessions : 0):F2}\n\n";

            report += "Session Details:\n";
            report += new string('-', 60) + "\n";

            if (data.Sessions != null && data.Sessions.Length > 0)
            {
                foreach (var session in data.Sessions)
                {
                    report += $"User: {session.Username,-15} Client: {session.ClientCode,-10} ";
                    report += $"Duration: {session.SelectedDuration,3} min  ";
                    report += $"Billing: ${session.CurrentBilling:F2}\n";
                }
            }
            else
            {
                report += "No sessions found in the selected period.\n";
            }

            return report;
        }

        private string GenerateBillingReport(ReportData data)
        {
            string report = $"Total Revenue: ${data.TotalRevenue:F2}\n";
            report += $"Sessions Billed: {data.TotalSessions}\n";
            report += $"Average Billing per Session: ${(data.TotalSessions > 0 ? data.TotalRevenue / data.TotalSessions : 0):F2}\n";
            report += $"Total Hours Billed: {data.TotalHours:F2}\n";

            // Get current rate
            decimal currentRate = serviceClient.GetCurrentBillingRate();
            report += $"Current Rate: ${currentRate:F2} per minute\n\n";

            report += "Billing Breakdown:\n";
            report += new string('-', 60) + "\n";

            if (data.Sessions != null && data.Sessions.Length > 0)
            {
                foreach (var session in data.Sessions)
                {
                    report += $"{session.StartTime:yyyy-MM-dd HH:mm} | ";
                    report += $"{session.Username,-15} | ";
                    report += $"{session.SelectedDuration,3} min | ";
                    report += $"${session.CurrentBilling,7:F2}\n";
                }
            }

            return report;
        }

        private string GenerateAlertsReport(DateTime fromDate, DateTime toDate)
        {
            try
            {
                // Get all alerts (including acknowledged ones) for the period
                // Note: This would require a new service method to get alerts by date range
                // For now, we'll get unacknowledged alerts only
                var alertList = serviceClient.GetUnacknowledgedAlerts();

                var filteredAlerts = alertList.Where(a =>
                    a.Timestamp >= fromDate && a.Timestamp <= toDate.AddDays(1)).ToArray();

                string report = $"Total Alerts: {filteredAlerts.Length}\n\n";

                // Group by alert type
                var groupedAlerts = filteredAlerts.GroupBy(a => a.AlertType);

                report += "Alert Type Summary:\n";
                report += new string('-', 60) + "\n";

                foreach (var group in groupedAlerts)
                {
                    report += $"{group.Key}: {group.Count()}\n";
                }

                report += "\n\nAlert Details:\n";
                report += new string('-', 60) + "\n";

                foreach (var alert in filteredAlerts.OrderByDescending(a => a.Timestamp))
                {
                    report += $"{alert.Timestamp:yyyy-MM-dd HH:mm:ss} | ";
                    report += $"{alert.ClientCode ?? "N/A",-10} | ";
                    report += $"{alert.Username ?? "N/A",-15} | ";
                    report += $"{alert.AlertType}\n";
                    report += $"  Description: {alert.Description}\n\n";
                }

                return report;
            }
            catch (Exception ex)
            {
                return $"Error generating alerts report: {ex.Message}\n";
            }
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout",
                                       MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                refreshTimer.Stop();
                DashboardPanel.Visibility = Visibility.Collapsed;
                LoginPanel.Visibility = Visibility.Visible;

                txtAdminUsername.Clear();
                txtAdminPassword.Clear();
                currentAdmin = null;

                activeSessions.Clear();
                alerts.Clear();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            refreshTimer?.Stop();
            base.OnClosing(e);
        }
    }

    // Data Models
    public class ActiveSession : System.ComponentModel.INotifyPropertyChanged
    {
        private string remainingTime;
        private string status;

        public string ClientId { get; set; }
        public string Username { get; set; }
        public string StartTime { get; set; }
        public string Duration { get; set; }

        public string RemainingTime
        {
            get => remainingTime;
            set
            {
                remainingTime = value;
                OnPropertyChanged(nameof(RemainingTime));
            }
        }

        public string CurrentBilling { get; set; }

        public string Status
        {
            get => status;
            set
            {
                status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class ClientInfo
    {
        public string ClientId { get; set; }
        public string MachineName { get; set; }
        public string IpAddress { get; set; }
        public string Status { get; set; }
        public string CurrentUser { get; set; }
        public string LastActive { get; set; }
    }

    public class SecurityAlert
    {
        public string Timestamp { get; set; }
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string AlertType { get; set; }
        public string Description { get; set; }
    }
}