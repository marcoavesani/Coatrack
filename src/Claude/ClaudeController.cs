using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace Coatrack
{
    /// <summary>Where the Claude desktop app is and how to start it.</summary>
    public class ClaudeApp
    {
        /// <summary>What Coatrack runs: the app execution alias for the MSIX build, or Claude's exe.</summary>
        public string LaunchPath;
        /// <summary>Folder whose processes count as Claude (for a custom path). May be null.</summary>
        public string InstallDir;
        public bool IsPackaged;
        public string Description;

        public static string AliasPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WindowsApps\claude-desktop.exe");
            }
        }

        public static string SquirrelPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"AnthropicClaude\claude.exe");
            }
        }

        /// <summary>Finds Claude. Returns null if it isn't installed (or the custom path is missing).</summary>
        public static ClaudeApp Locate(string customPath)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                if (!File.Exists(customPath)) return null;
                bool alias = Path.GetFileName(customPath).Equals("claude-desktop.exe", StringComparison.OrdinalIgnoreCase);
                return new ClaudeApp
                {
                    LaunchPath = customPath,
                    InstallDir = alias ? null : Path.GetDirectoryName(customPath),
                    IsPackaged = alias,
                    Description = customPath
                };
            }
            if (File.Exists(AliasPath))
                return new ClaudeApp { LaunchPath = AliasPath, IsPackaged = true, Description = "Claude (Microsoft Store / MSIX)" };
            if (File.Exists(SquirrelPath))
                return new ClaudeApp { LaunchPath = SquirrelPath, InstallDir = Path.GetDirectoryName(SquirrelPath), Description = SquirrelPath };
            return null;
        }

        /// <summary>Is this executable part of the Claude desktop app (not the Claude Code CLI)?</summary>
        public static bool IsClaudeAppImage(string imagePath, string installDir)
        {
            if (string.IsNullOrEmpty(imagePath)) return false;
            string file = Path.GetFileName(imagePath).ToLowerInvariant();
            if (file != "claude.exe" && file != "claude-desktop.exe") return false;
            string lower = imagePath.ToLowerInvariant();
            if (lower.Contains(@"\claude-code\")) return false; // Claude Code's own binaries, inside the data folder
            if (file == "claude-desktop.exe") return true;
            if (lower.Contains(@"\windowsapps\claude_")) return true;
            if (lower.Contains(@"\anthropicclaude\")) return true;
            if (installDir != null && PathUtil.IsSameOrInside(imagePath, installDir)) return true;
            return false;
        }
    }

    /// <summary>A running Claude main process and the data folder it was started with.</summary>
    public class RunningClaude
    {
        public int MainPid;
        /// <summary>The --user-data-dir it was launched with, or null for the default instance.</summary>
        public string UserDataDir;
    }

    public enum QuitResult { Quit, TimedOut }

    /// <summary>Finds, detects, launches and quits Claude.</summary>
    public class ClaudeController
    {
        readonly Dictionary<int, string> commandLineCache = new Dictionary<int, string>();
        readonly object gate = new object();

        /// <summary>Pids of every Claude desktop process (main and helpers).</summary>
        public List<int> AppProcessIds(ClaudeApp app)
        {
            var result = new List<int>();
            foreach (ProcInfo p in NativeProcess.Snapshot())
            {
                string name = (p.ExeName ?? "").ToLowerInvariant();
                if (name != "claude.exe" && name != "claude-desktop.exe") continue;
                string image = NativeProcess.GetImagePath(p.Pid);
                if (ClaudeApp.IsClaudeAppImage(image, app == null ? null : app.InstallDir)) result.Add(p.Pid);
            }
            return result;
        }

        /// <summary>Running Claude main processes (normally zero or one).</summary>
        public List<RunningClaude> Detect(ClaudeApp app)
        {
            var mains = new List<RunningClaude>();
            List<int> pids = AppProcessIds(app);
            lock (gate)
            {
                foreach (int stale in commandLineCache.Keys.Where(k => !pids.Contains(k)).ToList())
                    commandLineCache.Remove(stale);
                foreach (int pid in pids)
                {
                    string cmd;
                    if (!commandLineCache.TryGetValue(pid, out cmd))
                    {
                        cmd = NativeProcess.GetCommandLine(pid);
                        if (cmd == null) continue; // couldn't read it; try again next time
                        commandLineCache[pid] = cmd;
                    }
                    List<string> argv = CommandLine.Split(cmd);
                    if (!CommandLine.IsElectronMainProcess(argv)) continue;
                    string dir = CommandLine.GetSwitch(argv, "user-data-dir");
                    mains.Add(new RunningClaude { MainPid = pid, UserDataDir = string.IsNullOrEmpty(dir) ? null : dir });
                }
            }
            return mains;
        }

        /// <summary>
        /// Starts Claude as the given instance. If that instance is already running, Claude passes the
        /// arguments to it and brings its window to the front, so this also works for "show" and for links.
        /// </summary>
        public static void Launch(ClaudeApp app, Instance instance, string link)
        {
            var args = new List<string>();
            if (!instance.IsDefault)
            {
                Directory.CreateDirectory(instance.AppDataPath);
                Directory.CreateDirectory(instance.ClaudeCodePath);
                args.Add("--user-data-dir=" + instance.AppDataPath);
            }
            if (!string.IsNullOrEmpty(link)) args.Add(link);
            LaunchRaw(app, args, instance.IsDefault ? null : instance.ClaudeCodePath);
        }

        /// <summary>Launches with an explicit --user-data-dir (possibly one no instance knows about).</summary>
        public static void LaunchWithDataDir(ClaudeApp app, string userDataDir, string claudeCodeDir, string link)
        {
            var args = new List<string>();
            if (!string.IsNullOrEmpty(userDataDir)) args.Add("--user-data-dir=" + userDataDir);
            if (!string.IsNullOrEmpty(link)) args.Add(link);
            LaunchRaw(app, args, claudeCodeDir);
        }

        static void LaunchRaw(ClaudeApp app, List<string> args, string claudeConfigDir)
        {
            var psi = new ProcessStartInfo(app.LaunchPath, CommandLine.Join(args))
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            CleanEnvironment(psi.EnvironmentVariables);
            if (claudeConfigDir != null) psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = claudeConfigDir;
            NativeProcess.AllowSetForegroundWindow(-1); // ASFW_ANY: let Claude bring its window to the front
            using (Process.Start(psi)) { }
        }

        /// <summary>
        /// Drops Claude/Anthropic variables Coatrack itself may have inherited (for example when started from
        /// a Claude Code terminal), keeping any the user set permanently in their Windows environment.
        /// CLAUDE_CONFIG_DIR is always removed; Launch sets it again for non-default instances.
        /// </summary>
        public static void CleanEnvironment(System.Collections.Specialized.StringDictionary env)
        {
            var persistent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EnvironmentVariableTarget t in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                try
                {
                    foreach (DictionaryEntry e in Environment.GetEnvironmentVariables(t)) persistent.Add((string)e.Key);
                }
                catch (Exception) { }
            }
            var remove = new List<string>();
            foreach (string key in env.Keys)
            {
                string k = key.ToUpperInvariant();
                if (k == "CLAUDE_CONFIG_DIR" || k == "CLAUDE_USER_DATA_DIR") { remove.Add(key); continue; }
                if ((k.StartsWith("CLAUDE") || k.StartsWith("ANTHROPIC_")) && !persistent.Contains(key)) remove.Add(key);
            }
            foreach (string k in remove) env.Remove(k);
        }

        /// <summary>
        /// Asks Claude to quit and waits until every Claude desktop process (and anything the main
        /// process started) has gone, or the timeout passes.
        /// </summary>
        public QuitResult QuitAndWait(ClaudeApp app, TimeSpan timeout)
        {
            var watch = new HashSet<int>(AppProcessIds(app));
            List<RunningClaude> mains = Detect(app);
            List<ProcInfo> snap = NativeProcess.Snapshot();
            foreach (RunningClaude m in mains) AddDescendants(snap, m.MainPid, watch);

            foreach (RunningClaude m in mains) NativeProcess.SendEndSession(m.MainPid);

            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!watch.Any(NativeProcess.IsAlive) && AppProcessIds(app).Count == 0) return QuitResult.Quit;
                Thread.Sleep(250);
            }
            return QuitResult.TimedOut;
        }

        /// <summary>Ends every Claude desktop process and its children immediately.</summary>
        public void ForceQuit(ClaudeApp app)
        {
            var targets = new HashSet<int>(AppProcessIds(app));
            List<ProcInfo> snap = NativeProcess.Snapshot();
            foreach (int pid in targets.ToList()) AddDescendants(snap, pid, targets);
            foreach (int pid in targets)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid)) p.Kill();
                }
                catch (Exception) { }
            }
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && targets.Any(NativeProcess.IsAlive)) Thread.Sleep(100);
        }

        static void AddDescendants(List<ProcInfo> snap, int root, HashSet<int> into)
        {
            var queue = new Queue<int>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                foreach (ProcInfo c in snap)
                {
                    if (c.ParentPid == p && c.Pid != p && into.Add(c.Pid)) queue.Enqueue(c.Pid);
                }
            }
        }
    }
}
