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

        private SessionServiceClient _svc;
        private WebcamHelper _cam;
        private IllegalActivityDetectionService _detector;

        private string _pendingImage;
        private bool _passwordVisible;
        private int _failCount;
        private bool _manualLogout;

        // Tracks whether a paid session is currently active.
        // Controls ALL window restriction behaviour.
        private bool _sessionActive;

        private const int MAX_ATTEMPTS = 3;

        // ── Win32 P/Invoke for keyboard hook ──────────────────────────────────
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private static readonly int VK_TAB = 0x09;
        private static readonly int VK_ESCAPE = 0x1B;
        private static readonly int VK_F4 = 0x73;
        private static readonly int VK_DELETE = 0x2E;

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

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _clientCode = ConfigurationManager.AppSettings["ClientCode"] ?? "CL001";

            // Store proc delegate as field to prevent GC collection
            _keyboardProc = KeyboardHookCallback;

            Loaded += OnLoaded;
            Closing += OnClosingHandler;

            // Intercept Alt+F4 at WPF level as well
            KeyDown += OnKeyDown;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RESTRICTION ENGINE
        //
        //  Project statement: "Users must log in using valid credentials to
        //  ACCESS THE SYSTEM" — in an internet café, the system = the PC.
        //
        //  Three-state window behaviour:
        //
        //  [LOCKED — no session]
        //    • Maximized full-screen, WindowStyle=None → no title bar
        //    • Topmost=True → always on top of everything
        //    • Low-level keyboard hook blocks Alt+Tab, Alt+F4, Ctrl+Alt+Del*
        //    • Window cannot be moved, resized, minimized, or closed
        //    • User CANNOT reach the desktop → cannot use PC for free
        //
        //  [ACTIVE — session running]
        //    • Normal window, standard size, title bar restored
        //    • Topmost=False → user can Alt-Tab to browser, apps they paid for
        //    • Keyboard hook removed → full keyboard access
        //    • User has complete access to the PC
        //
        //  [LOCKED — after session ends]
        //    • Immediately re-locks before any desktop content is visible
        //    • Keyboard hook re-installed
        //
        //  * Ctrl+Alt+Del (Secure Desktop) cannot be blocked by any user-mode
        //    application — this is a Windows security design decision and is
        //    fine for this academic project. Full kiosk would require Group
        //    Policy (out-of-scope per SRS "Hardware provisioning" section).
        // ═════════════════════════════════════════════════════════════════════

        private void LockScreen()
        {
            // Maximized full-screen — covers entire desktop
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;

            // Remove title bar entry from Alt+Tab switcher while locked
            // (still shows in taskbar so admin knows it's running)
            ShowInTaskbar = true;

            // Install low-level keyboard hook
            InstallKeyboardHook();

            HeaderBar.Visibility = Visibility.Visible;
            lblMachineCode.Text = "Machine: " + _clientCode;
        }

        private void UnlockScreen(int durationMinutes)
        {
            // Uninstall keyboard hook first
            UninstallKeyboardHook();

            // Restore normal window
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            Topmost = false;
            ResizeMode = ResizeMode.NoResize;

            // Set a compact size for the session timer panel
            Width = 500;
            Height = 340;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Width - Width - 20;
            Top = 20;   // top-right corner — stays visible but out of the way

            HeaderBar.Visibility = Visibility.Collapsed;

            // Update title bar to show session info
            Title = "Session Timer — " + durationMinutes + " min — " + _fullname;
        }

        // ── Low-level keyboard hook ───────────────────────────────────────────

        private void InstallKeyboardHook()
        {
            string kiosk = ConfigurationManager.AppSettings["EnableKioskMode"] ?? "true";
            if (kiosk != "true") return;  // skip hook during dev
            if (_hookHandle != IntPtr.Zero) return;  // already installed
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
            try
            {
                UnhookWindowsEx(_hookHandle);
            }
            catch { /* best-effort */ }
            finally
            {
                _hookHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Intercepts system-wide keystrokes when screen is locked.
        /// Blocks Alt+Tab, Alt+Esc, Alt+F4, Win key, Ctrl+Esc.
        /// </summary>
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Only intercept when screen is locked (no active session)
            if (!_sessionActive && nCode >= 0 &&
                (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kbs = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                uint vk = kbs.vkCode;
                bool alt = (kbs.flags & 0x20) != 0;  // LLKHF_ALTDOWN
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0; // VK_CONTROL

                // Block Alt+Tab
                if (alt && vk == VK_TAB) return (IntPtr)1;
                // Block Alt+Esc
                if (alt && vk == VK_ESCAPE) return (IntPtr)1;
                // Block Alt+F4
                if (alt && vk == (uint)VK_F4) return (IntPtr)1;
                // Block Win key (left=0x5B, right=0x5C)
                if (vk == 0x5B || vk == 0x5C) return (IntPtr)1;
                // Block Ctrl+Esc (Start menu)
                if (ctrl && vk == VK_ESCAPE) return (IntPtr)1;
                // Block Task Manager shortcut: Ctrl+Shift+Esc
                bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                if (ctrl && shift && vk == VK_ESCAPE) return (IntPtr)1;
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>Block Alt+F4 at WPF level as a second layer.</summary>
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_sessionActive)
            {
                if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt)
                    e.Handled = true;
                if (e.Key == Key.F4)
                    e.Handled = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window close handler — intercepts X button
        // ─────────────────────────────────────────────────────────────────────
        private void OnClosingHandler(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_sessionActive)
            {
                // Active session — behave like "End Session" (UC-08)
                var r = MessageBox.Show(
                    "An active session is running.\nEnd session and exit?",
                    "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.No) { e.Cancel = true; return; }
                _timer.Stop();
                StopDetection();
                FinalizeSession("Manual");
            }
            else
            {
                // No session — block close (login screen must stay visible)
                // Only allow close if no session was ever started on this run
                // (admin would use Task Manager to close the app during setup)
                e.Cancel = true;
                MessageBox.Show("Please log in and start a session to use this computer.",
                    "Access Restricted", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Cleanup before exit
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
            LockScreen();  // Immediately lock on startup — covers desktop
            lblLoginStatus.Visibility = Visibility.Visible;

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
                lblLoginStatus.Visibility = Visibility.Visible;

                _cam = new WebcamHelper();
                _cam.CaptureError += (s, ev) =>
                    System.Diagnostics.Debug.WriteLine("[Cam] " + ev.ErrorMessage);
            }
            catch (Exception ex)
            {
                lblLoginStatus.Text = "Init error: " + ex.Message;
                lblLoginStatus.Visibility = Visibility.Visible;
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
                    lblWelcome.Text = "Welcome, " + (string.IsNullOrEmpty(_fullname) ? _username : _fullname);
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
        //  UC-04 — CAPTURE USER IMAGE
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

                // UC-05: upload login image
                if (!string.IsNullOrEmpty(_pendingImage))
                {
                    int sid = _sessionId; int uid = _userId; string img = _pendingImage;
                    _pendingImage = null;
                    System.Threading.Tasks.Task.Run(delegate ()
                    {
                        try { _svc.UploadLoginImage(sid, uid, img); }
                        catch (Exception ex)
                        { System.Diagnostics.Debug.WriteLine("[Upload] " + ex.Message); }
                    });
                }

                _svc.UpdateClientStatus(_clientCode, "Active");

                // ── UNLOCK: session started, user has paid access ────────────
                _sessionActive = true;
                UnlockScreen(minutes);         // restores normal window
                ShowPanel(SessionPanel);

                // UC-16: start detection
                StartDetection();

                StartCountdown(minutes, resp.StartTime, resp.ExpectedEndTime);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStartSession.IsEnabled = true;
                btnStartSession.Content = "START SESSION";
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-06 — COUNTDOWN TIMER
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

            if (_remaining.TotalMinutes <= 5)
            {
                lblTimeRemaining.Foreground = System.Windows.Media.Brushes.OrangeRed;
                lblWarning.Text = "⚠ Less than 5 minutes remaining!";
                lblWarning.Visibility = Visibility.Visible;
            }

            if (_remaining.TotalSeconds <= 0)
                EndSessionAuto();
        }

        private void UpdateTimerUI()
        {
            lblTimeRemaining.Text = _remaining.ToString(@"hh\:mm\:ss");
            decimal rate = _svc.GetCurrentBillingRate();
            double elapsed = (_total - _remaining).TotalMinutes;
            lblCurrentBilling.Text = "$" + ((decimal)elapsed * rate).ToString("F2");
            double pct = _total.TotalSeconds > 0
                         ? _remaining.TotalSeconds / _total.TotalSeconds * 100 : 0;
            progressBar.Value = Math.Max(0, pct);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UC-07 — AUTO TERMINATION
        // ═════════════════════════════════════════════════════════════════════
        private void EndSessionAuto()
        {
            _timer.Stop();
            StopDetection();
            FinalizeSession("Auto");

            MessageBox.Show("Your session has expired.\nPlease log in again to continue.",
                "Session Ended", MessageBoxButton.OK, MessageBoxImage.Information);

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
            FinalizeSession("Manual");
            ResetToLogin();
        }

        private void FinalizeSession(string type)
        {
            try
            {
                bool ok = _svc.EndSession(_sessionId, type);
                decimal rate = _svc.GetCurrentBillingRate();
                double elapsed = (_total - _remaining).TotalMinutes;
                decimal amount = (decimal)elapsed * rate;
                string summary = ok
                    ? "Session ended.\n\nDuration: " + (int)elapsed + " min\nTotal: $" + amount.ToString("F2")
                    : "Session ended locally. Sync pending.";
                MessageBox.Show(summary, "Session Summary",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Finalize error: " + ex.Message, "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
        // ═════════════════════════════════════════════════════════════════════
        private void OnSessionTerminated(object sender, SessionTerminatedEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.Invoke(delegate ()
            {
                _timer.Stop();
                StopDetection();
                if (!_manualLogout)
                    MessageBox.Show(
                        "Your session was terminated by the administrator.\nReason: " + e.Reason,
                        "Session Terminated", MessageBoxButton.OK, MessageBoxImage.Warning);
                _manualLogout = false;
                ResetToLogin();
            });
        }

        private void OnTimeWarning(object sender, TimeWarningEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.Invoke(delegate ()
            {
                _remaining = TimeSpan.FromMinutes(e.RemainingMinutes);
                MessageBox.Show("⚠ Only " + e.RemainingMinutes + " minute(s) remaining!",
                    "Time Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void OnServerMessage(object sender, ServerMessageEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Server] " + e.Message);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RESET — called after every session end
        //  Re-locks immediately before user can interact with desktop
        // ═════════════════════════════════════════════════════════════════════
        private void ResetToLogin()
        {
            _sessionActive = false;

            // ── RE-LOCK before showing login screen ──────────────────────────
            // This runs synchronously on the UI thread, so the screen
            // is locked BEFORE anything else happens.
            LockScreen();

            _svc?.UpdateClientStatus(_clientCode, "Idle");
            _username = null;
            _fullname = null;
            _sessionId = 0;
            _pendingImage = null;
            _failCount = 0;

            ResetLoginFields();
            txtCustomDuration.Clear();
            cboDuration.SelectedIndex = 2;  // default to 60 minutes
            lblTimeRemaining.Foreground = System.Windows.Media.Brushes.DarkSlateGray;
            lblTimeRemaining.Text = "00:00:00";
            lblCurrentBilling.Text = "$0.00";
            lblWarning.Visibility = Visibility.Collapsed;
            lblLoginStatus.Text = "Session ended. Please log in.";
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
            LoginPanel.Visibility = p == LoginPanel ? Visibility.Visible : Visibility.Collapsed;
            DurationPanel.Visibility = p == DurationPanel ? Visibility.Visible : Visibility.Collapsed;
            SessionPanel.Visibility = p == SessionPanel ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI HELPERS
        // ─────────────────────────────────────────────────────────────────────
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
            string sel = (cboDuration.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            if (int.TryParse(sel.Split(' ')[0], out minutes)) return true;
            MessageBox.Show("Please select a duration.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  NETWORK HELPERS
        // ─────────────────────────────────────────────────────────────────────
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