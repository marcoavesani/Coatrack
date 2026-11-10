using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Coatrack
{
    /// <summary>Small helpers so every window looks the same.</summary>
    public static class Ui
    {
        static Icon appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch (Exception) { appIcon = SystemIcons.Application; }
                }
                return appIcon;
            }
        }

        public static void StyleForm(Form f, string title)
        {
            f.Text = title;
            f.Font = SystemFonts.MessageBoxFont;
            f.AutoScaleMode = AutoScaleMode.Font;
            f.Icon = AppIcon;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ShowInTaskbar = true;
        }

        public static Label Heading(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 3)
            };
        }

        public static Label Note(string text, int maxWidth)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(maxWidth, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 2, 0, 4)
            };
        }

        public static Button Button(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0), Padding = new Padding(6, 1, 6, 1) };
            if (click != null) b.Click += click;
            return b;
        }

        public static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("explorer.exe", CommandLine.Quote(path)) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                MessageBox.Show("Couldn't open the folder.\n\n" + e.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>A message with an optional checkbox. Returns true for OK; checkboxValue says if it was ticked.</summary>
        public static bool Confirm(IWin32Window owner, string title, string message, string checkboxText, string okText, out bool checkboxValue)
        {
            using (var f = new Form())
            {
                StyleForm(f, title);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                f.AutoSize = true;
                f.AutoSizeMode = AutoSizeMode.GrowAndShrink;

                var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(16), Dock = DockStyle.Fill };
                root.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 12) });
                CheckBox cb = null;
                if (checkboxText != null)
                {
                    cb = new CheckBox { Text = checkboxText, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
                    root.Controls.Add(cb);
                }
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
                Button cancel = Button("Cancel", null);
                cancel.DialogResult = DialogResult.Cancel;
                Button ok = Button(okText, null);
                ok.DialogResult = DialogResult.OK;
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                root.Controls.Add(buttons);
                f.Controls.Add(root);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                bool result = f.ShowDialog(owner) == DialogResult.OK;
                checkboxValue = cb != null && cb.Checked;
                return result;
            }
        }
    }

    /// <summary>The modern Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS).</summary>
    public static class FolderPicker
    {
        public static string Pick(IWin32Window owner, string title, string startFolder)
        {
            try
            {
                var dialog = (IFileDialog)new FileOpenDialogCom();
                try
                {
                    uint opts;
                    dialog.GetOptions(out opts);
                    dialog.SetOptions(opts | 0x20 /* PICKFOLDERS */ | 0x40 /* FORCEFILESYSTEM */ | 0x800 /* PATHMUSTEXIST */);
                    dialog.SetTitle(title);
                    string start = startFolder;
                    while (!string.IsNullOrEmpty(start) && !Directory.Exists(start)) start = Path.GetDirectoryName(start);
                    if (!string.IsNullOrEmpty(start))
                    {
                        IShellItem folder;
                        Guid iid = typeof(IShellItem).GUID;
                        if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out folder) == 0) dialog.SetFolder(folder);
                    }
                    int hr = dialog.Show(owner == null ? IntPtr.Zero : owner.Handle);
                    if (hr != 0) return null; // cancelled
                    IShellItem result;
                    dialog.GetResult(out result);
                    string path;
                    result.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out path);
                    return path;
                }
                finally
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
            catch (Exception)
            {
                using (var fb = new FolderBrowserDialog { Description = title, SelectedPath = startFolder ?? "" })
                {
                    return fb.ShowDialog(owner) == DialogResult.OK ? fb.SelectedPath : null;
                }
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attrs);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
