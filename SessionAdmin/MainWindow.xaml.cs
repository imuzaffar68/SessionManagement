using System;
using System.Collections.ObjectModel;
using System.Data;
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

        private string _adminFullname; 
        private string _adminUsername;
        private int    _adminUserId;

        // Observable collections bound to DataGrids
        private ObservableCollection<ActiveSessionVM> _sessions = new ObservableCollection<ActiveSessionVM>();
        private ObservableCollection<ClientVM> _clients = new ObservableCollection<ClientVM>();
        private ObservableCollection<AlertVM> _alerts = new ObservableCollection<AlertVM>();
        private ObservableCollection<LogVM> _logs = new ObservableCollection<LogVM>();
        private ObservableCollection<UserVM> _users = new ObservableCollection<UserVM>();
        private ObservableCollection<BillingRateVM> _billingRates = new ObservableCollection<BillingRateVM>();
        private int? _selectedBillingRateId = null;

        // ─────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            dgActiveSessions.ItemsSource = _sessions;
            dgClients.ItemsSource        = _clients;
            dgAlerts.ItemsSource         = _alerts;
            dgLogs.ItemsSource           = _logs;
            dgUsers.ItemsSource          = _users;
            dgBillingRates.ItemsSource   = _billingRates;

            dpFromDate.SelectedDate = DateTime.Today.AddMonths(-1);
            dpToDate.SelectedDate   = DateTime.Today;
            dpLogFrom.SelectedDate  = DateTime.Today;
            dpLogTo.SelectedDate    = DateTime.Today;

            _refreshTimer          = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(5);   // NFR-02: ≤ 2 s practical bound
            _refreshTimer.Tick    += (_, __) => AutoRefresh();

            // Connection status timer
            var connTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            connTimer.Tick += (s, e) => UpdateConnectionStatus();
            connTimer.Start();

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

        private void UpdateConnectionStatus()
        {
            if (_svc != null && _svc.IsConnected)
            {
                ellipseConnectionStatus.Fill = System.Windows.Media.Brushes.LimeGreen;
                lblConnectionStatus.Text = "Connected";
                btnConnect.Visibility = Visibility.Collapsed;
            }
            else
            {
                ellipseConnectionStatus.Fill = System.Windows.Media.Brushes.Red;
                lblConnectionStatus.Text = "Disconnected";
                btnConnect.Visibility = Visibility.Visible;
            }
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_svc != null)
            {
                if (!_svc.Connect())
                {
                    UpdateConnectionStatus();
                    MessageBox.Show("Failed to connect to server. Please check your network or server status.",
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    UpdateConnectionStatus();
                }
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

                _adminFullname      = resp.FullName; 
                _adminUsername      = resp.Username;
                _adminUserId        = resp.UserId;
                lblAdminUser.Text   = $"Admin: {_adminFullname}";

                // SEQ-09 step 4: subscribe for real-time push (FR-14)
                _svc.SubscribeForNotifications("ADMIN");

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
                        MacAddress  = c.MacAddress,
                        Location    = c.Location,
                        IsActive = c.IsActive,
                        ClientMachineStatus = c.IsActive ? "Active" : "In-Active",
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

        private void btnEnableClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var client = btn?.DataContext as ClientVM;
            if (client == null) return;

            try
            {
                bool ok = _svc.UpdateClientMachineIsActive(client.ClientId, true);
                if (ok)
                {
                    client.IsActive = true;
                    MessageBox.Show(
                        $"Client '{client.ClientId}' enabled successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadClients();
                }
                else
                {
                    MessageBox.Show("Failed to enable client. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDisableClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var client = btn?.DataContext as ClientVM;
            if (client == null) return;

            try
            {
                bool ok = _svc.UpdateClientMachineIsActive(client.ClientId, false);
                if (ok)
                {
                    client.IsActive = false;
                    MessageBox.Show(
                        $"Client '{client.ClientId}' disabled successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadClients();
                }
                else
                {
                    MessageBox.Show("Failed to disable client. Please try again.",
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

        // ─────────────────────────────────────────────────────────
        //  FR-14: Real-time server push notification handler
        // ─────────────────────────────────────────────────────────
        //private void OnServerMessage(object sender, ServerMessageEventArgs e)
        //{
        //    // Show as popup (or you can append to a log/alert panel)
        //    Dispatcher.BeginInvoke(new Action(() =>
        //    {
        //        MessageBox.Show(e.Message, "Server Notification", MessageBoxButton.OK, MessageBoxImage.Information);
        //        // Optionally, add to alerts/logs:
        //        // _alerts.Add(new AlertVM { Message = e.Message, Timestamp = e.Timestamp });
        //    }));
        //}

        /// <summary>
        /// FR-14: WCF server callback — new alert pushed in real time.
        /// Refresh the alerts tab immediately.
        /// </summary>
        //private void OnServerMessage(object sender, ServerMessageEventArgs e)
        //{
        //    Dispatcher.Invoke(() =>
        //    {
        //        // Any server message that contains "ALERT" triggers an alert refresh
        //        if (e.Message.Contains("ALERT") || e.Message.Contains("alert"))
        //            LoadAlerts();

        //        // Session changes trigger a session refresh
        //        if (e.Message.Contains("session") || e.Message.Contains("Session"))
        //            LoadActiveSessions();
        //    });
        //}
        // ═══════════════════════════════════════════════════════════════
        //  PASTE THIS REPLACEMENT into SessionAdmin/MainWindow.xaml.cs
        //
        //  Replaces the OnServerMessage method and adds the auto-refresh
        //  helper so FR-14 real-time alert delivery works correctly.
        //
        //  UC-16 / UC-17:
        //    When ProxyDetectionService fires on a client machine, the WCF
        //    server pushes OnServerMessage to ALL admin subscribers.
        //    This handler parses that push and auto-refreshes the correct tab
        //    WITHOUT showing a modal dialog box for every security event.
        // ═══════════════════════════════════════════════════════════════

        // ── Replace the existing OnServerMessage method ───────────────

        /// <summary>
        /// FR-14: WCF server callback — fired whenever the server has something
        /// to push (new alert, session change, auto-expiry, etc.)
        ///
        /// SEQ-17 Step 2: Server sends alert to admin dashboard.
        /// We act on keyword hints in the message to decide which tab to refresh.
        /// We NEVER show a modal MessageBox here — that would block the UI thread
        /// and prevent further callbacks from being processed.
        /// </summary>
        private void OnServerMessage(object sender, ServerMessageEventArgs e)
        {
            // Always dispatch to the UI thread (callback arrives on thread-pool)
            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                try
                {
                    string msg = e.Message ?? "";

                    // UC-17: Any ALERT message → refresh Security Alerts tab immediately
                    if (msg.IndexOf("ALERT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("alert", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadAlerts();
                        // Show a non-blocking notification in the status bar rather than a popup
                        lblLastUpdate.Text = "Alert received: " + DateTime.Now.ToString("HH:mm:ss")
                                             + "  — " + TruncateMessage(msg, 80);
                    }

                    // UC-02 / UC-07: Session changes → refresh Active Sessions tab
                    if (msg.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadActiveSessions();
                    }

                    System.Diagnostics.Debug.WriteLine("[FR-14 Push] " + msg);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[OnServerMessage] " + ex.Message);
                }
            }));
        }

        private static string TruncateMessage(string msg, int maxLen)
        {
            if (msg == null) return "";
            return msg.Length <= maxLen ? msg : msg.Substring(0, maxLen) + "…";
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
            LoadClientUsers();
            LoadBillingRates();
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

        // ═══════════════════════════════════════════════════════════
        //  UC-03  —  USER REGISTRATION
        //  SEQ-03: Admin enters user details → validate → register → log
        // ═══════════════════════════════════════════════════════════

        private void btnRegisterUser_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string fullName = txtFullName.Text.Trim();
            string password = txtPassword.Password;
            string phone = txtPhone.Text.Trim();
            string address = txtAddress.Text.Trim();

            lblRegError.Visibility = Visibility.Collapsed;
            lblRegSuccess.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowRegError("Username and password are required.");
                return;
            }

            btnRegisterUser.IsEnabled = false;
            btnRegisterUser.Content = "Registering…";

            try
            {
                // SEQ-03 step 2: call server to register user
                var resp = _svc.RegisterClientUser(username, fullName, password, phone, address, _adminUserId);

                if (!resp.Success)
                {
                    ShowRegError(resp.ErrorMessage ?? "Registration failed.");
                    return;
                }

                // Success
                ShowRegSuccess($"User '{username}' registered successfully (ID: {resp.UserId})");

                // Clear form
                ClearRegistrationForm();

                // Refresh user list
                LoadClientUsers();
            }
            catch (Exception ex)
            {
                ShowRegError($"Connection error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[RegisterUser] {ex.Message}");
            }
            finally
            {
                btnRegisterUser.IsEnabled = true;
                btnRegisterUser.Content = "Register User";
            }
        }

        private void btnClearForm_Click(object sender, RoutedEventArgs e)
        {
            ClearRegistrationForm();
        }

        private void btnGeneratePassword_Click(object sender, RoutedEventArgs e)
        {
            txtPassword.Password = "User@123456";
            MessageBox.Show("Default password set: User@123456\n\nMake sure the user changes it on first login.",
                "Default Password", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnRefreshUsers_Click(object sender, RoutedEventArgs e)
        {
            LoadClientUsers();
            MessageBox.Show("User list refreshed.", "Refresh",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadClientUsers()
        {
            try
            {
                _users.Clear();
                var list = _svc.GetAllClientUsers();

                foreach (var u in list)
                {
                    _users.Add(new UserVM
                    {
                        UserId = u.UserId,
                        Username = u.Username,
                        FullName = u.FullName,
                        Phone = u.Phone,
                        Address = u.Address,
                        Status = u.Status,
                        CreatedAt = u.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        LastLogin = u.LastLoginAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"
                    });
                }

                lblUserCount.Text = _users.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadUsers] {ex.Message}");
            }
        }

        private void ClearRegistrationForm()
        {
            txtUsername.Clear();
            txtFullName.Clear();
            txtPassword.Clear();
            txtPhone.Clear();
            txtAddress.Clear();
            lblRegError.Visibility = Visibility.Collapsed;
            lblRegSuccess.Visibility = Visibility.Collapsed;
        }

        private void ShowRegError(string msg)
        {
            lblRegError.Text = msg;
            lblRegError.Visibility = Visibility.Visible;
        }

        private void ShowRegSuccess(string msg)
        {
            lblRegSuccess.Text = msg;
            lblRegSuccess.Visibility = Visibility.Visible;
        }

        // ═══════════════════════════════════════════════════════════
        //  USER MANAGEMENT - EDIT, RESET PASSWORD, TOGGLE STATUS
        // ═══════════════════════════════════════════════════════════

        private void btnEditUserInline_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var selected = btn?.DataContext as UserVM;
            if (selected == null)
            {
                ShowUserActionError("Unable to get user data.");
                return;
            }

            // Show edit dialog
            var editWindow = new EditUserWindow(selected);
            if (editWindow.ShowDialog() == true)
            {
                try
                {
                    var resp = _svc.UpdateClientUser(
                        selected.UserId,
                        editWindow.FullName,
                        editWindow.Phone,
                        editWindow.Address,
                        _adminUserId);

                    if (!resp.Success)
                    {
                        ShowUserActionError(resp.ErrorMessage ?? "Failed to update user.");
                        return;
                    }

                    ShowUserActionSuccess($"User '{selected.Username}' updated successfully.");
                    LoadClientUsers();
                }
                catch (Exception ex)
                {
                    ShowUserActionError($"Error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[EditUser] {ex.Message}");
                }
            }
        }

        private void btnResetPasswordInline_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var selected = btn?.DataContext as UserVM;
            if (selected == null)
            {
                ShowUserActionError("Unable to get user data.");
                return;
            }

            // Show reset password dialog
            var resetWindow = new ResetPasswordWindow(selected.Username);
            if (resetWindow.ShowDialog() == true)
            {
                try
                {
                    var resp = _svc.ResetClientUserPassword(
                        selected.UserId,
                        resetWindow.NewPassword,
                        _adminUserId);

                    if (!resp.Success)
                    {
                        ShowUserActionError(resp.ErrorMessage ?? "Failed to reset password.");
                        return;
                    }

                    ShowUserActionSuccess($"Password for user '{selected.Username}' reset to: {resetWindow.NewPassword}");
                    LoadClientUsers();
                }
                catch (Exception ex)
                {
                    ShowUserActionError($"Error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ResetPassword] {ex.Message}");
                }
            }
        }

        private void btnToggleStatusInline_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var selected = btn?.DataContext as UserVM;
            if (selected == null)
            {
                ShowUserActionError("Unable to get user data.");
                return;
            }

            string newStatus = selected.Status == "Active" ? "Disabled" : "Active";
            var result = MessageBox.Show(
                $"Change user '{selected.Username}' status from {selected.Status} to {newStatus}?",
                "Confirm Status Change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var resp = _svc.ToggleUserStatus(selected.UserId, _adminUserId);

                if (!resp.Success)
                {
                    ShowUserActionError(resp.ErrorMessage ?? "Failed to update user status.");
                    return;
                }

                ShowUserActionSuccess($"User '{selected.Username}' status changed to {resp.NewStatus}.");
                LoadClientUsers();
            }
            catch (Exception ex)
            {
                ShowUserActionError($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ToggleStatus] {ex.Message}");
            }
        }

        private void ShowUserActionError(string msg)
        {
            lblUserActionError.Text = msg;
            lblUserActionError.Visibility = Visibility.Visible;
            lblUserActionSuccess.Visibility = Visibility.Collapsed;
        }

        private void ShowUserActionSuccess(string msg)
        {
            lblUserActionSuccess.Text = msg;
            lblUserActionSuccess.Visibility = Visibility.Visible;
            lblUserActionError.Visibility = Visibility.Collapsed;
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
            txtAdminPasswordPlain.Clear();
            lblAdminUser.Text = "Admin: —";

            DashboardPanel.Visibility     = Visibility.Collapsed;
            LoginPanel.Visibility         = Visibility.Visible;
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
            LoginPanel.Visibility       = Visibility.Collapsed;
            DashboardPanel.Visibility   = Visibility.Visible;
            AdminHeaderPanel.Visibility = Visibility.Visible;
            NavigateTo("Sessions");
        }

        // ═══════════════════════════════════════════════════════════
        //  SIDEBAR NAVIGATION
        // ═══════════════════════════════════════════════════════════

        private string _currentPage = "Sessions";

        private void NavigateTo(string page)
        {
            // Hide all pages
            PageSessions.Visibility = Visibility.Collapsed;
            PageClients.Visibility  = Visibility.Collapsed;
            PageUsers.Visibility    = Visibility.Collapsed;
            PageAlerts.Visibility   = Visibility.Collapsed;
            PageLogs.Visibility     = Visibility.Collapsed;
            PageRates.Visibility    = Visibility.Collapsed;
            PageReports.Visibility  = Visibility.Collapsed;

            // Reset all nav button Tags
            if (btnNavSessions != null) btnNavSessions.Tag = null;
            if (btnNavClients  != null) btnNavClients.Tag  = null;
            if (btnNavUsers    != null) btnNavUsers.Tag     = null;
            if (btnNavAlerts   != null) btnNavAlerts.Tag    = null;
            if (btnNavLogs     != null) btnNavLogs.Tag      = null;
            if (btnNavRates    != null) btnNavRates.Tag     = null;
            if (btnNavReports  != null) btnNavReports.Tag   = null;

            _currentPage = page;

            switch (page)
            {
                case "Sessions":
                    PageSessions.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "Active Sessions";
                    if (btnNavSessions != null) btnNavSessions.Tag = "Active";
                    break;
                case "Clients":
                    PageClients.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "Client Machines";
                    if (btnNavClients != null) btnNavClients.Tag = "Active";
                    LoadClients();
                    break;
                case "Users":
                    PageUsers.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "User Management";
                    if (btnNavUsers != null) btnNavUsers.Tag = "Active";
                    LoadClientUsers();
                    break;
                case "Alerts":
                    PageAlerts.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "Security Alerts";
                    if (btnNavAlerts != null) btnNavAlerts.Tag = "Active";
                    LoadAlerts();
                    break;
                case "Logs":
                    PageLogs.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "System Logs";
                    if (btnNavLogs != null) btnNavLogs.Tag = "Active";
                    break;
                case "Rates":
                    PageRates.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "Billing Rates";
                    if (btnNavRates != null) btnNavRates.Tag = "Active";
                    LoadBillingRates();
                    break;
                case "Reports":
                    PageReports.Visibility = Visibility.Visible;
                    lblPageTitle.Text = "Reports";
                    if (btnNavReports != null) btnNavReports.Tag = "Active";
                    break;
            }
        }

        private void BtnNavSessions_Click(object sender, RoutedEventArgs e) => NavigateTo("Sessions");
        private void BtnNavClients_Click(object sender,  RoutedEventArgs e) => NavigateTo("Clients");
        private void BtnNavUsers_Click(object sender,    RoutedEventArgs e) => NavigateTo("Users");
        private void BtnNavAlerts_Click(object sender,   RoutedEventArgs e) => NavigateTo("Alerts");
        private void BtnNavLogs_Click(object sender,     RoutedEventArgs e) => NavigateTo("Logs");
        private void BtnNavRates_Click(object sender,    RoutedEventArgs e) => NavigateTo("Rates");
        private void BtnNavReports_Click(object sender,  RoutedEventArgs e) => NavigateTo("Reports");

        private void ShowLoginError(string msg)
        {
            lblAdminLoginError.Text       = msg;
            lblAdminLoginError.Visibility = Visibility.Visible;
        }

        // ═══════════════════════════════════════════════════════════
        //  BILLING RATE MANAGEMENT
        // ═══════════════════════════════════════════════════════════

        private void LoadBillingRates()
        {
            try
            {
                _billingRates.Clear();
                var rates = _svc.GetAllBillingRates();

                foreach (var rate in rates)
                {
                    _billingRates.Add(new BillingRateVM
                    {
                        BillingRateId = rate.BillingRateId,
                        Name = rate.Name,
                        RatePerMinute = rate.RatePerMinute,
                        Currency = rate.Currency,
                        EffectiveFrom = rate.EffectiveFrom,
                        EffectiveTo = rate.EffectiveTo,
                        IsActive = rate.IsActive ? 1 : 0,
                        IsDefault = rate.IsDefault ? 1 : 0,
                        CreatedAt = rate.CreatedAt,
                        Notes = rate.Notes
                    });
                }
                lblBillingRateCount.Text = _billingRates.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadBillingRates] {ex.Message}");
            }
        }

        private void btnRefreshBillingRates_Click(object sender, RoutedEventArgs e)
        {
            LoadBillingRates();
            MessageBox.Show("Billing rates refreshed.", "Refresh",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnAddBillingRate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRateName.Text))
            {
                ShowBillingRateError("Rate name is required.");
                return;
            }

            if (!decimal.TryParse(txtRatePerMinute.Text, out decimal rate) || rate < 0)
            {
                ShowBillingRateError("Rate must be a valid positive number.");
                return;
            }

            btnAddBillingRate.IsEnabled = false;
            btnAddBillingRate.Content = "Adding…";

            try
            {
                string currency = (cboCurrency.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "USD";
                DateTime? effectiveFrom = dpEffectiveFrom.SelectedDate;
                DateTime? effectiveTo = dpEffectiveTo.SelectedDate;
                bool isDefault = chkIsDefault.IsChecked ?? false;
                bool isActive = chkIsActive.IsChecked ?? true;
                string notes = txtNotes.Text.Trim();

                int newId = _svc.InsertBillingRate(txtRateName.Text.Trim(), rate, currency,
                    effectiveFrom, effectiveTo, isDefault, _adminUserId, notes);

                if (newId <= 0)
                {
                    ShowBillingRateError("Failed to insert billing rate. Please try again.");
                    return;
                }

                ShowBillingRateSuccess($"Billing rate '{txtRateName.Text}' added successfully (ID: {newId})");
                ClearBillingRateForm();
                LoadBillingRates();
            }
            catch (Exception ex)
            {
                ShowBillingRateError($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AddBillingRate] {ex.Message}");
            }
            finally
            {
                btnAddBillingRate.IsEnabled = true;
                btnAddBillingRate.Content = "Add Rate";
            }
        }

        private void btnEditBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var rate = btn?.DataContext as BillingRateVM;
            if (rate == null) return;

            // Populate form
            _selectedBillingRateId = rate.BillingRateId;
            txtRateName.Text = rate.Name;
            txtRatePerMinute.Text = rate.RatePerMinute.ToString();
            cboCurrency.SelectedItem = cboCurrency.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => x.Content.ToString() == rate.Currency) ?? cboCurrency.Items[0];
            dpEffectiveFrom.SelectedDate = rate.EffectiveFrom;
            dpEffectiveTo.SelectedDate = rate.EffectiveTo;
            chkIsActive.IsChecked = rate.IsActive == 1;
            chkIsDefault.IsChecked = rate.IsDefault == 1;
            txtNotes.Text = rate.Notes ?? "";

            btnAddBillingRate.Visibility = Visibility.Collapsed;
            btnUpdateBillingRate.Visibility = Visibility.Visible;
            lblBillingRateError.Visibility = Visibility.Collapsed;
            lblBillingRateSuccess.Visibility = Visibility.Collapsed;
        }

        private void btnUpdateBillingRate_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedBillingRateId.HasValue)
            {
                ShowBillingRateError("No rate selected for editing.");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtRateName.Text))
            {
                ShowBillingRateError("Rate name is required.");
                return;
            }

            if (!decimal.TryParse(txtRatePerMinute.Text, out decimal rate) || rate < 0)
            {
                ShowBillingRateError("Rate must be a valid positive number.");
                return;
            }

            btnUpdateBillingRate.IsEnabled = false;
            btnUpdateBillingRate.Content = "Updating…";

            try
            {
                string currency = (cboCurrency.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "USD";
                DateTime? effectiveFrom = dpEffectiveFrom.SelectedDate;
                DateTime? effectiveTo = dpEffectiveTo.SelectedDate;
                bool isDefault = chkIsDefault.IsChecked ?? false;
                bool isActive = chkIsActive.IsChecked ?? true;
                string notes = txtNotes.Text.Trim();

                bool success = _svc.UpdateBillingRate(_selectedBillingRateId.Value, txtRateName.Text.Trim(),
                    rate, currency, effectiveFrom, effectiveTo, isActive, isDefault, notes);

                if (!success)
                {
                    ShowBillingRateError("Failed to update billing rate. Please try again.");
                    return;
                }

                ShowBillingRateSuccess($"Billing rate '{txtRateName.Text}' updated successfully.");
                ClearBillingRateForm();
                _selectedBillingRateId = null;
                btnAddBillingRate.Visibility = Visibility.Visible;
                btnUpdateBillingRate.Visibility = Visibility.Collapsed;
                LoadBillingRates();
            }
            catch (Exception ex)
            {
                ShowBillingRateError($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdateBillingRate] {ex.Message}");
            }
            finally
            {
                btnUpdateBillingRate.IsEnabled = true;
                btnUpdateBillingRate.Content = "Update Rate";
            }
        }

        private void btnSetDefaultBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var rate = btn?.DataContext as BillingRateVM;
            if (rate == null) return;

            var result = MessageBox.Show(
                $"Set '{rate.Name}' as the default billing rate?\nThe current default will be updated.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                bool success = _svc.SetDefaultBillingRate(rate.BillingRateId);

                if (success)
                {
                    MessageBox.Show($"'{rate.Name}' is now the default rate.", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadBillingRates();
                }
                else
                {
                    MessageBox.Show("Failed to set default rate. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDeleteBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var rate = btn?.DataContext as BillingRateVM;
            if (rate == null) return;

            var result = MessageBox.Show(
                $"Delete billing rate '{rate.Name}'?\n\nThis action cannot be undone if the rate has been used in billing records.",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                bool success = _svc.DeleteBillingRate(rate.BillingRateId);

                if (success)
                {
                    MessageBox.Show($"Billing rate '{rate.Name}' deleted successfully.", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadBillingRates();
                }
                else
                {
                    MessageBox.Show("Cannot delete this rate:\n- At least one rate must exist\n- At least one default rate must exist",
                        "Deletion Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnClearBillingRateForm_Click(object sender, RoutedEventArgs e)
        {
            ClearBillingRateForm();
        }

        private void ClearBillingRateForm()
        {
            txtRateName.Clear();
            txtRatePerMinute.Clear();
            cboCurrency.SelectedIndex = 0;
            dpEffectiveFrom.SelectedDate = null;
            dpEffectiveTo.SelectedDate = null;
            chkIsActive.IsChecked = true;
            chkIsDefault.IsChecked = false;
            txtNotes.Clear();
            _selectedBillingRateId = null;
            btnAddBillingRate.Visibility = Visibility.Visible;
            btnUpdateBillingRate.Visibility = Visibility.Collapsed;
            lblBillingRateError.Visibility = Visibility.Collapsed;
            lblBillingRateSuccess.Visibility = Visibility.Collapsed;
        }

        private void ShowBillingRateError(string msg)
        {
            lblBillingRateError.Text = msg;
            lblBillingRateError.Visibility = Visibility.Visible;
            lblBillingRateSuccess.Visibility = Visibility.Collapsed;
        }

        private void ShowBillingRateSuccess(string msg)
        {
            lblBillingRateSuccess.Text = msg;
            lblBillingRateSuccess.Visibility = Visibility.Visible;
            lblBillingRateError.Visibility = Visibility.Collapsed;
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
        public string MacAddress  { get; set; }
        public string Location    { get; set; }
        public bool   IsActive    { get; set; }
        public string ClientMachineStatus { get; set; }
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

    public class UserVM
    {
        public int    UserId      { get; set; }
        public string Username    { get; set; }
        public string FullName    { get; set; }
        public string Phone       { get; set; }
        public string Address     { get; set; }
        public string Status      { get; set; }
        public string CreatedAt   { get; set; }
        public string LastLogin   { get; set; }
    }

    public class BillingRateVM
    {
        public int BillingRateId { get; set; }
        public string Name { get; set; }
        public decimal RatePerMinute { get; set; }
        public string Currency { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int IsActive { get; set; }
        public int IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Notes { get; set; }
    }
}
