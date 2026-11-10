using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Coatrack
{
    /// <summary>The notification-area icon, its menu, and switching between instances.</summary>
    public class TrayController : ApplicationContext
    {
        public readonly ConfigStore Store = new ConfigStore(ConfigStore.DefaultFolder);
        public Config Config;
        public ClaudeApp App;
        public readonly ClaudeController Controller = new ClaudeController();

        /// <summary>Running Claude main processes, refreshed by the poll timer.</summary>
        public List<RunningClaude> Running = new List<RunningClaude>();
        public bool Switching { get; private set; }

        /// <summary>Raised on the UI thread whenever instances, settings or the running state change.</summary>
        public event EventHandler StateChanged;

        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly Control marshal;
        readonly RegisteredWaitHandle showWait;
        ManageForm manageForm;
        SettingsForm settingsForm;
        string lastIconKey;

        public TrayController(EventWaitHandle showEvent, bool startedAtLogin)
        {
            Config = Store.Load();
            App = ClaudeApp.Locate(Config.ClaudePath);

            marshal = new Control();
            marshal.CreateControl();

            menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            menu.Opening += delegate { BuildMenu(); };

            icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = AppInfo.Name };
            icon.MouseUp += OnIconMouseUp;
            icon.DoubleClick += delegate { ShowManage(); };

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
            {
                marshal.BeginInvoke(new Action(ShowManage));
            }, null, Timeout.Infinite, false);

            if (LinkRelay.IsRegistered || LinkRelay.IsChosen) TryRun(LinkRelay.Register); // keep the exe path current
            TryRun(StartAtLogin.RefreshPathIfEnabled);

            timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += delegate { Poll(); };
            timer.Start();
            Poll();

            if (!startedAtLogin && Config.Instances.Count == 1)
            {
                // First run: show the Manage window so people can add their second instance.
                marshal.BeginInvoke(new Action(ShowManage));
            }
        }

        static void TryRun(Action a)
        {
            try { a(); } catch (Exception) { }
        }

        // ---- State ----

        public void Poll()
        {
            if (Switching) return;
            try
            {
                App = ClaudeApp.Locate(Config.ClaudePath);
                List<RunningClaude> now = App == null ? new List<RunningClaude>() : Controller.Detect(App);
                bool changed = now.Count != Running.Count ||
                    now.Where((r, i) => r.MainPid != Running[i].MainPid || r.UserDataDir != Running[i].UserDataDir).Any();
                Running = now;
                UpdateIcon();
                if (changed) RaiseStateChanged();
            }
            catch (Exception) { }
        }

        public void RaiseStateChanged()
        {
            EventHandler h = StateChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>The instance a running Claude belongs to, or null if its folder matches no instance.</summary>
        public Instance InstanceFor(RunningClaude r)
        {
            return Config.MatchUserDataDir(r.UserDataDir);
        }

        public bool IsRunning(Instance inst)
        {
            return Running.Any(r => InstanceFor(r) == inst);
        }

        public void SaveConfig()
        {
            Store.Save(Config);
            RaiseStateChanged();
        }

        // ---- Icon ----

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            lastIconKey = null;
            marshal.BeginInvoke(new Action(UpdateIcon));
        }

        static bool TaskbarIsLight()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                return v is int && (int)v == 1;
            }
        }

        static string Initial(string name)
        {
            string n = (name ?? "").Trim();
            return n.Length == 0 ? "?" : char.ToUpperInvariant(n[0]).ToString();
        }

        void UpdateIcon()
        {
            string badge, tip;
            if (Switching)
            {
                badge = "…";
                tip = AppInfo.Name + " – switching…";
            }
            else if (App == null)
            {
                badge = "!";
                tip = AppInfo.Name + " – Claude not found";
            }
            else if (Running.Count == 0)
            {
                badge = null;
                tip = AppInfo.Name + " – Claude isn't running";
            }
            else
            {
                Instance inst = InstanceFor(Running[0]);
                badge = inst == null ? "?" : Initial(inst.Name);
                tip = AppInfo.Name + " – " + (inst == null ? "Unknown instance" : inst.Name);
            }
            if (tip.Length > 63) tip = tip.Substring(0, 62) + "…";
            icon.Text = tip;

            bool light = TaskbarIsLight();
            string key = (badge ?? "") + "|" + light;
            if (key == lastIconKey) return;
            lastIconKey = key;
            Icon old = icon.Icon;
            int size = SystemInformation.SmallIconSize.Width;
            icon.Icon = HatIcon.DrawTrayIcon(Math.Max(16, size), light, badge);
            if (old != null) old.Dispose();
        }

        void OnIconMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Show the same menu on left click, as most tray apps do.
            MethodInfo show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (show != null) show.Invoke(icon, null);
        }

        // ---- Menu ----

        void BuildMenu()
        {
            Poll();
            menu.Items.Clear();
            var header = new ToolStripMenuItem("Your instances") { Enabled = false };
            header.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(header);

            if (App == null)
            {
                menu.Items.Add(new ToolStripMenuItem("⚠ Claude app not found") { Enabled = false });
                menu.Items.Add(new ToolStripMenuItem("Locate Claude…", null, delegate { ShowSettings(); }));
                menu.Items.Add(new ToolStripSeparator());
            }

            foreach (Instance inst in Config.Instances)
            {
                Instance target = inst;
                var item = new ToolStripMenuItem(inst.Name.Replace("&", "&&"), null, delegate { OnInstanceClicked(target); });
                item.Checked = IsRunning(inst);
                if (inst.IsDefault) item.ShortcutKeyDisplayString = "Default instance";
                item.Enabled = App != null && !Switching;
                menu.Items.Add(item);
            }
            foreach (RunningClaude r in Running.Where(r => InstanceFor(r) == null))
            {
                menu.Items.Add(new ToolStripMenuItem("Unknown instance") { Checked = true, Enabled = false, ToolTipText = r.UserDataDir });
            }

            menu.Items.Add(new ToolStripSeparator());
            if (Config.Instances.Count > 1 && !LinkRelay.IsChosen)
            {
                menu.Items.Add(new ToolStripMenuItem("⚠ Set up sign-in links…", null, delegate { ShowSettings(); }));
            }
            menu.Items.Add(new ToolStripMenuItem("Manage Instances…", null, delegate { ShowManage(); }));
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit " + AppInfo.Name, null, delegate { ExitThread(); }));
        }

        void OnInstanceClicked(Instance inst)
        {
            OpenInstance(inst, null);
        }

        /// <summary>
        /// Opens an instance: brings it to the front if it's running, starts it if Claude isn't running,
        /// otherwise (after confirming, if that's turned on) quits Claude and reopens it as this instance.
        /// </summary>
        public void OpenInstance(Instance inst, IWin32Window owner)
        {
            if (Switching) return;
            App = ClaudeApp.Locate(Config.ClaudePath);
            if (App == null)
            {
                MessageBox.Show(owner, "Coatrack can't find the Claude app. Choose where it is in Settings.", AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Running = Controller.Detect(App);

            if (Running.Count == 0 || (Running.Count == 1 && InstanceFor(Running[0]) == inst))
            {
                LaunchAndRemember(inst);
                return;
            }

            if (Config.ConfirmBeforeSwitching)
            {
                DialogResult r = MessageBox.Show(owner,
                    "Switch to " + inst.Name + "?\n\nClaude will quit and reopen. Anything in progress in Claude will stop.",
                    AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }
            SwitchTo(inst, owner);
        }

        void LaunchAndRemember(Instance inst)
        {
            try
            {
                ClaudeController.Launch(App, inst, null);
                Config.LastInstanceId = inst.Id;
                Store.Save(Config);
            }
            catch (Exception e)
            {
                MessageBox.Show("Couldn't open Claude as " + inst.Name + ".\n\n" + e.Message, AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Poll();
        }

        void SwitchTo(Instance inst, IWin32Window owner)
        {
            Switching = true;
            UpdateIcon();
            RaiseStateChanged();
            ClaudeApp app = App;
            Task.Factory.StartNew(() => Controller.QuitAndWait(app, TimeSpan.FromSeconds(20))).ContinueWith(t =>
            {
                bool proceed = true;
                if (t.IsFaulted || t.Result == QuitResult.TimedOut)
                {
                    DialogResult r = MessageBox.Show(owner,
                        "Claude hasn't quit yet.\n\nForce quit it? Anything unsaved in Claude may be lost.",
                        AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (r == DialogResult.OK) Controller.ForceQuit(app);
                    else proceed = false;
                }
                Switching = false;
                lastIconKey = null;
                if (proceed) LaunchAndRemember(inst);
                else Poll();
                RaiseStateChanged();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---- Windows ----

        public void ShowManage()
        {
            if (manageForm == null || manageForm.IsDisposed)
            {
                manageForm = new ManageForm(this);
            }
            manageForm.Show();
            if (manageForm.WindowState == FormWindowState.Minimized) manageForm.WindowState = FormWindowState.Normal;
            manageForm.Activate();
        }

        public void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(this);
            }
            settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            showWait.Unregister(null);
            icon.Visible = false;
            icon.Dispose();
            if (manageForm != null && !manageForm.IsDisposed) manageForm.Close();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            base.ExitThreadCore();
        }
    }
}
