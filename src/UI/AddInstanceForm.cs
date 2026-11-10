using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Coatrack
{
    /// <summary>Asks for a name and either a location for new folders or two existing folders.</summary>
    public class AddInstanceForm : Form
    {
        readonly Config config;
        readonly ClaudeLocations claude = ClaudeLocations.ForCurrentUser();
        readonly TextBox nameBox, locationBox, existingAppBox, existingCodeBox;
        readonly RadioButton createRadio, existingRadio;
        readonly TableLayoutPanel createPanel, existingPanel;
        readonly Label previewLabel, messages;
        readonly Button addButton;

        public Instance Result { get; private set; }

        public AddInstanceForm(Config config)
        {
            this.config = config;
            Ui.StyleForm(this, "Add Instance");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            const int W = 520;
            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(16) };
            Controls.Add(root);

            root.Controls.Add(new Label { Text = "Name", AutoSize = true, Margin = new Padding(0, 0, 0, 3) });
            nameBox = new TextBox { Width = W, Margin = new Padding(0, 0, 0, 12) };
            nameBox.TextChanged += delegate { Revalidate(); };
            root.Controls.Add(nameBox);

            createRadio = new RadioButton { Text = "Create new folders", AutoSize = true, Checked = true };
            existingRadio = new RadioButton { Text = "Use existing folders", AutoSize = true };
            createRadio.CheckedChanged += delegate { ModeChanged(); };
            existingRadio.CheckedChanged += delegate { ModeChanged(); };

            // Create new
            root.Controls.Add(createRadio);
            createPanel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(20, 0, 0, 8) };
            locationBox = new TextBox { Width = W - 20 - 100, Text = ConfigStore.DefaultInstancesFolder };
            locationBox.TextChanged += delegate { Revalidate(); };
            createPanel.Controls.Add(new Label { Text = "Location", AutoSize = true, Margin = new Padding(0, 4, 0, 3) }, 0, 0);
            createPanel.Controls.Add(locationBox, 0, 1);
            createPanel.Controls.Add(Ui.Button("Browse…", delegate { Browse(locationBox, "Choose where to put the new instance's folders"); }), 1, 1);
            previewLabel = Ui.Note("", W - 20);
            createPanel.Controls.Add(previewLabel, 0, 2);
            createPanel.SetColumnSpan(previewLabel, 2);
            root.Controls.Add(createPanel);

            // Use existing
            root.Controls.Add(existingRadio);
            existingPanel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(20, 0, 0, 8) };
            existingAppBox = new TextBox { Width = W - 20 - 100 };
            existingCodeBox = new TextBox { Width = W - 20 - 100 };
            existingAppBox.TextChanged += delegate { Revalidate(); };
            existingCodeBox.TextChanged += delegate { Revalidate(); };
            TableLayoutPanel tp = existingPanel;
            tp.Controls.Add(new Label { Text = "App data folder (--user-data-dir)", AutoSize = true, Margin = new Padding(0, 4, 0, 3) }, 0, 0);
            tp.Controls.Add(existingAppBox, 0, 1);
            tp.Controls.Add(Ui.Button("Browse…", delegate { Browse(existingAppBox, "Choose the app data folder"); }), 1, 1);
            tp.Controls.Add(new Label { Text = "Claude Code folder (CLAUDE_CONFIG_DIR), optional", AutoSize = true, Margin = new Padding(0, 8, 0, 3) }, 0, 2);
            tp.Controls.Add(existingCodeBox, 0, 3);
            tp.Controls.Add(Ui.Button("Browse…", delegate { Browse(existingCodeBox, "Choose the Claude Code folder"); }), 1, 3);
            Label codeNote = Ui.Note("Leave it empty to create a claude-code folder next to the app data folder.", W - 20);
            tp.Controls.Add(codeNote, 0, 4);
            tp.SetColumnSpan(codeNote, 2);
            root.Controls.Add(existingPanel);

            messages = new Label { AutoSize = true, MaximumSize = new Size(W, 0), Margin = new Padding(0, 4, 0, 8) };
            root.Controls.Add(messages);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            Button cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            addButton = Ui.Button("Add", delegate { Accept(); });
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(addButton);
            root.Controls.Add(buttons);
            AcceptButton = addButton;
            CancelButton = cancel;

            ModeChanged();
        }

        void ModeChanged()
        {
            createPanel.Enabled = createRadio.Checked;
            existingPanel.Enabled = existingRadio.Checked;
            Revalidate();
        }

        void Browse(TextBox box, string title)
        {
            string p = FolderPicker.Pick(this, title, string.IsNullOrEmpty(box.Text) ? ConfigStore.DefaultInstancesFolder : box.Text);
            if (p != null) box.Text = p;
        }

        /// <summary>Works out the two folders from the current choices. Either may be null if not chosen.</summary>
        void ComputePaths(out string appData, out string code)
        {
            appData = null;
            code = null;
            string name = nameBox.Text.Trim();
            if (createRadio.Checked)
            {
                string loc = PathUtil.Standardise(locationBox.Text);
                if (loc == null || name.Length == 0) return;
                try
                {
                    string folder = Path.Combine(loc, name);
                    appData = Path.Combine(folder, "app-data");
                    code = Path.Combine(folder, "claude-code");
                }
                catch (ArgumentException) { }
            }
            else
            {
                appData = PathUtil.Standardise(existingAppBox.Text);
                code = PathUtil.Standardise(existingCodeBox.Text);
                if (code == null && appData != null && string.IsNullOrWhiteSpace(existingCodeBox.Text))
                {
                    string parent = Path.GetDirectoryName(appData);
                    if (parent != null) code = Path.Combine(parent, "claude-code");
                }
            }
        }

        bool Revalidate()
        {
            string appData, code;
            ComputePaths(out appData, out code);
            string nameError = InstanceValidator.ValidateName(nameBox.Text, config.Instances, null);

            if (createRadio.Checked)
            {
                previewLabel.Text = appData != null
                    ? "Creates " + PathUtil.Abbreviate(Path.GetDirectoryName(appData)) + " with app-data and claude-code folders inside."
                    : "Enter a name and a location.";
            }

            string text = "";
            Color colour = SystemColors.GrayText;
            bool valid = nameError == null;
            if (nameError != null && nameBox.Text.Length > 0) { text = nameError; colour = Color.Firebrick; }

            if (existingRadio.Checked && string.IsNullOrWhiteSpace(existingAppBox.Text))
            {
                valid = false;
            }
            else if (appData != null || code != null)
            {
                ValidationResult r = InstanceValidator.ValidatePaths(appData, code, config.Instances, null, claude);
                if (existingRadio.Checked && appData != null && !Directory.Exists(appData))
                    r.Errors.Add("The app data folder doesn't exist.");
                if (!r.IsValid)
                {
                    valid = false;
                    if (text.Length == 0) { text = string.Join("\n", r.Errors); colour = Color.Firebrick; }
                }
                else if (r.Warnings.Count > 0 && text.Length == 0)
                {
                    text = string.Join("\n", r.Warnings);
                    colour = Color.DarkGoldenrod;
                }
            }
            else
            {
                valid = false;
            }

            messages.Text = text;
            messages.ForeColor = colour;
            addButton.Enabled = valid;
            return valid;
        }

        void Accept()
        {
            if (!Revalidate()) return;
            string appData, code;
            ComputePaths(out appData, out code);
            Result = Instance.Create(nameBox.Text, appData, code);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
