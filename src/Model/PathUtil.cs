using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Coatrack
{
    /// <summary>Path standardisation and comparison. All comparisons are case-insensitive, as on NTFS.</summary>
    public static class PathUtil
    {
        /// <summary>
        /// Expands %VARS% and ~, makes the path absolute, resolves junctions/symlinks and 8.3 short names
        /// (for the part of the path that exists) and removes trailing separators. Returns null for
        /// empty or invalid input.
        /// </summary>
        public static string Standardise(string path)
        {
            if (path == null) return null;
            string p = path.Trim().Trim('"').Trim();
            if (p.Length == 0) return null;

            p = Environment.ExpandEnvironmentVariables(p);
            if (p == "~" || p.StartsWith("~\\") || p.StartsWith("~/"))
                p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = p.Replace('/', '\\');

            try
            {
                if (!Path.IsPathRooted(p)) return null;
                p = Path.GetFullPath(p);
            }
            catch (Exception)
            {
                return null;
            }

            p = ResolveExistingPart(p);
            return TrimTrailingSeparators(p);
        }

        /// <summary>Lexical-only version of Standardise (no file-system access). Used by tests.</summary>
        public static string StandardiseLexical(string path)
        {
            if (path == null) return null;
            string p = path.Trim().Trim('"').Trim();
            if (p.Length == 0) return null;
            p = p.Replace('/', '\\');
            try
            {
                if (!Path.IsPathRooted(p)) return null;
                p = Path.GetFullPath(p);
            }
            catch (Exception)
            {
                return null;
            }
            return TrimTrailingSeparators(p);
        }

        public static string TrimTrailingSeparators(string p)
        {
            if (p == null) return null;
            // Keep "C:\" and "\\server\share" intact.
            while (p.Length > 3 && p.EndsWith("\\")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        public static bool AreEqual(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(TrimTrailingSeparators(a), TrimTrailingSeparators(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True if child is parent or lies anywhere inside it. Both should already be standardised.</summary>
        public static bool IsSameOrInside(string child, string parent)
        {
            if (child == null || parent == null) return false;
            string c = TrimTrailingSeparators(child);
            string p = TrimTrailingSeparators(parent);
            if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = p.EndsWith("\\") ? p : p + "\\";
            return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True if either path is the same as, or inside, the other.</summary>
        public static bool Overlap(string a, string b)
        {
            return IsSameOrInside(a, b) || IsSameOrInside(b, a);
        }

        /// <summary>Is the path a drive root such as C:\ ?</summary>
        public static bool IsDriveRoot(string p)
        {
            if (p == null) return false;
            string t = TrimTrailingSeparators(p);
            return t.Length <= 3 && t.Length >= 2 && t[1] == ':';
        }

        /// <summary>Shortens a path for display: replaces the user profile with ~.</summary>
        public static string Abbreviate(string p)
        {
            if (p == null) return "";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (IsSameOrInside(p, home)) return "~" + p.Substring(home.Length);
            return p;
        }

        // ---- Resolving junctions, symlinks and short names ----

        static string ResolveExistingPart(string full)
        {
            // Find the deepest existing ancestor, resolve it, then append the rest.
            string existing = full;
            var tail = new List<string>();
            try
            {
                while (existing != null && !Directory.Exists(existing) && !File.Exists(existing))
                {
                    string parent = Path.GetDirectoryName(existing);
                    if (parent == null) return full;
                    tail.Insert(0, Path.GetFileName(existing));
                    existing = parent;
                }
                if (existing == null) return full;
                string resolved = FinalPath(existing);
                if (resolved == null) return full;
                foreach (string part in tail) resolved = Path.Combine(resolved, part);
                return resolved;
            }
            catch (Exception)
            {
                return full;
            }
        }

        const uint FILE_READ_ATTRIBUTES = 0x80;
        const uint FILE_SHARE_ALL = 0x7;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);

        static string FinalPath(string path)
        {
            using (SafeFileHandle h = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var sb = new StringBuilder(1024);
                uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n == 0 || n >= sb.Capacity) return null;
                string s = sb.ToString();
                if (s.StartsWith(@"\\?\UNC\")) return @"\\" + s.Substring(8);
                if (s.StartsWith(@"\\?\")) return s.Substring(4);
                return s;
            }
        }
    }
}
