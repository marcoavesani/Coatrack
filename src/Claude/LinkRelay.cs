using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Coatrack
{
    /// <summary>
    /// Forwards claude:// links (such as the sign-in callback) to the instance that is running.
    ///
    /// Windows normally hands claude:// links to Claude's own registration, which always starts the
    /// default instance. Coatrack registers itself as another claude:// handler; once the user picks it
    /// in Settings › Default apps, Windows runs "Coatrack.exe --open-url &lt;link&gt;" and Coatrack passes
    /// the link on with the running instance's --user-data-dir, so Claude delivers it to that instance.
    /// </summary>
    public static class LinkRelay
    {
        public const string ProgId = "Coatrack.ClaudeLink";
        const string CapabilitiesKey = @"Software\" + AppInfo.Name + @"\Capabilities";
        const string UserChoiceKey = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\claude\UserChoice";

        public static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        public static string Command { get { return CommandLine.Quote(ExePath) + " --open-url \"%1\""; } }

        /// <summary>Is Coatrack registered as a claude:// handler (doesn't mean it's the chosen one)?</summary>
        public static bool IsRegistered
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                {
                    return k != null && string.Equals(k.GetValue("") as string, Command, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>Has the user chosen Coatrack for claude:// links in Default apps?</summary>
        public static bool IsChosen
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UserChoiceKey))
                {
                    return k != null && string.Equals(k.GetValue("ProgId") as string, ProgId, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>Registers (or refreshes) Coatrack as a claude:// handler for this user. No admin needed.</summary>
        public static void Register()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                k.SetValue("", "Claude link (via " + AppInfo.Name + ")");
                k.SetValue("URL Protocol", "");
                using (RegistryKey icon = k.CreateSubKey("DefaultIcon")) icon.SetValue("", CommandLine.Quote(ExePath) + ",0");
                using (RegistryKey cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue("", Command);
            }
            using (RegistryKey caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
            {
                caps.SetValue("ApplicationName", AppInfo.Name);
                caps.SetValue("ApplicationDescription", "Opens Claude sign-in links in the Claude instance that's running.");
                using (RegistryKey urls = caps.CreateSubKey("URLAssociations")) urls.SetValue("claude", ProgId);
            }
            using (RegistryKey reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                reg.SetValue(AppInfo.Name, CapabilitiesKey);
            }
            NotifyAssociationsChanged();
        }

        /// <summary>Removes Coatrack's registration. Windows then goes back to Claude's own handler.</summary>
        public static void Unregister()
        {
            if (IsChosen)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(UserChoiceKey, false); } catch (Exception) { }
            }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false); } catch (Exception) { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + AppInfo.Name, false); } catch (Exception) { }
            using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            {
                if (reg != null) reg.DeleteValue(AppInfo.Name, false);
            }
            NotifyAssociationsChanged();
        }

        /// <summary>Opens Settings › Default apps on Coatrack's page, where the user picks it for CLAUDE links.</summary>
        public static void OpenDefaultAppsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppInfo.Name)) { UseShellExecute = true });
            }
            catch (Exception)
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);

        static void NotifyAssociationsChanged()
        {
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Handles "Coatrack.exe --open-url &lt;link&gt;". Sends the link to the running Claude instance,
        /// or, if Claude isn't running, starts the instance used last with the link.
        /// </summary>
        public static void Relay(string link)
        {
            var store = new ConfigStore(ConfigStore.DefaultFolder);
            Config config = store.Load();
            ClaudeApp app = ClaudeApp.Locate(config.ClaudePath);
            Log("link received (" + Describe(link) + ")");
            if (app == null)
            {
                Log("Claude not found; link dropped");
                return;
            }

            var controller = new ClaudeController();
            List<RunningClaude> running = controller.Detect(app);
            if (running.Count > 0)
            {
                RunningClaude target = running[0];
                Instance inst = config.MatchUserDataDir(target.UserDataDir);
                string codeDir = inst != null && !inst.IsDefault ? inst.ClaudeCodePath : null;
                ClaudeController.LaunchWithDataDir(app, target.UserDataDir, codeDir, link);
                Log("forwarded to running instance: " + (inst != null ? inst.Name : "unknown (" + target.UserDataDir + ")"));
                return;
            }

            Instance last = config.FindById(config.LastInstanceId) ?? config.DefaultInstance;
            ClaudeController.Launch(app, last, link);
            Log("Claude wasn't running; started " + last.Name + " with the link");
        }

        /// <summary>Logs the scheme and host only: sign-in links carry one-time codes that shouldn't be written to disk.</summary>
        static string Describe(string link)
        {
            Uri u;
            if (link != null && Uri.TryCreate(link, UriKind.Absolute, out u)) return u.Scheme + "://" + u.Host + "/…";
            return "unparseable";
        }

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.DefaultFolder);
                string path = Path.Combine(ConfigStore.DefaultFolder, "links.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
