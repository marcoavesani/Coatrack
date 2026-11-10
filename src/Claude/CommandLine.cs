using System;
using System.Collections.Generic;
using System.Text;

namespace Coatrack
{
    /// <summary>Windows command-line splitting and quoting, plus the Chromium switches Coatrack cares about.</summary>
    public static class CommandLine
    {
        /// <summary>
        /// Splits a command line like CommandLineToArgvW: the program name ends at the first space unless
        /// quoted (and has no escapes); other arguments follow the MSVC rules for backslashes and quotes.
        /// </summary>
        public static List<string> Split(string cmd)
        {
            var args = new List<string>();
            if (string.IsNullOrEmpty(cmd)) return args;
            int i = 0, n = cmd.Length;

            // Program name.
            var sb = new StringBuilder();
            if (cmd[0] == '"')
            {
                i = 1;
                while (i < n && cmd[i] != '"') sb.Append(cmd[i++]);
                if (i < n) i++; // closing quote
            }
            else
            {
                while (i < n && cmd[i] != ' ' && cmd[i] != '\t') sb.Append(cmd[i++]);
            }
            args.Add(sb.ToString());

            while (true)
            {
                while (i < n && (cmd[i] == ' ' || cmd[i] == '\t')) i++;
                if (i >= n) break;
                sb.Clear();
                bool inQuotes = false;
                while (i < n)
                {
                    char ch = cmd[i];
                    if ((ch == ' ' || ch == '\t') && !inQuotes) break;
                    if (ch == '\\')
                    {
                        int slashes = 0;
                        while (i < n && cmd[i] == '\\') { slashes++; i++; }
                        if (i < n && cmd[i] == '"')
                        {
                            sb.Append('\\', slashes / 2);
                            if (slashes % 2 == 1) { sb.Append('"'); i++; }
                            // even: the quote is handled by the next loop iteration
                        }
                        else
                        {
                            sb.Append('\\', slashes);
                        }
                        continue;
                    }
                    if (ch == '"')
                    {
                        if (inQuotes && i + 1 < n && cmd[i + 1] == '"')
                        {
                            sb.Append('"'); // "" inside quotes is a literal quote
                            i += 2;
                            continue;
                        }
                        inQuotes = !inQuotes;
                        i++;
                        continue;
                    }
                    sb.Append(ch);
                    i++;
                }
                args.Add(sb.ToString());
            }
            return args;
        }

        /// <summary>Quotes one argument so Split (and CommandLineToArgvW) gives it back unchanged.</summary>
        public static string Quote(string arg)
        {
            if (arg == null) arg = "";
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            for (int i = 0; ; i++)
            {
                int slashes = 0;
                while (i < arg.Length && arg[i] == '\\') { slashes++; i++; }
                if (i == arg.Length)
                {
                    sb.Append('\\', slashes * 2);
                    break;
                }
                if (arg[i] == '"')
                {
                    sb.Append('\\', slashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', slashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> args)
        {
            var parts = new List<string>();
            foreach (string a in args) parts.Add(Quote(a));
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Returns the value of a Chromium switch (e.g. "user-data-dir"), or null if absent.
        /// Chromium accepts --, - and / prefixes on Windows, matches names case-insensitively,
        /// stops at a bare "--" and lets later values win.
        /// </summary>
        public static string GetSwitch(IList<string> argv, string name)
        {
            string value = null;
            for (int k = 1; k < argv.Count; k++)
            {
                string a = argv[k];
                if (a == "--") break;
                string body;
                if (a.StartsWith("--")) body = a.Substring(2);
                else if (a.StartsWith("-") || a.StartsWith("/")) body = a.Substring(1);
                else continue;
                int eq = body.IndexOf('=');
                string key = eq < 0 ? body : body.Substring(0, eq);
                if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = eq < 0 ? "" : body.Substring(eq + 1);
            }
            return value;
        }

        /// <summary>The Electron main process has no --type= switch; helpers (renderer, gpu...) do.</summary>
        public static bool IsElectronMainProcess(IList<string> argv)
        {
            return GetSwitch(argv, "type") == null;
        }

        /// <summary>Finds a claude:// link among the arguments, if any.</summary>
        public static string FindClaudeLink(IList<string> argv)
        {
            for (int k = 1; k < argv.Count; k++)
                if (argv[k].StartsWith("claude:", StringComparison.OrdinalIgnoreCase)) return argv[k];
            return null;
        }
    }
}
