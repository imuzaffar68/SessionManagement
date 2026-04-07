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
        // ═══════════════════════════════════════════════════════════
        //  #region STATE FIELDS
        // ═══════════════════════════════════════════════════════════
        #region State Fields

        private SessionServiceClient _svc;
        private DispatcherTimer _refreshTimer;

        private string _adminFullname;
        private string _adminUsername;
        private int _adminUserId;

        private ObservableCollection<ActiveSessionVM> _sessions = new ObservableCollection<ActiveSessionVM>();
        private ObservableCollection<ClientVM> _clients = new ObservableCollection<ClientVM>();
        private ObservableCollection<AlertVM> _alerts = new ObservableCollection<AlertVM>();
        private ObservableCollection<LogVM> _logs = new ObservableCollection<LogVM>();
        private ObservableCollection<UserVM> _users = new ObservableCollection<UserVM>();
        private ObservableCollection<BillingRateVM> _billingRates = new ObservableCollection<BillingRateVM>();
        private int? _selectedBillingRateId = null;

        // Current active nav page
        private string _currentPage = "dashboard";

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region INITIALIZATION
        // ═══════════════════════════════════════════════════════════
        #region Initialization

        public MainWindow()
        {
            InitializeComponent();

            // Bind collections
            dgActiveSessions.ItemsSource = _sessions;
            dgClients.ItemsSource = _clients;
            dgAlerts.ItemsSource = _alerts;
            dgLogs.ItemsSource = _logs;
            dgUsers.ItemsSource = _users;
            dgBillingRates.ItemsSource = _billingRates;

            // Default date ranges
            dpFromDate.SelectedDate = DateTime.Today.AddMonths(-1);
            dpToDate.SelectedDate = DateTime.Today;
            dpLogFrom.SelectedDate = DateTime.Today;
            dpLogTo.SelectedDate = DateTime.Today;

            // Auto-refresh timer (5 seconds)
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += (_, __) => AutoRefresh();

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
                _svc.ServerMessage += OnServerMessage;

                if (!_svc.Connect())
                    MessageBox.Show("Cannot connect to server.", "Connection Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Init error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region CONNECTION STATUS
        // ═══════════════════════════════════════════════════════════
        #region Connection Status

        private void UpdateConnectionStatus()
        {
            bool connected = _svc != null && _svc.IsConnected;
            ellipseConnectionStatus.Fill = connected
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.OrangeRed;
            lblConnectionStatus.Text = connected ? "Connected" : "Disconnected";
            btnConnect.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_svc != null && !_svc.Connect())
                MessageBox.Show("Failed to connect to server.", "Connection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateConnectionStatus();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region NAVIGATION
        // ═══════════════════════════════════════════════════════════
        #region Navigation

        private void NavigateTo(string page)
        {
            _currentPage = page;

            // Hide all pages
            PageDashboard.Visibility = Visibility.Collapsed;
            PageSessions.Visibility = Visibility.Collapsed;
            PageClients.Visibility = Visibility.Collapsed;
            PageUsers.Visibility = Visibility.Collapsed;
            PageBilling.Visibility = Visibility.Collapsed;
            PageAlerts.Visibility = Visibility.Collapsed;
            PageLogs.Visibility = Visibility.Collapsed;
            PageReports.Visibility = Visibility.Collapsed;

            // Reset all nav buttons to inactive
            btnNavDashboard.Style = (Style)Resources["NavBtn"];
            btnNavSessions.Style = (Style)Resources["NavBtn"];
            btnNavClients.Style = (Style)Resources["NavBtn"];
            btnNavUsers.Style = (Style)Resources["NavBtn"];
            btnNavBilling.Style = (Style)Resources["NavBtn"];
            btnNavAlerts.Style = (Style)Resources["NavBtn"];
            btnNavLogs.Style = (Style)Resources["NavBtn"];
            btnNavReports.Style = (Style)Resources["NavBtn"];

            switch (page)
            {
                case "dashboard":
                    PageDashboard.Visibility = Visibility.Visible;
                    btnNavDashboard.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Dashboard";
                    lblPageSubtitle.Text = " — Live Overview";
                    LoadDashboard();
                    break;

                case "sessions":
                    PageSessions.Visibility = Visibility.Visible;
                    btnNavSessions.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Active Sessions";
                    lblPageSubtitle.Text = " — Live Monitoring";
                    LoadActiveSessions();
                    break;

                case "clients":
                    PageClients.Visibility = Visibility.Visible;
                    btnNavClients.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Client Machines";
                    lblPageSubtitle.Text = " — Machine Status";
                    LoadClients();
                    break;

                case "users":
                    PageUsers.Visibility = Visibility.Visible;
                    btnNavUsers.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "User Management";
                    lblPageSubtitle.Text = " — Client Accounts";
                    LoadClientUsers();
                    break;

                case "billing":
                    PageBilling.Visibility = Visibility.Visible;
                    btnNavBilling.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Billing Rates";
                    lblPageSubtitle.Text = " — Rate Configuration";
                    LoadBillingRates();
                    break;

                case "alerts":
                    PageAlerts.Visibility = Visibility.Visible;
                    btnNavAlerts.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Security Alerts";
                    lblPageSubtitle.Text = " — Threat Detection";
                    LoadAlerts();
                    break;

                case "logs":
                    PageLogs.Visibility = Visibility.Visible;
                    btnNavLogs.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Session Logs";
                    lblPageSubtitle.Text = " — Activity History";
                    break;

                case "reports":
                    PageReports.Visibility = Visibility.Visible;
                    btnNavReports.Style = (Style)Resources["NavBtnActive"];
                    lblPageTitle.Text = "Reports";
                    lblPageSubtitle.Text = " — Analytics";
                    break;
            }
        }

        private void btnNavDashboard_Click(object sender, RoutedEventArgs e) => NavigateTo("dashboard");
        private void btnNavSessions_Click(object sender, RoutedEventArgs e) => NavigateTo("sessions");
        private void btnNavClients_Click(object sender, RoutedEventArgs e) => NavigateTo("clients");
        private void btnNavUsers_Click(object sender, RoutedEventArgs e) => NavigateTo("users");
        private void btnNavBilling_Click(object sender, RoutedEventArgs e) => NavigateTo("billing");
        private void btnNavAlerts_Click(object sender, RoutedEventArgs e) => NavigateTo("alerts");
        private void btnNavLogs_Click(object sender, RoutedEventArgs e) => NavigateTo("logs");
        private void btnNavReports_Click(object sender, RoutedEventArgs e) => NavigateTo("reports");

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region ADMIN LOGIN (UC-09)
        // ═══════════════════════════════════════════════════════════
        #region Admin Login

        private bool _adminPasswordVisible;

        private void btnShowAdminPassword_Click(object sender, RoutedEventArgs e)
        {
            _adminPasswordVisible = !_adminPasswordVisible;
            if (_adminPasswordVisible)
            {
                txtAdminPasswordPlain.Text = txtAdminPassword.Password;
                txtAdminPassword.Visibility = Visibility.Collapsed;
                txtAdminPasswordPlain.Visibility = Visibility.Visible;
                btnShowAdminPassword.Content = "🙈";
            }
            else
            {
                txtAdminPassword.Password = txtAdminPasswordPlain.Text;
                txtAdminPasswordPlain.Visibility = Visibility.Collapsed;
                txtAdminPassword.Visibility = Visibility.Visible;
                btnShowAdminPassword.Content = "👁";
            }
        }

        private void btnAdminLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtAdminUsername.Text.Trim();
            string pass = _adminPasswordVisible
                ? txtAdminPasswordPlain.Text
                : txtAdminPassword.Password;

            loginErrorBorder.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                ShowLoginError("Please enter both username and password.");
                return;
            }

            btnAdminLogin.IsEnabled = false;
            btnAdminLogin.Content = "Signing in…";

            try
            {
                var resp = _svc.AuthenticateUser(user, pass, "ADMIN");

                if (!resp.IsAuthenticated)
                { ShowLoginError(resp.ErrorMessage ?? "Invalid credentials."); return; }

                if (resp.UserType != "Admin")
                { ShowLoginError("Access denied. Admin privileges required."); return; }

                _adminFullname = resp.FullName;
                _adminUsername = resp.Username;
                _adminUserId = resp.UserId;
                lblAdminUser.Text = _adminFullname ?? _adminUsername;

                _svc.SubscribeForNotifications("ADMIN");
                ShowDashboard();
                LoadAll();
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                ShowLoginError("Connection error: " + ex.Message);
            }
            finally
            {
                btnAdminLogin.IsEnabled = true;
                btnAdminLogin.Content = "Sign In →";
            }
        }

        private void ShowLoginError(string msg)
        {
            lblAdminLoginError.Text = msg;
            loginErrorBorder.Visibility = Visibility.Visible;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region DASHBOARD / KANBAN
        // ═══════════════════════════════════════════════════════════
        #region Dashboard

        private void LoadDashboard()
        {
            LoadActiveSessions();
            LoadAlerts();
            LoadClients();
            UpdateKPIs();
            UpdateKanban();
        }

        private void UpdateKPIs()
        {
            kpiActiveSessions.Text = _sessions.Count.ToString();
            kpiAlerts.Text = _alerts.Count.ToString();
            kpiClients.Text = _clients.Count.ToString();

            decimal totalBilling = _sessions.Sum(s =>
            {
                if (s.CurrentBilling != null && s.CurrentBilling.StartsWith("$"))
                    return decimal.TryParse(s.CurrentBilling.Substring(1), out decimal val) ? val : 0m;
                return 0m;
            });
            kpiRevenue.Text = $"${totalBilling:F2}";

            // Sidebar badges
            sidebarSessionCount.Text = _sessions.Count.ToString();
            if (_alerts.Count > 0)
            {
                alertBadge.Visibility = Visibility.Visible;
                sidebarAlertCount.Text = _alerts.Count.ToString();
            }
            else
            {
                alertBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateKanban()
        {
            // Active sessions in kanban
            kanbanActiveSessions.ItemsSource = _sessions.Take(5).ToList();
            kanbanActiveCount.Text = _sessions.Count.ToString();

            // Idle clients
            var idleClients = _clients.Where(c => c.Status == "Idle" || c.CurrentUser == "—").Take(5).ToList();
            kanbanIdleClients.ItemsSource = idleClients;
            kanbanIdleCount.Text = idleClients.Count.ToString();

            // Recent alerts
            kanbanAlerts.ItemsSource = _alerts.Take(4).ToList();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region ACTIVE SESSIONS (UC-10)
        // ═══════════════════════════════════════════════════════════
        #region Active Sessions

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
                        SessionId = s.SessionId,
                        ClientId = s.ClientCode,
                        Username = s.Username,
                        StartTime = s.StartTime.ToString("HH:mm:ss"),
                        Duration = $"{s.SelectedDuration} min",
                        RemainingTime = $"{s.RemainingMinutes} min",
                        CurrentBilling = $"${s.CurrentBilling:F2}",
                        Status = s.SessionStatus
                    });
                }
                lblActiveCount.Text = $"{_sessions.Count} sessions";
                lblLastUpdate.Text = $"Updated {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadSessions] {ex.Message}");
            }
        }

        private void btnRefreshSessions_Click(object sender, RoutedEventArgs e)
        {
            LoadAll();
        }

        private void btnTerminateSession_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var session = btn?.DataContext as ActiveSessionVM;
            if (session == null) return;

            var r = MessageBox.Show(
                $"Terminate session {session.SessionId} for '{session.Username}'?",
                "Confirm Termination", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                bool ok = _svc.EndSession(session.SessionId, "Admin");
                if (ok)
                {
                    _sessions.Remove(session);
                    lblActiveCount.Text = $"{_sessions.Count} sessions";
                }
                else
                    MessageBox.Show("Failed to terminate session.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnViewImage_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgActiveSessions.SelectedItem as ActiveSessionVM;
            if (selected == null)
            {
                MessageBox.Show("Please select a session first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string b64 = _svc.DownloadLoginImage(selected.SessionId);
                if (string.IsNullOrEmpty(b64))
                {
                    MessageBox.Show("No image available for this session.", "No Image",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                byte[] bytes = Convert.FromBase64String(b64);
                using (var ms = new System.IO.MemoryStream(bytes))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgSessionPhoto.Source = bmp;
                }

                lblImageTitle.Text = $"Session {selected.SessionId} — {selected.Username}";
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
            imgSessionPhoto.Source = null;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region CLIENT MACHINES (UC-11)
        // ═══════════════════════════════════════════════════════════
        #region Client Machines

        private void LoadClients()
        {
            try
            {
                _clients.Clear();
                foreach (var c in _svc.GetAllClients())
                {
                    _clients.Add(new ClientVM
                    {
                        ClientId = c.ClientCode,
                        MachineName = c.MachineName,
                        IpAddress = c.IpAddress,
                        MacAddress = c.MacAddress,
                        Location = c.Location,
                        IsActive = c.IsActive,
                        ClientMachineStatus = c.IsActive ? "Active" : "Inactive",
                        Status = c.Status,
                        CurrentUser = c.CurrentUser ?? "—",
                        LastActive = c.LastActiveTime?.ToString("yyyy-MM-dd HH:mm") ?? "Never"
                    });
                }
                kpiClients.Text = _clients.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadClients] {ex.Message}");
            }
        }

        private void btnEnableClient_Click(object sender, RoutedEventArgs e)
        {
            var client = (sender as Button)?.DataContext as ClientVM;
            if (client == null) return;
            try
            {
                if (_svc.UpdateClientMachineIsActive(client.ClientId, true))
                    LoadClients();
                else
                    MessageBox.Show("Failed to enable client.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void btnDisableClient_Click(object sender, RoutedEventArgs e)
        {
            var client = (sender as Button)?.DataContext as ClientVM;
            if (client == null) return;
            try
            {
                if (_svc.UpdateClientMachineIsActive(client.ClientId, false))
                    LoadClients();
                else
                    MessageBox.Show("Failed to disable client.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region SECURITY ALERTS (UC-17)
        // ═══════════════════════════════════════════════════════════
        #region Security Alerts

        private void LoadAlerts()
        {
            try
            {
                _alerts.Clear();
                foreach (var a in _svc.GetUnacknowledgedAlerts())
                {
                    _alerts.Add(new AlertVM
                    {
                        AlertId = a.AlertId,
                        Timestamp = a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        Severity = a.Severity,
                        ClientId = a.ClientCode ?? "—",
                        Username = a.Username ?? "—",
                        AlertType = a.AlertType,
                        Description = a.Description
                    });
                }
                lblAlertCount.Text = $"{_alerts.Count} unresolved";
                kpiAlerts.Text = _alerts.Count.ToString();
                alertBadge.Visibility = _alerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                sidebarAlertCount.Text = _alerts.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadAlerts] {ex.Message}");
            }
        }

        private void btnAcknowledgeAlert_Click(object sender, RoutedEventArgs e)
        {
            var alert = (sender as Button)?.DataContext as AlertVM;
            if (alert == null) return;
            try
            {
                bool ok = _svc.AcknowledgeAlert(alert.AlertId, _adminUserId);
                if (ok)
                {
                    _alerts.Remove(alert);
                    lblAlertCount.Text = $"{_alerts.Count} unresolved";
                    kpiAlerts.Text = _alerts.Count.ToString();
                }
            }
            catch (Exception ex)
            { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region SESSION LOGS (UC-15)
        // ═══════════════════════════════════════════════════════════
        #region Session Logs

        private void btnLoadLogs_Click(object sender, RoutedEventArgs e)
        {
            if (dpLogFrom.SelectedDate == null || dpLogTo.SelectedDate == null)
            {
                MessageBox.Show("Please select both From and To dates.", "Date Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime from = dpLogFrom.SelectedDate.Value;
            DateTime to = dpLogTo.SelectedDate.Value;
            if (from > to)
            {
                MessageBox.Show("From date cannot be after To date.", "Invalid Range",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string cat = (cboLogCategory.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (cat == "All") cat = null;

            try
            {
                var logs = _svc.GetSystemLogs(from, to, cat);
                _logs.Clear();
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
                MessageBox.Show($"Error loading logs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region REPORTS (UC-18)
        // ═══════════════════════════════════════════════════════════
        #region Reports

        private void btnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (dpFromDate.SelectedDate == null || dpToDate.SelectedDate == null)
            {
                MessageBox.Show("Please select From and To dates.", "Date Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime from = dpFromDate.SelectedDate.Value;
            DateTime to = dpToDate.SelectedDate.Value;

            string type = (cboReportType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Session Usage";

            try
            {
                var data = _svc.GetSessionReport(from, to);
                txtReportOutput.Text = BuildReportText(type, data, from, to);
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
            sb.AppendLine($"{'═',-72}");
            sb.AppendLine($"  {type.ToUpper()} REPORT");
            sb.AppendLine($"  Period  : {from:yyyy-MM-dd}  →  {to:yyyy-MM-dd}");
            sb.AppendLine($"  Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}  by  {_adminUsername}");
            sb.AppendLine($"{'═',-72}");
            sb.AppendLine();

            switch (type)
            {
                case "Session Usage":
                    sb.AppendLine($"  Total Sessions   : {data.TotalSessions}");
                    sb.AppendLine($"  Total Hours      : {data.TotalHours:F2}");
                    sb.AppendLine($"  Total Revenue    : ${data.TotalRevenue:F2}");
                    sb.AppendLine();
                    sb.AppendLine($"  {"User",-18} {"Client",-12} {"Duration",8}  {"Billing",10}  Status");
                    sb.AppendLine(new string('─', 70));
                    foreach (var s in data.Sessions ?? Array.Empty<SessionInfo>())
                        sb.AppendLine($"  {s.Username,-18} {s.ClientCode,-12} {s.SelectedDuration,6}min  ${s.CurrentBilling,8:F2}  {s.SessionStatus}");
                    break;

                case "Billing Summary":
                    sb.AppendLine($"  Total Revenue    : ${data.TotalRevenue:F2}");
                    sb.AppendLine($"  Sessions Billed  : {data.TotalSessions}");
                    sb.AppendLine($"  Avg/Session      : ${(data.TotalSessions > 0 ? data.TotalRevenue / data.TotalSessions : 0):F2}");
                    sb.AppendLine($"  Billable Hours   : {data.TotalHours:F2}");
                    sb.AppendLine($"  Current Rate     : ${_svc.GetCurrentBillingRate():F2}/min");
                    break;

                case "Security Alerts":
                    var al = _svc.GetUnacknowledgedAlerts()
                        .Where(a => a.Timestamp >= from && a.Timestamp <= to.AddDays(1)).ToArray();
                    sb.AppendLine($"  Unresolved Alerts : {al.Length}");
                    sb.AppendLine();
                    foreach (var a in al.OrderByDescending(x => x.Timestamp))
                    {
                        sb.AppendLine($"  {a.Timestamp:yyyy-MM-dd HH:mm}  [{a.Severity}]  {a.AlertType}");
                        sb.AppendLine($"    {a.Description}");
                    }
                    break;
            }
            return sb.ToString();
        }

        private void btnExportReport_Click(object sender, RoutedEventArgs e)
        {
            string text = txtReportOutput.Text;
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("Generate"))
            {
                MessageBox.Show("Generate a report first.", "Nothing to export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, text);
                MessageBox.Show($"Exported to:\n{dlg.FileName}", "Exported",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region USER MANAGEMENT (UC-03)
        // ═══════════════════════════════════════════════════════════
        #region User Management

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
                        CreatedAt = u.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        LastLogin = u.LastLoginAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never"
                    });
                }
                lblUserCount.Text = _users.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadUsers] {ex.Message}");
            }
        }

        private void btnRegisterUser_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string fullName = txtFullName.Text.Trim();
            string password = txtPassword.Password;
            string phone = txtPhone.Text.Trim();
            string address = txtAddress.Text.Trim();

            HideUserFormMessages();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowRegError("Username and password are required.");
                return;
            }

            btnRegisterUser.IsEnabled = false;
            btnRegisterUser.Content = "Registering…";
            try
            {
                var resp = _svc.RegisterClientUser(username, fullName, password, phone, address, _adminUserId);
                if (!resp.Success)
                { ShowRegError(resp.ErrorMessage ?? "Registration failed."); return; }

                ShowRegSuccess($"✓ User '{username}' registered successfully (ID: {resp.UserId})");
                ClearRegistrationForm();
                LoadClientUsers();
            }
            catch (Exception ex)
            { ShowRegError($"Connection error: {ex.Message}"); }
            finally
            {
                btnRegisterUser.IsEnabled = true;
                btnRegisterUser.Content = "+ Register User";
            }
        }

        private void btnClearForm_Click(object sender, RoutedEventArgs e) => ClearRegistrationForm();

        private void btnGeneratePassword_Click(object sender, RoutedEventArgs e)
        {
            txtPassword.Password = "User@123456";
            MessageBox.Show("Default password set: User@123456", "Default Password",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnRefreshUsers_Click(object sender, RoutedEventArgs e) => LoadClientUsers();

        private void ClearRegistrationForm()
        {
            txtUsername.Clear();
            txtFullName.Clear();
            txtPassword.Clear();
            txtPhone.Clear();
            txtAddress.Clear();
            HideUserFormMessages();
        }

        private void ShowRegError(string msg)
        {
            lblRegError.Text = msg;
            regErrorBorder.Visibility = Visibility.Visible;
            regSuccessBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowRegSuccess(string msg)
        {
            lblRegSuccess.Text = msg;
            regSuccessBorder.Visibility = Visibility.Visible;
            regErrorBorder.Visibility = Visibility.Collapsed;
        }

        private void HideUserFormMessages()
        {
            regErrorBorder.Visibility = Visibility.Collapsed;
            regSuccessBorder.Visibility = Visibility.Collapsed;
        }

        // Inline actions
        private void btnEditUserInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowUserActionError("Unable to get user data."); return; }

            var editWindow = new EditUserWindow(selected);
            if (editWindow.ShowDialog() == true)
            {
                try
                {
                    var resp = _svc.UpdateClientUser(selected.UserId, editWindow.FullName,
                        editWindow.Phone, editWindow.Address, _adminUserId);
                    if (!resp.Success) { ShowUserActionError(resp.ErrorMessage ?? "Update failed."); return; }
                    ShowUserActionSuccess($"✓ User '{selected.Username}' updated successfully.");
                    LoadClientUsers();
                }
                catch (Exception ex) { ShowUserActionError($"Error: {ex.Message}"); }
            }
        }

        private void btnResetPasswordInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowUserActionError("Unable to get user data."); return; }

            var resetWindow = new ResetPasswordWindow(selected.Username);
            if (resetWindow.ShowDialog() == true)
            {
                try
                {
                    var resp = _svc.ResetClientUserPassword(selected.UserId, resetWindow.NewPassword, _adminUserId);
                    if (!resp.Success) { ShowUserActionError(resp.ErrorMessage ?? "Reset failed."); return; }
                    ShowUserActionSuccess($"✓ Password for '{selected.Username}' reset successfully.");
                }
                catch (Exception ex) { ShowUserActionError($"Error: {ex.Message}"); }
            }
        }

        private void btnToggleStatusInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowUserActionError("Unable to get user data."); return; }

            string newStatus = selected.Status == "Active" ? "Disabled" : "Active";
            var result = MessageBox.Show(
                $"Change '{selected.Username}' status from {selected.Status} to {newStatus}?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                var resp = _svc.ToggleUserStatus(selected.UserId, _adminUserId);
                if (!resp.Success) { ShowUserActionError(resp.ErrorMessage ?? "Toggle failed."); return; }
                ShowUserActionSuccess($"✓ '{selected.Username}' is now {resp.NewStatus}.");
                LoadClientUsers();
            }
            catch (Exception ex) { ShowUserActionError($"Error: {ex.Message}"); }
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

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region BILLING RATES
        // ═══════════════════════════════════════════════════════════
        #region Billing Rates

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

        private void btnRefreshBillingRates_Click(object sender, RoutedEventArgs e) => LoadBillingRates();

        private void btnAddBillingRate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRateName.Text))
            { ShowBillingRateError("Rate name is required."); return; }
            if (!decimal.TryParse(txtRatePerMinute.Text, out decimal rate) || rate < 0)
            { ShowBillingRateError("Rate must be a valid positive number."); return; }

            btnAddBillingRate.IsEnabled = false;
            try
            {
                string currency = (cboCurrency.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "USD";
                int newId = _svc.InsertBillingRate(txtRateName.Text.Trim(), rate, currency,
                    dpEffectiveFrom.SelectedDate, dpEffectiveTo.SelectedDate,
                    chkIsDefault.IsChecked ?? false, _adminUserId, txtNotes.Text.Trim());

                if (newId <= 0) { ShowBillingRateError("Failed to insert billing rate."); return; }
                ShowBillingRateSuccess($"✓ Rate '{txtRateName.Text}' added (ID: {newId})");
                ClearBillingRateForm();
                LoadBillingRates();
            }
            catch (Exception ex) { ShowBillingRateError($"Error: {ex.Message}"); }
            finally { btnAddBillingRate.IsEnabled = true; }
        }

        private void btnEditBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

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
        }

        private void btnUpdateBillingRate_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedBillingRateId.HasValue) { ShowBillingRateError("No rate selected."); return; }
            if (string.IsNullOrWhiteSpace(txtRateName.Text)) { ShowBillingRateError("Rate name required."); return; }
            if (!decimal.TryParse(txtRatePerMinute.Text, out decimal rate) || rate < 0)
            { ShowBillingRateError("Invalid rate value."); return; }

            btnUpdateBillingRate.IsEnabled = false;
            try
            {
                string currency = (cboCurrency.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "USD";
                bool success = _svc.UpdateBillingRate(_selectedBillingRateId.Value, txtRateName.Text.Trim(),
                    rate, currency, dpEffectiveFrom.SelectedDate, dpEffectiveTo.SelectedDate,
                    chkIsActive.IsChecked ?? true, chkIsDefault.IsChecked ?? false, txtNotes.Text.Trim());

                if (!success) { ShowBillingRateError("Update failed."); return; }
                ShowBillingRateSuccess($"✓ Rate '{txtRateName.Text}' updated.");
                ClearBillingRateForm();
                LoadBillingRates();
            }
            catch (Exception ex) { ShowBillingRateError($"Error: {ex.Message}"); }
            finally
            {
                btnUpdateBillingRate.IsEnabled = true;
                btnUpdateBillingRate.Visibility = Visibility.Collapsed;
                btnAddBillingRate.Visibility = Visibility.Visible;
            }
        }

        private void btnSetDefaultBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            var result = MessageBox.Show($"Set '{rate.Name}' as the default rate?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                bool success = _svc.SetDefaultBillingRate(rate.BillingRateId);
                if (success) { ShowBillingRateSuccess($"✓ '{rate.Name}' is now the default rate."); LoadBillingRates(); }
                else ShowBillingRateError("Failed to set default rate.");
            }
            catch (Exception ex) { ShowBillingRateError($"Error: {ex.Message}"); }
        }

        private void btnDeleteBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            var result = MessageBox.Show($"Delete rate '{rate.Name}'? This cannot be undone.",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                bool success = _svc.DeleteBillingRate(rate.BillingRateId);
                if (success) { ShowBillingRateSuccess($"✓ Rate '{rate.Name}' deleted."); LoadBillingRates(); }
                else ShowBillingRateError("Cannot delete: at least one rate and one default must exist.");
            }
            catch (Exception ex) { ShowBillingRateError($"Error: {ex.Message}"); }
        }

        private void btnClearBillingRateForm_Click(object sender, RoutedEventArgs e) => ClearBillingRateForm();

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
            billingErrorBorder.Visibility = Visibility.Collapsed;
            billingSuccessBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowBillingRateError(string msg)
        {
            lblBillingRateError.Text = msg;
            billingErrorBorder.Visibility = Visibility.Visible;
            billingSuccessBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowBillingRateSuccess(string msg)
        {
            lblBillingRateSuccess.Text = msg;
            billingSuccessBorder.Visibility = Visibility.Visible;
            billingErrorBorder.Visibility = Visibility.Collapsed;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region AUTO REFRESH & WCF CALLBACKS
        // ═══════════════════════════════════════════════════════════
        #region Auto Refresh & Callbacks

        private void LoadAll()
        {
            LoadActiveSessions();
            LoadClients();
            LoadAlerts();
            if (_currentPage == "users") LoadClientUsers();
            if (_currentPage == "billing") LoadBillingRates();
            UpdateKPIs();
            UpdateKanban();
        }

        private void AutoRefresh()
        {
            try
            {
                LoadActiveSessions();
                LoadAlerts();
                UpdateKPIs();
                if (_currentPage == "dashboard") UpdateKanban();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoRefresh] {ex.Message}");
            }
        }

        private void OnServerMessage(object sender, ServerMessageEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    string msg = e.Message ?? "";
                    if (msg.IndexOf("ALERT", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadAlerts();
                        lblLastUpdate.Text = $"Alert received {DateTime.Now:HH:mm:ss}";
                    }
                    if (msg.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
                        LoadActiveSessions();
                }
                catch { }
            }));
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region DASHBOARD DISPLAY
        // ═══════════════════════════════════════════════════════════
        #region Dashboard Display

        private void ShowDashboard()
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
            AdminHeaderPanel.Visibility = Visibility.Visible;
            NavigateTo("dashboard");
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("Sign out of admin console?", "Confirm Sign Out",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            _refreshTimer.Stop();
            try { _svc?.UnsubscribeFromNotifications("ADMIN_" + _adminUserId); } catch { }

            _sessions.Clear(); _clients.Clear(); _alerts.Clear(); _logs.Clear();
            txtAdminUsername.Clear();
            txtAdminPassword.Clear();
            txtAdminPasswordPlain.Clear();
            loginErrorBorder.Visibility = Visibility.Collapsed;

            DashboardPanel.Visibility = Visibility.Collapsed;
            AdminHeaderPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
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
            catch { }
            base.OnClosing(e);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region VIEW MODELS
        // ═══════════════════════════════════════════════════════════
    }

    #region View Models

    public class ActiveSessionVM : System.ComponentModel.INotifyPropertyChanged
    {
        public int SessionId { get; set; }
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string StartTime { get; set; }
        public string Duration { get; set; }

        private string _remaining;
        public string RemainingTime
        {
            get => _remaining;
            set { _remaining = value; PC(nameof(RemainingTime)); }
        }

        private string _billing;
        public string CurrentBilling
        {
            get => _billing;
            set { _billing = value; PC(nameof(CurrentBilling)); }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set { _status = value; PC(nameof(Status)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void PC(string n) => PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(n));
    }

    public class ClientVM
    {
        public string ClientId { get; set; }
        public string MachineName { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string Location { get; set; }
        public bool IsActive { get; set; }
        public string ClientMachineStatus { get; set; }
        public string Status { get; set; }
        public string CurrentUser { get; set; }
        public string LastActive { get; set; }
    }

    public class AlertVM
    {
        public int AlertId { get; set; }
        public string Timestamp { get; set; }
        public string Severity { get; set; }
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string AlertType { get; set; }
        public string Description { get; set; }
    }

    public class LogVM
    {
        public string LogTime { get; set; }
        public string Category { get; set; }
        public string LogType { get; set; }
        public string Source { get; set; }
        public string ClientCode { get; set; }
        public string Username { get; set; }
        public string Message { get; set; }
    }

    public class UserVM
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Status { get; set; }
        public string CreatedAt { get; set; }
        public string LastLogin { get; set; }
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

    #endregion
}
