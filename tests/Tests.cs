using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Coatrack.Tests
{
    /// <summary>Unit tests for the pure logic. A tiny runner keeps the build dependency-free.</summary>
    static class TestRunner
    {
        static int passed, failed;

        static int Main()
        {
            Run("PathUtil standardises lexically", PathLexical);
            Run("PathUtil compares containment", PathContainment);
            Run("PathUtil resolves short names and env vars", PathResolves);
            Run("CommandLine splits like Windows", SplitTests);
            Run("CommandLine quoting round-trips", QuoteRoundTrip);
            Run("CommandLine reads Chromium switches", SwitchTests);
            Run("Claude processes are recognised", ImageTests);
            Run("Names are validated", NameTests);
            Run("Paths are validated", PathValidationTests);
            Run("Running folder matches instances", MatchTests);
            Run("Config saves and loads", StoreRoundTrip);
            Run("Environment is cleaned for launch", EnvTests);

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("  ok    " + name);
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("  FAIL  " + name + "\n        " + e.Message);
            }
        }

        static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(what + ": expected [" + expected + "] but got [" + actual + "]");
        }

        static void True(bool cond, string what)
        {
            if (!cond) throw new Exception(what);
        }

        // ---- PathUtil ----

        static void PathLexical()
        {
            Eq(@"C:\a\b", PathUtil.StandardiseLexical(@"C:\a\b\"), "trailing slash");
            Eq(@"C:\a\b", PathUtil.StandardiseLexical(@"C:/a/b//"), "forward slashes");
            Eq(@"C:\a\c", PathUtil.StandardiseLexical(@"C:\a\b\..\c"), "dot-dot");
            Eq(@"C:\", PathUtil.StandardiseLexical(@"C:\"), "root kept");
            Eq(@"C:\x y", PathUtil.StandardiseLexical("  \"C:\\x y\\\"  "), "quotes and spaces");
            Eq<string>(null, PathUtil.StandardiseLexical("relative\\path"), "relative rejected");
            Eq<string>(null, PathUtil.StandardiseLexical("   "), "blank rejected");
        }

        static void PathContainment()
        {
            True(PathUtil.IsSameOrInside(@"C:\a\b", @"C:\a"), "child inside parent");
            True(PathUtil.IsSameOrInside(@"c:\A\B", @"C:\a\"), "case-insensitive");
            True(PathUtil.IsSameOrInside(@"C:\a", @"C:\a"), "same");
            True(!PathUtil.IsSameOrInside(@"C:\ab", @"C:\a"), "sibling with shared prefix");
            True(!PathUtil.IsSameOrInside(@"C:\a", @"C:\a\b"), "parent not inside child");
            True(PathUtil.IsSameOrInside(@"C:\x", @"C:\"), "inside drive root");
            True(PathUtil.Overlap(@"C:\a", @"C:\a\b") && PathUtil.Overlap(@"C:\a\b", @"C:\a"), "overlap both ways");
            True(PathUtil.IsDriveRoot(@"D:\") && PathUtil.IsDriveRoot("D:") && !PathUtil.IsDriveRoot(@"D:\x"), "drive roots");
        }

        static void PathResolves()
        {
            string temp = Path.GetTempPath();
            string std = PathUtil.Standardise(temp);
            True(std != null && Directory.Exists(std), "temp resolves to an existing folder");
            True(!std.Contains("~"), "8.3 short names are expanded: " + std);
            string viaEnv = PathUtil.Standardise(@"%TEMP%\does-not-exist-123\x\");
            Eq(Path.Combine(std, @"does-not-exist-123\x"), viaEnv, "env var + missing tail");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Eq(PathUtil.Standardise(home + @"\foo"), PathUtil.Standardise(@"~\foo"), "tilde");
        }

        // ---- CommandLine ----

        static void SplitTests()
        {
            List<string> a = CommandLine.Split("\"C:\\Program Files\\Claude\\claude.exe\" --type=renderer --user-data-dir=\"C:\\Users\\Me\\My Data\" /prefetch:1");
            Eq(4, a.Count, "count");
            Eq(@"C:\Program Files\Claude\claude.exe", a[0], "program");
            Eq("--type=renderer", a[1], "switch");
            Eq(@"--user-data-dir=C:\Users\Me\My Data", a[2], "quoted value in the middle of an argument");
            Eq("/prefetch:1", a[3], "slash switch");

            a = CommandLine.Split(@"C:\x\claude.exe a\\\""b c\\""d");
            Eq(@"a\""b", a[1], "odd backslashes before quote");
            Eq(@"c\d", a[2], "even backslashes before quote");

            a = CommandLine.Split("prog \"\" x");
            Eq(3, a.Count, "empty quoted argument");
            Eq("", a[1], "empty quoted argument value");

            a = CommandLine.Split("\"C:\\a b\\claude.exe\"");
            Eq(1, a.Count, "program only");
        }

        static void QuoteRoundTrip()
        {
            string[] samples =
            {
                "plain", "with space", @"C:\trailing\", @"C:\with space\", "quote\"inside", @"back\\slash\""quote", "", "claude://claude.ai/login?code=a&state=b",
                @"--user-data-dir=C:\Users\Jane Doe\AppData\Local\Coatrack\Instances\Work\app-data"
            };
            string cmd = "\"C:\\p\\claude.exe\" " + CommandLine.Join(samples);
            List<string> back = CommandLine.Split(cmd);
            Eq(samples.Length + 1, back.Count, "count");
            for (int i = 0; i < samples.Length; i++) Eq(samples[i], back[i + 1], "sample " + i);
        }

        static void SwitchTests()
        {
            var argv = new List<string> { "claude.exe", "--user-data-dir=C:\\a", "--Type=gpu", "-user-data-dir=C:\\b" };
            Eq(@"C:\b", CommandLine.GetSwitch(argv, "user-data-dir"), "later value wins, single dash");
            Eq("gpu", CommandLine.GetSwitch(argv, "type"), "case-insensitive name");
            True(!CommandLine.IsElectronMainProcess(argv), "helper has --type");
            argv = new List<string> { "claude.exe", "--", "--user-data-dir=C:\\a" };
            Eq<string>(null, CommandLine.GetSwitch(argv, "user-data-dir"), "stops at --");
            argv = new List<string> { "claude.exe", "claude://x", "--user-data-dirx=1" };
            Eq<string>(null, CommandLine.GetSwitch(argv, "user-data-dir"), "no prefix match");
            True(CommandLine.IsElectronMainProcess(argv), "main process");
            Eq("claude://x", CommandLine.FindClaudeLink(argv), "link found");
        }

        static void ImageTests()
        {
            True(ClaudeApp.IsClaudeAppImage(@"C:\Program Files\WindowsApps\Claude_4.23118.1.0_x64__pzs8sxrjxfjjc\app\claude.exe", null), "MSIX app");
            True(ClaudeApp.IsClaudeAppImage(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\claude-desktop.exe", null), "alias");
            True(ClaudeApp.IsClaudeAppImage(@"C:\Users\me\AppData\Local\AnthropicClaude\app-1.0.0\claude.exe", null), "Squirrel app");
            True(!ClaudeApp.IsClaudeAppImage(@"C:\Users\me\AppData\Local\Claude-Data\claude_x\claude-code\2.1.3\abc\claude.exe", null), "Claude Code in data folder");
            True(!ClaudeApp.IsClaudeAppImage(@"C:\Users\me\.local\bin\claude.exe", null), "Claude Code CLI");
            True(ClaudeApp.IsClaudeAppImage(@"D:\Apps\Claude\claude.exe", @"D:\Apps\Claude"), "custom install dir");
            True(!ClaudeApp.IsClaudeAppImage(@"C:\Windows\notepad.exe", null), "other exe");
        }

        // ---- Validation ----

        static List<Instance> SampleInstances()
        {
            Instance def = Instance.NewDefault();
            var work = new Instance { Id = "w", Name = "Work", AppDataPath = @"C:\Data\Work\app-data", ClaudeCodePath = @"C:\Data\Work\claude-code" };
            return new List<Instance> { def, work };
        }

        static ClaudeLocations FakeClaude()
        {
            var l = new ClaudeLocations();
            l.AppData.Add(@"C:\Users\me\AppData\Roaming\Claude");
            l.AppData.Add(@"C:\Users\me\AppData\Local\Claude-Data");
            l.ClaudeCode.Add(@"C:\Users\me\.claude");
            l.Synced.Add(@"C:\Users\me\OneDrive");
            return l;
        }

        static void NameTests()
        {
            List<Instance> list = SampleInstances();
            True(InstanceValidator.ValidateName("Personal", list, null) == null, "fine name");
            True(InstanceValidator.ValidateName("  ", list, null) != null, "empty");
            True(InstanceValidator.ValidateName("work", list, null) != null, "duplicate, case-insensitive");
            True(InstanceValidator.ValidateName("Work", list, "w") == null, "renaming to itself");
            True(InstanceValidator.ValidateName("a/b", list, null) != null, "invalid character");
            True(InstanceValidator.ValidateName("con", list, null) != null, "reserved name");
            True(InstanceValidator.ValidateName("name.", list, null) != null, "trailing dot");
            True(InstanceValidator.ValidateName("default", list, null) != null, "clashes with Default");
        }

        static void PathValidationTests()
        {
            List<Instance> list = SampleInstances();
            ClaudeLocations c = FakeClaude();
            ValidationResult r;

            r = InstanceValidator.ValidatePaths(@"C:\Data\Personal\app-data", @"C:\Data\Personal\claude-code", list, null, c);
            True(r.IsValid && r.Warnings.Count == 0, "good paths: " + string.Join("; ", r.Errors));

            r = InstanceValidator.ValidatePaths(@"C:\Users\me\AppData\Roaming\Claude\x", @"C:\Data\P\code", list, null, c);
            True(!r.IsValid, "inside Claude's app data");

            r = InstanceValidator.ValidatePaths(@"C:\Data\P\app", @"C:\Users\me\.claude", list, null, c);
            True(!r.IsValid, "Claude Code default folder");

            r = InstanceValidator.ValidatePaths(@"C:\Users", @"C:\Data\P\code", list, null, c);
            True(!r.IsValid, "contains Claude's data");

            r = InstanceValidator.ValidatePaths(@"C:\Data\Work\app-data", @"C:\Data\P\code", list, null, c);
            True(!r.IsValid, "shared with Work");
            r = InstanceValidator.ValidatePaths(@"C:\Data\Work\app-data", @"C:\Data\Work\claude-code", list, "w", c);
            True(r.IsValid, "an instance may keep its own paths: " + string.Join("; ", r.Errors));

            r = InstanceValidator.ValidatePaths(@"C:\Data\Work", @"C:\Data\P\code", list, null, c);
            True(!r.IsValid, "contains Work's folder");

            r = InstanceValidator.ValidatePaths(@"C:\Data\Q", @"C:\Data\Q\code", list, null, c);
            True(!r.IsValid, "one folder inside the other");

            r = InstanceValidator.ValidatePaths(@"D:\", @"C:\Data\Q\code", list, null, c);
            True(!r.IsValid, "drive root");

            r = InstanceValidator.ValidatePaths(@"C:\Users\me\OneDrive\Claude\app", @"C:\Users\me\OneDrive\Claude\code", list, null, c);
            True(r.IsValid && r.Warnings.Count == 1, "OneDrive warns but doesn't block");

            r = InstanceValidator.ValidatePaths(null, @"C:\x", list, null, c);
            True(!r.IsValid, "missing path");
        }

        static void MatchTests()
        {
            var config = new Config();
            config.Instances.AddRange(SampleInstances());
            config.Normalise();
            Eq("default", config.MatchUserDataDir(null).Id, "no switch is default");
            Eq("default", config.MatchUserDataDir("").Id, "empty switch is default");
            Eq("w", config.MatchUserDataDir(@"c:\data\work\APP-DATA\").Id, "case and trailing slash");
            Eq<Instance>(null, config.MatchUserDataDir(@"C:\Elsewhere"), "unknown");
        }

        static void StoreRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "coatrack-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ConfigStore(dir);
                Config c = store.Load();
                Eq(1, c.Instances.Count, "fresh config has the default instance");
                True(c.ConfirmBeforeSwitching, "default setting");
                c.Instances.Add(Instance.Create("Personal", @"C:\P\app-data", @"C:\P\claude-code"));
                c.Instances[0].Name = "Work laptop \"main\"";
                c.ConfirmBeforeSwitching = false;
                c.LastInstanceId = c.Instances[1].Id;
                store.Save(c);
                store.Save(c); // second save goes through File.Replace

                Config back = store.Load();
                Eq(2, back.Instances.Count, "count");
                Eq("Work laptop \"main\"", back.Instances[0].Name, "default renamed");
                True(back.Instances[0].IsDefault, "default first");
                Eq(@"C:\P\app-data", back.Instances[1].AppDataPath, "path");
                Eq(false, back.ConfirmBeforeSwitching, "setting kept");
                Eq(c.Instances[1].Id, back.LastInstanceId, "last instance");

                File.WriteAllText(store.FilePath, "{\"version\":1,\"instances\":[{\"id\":\"x\",\"name\":\"X\",\"appDataPath\":\"C:\\\\X\",\"claudeCodePath\":\"C:\\\\Y\"}]}");
                Config partial = store.Load();
                True(partial.ConfirmBeforeSwitching, "missing settings get defaults");
                True(partial.Instances[0].IsDefault && partial.Instances.Count == 2, "default instance restored");

                File.WriteAllText(store.FilePath, "not json");
                Config broken = store.Load();
                Eq(1, broken.Instances.Count, "unreadable config starts fresh");
                True(File.Exists(store.FilePath + ".unreadable"), "unreadable config kept aside");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        static void EnvTests()
        {
            var env = new System.Collections.Specialized.StringDictionary();
            env["PATH"] = "x";
            env["CLAUDE_CONFIG_DIR"] = "y";
            env["CLAUDECODE"] = "1";
            env["CLAUDE_CODE_ENTRYPOINT"] = "z";
            env["ANTHROPIC_BASE_URL_COATRACK_TEST"] = "z";
            ClaudeController.CleanEnvironment(env);
            True(env.ContainsKey("PATH"), "keeps unrelated variables");
            True(!env.ContainsKey("CLAUDE_CONFIG_DIR"), "drops CLAUDE_CONFIG_DIR");
            True(!env.ContainsKey("CLAUDECODE") && !env.ContainsKey("CLAUDE_CODE_ENTRYPOINT"), "drops inherited Claude Code variables");
            True(!env.ContainsKey("ANTHROPIC_BASE_URL_COATRACK_TEST"), "drops inherited Anthropic variables");
        }
    }
}
