using System;
using System.Configuration;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.Security;
using SessionManagement.WCF;
using SessionManagement.Media;

namespace SessionClient
{
    public partial class MainWindow : Window
    {
        // ═══════════════════════════════════════════════════════════
        //  #region STATE FIELDS
        // ═══════════════════════════════════════════════════════════
        #region State Fields

        private DispatcherTimer _timer;
        private TimeSpan _remaining;
        private TimeSpan _total;

        private string _fullname;
        private string _username;
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
            catch { }
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

        public MainWindow()
        {
            InitializeComponent();

            string kioskSetting = ConfigurationManager.AppSettings["EnableKioskMode"] ?? "true";
            _kioskMode = string.Equals(kioskSetting, "true", StringComparison.OrdinalIgnoreCase);
            _keyboardProc = KeyboardHookCallback;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _clientCode = ConfigurationManager.AppSettings["ClientCode"] ?? "CL001";

            Loaded += OnLoaded;
            Closing += OnClosingHandler;
            KeyDown += OnKeyDown;

            // Connection status polling
            var connTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            connTimer.Tick += (s, e) => UpdateConnectionStatus();
            connTimer.Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LockScreen();
            lblLoginStatus.Visibility = Visibility.Visible;

            try
            {
                _svc = new SessionServiceClient();
                _svc.SessionTerminated += OnSessionTerminated;
                _svc.TimeWarning += OnTimeWarning;
                _svc.ServerMessage += OnServerMessage;

                if (!_svc.Connect())
                { lblLoginStatus.Text = "⚠ Server unreachable."; return; }

                string machine = ConfigurationManager.AppSettings["ClientMachineName"]
                                 ?? Environment.MachineName;
                _svc.RegisterClient(_clientCode, machine, GetLocalIp(), GetMac());
                _svc.SubscribeForNotifications(_clientCode);
                _svc.UpdateClientStatus(_clientCode, "Idle");
                lblLoginStatus.Text = "Ready — please sign in.";

                _cam = new WebcamHelper();
                _cam.CaptureError += (s2, ev) =>
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ev.ErrorMessage);
            }
            catch (Exception ex)
            {
                lblLoginStatus.Text = "Init error: " + ex.Message;
            }
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
            }
            else
            {
                Topmost = false;
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                Width = 500; Height = 600;
                ResizeMode = ResizeMode.CanResize;
            }
            ShowInTaskbar = true;
            HeaderBar.Visibility = Visibility.Visible;
            lblMachineCode.Text = "Machine: " + _clientCode + (_kioskMode ? "" : " [DEV]");
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
            WindowStyle = WindowStyle.SingleBorderWindow;
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
            bool connected = _svc != null && _svc.IsConnected;
            ellipseConnectionStatus.Fill = connected
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.OrangeRed;
            lblConnectionStatus.Text = connected ? "Connected" : "Disconnected";
            btnConnect.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            _svc?.Connect();
            UpdateConnectionStatus();
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
                    _fullname = resp.FullName;
                    _username = resp.Username;
                    _userId = resp.UserId;
                    CaptureImageAsync();
                    lblWelcome.Text = $"Welcome, {(_fullname ?? _username)}!";
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
                        _svc.LogSecurityAlert(0, _userId, "CameraUnavailable",
                            "No webcam at login", "Low");
                        return;
                    }
                    Bitmap img = _cam.CaptureImage();
                    if (img == null && !retried)
                    { retried = true; System.Threading.Thread.Sleep(500); goto TryCapture; }
                    if (img == null)
                    {
                        _svc.LogSecurityAlert(0, _userId, "ImageCaptureFailed",
                            "Webcam capture failed", "Low");
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

            // Visual feedback: highlight selected button
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

            // Also sync legacy ComboBox
            int idx = minutes == 15 ? 0 : minutes == 30 ? 1 : minutes == 60 ? 2 : 3;
            cboDuration.SelectedIndex = idx;
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
                    MessageBox.Show(resp.ErrorMessage ?? "Failed to start session.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _sessionId = resp.SessionId;

                try { _billingRate = _svc.GetCurrentBillingRate(); }
                catch { _billingRate = 0.50m; }

                // Upload image in background
                if (!string.IsNullOrEmpty(_pendingImage))
                {
                    int sid = _sessionId, uid = _userId;
                    string img = _pendingImage;
                    _pendingImage = null;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try { _svc.UploadLoginImage(sid, uid, img); }
                        catch { }
                    });
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
                MessageBox.Show("Error starting session: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                lblTimeRemaining.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));
                lblWarning.Text = "⚠ Less than 5 minutes remaining!";
                lblWarning.Visibility = Visibility.Visible;

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
            string summary = FinalizeSession("Auto");

            // Ensure main window is visible before showing message
            if (WindowState == WindowState.Minimized ||
                (_floatingTimerWindow?.IsVisible == true))
            {
                CloseFloatingTimer();
                this.Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            MessageBox.Show("Your session has expired." +
                (string.IsNullOrEmpty(summary) ? "" : "\n\n" + summary) +
                "\n\nPlease sign in again to continue.", "Session Ended",
                MessageBoxButton.OK, MessageBoxImage.Information);

            ResetToLogin();
        }

        private void btnEndSession_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "End this session?\nRemaining time will be forfeited.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            _manualLogout = true;
            _timer.Stop();
            StopDetection();
            string summary = FinalizeSession("Manual");
            if (!string.IsNullOrEmpty(summary))
                MessageBox.Show(summary, "Session Summary",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            CloseFloatingTimer();
            ResetToLogin();
        }

        private string FinalizeSession(string type)
        {
            try
            {
                bool ok = _svc.EndSession(_sessionId, type);
                double elapsed = (_total - _remaining).TotalMinutes;
                decimal amount = (decimal)elapsed * _billingRate;
                return ok
                    ? $"Duration used: {(int)elapsed} min\nTotal charged: ${amount:F2}"
                    : "Session ended locally.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[FinalizeSession] " + ex.Message);
                return "Session ended.";
            }
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
            System.Threading.ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                try
                {
                    _svc.LogSecurityAlert(e.SessionId, e.UserId, e.AlertType,
                        e.Description, e.Severity);
                }
                catch { }
            });
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
                _timer.Stop();
                StopDetection();
                CloseFloatingTimer();
                // Ensure main window is visible and restored before showing message
                if (!this.IsVisible)
                    this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
                if (!_manualLogout)
                    MessageBox.Show(
                        "Your session was terminated by the administrator.\nReason: " + e.Reason,
                        "Session Terminated", MessageBoxButton.OK, MessageBoxImage.Warning);
                _manualLogout = false;
                ResetToLogin();
            }));
        }

        private void OnTimeWarning(object sender, TimeWarningEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _remaining = TimeSpan.FromMinutes(e.RemainingMinutes);
                if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Activate(); }
                MessageBox.Show($"⚠ Only {e.RemainingMinutes} minute(s) remaining!",
                    "Time Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            try { _svc?.UpdateClientStatus(_clientCode, "Idle"); } catch { }

            _username = null;
            _fullname = null;
            _sessionId = 0;
            _pendingImage = null;
            _failCount = 0;
            _billingRate = 0m;
            _selectedDurationMinutes = 60;

            ResetLoginFields();
            txtCustomDuration?.Clear();
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
            LoginPanel.Visibility = (p == LoginPanel) ? Visibility.Visible : Visibility.Collapsed;
            DurationPanel.Visibility = (p == DurationPanel) ? Visibility.Visible : Visibility.Collapsed;
            SessionPanel.Visibility = (p == SessionPanel) ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════
        //  #region HELPERS
        // ═══════════════════════════════════════════════════════════
        #region Helpers

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
                    MessageBox.Show("Enter a valid number of minutes.", "Invalid",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                int mn = 15, mx = 480;
                string mnS = ConfigurationManager.AppSettings["MinSessionDuration"];
                string mxS = ConfigurationManager.AppSettings["MaxSessionDuration"];
                if (!string.IsNullOrEmpty(mnS)) int.TryParse(mnS, out mn);
                if (!string.IsNullOrEmpty(mxS)) int.TryParse(mxS, out mx);
                if (minutes < mn || minutes > mx)
                {
                    MessageBox.Show($"Duration must be between {mn} and {mx} minutes.",
                        "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            catch { }
            return "127.0.0.1";
        }

        private static string GetMac()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    if (nic.OperationalStatus == OperationalStatus.Up)
                        return nic.GetPhysicalAddress().ToString();
            }
            catch { }
            return null;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_sessionActive && e.Key == Key.F4 &&
                Keyboard.Modifiers == ModifierKeys.Alt)
                e.Handled = true;
        }

        private void OnClosingHandler(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_sessionActive)
            {
                var r = MessageBox.Show("End session and exit?", "Confirm Exit",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.No) { e.Cancel = true; return; }
                _timer.Stop();
                StopDetection();
                string summary = FinalizeSession("Manual");
                if (!string.IsNullOrEmpty(summary))
                    MessageBox.Show(summary, "Summary", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Kiosk: block close on login screen
                e.Cancel = true;
                MessageBox.Show("Please log in to use this computer.", "Access Restricted",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UninstallKeyboardHook();
            StopDetection();
            CloseFloatingTimer();
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
            catch { }
        }

        #endregion
    }
}
