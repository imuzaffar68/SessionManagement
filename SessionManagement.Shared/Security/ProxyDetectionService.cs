using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using Microsoft.Win32;

namespace SessionManagement.Security
{
    /// <summary>
    /// Monitors for proxy/VPN/tunneling and blacklisted processes while a session is active.
    /// FR-12 rules:
    ///   1. VPN/tunneling network adapters active
    ///   2. System proxy settings changed to unauthorized proxy
    ///   3. Blacklisted proxy-bypass processes running
    ///   4. Repeated login failures (handled in SessionService, not here)
    ///
    /// Subscribe to AlertDetected.  Call Stop() when the session ends.
    /// Alerts are de-duplicated within one session so the admin is not flooded.
    /// </summary>
    public sealed class ProxyDetectionService : IDisposable
    {
        // ── Events ────────────────────────────────────────────────
        public event EventHandler<SecurityAlertEventArgs> AlertDetected;

        // ── State ─────────────────────────────────────────────────
        private readonly int       _sessionId;
        private readonly int       _userId;
        private readonly Timer     _timer;
        private volatile bool      _running;

        // Keys of alerts already raised this session (no duplicates)
        private readonly HashSet<string> _reported = new HashSet<string>();
        private readonly object          _lock     = new object();

        // ── Blacklisted process names (lower-case, no extension) ──
        private static readonly HashSet<string> BlackList =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // VPN clients
            "openvpn","nordvpn","expressvpn","windscribe","protonvpn",
            "surfshark","cyberghost","tunnelbear","purevpn","hotspotshield",
            // Proxy tools
            "proxifier","freecap","sockscap","proxycap",
            // Tor / anonymisers
            "tor","torbrowser","vidalia",
            // Tunneling / circumvention
            "psiphon","ultrasurf","lantern","freegate","hoxx","zenmate"
        };

        // ─────────────────────────────────────────────────────────
        /// <param name="sessionId">Active session for alert context.</param>
        /// <param name="userId">Active user for alert context.</param>
        /// <param name="checkIntervalSeconds">Scan frequency (min 15 s).</param>
        public ProxyDetectionService(int sessionId, int userId,
            int checkIntervalSeconds = 60)
        {
            _sessionId = sessionId;
            _userId    = userId;
            _running   = true;

            var interval = TimeSpan.FromSeconds(Math.Max(15, checkIntervalSeconds));
            _timer = new Timer(RunChecks, null, interval, interval);
        }

        // ─────────────────────────────────────────────────────────
        //  Detection loop
        // ─────────────────────────────────────────────────────────

        private void RunChecks(object _)
        {
            if (!_running) return;
            try { CheckSystemProxy();      } catch { }
            try { CheckBlacklistProcess(); } catch { }
            try { CheckVpnAdapter();       } catch { }
        }

        // FR-12 rule 2 — system proxy settings
        private void CheckSystemProxy()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                if (key == null) return;

                int enabled = (int)(key.GetValue("ProxyEnable", 0) ?? 0);
                if (enabled == 1)
                {
                    string srv = key.GetValue("ProxyServer")?.ToString() ?? "";
                    Raise("SystemProxy", "ProxySettingsEnabled",
                        $"System proxy enabled: {srv}", "High");
                }

                string pac = key.GetValue("AutoConfigURL")?.ToString();
                if (!string.IsNullOrWhiteSpace(pac))
                    Raise("AutoConfigProxy", "ProxyAutoConfigDetected",
                        $"PAC proxy URL set: {pac}", "Medium");
            }
        }

        // FR-12 rule 3 — blacklisted processes
        private void CheckBlacklistProcess()
        {
            var running = Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return null; } })
                .Where(n => n != null)
                .ToList();

            foreach (var name in running)
            {
                string bare = System.IO.Path.GetFileNameWithoutExtension(name);
                if (BlackList.Contains(bare))
                    Raise($"Proc:{bare.ToLower()}", "BlacklistedProcessRunning",
                        $"Blacklisted process: {name}", "High");
            }
        }

        // FR-12 rule 1 — VPN network adapters
        private void CheckVpnAdapter()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                string desc = nic.Description.ToLowerInvariant();
                string nm   = nic.Name.ToLowerInvariant();

                bool isTun = desc.Contains("tap")      || desc.Contains("tun") ||
                             desc.Contains("vpn")       || desc.Contains("wireguard") ||
                             desc.Contains("wintun")    || desc.Contains("openssl") ||
                             nm.Contains("tun")         || nm.Contains("tap");

                if (isTun)
                    Raise($"VPN:{nic.Name}", "VpnAdapterActive",
                        $"VPN/TUN adapter active: {nic.Description}", "High");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Raise (de-duplicated)
        // ─────────────────────────────────────────────────────────

        private void Raise(string key, string alertType,
            string description, string severity)
        {
            lock (_lock)
            {
                if (_reported.Contains(key)) return;
                _reported.Add(key);
            }

            AlertDetected?.Invoke(this, new SecurityAlertEventArgs
            {
                SessionId   = _sessionId,
                UserId      = _userId,
                AlertType   = alertType,
                Description = description,
                Severity    = severity,
                DetectedAt  = DateTime.Now
            });
        }

        // ─────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────

        public void Stop()
        {
            _running = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            Stop();
            _timer?.Dispose();
        }
    }

    // ── Event args ────────────────────────────────────────────────
    public sealed class SecurityAlertEventArgs : EventArgs
    {
        public int      SessionId   { get; set; }
        public int      UserId      { get; set; }
        public string   AlertType   { get; set; }
        public string   Description { get; set; }
        public string   Severity    { get; set; }   // Low | Medium | High
        public DateTime DetectedAt  { get; set; }
    }
}
