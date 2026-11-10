using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Coatrack")]
[assembly: AssemblyDescription("Switch the Claude desktop app between separate instances on Windows.")]
[assembly: AssemblyProduct("Coatrack")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Coatrack
{
    public static class AppInfo
    {
        public const string Name = "Coatrack";
        public const string Version = "1.0";
    }

    static class Program
    {
        const string MutexName = @"Local\Coatrack.Tray";
        public const string ShowEventName = @"Local\Coatrack.Show";

        [STAThread]
        static int Main(string[] args)
        {
            // "Coatrack.exe --open-url <link>" is run by Windows for claude:// links. It does its job and exits.
            int urlIndex = Array.IndexOf(args, "--open-url");
            if (urlIndex >= 0)
            {
                if (urlIndex + 1 < args.Length)
                {
                    try { LinkRelay.Relay(args[urlIndex + 1]); }
                    catch (Exception e) { LinkRelay.Log("relay failed: " + e.GetType().Name + ": " + e.Message); return 1; }
                }
                return 0;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                if (!created)
                {
                    // Already running: ask that copy to open its Manage window.
                    showEvent.Set();
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => ReportCrash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);
                Application.Run(new TrayController(showEvent, args.Contains("--startup")));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static void ReportCrash(Exception e)
        {
            string text = e == null ? "Unknown error" : e.ToString();
            try
            {
                System.IO.Directory.CreateDirectory(ConfigStore.DefaultFolder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(ConfigStore.DefaultFolder, "errors.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
            MessageBox.Show("Something went wrong in " + AppInfo.Name + ":\n\n" + (e == null ? text : e.Message) +
                "\n\nDetails are in errors.log in " + ConfigStore.DefaultFolder + ".", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>"Start Coatrack when I sign in to Windows", using the per-user Run key.</summary>
    public static class StartAtLogin
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static string Command { get { return CommandLine.Quote(Application.ExecutablePath) + " --startup"; } }

        public static bool Enabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return k != null && k.GetValue(AppInfo.Name) != null;
                }
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(AppInfo.Name, Command);
                    else k.DeleteValue(AppInfo.Name, false);
                }
            }
        }

        /// <summary>If start-at-login is on but points at an old copy of the exe, point it at this one.</summary>
        public static void RefreshPathIfEnabled()
        {
            if (Enabled) Enabled = true;
        }
    }
}
