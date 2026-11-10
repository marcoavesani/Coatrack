using System;
using System.Drawing;
using System.Windows.Forms;

namespace Coatrack
{
    public class SettingsForm : Form
    {
        readonly TrayController tray;
        readonly CheckBox startAtLogin, confirmSwitch;
        readonly Label linkStatus, claudeLabel;
        readonly Button linkSetUp, linkRemove, claudeAuto;

        public SettingsForm(TrayController tray)
        {
            this.tray = tray;
            Ui.StyleForm(this, "Settings – " + AppInfo.Name);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            const int W = 500;
            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(16, 8, 16, 16) };
            Controls.Add(root);

            root.Controls.Add(Ui.Heading("General"));
            startAtLogin = new CheckBox { Text = "Start " + AppInfo.Name + " when I sign in to Windows", AutoSize = true };
            startAtLogin.Checked = StartAtLogin.Enabled;
            startAtLogin.CheckedChanged += delegate
            {
                try { StartAtLogin.Enabled = startAtLogin.Checked; }
                catch (Exception e) { MessageBox.Show(this, e.Message, AppInfo.Name); }
            };
            root.Controls.Add(startAtLogin);
            confirmSwitch = new CheckBox { Text = "Confirm before switching instances", AutoSize = true, Checked = tray.Config.ConfirmBeforeSwitching };
            confirmSwitch.CheckedChanged += delegate
            {
                tray.Config.ConfirmBeforeSwitching = confirmSwitch.Checked;
                tray.SaveConfig();
            };
            root.Controls.Add(confirmSwitch);

            root.Controls.Add(Ui.Heading("Sign-in links"));
            root.Controls.Add(Ui.Note(
                "Claude signs in through your browser, which then opens a claude:// link. Windows normally sends those links " +
                "to the default instance. Choose " + AppInfo.Name + " for CLAUDE links in Default apps and it passes each link " +
                "to the instance that's running (or the one you used last).", W));
            linkStatus = new Label { AutoSize = true, MaximumSize = new Size(W, 0), Margin = new Padding(0, 4, 0, 6) };
            root.Controls.Add(linkStatus);
            var linkButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            linkSetUp = Ui.Button("Set up…", delegate { SetUpLinks(this); RefreshState(); });
            linkRemove = Ui.Button("Stop handling links", delegate
            {
                LinkRelay.Unregister();
                RefreshState();
            });
            linkButtons.Controls.Add(linkSetUp);
            linkButtons.Controls.Add(linkRemove);
            root.Controls.Add(linkButtons);

            root.Controls.Add(Ui.Heading("Claude app"));
            claudeLabel = new Label { AutoSize = true, MaximumSize = new Size(W, 0), Margin = new Padding(0, 2, 0, 6) };
            root.Controls.Add(claudeLabel);
            var claudeButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            claudeButtons.Controls.Add(Ui.Button("Choose…", delegate { ChooseClaude(); }));
            claudeAuto = Ui.Button("Find automatically", delegate
            {
                tray.Config.ClaudePath = null;
                tray.SaveConfig();
                tray.Poll();
                RefreshState();
            });
            claudeButtons.Controls.Add(claudeAuto);
            root.Controls.Add(claudeButtons);

            root.Controls.Add(Ui.Heading("About"));
            var about = new LinkLabel
            {
                Text = AppInfo.Name + " " + AppInfo.Version + " · " + AppInfo.RepositoryUrl.Replace("https://", ""),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            };
            about.LinkArea = new LinkArea(about.Text.IndexOf('·') + 2, AppInfo.RepositoryUrl.Length - "https://".Length);
            about.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true }); }
                catch (Exception) { }
            };
            root.Controls.Add(about);
            root.Controls.Add(Ui.Note("Unofficial; not made by or affiliated with Anthropic. Settings are saved in " +
                PathUtil.Abbreviate(new ConfigStore(ConfigStore.DefaultFolder).FilePath) + ".", W));

            Activated += delegate { RefreshState(); };
            RefreshState();
        }

        void RefreshState()
        {
            bool chosen = LinkRelay.IsChosen;
            bool registered = LinkRelay.IsRegistered;
            if (chosen)
            {
                linkStatus.Text = "✓ " + AppInfo.Name + " handles claude:// links.";
                linkStatus.ForeColor = Color.ForestGreen;
            }
            else if (registered)
            {
                linkStatus.Text = "Not chosen yet. In Settings › Apps › Default apps › " + AppInfo.Name + ", set CLAUDE to " + AppInfo.Name + ".";
                linkStatus.ForeColor = Color.DarkGoldenrod;
            }
            else
            {
                linkStatus.Text = "Not set up. Sign-in links open the default instance.";
                linkStatus.ForeColor = Color.DarkGoldenrod;
            }
            linkSetUp.Text = registered && !chosen ? "Open Default apps…" : "Set up…";
            linkSetUp.Visible = !chosen;
            linkRemove.Visible = registered || chosen;

            ClaudeApp app = ClaudeApp.Locate(tray.Config.ClaudePath);
            if (app == null)
            {
                claudeLabel.Text = string.IsNullOrEmpty(tray.Config.ClaudePath)
                    ? "⚠ Claude isn't installed, or " + AppInfo.Name + " can't find it."
                    : "⚠ Not found: " + tray.Config.ClaudePath;
                claudeLabel.ForeColor = Color.Firebrick;
            }
            else
            {
                claudeLabel.Text = (string.IsNullOrEmpty(tray.Config.ClaudePath) ? "Found automatically: " : "Using: ") + app.Description;
                claudeLabel.ForeColor = SystemColors.ControlText;
            }
            claudeAuto.Visible = !string.IsNullOrEmpty(tray.Config.ClaudePath);
        }

        void ChooseClaude()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Choose Claude",
                Filter = "Claude (claude.exe; claude-desktop.exe)|claude.exe;claude-desktop.exe|Programs (*.exe)|*.exe",
                CheckFileExists = true
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                tray.Config.ClaudePath = d.FileName;
                tray.SaveConfig();
                tray.Poll();
                RefreshState();
            }
        }

        /// <summary>Registers Coatrack for claude:// links and opens Default apps so the user can choose it.</summary>
        public static void SetUpLinks(IWin32Window owner)
        {
            try
            {
                LinkRelay.Register();
            }
            catch (Exception e)
            {
                MessageBox.Show(owner, "Couldn't register " + AppInfo.Name + " for claude:// links.\n\n" + e.Message, AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (LinkRelay.IsChosen) return;
            MessageBox.Show(owner,
                "Settings will open on " + AppInfo.Name + "'s Default apps page.\n\n" +
                "Find CLAUDE (URL:claude), click the app shown under it and choose " + AppInfo.Name + ".\n\n" +
                "If the page doesn't list " + AppInfo.Name + ", search Default apps for “claude” under “Set a default for a link type”.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            LinkRelay.OpenDefaultAppsSettings();
        }
    }
}
