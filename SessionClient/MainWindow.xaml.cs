using System;
using System.Configuration;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SessionManagement.Client;
using SessionManagement.Media;
using SessionManagement.WCF;

namespace SessionClient
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer sessionTimer;
        private TimeSpan remainingTime;
        private TimeSpan totalDuration;
        private string currentUser;
        private int currentUserId;
        private int currentSessionId;
        private SessionServiceClient serviceClient;
        private WebcamHelper webcamHelper;
        private string clientCode;

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();
            InitializeTimer();
        }

        private void InitializeServices()
        {
            try
            {
                // Initialize WCF service client
                serviceClient = new SessionServiceClient();

                // Subscribe to server events
                serviceClient.SessionTerminated += ServiceClient_SessionTerminated;
                serviceClient.TimeWarning += ServiceClient_TimeWarning;
                serviceClient.ServerMessage += ServiceClient_ServerMessage;

                // Get client code from configuration
                clientCode = ServiceConfiguration.ClientCode;

                // Connect to server
                if (!serviceClient.Connect())
                {
                    MessageBox.Show("Unable to connect to server. Please check network connection and try again.",
                                  "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // Subscribe for server notifications
                    serviceClient.SubscribeForNotifications(clientCode);

                    // Update client status to Online
                    serviceClient.UpdateClientStatus(clientCode, "Online");
                }

                // Initialize webcam helper
                webcamHelper = new WebcamHelper();
                webcamHelper.CaptureError += Webcam_CaptureError;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeTimer()
        {
            sessionTimer = new DispatcherTimer();
            sessionTimer.Interval = TimeSpan.FromSeconds(1);
            sessionTimer.Tick += SessionTimer_Tick;
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            lblLoginError.Visibility = Visibility.Collapsed;

            // Validate input
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                lblLoginError.Text = "Please enter both username and password";
                lblLoginError.Visibility = Visibility.Visible;
                return;
            }

            // Disable login button to prevent multiple clicks
            btnLogin.IsEnabled = false;
            btnLogin.Content = "Authenticating...";

            try
            {
                // Call WCF service for authentication
                var response = serviceClient.AuthenticateUser(username, password, clientCode);

                if (response.IsAuthenticated)
                {
                    // Store user information
                    currentUser = response.Username;
                    currentUserId = response.UserId;

                    // UC-04: Capture User Image
                    CaptureUserImage();

                    // Show duration selection panel
                    ShowDurationPanel();
                }
                else
                {
                    lblLoginError.Text = response.ErrorMessage ?? "Invalid credentials. Please try again.";
                    lblLoginError.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                lblLoginError.Text = "Connection error. Please check server connection.";
                lblLoginError.Visibility = Visibility.Visible;
                MessageBox.Show($"Authentication error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnLogin.IsEnabled = true;
                btnLogin.Content = "Login";
            }
        }

        private void CaptureUserImage()
        {
            // UC-04: Capture User Image
            try
            {
                if (!webcamHelper.IsDeviceAvailable)
                {
                    MessageBox.Show("No webcam detected. Session will continue without image capture.",
                                  "Webcam Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Log to server that camera is unavailable
                    serviceClient.LogSecurityAlert(0, currentUserId, "CameraUnavailable",
                                                  "Webcam not detected during login", "Low");
                    return;
                }

                // Capture image from webcam
                Bitmap capturedImage = webcamHelper.CaptureImage();

                if (capturedImage != null)
                {
                    // UC-05: Send Image to Server
                    SendImageToServer(capturedImage);
                }
                else
                {
                    MessageBox.Show("Failed to capture image. Session will continue.",
                                  "Capture Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Image capture error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SendImageToServer(Bitmap image)
        {
            // UC-05: Send Image to Server
            try
            {
                // Convert bitmap to Base64
                string imageBase64 = WebcamHelper.BitmapToBase64(image, System.Drawing.Imaging.ImageFormat.Jpeg);

                if (!string.IsNullOrEmpty(imageBase64))
                {
                    // Upload to server (will be saved after session starts)
                    // For now, store temporarily - will be uploaded after session creation
                    this.Tag = imageBase64; // Temporary storage

                    MessageBox.Show("Image captured successfully", "Success",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending image: {ex.Message}", "Upload Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Webcam_CaptureError(object sender, WebcamErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Webcam error: {e.ErrorMessage}", "Webcam Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void ShowDurationPanel()
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            DurationPanel.Visibility = Visibility.Visible;
        }

        private void btnCancelDuration_Click(object sender, RoutedEventArgs e)
        {
            // Return to login screen
            DurationPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            txtUsername.Clear();
            txtPassword.Clear();
            currentUser = null;
        }

        private void btnStartSession_Click(object sender, RoutedEventArgs e)
        {
            int durationMinutes = 0;

            // Validate duration selection
            if (cboDuration.SelectedIndex == 4) // Custom
            {
                if (!int.TryParse(txtCustomDuration.Text, out durationMinutes) || durationMinutes <= 0)
                {
                    MessageBox.Show("Please enter a valid duration in minutes", "Invalid Duration",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check duration limits
                int minDuration = int.Parse(ConfigurationManager.AppSettings["MinSessionDuration"] ?? "15");
                int maxDuration = int.Parse(ConfigurationManager.AppSettings["MaxSessionDuration"] ?? "480");

                if (durationMinutes < minDuration || durationMinutes > maxDuration)
                {
                    MessageBox.Show($"Duration must be between {minDuration} and {maxDuration} minutes",
                                  "Invalid Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                string selected = (cboDuration.SelectedItem as ComboBoxItem)?.Content.ToString();
                durationMinutes = int.Parse(selected.Split(' ')[0]);
            }

            // Disable button during operation
            btnStartSession.IsEnabled = false;
            btnStartSession.Content = "Starting Session...";

            try
            {
                // UC-02: Start Session on server
                var response = serviceClient.StartSession(currentUserId, clientCode, durationMinutes);

                if (response.Success)
                {
                    currentSessionId = response.SessionId;

                    // Upload captured image if available
                    if (this.Tag != null && this.Tag is string imageBase64)
                    {
                        serviceClient.UploadLoginImage(currentSessionId, currentUserId, imageBase64);
                        this.Tag = null; // Clear temporary storage
                    }

                    // Start local session timer
                    StartSession(durationMinutes, response.StartTime, response.ExpectedEndTime);
                }
                else
                {
                    MessageBox.Show(response.ErrorMessage ?? "Failed to start session. Please try again.",
                                  "Session Start Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting session: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStartSession.IsEnabled = true;
                btnStartSession.Content = "Start Session";
            }
        }

        private void StartSession(int durationMinutes, DateTime startTime, DateTime expectedEndTime)
        {
            totalDuration = TimeSpan.FromMinutes(durationMinutes);

            // Calculate remaining time based on server time
            remainingTime = expectedEndTime - DateTime.Now;
            if (remainingTime.TotalSeconds < 0)
                remainingTime = totalDuration;

            // Update UI
            lblSessionUser.Text = currentUser;
            lblSessionDuration.Text = $"{durationMinutes} minutes";
            lblTimeRemaining.Text = remainingTime.ToString(@"hh\:mm\:ss");
            progressBar.Value = 100;

            // Show session panel
            DurationPanel.Visibility = Visibility.Collapsed;
            SessionPanel.Visibility = Visibility.Visible;

            // Start timer
            sessionTimer.Start();

            // Subscribe for server notifications
            serviceClient.SubscribeForNotifications(clientCode);
        }

        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            remainingTime = remainingTime.Subtract(TimeSpan.FromSeconds(1));

            // UC-06: View Remaining Time
            lblTimeRemaining.Text = remainingTime.ToString(@"hh\:mm\:ss");

            // Update progress bar
            double percentage = (remainingTime.TotalSeconds / totalDuration.TotalSeconds) * 100;
            progressBar.Value = percentage;

            // Change color when time is running low
            if (remainingTime.TotalMinutes <= 5)
            {
                lblTimeRemaining.Foreground = System.Windows.Media.Brushes.Red;
            }

            // UC-07: End Session Automatically
            if (remainingTime.TotalSeconds <= 0)
            {
                EndSessionAutomatically();
            }
        }

        private void EndSessionAutomatically()
        {
            sessionTimer.Stop();

            MessageBox.Show("Your session has expired.", "Session Ended",
                          MessageBoxButton.OK, MessageBoxImage.Information);

            // UC-07: End session on server
            FinalizeBillingAndLogs("Auto");

            // Return to login
            ResetToLogin();
        }

        private void btnEndSession_Click(object sender, RoutedEventArgs e)
        {
            // UC-08: Logout / Exit Session
            var result = MessageBox.Show("Are you sure you want to end this session?",
                                       "Confirm Exit",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                sessionTimer.Stop();

                // End session on server
                FinalizeBillingAndLogs("Manual");

                ResetToLogin();
            }
        }

        private void FinalizeBillingAndLogs(string terminationType)
        {
            try
            {
                // Calculate actual usage time
                TimeSpan usedTime = totalDuration.Subtract(remainingTime);

                // Call server to end session and finalize billing
                bool success = serviceClient.EndSession(currentSessionId, terminationType);

                if (success)
                {
                    // Get final billing information
                    decimal finalBilling = serviceClient.CalculateSessionBilling(currentSessionId);

                    MessageBox.Show($"Session ended successfully.\n\n" +
                                  $"Duration: {usedTime.TotalMinutes:F0} minutes\n" +
                                  $"Amount: ${finalBilling:F2}",
                                  "Session Summary",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Session ended locally. Server sync may be pending.",
                                  "Session End",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error finalizing session: {ex.Message}",
                              "Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private void ServiceClient_SessionTerminated(object sender, SessionTerminatedEventArgs e)
        {
            // Server has terminated the session
            Dispatcher.Invoke(() =>
            {
                if (e.SessionId == currentSessionId)
                {
                    sessionTimer.Stop();

                    MessageBox.Show($"Your session has been terminated by administrator.\n\nReason: {e.Reason}",
                                  "Session Terminated",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);

                    ResetToLogin();
                }
            });
        }

        private void ServiceClient_TimeWarning(object sender, TimeWarningEventArgs e)
        {
            // Server warning about remaining time
            Dispatcher.Invoke(() =>
            {
                if (e.SessionId == currentSessionId)
                {
                    MessageBox.Show($"Warning: Only {e.RemainingMinutes} minutes remaining in your session.",
                                  "Time Warning",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                }
            });
        }

        private void ServiceClient_ServerMessage(object sender, ServerMessageEventArgs e)
        {
            // General server message
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(e.Message, "Server Message",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
            });
        }

        private void ResetToLogin()
        {
            SessionPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;

            txtUsername.Clear();
            txtPassword.Clear();
            txtCustomDuration.Clear();
            cboDuration.SelectedIndex = 0;
            lblTimeRemaining.Foreground = System.Windows.Media.Brushes.Black;

            currentUser = null;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (sessionTimer.IsEnabled)
            {
                var result = MessageBox.Show("You have an active session. Are you sure you want to exit?",
                                           "Confirm Exit",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                sessionTimer.Stop();
                FinalizeBillingAndLogs("Manual");
            }

            // Cleanup
            try
            {
                // Unsubscribe from notifications
                if (serviceClient != null && serviceClient.IsConnected)
                {
                    serviceClient.UpdateClientStatus(clientCode, "Offline");
                    serviceClient.UnsubscribeFromNotifications(clientCode);
                    serviceClient.Disconnect();
                }

                // Dispose webcam
                webcamHelper?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }

            base.OnClosing(e);
        }
    }
}