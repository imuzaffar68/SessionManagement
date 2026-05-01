using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SessionManagement.Security
{
    // ══════════════════════════════════════════════════════════════════════════
    //  IllegalActivityDetectionService
    //
    //  Implements FR-12 (UC-16) — Illegal / Suspicious Activity Detection.
    //
    //  SRS FR-12 rules monitored every scan interval:
    //    Rule 1  — VPN / tunneling network adapters active
    //    Rule 2  — Registry proxy settings changed (WinINet + WinHTTP)
    //    Rule 3  — Blacklisted proxy/VPN/remote-access processes running
    //    Rule 4  — Repeated failed logins (handled at login layer, not here)
    //
    //  Additional detections aligned with the project brief:
    //    Rule 5  — Mobile hotspot detection (IP-range based — reliable)
    //    Rule 6  — Network type switching (Ethernet ↔ WiFi ↔ Hotspot)
    //    Rule 7  — System clock tampering
    //    Rule 8  — RDP / Remote Desktop session active
    //    Rule 9  — Virtual / fake webcam (OBS, DroidCam, ManyCam, etc.)
    //    Rule 10 — WCF server heartbeat loss (offline / disconnected client)
    //
    //  Architecture:
    //    • Runs entirely on the CLIENT side (SessionClient process).
    //    • Background Timer fires every checkIntervalSeconds.
    //    • On detection → fires AlertDetected event (thread-pool thread).
    //    • MainWindow.OnIllegalActivityDetected → _svc.LogSecurityAlert() via WCF.
    //    • Server inserts tblAlert + tblSystemLog, broadcasts to admin (FR-14).
    //
    //  De-duplication:
    //    • Each unique alert key fires at most ONCE per session to prevent flood.
    //    • Exception: network-switch alerts re-arm after every switch.
    //
    //  C# 7.3 / .NET 4.7.2 — strict compatibility.
    // ══════════════════════════════════════════════════════════════════════════
    public sealed class IllegalActivityDetectionService : IDisposable
    {
        // ── Public event ──────────────────────────────────────────────────────
        public event EventHandler<SecurityAlertEventArgs> AlertDetected;

        // ── Session context ───────────────────────────────────────────────────
        private readonly int _sessionId;
        private readonly int _userId;

        // ── Timer ─────────────────────────────────────────────────────────────
        private readonly Timer _timer;
        private volatile bool _running;

        // ── De-duplication store ──────────────────────────────────────────────
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _reportLock = new object();

        // ── Network-switch tracking ───────────────────────────────────────────
        private string _lastNetworkProfile = string.Empty;   // "Ethernet" | "WiFi" | "Hotspot" | "Unknown"
        private bool _networkProfileInitialised;

        // ── Clock-tamper tracking ─────────────────────────────────────────────
        private DateTime _serviceStartWallClock;   // DateTime.Now when service started
        private long _serviceStartTick;         // Environment.TickCount64 equivalent

        // ─────────────────────────────────────────────────────────────────────
        //  Blacklisted process names (lower-case, without .exe extension)
        //
        //  FR-12 Rule 3: proxy-bypass, VPN clients, Tor, remote-access tools.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly HashSet<string> _blacklist =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── VPN clients ────────────────────────────────────────────────
            "openvpn", "openvpn-gui",
            "nordvpn", "nordvpnservice",
            "expressvpn", "expressvpnservice",
            "windscribe", "windscribeservice",
            "protonvpn", "protonvpn-service",
            "surfshark", "surfsharkservice",
            "cyberghost", "cyberghostservice",
            "tunnelbear",
            "purevpn", "purevpnservice",
            "hotspotshield",
            "privatevpn",
            "mullvad", "mullvad-vpn",
            "ipvanish",
            "hidemyass", "hma",
            "pia", "privateinternetaccess",
            "vyprvpn",
            "wireguard",

            // ── Proxy tools ────────────────────────────────────────────────
            "proxifier",
            "freecap",
            "sockscap",
            "proxycap",
            "proxytunnel",
            "stunnel",
            "httptunnel",
            "privoxy",
            "polipo",
            "3proxy",

            // ── Tor / anonymisers ──────────────────────────────────────────
            "tor",
            "torbrowser", "firefox",   // firefox is tor browser's host process
            "vidalia",
            "obfs4proxy",
            "snowflake-client",

            // ── Circumvention tools ────────────────────────────────────────
            "psiphon", "psiphon3",
            "ultrasurf",
            "lantern",
            "freegate",
            "zenmate",
            "shadowsocks", "shadowsocksr", "sslocal",
            "v2ray", "v2rayn", "v2rayng",
            "xray",
            "clash", "clashx",
            "trojan",
            "naiveproxy",
            "goproxy",

            // ── Remote access / screen sharing ────────────────────────────
            "teamviewer", "teamviewer_service",
            "anydesk",
            "logmein",
            "ammyyadmin",
            "uvnc_service", "winvnc",
            "ultravnc",
            "tightvnc", "tvnserver",
            "realvnc", "vncserver",
            "rustdesk",
            "supremo",
            "radmin",
            "connectwise",

            // ── Virtual/fake webcam drivers ────────────────────────────────
            "obs", "obs64", "obs32",            // OBS Studio (virtual cam)
            "droidcam",                          // DroidCam
            "manycam",                           // ManyCam
            "splitcam",                          // SplitCam
            "xsplit",                            // XSplit
            "vcamservice",                       // generic virtual cam service
            "iriun",                             // Iriun webcam
            "epoccam",                           // EpocCam
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Mobile hotspot IP ranges (reliable, address-based)
        //
        //  Name-based detection is unreliable because adapter names vary by
        //  driver version and locale. IP prefix matching is deterministic.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly string[] _hotspotPrefixes = new string[]
        {
            "192.168.43.",   // Android hotspot (standard)
            "192.168.42.",   // Android hotspot (alternate)
            "172.20.10.",    // iPhone / iOS hotspot
            "192.168.137.",  // Windows Mobile Hotspot (ICS)
            "192.168.0.",    // Generic — only flagged when adapter name hints hotspot
        };


        #region Constructor
        public IllegalActivityDetectionService(int sessionId, int userId,
            int checkIntervalSeconds = 60)
        {
            _sessionId = sessionId;
            _userId = userId;
            _running = true;

            // Record reference point for clock-tamper detection
            _serviceStartWallClock = DateTime.UtcNow;
            _serviceStartTick = Environment.TickCount;   // milliseconds since boot (wraps ~49 days)

            int clamped = checkIntervalSeconds < 15 ? 15 : checkIntervalSeconds;
            var interval = TimeSpan.FromSeconds(clamped);

            // First scan after one interval (give system time to settle after login)
            _timer = new Timer(RunChecks, null, interval, interval);
        }


        #endregion

        #region Main scan loop — runs on thread-pool
        private void RunChecks(object state)
        {
            if (!_running) return;

            SafeRun(CheckRegistryProxy);       // FR-12 Rule 2a — WinINet proxy
            SafeRun(CheckWinHttpProxy);        // FR-12 Rule 2b — WinHTTP proxy (netsh)
            SafeRun(CheckPacScript);           // FR-12 Rule 2c — PAC/AutoConfigURL
            SafeRun(CheckVpnAdapters);         // FR-12 Rule 1  — TAP/TUN/Wintun adapters
            SafeRun(CheckMobileHotspot);       // Rule 5        — IP-range hotspot
            SafeRun(CheckNetworkSwitch);       // Rule 6        — network type change
            SafeRun(CheckBlacklistedProcesses);// FR-12 Rule 3  — blacklisted processes
            SafeRun(CheckRdpSession);          // Rule 8        — RDP active
            SafeRun(CheckVirtualWebcam);       // Rule 9        — virtual/fake webcam
            SafeRun(CheckClockTamper);         // Rule 7        — system time change
        }

        // ═════════════════════════════════════════════════════════════════════
        //  FR-12 Rule 2a — WinINet registry proxy (Internet Explorer / system)
        //
        //  BUG FIX: ProxyEnable may be stored as REG_DWORD (int) or REG_SZ
        //  (string "1"). Convert.ToInt32() handles both safely.
        // ═════════════════════════════════════════════════════════════════════
        private void CheckRegistryProxy()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                if (key == null) return;

                // --- ProxyEnable ---
                object raw = key.GetValue("ProxyEnable");
                if (raw != null)
                {
                    int enabled = 0;
                    try { enabled = Convert.ToInt32(raw); }  // handles DWORD and string "1"
                    catch { enabled = 0; }

                    if (enabled == 1)
                    {
                        string server = string.Empty;
                        object srv = key.GetValue("ProxyServer");
                        if (srv != null) server = srv.ToString().Trim();

                        Raise("WININET_PROXY",
                            "ProxySettingsEnabled",
                            "WinINet system proxy enabled — server: " +
                            (string.IsNullOrEmpty(server) ? "(not set)" : server),
                            "High");
                    }
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  FR-12 Rule 2b — WinHTTP proxy (used by services, PowerShell, WCF)
        //
        //  WinHTTP proxy is stored separately from WinINet and is NOT visible
        //  in the Internet Settings registry key. Must query via netsh or
        //  directly from the WinHTTP registry hive.
        //
        //  Method: Read HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\
        //          Internet Settings\Connections\WinHttpSettings
        //  Fallback: run "netsh winhttp show proxy" and parse output.
        // ═════════════════════════════════════════════════════════════════════
        private void CheckWinHttpProxy()
        {
            // Primary: registry-based (no process spawn, faster, always available)
            bool foundViaRegistry = CheckWinHttpRegistry();
            if (foundViaRegistry) return;   // already raised — no need to run netsh

            // Fallback: netsh output parsing
            CheckWinHttpNetsh();
        }

        private bool CheckWinHttpRegistry()
        {
            // WinHTTP proxy blob is stored in HKLM
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key == null) return false;

                    byte[] blob = key.GetValue("WinHttpSettings") as byte[];
                    if (blob == null || blob.Length < 16) return false;

                    // WinHTTP settings blob structure (little-endian):
                    //   Offset 0x00 : DWORD signature (0x18 = 24 for no-proxy, varies)
                    //   Offset 0x04 : DWORD version
                    //   Offset 0x08 : DWORD flags  (0x03 = proxy set, 0x01 = direct)
                    //   Offset 0x0C : DWORD proxy server length
                    //   Offset 0x10 : proxy server string (ASCII)

                    int flags = blob.Length >= 12 ? BitConverter.ToInt32(blob, 8) : 0;

                    // Bit 0x02 set = proxy is configured
                    if ((flags & 0x02) == 0x02)
                    {
                        string proxyStr = string.Empty;
                        if (blob.Length >= 16)
                        {
                            int proxyLen = BitConverter.ToInt32(blob, 12);
                            if (proxyLen > 0 && blob.Length >= 16 + proxyLen)
                                proxyStr = Encoding.ASCII.GetString(blob, 16, proxyLen).Trim('\0');
                        }

                        Raise("WINHTTP_PROXY",
                            "WinHttpProxyEnabled",
                            "WinHTTP proxy configured — server: " +
                            (string.IsNullOrEmpty(proxyStr) ? "(unknown)" : proxyStr),
                            "High");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WinHTTP Registry] " + ex.Message);
            }
            return false;
        }

        private void CheckWinHttpNetsh()
        {
            // Run: netsh winhttp show proxy
            // Expected output on clean system: "Direct access (no proxy server)."
            // On proxy system: "Proxy Server(s) :  proxy.example.com:8080"
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "winhttp show proxy",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                string output = string.Empty;
                using (var proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.Start();
                    output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);  // max 3 s
                }

                if (string.IsNullOrWhiteSpace(output)) return;

                string lower = output.ToLowerInvariant();

                // Clean system says "direct access" — anything else is a proxy
                if (!lower.Contains("direct access") &&
                    (lower.Contains("proxy server") || lower.Contains("proxy:")))
                {
                    // Extract server value if present
                    string proxyValue = string.Empty;
                    foreach (string line in output.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("Proxy Server", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Proxy:", StringComparison.OrdinalIgnoreCase))
                        {
                            int colon = trimmed.IndexOf(':');
                            if (colon >= 0 && colon < trimmed.Length - 1)
                                proxyValue = trimmed.Substring(colon + 1).Trim();
                            break;
                        }
                    }

                    Raise("WINHTTP_PROXY_NETSH",
                        "WinHttpProxyEnabled",
                        "WinHTTP proxy detected via netsh — " +
                        (string.IsNullOrEmpty(proxyValue) ? output.Trim() : proxyValue),
                        "High");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[netsh winhttp] " + ex.Message);
            }
        }


        #endregion

        #region FR-12 Rule 2c — PAC script / AutoConfigURL
        private void CheckPacScript()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                if (key == null) return;

                object raw = key.GetValue("AutoConfigURL");
                if (raw != null)
                {
                    string url = raw.ToString().Trim();
                    if (!string.IsNullOrEmpty(url))
                        Raise("PAC_SCRIPT:" + url,
                            "ProxyAutoConfigDetected",
                            "PAC/Auto-config proxy script URL set: " + url,
                            "Medium");
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  FR-12 Rule 1 — VPN / TAP / TUN / Wintun adapters
        //
        //  Checks adapter description AND GUID-based device class for
        //  Wintun (WireGuard's kernel driver) which does not expose "tun"
        //  in its adapter name string.
        // ═════════════════════════════════════════════════════════════════════
        private void CheckVpnAdapters()
        {
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return; }

            foreach (var nic in nics)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                string desc = nic.Description.ToLowerInvariant();
                string name = nic.Name.ToLowerInvariant();

                bool isTunnel =
                    // TAP-Windows (OpenVPN legacy)
                    desc.Contains("tap-windows") || desc.Contains("tap adapter") ||
                    // TUN adapters (Linux-style)
                    desc.Contains("tun ") || name.StartsWith("tun") ||
                    // WireGuard / Wintun
                    desc.Contains("wireguard") || desc.Contains("wintun") ||
                    name.Contains("wg") ||
                    // Named VPN vendors
                    desc.Contains("nordvpn") || desc.Contains("nord ") ||
                    desc.Contains("expressvpn") || desc.Contains("express ") ||
                    desc.Contains("protonvpn") || desc.Contains("proton ") ||
                    desc.Contains("mullvad") ||
                    desc.Contains("surfshark") ||
                    desc.Contains("hotspotshield") ||
                    desc.Contains("tunnelbear") ||
                    // Generic VPN keyword in name
                    desc.Contains(" vpn") || name.Contains("vpn");

                if (isTunnel)
                    Raise("VPN:" + nic.Name,
                        "VpnAdapterActive",
                        "VPN/tunneling adapter active — " + nic.Description + " (" + nic.Name + ")",
                        "High");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rule 5 — Mobile hotspot detection (IP-range based)
        //
        //  FIX: adapter name-based detection is unreliable — driver names
        //  differ across Windows versions and locales. Instead we enumerate
        //  all active IPv4 addresses and match against known hotspot subnets.
        // ═════════════════════════════════════════════════════════════════════
        private void CheckMobileHotspot()
        {
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return; }

            foreach (var nic in nics)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); }
                catch { continue; }

                foreach (var uni in props.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = uni.Address.ToString();

                    // Check definite hotspot ranges (Android, iOS, Windows ICS)
                    if (ip.StartsWith("192.168.43.") ||  // Android standard
                        ip.StartsWith("192.168.42.") ||  // Android alternate
                        ip.StartsWith("172.20.10.") ||  // iOS iPhone
                        ip.StartsWith("192.168.137."))   // Windows Mobile Hotspot
                    {
                        Raise("HOTSPOT:" + ip,
                            "MobileHotspotDetected",
                            "Mobile hotspot connection detected — IP: " + ip +
                            " on " + nic.Description,
                            "High");
                        break;
                    }

                    // For 192.168.0.x — only flag if adapter name also hints hotspot
                    if (ip.StartsWith("192.168.0."))
                    {
                        string desc = nic.Description.ToLowerInvariant();
                        string nm = nic.Name.ToLowerInvariant();
                        if (desc.Contains("hotspot") || desc.Contains("mobile") ||
                            nm.Contains("hotspot") || nm.Contains("mobile") ||
                            desc.Contains("softap") || nm.Contains("softap"))
                        {
                            Raise("HOTSPOT:" + ip,
                                "MobileHotspotDetected",
                                "Possible mobile hotspot — IP: " + ip +
                                " on " + nic.Description,
                                "Medium");
                            break;
                        }
                    }
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rule 6 — Network type switching (Ethernet ↔ WiFi ↔ Hotspot)
        //
        //  The expected usage environment is a wired LAN (internet cafe).
        //  Switching to WiFi or hotspot mid-session is suspicious.
        //  We record the primary interface type at first scan and alert on change.
        // ═════════════════════════════════════════════════════════════════════
        private void CheckNetworkSwitch()
        {
            string currentProfile = GetNetworkProfile();

            if (!_networkProfileInitialised)
            {
                _lastNetworkProfile = currentProfile;
                _networkProfileInitialised = true;
                return;   // baseline established — no alert on first scan
            }

            if (currentProfile != _lastNetworkProfile &&
                !string.IsNullOrEmpty(_lastNetworkProfile) &&
                !string.IsNullOrEmpty(currentProfile))
            {
                string key = "NETSWITCH:" + _lastNetworkProfile + "→" + currentProfile;

                // Network-switch alert re-arms on each change (remove from dedupe)
                lock (_reportLock) { _reported.Remove(key); }

                Raise(key,
                    "NetworkTypeSwitched",
                    "Network type changed during session: " + _lastNetworkProfile +
                    " → " + currentProfile,
                    "Medium");

                _lastNetworkProfile = currentProfile;
            }
        }

        private static string GetNetworkProfile()
        {
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return "Unknown"; }

            foreach (var nic in nics)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                // Check for hotspot IP first
                try
                {
                    foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = uni.Address.ToString();
                        if (ip.StartsWith("192.168.43.") || ip.StartsWith("172.20.10.") ||
                            ip.StartsWith("192.168.137.") || ip.StartsWith("192.168.42."))
                            return "Hotspot";
                    }
                }
                catch { /* skip */ }

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    return "Ethernet";
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    return "WiFi";
                if (nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet)
                    return "Ethernet";
            }
            return "Unknown";
        }


        #endregion

        #region FR-12 Rule 3 — Blacklisted process detection
        private void CheckBlacklistedProcesses()
        {
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return; }

            foreach (var proc in all)
            {
                string name = null;
                try { name = proc.ProcessName; }
                catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;

                string bare = System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant();

                // Special-case: only flag firefox if it's tor-bundled
                // (running from AppData\Tor Browser path)
                if (bare == "firefox")
                {
                    string mainModule = null;
                    try { mainModule = proc.MainModule?.FileName ?? string.Empty; }
                    catch { }
                    if (mainModule != null &&
                        (mainModule.ToLowerInvariant().Contains("tor browser") ||
                         mainModule.ToLowerInvariant().Contains("torbrowser")))
                    {
                        Raise("PROC:torbrowser",
                            "BlacklistedProcessRunning",
                            "Tor Browser (Firefox) detected: " + mainModule,
                            "High");
                    }
                    continue;  // regular Firefox is not blacklisted
                }

                if (_blacklist.Contains(bare))
                    Raise("PROC:" + bare,
                        "BlacklistedProcessRunning",
                        "Blacklisted process running: " + name,
                        GetProcessSeverity(bare));
            }
        }

        private static string GetProcessSeverity(string bare)
        {
            // Remote-access tools are High; virtual webcam drivers are Medium
            if (bare == "obs" || bare == "obs64" || bare == "obs32" ||
                bare == "droidcam" || bare == "manycam" || bare == "splitcam" ||
                bare == "xsplit" || bare == "iriun" || bare == "epoccam")
                return "Medium";
            return "High";
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rule 8 — RDP / Remote Desktop session active
        //
        //  GetSystemMetrics(SM_REMOTESESSION) returns non-zero when the
        //  current process is running inside an RDP session.
        // ═════════════════════════════════════════════════════════════════════
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_REMOTESESSION = 0x1000;

        private void CheckRdpSession()
        {
            try
            {
                int remote = GetSystemMetrics(SM_REMOTESESSION);
                if (remote != 0)
                    Raise("RDP_SESSION",
                        "RemoteDesktopSessionDetected",
                        "Client is running inside an RDP / Remote Desktop session",
                        "High");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[RDP check] " + ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rule 9 — Virtual / fake webcam detection
        //
        //  Checks the DirectShow / Windows Camera device registry for
        //  known virtual camera driver names (OBS, DroidCam, ManyCam, etc.)
        //  Also checks if OBS Virtual Camera filter is registered in the
        //  CLSID registry.
        // ═════════════════════════════════════════════════════════════════════
        private void CheckVirtualWebcam()
        {
            // Known virtual camera device strings (registry substring match)
            string[] virtualCamHints = new string[]
            {
                "obs-camera",
                "obs virtual",
                "droidcam",
                "manycam",
                "splitcam",
                "xsplit",
                "vcam",
                "virtual camera",
                "virtual webcam",
                "iriun",
                "epoccam",
                "lcs virtual"
            };

            // Check under HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnP\Hive
            // and under HKLM\SYSTEM\CurrentControlSet\Control\Class\{6BDD1FC6-...} (cameras)
            const string cameraClass = @"SYSTEM\CurrentControlSet\Control\Class\{6BDD1FC6-810F-11D0-BEC7-08002BE2092F}";
            try
            {
                using (var classKey = Registry.LocalMachine.OpenSubKey(cameraClass))
                {
                    if (classKey != null)
                    {
                        foreach (string subName in classKey.GetSubKeyNames())
                        {
                            try
                            {
                                using (var dev = classKey.OpenSubKey(subName))
                                {
                                    if (dev == null) continue;
                                    string friendly = (dev.GetValue("FriendlyName") ?? string.Empty).ToString().ToLowerInvariant();
                                    string driver = (dev.GetValue("DriverDesc") ?? string.Empty).ToString().ToLowerInvariant();
                                    string devId = (dev.GetValue("DeviceInstance") ?? string.Empty).ToString().ToLowerInvariant();

                                    foreach (string hint in virtualCamHints)
                                    {
                                        if (friendly.Contains(hint) || driver.Contains(hint) || devId.Contains(hint))
                                        {
                                            string display = string.IsNullOrEmpty(friendly) ? driver : friendly;
                                            Raise("VCAM:" + hint,
                                                "VirtualCameraDetected",
                                                "Virtual/fake webcam device found: " + display,
                                                "Medium");
                                            break;
                                        }
                                    }
                                }
                            }
                            catch (System.Security.SecurityException secEx)
                            {
                                Debug.WriteLine("[VCam registry: SecurityException] " + secEx.Message);
                                continue;
                            }
                        }
                    }
                }
            }
            catch (System.Security.SecurityException secEx)
            {
                Debug.WriteLine("[VCam registry: SecurityException] " + secEx.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[VCam registry] " + ex.Message);
            }

            // Also check if OBS Virtual Camera COM filter is registered
            const string obsClsid = @"CLSID\{A3FCE0F5-3493-419F-958A-ABA1250EC20B}";
            try
            {
                using (var clsidKey = Registry.ClassesRoot.OpenSubKey(obsClsid))
                {
                    if (clsidKey != null)
                        Raise("VCAM:obs_clsid",
                            "VirtualCameraDetected",
                            "OBS Virtual Camera COM filter is registered on this system",
                            "Medium");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[OBS CLSID] " + ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Rule 7 — System clock tamper detection
        //
        //  We compare elapsed real time (DateTime.UtcNow) with elapsed
        //  Environment.TickCount (milliseconds since OS boot, not affected
        //  by clock changes). A significant discrepancy means the wall-clock
        //  was moved forward or backward.
        //
        //  Tolerance: 60 seconds (covers NTP adjustments and check jitter).
        // ═════════════════════════════════════════════════════════════════════
        private void CheckClockTamper()
        {
            try
            {
                // Elapsed according to the monotonic OS tick counter (not adjustable by user)
                long nowTick = Environment.TickCount;
                long tickElapsedMs = unchecked(nowTick - _serviceStartTick);  // handles wrap
                if (tickElapsedMs < 0) tickElapsedMs += (long)uint.MaxValue + 1; // 32-bit wrap

                // Elapsed according to wall clock (adjustable)
                double wallElapsedMs = (DateTime.UtcNow - _serviceStartWallClock).TotalMilliseconds;

                // Discrepancy > 60 seconds either direction = tampering
                double diffMs = Math.Abs(wallElapsedMs - tickElapsedMs);
                if (diffMs > 60_000 && tickElapsedMs > 15_000)
                {
                    Raise("CLOCK_TAMPER",
                        "SystemTimeTampered",
                        string.Format(
                            "System clock discrepancy detected — wall clock moved by ~{0:F0} seconds",
                            (wallElapsedMs - tickElapsedMs) / 1000.0),
                        "High");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ClockTamper] " + ex.Message);
            }
        }


        #endregion

        #region Helpers
        private void Raise(string dedupeKey, string alertType, string description, string severity)
        {
            lock (_reportLock)
            {
                if (_reported.Contains(dedupeKey)) return;
                _reported.Add(dedupeKey);
            }

            var handler = AlertDetected;
            if (handler != null)
            {
                handler(this, new SecurityAlertEventArgs
                {
                    SessionId = _sessionId,
                    UserId = _userId,
                    AlertType = alertType,
                    Description = description,
                    Severity = severity,
                    DetectedAt = DateTime.Now
                });
            }
        }

        private static void SafeRun(Action check)
        {
            try { check(); }
            catch (Exception ex)
            {
                Debug.WriteLine("[Detection] " + ex.Message);
            }
        }


        #endregion

        #region Lifecycle
        public void Stop()
        {
            _running = false;
            if (_timer != null)
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            Stop();
            if (_timer != null)
                _timer.Dispose();
        }
    }

    // ── Event args ─────────────────────────────────────────────────────────────

    public sealed class SecurityAlertEventArgs : EventArgs
    {
        public int SessionId { get; set; }
        public int UserId { get; set; }
        /// <summary>Maps to tblActivityType.Name in the database.</summary>
        public string AlertType { get; set; }
        public string Description { get; set; }
        /// <summary>Low | Medium | High</summary>
        public string Severity { get; set; }
        public DateTime DetectedAt { get; set; }
    }

        #endregion
}