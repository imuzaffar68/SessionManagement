namespace SessionManagement.WCF
{
    /// <summary>
    /// Named constants for all magic numbers used across the WCF service and server host.
    /// Update values here and every reference updates automatically.
    /// </summary>
    public static class ServiceConstants
    {
        // ── Heartbeat / Liveness ──────────────────────────────────
        /// <summary>How often the client sends a heartbeat to the server (seconds).</summary>
        public const int HeartbeatIntervalSeconds    = 30;

        /// <summary>How often the server checks for expired sessions (seconds).</summary>
        public const int ExpiryCheckIntervalSeconds  = 30;

        /// <summary>How often the server scans for offline clients (seconds).</summary>
        public const int OfflineCheckIntervalSeconds = 60;

        /// <summary>
        /// A client not heard from within this window is marked Offline (seconds).
        /// Must be > HeartbeatIntervalSeconds to allow one missed beat before flagging.
        /// </summary>
        public const int OfflineThresholdSeconds     = 90;

        // ── WCF Host ──────────────────────────────────────────────
        /// <summary>Maximum concurrent WCF calls / sessions the server accepts.</summary>
        public const int MaxConcurrentCalls    = 100;
        public const int MaxConcurrentSessions = 100;

        /// <summary>20 MB — large enough for webcam login images sent as Base64.</summary>
        public const int WcfMaxMessageBytes    = 20_971_520;
    }
}
