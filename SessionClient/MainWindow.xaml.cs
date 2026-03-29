using System;
using System.Configuration;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.Security;
using SessionManagement.WCF;
using SessionManagement.Media;

namespace SessionClient
{
    public partial class MainWindow : Window
    {
        // ── fields ───────────────────────────────────────────────
        private DispatcherTimer       _timer;
        private TimeSpan              _remaining;
        private TimeSpan              _total;

        private string  _username;
        private int     _userId;
        private int     _sessionId;
        private string  _clientCode;
        private int     _machineId;         // returned by RegisterClient

        private SessionServiceClient  _svc;
        private WebcamHelper          _cam;
        private ProxyDetectionService _proxy;

        private string  _pendingImage;      // Base64 captured at login; sent after session starts
        private bool    _passwordVisible;
        private int     _failCount;

        private const int MAX_ATTEMPTS = 3;

        // ─────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            _timer          = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick    += Timer_Tick;
            _clientCode     = ConfigurationManager.AppSettings["ClientCode"] ?? "CL001";
            Loaded         += OnLoaded;
        }

        // ─────────────────────────────────────────────────────────
        //  STARTUP  (call RegisterClient + subscribe)
        // ─────────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _svc = new SessionServiceClient();
                _svc.SessionTerminated += OnSessionTerminated;
                _svc.TimeWarning       += OnTimeWarning;
                _svc.ServerMessage     += OnServerMessage;

                if (!_svc.Connect())
                {
                    ShowError("Cannot connect to server. Check network and restart.");
                    return;
                }

                // UC-10/11: register this machine so it appears in admin dashboard
                string machine  = ConfigurationManager.AppSettings["ClientMachineName"]
                                  ?? Environment.MachineName;
                string ip       = GetLocalIp();
                string mac      = GetMac();

                bool registered = _svc.RegisterClient(_clientCode, machine, ip, mac);
                if (!registered)
                    System.Diagnostics.Debug.WriteLine(
                        "[Client] Machine registration failed — may already exist.");

                // Subscribe for WCF callbacks (time warnings, forced termination)
                _svc.SubscribeForNotifications(_clientCode);

                // Mark machine Online/Idle in dashboard
                _svc.UpdateClientStatus(_clientCode, "Idle");

                // Init webcam
                _cam              = new WebcamHelper();
                _cam.CaptureError += (s, ev) =>
                    System.Diagnostics.Debug.WriteLine($"[Cam] {ev.ErrorMessage}");
            }
            catch (Exception ex)
            {
                ShowError($"Initialization error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-01  ─  LOGIN
        //  SEQ-01: User → Client → Server → DB → Server → Client
        // ─────────────────────────────────────────────────────────

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtUsername.Text.Trim();
            string pass = _passwordVisible
                          ? txtPasswordPlain.Text
                          : txtPassword.Password;

            HideLoginError();

            // Basic client-side validation
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            { ShowLoginError("Please enter both username and password."); return; }

            // Lockout after MAX_ATTEMPTS consecutive failures
            if (_failCount >= MAX_ATTEMPTS)
            {
                ShowLoginError($"Too many failed attempts. Please wait before trying again.");
                return;
            }

            btnLogin.IsEnabled = false;
            btnLogin.Content   = "Authenticating…";

            try
            {
                // SEQ-01 step 2: client sends to server
                var resp = _svc.AuthenticateUser(user, pass, _clientCode);

                if (resp.IsAuthenticated)
                {
                    _failCount = 0;
                    _username  = resp.Username;
                    _userId    = resp.UserId;

                    // UC-04 step 1: capture image immediately after successful login
                    CaptureImageAsync();

                    // Move to duration screen
                    ShowPanel(DurationPanel);
                }
                else
                {
                    _failCount++;
                    ShowLoginError(resp.ErrorMessage ?? "Invalid credentials.");
                }
            }
            catch (Exception ex)
            {
                ShowLoginError("Connection error. Check server connection.");
                System.Diagnostics.Debug.WriteLine($"[Login] {ex.Message}");
            }
            finally
            {
                btnLogin.IsEnabled = true;
                btnLogin.Content   = "Login";
            }
        }

        // Password show/hide toggle
        private void btnShowPassword_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            if (_passwordVisible)
            {
                txtPasswordPlain.Text         = txtPassword.Password;
                txtPassword.Visibility        = Visibility.Collapsed;
                txtPasswordPlain.Visibility   = Visibility.Visible;
                btnShowPassword.Content       = "🙈";
            }
            else
            {
                txtPassword.Password          = txtPasswordPlain.Text;
                txtPasswordPlain.Visibility   = Visibility.Collapsed;
                txtPassword.Visibility        = Visibility.Visible;
                btnShowPassword.Content       = "👁";
            }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-04  ─  CAPTURE USER IMAGE (async, retries once)
        //  SEQ-04: Login success → activate webcam → capture → store locally
        // ─────────────────────────────────────────────────────────

        private void CaptureImageAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool retried = false;
            TryCapture:
                try
                {
                    if (!_cam.IsDeviceAvailable)
                    {
                        // SEQ-04 alt path A1: camera unavailable
                        _svc.LogSecurityAlert(0, _userId,
                            "CameraUnavailable", "No webcam at login", "Low");
                        return;
                    }

                    Bitmap img = _cam.CaptureImage();

                    if (img == null && !retried)
                    {
                        // SEQ-04 exception: retry once
                        retried = true;
                        System.Threading.Thread.Sleep(500);
                        goto TryCapture;
                    }

                    if (img == null)
                    {
                        // SEQ-04: retry failed → log failure
                        _svc.LogSecurityAlert(0, _userId,
                            "ImageCaptureFailed", "Webcam capture failed after retry", "Low");
                        return;
                    }

                    // Store locally until we have a sessionId
                    _pendingImage = WebcamHelper.BitmapToBase64(
                        img, System.Drawing.Imaging.ImageFormat.Jpeg);
                    img.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Cam] {ex.Message}");
                    if (!retried) { retried = true; goto TryCapture; }
                }
            });
        }

        // ─────────────────────────────────────────────────────────
        //  UC-03  ─  SELECT SESSION DURATION
        // ─────────────────────────────────────────────────────────

        private void cboDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomDurationPanel == null) return;
            CustomDurationPanel.Visibility =
                cboDuration.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnCancelDuration_Click(object sender, RoutedEventArgs e)
        {
            _pendingImage = null;
            _username     = null;
            ResetLoginFields();
            ShowPanel(LoginPanel);
        }

        // ─────────────────────────────────────────────────────────
        //  UC-02  ─  START SESSION
        //  SEQ-02: duration confirmed → server creates session → timer starts
        // ─────────────────────────────────────────────────────────

        private void btnStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetDuration(out int minutes)) return;

            btnStartSession.IsEnabled = false;
            btnStartSession.Content   = "Starting…";

            try
            {
                // SEQ-02 step 1: send StartSession to server
                var resp = _svc.StartSession(_userId, _clientCode, minutes);

                if (!resp.Success)
                {
                    MessageBox.Show(resp.ErrorMessage ?? "Failed to start session.",
                        "Session Start Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _sessionId = resp.SessionId;

                // UC-05: upload the captured image now that we have a sessionId
                // SEQ-05: client packages image+sessionId → server saves to disk → writes tblSessionImage
                if (!string.IsNullOrEmpty(_pendingImage))
                {
                    var sessionId = _sessionId;
                    var userId = _userId;
                    var image = _pendingImage;
                    _pendingImage = null;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            bool ok = _svc.UploadLoginImage(sessionId, userId, image);
                            if (!ok)
                                System.Diagnostics.Debug.WriteLine("[ImageUpload] Failed to upload login image.");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("[ImageUpload] Exception: " + ex.Message);
                        }
                    });
                }

                // Mark machine Active
                _svc.UpdateClientStatus(_clientCode, "Active");

                // FR-12: start proxy/illegal-activity detection
                //StartProxyDetection();
                
                // UC-02 step 3: start local countdown synced to server times
                StartCountdown(minutes, resp.StartTime, resp.ExpectedEndTime);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting session: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStartSession.IsEnabled = true;
                btnStartSession.Content   = "Start Session";
            }
        }

        // ─────────────────────────────────────────────────────────
        //  UC-06  ─  VIEW REMAINING TIME  (countdown)
        //  SEQ-06: countdown ticks every second, display updates, 5-min warning
        // ─────────────────────────────────────────────────────────

        private void StartCountdown(int minutes, DateTime serverStart, DateTime serverEnd)
        {
            _total     = TimeSpan.FromMinutes(minutes);
            _remaining = serverEnd - DateTime.Now;
            if (_remaining.TotalSeconds < 0) _remaining = _total;

            lblSessionUser.Text     = _username;
            lblSessionDuration.Text = $"{minutes} min";
            UpdateTimerUI();

            ShowPanel(SessionPanel);
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));
            UpdateTimerUI();

            // Colour warning at ≤ 5 minutes (SEQ-06)
            lblTimeRemaining.Foreground =
                _remaining.TotalMinutes <= 5
                ? System.Windows.Media.Brushes.OrangeRed
                : System.Windows.Media.Brushes.DarkSlateBlue;

            // UC-07: auto-terminate when timer reaches zero
            if (_remaining.TotalSeconds <= 0)
                EndSessionAuto();
        }

        private void UpdateTimerUI()
        {
            lblTimeRemaining.Text = _remaining.ToString(@"hh\:mm\:ss");

            // Show running billing (FR-07 / FR-08)
            decimal rate    = _svc.GetCurrentBillingRate();
            double  elapsed = (_total - _remaining).TotalMinutes;
            lblCurrentBilling.Text = $"${(decimal)elapsed * rate:F2}";

            double pct = _total.TotalSeconds > 0
                         ? _remaining.TotalSeconds / _total.TotalSeconds * 100
                         : 0;
            progressBar.Value = Math.Max(0, pct);
        }

        // ─────────────────────────────────────────────────────────
        //  UC-07  ─  AUTO SESSION TERMINATION
        //  SEQ-07: timer expired → end session → finalize billing → reset UI
        // ─────────────────────────────────────────────────────────

        private void EndSessionAuto()
        {
            _timer.Stop();
            StopProxyDetection();

            MessageBox.Show("Your session has expired.", "Session Ended",
                MessageBoxButton.OK, MessageBoxImage.Information);

            FinalizeSession("Auto");
            ResetToLogin();
        }

        // ─────────────────────────────────────────────────────────
        //  UC-08  ─  MANUAL LOGOUT
        //  SEQ-08: user clicks End → confirm → end session → finalize billing → reset UI
        // ─────────────────────────────────────────────────────────

        private void btnEndSession_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("Are you sure you want to end this session?",
                "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            _timer.Stop();
            StopProxyDetection();
            FinalizeSession("Manual");
            ResetToLogin();
        }

        // ─────────────────────────────────────────────────────────
        //  Common session finalize (used by both UC-07 and UC-08)
        // ─────────────────────────────────────────────────────────

        private void FinalizeSession(string type)
        {
            try
            {
                bool ok = _svc.EndSession(_sessionId, type);

                decimal rate    = _svc.GetCurrentBillingRate();
                double  elapsed = (_total - _remaining).TotalMinutes;
                decimal amount  = (decimal)elapsed * rate;

                string summary = ok
                    ? $"Session ended.\n\nDuration: {(int)elapsed} min\nAmount: ${amount:F2}"
                    : "Session ended locally. Server sync may be pending.";

                MessageBox.Show(summary, "Session Summary",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error finalizing session: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  FR-12  ─  PROXY / ILLEGAL ACTIVITY DETECTION
        // ─────────────────────────────────────────────────────────

        private void StartProxyDetection()
        {
            int interval = int.Parse(
                ConfigurationManager.AppSettings["ProxyCheckInterval"] ?? "60");
            _proxy = new ProxyDetectionService(_sessionId, _userId, interval);
            _proxy.AlertDetected += OnAlertDetected;
        }

        private void StopProxyDetection()
        {
            if (_proxy == null) return;
            _proxy.AlertDetected -= OnAlertDetected;
            _proxy.Stop();
            _proxy.Dispose();
            _proxy = null;
        }

        private void OnAlertDetected(object sender, SecurityAlertEventArgs e)
        {
            // FR-13: transmit alert to server → server logs + notifies admin
            _svc.LogSecurityAlert(
                e.SessionId, e.UserId, e.AlertType, e.Description, e.Severity);
        }

        // ─────────────────────────────────────────────────────────
        //  WCF CALLBACKS  (server → client)
        // ─────────────────────────────────────────────────────────

        private void OnSessionTerminated(object sender, SessionTerminatedEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.Invoke(() =>
            {
                _timer.Stop();
                StopProxyDetection();
                MessageBox.Show(
                    $"Your session has been terminated by the administrator.\nReason: {e.Reason}",
                    "Session Terminated", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetToLogin();
            });
        }

        private void OnTimeWarning(object sender, TimeWarningEventArgs e)
        {
            if (e.SessionId != _sessionId) return;
            Dispatcher.Invoke(() =>
            {
                // Sync remaining time with server value (NFR-03)
                _remaining = TimeSpan.FromMinutes(e.RemainingMinutes);
                MessageBox.Show(
                    $"Warning: Only {e.RemainingMinutes} minute(s) remaining.",
                    "Time Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void OnServerMessage(object sender, ServerMessageEventArgs e)
            => Dispatcher.Invoke(() =>
                System.Diagnostics.Debug.WriteLine($"[Server] {e.Message}"));

        // ─────────────────────────────────────────────────────────
        //  UI helpers
        // ─────────────────────────────────────────────────────────

        private void ShowPanel(UIElement p)
        {
            LoginPanel.Visibility    = p == LoginPanel    ? Visibility.Visible : Visibility.Collapsed;
            DurationPanel.Visibility = p == DurationPanel ? Visibility.Visible : Visibility.Collapsed;
            SessionPanel.Visibility  = p == SessionPanel  ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowLoginError(string msg)
        {
            lblLoginError.Text       = msg;
            lblLoginError.Visibility = Visibility.Visible;
        }

        private void HideLoginError() => lblLoginError.Visibility = Visibility.Collapsed;

        private void ShowError(string msg)
            => MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

        private void ResetLoginFields()
        {
            txtUsername.Clear();
            txtPassword.Clear();
            txtPasswordPlain.Clear();
        }

        private void ResetToLogin()
        {
            _svc?.UpdateClientStatus(_clientCode, "Idle");
            _username     = null;
            _sessionId    = 0;
            _pendingImage = null;
            _failCount    = 0;

            ResetLoginFields();
            txtCustomDuration.Clear();
            cboDuration.SelectedIndex      = 0;
            lblTimeRemaining.Foreground    = System.Windows.Media.Brushes.DarkSlateBlue;
            lblCurrentBilling.Text         = "$0.00";

            ShowPanel(LoginPanel);
        }

        private bool TryGetDuration(out int minutes)
        {
            minutes = 0;
            if (cboDuration.SelectedIndex == 4) // Custom
            {
                if (!int.TryParse(txtCustomDuration.Text, out minutes) || minutes <= 0)
                {
                    MessageBox.Show("Please enter a valid duration in minutes.",
                        "Invalid Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                int min = int.Parse(ConfigurationManager.AppSettings["MinSessionDuration"] ?? "15");
                int max = int.Parse(ConfigurationManager.AppSettings["MaxSessionDuration"] ?? "480");
                if (minutes < min || minutes > max)
                {
                    MessageBox.Show($"Duration must be between {min} and {max} minutes.",
                        "Invalid Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return true;
            }

            string sel = (cboDuration.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            if (int.TryParse(sel.Split(' ')[0], out minutes)) return true;

            MessageBox.Show("Please select a duration.", "Duration Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // ─────────────────────────────────────────────────────────
        //  CLOSING
        // ─────────────────────────────────────────────────────────

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_timer.IsEnabled)
            {
                var r = MessageBox.Show("Active session running. Exit anyway?",
                    "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.No) { e.Cancel = true; return; }

                _timer.Stop();
                FinalizeSession("Manual");
            }

            StopProxyDetection();

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
            catch { /* best-effort cleanup */ }

            base.OnClosing(e);
        }

        // ─────────────────────────────────────────────────────────
        //  Network helpers
        // ─────────────────────────────────────────────────────────

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
