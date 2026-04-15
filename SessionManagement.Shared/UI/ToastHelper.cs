using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SessionManagement.UI
{
    /// <summary>
    /// Sends Windows-native Action Center toast notifications.
    /// Each app registers its own AUMID via a Start-Menu shortcut on first launch.
    /// Calls are fire-and-forget; all errors are silently swallowed so a missing
    /// notification never crashes the application.
    /// </summary>
    public static class ToastHelper
    {
        // ── App user-model IDs ────────────────────────────────────────
        public const string AdminAppId  = "NetCafe.SessionManagement.Admin";
        public const string ClientAppId = "NetCafe.SessionManagement.Client";

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Ensures the app is registered in the Start Menu with its AUMID.
        /// Call once from App.OnStartup, before any toast is sent.
        /// </summary>
        public static void EnsureRegistered(string appId, string displayName)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(exePath)) return;

                string shortcutPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    displayName + ".lnk");

                if (!File.Exists(shortcutPath))
                    CreateShortcutWithAumid(shortcutPath, exePath, appId);
            }
            catch { }
        }

        /// <summary>Sends a toast notification visible in Action Center.</summary>
        public static void Show(string appId, string title, string body)
        {
            try
            {
                string xml = BuildXml(title, body);
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                var toast    = new ToastNotification(xmlDoc);
                var notifier = ToastNotificationManager.CreateToastNotifier(appId);
                notifier.Show(toast);
            }
            catch { }
        }

        // ── XML builder ───────────────────────────────────────────────

        private static string BuildXml(string title, string body)
        {
            return string.Format(
                "<toast>" +
                  "<visual>" +
                    "<binding template=\"ToastGeneric\">" +
                      "<text>{0}</text>" +
                      "<text>{1}</text>" +
                    "</binding>" +
                  "</visual>" +
                "</toast>",
                Escape(title), Escape(body));
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        // ── Start-Menu shortcut creation (COM / IShellLink) ───────────

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                         int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                                 int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            int  IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PROPERTYKEY pkey);
            void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            int  SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            int  Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY
        {
            public Guid  fmtid;
            public uint  pid;
            public PROPERTYKEY(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT : IDisposable
        {
            private ushort vt;
            private ushort r1, r2, r3;
            private IntPtr pszVal;

            public static PROPVARIANT FromString(string s)
            {
                var pv = new PROPVARIANT();
                pv.vt     = 31; // VT_LPWSTR
                pv.pszVal = Marshal.StringToCoTaskMemUni(s);
                return pv;
            }

            public void Dispose()
            {
                if (vt == 31 && pszVal != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pszVal);
                    pszVal = IntPtr.Zero;
                }
            }
        }

        private static void CreateShortcutWithAumid(string shortcutPath, string targetExe, string appId)
        {
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(targetExe);
            link.SetArguments("");

            // Set PKEY_AppUserModel_ID on the shortcut so Windows associates
            // toast notifications from this AUMID with this app.
            var store = (IPropertyStore)link;
            // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, pid=5
            var key = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var pv  = PROPVARIANT.FromString(appId);
            try   { store.SetValue(ref key, ref pv); store.Commit(); }
            finally { pv.Dispose(); }

            ((IPersistFile)link).Save(shortcutPath, true);
        }
    }
}
