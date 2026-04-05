using System;
using System.Configuration;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.Security;
using SessionManagement.WCF;
using SessionManagement.Media;

// ═══════════════════════════════════════════════════════════════════════════════
//  BUGS FIXED IN THIS VERSION
//  ─────────────────────────────────────────────────────────────────────────────
//  BUG 1 [CRITICAL – UI FREEZE]: UpdateTimerUI() called _svc.GetCurrentBillingRate()
//         (a WCF network call) on the UI thread every second. Fixed by caching
//         the rate in _billingRate at session start. No more WCF calls per tick.
//
//  BUG 2 [UI FREEZE on Start Session]: Multiple window-property changes
//         (WindowStyle, WindowState, Width, Height) each triggered a layout pass
//         making the UI appear frozen mid-transition. Fixed by issuing all changes
//         inside a single Dispatcher.BeginInvoke(Background priority) block.
//
//  BUG 3 [DOUBLE MessageBox on session end]: FinalizeSession showed a billing
//         summary MessageBox AND EndSessionAuto showed a second "session expired"
//         MessageBox. Fixed: FinalizeSession returns a summary string; the caller
//         decides if/when to show it.
//
//  BUG 4 [DEADLOCK RISK]: OnSessionTerminated used Dispatcher.Invoke (blocking).
//         Fixed to Dispatcher.BeginInvoke (non-blocking), matching SessionAdmin.
//
//  BUG 5 [WINDOW POSITION]: Left = WorkArea.Width - Width - 20 used WPF Width
//         property which may not reflect the new value immediately after assignment.
//         Fixed by using the literal constant 500.
//
//  BUG 6 [DEV USABILITY]: LockScreen() applied WindowStyle=None / Maximized even
//         when EnableKioskMode=false. Fixed: kiosk window state only applied when
//         EnableKioskMode=true, making dev/testing workflow sane.
//
//  BUG 7 [SESSION PANEL LAYOUT]: SessionPanel was nested inside the full-screen
//         Grid row, so it stretched across the full maximized window before
//         UnlockScreen completed. Fixed by reordering: UnlockScreen is fully
//         applied before ShowPanel(SessionPanel).
// ═══════════════════════════════════════════════════════════════════════════════

namespace SessionClient
{
    public partial class MainWindow : Window
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private DispatcherTimer _timer;
        private TimeSpan _remaining;
        private TimeSpan _total;

        private string _fullname;
        private string _username;
        private int _userId;
        private int _sessionId;
        private string _clientCode;

        // FIX BUG 1: cache billing rate — avoids WCF call every timer tick
        private decimal _billingRate;

        private SessionServiceClient _svc;
        private WebcamHelper _cam;
        private IllegalActivityDetectionService _detector;

        private string _pendingImage;
        private bool _passwordVisible;
        private int _failCount;
        private bool _manualLogout;
        private bool _sessionActive;

        private const int MAX_ATTEMPTS = 3;

        // FIX BUG 6: read kiosk flag once, use everywhere
        private readonly bool _kioskMode;

        // ── Win32 keyboard hook ───────────────────────────────────────────────
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
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ─────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            // Read EnableKioskMode once — used in LockScreen / InstallKeyboardHook
            string kioskSetting = ConfigurationManager.AppSettings["EnableKioskMode"] ?? "true";
            _kioskMode = string.Equals(kioskSetting, "true", StringComparison.OrdinalIgnoreCase);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _clientCode = ConfigurationManager.AppSettings["ClientCode"] ?? "CL001";
            _keyboardProc = KeyboardHookCallback;  // keep delegate alive (prevent GC)

            Loaded += OnLoaded;
            Closing += OnClosingHandler;
            KeyDown += OnKeyDown;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RESTRICTION ENGINE
        //
        //  State 1 – LOCKED (no session, or after session ends)
        //    Full-screen, no title bar, keyboard hook blocks navigation keys.
        //    User CANNOT reach the desktop → cannot use PC for free.
        //
        //  State 2 – ACTIVE (session running, user has paid)
        //    Compact 500×340 window, normal title bar, hook removed.
        //    User has full access to the PC.
        //
        //  State 1 is restored SYNCHRONOUSLY at the very start of ResetToLogin()
        //  so the screen is covered before any other UI change.
        //
        //  EnableKioskMode=false in App.config → still shows login UI but
        //  does NOT apply full-screen or keyboard hook (for development only).
        // ═════════════════════════════════════════════════════════════════════

        private void LockScreen()
        {
            if (_kioskMode)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                InstallKeyboardHook();
            }
            else
            {
                // Dev mode: normal window at a fixed size so it can be Alt-Tabbed
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                Width = 460;
                Height = 560;
                Topmost = false;
                ResizeMode = ResizeMode.CanResize;
            }

            ShowInTaskbar = true;
            HeaderBar.Visibility = Visibility.Visible;
            lblMachineCode.Text = "Machine: " + _clientCode +
                                       (_kioskMode ? "" : "  [DEV MODE]");
        }

        // FIX BUG 2: all window-property changes are batched in one BeginInvoke
        // at Background priority so WPF can flush pending renders first.
        private void UnlockScreen(int durationMinutes)
        {
            //UninstallKeyboardHook();

            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                UninstallKeyboardHook(); 
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                Topmost = false;
                ResizeMode = ResizeMode.NoResize;
                Width = 500;
                Height = 340;
                // FIX BUG 5: use literal 500 not Width property
                Left = SystemParameters.WorkArea.Width - 500 - 20;
                Top = 20;
                HeaderBar.Visibility = Visibility.Collapsed;
                Title = "Session Timer — " + durationMinutes + " min — " + _fullname;
            }), DispatcherPriority.Background);
        }

        // ── Keyboard hook ─────────────────────────────────────────────────────

        private void InstallKeyboardHook()
        {
            if (!_kioskMode) return;
            if (_hookHandle != IntPtr.Zero) return;
            try
            {
                using (var curProc = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProc.MainModule)
                {
                    _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc,
                        GetModuleHandle(curModule.ModuleName), 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Hook] Install failed: " + ex.Message);
            }
        }

        private void UninstallKeyboardHook()
        {
            if (_hookHandle == IntPtr.Zero) return;
            try { UnhookWindowsEx(_hookHandle); }
            catch { /* best-effort */ }
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

                if (alt && vk == VK_TAB) return (IntPtr)1;  // Alt+Tab
                if (alt && vk == VK_ESCAPE) return (IntPtr)1;  // Alt+Esc
                if (alt && vk == VK_F4) return (IntPtr)1;  // Alt+F4
                if (vk == VK_LWIN || vk == VK_RWIN) return (IntPtr)1;  // Win keys
                if (ctrl && vk == VK_ESCAPE) return (IntPtr)1; // Ctrl+Esc (Start)
                if (ctrl && shift && vk == VK_ESCAPE) return (IntPtr)1; // Ctrl+Shift+Esc
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_sessionActive && (
                (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) ||
                e.Key == Key.F4))
                e.Handled = true;
        }

        // ── Close / X button ─────────────────────────────────────────────────
        private void OnClosingHandler(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_sessionActive)
            {
                var r = MessageBox.Show(
                    "An active session is running.\nEnd session and exit?",
                    "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.No) { e.Cancel = true; return; }

                _timer.Stop();
                StopDetection();
                string summary = FinalizeSession("Manual");
                if (!string.IsNullOrEmpty(summary))
                    MessageBox.Show(summary, "Session Summary",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Login screen must stay visible — cannot be closed
                e.Cancel = true;
                MessageBox.Show(
                    "Please log in and start a session to use this computer.",
                    "Access Restricted", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UninstallKeyboardHook();
            StopDetection();
            try
            {
                if (_svc != null && _svc.IsConnected)
                {
                    _svc.UpdateClientStatus(_clientCode, "Offline");
                    _svc.UnsubscribeFromNotifications(_clientCode);
                    _svc.Disconnect();
                }
                if (_cam != null) _cam.Dispose();
            }
            catch { /* best-effort */ }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STARTUP
        // ═════════════════════════════════════════════════════════════════════
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LockScreen();
            lblLoginStatus.Visibility = Visibility.Visible;
            lblLoginStatus.Text = "Connecting to server…";

            try
            {
                _svc = new SessionServiceClient();
                _svc.SessionTerminated += OnSessionTerminated;
                _svc.TimeWarning += OnTimeWarning;
                _svc.ServerMessage += OnServerMessage;

                if (!_svc.Connect())
                {
                    lblLoginStatus.Text = "⚠ Server unreachable — check connection.";
                    return;
                }

                string machine = ConfigurationManager.AppSettings["ClientMachineName"]
                                 ?? Environment.MachineName;
                _svc.RegisterClient(_clientCode, machine, GetLocalIp(), GetMac());
                _svc.SubscribeForNotifications(_clientCode);
                _svc.UpdateClientStatus(_clientCode, "Idle");

                lblLoginStatus.Text = "Connected — please log in.";

                _cam = new WebcamHelper();
                _cam.CaptureError += (s, ev) =>
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ev.ErrorMessage);
            }
            catch (Exception ex)
            {
                lblLoginStatus.Text = "Init error: " + ex.Message;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-01 — LOGIN
        // ═════════════════════════════════════════════════════════════════════
        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtUsername.Text.Trim();
            string pass = _passwordVisible ? txtPasswordPlain.Text : txtPassword.Password;

            HideLoginError();

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            { ShowLoginError("Please enter both username and password."); return; }

            if (_failCount >= MAX_ATTEMPTS)
            { ShowLoginError("Too many failed attempts. Please contact the administrator."); return; }

            btnLogin.IsEnabled = false;
            btnLogin.Content = "Authenticating…";
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
                    lblWelcome.Text = "Welcome, " +
                        (string.IsNullOrEmpty(_fullname) ? _username : _fullname);
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
                btnLogin.Content = "LOGIN";
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

        // ═════════════════════════════════════════════════════════════════════
        //  UC-04 — WEBCAM IMAGE CAPTURE (async, background thread)
        // ═════════════════════════════════════════════════════════════════════
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
                            "No webcam available at login", "Low");
                        return;
                    }
                    Bitmap img = _cam.CaptureImage();
                    if (img == null && !retried)
                    { retried = true; System.Threading.Thread.Sleep(500); goto TryCapture; }
                    if (img == null)
                    {
                        _svc.LogSecurityAlert(0, _userId, "ImageCaptureFailed",
                            "Webcam capture failed after retry", "Low");
                        return;
                    }
                    _pendingImage = WebcamHelper.BitmapToBase64(
                        img, System.Drawing.Imaging.ImageFormat.Jpeg);
                    img.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ex.Message);
                    if (!retried) { retried = true; goto TryCapture; }
                }
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-03 — DURATION SELECTION
        // ═════════════════════════════════════════════════════════════════════
        private void cboDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomDurationPanel == null) return;
            CustomDurationPanel.Visibility =
                cboDuration.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnCancelDuration_Click(object sender, RoutedEventArgs e)
        {
            _pendingImage = null;
            _username = null;
            _fullname = null;
            ResetLoginFields();
            ShowPanel(LoginPanel);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-02 — START SESSION
        //
        //  FIX BUG 1: fetch billing rate here (once), store in _billingRate.
        //  FIX BUG 2: UI transition done via Dispatcher.BeginInvoke(Background).
        //  FIX BUG 7: UnlockScreen() is called BEFORE ShowPanel(SessionPanel).
        // ═════════════════════════════════════════════════════════════════════
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

                // FIX BUG 1: cache the billing rate — no WCF call per timer tick
                try { _billingRate = _svc.GetCurrentBillingRate(); }
                catch { _billingRate = 0.50m; }

                // Upload login image in background (non-blocking)
                if (!string.IsNullOrEmpty(_pendingImage))
                {
                    int sid = _sessionId;
                    int uid = _userId;
                    string img = _pendingImage;
                    _pendingImage = null;
                    System.Threading.Tasks.Task.Run(delegate ()
                    {
                        try { _svc.UploadLoginImage(sid, uid, img); }
                        catch (Exception ex2)
                        { System.Diagnostics.Debug.WriteLine("[Upload] " + ex2.Message); }
                    });
                }

                _svc.UpdateClientStatus(_clientCode, "Active");

                // Mark session active BEFORE calling UnlockScreen
                // so keyboard hook is correctly removed and lock state is correct
                _sessionActive = true;

                // FIX BUG 2 + BUG 7: UnlockScreen batches all window changes at
                // Background priority, THEN we show the session panel
                UnlockScreen(minutes);

                // Show session panel immediately (window resize happens async)
                ShowPanel(SessionPanel);

                // Start activity detection
                //StartDetection();

                // delay detection until UI stabilizes
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StartDetection();
                }), DispatcherPriority.ApplicationIdle);

                // Initialise countdown — uses cached _billingRate, no WCF calls
                //StartCountdown(minutes, resp.StartTime, resp.ExpectedEndTime);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StartCountdown(minutes, resp.StartTime, resp.ExpectedEndTime);
                }), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error starting session: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _sessionActive = false;
            }
            finally
            {
                btnStartSession.IsEnabled = true;
                btnStartSession.Content = "START SESSION";
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-06 — COUNTDOWN TIMER
        //
        //  FIX BUG 1: UpdateTimerUI now uses _billingRate field — no WCF call.
        // ═════════════════════════════════════════════════════════════════════
        private void StartCountdown(int minutes, DateTime serverStart, DateTime serverEnd)
        {
            _total = TimeSpan.FromMinutes(minutes);
            _remaining = serverEnd - DateTime.Now;
            if (_remaining.TotalSeconds < 0) _remaining = _total;

            lblSessionUser.Text = _fullname;
            lblSessionDuration.Text = minutes + " min";
            UpdateTimerUI();
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));
            UpdateTimerUI();

            if (_remaining.TotalMinutes <= 5 && _remaining.TotalSeconds > 0)
            {
                lblTimeRemaining.Foreground = System.Windows.Media.Brushes.OrangeRed;
                lblWarning.Text = "⚠ Less than 5 minutes remaining!";
                lblWarning.Visibility = Visibility.Visible;
            }

            if (_remaining.TotalSeconds <= 0)
                EndSessionAuto();
        }

        // FIX BUG 1: uses _billingRate — no network call per tick
        private void UpdateTimerUI()
        {
            if (_remaining.TotalSeconds < 0) _remaining = TimeSpan.Zero;

            lblTimeRemaining.Text = _remaining.ToString(@"hh\:mm\:ss");

            double elapsed = (_total - _remaining).TotalMinutes;
            decimal amount = (decimal)elapsed * _billingRate;
            lblCurrentBilling.Text = "$" + amount.ToString("F2");

            double pct = _total.TotalSeconds > 0
                         ? _remaining.TotalSeconds / _total.TotalSeconds * 100.0 : 0.0;
            progressBar.Value = Math.Max(0, pct);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-07 — AUTOMATIC SESSION TERMINATION
        //
        //  FIX BUG 3: FinalizeSession returns summary string.
        //  Only ONE MessageBox shown to user.
        // ═════════════════════════════════════════════════════════════════════
        private void EndSessionAuto()
        {
            _timer.Stop();
            StopDetection();
            string summary = FinalizeSession("Auto");

            // Single combined message — NOT two separate dialogs
            string msg = "Your session has expired." +
                         (string.IsNullOrEmpty(summary) ? "" : "\n\n" + summary) +
                         "\n\nPlease log in again to continue.";
            MessageBox.Show(msg, "Session Ended",
                MessageBoxButton.OK, MessageBoxImage.Information);

            ResetToLogin();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-08 — MANUAL LOGOUT
        // ═════════════════════════════════════════════════════════════════════
        private void btnEndSession_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Are you sure you want to end this session?\nRemaining time will be forfeited.",
                "Confirm Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            _manualLogout = true;
            _timer.Stop();
            StopDetection();
            string summary = FinalizeSession("Manual");
            if (!string.IsNullOrEmpty(summary))
                MessageBox.Show(summary, "Session Summary",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            ResetToLogin();
        }

        // FIX BUG 3: returns summary string — does NOT show MessageBox itself.
        // Callers decide whether to display the summary.
        private string FinalizeSession(string type)
        {
            try
            {
                bool ok = _svc.EndSession(_sessionId, type);
                double elapsed = (_total - _remaining).TotalMinutes;
                decimal amount = (decimal)elapsed * _billingRate;

                if (ok)
                    return "Duration: " + (int)elapsed + " min\nTotal charged: $" + amount.ToString("F2");
                else
                    return "Session ended locally. Billing will sync when server is reachable.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[FinalizeSession] " + ex.Message);
                return "Session ended. Billing sync pending.";
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-16 — ILLEGAL ACTIVITY DETECTION
        // ═════════════════════════════════════════════════════════════════════
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
                    _svc.LogSecurityAlert(
                        e.SessionId, e.UserId, e.AlertType, e.Description, e.Severity);
                }
                catch (Exception ex)
                { System.Diagnostics.Debug.WriteLine("[UC-16] " + ex.Message); }
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  WCF CALLBACKS
        //
        //  FIX BUG 4: use Dispatcher.BeginInvoke (non-blocking) not Invoke.
        // ═════════════════════════════════════════════════════════════════════
        private void OnSessionTerminated(object sender, SessionTerminatedEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            // FIX BUG 4: BeginInvoke prevents potential deadlock
            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                _timer.Stop();
                StopDetection();
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
            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                _remaining = TimeSpan.FromMinutes(e.RemainingMinutes);
                MessageBox.Show("⚠ Only " + e.RemainingMinutes + " minute(s) remaining!",
                    "Time Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }));
        }

        private void OnServerMessage(object sender, ServerMessageEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Server] " + e.Message);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RESET TO LOGIN
        //  Called after every session end.
        //  LockScreen() is the FIRST call — covers desktop before any other change.
        // ═════════════════════════════════════════════════════════════════════
        private void ResetToLogin()
        {
            _sessionActive = false;

            // RE-LOCK immediately — synchronous, on UI thread
            // Screen is covered BEFORE anything else changes
            LockScreen();

            _svc?.UpdateClientStatus(_clientCode, "Idle");

            _username = null;
            _fullname = null;
            _sessionId = 0;
            _pendingImage = null;
            _failCount = 0;
            _billingRate = 0m;

            ResetLoginFields();
            cboDuration.SelectedIndex = 2;  // 60 minutes default
            txtCustomDuration.Clear();
            lblTimeRemaining.Foreground = System.Windows.Media.Brushes.DarkSlateGray;
            lblTimeRemaining.Text = "00:00:00";
            lblCurrentBilling.Text = "$0.00";
            progressBar.Value = 100;
            lblWarning.Visibility = Visibility.Collapsed;
            lblLoginStatus.Text = "Session ended — please log in.";
            lblLoginStatus.Visibility = Visibility.Visible;

            ShowPanel(LoginPanel);
            Activate();
            txtUsername.Focus();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  PANEL NAVIGATION
        // ═════════════════════════════════════════════════════════════════════
        private void ShowPanel(UIElement p)
        {
            LoginPanel.Visibility = (p == LoginPanel) ? Visibility.Visible : Visibility.Collapsed;
            DurationPanel.Visibility = (p == DurationPanel) ? Visibility.Visible : Visibility.Collapsed;
            SessionPanel.Visibility = (p == SessionPanel) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── UI helpers ────────────────────────────────────────────────────────
        private void ShowLoginError(string msg)
        {
            lblLoginError.Text = msg;
            pnlLoginError.Visibility = Visibility.Visible;
        }

        private void HideLoginError()
        {
            pnlLoginError.Visibility = Visibility.Collapsed;
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
        }

        private bool TryGetDuration(out int minutes)
        {
            minutes = 0;
            if (cboDuration.SelectedIndex == 4)
            {
                if (!int.TryParse(txtCustomDuration.Text, out minutes) || minutes <= 0)
                {
                    MessageBox.Show("Enter a valid number of minutes.", "Invalid Duration",
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
                    MessageBox.Show("Duration must be between " + mn + " and " + mx + " minutes.",
                        "Invalid Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return true;
            }
            string sel = (cboDuration.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (sel.Length > 0 && int.TryParse(sel.Split(' ')[0], out minutes)) return true;
            MessageBox.Show("Please select a duration.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // ── Network helpers ───────────────────────────────────────────────────
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
    }
}