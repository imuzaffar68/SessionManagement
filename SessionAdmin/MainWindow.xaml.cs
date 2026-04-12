using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.UI;
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

        // Current active nav page
        private string _currentPage = "dashboard";

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region INITIALIZATION
        // ═══════════════════════════════════════════════════════════
        #region Initialization

        // Called by SplashWindow with an already-connected service client.
        public MainWindow(SessionServiceClient svc = null)
        {
            InitializeComponent();

            if (svc != null)
            {
                _svc = svc;
                _svc.ServerMessage += OnServerMessage;
            }

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
            HookPasswordPlaceholder(txtAdminPassword);

            // Skip if splash already connected.
            if (_svc != null && _svc.IsConnected) return;

            try
            {
                _svc = new SessionServiceClient();
                _svc.ServerMessage += OnServerMessage;

                if (!_svc.Connect())
                    AppDialog.ShowError("Cannot connect to server.", "Connection Error");
            }
            catch (Exception ex)
            {
                AppDialog.ShowError($"Init error: {ex.Message}");
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region TOUCHPAD HORIZONTAL SCROLL
        // ═══════════════════════════════════════════════════════════
        #region Touchpad Horizontal Scroll

        private const int WM_MOUSEHWHEEL = 0x020E;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            src?.AddHook(HandleTouchpadHorizontal);
        }

        private IntPtr HandleTouchpadHorizontal(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_MOUSEHWHEEL) return IntPtr.Zero;

            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (delta == 0) return IntPtr.Zero;

            int lp = lParam.ToInt32();
            var screenPt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var clientPt = PointFromScreen(screenPt);

            var hit = InputHitTest(clientPt) as DependencyObject;
            if (hit == null) return IntPtr.Zero;

            var dg = FindAncestorOrSelf<DataGrid>(hit);
            if (dg == null) return IntPtr.Zero;

            var sv = FindDescendant<ScrollViewer>(dg);
            if (sv == null) return IntPtr.Zero;

            sv.ScrollToHorizontalOffset(sv.HorizontalOffset + delta / 3.0);
            handled = true;
            return IntPtr.Zero;
        }

        private static T FindAncestorOrSelf<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T t) return t;
                obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        private static T FindDescendant<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj is T t) return t;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                var result = FindDescendant<T>(System.Windows.Media.VisualTreeHelper.GetChild(obj, i));
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region CONNECTION STATUS
        // ═══════════════════════════════════════════════════════════
        #region Connection Status

        private void UpdateConnectionStatus()
        {
            bool connected = _svc != null && _svc.IsConnected;
            var dot   = connected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed;
            var label = connected ? "Connected" : "Disconnected";
            var retry = connected ? Visibility.Collapsed : Visibility.Visible;

            // Login panel
            ellipseConnectionStatus.Fill   = dot;  lblConnectionStatus.Text   = label;  btnConnect.Visibility        = retry;
            // Sidebar
            ellipseSidebarConnection.Fill  = dot;  lblSidebarConnection.Text  = label;  btnSidebarConnect.Visibility = retry;
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_svc != null && !_svc.Connect())
                AppDialog.ShowError("Failed to connect to server.", "Connection Error");
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

            switch (page)
            {
                case "dashboard":
                    PageDashboard.Visibility = Visibility.Visible;
                    btnNavDashboard.IsChecked = true;
                    lblPageTitle.Text = "Dashboard";
                    lblPageSubtitle.Text = " — Live Overview";
                    LoadDashboard();
                    break;

                case "sessions":
                    PageSessions.Visibility = Visibility.Visible;
                    btnNavSessions.IsChecked = true;
                    lblPageTitle.Text = "Active Sessions";
                    lblPageSubtitle.Text = " — Live Monitoring";
                    LoadActiveSessions();
                    break;

                case "clients":
                    PageClients.Visibility = Visibility.Visible;
                    btnNavClients.IsChecked = true;
                    lblPageTitle.Text = "Client Machines";
                    lblPageSubtitle.Text = " — Machine Status";
                    LoadClients();
                    break;

                case "users":
                    PageUsers.Visibility = Visibility.Visible;
                    btnNavUsers.IsChecked = true;
                    lblPageTitle.Text = "User Management";
                    lblPageSubtitle.Text = " — Client Accounts";
                    LoadClientUsers();
                    break;

                case "billing":
                    PageBilling.Visibility = Visibility.Visible;
                    btnNavBilling.IsChecked = true;
                    lblPageTitle.Text = "Billing Rates";
                    lblPageSubtitle.Text = " — Rate Configuration";
                    LoadBillingRates();
                    break;

                case "alerts":
                    PageAlerts.Visibility = Visibility.Visible;
                    btnNavAlerts.IsChecked = true;
                    lblPageTitle.Text = "Security Alerts";
                    lblPageSubtitle.Text = " — Threat Detection";
                    LoadAlerts();
                    break;

                case "logs":
                    PageLogs.Visibility = Visibility.Visible;
                    btnNavLogs.IsChecked = true;
                    lblPageTitle.Text = "Session Logs";
                    lblPageSubtitle.Text = " — Activity History";
                    break;

                case "reports":
                    PageReports.Visibility = Visibility.Visible;
                    btnNavReports.IsChecked = true;
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

        // Fix: PasswordBox.Password is not a DP so XAML triggers can't hide the placeholder
        // once content has been typed. Hook PasswordChanged to manage Visibility directly.
        private static void HookPasswordPlaceholder(System.Windows.Controls.PasswordBox pb)
        {
            pb.PasswordChanged += (s, _) =>
            {
                var box = (System.Windows.Controls.PasswordBox)s;
                var ph = FindVisualChild<System.Windows.Controls.TextBlock>(box, "Placeholder");
                if (ph == null) return;
                if (box.Password.Length > 0)
                    ph.Visibility = Visibility.Collapsed;
                else
                    ph.ClearValue(UIElement.VisibilityProperty);
            };
        }

        private static T FindVisualChild<T>(System.Windows.DependencyObject parent, string name)
            where T : System.Windows.FrameworkElement
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name) return t;
                var found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

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
                        StartTime = s.StartTime.ToString("hh:mm:ss tt"),
                        Duration = $"{s.SelectedDuration} min",
                        RemainingTime = $"{s.RemainingMinutes} min",
                        CurrentBilling = $"${s.CurrentBilling:F2}",
                        Status = s.SessionStatus,
                        ImagePath = s.ImagePath
                    });
                }
                lblActiveCount.Text = $"{_sessions.Count} sessions";
                lblLastUpdate.Text = $"Updated {DateTime.Now:hh:mm:ss tt}";
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

            if (!AppDialog.Confirm($"Terminate session {session.SessionId} for '{session.Username}'?", "Confirm Termination")) return;

            try
            {
                bool ok = _svc.EndSession(session.SessionId, "Admin");
                if (ok)
                {
                    _sessions.Remove(session);
                    lblActiveCount.Text = $"{_sessions.Count} sessions";
                    AppDialog.ShowInfo($"Session {session.SessionId} for '{session.Username}' terminated.", "Session Terminated");
                }
                else
                    AppDialog.ShowError("Failed to terminate session.");
            }
            catch (Exception ex)
            {
                AppDialog.ShowError($"Error: {ex.Message}");
            }
        }

        private void btnViewImage_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgActiveSessions.SelectedItem as ActiveSessionVM;
            if (selected == null)
            {
                AppDialog.ShowInfo("Please select a session first.", "No Selection");
                return;
            }

            try
            {
                string b64 = _svc.DownloadLoginImage(selected.SessionId);
                if (string.IsNullOrEmpty(b64))
                {
                    AppDialog.ShowInfo("No image available for this session.", "No Image");
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
                AppDialog.ShowError($"Error loading image: {ex.Message}");
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
                        LastActive = c.LastActiveTime?.ToString("MM/dd/yyyy hh:mm tt") ?? "Never"
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
                {
                    LoadClients();
                    AppDialog.ShowInfo($"Client '{client.ClientId}' enabled.", "Client Enabled");
                }
                else
                    AppDialog.ShowError("Failed to enable client.");
            }
            catch (Exception ex)
            { AppDialog.ShowError($"Error: {ex.Message}"); }
        }

        private void btnDisableClient_Click(object sender, RoutedEventArgs e)
        {
            var client = (sender as Button)?.DataContext as ClientVM;
            if (client == null) return;
            try
            {
                if (_svc.UpdateClientMachineIsActive(client.ClientId, false))
                {
                    LoadClients();
                    AppDialog.ShowInfo($"Client '{client.ClientId}' disabled.", "Client Disabled");
                }
                else
                    AppDialog.ShowError("Failed to disable client.");
            }
            catch (Exception ex)
            { AppDialog.ShowError($"Error: {ex.Message}"); }
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
                        Timestamp = a.Timestamp.ToString("MM/dd/yyyy hh:mm tt"),
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
            { AppDialog.ShowError($"Error: {ex.Message}"); }
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
                AppDialog.ShowWarning("Please select both From and To dates.", "Date Required");
                return;
            }

            DateTime from = dpLogFrom.SelectedDate.Value;
            DateTime to = dpLogTo.SelectedDate.Value;
            if (from > to)
            {
                AppDialog.ShowWarning("From date cannot be after To date.", "Invalid Range");
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
                        LogTime = log.LoggedAt.ToString("MM/dd/yyyy hh:mm tt"),
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
                AppDialog.ShowError($"Error loading logs: {ex.Message}");
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
                AppDialog.ShowWarning("Please select From and To dates.", "Date Required");
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
                AppDialog.ShowError($"Error generating report: {ex.Message}");
            }
        }

        private string BuildReportText(string type, ReportData data, DateTime from, DateTime to)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{'═',-72}");
            sb.AppendLine($"  {type.ToUpper()} REPORT");
            sb.AppendLine($"  Period  : {from:yyyy-MM-dd}  →  {to:yyyy-MM-dd}");
            sb.AppendLine($"  Generated : {DateTime.Now:MM/dd/yyyy hh:mm:ss tt}  by  {_adminUsername}");
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
                        sb.AppendLine($"  {a.Timestamp:MM/dd/yyyy hh:mm tt}  [{a.Severity}]  {a.AlertType}");
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
                AppDialog.ShowInfo("Generate a report first.", "Nothing to export");
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
                AppDialog.ShowInfo($"Exported to:\n{dlg.FileName}", "Exported");
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
                        CreatedAt = u.CreatedAt.ToString("MM/dd/yyyy hh:mm tt"),
                        LastLogin = u.LastLoginAt?.ToString("MM/dd/yyyy hh:mm tt") ?? "Never"
                    });
                }
                lblUserCount.Text = _users.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadUsers] {ex.Message}");
            }
        }

        private void btnRefreshUsers_Click(object sender, RoutedEventArgs e) => LoadClientUsers();

        private void btnAddNewUser_Click(object sender, RoutedEventArgs e)
        {
            var win = new UserFormWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                try
                {
                    var resp = _svc.RegisterClientUser(win.Username, win.FullName, win.Password,
                        win.Phone, win.Address, _adminUserId);
                    if (!resp.Success) { ShowUserActionError(resp.ErrorMessage ?? "Registration failed."); return; }
                    ShowUserActionSuccess($"✓ User '{win.Username}' registered successfully (ID: {resp.UserId})");
                    LoadClientUsers();
                }
                catch (Exception ex) { ShowUserActionError($"Connection error: {ex.Message}"); }
            }
        }

        // Inline actions
        private void btnEditUserInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowUserActionError("Unable to get user data."); return; }

            var editWindow = new UserFormWindow(selected) { Owner = this };
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
            if (!AppDialog.Confirm($"Change '{selected.Username}' status from {selected.Status} to {newStatus}?")) return;

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

        private void btnAddNewRate_Click(object sender, RoutedEventArgs e)
        {
            var win = new BillingRateFormWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                try
                {
                    int newId = _svc.InsertBillingRate(win.RateName, win.RatePerMinute, win.Currency,
                        win.EffectiveFrom, win.EffectiveTo, win.IsDefault, _adminUserId, win.Notes);
                    if (newId == -2) { ShowBillingActionError("A billing rate with that name already exists."); return; }
                    if (newId == -3) { ShowBillingActionError("Date range overlaps an existing active rate for the same currency."); return; }
                    if (newId <= 0)  { ShowBillingActionError("Failed to insert billing rate."); return; }
                    ShowBillingActionSuccess($"✓ Rate '{win.RateName}' added (ID: {newId})");
                    LoadBillingRates();
                }
                catch (Exception ex) { ShowBillingActionError($"Error: {ex.Message}"); }
            }
        }

        private void btnEditBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            var win = new BillingRateFormWindow(rate) { Owner = this };
            if (win.ShowDialog() == true)
            {
                try
                {
                    bool success = _svc.UpdateBillingRate(rate.BillingRateId, win.RateName, win.RatePerMinute,
                        win.Currency, win.EffectiveFrom, win.EffectiveTo, win.IsActive, win.IsDefault, win.Notes);
                    if (!success) { ShowBillingActionError("Update failed — check for duplicate name or overlapping date range."); return; }
                    ShowBillingActionSuccess($"✓ Rate '{win.RateName}' updated.");
                    LoadBillingRates();
                }
                catch (Exception ex) { ShowBillingActionError($"Error: {ex.Message}"); }
            }
        }

        private void btnSetDefaultBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            if (!AppDialog.Confirm($"Set '{rate.Name}' as the default rate?")) return;

            try
            {
                bool success = _svc.SetDefaultBillingRate(rate.BillingRateId);
                if (success) { ShowBillingActionSuccess($"✓ '{rate.Name}' is now the default rate."); LoadBillingRates(); }
                else ShowBillingActionError("Failed to set default rate.");
            }
            catch (Exception ex) { ShowBillingActionError($"Error: {ex.Message}"); }
        }

        private void btnDeleteBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            if (!AppDialog.Confirm($"Delete rate '{rate.Name}'? This cannot be undone.", "Confirm Deletion")) return;

            try
            {
                bool success = _svc.DeleteBillingRate(rate.BillingRateId);
                if (success) { ShowBillingActionSuccess($"✓ Rate '{rate.Name}' deleted."); LoadBillingRates(); }
                else ShowBillingActionError("Cannot delete: at least one rate and one default must exist.");
            }
            catch (Exception ex) { ShowBillingActionError($"Error: {ex.Message}"); }
        }

        private void ShowBillingActionError(string msg)
        {
            lblBillingActionError.Text = msg;
            lblBillingActionError.Visibility = Visibility.Visible;
            lblBillingActionSuccess.Visibility = Visibility.Collapsed;
        }

        private void ShowBillingActionSuccess(string msg)
        {
            lblBillingActionSuccess.Text = msg;
            lblBillingActionSuccess.Visibility = Visibility.Visible;
            lblBillingActionError.Visibility = Visibility.Collapsed;
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
                        lblLastUpdate.Text = $"Alert received {DateTime.Now:hh:mm:ss tt}";
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
            MainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(220);
            LoginPanel.Visibility = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
            AdminHeaderPanel.Visibility = Visibility.Visible;
            SidebarNav.Visibility = Visibility.Visible;
            NavigateTo("dashboard");
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDialog.Confirm("Sign out of admin console?", "Confirm Sign Out")) return;

            _refreshTimer.Stop();
            try { _svc?.UnsubscribeFromNotifications("ADMIN_" + _adminUserId); } catch { }

            _sessions.Clear(); _clients.Clear(); _alerts.Clear(); _logs.Clear();
            txtAdminUsername.Clear();
            txtAdminPassword.Clear();
            txtAdminPasswordPlain.Clear();
            loginErrorBorder.Visibility = Visibility.Collapsed;

            MainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(0);
            DashboardPanel.Visibility = Visibility.Collapsed;
            AdminHeaderPanel.Visibility = Visibility.Collapsed;
            SidebarNav.Visibility = Visibility.Collapsed;
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
        //  #region CUSTOM TITLE BAR
        // ═══════════════════════════════════════════════════════════
        #region Custom Title Bar

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                if (e.ClickCount == 2) ToggleMaximize();
                else DragMove();
            }
        }

        private void btnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                btnMaximize.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                btnMaximize.Content = "❐";
            }
        }

        private void btnWindowClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region VIEW MODELS
        // ═══════════════════════════════════════════════════════════
    }

    #region View Models

    public class ActiveSessionVM : System.ComponentModel.INotifyPropertyChanged
    {
        public int    SessionId { get; set; }
        public string ClientId  { get; set; }
        public string Username  { get; set; }
        public string StartTime { get; set; }
        public string Duration  { get; set; }

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

        private string _imagePath;
        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; PC(nameof(ImagePath)); PC(nameof(PhotoSource)); }
        }

        // Returns a BitmapImage when a file exists at ImagePath, otherwise null (cell stays empty).
        public System.Windows.Media.ImageSource PhotoSource
        {
            get
            {
                if (string.IsNullOrEmpty(_imagePath) || !System.IO.File.Exists(_imagePath))
                    return null;
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource        = new Uri(_imagePath, UriKind.Absolute);
                    bmp.CacheOption      = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 48;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { return null; }
            }
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
