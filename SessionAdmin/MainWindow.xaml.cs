using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.WCF;

namespace SessionAdmin
{
    public partial class MainWindow : Window
    {
        // ── State ─────────────────────────────────────────────────
        private SessionServiceClient _svc;
        private DispatcherTimer      _refreshTimer;

        private string _adminUsername;
        private int    _adminUserId;

        // Observable collections bound to DataGrids
        private ObservableCollection<ActiveSessionVM> _sessions = new ObservableCollection<ActiveSessionVM>();
        private ObservableCollection<ClientVM> _clients = new ObservableCollection<ClientVM>();
        private ObservableCollection<AlertVM> _alerts = new ObservableCollection<AlertVM>();
        private ObservableCollection<LogVM> _logs = new ObservableCollection<LogVM>();

        // ─────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            dgActiveSessions.ItemsSource = _sessions;
            dgClients.ItemsSource        = _clients;
            dgAlerts.ItemsSource         = _alerts;
            dgLogs.ItemsSource           = _logs;

            dpFromDate.SelectedDate = DateTime.Today.AddMonths(-1);
            dpToDate.SelectedDate   = DateTime.Today;
            dpLogFrom.SelectedDate  = DateTime.Today;
            dpLogTo.SelectedDate    = DateTime.Today;

            _refreshTimer          = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(5);   // NFR-02: ≤ 2 s practical bound
            _refreshTimer.Tick    += (_, __) => AutoRefresh();

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _svc = new SessionServiceClient();
                _svc.ServerMessage += OnServerMessage;  // FR-14: real-time alerts

                if (!_svc.Connect())
                    MessageBox.Show("Cannot connect to server.",
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Init error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-09  —  ADMIN LOGIN
        //  SEQ-09: admin enters creds → AuthenticateUser (Role=Admin) → dashboard
        // ═══════════════════════════════════════════════════════════

        private bool _adminPasswordVisible;

        private void btnShowAdminPassword_Click(object sender, RoutedEventArgs e)
        {
            _adminPasswordVisible = !_adminPasswordVisible;
            if (_adminPasswordVisible)
            {
                txtAdminPasswordPlain.Text       = txtAdminPassword.Password;
                txtAdminPassword.Visibility     = Visibility.Collapsed;
                txtAdminPasswordPlain.Visibility= Visibility.Visible;
                btnShowAdminPassword.Content    = "🙈";
            }
            else
            {
                txtAdminPassword.Password       = txtAdminPasswordPlain.Text;
                txtAdminPasswordPlain.Visibility= Visibility.Collapsed;
                txtAdminPassword.Visibility     = Visibility.Visible;
                btnShowAdminPassword.Content    = "👁";
            }
        }

        private void btnAdminLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtAdminUsername.Text.Trim();
            string pass = _adminPasswordVisible
                          ? txtAdminPasswordPlain.Text
                          : txtAdminPassword.Password;

            lblAdminLoginError.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                ShowLoginError("Please enter both username and password.");
                return;
            }

            btnAdminLogin.IsEnabled = false;
            btnAdminLogin.Content   = "Authenticating…";

            try
            {
                // SEQ-09 step 2: send to server (same AuthenticateUser, Role checked below)
                var resp = _svc.AuthenticateUser(user, pass, "ADMIN");

                if (!resp.IsAuthenticated)
                {
                    ShowLoginError(resp.ErrorMessage ?? "Invalid credentials.");
                    return;
                }

                // SEQ-09 step 3: verify role
                if (resp.UserType != "Admin")
                {
                    ShowLoginError("Access denied. Admin privileges required.");
                    return;
                }

                _adminUsername      = resp.Username;
                _adminUserId        = resp.UserId;
                lblAdminUser.Text   = $"Admin: {_adminUsername}";

                // SEQ-09 step 4: subscribe for real-time push (FR-14)
                _svc.SubscribeForNotifications("ADMIN_" + _adminUserId);

                ShowDashboard();
                LoadAll();
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                ShowLoginError("Connection error.");
                System.Diagnostics.Debug.WriteLine($"[AdminLogin] {ex.Message}");
            }
            finally
            {
                btnAdminLogin.IsEnabled = true;
                btnAdminLogin.Content   = "Login";
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-10  —  MONITOR ACTIVE SESSIONS
        //  SEQ-10: admin requests active sessions → server queries → display
        // ═══════════════════════════════════════════════════════════

        private void LoadActiveSessions()
        {
            try
            {
                _sessions.Clear();
                var list = _svc.GetActiveSessions();

                foreach (var s in list)
                {
                    _sessions.Add(new ActiveSessionVM
                    {
                        SessionId      = s.SessionId,
                        ClientId       = s.ClientCode,
                        Username       = s.Username,
                        StartTime      = s.StartTime.ToString("HH:mm:ss"),
                        Duration       = $"{s.SelectedDuration} min",
                        RemainingTime  = $"{s.RemainingMinutes} min",
                        CurrentBilling = $"${s.CurrentBilling:F2}",
                        Status         = s.SessionStatus
                    });
                }

                lblActiveCount.Text = _sessions.Count.ToString();
                lblLastUpdate.Text  = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadSessions] {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-11  —  VIEW CLIENT LIST & STATUS
        //  SEQ-11: admin requests client list → server queries tblClientMachine → display
        // ═══════════════════════════════════════════════════════════

        private void LoadClients()
        {
            try
            {
                _clients.Clear();
                foreach (var c in _svc.GetAllClients())
                {
                    _clients.Add(new ClientVM
                    {
                        ClientId    = c.ClientCode,
                        MachineName = c.MachineName,
                        IpAddress   = c.IpAddress,
                        Status      = c.Status,
                        CurrentUser = c.CurrentUser ?? "—",
                        LastActive  = c.LastActiveTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadClients] {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-12  —  VIEW CAPTURED USER IMAGES
        //  SEQ-12: admin selects session → DownloadLoginImage → display
        // ═══════════════════════════════════════════════════════════

        private void btnViewImage_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgActiveSessions.SelectedItem as ActiveSessionVM;
            if (selected == null)
            {
                MessageBox.Show("Please select a session first.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // SEQ-12 step 2: request image from server
                string b64 = _svc.DownloadLoginImage(selected.SessionId);

                if (string.IsNullOrEmpty(b64))
                {
                    MessageBox.Show("No image available for this session.",
                        "No Image", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // SEQ-12 step 3: decode and display
                byte[] bytes = Convert.FromBase64String(b64);
                using (var ms = new MemoryStream(bytes))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption  = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgSessionPhoto.Source = bmp;
                }

                lblImageTitle.Text    = $"Login Image — Session {selected.SessionId} | {selected.Username}";
                ImageViewerPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCloseImage_Click(object sender, RoutedEventArgs e)
        {
            ImageViewerPanel.Visibility = Visibility.Collapsed;
            imgSessionPhoto.Source      = null;
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-13  —  CALCULATE / VIEW BILLING
        //  Billing is displayed live in the Active Sessions grid.
        //  Finalized billing is shown in the Reports tab.
        // ═══════════════════════════════════════════════════════════
        // (Live billing comes from sp_GetActiveSessions.CurrentBilling — already shown
        //  in dgActiveSessions.CurrentBilling column. Nothing extra needed here.)

        // ═══════════════════════════════════════════════════════════
        //  UC-14  —  TERMINATE SESSION MANUALLY
        //  SEQ-14: admin clicks Terminate → EndSession("Admin") → callback to client
        // ═══════════════════════════════════════════════════════════

        private void btnTerminateSession_Click(object sender, RoutedEventArgs e)
        {
            var btn     = sender as Button;
            var session = btn?.DataContext as ActiveSessionVM;
            if (session == null) return;

            var r = MessageBox.Show(
                $"Terminate session {session.SessionId} for user '{session.Username}'?",
                "Confirm Termination", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                // SEQ-14 step 2: call EndSession with terminationType "Admin"
                bool ok = _svc.EndSession(session.SessionId, "Admin");

                if (ok)
                {
                    // SEQ-14 step 3: remove from UI immediately
                    _sessions.Remove(session);
                    lblActiveCount.Text = _sessions.Count.ToString();

                    MessageBox.Show(
                        $"Session {session.SessionId} terminated successfully.",
                        "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to terminate session. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-15  —  VIEW SESSION LOGS / HISTORY
        //  SEQ-15: admin sets filter → DB queries tblSystemLog → display
        // ═══════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════
        //  REPLACE the btnLoadLogs_Click method in
        //  SessionAdmin/MainWindow.xaml.cs with this version.
        //
        //  UC-15 (View Session Logs / History) — SEQ-15 complete.
        //  Now calls GetSystemLogs() which queries tblSystemLog directly.
        // ═══════════════════════════════════════════════════════════

        private void btnLoadLogs_Click(object sender, RoutedEventArgs e)
        {
            if (dpLogFrom.SelectedDate == null || dpLogTo.SelectedDate == null)
            {
                MessageBox.Show("Please select both From and To dates.",
                    "Date Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime from = dpLogFrom.SelectedDate.Value;
            DateTime to = dpLogTo.SelectedDate.Value;

            if (from > to)
            {
                MessageBox.Show("From date cannot be after To date.",
                    "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Resolve category — "All" item = no filter
            string cat = (cboLogCategory.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (cat == "All") cat = null;

            try
            {
                // SEQ-15 step 2: call server → tblSystemLog query
                var logs = _svc.GetSystemLogs(from, to, cat);

                _logs.Clear();

                if (logs.Length == 0)
                {
                    MessageBox.Show("No log records found for the selected period.",
                        "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var log in logs)
                {
                    _logs.Add(new LogVM
                    {
                        LogTime = log.LoggedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Category = log.Category,
                        LogType = log.Type,
                        Source = log.Source ?? "—",
                        ClientCode = log.ClientCode ?? "—",
                        Username = log.Username ?? "—",
                        Message = log.Message
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading logs: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-17  —  RECEIVE & ACKNOWLEDGE SECURITY ALERTS
        //  SEQ-17: alert arrives via WCF callback → shown in Alerts tab
        //          admin clicks Acknowledge → AcknowledgeAlert → DB update
        // ═══════════════════════════════════════════════════════════

        private void LoadAlerts()
        {
            try
            {
                _alerts.Clear();
                foreach (var a in _svc.GetUnacknowledgedAlerts())
                {
                    _alerts.Add(new AlertVM
                    {
                        AlertId     = a.AlertId,
                        Timestamp   = a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        Severity    = a.Severity,
                        ClientId    = a.ClientCode ?? "—",
                        Username    = a.Username   ?? "—",
                        AlertType   = a.AlertType,
                        Description = a.Description
                    });
                }
                lblAlertCount.Text = _alerts.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadAlerts] {ex.Message}");
            }
        }

        private void btnAcknowledgeAlert_Click(object sender, RoutedEventArgs e)
        {
            var btn   = sender as Button;
            var alert = btn?.DataContext as AlertVM;
            if (alert == null) return;

            try
            {
                // SEQ-17 step 3: AcknowledgeAlert → writes AcknowledgedByAdminUserId
                bool ok = _svc.AcknowledgeAlert(alert.AlertId, _adminUserId);

                if (ok)
                {
                    _alerts.Remove(alert);
                    lblAlertCount.Text = _alerts.Count.ToString();
                    MessageBox.Show("Alert acknowledged.", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to acknowledge alert.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// FR-14: WCF server callback — new alert pushed in real time.
        /// Refresh the alerts tab immediately.
        /// </summary>
        private void OnServerMessage(object sender, ServerMessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Any server message that contains "ALERT" triggers an alert refresh
                if (e.Message.Contains("ALERT") || e.Message.Contains("alert"))
                    LoadAlerts();

                // Session changes trigger a session refresh
                if (e.Message.Contains("session") || e.Message.Contains("Session"))
                    LoadActiveSessions();
            });
        }

        // ═══════════════════════════════════════════════════════════
        //  UC-18  —  GENERATE REPORTS
        //  SEQ-18: admin selects type+range → GetSessionReport → format → display
        // ═══════════════════════════════════════════════════════════

        private void btnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (dpFromDate.SelectedDate == null || dpToDate.SelectedDate == null)
            {
                MessageBox.Show("Please select From and To dates.",
                    "Date Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime from = dpFromDate.SelectedDate.Value;
            DateTime to   = dpToDate.SelectedDate.Value;

            if (from > to)
            {
                MessageBox.Show("From date cannot be after To date.",
                    "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string type = (cboReportType.SelectedItem as ComboBoxItem)?.Content.ToString()
                          ?? "Session Usage";

            try
            {
                // SEQ-18 step 2: request report data from server
                var data = _svc.GetSessionReport(from, to);

                string report = BuildReportText(type, data, from, to);
                txtReportOutput.Text = report;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating report: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildReportText(string type, ReportData data, DateTime from, DateTime to)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"{'=',-70}");
            sb.AppendLine($"  {type.ToUpper()} REPORT");
            sb.AppendLine($"  Period  : {from:yyyy-MM-dd}  to  {to:yyyy-MM-dd}");
            sb.AppendLine($"  Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}  by  {_adminUsername}");
            sb.AppendLine($"{'=',-70}");
            sb.AppendLine();

            switch (type)
            {
                case "Session Usage":
                    sb.AppendLine($"  Total Sessions        : {data.TotalSessions}");
                    sb.AppendLine($"  Total Usage Hours     : {data.TotalHours:F2}");
                    sb.AppendLine($"  Avg Duration (h)      : {(data.TotalSessions > 0 ? data.TotalHours / data.TotalSessions : 0):F2}");
                    sb.AppendLine($"  Total Revenue         : ${data.TotalRevenue:F2}");
                    sb.AppendLine();
                    sb.AppendLine($"  {"User",-16} {"Client",-12} {"Duration",8}  {"Billing",10}  Status");
                    sb.AppendLine(new string('-', 66));
                    foreach (var s in data.Sessions ?? Array.Empty<SessionInfo>())
                        sb.AppendLine(
                            $"  {s.Username,-16} {s.ClientCode,-12} {s.SelectedDuration,6} min  ${s.CurrentBilling,8:F2}  {s.SessionStatus}");
                    break;

                case "Billing Summary":
                    sb.AppendLine($"  Total Revenue         : ${data.TotalRevenue:F2}");
                    sb.AppendLine($"  Sessions Billed       : {data.TotalSessions}");
                    sb.AppendLine($"  Avg Bill / Session    : ${(data.TotalSessions > 0 ? data.TotalRevenue / data.TotalSessions : 0):F2}");
                    sb.AppendLine($"  Total Billable Hours  : {data.TotalHours:F2}");
                    sb.AppendLine($"  Current Rate          : ${_svc.GetCurrentBillingRate():F2} / min");
                    sb.AppendLine();
                    sb.AppendLine($"  {"Date/Time",-20} {"User",-16} {"Dur",5}  {"Amount",10}");
                    sb.AppendLine(new string('-', 60));
                    foreach (var s in data.Sessions ?? Array.Empty<SessionInfo>())
                        sb.AppendLine(
                            $"  {s.StartTime:yyyy-MM-dd HH:mm}  {s.Username,-16} {s.SelectedDuration,3} min  ${s.CurrentBilling,8:F2}");
                    break;

                case "Security Alerts":
                    var al = _svc.GetUnacknowledgedAlerts()
                                 .Where(a => a.Timestamp >= from && a.Timestamp <= to.AddDays(1))
                                 .ToArray();
                    sb.AppendLine($"  Total Unresolved Alerts : {al.Length}");
                    sb.AppendLine();
                    var byType = al.GroupBy(a => a.AlertType);
                    sb.AppendLine("  Alert Type Summary:");
                    sb.AppendLine(new string('-', 40));
                    foreach (var g in byType)
                        sb.AppendLine($"    {g.Key,-30} {g.Count(),4}");
                    sb.AppendLine();
                    sb.AppendLine("  Alert Details:");
                    sb.AppendLine(new string('-', 80));
                    foreach (var a in al.OrderByDescending(x => x.Timestamp))
                    {
                        sb.AppendLine(
                            $"  {a.Timestamp:yyyy-MM-dd HH:mm:ss}  [{a.Severity,-6}]  {a.AlertType}");
                        sb.AppendLine(
                            $"    Client: {a.ClientCode ?? "?"}  User: {a.Username ?? "?"}");
                        sb.AppendLine($"    {a.Description}");
                        sb.AppendLine();
                    }
                    break;

                default:
                    sb.AppendLine("No data available for the selected period.");
                    break;
            }

            return sb.ToString();
        }

        /// <summary>UC-18 alt: export report text to a .txt file.</summary>
        private void btnExportReport_Click(object sender, RoutedEventArgs e)
        {
            string text = txtReportOutput.Text;
            if (string.IsNullOrWhiteSpace(text) ||
                text.StartsWith("Report output"))
            {
                MessageBox.Show("Generate a report first.",
                    "Nothing to export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "Text files (*.txt)|*.txt",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, text);
                MessageBox.Show($"Exported to:\n{dlg.FileName}", "Exported",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  REFRESH / LOAD ALL
        // ─────────────────────────────────────────────────────────

        private void LoadAll()
        {
            LoadActiveSessions();
            LoadClients();
            LoadAlerts();
        }

        private void AutoRefresh()
        {
            try { LoadActiveSessions(); LoadAlerts(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoRefresh] {ex.Message}");
            }
        }

        private void btnRefreshSessions_Click(object sender, RoutedEventArgs e)
        {
            LoadAll();
            MessageBox.Show("Data refreshed.", "Refresh",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ─────────────────────────────────────────────────────────
        //  LOGOUT / CLOSE
        // ─────────────────────────────────────────────────────────

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            _refreshTimer.Stop();
            _svc?.UnsubscribeFromNotifications("ADMIN_" + _adminUserId);

            _sessions.Clear(); _clients.Clear(); _alerts.Clear(); _logs.Clear();
            txtAdminUsername.Clear();
            txtAdminPassword.Clear();
            lblAdminUser.Text = "Admin: —";

            DashboardPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility     = Visibility.Visible;
            AdminHeaderPanel.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _refreshTimer?.Stop();
            try
            {
                if (_svc?.IsConnected == true)
                {
                    _svc.UnsubscribeFromNotifications("ADMIN_" + _adminUserId);
                    _svc.Disconnect();
                }
            }
            catch { /* best-effort */ }
            base.OnClosing(e);
        }

        // ─────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────

        private void ShowDashboard()
        {
            LoginPanel.Visibility     = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
            AdminHeaderPanel.Visibility = Visibility.Visible;
        }

        private void ShowLoginError(string msg)
        {
            lblAdminLoginError.Text       = msg;
            lblAdminLoginError.Visibility = Visibility.Visible;
        }
    }

    // ── View-models ───────────────────────────────────────────────

    public class ActiveSessionVM : System.ComponentModel.INotifyPropertyChanged
    {
        public int    SessionId      { get; set; }
        public string ClientId       { get; set; }
        public string Username       { get; set; }
        public string StartTime      { get; set; }
        public string Duration       { get; set; }

        private string _remaining;
        public  string RemainingTime
        {
            get => _remaining;
            set { _remaining = value; PC(nameof(RemainingTime)); }
        }

        private string _billing;
        public  string CurrentBilling
        {
            get => _billing;
            set { _billing = value; PC(nameof(CurrentBilling)); }
        }

        private string _status;
        public  string Status
        {
            get => _status;
            set { _status = value; PC(nameof(Status)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void PC(string n)
            => PropertyChanged?.Invoke(this,
               new System.ComponentModel.PropertyChangedEventArgs(n));
    }

    public class ClientVM
    {
        public string ClientId    { get; set; }
        public string MachineName { get; set; }
        public string IpAddress   { get; set; }
        public string Status      { get; set; }
        public string CurrentUser { get; set; }
        public string LastActive  { get; set; }
    }

    public class AlertVM
    {
        public int    AlertId     { get; set; }
        public string Timestamp   { get; set; }
        public string Severity    { get; set; }
        public string ClientId    { get; set; }
        public string Username    { get; set; }
        public string AlertType   { get; set; }
        public string Description { get; set; }
    }

    public class LogVM
    {
        public string LogTime    { get; set; }
        public string Category   { get; set; }
        public string LogType    { get; set; }
        public string Source     { get; set; }
        public string ClientCode { get; set; }
        public string Username   { get; set; }
        public string Message    { get; set; }
    }
}
