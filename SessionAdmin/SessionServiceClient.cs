using System;
using System.Configuration;
using System.ServiceModel;
using SessionManagement.WCF;

namespace SessionManagement.Client
{
    /// <summary>
    /// Client-side proxy for WCF Session Service with callback support
    /// </summary>
    public class SessionServiceClient : IDisposable
    {
        private DuplexChannelFactory<ISessionService> channelFactory;
        private ISessionService serviceProxy;
        private SessionServiceCallbackHandler callbackHandler;
        private bool isConnected = false;

        public event EventHandler<SessionTerminatedEventArgs> SessionTerminated;
        public event EventHandler<TimeWarningEventArgs> TimeWarning;
        public event EventHandler<ServerMessageEventArgs> ServerMessage;

        #region Connection Management

        public bool Connect()
        {
            try
            {
                if (isConnected)
                {
                    return true;
                }

                // Create callback handler
                callbackHandler = new SessionServiceCallbackHandler();
                callbackHandler.SessionTerminated += (s, e) => OnSessionTerminated(e);
                callbackHandler.TimeWarning += (s, e) => OnTimeWarning(e);
                callbackHandler.ServerMessage += (s, e) => OnServerMessage(e);

                // Create instance context with callback
                InstanceContext instanceContext = new InstanceContext(callbackHandler);

                // Create channel factory
                channelFactory = new DuplexChannelFactory<ISessionService>(
                    instanceContext,
                    "SessionServiceEndpoint");

                // Create service proxy
                serviceProxy = channelFactory.CreateChannel();

                // Test connection
                var channel = (IClientChannel)serviceProxy;
                channel.Open();

                isConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Connection error: {ex.Message}");
                isConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (serviceProxy != null)
                {
                    var channel = (IClientChannel)serviceProxy;
                    if (channel.State == CommunicationState.Opened)
                    {
                        channel.Close();
                    }
                }

                if (channelFactory != null)
                {
                    channelFactory.Close();
                }

                isConnected = false;
            }
            catch (Exception ex)
            {
                LogError($"Disconnect error: {ex.Message}");
            }
        }

        public bool IsConnected => isConnected;

        #endregion

        #region Service Methods

        public AuthenticationResponse AuthenticateUser(string username, string password, string clientCode)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return new AuthenticationResponse
                    {
                        IsAuthenticated = false,
                        ErrorMessage = "Not connected to server"
                    };
                }

                return serviceProxy.AuthenticateUser(username, password, clientCode);
            }
            catch (Exception ex)
            {
                LogError($"Authentication error: {ex.Message}");
                return new AuthenticationResponse
                {
                    IsAuthenticated = false,
                    ErrorMessage = $"Connection error: {ex.Message}"
                };
            }
        }

        public SessionStartResponse StartSession(int userId, string clientCode, int durationMinutes)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return new SessionStartResponse
                    {
                        Success = false,
                        ErrorMessage = "Not connected to server"
                    };
                }

                return serviceProxy.StartSession(userId, clientCode, durationMinutes);
            }
            catch (Exception ex)
            {
                LogError($"Start session error: {ex.Message}");
                return new SessionStartResponse
                {
                    Success = false,
                    ErrorMessage = $"Connection error: {ex.Message}"
                };
            }
        }

        public bool EndSession(int sessionId, string terminationType)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return false;
                }

                return serviceProxy.EndSession(sessionId, terminationType);
            }
            catch (Exception ex)
            {
                LogError($"End session error: {ex.Message}");
                return false;
            }
        }

        public SessionInfo GetSessionInfo(int sessionId)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return null;
                }

                return serviceProxy.GetSessionInfo(sessionId);
            }
            catch (Exception ex)
            {
                LogError($"Get session info error: {ex.Message}");
                return null;
            }
        }

        public bool UploadLoginImage(int sessionId, int userId, string imageBase64)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return false;
                }

                return serviceProxy.UploadLoginImage(sessionId, userId, imageBase64);
            }
            catch (Exception ex)
            {
                LogError($"Upload image error: {ex.Message}");
                return false;
            }
        }

        public bool UpdateClientStatus(string clientCode, string status)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return false;
                }

                return serviceProxy.UpdateClientStatus(clientCode, status);
            }
            catch (Exception ex)
            {
                LogError($"Update client status error: {ex.Message}");
                return false;
            }
        }

        public bool LogSecurityAlert(int sessionId, int userId, string alertType, string description, string severity)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return false;
                }

                return serviceProxy.LogSecurityAlert(sessionId, userId, alertType, description, severity);
            }
            catch (Exception ex)
            {
                LogError($"Log security alert error: {ex.Message}");
                return false;
            }
        }

        public void SubscribeForNotifications(string clientCode)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return;
                }

                serviceProxy.SubscribeForNotifications(clientCode);
            }
            catch (Exception ex)
            {
                LogError($"Subscribe error: {ex.Message}");
            }
        }

        public void UnsubscribeFromNotifications(string clientCode)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return;
                }

                serviceProxy.UnsubscribeFromNotifications(clientCode);
            }
            catch (Exception ex)
            {
                LogError($"Unsubscribe error: {ex.Message}");
            }
        }

        #endregion

        #region Admin Methods

        public SessionInfo[] GetActiveSessions()
        {
            try
            {
                if (!EnsureConnection())
                {
                    return new SessionInfo[0];
                }

                return serviceProxy.GetActiveSessions();
            }
            catch (Exception ex)
            {
                LogError($"Get active sessions error: {ex.Message}");
                return new SessionInfo[0];
            }
        }

        public ClientInfo[] GetAllClients()
        {
            try
            {
                if (!EnsureConnection())
                {
                    return new ClientInfo[0];
                }

                return serviceProxy.GetAllClients();
            }
            catch (Exception ex)
            {
                LogError($"Get all clients error: {ex.Message}");
                return new ClientInfo[0];
            }
        }

        public AlertInfo[] GetUnacknowledgedAlerts()
        {
            try
            {
                if (!EnsureConnection())
                {
                    return new AlertInfo[0];
                }

                return serviceProxy.GetUnacknowledgedAlerts();
            }
            catch (Exception ex)
            {
                LogError($"Get alerts error: {ex.Message}");
                return new AlertInfo[0];
            }
        }

        public bool AcknowledgeAlert(int alertId, int adminUserId)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return false;
                }

                return serviceProxy.AcknowledgeAlert(alertId, adminUserId);
            }
            catch (Exception ex)
            {
                LogError($"Acknowledge alert error: {ex.Message}");
                return false;
            }
        }

        public string DownloadLoginImage(int sessionId)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return null;
                }

                return serviceProxy.DownloadLoginImage(sessionId);
            }
            catch (Exception ex)
            {
                LogError($"Download image error: {ex.Message}");
                return null;
            }
        }

        public decimal GetCurrentBillingRate()
        {
            try
            {
                if (!EnsureConnection())
                {
                    return 0;
                }

                return serviceProxy.GetCurrentBillingRate();
            }
            catch (Exception ex)
            {
                LogError($"Get billing rate error: {ex.Message}");
                return 0;
            }
        }

        public ReportData GetSessionReport(DateTime fromDate, DateTime toDate)
        {
            try
            {
                if (!EnsureConnection())
                {
                    return new ReportData();
                }

                return serviceProxy.GetSessionReport(fromDate, toDate);
            }
            catch (Exception ex)
            {
                LogError($"Get report error: {ex.Message}");
                return new ReportData();
            }
        }

        #endregion

        #region Helper Methods

        private bool EnsureConnection()
        {
            if (!isConnected)
            {
                return Connect();
            }

            // Check if channel is still open
            if (serviceProxy != null)
            {
                var channel = (IClientChannel)serviceProxy;
                if (channel.State != CommunicationState.Opened)
                {
                    isConnected = false;
                    return Connect();
                }
            }

            return true;
        }

        private void LogError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionServiceClient] {message}");
            // Add file logging if needed
        }

        #endregion

        #region Event Handlers

        protected virtual void OnSessionTerminated(SessionTerminatedEventArgs e)
        {
            SessionTerminated?.Invoke(this, e);
        }

        protected virtual void OnTimeWarning(TimeWarningEventArgs e)
        {
            TimeWarning?.Invoke(this, e);
        }

        protected virtual void OnServerMessage(ServerMessageEventArgs e)
        {
            ServerMessage?.Invoke(this, e);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Disconnect();
        }

        #endregion
    }

    #region Callback Handler

    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant)]
    internal class SessionServiceCallbackHandler : ISessionServiceCallback
    {
        public event EventHandler<SessionTerminatedEventArgs> SessionTerminated;
        public event EventHandler<TimeWarningEventArgs> TimeWarning;
        public event EventHandler<ServerMessageEventArgs> ServerMessage;

        public void OnSessionTerminated(int sessionId, string reason)
        {
            SessionTerminated?.Invoke(this, new SessionTerminatedEventArgs
            {
                SessionId = sessionId,
                Reason = reason,
                Timestamp = DateTime.Now
            });
        }

        public void OnTimeWarning(int sessionId, int remainingMinutes)
        {
            TimeWarning?.Invoke(this, new TimeWarningEventArgs
            {
                SessionId = sessionId,
                RemainingMinutes = remainingMinutes,
                Timestamp = DateTime.Now
            });
        }

        public void OnServerMessage(string message)
        {
            ServerMessage?.Invoke(this, new ServerMessageEventArgs
            {
                Message = message,
                Timestamp = DateTime.Now
            });
        }
    }

    #endregion

    #region Event Args

    public class SessionTerminatedEventArgs : EventArgs
    {
        public int SessionId { get; set; }
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TimeWarningEventArgs : EventArgs
    {
        public int SessionId { get; set; }
        public int RemainingMinutes { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ServerMessageEventArgs : EventArgs
    {
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #region Configuration Helper

    public static class ServiceConfiguration
    {
        public static string ServerAddress => ConfigurationManager.AppSettings["ServerAddress"] ?? "localhost";
        public static string ServerPort => ConfigurationManager.AppSettings["ServerPort"] ?? "8080";
        public static string ClientCode => ConfigurationManager.AppSettings["ClientCode"] ?? "CL001";
        public static string ClientMachineName => ConfigurationManager.AppSettings["ClientMachineName"] ?? Environment.MachineName;

        public static string GetServiceAddress()
        {
            return $"net.tcp://{ServerAddress}:{ServerPort}/SessionService";
        }
    }

    #endregion
}