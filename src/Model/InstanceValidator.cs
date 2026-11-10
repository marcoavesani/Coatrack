using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Coatrack
{
    /// <summary>Claude's own data locations, which instances must never use.</summary>
    public class ClaudeLocations
    {
        /// <summary>Folders the Claude app itself uses for its default data (app data side).</summary>
        public List<string> AppData = new List<string>();
        /// <summary>Claude Code's default folder (~\.claude).</summary>
        public List<string> ClaudeCode = new List<string>();
        /// <summary>Folders that OneDrive or similar may sync. Instances there get a warning, not an error.</summary>
        public List<string> Synced = new List<string>();

        public static ClaudeLocations ForCurrentUser()
        {
            var l = new ClaudeLocations();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            AddStd(l.AppData, Path.Combine(roaming, "Claude"));
            AddStd(l.AppData, Path.Combine(roaming, "Claude-3p"));
            AddStd(l.AppData, Path.Combine(local, "Claude"));
            AddStd(l.AppData, Path.Combine(local, "Claude-Data"));
            AddStd(l.AppData, Path.Combine(local, "Claude-3p"));
            AddStd(l.AppData, Path.Combine(local, "AnthropicClaude"));
            try
            {
                string packages = Path.Combine(local, "Packages");
                if (Directory.Exists(packages))
                    foreach (string d in Directory.GetDirectories(packages, "Claude_*")) AddStd(l.AppData, d);
            }
            catch (Exception) { }

            AddStd(l.ClaudeCode, Path.Combine(home, ".claude"));
            AddStd(l.ClaudeCode, Path.Combine(home, ".claude.json"));

            foreach (string v in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
                AddStd(l.Synced, Environment.GetEnvironmentVariable(v));
            AddStd(l.Synced, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddStd(l.Synced, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            return l;
        }

        static void AddStd(List<string> list, string p)
        {
            if (string.IsNullOrEmpty(p)) return;
            string s = PathUtil.Standardise(p);
            if (s != null && !list.Any(x => PathUtil.AreEqual(x, s))) list.Add(s);
        }

        public IEnumerable<string> All { get { return AppData.Concat(ClaudeCode); } }
    }

    public class ValidationResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool IsValid { get { return Errors.Count == 0; } }
    }

    public static class InstanceValidator
    {
        static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Returns an error message, or null if the name is fine.</summary>
        public static string ValidateName(string name, IEnumerable<Instance> existing, string ignoreId)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return "Enter a name.";
            if (n.Length > 60) return "Use a name of 60 characters or fewer.";
            if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n == "." || n == ".." || n.EndsWith("."))
                return "A name can't contain \\ / : * ? \" < > | or end with a full stop.";
            if (ReservedNames.Contains(n.ToUpperInvariant()))
                return "Windows reserves that name. Choose another.";
            foreach (Instance i in existing)
            {
                if (i.Id == ignoreId) continue;
                if (string.Equals(i.Name.Trim(), n, StringComparison.OrdinalIgnoreCase))
                    return "There's already an instance called " + i.Name + ".";
            }
            return null;
        }

        /// <summary>
        /// Checks an instance's two folders. Both are standardised first. claudeCode may be null
        /// only if the caller will derive it (it is validated once derived).
        /// </summary>
        public static ValidationResult ValidatePaths(string appData, string claudeCode,
            IEnumerable<Instance> existing, string ignoreId, ClaudeLocations claude)
        {
            var r = new ValidationResult();
            string a = PathUtil.Standardise(appData);
            string c = PathUtil.Standardise(claudeCode);

            if (a == null) { r.Errors.Add("Choose a valid app data folder."); }
            if (c == null) { r.Errors.Add("Choose a valid Claude Code folder."); }
            if (!r.IsValid) return r;

            CheckOne(r, a, "app data folder", claude);
            CheckOne(r, c, "Claude Code folder", claude);

            if (PathUtil.Overlap(a, c))
                r.Errors.Add("The app data folder and the Claude Code folder must be separate folders, not one inside the other.");

            foreach (Instance i in existing)
            {
                if (i.Id == ignoreId || i.IsDefault) continue;
                foreach (string other in new[] { i.AppDataPath, i.ClaudeCodePath })
                {
                    if (other == null) continue;
                    if (PathUtil.Overlap(a, other))
                        r.Errors.Add("The app data folder overlaps a folder used by " + i.Name + ".");
                    if (PathUtil.Overlap(c, other))
                        r.Errors.Add("The Claude Code folder overlaps a folder used by " + i.Name + ".");
                }
            }

            foreach (string s in claude.Synced)
            {
                if (PathUtil.IsSameOrInside(a, s) || PathUtil.IsSameOrInside(c, s))
                {
                    r.Warnings.Add("This is inside a folder that OneDrive may sync (" + PathUtil.Abbreviate(s) +
                        "). Syncing Claude's data can corrupt it; a local folder is safer.");
                    break;
                }
            }
            return r;
        }

        static void CheckOne(ValidationResult r, string p, string label, ClaudeLocations claude)
        {
            if (PathUtil.IsDriveRoot(p))
            {
                r.Errors.Add("The " + label + " can't be the root of a drive.");
                return;
            }
            foreach (string d in claude.All)
            {
                if (PathUtil.IsSameOrInside(p, d))
                {
                    r.Errors.Add("The " + label + " is inside Claude's own data (" + PathUtil.Abbreviate(d) +
                        "). That belongs to the default instance.");
                    return;
                }
                if (PathUtil.IsSameOrInside(d, p))
                {
                    r.Errors.Add("The " + label + " contains Claude's own data (" + PathUtil.Abbreviate(d) + ").");
                    return;
                }
            }
        }
    }
}
