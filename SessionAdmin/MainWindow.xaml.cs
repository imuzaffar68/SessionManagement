using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
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
        private DispatcherTimer _toastTimer;

        private string _adminFullname;
        private string _adminUsername;
        private int    _adminUserId;
        private string _adminProfileBase64;

        private ObservableCollection<ActiveSessionVM> _sessions = new ObservableCollection<ActiveSessionVM>();
        private ObservableCollection<ClientVM> _clients = new ObservableCollection<ClientVM>();
        private ObservableCollection<AlertVM> _alerts = new ObservableCollection<AlertVM>();
        private ObservableCollection<LogVM> _logs = new ObservableCollection<LogVM>();
        private ObservableCollection<UserVM> _users = new ObservableCollection<UserVM>();
        private ObservableCollection<BillingRateVM>    _billingRates   = new ObservableCollection<BillingRateVM>();
        private ObservableCollection<BillingRecordVM>  _billingRecords = new ObservableCollection<BillingRecordVM>();

        // Current active nav page
        private string _currentPage = "dashboard";

        // Custom-maximize state — tracks whether the window is filling the work area.
        // We do NOT use WindowState.Maximized because with WindowStyle="None" it extends
        // over the taskbar; instead we manually size the window to SystemParameters.WorkArea.
        private bool _isManuallyMaximized;
        private Rect _normalBounds;

        // Prevents concurrent background reconnect tasks from the 2-second poll.
        private bool _reconnectInProgress;

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
            dgBillingRates.ItemsSource   = _billingRates;
            dgBillingRecords.ItemsSource = _billingRecords;

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
        private DataGrid _mouseOverDataGrid;
        private bool _hScrollProcessing;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Track which DataGrid the mouse is currently over via WPF events —
            // far more reliable than hit-testing inside a WndProc hook.
            PreviewMouseMove += (s, me) =>
                _mouseOverDataGrid = FindAncestorOrSelf<DataGrid>(me.OriginalSource as DependencyObject);

            // Two hooks for belt-and-suspenders: AddHook fires for the window HWND;
            // ThreadPreprocessMessage fires for any child HWND (WPF rendering child).
            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            src?.AddHook(WndProcHook);

            ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
            Closed += (s, ev) => ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_MOUSEHWHEEL || _hScrollProcessing) return IntPtr.Zero;
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (delta == 0) return IntPtr.Zero;
            _hScrollProcessing = true;
            try { ScrollDataGridH(delta); } finally { _hScrollProcessing = false; }
            handled = true;
            return IntPtr.Zero;
        }

        private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (handled || msg.message != WM_MOUSEHWHEEL || _hScrollProcessing) return;
            int delta = (short)((msg.wParam.ToInt64() >> 16) & 0xFFFF);
            if (delta == 0) return;
            _hScrollProcessing = true;
            try { ScrollDataGridH(delta); } finally { _hScrollProcessing = false; }
            handled = true;
        }

        private void ScrollDataGridH(int delta)
        {
            var dg = _mouseOverDataGrid;
            if (dg == null || !dg.IsVisible) return;
            dg.ApplyTemplate();
            var sv = dg.Template?.FindName("DG_ScrollViewer", dg) as ScrollViewer
                     ?? FindDescendant<ScrollViewer>(dg);
            sv?.ScrollToHorizontalOffset(sv.HorizontalOffset + delta / 3.0);
        }

        private void DgScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!(sender is DataGrid dg)) return;
            var parent = VisualTreeHelper.GetParent(dg) as Grid;
            if (parent == null) return;
            bool canScroll = e.ExtentWidth > e.ViewportWidth;
            foreach (UIElement child in parent.Children)
                if (child is StackPanel sp) sp.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            var container = FindAncestorOrSelf<Grid>((Button)sender);
            ScrollDgHByButton(container, -120);
        }

        private void BtnScrollRight_Click(object sender, RoutedEventArgs e)
        {
            var container = FindAncestorOrSelf<Grid>((Button)sender);
            ScrollDgHByButton(container, +120);
        }

        private void ScrollDgHByButton(Grid container, double delta)
        {
            if (container == null) return;
            DataGrid dg = null;
            foreach (UIElement child in container.Children)
                if (child is DataGrid d) { dg = d; break; }
            if (dg == null) return;
            dg.ApplyTemplate();
            var sv = dg.Template?.FindName("DG_ScrollViewer", dg) as ScrollViewer
                     ?? FindDescendant<ScrollViewer>(dg);
            sv?.ScrollToHorizontalOffset(sv.HorizontalOffset + delta);
        }

        private static T FindAncestorOrSelf<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T t) return t;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        private static T FindDescendant<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj is T t) return t;
            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                var result = FindDescendant<T>(VisualTreeHelper.GetChild(obj, i));
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
            bool connected = _svc != null && _svc.IsChannelReady;
            var dot   = connected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed;
            var label = connected ? "Connected" : "Disconnected";
            var retry = connected ? Visibility.Collapsed : Visibility.Visible;

            // Login panel
            ellipseConnectionStatus.Fill   = dot;  lblConnectionStatus.Text   = label;  btnConnect.Visibility        = retry;
            // Sidebar
            ellipseSidebarConnection.Fill  = dot;  lblSidebarConnection.Text  = label;  btnSidebarConnect.Visibility = retry;

            // Background auto-reconnect — one attempt at a time; EnsureConnection() has
            // a built-in 10s cooldown so this never hammers the server.
            if (!connected && _svc != null && !_reconnectInProgress)
            {
                _reconnectInProgress = true;
                var svc = _svc;
                Task.Run(() =>
                {
                    try { svc.EnsureConnection(); } catch { }
                    Dispatcher.BeginInvoke(new Action(() => _reconnectInProgress = false));
                });
            }
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            btnConnect.IsEnabled         = false;
            btnSidebarConnect.IsEnabled  = false;
            ConnectingOverlay.Visibility = Visibility.Visible;

            try
            {
                var svc = _svc;
                await Task.Run(() => { try { svc?.Connect(); } catch { } });
            }
            finally
            {
                ConnectingOverlay.Visibility = Visibility.Collapsed;
                btnConnect.IsEnabled         = true;
                btnSidebarConnect.IsEnabled  = true;
                UpdateConnectionStatus();

                if (_svc?.IsChannelReady != true)
                    AppDialog.ShowError("Could not reach the server.\nPlease try again in a moment.",
                                        "Connection Failed");
            }
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
                    LoadBillingRecords();
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

        private async void btnAdminLogin_Click(object sender, RoutedEventArgs e)
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
            btnAdminLogin.Content   = "Signing in…";

            AuthenticationResponse resp = null;
            Exception loginEx           = null;

            try
            {
                var svc = _svc;
                (resp, loginEx) = await Task.Run(() =>
                {
                    try
                    {
                        var r = svc.AuthenticateUser(user, pass, "ADMIN");
                        if (r.IsAuthenticated && r.UserType == "Admin")
                            svc.SubscribeForNotifications("ADMIN_" + r.UserId);
                        return (r, (Exception)null);
                    }
                    catch (Exception ex) { return (null, ex); }
                });
            }
            finally
            {
                btnAdminLogin.IsEnabled = true;
                btnAdminLogin.Content   = "Sign In →";
            }

            if (loginEx != null)
            { ShowLoginError("Connection error: " + loginEx.Message); return; }

            if (!resp.IsAuthenticated)
            { ShowLoginError(resp.ErrorMessage ?? "Invalid credentials."); return; }

            if (resp.UserType != "Admin")
            { ShowLoginError("Access denied. Admin privileges required."); return; }

            _adminFullname      = resp.FullName;
            _adminUsername      = resp.Username;
            _adminUserId        = resp.UserId;
            _adminProfileBase64 = resp.ProfilePictureBase64;
            lblAdminUser.Text    = _adminFullname ?? _adminUsername;
            lblAdminInitial.Text = (_adminFullname ?? _adminUsername ?? "A").Substring(0, 1).ToUpper();

            if (!string.IsNullOrEmpty(_adminProfileBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(_adminProfileBase64);
                    var bmp   = new BitmapImage();
                    using (var ms = new System.IO.MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption  = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    imgAdminAvatar.Source      = bmp;
                    imgAdminAvatar.Visibility  = Visibility.Visible;
                    lblAdminInitial.Visibility = Visibility.Collapsed;
                }
                catch { /* corrupt/missing picture — show initial */ }
            }

            ShowDashboard();
            LoadAll();
            _refreshTimer.Start();
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
                        SessionId      = s.SessionId,
                        ClientId       = s.ClientCode,
                        Username       = s.Username,
                        FullName       = s.FullName,
                        StartTime      = s.StartTime.ToString("hh:mm:ss tt"),
                        Duration       = $"{s.SelectedDuration} min",
                        RemainingTime  = $"{s.RemainingMinutes} min",
                        CurrentBilling = $"${s.CurrentBilling:F2}",
                        Status         = s.SessionStatus,
                        ImagePath      = s.ImagePath
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

        private async void btnTerminateSession_Click(object sender, RoutedEventArgs e)
        {
            var session = (sender as Button)?.DataContext as ActiveSessionVM;
            if (session == null) return;

            if (!AppDialog.Confirm($"Terminate session {session.SessionId} for '{session.Username}'?", "Confirm Termination")) return;

            bool ok = false;
            Exception err = null;
            var svc = _svc;
            (ok, err) = await Task.Run(() =>
            {
                try   { return (svc.EndSession(session.SessionId, "Admin"), (Exception)null); }
                catch (Exception ex) { return (false, ex); }
            });

            if (err != null) { ShowToast($"Error: {err.Message}", "error"); return; }

            if (ok)
            {
                _sessions.Remove(session);
                lblActiveCount.Text = $"{_sessions.Count} sessions";
                ShowToast($"Session {session.SessionId} for '{session.Username}' terminated.");
            }
            else
                ShowToast("Failed to terminate session.", "error");
        }

        private async void btnViewImage_Click(object sender, RoutedEventArgs e)
        {
            var session = ((Button)sender).DataContext as ActiveSessionVM;
            if (session == null) return;

            string b64 = null;
            Exception err = null;
            var svc = _svc;
            (b64, err) = await Task.Run(() =>
            {
                try   { return (svc.DownloadLoginImage(session.SessionId), (Exception)null); }
                catch (Exception ex) { return ((string)null, ex); }
            });

            if (err != null) { ShowToast($"Error loading image: {err.Message}", "error"); return; }

            if (string.IsNullOrEmpty(b64))
            {
                ShowToast("No login image available for this session.", "warning");
                return;
            }

            try
            {
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
                lblImageTitle.Text = $"Session {session.SessionId} — {session.Username}";
                ImageViewerPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { ShowToast($"Error decoding image: {ex.Message}", "error"); }
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
                        ClientId             = c.ClientCode,
                        MachineName          = c.MachineName,
                        IpAddress            = c.IpAddress,
                        MacAddress           = c.MacAddress,
                        Location             = c.Location,
                        IsActive             = c.IsActive,
                        ClientMachineStatus  = c.IsActive ? "Active" : "Inactive",
                        Status               = c.Status,
                        CurrentUser          = c.CurrentUser ?? "—",
                        LastActive           = c.LastActiveTime?.ToString("MM/dd/yyyy hh:mm tt") ?? "Never",
                        MissedHeartbeats     = c.MissedHeartbeats
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
                { LoadClients(); ShowToast($"Client '{client.ClientId}' enabled."); }
                else
                    ShowToast($"Failed to enable client '{client.ClientId}'.", "error");
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
        }

        private void btnDisableClient_Click(object sender, RoutedEventArgs e)
        {
            var client = (sender as Button)?.DataContext as ClientVM;
            if (client == null) return;
            try
            {
                if (_svc.UpdateClientMachineIsActive(client.ClientId, false))
                { LoadClients(); ShowToast($"Client '{client.ClientId}' disabled.", "warning"); }
                else
                    ShowToast($"Failed to disable client '{client.ClientId}'.", "error");
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
        }

        /// <summary>
        /// Opens EditMachineWindow with the selected machine's current name/location.
        /// The WCF save call happens inside the window so it stays open on failure.
        /// </summary>
        private void btnEditMachine_Click(object sender, RoutedEventArgs e)
        {
            var client = (sender as Button)?.DataContext as ClientVM;
            if (client == null) return;

            var win = new EditMachineWindow(client,
                (name, location) => _svc.UpdateClientMachineInfo(client.ClientId, name, location))
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
            {
                LoadClients();
                ShowToast($"Machine '{client.ClientId}' updated.");
            }
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

        private async void btnAcknowledgeAlert_Click(object sender, RoutedEventArgs e)
        {
            var alert = (sender as Button)?.DataContext as AlertVM;
            if (alert == null) return;
            if (!AppDialog.Confirm(
                    $"Acknowledge alert #{alert.AlertId}?\n\nType: {alert.AlertType}\nClient: {alert.ClientId}",
                    "Confirm Acknowledge")) return;

            bool ok = false;
            Exception err = null;
            var svc = _svc;
            int alertId = alert.AlertId;
            int adminId = _adminUserId;
            (ok, err) = await Task.Run(() =>
            {
                try   { return (svc.AcknowledgeAlert(alertId, adminId), (Exception)null); }
                catch (Exception ex) { return (false, ex); }
            });

            if (err != null) { ShowToast($"Error: {err.Message}", "error"); return; }

            if (ok)
            {
                _alerts.Remove(alert);
                lblAlertCount.Text = $"{_alerts.Count} unresolved";
                kpiAlerts.Text = _alerts.Count.ToString();
                ShowToast($"Alert #{alertId} acknowledged.");
            }
            else
                ShowToast("Failed to acknowledge alert.", "error");
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region SESSION LOGS (UC-15)
        // ═══════════════════════════════════════════════════════════
        #region Session Logs

        private async void btnLoadLogs_Click(object sender, RoutedEventArgs e)
        {
            if (dpLogFrom.SelectedDate == null || dpLogTo.SelectedDate == null)
            { ShowToast("Please select both From and To dates.", "warning"); return; }

            DateTime from = dpLogFrom.SelectedDate.Value;
            DateTime to   = dpLogTo.SelectedDate.Value;
            if (from > to)
            { ShowToast("From date cannot be after To date.", "warning"); return; }

            string cat = (cboLogCategory.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (cat == "All") cat = null;

            SessionManagement.WCF.SystemLogInfo[] logs = null;
            Exception err = null;
            var svc = _svc;
            (logs, err) = await Task.Run(() =>
            {
                try   { return (svc.GetSystemLogs(from, to, cat), (Exception)null); }
                catch (Exception ex) { return ((SessionManagement.WCF.SystemLogInfo[])null, ex); }
            });

            if (err != null) { ShowToast($"Error loading logs: {err.Message}", "error"); return; }

            _logs.Clear();
            foreach (var log in logs)
            {
                _logs.Add(new LogVM
                {
                    LogTime    = log.LoggedAt.ToString("MM/dd/yyyy hh:mm tt"),
                    Category   = log.Category,
                    LogType    = log.Type,
                    Source     = log.Source    ?? "—",
                    ClientCode = log.ClientCode ?? "—",
                    Username   = log.Username   ?? "—",
                    Message    = log.Message
                });
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region REPORTS (UC-18)
        // ═══════════════════════════════════════════════════════════
        #region Reports

        private async void btnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (dpFromDate.SelectedDate == null || dpToDate.SelectedDate == null)
            { ShowToast("Please select From and To dates.", "warning"); return; }

            DateTime from = dpFromDate.SelectedDate.Value;
            DateTime to   = dpToDate.SelectedDate.Value;

            string type = (cboReportType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Session Usage";

            ReportData data = null;
            Exception err = null;
            var svc = _svc;
            (data, err) = await Task.Run(() =>
            {
                try   { return (svc.GetSessionReport(from, to), (Exception)null); }
                catch (Exception ex) { return ((ReportData)null, ex); }
            });

            if (err != null) { ShowToast($"Error generating report: {err.Message}", "error"); return; }

            txtReportOutput.Text = BuildReportText(type, data, from, to);
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
            { ShowToast("Generate a report first before exporting.", "warning"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, text);
                    ShowToast($"Report exported to: {System.IO.Path.GetFileName(dlg.FileName)}");
                }
                catch (Exception ex) { ShowToast($"Export failed: {ex.Message}", "error"); }
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
                        UserId               = u.UserId,
                        Username             = u.Username,
                        FullName             = u.FullName,
                        Phone                = u.Phone,
                        Address              = u.Address,
                        Status               = u.Status,
                        CreatedAt            = u.CreatedAt.ToString("MM/dd/yyyy hh:mm tt"),
                        LastLogin            = u.LastLoginAt?.ToString("MM/dd/yyyy hh:mm tt") ?? "Never",
                        ProfilePictureBase64 = u.ProfilePictureBase64
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
            var win = new UserFormWindow(
                form =>
                {
                    var resp = _svc.RegisterClientUser(form.Username, form.FullName, form.Password,
                        form.Phone, form.Address, _adminUserId, form.ProfilePictureBase64);
                    return resp.Success ? null : (resp.ErrorMessage ?? "Registration failed.");
                },
                msg => ShowToast(msg))
            { Owner = this };

            if (win.ShowDialog() == true) LoadClientUsers();
        }

        // Inline actions
        private void btnEditUserInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowToast("Unable to get user data.", "error"); return; }

            var win = new UserFormWindow(selected,
                form =>
                {
                    var resp = _svc.UpdateClientUser(selected.UserId, form.FullName,
                        form.Phone, form.Address, _adminUserId, form.ProfilePictureBase64);
                    return resp.Success ? null : (resp.ErrorMessage ?? "Update failed.");
                },
                msg => ShowToast(msg))
            { Owner = this };

            if (win.ShowDialog() == true) LoadClientUsers();
        }

        private void btnResetPasswordInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowToast("Unable to get user data.", "error"); return; }

            var win = new ResetPasswordWindow(selected.Username,
                pwd =>
                {
                    var resp = _svc.ResetClientUserPassword(selected.UserId, pwd, _adminUserId);
                    return resp.Success ? null : (resp.ErrorMessage ?? "Reset failed.");
                },
                msg => ShowToast(msg))
            { Owner = this };

            win.ShowDialog();
        }

        private void btnDeleteUserInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowToast("Unable to get user data.", "error"); return; }

            if (!AppDialog.Confirm($"Permanently delete '{selected.Username}'? This cannot be undone.")) return;

            try
            {
                var resp = _svc.DeleteClientUser(selected.UserId, _adminUserId);
                if (!resp.Success) { ShowToast(resp.ErrorMessage ?? "Delete failed.", "error"); return; }
                ShowToast($"User '{selected.Username}' deleted.");
                LoadClientUsers();
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
        }

        private void btnToggleStatusInline_Click(object sender, RoutedEventArgs e)
        {
            var selected = (sender as Button)?.DataContext as UserVM;
            if (selected == null) { ShowToast("Unable to get user data.", "error"); return; }

            string newStatus = selected.Status == "Active" ? "Disabled" : "Active";
            if (!AppDialog.Confirm($"Change '{selected.Username}' status from {selected.Status} to {newStatus}?")) return;

            try
            {
                var resp = _svc.ToggleUserStatus(selected.UserId, _adminUserId);
                if (!resp.Success) { ShowToast(resp.ErrorMessage ?? "Toggle failed.", "error"); return; }
                string toastType = resp.NewStatus == "Active" ? "success" : "warning";
                ShowToast($"'{selected.Username}' is now {resp.NewStatus}.", toastType);
                LoadClientUsers();
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
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
            var win = new BillingRateFormWindow(
                form =>
                {
                    int newId = _svc.InsertBillingRate(form.RateName, form.RatePerMinute, form.Currency,
                        form.EffectiveFrom, form.EffectiveTo, form.IsDefault, _adminUserId, form.Notes);
                    if (newId == -2) return "A billing rate with that name already exists.";
                    if (newId == -3) return "Date range overlaps an existing active rate for the same currency.";
                    if (newId <= 0)  return "Failed to insert billing rate.";
                    return null;
                },
                msg => ShowToast(msg))
            { Owner = this };

            if (win.ShowDialog() == true) LoadBillingRates();
        }

        private void btnEditBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            var win = new BillingRateFormWindow(rate,
                form =>
                {
                    bool ok = _svc.UpdateBillingRate(rate.BillingRateId, form.RateName, form.RatePerMinute,
                        form.Currency, form.EffectiveFrom, form.EffectiveTo, form.IsActive, form.IsDefault, form.Notes);
                    return ok ? null : "Update failed — check for duplicate name or overlapping date range.";
                },
                msg => ShowToast(msg))
            { Owner = this };

            if (win.ShowDialog() == true) LoadBillingRates();
        }

        private void btnSetDefaultBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            if (!AppDialog.Confirm($"Set '{rate.Name}' as the default rate?")) return;

            try
            {
                bool success = _svc.SetDefaultBillingRate(rate.BillingRateId);
                if (success) { ShowToast($"'{rate.Name}' is now the default rate."); LoadBillingRates(); }
                else ShowToast("Failed to set default rate.", "error");
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
        }

        private void btnDeleteBillingRate_Click(object sender, RoutedEventArgs e)
        {
            var rate = (sender as Button)?.DataContext as BillingRateVM;
            if (rate == null) return;

            if (!AppDialog.Confirm($"Delete rate '{rate.Name}'? This cannot be undone.", "Confirm Deletion")) return;

            try
            {
                bool success = _svc.DeleteBillingRate(rate.BillingRateId);
                if (success) { ShowToast($"Rate '{rate.Name}' deleted."); LoadBillingRates(); }
                else ShowToast("Cannot delete: at least one rate and one default must exist.", "error");
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
        }

        private void LoadBillingRecords()
        {
            if (_svc == null) return;
            try
            {
                bool unpaidOnly = chkUnpaidOnly.IsChecked == true;
                var records = _svc.GetBillingRecords(unpaidOnly);
                _billingRecords.Clear();
                foreach (var r in records)
                {
                    _billingRecords.Add(new BillingRecordVM
                    {
                        BillingRecordId = r.BillingRecordId,
                        SessionId       = r.SessionId,
                        Username        = r.Username,
                        MachineCode     = r.MachineCode,
                        BillableMinutes = r.BillableMinutes,
                        Amount          = r.Amount,
                        Currency        = r.Currency,
                        CalculatedAt    = r.CalculatedAt,
                        IsPaid          = r.IsPaid,
                        PaidAt          = r.PaidAt
                    });
                }
                int unpaidCount = _billingRecords.Count(x => !x.IsPaid);
                lblUnpaidCount.Text = unpaidCount.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadBillingRecords] {ex.Message}");
            }
        }

        private void btnRefreshPayments_Click(object sender, RoutedEventArgs e) => LoadBillingRecords();

        private void chkUnpaidOnly_Changed(object sender, RoutedEventArgs e) => LoadBillingRecords();

        private void btnMarkPaidInline_Click(object sender, RoutedEventArgs e)
        {
            var record = (sender as Button)?.DataContext as BillingRecordVM;
            if (record == null) return;
            if (!AppDialog.Confirm(
                    $"Mark session #{record.SessionId} as paid?\n\nUser: {record.Username}\nAmount: {record.AmountDisplay}",
                    "Confirm Payment")) return;

            try
            {
                bool ok = _svc.MarkBillingRecordPaid(record.BillingRecordId, _adminUserId);
                if (ok) { ShowToast($"Session #{record.SessionId} marked as paid."); LoadBillingRecords(); }
                else ShowToast("Could not mark as paid. Record may not exist or is already paid.", "error");
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", "error"); }
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
            if (_currentPage == "billing") { LoadBillingRates(); LoadBillingRecords(); }
            UpdateKPIs();
            UpdateKanban();
        }

        private void AutoRefresh()
        {
            // Skip when offline — prevents the 5-second DispatcherTimer from blocking the
            // UI thread for up to SendTimeout (10s) when the server is unreachable.
            if (_svc?.IsChannelReady != true) return;

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
                    if (msg.StartsWith("SESSION_STARTED:"))
                    {
                        string info = msg.Substring("SESSION_STARTED:".Length);
                        LoadActiveSessions();
                        UpdateKPIs();
                        if (_currentPage == "dashboard") UpdateKanban();
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.AdminAppId,
                            "Session Started",
                            info);
                    }
                    else if (msg.StartsWith("SESSION_ENDED:"))
                    {
                        string info = msg.Substring("SESSION_ENDED:".Length);
                        LoadActiveSessions();
                        UpdateKPIs();
                        if (_currentPage == "dashboard") UpdateKanban();
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.AdminAppId,
                            "Session Ended",
                            info);
                    }
                    else if (msg.IndexOf("ALERT", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadAlerts();
                        UpdateKPIs();
                        lblLastUpdate.Text = $"Alert received {DateTime.Now:hh:mm:ss tt}";
                        ShowToast("New security alert received!");
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.AdminAppId,
                            "Security Alert",
                            "A new security alert was detected. Check the Alerts tab.");
                    }
                    else if (msg.StartsWith("MACHINE_ONLINE:"))
                    {
                        string info = msg.Substring("MACHINE_ONLINE:".Length);
                        LoadActiveSessions();
                        UpdateKPIs();
                        if (_currentPage == "dashboard") UpdateKanban();
                        ShowToast($"Machine back online: {info}");
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.AdminAppId,
                            "Client Machine Online",
                            $"{info}");
                    }
                    else if (msg.StartsWith("CLIENT_REGISTERED:"))
                    {
                        string info = msg.Substring("CLIENT_REGISTERED:".Length);
                        LoadClients();
                        UpdateKPIs();
                        if (_currentPage == "dashboard") UpdateKanban();
                        ShowToast($"New machine registered: {info}");
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.AdminAppId,
                            "New Client Machine",
                            $"{info} joined the network. Check the Clients tab.");
                    }
                    else if (msg.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadActiveSessions();
                        UpdateKPIs();
                        if (_currentPage == "dashboard") UpdateKanban();
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.AdminAppId,
                            "Client Machine Offline",
                            "One or more client machines stopped responding. Check the Clients tab.");
                    }
                }
                catch { /* UI refresh during server callback — swallow to prevent crashing the callback thread */ }
            }));
        }

        // type: "success" | "warning" | "error"
        private void ShowToast(string message, string type = "success")
        {
            switch (type)
            {
                case "error":
                    ToastPanel.Background   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D0A0A"));
                    ToastPanel.BorderBrush  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F1D1D"));
                    lblToastIcon.Text       = "✕";
                    lblToastIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
                    lblToastMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
                    break;
                case "warning":
                    ToastPanel.Background   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1200"));
                    ToastPanel.BorderBrush  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854D0E"));
                    lblToastIcon.Text       = "⚠";
                    lblToastIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    lblToastMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                    break;
                default: // success
                    ToastPanel.Background   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A2118"));
                    ToastPanel.BorderBrush  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));
                    lblToastIcon.Text       = "✓";
                    lblToastIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
                    lblToastMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                    break;
            }

            lblToastMessage.Text = message;
            ToastPanel.Visibility = Visibility.Visible;

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _toastTimer.Tick += (_, __) =>
            {
                ToastPanel.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
            _toastTimer.Start();
        }

        private void btnDismissToast_Click(object sender, RoutedEventArgs e)
        {
            _toastTimer?.Stop();
            ToastPanel.Visibility = Visibility.Collapsed;
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
            try { _svc?.UnsubscribeFromNotifications("ADMIN_" + _adminUserId); } catch { /* best-effort WCF unsubscribe on close */ }

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
            // Warn if any client sessions are still running.
            try
            {
                if (_svc?.IsConnected == true)
                {
                    SessionInfo[] active = _svc.GetActiveSessions();
                    if (active.Length > 0 &&
                        !AppDialog.Confirm(
                            $"{active.Length} session(s) are currently active.\nClose the admin console anyway?",
                            "Sessions Active"))
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }
            catch { /* GetActiveSessions may fail if server is down — allow close to proceed */ }

            _refreshTimer?.Stop();
            try
            {
                if (_svc?.IsConnected == true)
                {
                    _svc.UnsubscribeFromNotifications("ADMIN_" + _adminUserId);
                    _svc.Disconnect();
                }
            }
            catch { /* best-effort WCF cleanup on window close */ }
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
                if (e.ClickCount == 2)
                    ToggleMaximize();
                else if (!_isManuallyMaximized)
                    DragMove();
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
            if (_isManuallyMaximized)
            {
                // Restore the resize border first, then set bounds, to avoid a
                // one-frame flash where the window briefly has no resize handles.
                ResizeMode = ResizeMode.CanResize;
                Left   = _normalBounds.Left;
                Top    = _normalBounds.Top;
                Width  = _normalBounds.Width;
                Height = _normalBounds.Height;
                _isManuallyMaximized = false;
                btnMaximize.Content  = "□";
            }
            else
            {
                // Save current bounds so Restore works correctly.
                _normalBounds = new Rect(Left, Top, Width, Height);

                // NoResize removes WPF's invisible non-client resize border (~4-8 px each
                // side with WindowStyle="None" + CanResize). Without this the border eats
                // into the bounds, leaving a visible gap on all four edges when maximized.
                ResizeMode = ResizeMode.NoResize;

                // WorkArea excludes the taskbar; WindowState.Maximized with
                // WindowStyle="None" would use the full screen rect and hide it.
                var area = SystemParameters.WorkArea;
                Left   = area.Left;
                Top    = area.Top;
                Width  = area.Width;
                Height = area.Height;
                _isManuallyMaximized = true;
                btnMaximize.Content  = "❐";
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
        public int      SessionId     { get; set; }
        public string   ClientId      { get; set; }
        public string   Username      { get; set; }
        public string   FullName      { get; set; }
        public string   StartTime     { get; set; }
        public string   Duration      { get; set; }

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
        public string ClientId            { get; set; }
        public string MachineName         { get; set; }
        public string IpAddress           { get; set; }
        public string MacAddress          { get; set; }
        public string Location            { get; set; }
        public bool   IsActive            { get; set; }
        public string ClientMachineStatus { get; set; }
        public string Status              { get; set; }
        public string CurrentUser         { get; set; }
        public string LastActive          { get; set; }
        public int    MissedHeartbeats    { get; set; }
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
        public int    UserId               { get; set; }
        public string Username             { get; set; }
        public string FullName             { get; set; }
        public string Phone                { get; set; }
        public string Address              { get; set; }
        public string Status               { get; set; }
        public string CreatedAt            { get; set; }
        public string LastLogin            { get; set; }
        public string ProfilePictureBase64 { get; set; }

        public System.Windows.Media.ImageSource AvatarSource
        {
            get
            {
                if (string.IsNullOrEmpty(ProfilePictureBase64)) return null;
                try
                {
                    var bytes = Convert.FromBase64String(ProfilePictureBase64);
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    using (var ms = new System.IO.MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    return bmp;
                }
                catch { return null; }
            }
        }
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

    public class BillingRecordVM
    {
        public int       BillingRecordId { get; set; }
        public int       SessionId       { get; set; }
        public string    Username        { get; set; }
        public string    MachineCode     { get; set; }
        public int       BillableMinutes { get; set; }
        public decimal   Amount          { get; set; }
        public string    Currency        { get; set; }
        public DateTime  CalculatedAt    { get; set; }
        public bool      IsPaid          { get; set; }
        public DateTime? PaidAt          { get; set; }

        public string AmountDisplay     => $"{Currency} {Amount:F2}";
        public string PaidAtDisplay     => PaidAt.HasValue ? PaidAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
        public string StatusLabel       => IsPaid ? "PAID" : "UNPAID";
        public string StatusBackground  => IsPaid ? "#14532D" : "#4C0519";
        public string StatusForeground  => IsPaid ? "#6EE7B7" : "#FCA5A5";
        public Visibility MarkPaidVisibility => IsPaid ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion
}
