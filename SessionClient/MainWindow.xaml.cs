using System;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.Media;
using SessionManagement.Security;
using SessionManagement.UI;
using SessionManagement.WCF;

namespace SessionClient
{
    public partial class MainWindow : Window
    {
        // ═══════════════════════════════════════════════════════════
        //  #region STATE FIELDS
        // ═══════════════════════════════════════════════════════════
        #region State Fields

        private DispatcherTimer _timer;
        private DispatcherTimer _heartbeatTimer;
        private TimeSpan _remaining;
        private TimeSpan _total;

        private string _fullname;
        private string _username;
        private string _profilePictureBase64;
        private int _userId;
        private int _sessionId;
        private string _clientCode;

        private decimal _billingRate;
        private SessionServiceClient _svc;
        private WebcamHelper _cam;
        private IllegalActivityDetectionService _detector;

        private string _pendingImage;
        private bool _passwordVisible;
        private int _failCount;
        private bool _manualLogout;
        private bool _sessionActive;

        // Server power-cut recovery: retry EndSession when server comes back.
        private bool   _pendingSessionEnd;
        private int    _pendingSessionId;
        private string _pendingSessionEndType;

        // Reconnect detection: track whether we were connected on the previous poll.
        private bool _prevConnected;

        // Warning priority: true when < 5 min remaining; prevents offline banner from
        // permanently hiding the low-time warning after server reconnects.
        private bool _lowTimeWarning;

        // Selected duration from preset buttons
        private int _selectedDurationMinutes = 60;

        private const int MAX_ATTEMPTS = 3;
        private readonly bool _kioskMode;

        // Floating timer window
        private FloatingTimerWindow _floatingTimerWindow;

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region WIN32 KEYBOARD HOOK
        // ═══════════════════════════════════════════════════════════
        #region Keyboard Hook

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private static readonly uint VK_TAB = 0x09;
        private static readonly uint VK_ESCAPE = 0x1B;
        private static readonly uint VK_F4 = 0x73;
        private static readonly uint VK_LWIN = 0x5B;
        private static readonly uint VK_RWIN = 0x5C;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc _keyboardProc;
        private IntPtr _hookHandle = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        private void InstallKeyboardHook()
        {
            if (!_kioskMode || _hookHandle != IntPtr.Zero) return;
            try
            {
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                using (var mod = proc.MainModule)
                {
                    _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc,
                        GetModuleHandle(mod.ModuleName), 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Hook] " + ex.Message);
            }
        }

        private void UninstallKeyboardHook()
        {
            if (_hookHandle == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hookHandle); }
            catch { /* Win32 unhook can fail if the hook was never installed — safe to ignore */ }
            finally { _hookHandle = IntPtr.Zero; }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!_sessionActive && nCode >= 0 &&
                (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kbs = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                uint vk = kbs.vkCode;
                bool alt = (kbs.flags & 0x20) != 0;
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;

                if (alt && vk == VK_TAB) return (IntPtr)1;
                if (alt && vk == VK_ESCAPE) return (IntPtr)1;
                if (alt && vk == VK_F4) return (IntPtr)1;
                if (vk == VK_LWIN || vk == VK_RWIN) return (IntPtr)1;
                if (ctrl && vk == VK_ESCAPE) return (IntPtr)1;
                if (ctrl && shift && vk == VK_ESCAPE) return (IntPtr)1;
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region INITIALIZATION
        // ═══════════════════════════════════════════════════════════
        #region Initialization

        // Called by SplashWindow with an already-connected service and initialized webcam.
        public MainWindow(SessionServiceClient svc = null, WebcamHelper cam = null)
        {
            InitializeComponent();

            if (svc != null)
            {
                _svc = svc;
                _svc.SessionTerminated += OnSessionTerminated;
                _svc.TimeWarning       += OnTimeWarning;
                _svc.ServerMessage     += OnServerMessage;
            }

            if (cam != null)
            {
                _cam = cam;
                _cam.CaptureError += (s, ev) =>
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ev.ErrorMessage);
            }

            if (!bool.TryParse(ConfigurationManager.AppSettings["EnableKioskMode"], out _kioskMode))
                _kioskMode = true; // default ON — safe for unattended kiosk deployment
            _keyboardProc = KeyboardHookCallback;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _clientCode = ResolveClientCode();

            Loaded += OnLoaded;
            Closing += OnClosingHandler;
            KeyDown += OnKeyDown;

            // OS-level shutdown / logoff (bypasses WPF Closing event entirely).
            Microsoft.Win32.SystemEvents.SessionEnding += OnSystemSessionEnding;

            // Connection status polling
            var connTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            connTimer.Tick += (s, e) => UpdateConnectionStatus();
            connTimer.Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LockScreen();
            lblLoginStatus.Visibility = Visibility.Visible;

            // Splash already connected — finish registration on the correct _clientCode
            // and start the heartbeat timer (which the splash path skips otherwise).
            if (_svc != null && _svc.IsConnected)
            {
                RegisterAndStartHeartbeat();
                return;
            }

            try
            {
                _svc = new SessionServiceClient();
                _svc.SessionTerminated += OnSessionTerminated;
                _svc.TimeWarning       += OnTimeWarning;
                _svc.ServerMessage     += OnServerMessage;

                if (!_svc.Connect())
                { lblLoginStatus.Text = "⚠ Server unreachable."; return; }
            }
            catch (Exception ex)
            {
                lblLoginStatus.Text = "Init error: " + ex.Message;
                return;
            }

            RegisterAndStartHeartbeat();
        }

        /// <summary>
        /// Registers this machine with the server, starts the heartbeat timer, and
        /// initialises the webcam.  Called from both the "already connected via Splash"
        /// path and the "fresh connect" path in OnLoaded so the logic lives in one place.
        /// </summary>
        private void RegisterAndStartHeartbeat()
        {
            try
            {
                string machine = ConfigurationManager.AppSettings["ClientMachineName"]
                                 ?? Environment.MachineName;
                // Terminate orphan session BEFORE RegisterClient() — RegisterClient() stamps
                // LastSeenAt = GETDATE(), which would overwrite the pre-crash timestamp
                // that TerminateOrphanSession() uses to calculate billable elapsed time.
                _svc.TerminateOrphanSession(_clientCode);
                _svc.RegisterClient(_clientCode, machine, GetLocalIp(), GetMac());
                _svc.SubscribeForNotifications(_clientCode);
                _svc.UpdateClientStatus(_clientCode, "Idle");
            }
            catch (Exception ex)
            {
                lblLoginStatus.Text = "Init error: " + ex.Message;
                return;
            }

            // Send a heartbeat every 30 s so server can detect crashes/offline.
            _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _heartbeatTimer.Tick += (s, ea) =>
            {
                // Run off the UI thread so a slow/offline server call never freezes the countdown.
                var code = _clientCode;
                var svc  = _svc;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { svc?.Heartbeat(code); }
                    catch { /* best-effort; next tick will retry */ }
                });
            };
            _heartbeatTimer.Start();

            if (_cam == null)
            {
                _cam = new WebcamHelper();
                _cam.CaptureError += (s2, ev) =>
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ev.ErrorMessage);
            }

            lblLoginStatus.Text = "Ready — please sign in.";
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region LOCK / UNLOCK SCREEN
        // ═══════════════════════════════════════════════════════════
        #region Lock / Unlock Screen

        private void LockScreen()
        {
            if (_kioskMode)
            {
                Topmost = true;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
                InstallKeyboardHook();
                HeaderBar.Visibility = Visibility.Visible;
                lblMachineCode.Text = "Machine: " + _clientCode;
            }
            else
            {
                Topmost = false;
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                Width = 560; Height = 700;
                Left = (SystemParameters.WorkArea.Width  - 560) / 2;
                Top  = (SystemParameters.WorkArea.Height - 700) / 2;
                ResizeMode = ResizeMode.NoResize;
                Title = "NetCafé — Sign In";
                HeaderBar.Visibility = Visibility.Collapsed;
                ApplyDarkTitleBar();
            }
            ShowInTaskbar = true;
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int value = 1; // enable immersive dark mode
                DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
            }
            catch { /* DWM dark mode unsupported on Windows < 10 build 18985 — skip */ }
        }

        /// <summary>
        /// Synchronously unlocks screen and resizes to compact session timer.
        /// All property changes fire before ShowPanel(SessionPanel) is called.
        /// </summary>
        private void UnlockScreen(int durationMinutes)
        {
            _sessionActive = true;
            UninstallKeyboardHook();

            Topmost = false;
            WindowStyle = WindowStyle.None;       // custom dark title bar — no OS chrome
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;

            Width = 310;
            Height = 420;
            Left = SystemParameters.WorkArea.Width - 310 - 20;
            Top = 20;

            HeaderBar.Visibility = Visibility.Collapsed;
            Title = $"Session — {durationMinutes} min — {_fullname}";

            UpdateLayout();

            // Force Z-order reset
            this.Topmost = true;
            this.Topmost = false;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region CONNECTION STATUS
        // ═══════════════════════════════════════════════════════════
        #region Connection Status

        private void UpdateConnectionStatus()
        {
            bool connected = _svc != null && _svc.IsChannelReady;

            ellipseConnectionStatus.Fill = connected
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.OrangeRed;
            lblConnectionStatus.Text = connected ? "Connected" : "Disconnected";
            btnConnect.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;

            // Show / hide server-offline warning on the session screen.
            if (_sessionActive)
            {
                if (!connected)
                {
                    lblWarning.Text       = "⚠ Server offline — your session continues locally";
                    lblWarning.Visibility = Visibility.Visible;

                    // OS toast — only on transition to offline, not every poll cycle
                    if (_prevConnected)
                        SessionManagement.UI.ToastHelper.Show(
                            SessionManagement.UI.ToastHelper.ClientAppId,
                            "Server Offline",
                            "Connection lost. Your session timer continues running locally.");
                }
                else if (lblWarning.Text.StartsWith("⚠ Server offline"))
                {
                    // Server came back — restore low-time warning if still applicable,
                    // otherwise hide the banner.
                    if (_lowTimeWarning && _remaining.TotalSeconds > 0)
                    {
                        lblWarning.Text       = "⚠ Less than 5 minutes remaining!";
                        lblWarning.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        lblWarning.Visibility = Visibility.Collapsed;
                    }
                }
            }

            // Reconnect detected: re-register and re-subscribe so WCF callbacks
            // (admin termination, time warnings) resume for the running session.
            bool justReconnected = connected && !_prevConnected;
            if (justReconnected && _svc != null)
            {
                try
                {
                    string machine = ConfigurationManager.AppSettings["ClientMachineName"]
                                     ?? Environment.MachineName;
                    _svc.RegisterClient(_clientCode, machine, GetLocalIp(), GetMac());

                    // Subscribe BEFORE validating session state so that any pending
                    // termination stored server-side is replayed on this call (RC-4).
                    _svc.SubscribeForNotifications(_clientCode);

                    // RC-1 fix: "Active" is correct for an in-progress session.
                    // "InUse" is a transitional state for session-start only.
                    _svc.UpdateClientStatus(_clientCode, _sessionActive ? "Active" : "Idle");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Reconnect] re-register failed: " + ex.Message);
                }

                // RC-2: Validate the running session against server state.
                // The session may have been crash-terminated by the offline scan while
                // the server was down — the client must not keep running a dead session.
                if (_sessionActive && _sessionId > 0)
                {
                    try
                    {
                        var info = _svc.GetSessionInfo(_sessionId);
                        if (info == null || !string.Equals(info.SessionStatus, "Active",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            // Session was terminated server-side during the outage.
                            _timer.Stop();
                            StopDetection();
                            var (elapsed, amount) = ComputeBillingSummary();
                            CloseFloatingTimer();
                            ShowSummaryPanel(
                                "Your session was ended while the server was offline.",
                                elapsed, amount,
                                "Server — session terminated during outage");
                        }
                        else
                        {
                            // Re-sync local countdown to the server's authoritative expected end time
                            // (second-precision) so the timer doesn't jump to a whole-minute boundary.
                            var serverRemaining = info.ExpectedEndTime - DateTime.Now;
                            if (serverRemaining > TimeSpan.Zero)
                                _remaining = serverRemaining;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[Reconnect] session validate failed: " + ex.Message);
                    }
                }

                // Retry a queued EndSession that failed while the server was down.
                if (_pendingSessionEnd)
                {
                    try
                    {
                        bool ok = _svc.EndSession(_pendingSessionId, _pendingSessionEndType);
                        if (ok) _pendingSessionEnd = false;
                        // If ok == false: keep _pendingSessionEnd = true and retry next reconnect.
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[Reconnect] retry EndSession failed: " + ex.Message);
                    }
                }

                // RC-6: Toast only when a session is still running after validation —
                // confirms to the user that termination callbacks are live again.
                if (_sessionActive)
                    SessionManagement.UI.ToastHelper.Show(
                        SessionManagement.UI.ToastHelper.ClientAppId,
                        "Server Reconnected",
                        "Connection restored. Session is active and monitored.");
            }

            _prevConnected = connected;
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            btnConnect.IsEnabled       = false;
            lblConnectionStatus.Text   = "Connecting…";
            ConnectingOverlay.Visibility = Visibility.Visible;
            try
            {
                var svc = _svc;
                await Task.Run(() => { try { svc?.Connect(); } catch { } });
            }
            finally
            {
                ConnectingOverlay.Visibility = Visibility.Collapsed;
                btnConnect.IsEnabled = true;
                UpdateConnectionStatus();
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region LOGIN (UC-01)
        // ═══════════════════════════════════════════════════════════
        #region Login

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtUsername.Text.Trim();
            string pass = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;

            HideLoginError();

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            { ShowLoginError("Please enter both username and password."); return; }

            if (_failCount >= MAX_ATTEMPTS)
            { ShowLoginError("Too many failed attempts. Contact the administrator."); return; }

            btnLogin.IsEnabled = false;
            btnLogin.Content = "Signing in…";

            try
            {
                var resp = _svc.AuthenticateUser(user, pass, _clientCode);
                if (resp.IsAuthenticated)
                {
                    _failCount = 0;
                    _fullname              = resp.FullName;
                    _username              = resp.Username;
                    _userId                = resp.UserId;
                    _profilePictureBase64  = resp.ProfilePictureBase64;
                    CaptureImageAsync();

                    // Fetch billing rate now so cost previews are ready before the user picks a duration
                    try { _billingRate = _svc.GetCurrentBillingRate(); }
                    catch { _billingRate = 0.50m; }
                    UpdateDurationButtonCosts();

                    lblWelcome.Text = $"Welcome, {(_fullname ?? _username)}!";
                    SetUserAvatar(_profilePictureBase64,
                                  imgDurationAvatar, lblDurationInitial,
                                  _fullname ?? _username);
                    ShowPanel(DurationPanel);
                }
                else
                {
                    _failCount++;
                    ShowLoginError(resp.ErrorMessage ?? "Invalid credentials. Please try again.");
                }
            }
            catch (Exception ex)
            {
                ShowLoginError("Connection error. Please try again.");
                System.Diagnostics.Debug.WriteLine("[Login] " + ex.Message);
            }
            finally
            {
                btnLogin.IsEnabled = true;
                btnLogin.Content = "Sign In →";
            }
        }

        private void btnShowPassword_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            if (_passwordVisible)
            {
                txtPasswordPlain.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordPlain.Visibility = Visibility.Visible;
                btnShowPassword.Content = "🙈";
            }
            else
            {
                txtPassword.Password = txtPasswordPlain.Text;
                txtPasswordPlain.Visibility = Visibility.Collapsed;
                txtPassword.Visibility = Visibility.Visible;
                btnShowPassword.Content = "👁";
            }
        }

        private void ShowLoginError(string msg)
        {
            lblLoginError.Text = msg;
            pnlLoginError.Visibility = Visibility.Visible;
        }

        private void HideLoginError() => pnlLoginError.Visibility = Visibility.Collapsed;

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region WEBCAM CAPTURE (UC-04)
        // ═══════════════════════════════════════════════════════════
        #region Webcam Capture

        private void CaptureImageAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                bool retried = false;
            TryCapture:
                try
                {
                    if (!_cam.IsDeviceAvailable)
                    {
                        // WCF proxy is NOT thread-safe — dispatch to UI thread
                        int uid = _userId;
                        Dispatcher.BeginInvoke(new Action(() =>
                            _svc?.LogSecurityAlert(0, uid, "CameraUnavailable",
                                "No webcam at login", "Low")));
                        return;
                    }
                    Bitmap img = _cam.CaptureImage();
                    if (img == null && !retried)
                    { retried = true; System.Threading.Thread.Sleep(500); goto TryCapture; }
                    if (img == null)
                    {
                        int uid2 = _userId;
                        Dispatcher.BeginInvoke(new Action(() =>
                            _svc?.LogSecurityAlert(0, uid2, "ImageCaptureFailed",
                                "Webcam capture failed", "Low")));
                        return;
                    }
                    _pendingImage = WebcamHelper.BitmapToBase64(img,
                        System.Drawing.Imaging.ImageFormat.Jpeg);
                    img.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ex.Message);
                    if (!retried) { retried = true; goto TryCapture; }
                }
            });
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region DURATION SELECTION (UC-03 client side)
        // ═══════════════════════════════════════════════════════════
        #region Duration Selection

        /// <summary>Handles the modern preset buttons (15/30/60/120 min)</summary>
        private void btnDurationPreset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag == null) return;
            if (!int.TryParse(btn.Tag.ToString(), out int minutes)) return;
            _selectedDurationMinutes = minutes;

            // Visual feedback: highlight selected button, reset all others
            foreach (Button b in new[] { btnDur15, btnDur30, btnDur60, btnDur120 })
            {
                if (b == null) continue;
                b.BorderBrush = new SolidColorBrush(
                    b == btn
                        ? System.Windows.Media.Color.FromRgb(0x4F, 0x8E, 0xF7)
                        : System.Windows.Media.Color.FromRgb(0x2D, 0x37, 0x48));
                b.Foreground = new SolidColorBrush(
                    b == btn
                        ? System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA)
                        : System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));
            }

            // Preset selected — hide custom panel and clear its field
            CustomDurationPanel.Visibility = Visibility.Collapsed;
            txtCustomDuration.Clear();

            // Also sync legacy ComboBox
            int idx = minutes == 15 ? 0 : minutes == 30 ? 1 : minutes == 60 ? 2 : 3;
            cboDuration.SelectedIndex = idx;

            // Show cost estimate for this preset
            ShowCostEstimate(minutes);
        }

        private void btnDurCustom_Click(object sender, RoutedEventArgs e)
        {
            // Clear all preset highlights
            foreach (Button b in new[] { btnDur15, btnDur30, btnDur60, btnDur120 })
            {
                if (b == null) continue;
                b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x37, 0x48));
                b.Foreground  = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));
            }
            cboDuration.SelectedIndex = 4;
            CustomDurationPanel.Visibility = Visibility.Visible;
            lblCostEstimate.Visibility = Visibility.Collapsed;
            txtCustomDuration.Focus();
        }

        private void txtCustomDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lblCostEstimate == null) return;
            if (int.TryParse(txtCustomDuration.Text, out int mins) && mins > 0 && _billingRate > 0)
                ShowCostEstimate(mins);
            else
                lblCostEstimate.Visibility = Visibility.Collapsed;
        }

        private void cboDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomDurationPanel == null) return;
            CustomDurationPanel.Visibility = cboDuration.SelectedIndex == 4
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnCancelDuration_Click(object sender, RoutedEventArgs e)
        {
            _pendingImage = null;
            _username = null;
            _fullname = null;
            ResetLoginFields();
            ShowPanel(LoginPanel);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region START SESSION (UC-02)
        // ═══════════════════════════════════════════════════════════
        #region Start Session

        private void btnStartSession_Click(object sender, RoutedEventArgs e)
        {
            int minutes;
            if (!TryGetDuration(out minutes)) return;

            btnStartSession.IsEnabled = false;
            btnStartSession.Content = "Starting…";

            try
            {
                var resp = _svc.StartSession(_userId, _clientCode, minutes);
                if (!resp.Success)
                {
                    AppDialog.ShowError(resp.ErrorMessage ?? "Failed to start session.");
                    return;
                }
                _sessionId = resp.SessionId;

                // Upload image in background
                if (!string.IsNullOrEmpty(_pendingImage))
                {
                    int sid = _sessionId, uid = _userId;
                    string img = _pendingImage;
                    _pendingImage = null;
                    // Use BeginInvoke so the upload runs on the UI thread — same thread
                    // as the heartbeat DispatcherTimer — preventing concurrent _proxy access.
                    // Localhost WCF upload is fast (< 500 ms) so no noticeable UI impact.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { _svc?.UploadLoginImage(sid, uid, img); }
                        catch { /* image upload is best-effort — session continues if webcam/upload fails */ }
                    }));
                }

                _svc.UpdateClientStatus(_clientCode, "Active");

                // Synchronous unlock — window resized before ShowPanel
                UnlockScreen(minutes);
                ShowPanel(SessionPanel);
                StartDetection();
                StartCountdown(minutes);
            }
            catch (Exception ex)
            {
                AppDialog.ShowError("Error starting session: " + ex.Message);
                _sessionActive = false;
                LockScreen();
                ShowPanel(DurationPanel);
            }
            finally
            {
                btnStartSession.IsEnabled = true;
                btnStartSession.Content = "▶  Start Session";
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region DRAGGABLE TIMER WINDOW
        // ═══════════════════════════════════════════════════════════
        #region Draggable Timer Window

        /// <summary>
        /// Makes the compact session timer window draggable by clicking
        /// the drag handle area at the top.
        /// </summary>
        private void sessionDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region MINIMIZE TO FLOATING TIMER
        // ═══════════════════════════════════════════════════════════
        #region Floating Timer

        private void MinimizeTimerWindow()
        {
            if (_floatingTimerWindow == null)
            {
                _floatingTimerWindow = new FloatingTimerWindow();
                _floatingTimerWindow.RestoreRequested += FloatingTimerWindow_RestoreRequested;
            }
            _floatingTimerWindow.SetTime(lblTimeRemaining.Text);
            _floatingTimerWindow.Left = SystemParameters.WorkArea.Right - _floatingTimerWindow.Width - 20;
            _floatingTimerWindow.Top = SystemParameters.WorkArea.Bottom - _floatingTimerWindow.Height - 20;
            _floatingTimerWindow.Show();
            this.Hide();
        }

        private void FloatingTimerWindow_RestoreRequested(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            if (_floatingTimerWindow != null)
                _floatingTimerWindow.Hide();
        }

        private void UpdateFloatingTimerWindow()
        {
            if (_floatingTimerWindow?.IsVisible == true)
                _floatingTimerWindow.SetTime(lblTimeRemaining.Text);
        }

        private void btnMinimizeTimer_Click(object sender, RoutedEventArgs e)
        {
            MinimizeTimerWindow();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized && _sessionActive)
                MinimizeTimerWindow();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region COUNTDOWN TIMER (UC-06)
        // ═══════════════════════════════════════════════════════════
        #region Countdown Timer

        private void StartCountdown(int minutes)
        {
            _total = TimeSpan.FromMinutes(minutes);
            _remaining = _total;
            lblSessionUser.Text = _fullname ?? _username;
            SetUserAvatar(_profilePictureBase64,
                          imgSessionAvatar, lblSessionInitial,
                          _fullname ?? _username);
            lblSessionDuration.Text = minutes + " min";
            UpdateTimerUI();
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));
            UpdateTimerUI();
            UpdateFloatingTimerWindow();

            if (WindowState == WindowState.Minimized)
                Title = $"⏱ {_remaining:hh\\:mm\\:ss} remaining";

            if (_remaining.TotalMinutes <= 5 && _remaining.TotalSeconds > 0)
            {
                _lowTimeWarning = true;
                lblTimeRemaining.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));

                // Only write the low-time text when the offline banner isn't already showing
                // (offline warning takes priority; it will restore this text on reconnect).
                if (!lblWarning.Text.StartsWith("⚠ Server offline"))
                {
                    lblWarning.Text       = "⚠ Less than 5 minutes remaining!";
                    lblWarning.Visibility = Visibility.Visible;
                }

                if (WindowState == WindowState.Minimized)
                {
                    if (_floatingTimerWindow?.IsVisible == true)
                    {
                        _floatingTimerWindow.Hide();
                        this.Show();
                    }
                    WindowState = WindowState.Normal;
                    Activate();
                    Title = "Session Timer — ⚠ 5 min left!";
                }
            }
            else
            {
                _lowTimeWarning = false;
            }

            if (_remaining.TotalSeconds <= 0)
                EndSessionAuto();
        }

        private void UpdateTimerUI()
        {
            if (_remaining.TotalSeconds < 0) _remaining = TimeSpan.Zero;

            lblTimeRemaining.Text = _remaining.ToString(@"hh\:mm\:ss");

            double elapsed = (_total - _remaining).TotalMinutes;
            decimal amount = (decimal)elapsed * _billingRate;
            lblCurrentBilling.Text = $"${amount:F2}";

            double pct = _total.TotalSeconds > 0
                ? _remaining.TotalSeconds / _total.TotalSeconds * 100.0 : 0.0;
            progressBar.Value = Math.Max(0, pct);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region SESSION END (UC-07 / UC-08)
        // ═══════════════════════════════════════════════════════════
        #region Session End

        private void EndSessionAuto()
        {
            _timer.Stop();
            StopDetection();

            // Restore window to foreground if minimised / floating
            if (WindowState == WindowState.Minimized ||
                (_floatingTimerWindow?.IsVisible == true))
            {
                _floatingTimerWindow?.Hide();
                this.Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            var (elapsed, amount) = ComputeBillingSummary();
            CloseFloatingTimer();

            // RC-2 fix: lock the screen FIRST, then tell the server.
            // The old order blocked the UI waiting for the server call before
            // ShowSummaryPanel could run, leaving the session screen visible.
            // EndSessionOnServer is now non-blocking: if the channel is not ready
            // it queues the call for retry on reconnect and returns immediately.
            ShowSummaryPanel("Your session time has expired.", elapsed, amount, "Auto — time expired");
            EndSessionOnServer("Auto");
        }

        private void btnEndSession_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDialog.Confirm("End this session?\nRemaining time will be forfeited.", "Confirm")) return;

            _manualLogout = true;
            _timer.Stop();
            StopDetection();
            var (elapsed, amount) = ComputeBillingSummary();
            EndSessionOnServer("Manual");
            CloseFloatingTimer();
            ShowSummaryPanel("You ended your session early.", elapsed, amount, "Manual — user ended session");
        }

        /// <summary>Tells the server to finalize the session (fire-and-forget result).</summary>
        private void EndSessionOnServer(string type)
        {
            // RC-1 fix: do NOT call EnsureConnection() / Connect() here.
            // Those methods may block the UI thread for ConnectTimeoutSeconds while
            // attempting a TCP open — which freezes the DispatcherTimer and causes
            // the countdown to stick at 00:00:01 when the server is offline.
            // IsChannelReady is a pure state-read with zero blocking.
            if (_svc == null || !_svc.IsChannelReady)
            {
                // Queue for retry the moment the server reconnects.
                _pendingSessionEnd     = true;
                _pendingSessionId      = _sessionId;
                _pendingSessionEndType = type;
                return;
            }

            try
            {
                // RC-3 fix: EndSession() returns bool — it does not throw on failure.
                // The old code set _pendingSessionEnd = false unconditionally, losing
                // the session-end when the server returned false without throwing.
                bool ok = _svc.EndSession(_sessionId, type);
                if (ok)
                {
                    _pendingSessionEnd = false;
                }
                else
                {
                    _pendingSessionEnd     = true;
                    _pendingSessionId      = _sessionId;
                    _pendingSessionEndType = type;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[EndSession] " + ex.Message);
                _pendingSessionEnd     = true;
                _pendingSessionId      = _sessionId;
                _pendingSessionEndType = type;
            }
        }

        /// <summary>Returns how many full minutes were used and the PKR charge.</summary>
        private (int elapsedMinutes, decimal amount) ComputeBillingSummary()
        {
            double elapsedExact = (_total - _remaining).TotalMinutes;
            int elapsed = (int)Math.Ceiling(elapsedExact);
            decimal amount = (decimal)elapsedExact * _billingRate;
            return (elapsed, amount);
        }

        /// <summary>
        /// Restores kiosk window to full-screen, populates summary labels,
        /// and switches to the SummaryPanel.  Called from every session-end path.
        /// </summary>
        private void ShowSummaryPanel(string subtitle, int elapsedMinutes, decimal amount, string reason)
        {
            _sessionActive  = false;
            _lowTimeWarning = false;
            _heartbeatTimer?.Stop(); // prevent blocking UI thread while summary is displayed
            LockScreen();   // restore full-screen / kiosk state

            lblSummaryUser.Text     = _fullname ?? _username ?? "—";
            lblSummaryMachine.Text  = _clientCode;
            lblSummaryDuration.Text = elapsedMinutes + " min";
            lblSummaryAmount.Text   = $"PKR {amount:F2}";
            lblSummaryReason.Text   = reason;
            lblSummarySubtitle.Text = subtitle;

            ShowPanel(SummaryPanel);
            Activate();
        }

        private void btnSummaryClose_Click(object sender, RoutedEventArgs e)
        {
            ResetToLogin();
        }

        private void CloseFloatingTimer()
        {
            if (_floatingTimerWindow != null)
            {
                _floatingTimerWindow.Close();
                _floatingTimerWindow = null;
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region DETECTION (UC-16)
        // ═══════════════════════════════════════════════════════════
        #region Illegal Activity Detection

        private void StartDetection()
        {
            int interval = 60;
            string cfg = ConfigurationManager.AppSettings["ProxyCheckInterval"];
            if (!string.IsNullOrEmpty(cfg)) int.TryParse(cfg, out interval);
            _detector = new IllegalActivityDetectionService(_sessionId, _userId, interval);
            _detector.AlertDetected += OnIllegalActivityDetected;
        }

        private void StopDetection()
        {
            if (_detector == null) return;
            _detector.AlertDetected -= OnIllegalActivityDetected;
            _detector.Stop();
            _detector.Dispose();
            _detector = null;
        }

        private void OnIllegalActivityDetected(object sender, SecurityAlertEventArgs e)
        {
            // IMPORTANT: _svc/_proxy is NOT thread-safe for concurrent access.
            // The detection timer fires on a thread-pool thread; all WCF proxy
            // calls (including Heartbeat from DispatcherTimer) must run on the
            // UI thread to avoid channel corruption.  BeginInvoke queues the
            // call without blocking the detection timer thread.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _svc?.LogSecurityAlert(e.SessionId, e.UserId, e.AlertType,
                        e.Description, e.Severity);
                }
                catch { /* security alert logging is best-effort — detection continues if server is unreachable */ }
            }));
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region WCF CALLBACKS
        // ═══════════════════════════════════════════════════════════
        #region WCF Callbacks

        private void OnSessionTerminated(object sender, SessionTerminatedEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_sessionActive) return;  // already handled

                // OS toast so the user sees the alert regardless of window state
                SessionManagement.UI.ToastHelper.Show(
                    SessionManagement.UI.ToastHelper.ClientAppId,
                    "Session Terminated",
                    "Your session was ended by the administrator.");

                _timer.Stop();
                StopDetection();

                // Restore window if minimised / floating
                if (WindowState == WindowState.Minimized ||
                    (_floatingTimerWindow?.IsVisible == true))
                {
                    _floatingTimerWindow?.Hide();
                    this.Show();
                    WindowState = WindowState.Normal;
                }

                var (elapsed, amount) = ComputeBillingSummary();
                CloseFloatingTimer();
                ShowSummaryPanel(
                    "Your session was terminated by the administrator.",
                    elapsed, amount,
                    "Admin — " + (e.Reason ?? "no reason given"));
                _manualLogout = false;
            }));
        }

        private void OnTimeWarning(object sender, TimeWarningEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _remaining = TimeSpan.FromMinutes(e.RemainingMinutes);

                // OS toast — visible even if minimised or floating timer is showing
                SessionManagement.UI.ToastHelper.Show(
                    SessionManagement.UI.ToastHelper.ClientAppId,
                    "Session Time Warning",
                    $"Only {e.RemainingMinutes} minute(s) remaining in your session.");

                if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Activate(); }
                AppDialog.ShowWarning($"Only {e.RemainingMinutes} minute(s) remaining!", "Time Warning");
            }));
        }

        private void OnServerMessage(object sender, ServerMessageEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Server] " + e.Message);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region RESET TO LOGIN
        // ═══════════════════════════════════════════════════════════
        #region Reset To Login

        private void ResetToLogin()
        {
            _sessionActive = false;
            LockScreen();

            if (_svc?.IsChannelReady == true)
                try { _svc.UpdateClientStatus(_clientCode, "Idle"); } catch { /* best-effort status reset on logout */ }
            _heartbeatTimer?.Start(); // resume heartbeat now that we are back to login (idle)

            _username             = null;
            _fullname             = null;
            _profilePictureBase64 = null;
            _sessionId            = 0;
            _pendingImage         = null;
            _failCount = 0;
            _billingRate = 0m;
            _selectedDurationMinutes = 60;

            ResetLoginFields();
            txtCustomDuration?.Clear();
            if (lblCostEstimate != null) lblCostEstimate.Visibility = Visibility.Collapsed;
            CustomDurationPanel.Visibility = Visibility.Collapsed;
            lblTimeRemaining.Text = "00:00:00";
            lblTimeRemaining.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x4F, 0x8E, 0xF7));
            lblCurrentBilling.Text = "$0.00";
            progressBar.Value = 100;
            lblWarning.Visibility = Visibility.Collapsed;
            lblLoginStatus.Text = "Session ended — please sign in again.";
            lblLoginStatus.Visibility = Visibility.Visible;

            ShowPanel(LoginPanel);
            Activate();
            txtUsername.Focus();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region PANEL NAVIGATION
        // ═══════════════════════════════════════════════════════════
        #region Panel Navigation

        private void ShowPanel(UIElement p)
        {
            LoginPanel.Visibility    = (p == LoginPanel)    ? Visibility.Visible : Visibility.Collapsed;
            DurationPanel.Visibility = (p == DurationPanel) ? Visibility.Visible : Visibility.Collapsed;
            SessionPanel.Visibility  = (p == SessionPanel)  ? Visibility.Visible : Visibility.Collapsed;
            SummaryPanel.Visibility  = (p == SummaryPanel)  ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region HELPERS
        // ═══════════════════════════════════════════════════════════
        #region Helpers

        /// <summary>
        /// Refreshes the label on every preset duration button to show
        /// "X min  ~PKR Y" once the billing rate is known.
        /// </summary>
        private void UpdateDurationButtonCosts()
        {
            if (_billingRate <= 0) return;
            var presets = new[] { (btnDur15, 15), (btnDur30, 30), (btnDur60, 60), (btnDur120, 120) };
            foreach (var (btn, mins) in presets)
            {
                decimal cost = mins * _billingRate;
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                sp.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text                = $"{mins} min",
                    FontSize            = 14,
                    FontFamily          = new System.Windows.Media.FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground          = new System.Windows.Media.SolidColorBrush(
                                              System.Windows.Media.Colors.White)
                });
                sp.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text                = $"~PKR {cost:F0}",
                    FontSize            = 10,
                    FontFamily          = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground          = new System.Windows.Media.SolidColorBrush(
                                              System.Windows.Media.Color.FromRgb(0x4F, 0x8E, 0xF7)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 2, 0, 0)
                });
                btn.Content = sp;
            }

            // Default-select 60 min so estimate is shown immediately
            ShowCostEstimate(_selectedDurationMinutes);
        }

        /// <summary>Shows the cost estimate label for the given number of minutes.</summary>
        private void ShowCostEstimate(int minutes)
        {
            if (lblCostEstimate == null) return;
            if (_billingRate <= 0) { lblCostEstimate.Visibility = Visibility.Collapsed; return; }
            decimal cost = minutes * _billingRate;
            lblCostEstimate.Text       = $"Estimated cost for {minutes} min:  PKR {cost:F2}";
            lblCostEstimate.Visibility = Visibility.Visible;
        }

        private void SetUserAvatar(string base64,
            System.Windows.Controls.Image imgElement,
            System.Windows.Controls.TextBlock lblInitial,
            string displayName)
        {
            if (imgElement == null) return;
            // Update initial letter
            if (lblInitial != null)
                lblInitial.Text = string.IsNullOrEmpty(displayName) ? "?" :
                                  displayName.Substring(0, 1).ToUpper();

            if (!string.IsNullOrEmpty(base64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    using (var ms = new System.IO.MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    imgElement.Source = bmp;
                    if (lblInitial != null) lblInitial.Visibility = Visibility.Collapsed;
                    return;
                }
                catch { /* fall through to initial */ }
            }
            imgElement.Source = null;
            if (lblInitial != null) lblInitial.Visibility = Visibility.Visible;
        }

        private void ResetLoginFields()
        {
            txtUsername.Clear();
            txtPassword.Clear();
            txtPasswordPlain.Clear();
            _passwordVisible = false;
            txtPassword.Visibility = Visibility.Visible;
            txtPasswordPlain.Visibility = Visibility.Collapsed;
            btnShowPassword.Content = "👁";
            HideLoginError();
        }

        private bool TryGetDuration(out int minutes)
        {
            minutes = _selectedDurationMinutes;

            // If custom is visible, parse it
            if (CustomDurationPanel?.Visibility == Visibility.Visible)
            {
                if (!int.TryParse(txtCustomDuration.Text, out minutes) || minutes <= 0)
                {
                    AppDialog.ShowWarning("Enter a valid number of minutes.", "Invalid Duration");
                    return false;
                }
                int mn = 15, mx = 480;
                string mnS = ConfigurationManager.AppSettings["MinSessionDuration"];
                string mxS = ConfigurationManager.AppSettings["MaxSessionDuration"];
                if (!string.IsNullOrEmpty(mnS)) int.TryParse(mnS, out mn);
                if (!string.IsNullOrEmpty(mxS)) int.TryParse(mxS, out mx);
                if (minutes < mn || minutes > mx)
                {
                    AppDialog.ShowWarning($"Duration must be between {mn} and {mx} minutes.", "Invalid Duration");
                    return false;
                }
            }
            return true;
        }

        private static string GetLocalIp()
        {
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { /* DNS resolution failed — fall back to loopback */ }
            return "127.0.0.1";
        }

        private static string GetMac()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        return nic.GetPhysicalAddress().ToString();
            }
            catch { /* NIC enumeration failed — ResolveClientCode() will fall back to machine-name hash */ }
            return null;
        }

        /// <summary>
        /// Returns the client code to use for this machine.
        /// If App.config has a non-default value the admin set it intentionally — use it.
        /// Otherwise auto-derive from MAC address so every fresh install gets a unique
        /// code without any manual config step (NFR-12: 30-50 concurrent machines).
        ///
        /// NIC priority: Ethernet (wired) → WiFi → any other physical adapter.
        /// This keeps the code stable across reboots on machines with multiple adapters,
        /// because the OS does not guarantee NIC enumeration order.
        /// </summary>
        private static string ResolveClientCode()
        {
            string configured = ConfigurationManager.AppSettings["ClientCode"];
            if (!string.IsNullOrWhiteSpace(configured) &&
                !configured.Equals("CL001", StringComparison.OrdinalIgnoreCase))
                return configured;   // admin override — honour it

            // Try preferred NIC types in order: wired Ethernet first, then WiFi,
            // then any remaining Up non-loopback adapter.
            var preferred = new[]
            {
                NetworkInterfaceType.Ethernet,
                NetworkInterfaceType.Wireless80211,
                NetworkInterfaceType.GigabitEthernet,
                NetworkInterfaceType.FastEthernetFx,
                NetworkInterfaceType.FastEthernetT,
            };

            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                // Walk preferred types in order.
                foreach (var preferredType in preferred)
                {
                    var match = nics.FirstOrDefault(n => n.NetworkInterfaceType == preferredType);
                    if (match != null)
                    {
                        string mac = match.GetPhysicalAddress().ToString();
                        if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                            return "CL-" + mac;
                    }
                }

                // Fallback: any Up non-loopback adapter with a real MAC.
                foreach (var nic in nics)
                {
                    string mac = nic.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                        return "CL-" + mac;
                }
            }
            catch { /* NIC enumeration failed — fall through to machine-name hash fallback */ }

            // Last resort: stable hash of machine name.
            return "CL-" + Math.Abs(Environment.MachineName.GetHashCode()).ToString("X8");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_sessionActive && e.Key == Key.F4 &&
                Keyboard.Modifiers == ModifierKeys.Alt)
                e.Handled = true;
        }

        /// <summary>
        /// Fired by Windows on shutdown, restart, or user logoff — bypasses WPF Closing.
        /// Must be fast and best-effort; OS grants only a few seconds before hard-killing the process.
        /// </summary>
        private void OnSystemSessionEnding(object sender,
            Microsoft.Win32.SessionEndingEventArgs e)
        {
            _heartbeatTimer?.Stop();
            _timer?.Stop();
            try { StopDetection(); }      catch { /* best-effort shutdown — OS grants only a few seconds */ }
            try { CloseFloatingTimer(); } catch { /* best-effort shutdown */ }
            try { UninstallKeyboardHook(); } catch { /* best-effort shutdown */ }
            try
            {
                if (_sessionActive)
                    EndSessionOnServer("Manual");
                if (_svc?.IsConnected == true)
                {
                    _svc.UpdateClientStatus(_clientCode, "Offline");
                    _svc.UnsubscribeFromNotifications(_clientCode);
                    _svc.Disconnect();
                }
                _cam?.Dispose();
            }
            catch { /* best-effort OS shutdown — any WCF/cam error here is non-fatal */ }
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSystemSessionEnding;
        }

        private void OnClosingHandler(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_sessionActive)
            {
                if (!AppDialog.Confirm("End session and exit?", "Confirm Exit")) { e.Cancel = true; return; }
                _timer.Stop();
                StopDetection();
                var (elapsed, amount) = ComputeBillingSummary();
                EndSessionOnServer("Manual");
                if (elapsed > 0)
                    AppDialog.ShowInfo(
                        $"Session ended.\nDuration: {elapsed} min\nCharged: PKR {amount:F2}",
                        "Session Summary");
            }
            else
            {
                // Kiosk: silently block close on the login screen — no dialog.
                e.Cancel = true;
                return;
            }

            Microsoft.Win32.SystemEvents.SessionEnding -= OnSystemSessionEnding;
            UninstallKeyboardHook();
            StopDetection();
            CloseFloatingTimer();
            _heartbeatTimer?.Stop();
            try
            {
                if (_svc?.IsConnected == true)
                {
                    _svc.UpdateClientStatus(_clientCode, "Offline");
                    _svc.UnsubscribeFromNotifications(_clientCode);
                    _svc.Disconnect();
                }
                _cam?.Dispose();
            }
            catch { /* best-effort WCF cleanup on window close */ }
        }

        #endregion
    }
}
