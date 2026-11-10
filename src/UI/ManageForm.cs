using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Coatrack
{
    /// <summary>Lists instances on the left and shows the selected one on the right.</summary>
    public class ManageForm : Form
    {
        readonly TrayController tray;
        readonly ListBox list;
        readonly Button addButton, removeButton;
        readonly TextBox nameBox, appDataBox, codeBox;
        readonly Button renameButton, openButton;
        readonly Label statusLabel, nameError, defaultNote;
        bool loading;

        public ManageForm(TrayController tray)
        {
            this.tray = tray;
            Ui.StyleForm(this, "Manage Instances – " + AppInfo.Name);
            MinimumSize = new Size(720, 480);
            Size = new Size(800, 490);
            Padding = new Padding(12);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            // ---- Left: list and +/- ----
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            list.ItemHeight = (int)(Font.Height * 2.6);
            list.DrawItem += DrawListItem;
            list.SelectedIndexChanged += delegate { ShowSelected(); };
            left.Controls.Add(list, 0, 0);

            var listButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            addButton = new Button { Text = "+", Width = 34, Height = 28 };
            addButton.Click += delegate { AddInstance(); };
            removeButton = new Button { Text = "−", Width = 34, Height = 28 };
            removeButton.Click += delegate { RemoveSelected(); };
            var tips = new ToolTip();
            tips.SetToolTip(addButton, "Add an instance");
            tips.SetToolTip(removeButton, "Remove the selected instance");
            listButtons.Controls.Add(addButton);
            listButtons.Controls.Add(removeButton);
            left.Controls.Add(listButtons, 0, 1);
            root.Controls.Add(left, 0, 0);

            // ---- Right: details ----
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label h1 = Ui.Heading("Name");
            right.Controls.Add(h1, 0, 0);
            right.SetColumnSpan(h1, 2);
            nameBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 3) };
            nameBox.TextChanged += delegate { OnNameChanged(); };
            nameBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && renameButton.Enabled) { Rename(); e.SuppressKeyPress = true; } };
            renameButton = Ui.Button("Rename", delegate { Rename(); });
            right.Controls.Add(nameBox, 0, 1);
            right.Controls.Add(renameButton, 1, 1);
            nameError = new Label { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0) };
            right.Controls.Add(nameError, 0, 2);
            right.SetColumnSpan(nameError, 2);

            Label h2 = Ui.Heading("App data folder");
            right.Controls.Add(h2, 0, 3);
            right.SetColumnSpan(h2, 2);
            appDataBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(0, 3, 6, 3) };
            right.Controls.Add(appDataBox, 0, 4);
            right.Controls.Add(Ui.Button("Open folder", delegate { Ui.OpenFolder(appDataBox.Text); }), 1, 4);
            Label n2 = Ui.Note("Sign-in, chats, settings and MCP servers. Passed to Claude as --user-data-dir.", 460);
            right.Controls.Add(n2, 0, 5);
            right.SetColumnSpan(n2, 2);

            Label h3 = Ui.Heading("Claude Code folder");
            right.Controls.Add(h3, 0, 6);
            right.SetColumnSpan(h3, 2);
            codeBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(0, 3, 6, 3) };
            right.Controls.Add(codeBox, 0, 7);
            right.Controls.Add(Ui.Button("Open folder", delegate { Ui.OpenFolder(codeBox.Text); }), 1, 7);
            Label n3 = Ui.Note("Settings and sessions for the Code tab. Passed to Claude as CLAUDE_CONFIG_DIR.", 460);
            right.Controls.Add(n3, 0, 8);
            right.SetColumnSpan(n3, 2);

            defaultNote = Ui.Note("This is Claude's normal data. Opening Claude from the Start menu, taskbar or at sign-in always opens this instance.", 460);
            right.Controls.Add(defaultNote, 0, 9);
            right.SetColumnSpan(defaultNote, 2);

            var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 16, 0, 0) };
            openButton = Ui.Button("Open", delegate { OpenSelected(); });
            statusLabel = new Label { AutoSize = true, Margin = new Padding(10, 8, 0, 0), ForeColor = SystemColors.GrayText };
            bottom.Controls.Add(openButton);
            bottom.Controls.Add(statusLabel);
            right.Controls.Add(bottom, 0, 10);
            right.SetColumnSpan(bottom, 2);

            root.Controls.Add(right, 1, 0);

            tray.StateChanged += OnTrayStateChanged;
            FormClosed += delegate { tray.StateChanged -= OnTrayStateChanged; };

            Reload(tray.Config.Instances.FirstOrDefault());
        }

        void OnTrayStateChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            Reload(Selected);
        }

        Instance Selected { get { return list.SelectedItem as Instance; } }

        void Reload(Instance select)
        {
            loading = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Instance i in tray.Config.Instances) list.Items.Add(i);
            int idx = select == null ? -1 : tray.Config.Instances.FindIndex(i => i.Id == select.Id);
            list.SelectedIndex = idx >= 0 ? idx : (list.Items.Count > 0 ? 0 : -1);
            list.EndUpdate();
            loading = false;
            ShowSelected();
        }

        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var inst = (Instance)list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color main = selected ? SystemColors.HighlightText : SystemColors.ControlText;
            Color sub = selected ? SystemColors.HighlightText : SystemColors.GrayText;
            string subtitle = inst.IsDefault ? "Default instance" : "";
            if (tray.IsRunning(inst)) subtitle = subtitle.Length > 0 ? subtitle + " · Running" : "Running";
            int pad = 8;
            var r1 = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + 4, e.Bounds.Width - 2 * pad, Font.Height + 2);
            var r2 = new Rectangle(e.Bounds.X + pad, r1.Bottom, e.Bounds.Width - 2 * pad, Font.Height + 2);
            using (var bold = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, inst.Name, bold, r1, main, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (subtitle.Length > 0)
                TextRenderer.DrawText(e.Graphics, subtitle, Font, r2, sub, TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        void ShowSelected()
        {
            if (loading) return;
            Instance inst = Selected;
            bool has = inst != null;
            nameBox.Enabled = has;
            removeButton.Enabled = has && !inst.IsDefault && !tray.IsRunning(inst) && !tray.Switching;
            openButton.Enabled = has && !tray.Switching && tray.App != null;
            if (!has) return;

            nameBox.Text = inst.Name;
            nameError.Text = "";
            renameButton.Enabled = false;
            if (inst.IsDefault)
            {
                appDataBox.Text = DefaultLocations.AppData;
                codeBox.Text = DefaultLocations.ClaudeCode;
            }
            else
            {
                appDataBox.Text = inst.AppDataPath;
                codeBox.Text = inst.ClaudeCodePath;
            }
            defaultNote.Visible = inst.IsDefault;

            bool running = tray.IsRunning(inst);
            if (tray.Switching)
            {
                openButton.Text = "Switching…";
                statusLabel.Text = "";
            }
            else if (running)
            {
                openButton.Text = "Show Claude";
                statusLabel.Text = "Running now";
            }
            else if (tray.Running.Count > 0)
            {
                openButton.Text = "Switch to this instance";
                statusLabel.Text = "Claude will quit and reopen as " + inst.Name + ".";
            }
            else
            {
                openButton.Text = "Open";
                statusLabel.Text = tray.App == null ? "Claude app not found. Choose it in Settings." : "Not running";
            }
        }

        void OnNameChanged()
        {
            Instance inst = Selected;
            if (inst == null || loading) return;
            string error = InstanceValidator.ValidateName(nameBox.Text, tray.Config.Instances, inst.Id);
            bool changed = nameBox.Text.Trim() != inst.Name;
            nameError.Text = changed ? (error ?? "") : "";
            renameButton.Enabled = changed && error == null;
        }

        void Rename()
        {
            Instance inst = Selected;
            if (inst == null) return;
            if (InstanceValidator.ValidateName(nameBox.Text, tray.Config.Instances, inst.Id) != null) return;
            inst.Name = nameBox.Text.Trim();
            tray.SaveConfig();
            tray.Poll();
        }

        void OpenSelected()
        {
            Instance inst = Selected;
            if (inst != null) tray.OpenInstance(inst, this);
        }

        void AddInstance()
        {
            using (var f = new AddInstanceForm(tray.Config))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) return;
                tray.Config.Instances.Add(f.Result);
                try
                {
                    Directory.CreateDirectory(f.Result.AppDataPath);
                    Directory.CreateDirectory(f.Result.ClaudeCodePath);
                }
                catch (Exception e)
                {
                    MessageBox.Show(this, "The instance was added, but its folders couldn't be created yet.\n\n" + e.Message,
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                tray.SaveConfig();
                Reload(f.Result);
                AfterAdding(f.Result);
            }
        }

        void AfterAdding(Instance inst)
        {
            if (!LinkRelay.IsChosen)
            {
                bool dummy;
                bool setUp = Ui.Confirm(this, "Sign-in links",
                    "Before you sign in to " + inst.Name + ", let " + AppInfo.Name + " handle Claude's sign-in links.\n\n" +
                    "Claude signs in through your browser, which then opens a claude:// link. Windows sends those links " +
                    "to the default instance unless you choose " + AppInfo.Name + " for them. " + AppInfo.Name +
                    " then passes each link to the instance that's running.\n\n" +
                    "Settings will open: choose " + AppInfo.Name + " for CLAUDE links. You only need to do this once.",
                    null, "Set up now", out dummy);
                if (setUp) SettingsForm.SetUpLinks(this);
            }

            bool unused;
            string message = "Open " + inst.Name + " now?\n\nThe first time, Claude shows its sign-in screen. Sign in with the account you want for this instance.";
            if (tray.Running.Count > 0) message += "\n\nClaude will quit and reopen as " + inst.Name + ".";
            if (Ui.Confirm(this, "Open " + inst.Name, message, null, "Open", out unused))
            {
                bool confirm = tray.Config.ConfirmBeforeSwitching;
                tray.Config.ConfirmBeforeSwitching = false; // they've just confirmed
                try { tray.OpenInstance(inst, this); }
                finally { tray.Config.ConfirmBeforeSwitching = confirm; }
            }
        }

        void RemoveSelected()
        {
            Instance inst = Selected;
            if (inst == null || inst.IsDefault || tray.IsRunning(inst)) return;
            bool recycle;
            bool ok = Ui.Confirm(this, "Remove " + inst.Name,
                "Remove " + inst.Name + " from " + AppInfo.Name + "?\n\nIts sign-in, chats and settings stay in its folders unless you also move them to the Recycle Bin.",
                "Also move its folders to the Recycle Bin", "Remove", out recycle);
            if (!ok) return;

            tray.Config.Instances.Remove(inst);
            if (tray.Config.LastInstanceId == inst.Id) tray.Config.LastInstanceId = null;
            tray.SaveConfig();

            if (recycle)
            {
                foreach (string dir in new[] { inst.AppDataPath, inst.ClaudeCodePath })
                {
                    try
                    {
                        if (Directory.Exists(dir))
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(this, "Couldn't move " + dir + " to the Recycle Bin.\n\n" + e.Message, AppInfo.Name,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                // Tidy up the "<Name>" folder Coatrack made, if it's now empty.
                try
                {
                    string parent = Path.GetDirectoryName(inst.AppDataPath);
                    if (parent != null && PathUtil.AreEqual(parent, Path.GetDirectoryName(inst.ClaudeCodePath)) &&
                        Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                        Directory.Delete(parent);
                }
                catch (Exception) { }
            }
            Reload(tray.Config.DefaultInstance);
        }
    }

    /// <summary>Where the default instance keeps its data, for display.</summary>
    public static class DefaultLocations
    {
        public static string AppData
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string moved = Path.Combine(local, "Claude-Data");
                try
                {
                    if (Directory.Exists(moved))
                    {
                        string d = Directory.GetDirectories(moved, "claude_*").FirstOrDefault();
                        if (d != null) return d;
                    }
                }
                catch (Exception) { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            }
        }

        public static string ClaudeCode
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"); }
        }
    }
}
