using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace Coatrack
{
    public class ProcInfo
    {
        public int Pid;
        public int ParentPid;
        public string ExeName;
    }

    /// <summary>Thin wrappers around the Win32 process APIs Coatrack needs.</summary>
    public static class NativeProcess
    {
        const uint TH32CS_SNAPPROCESS = 0x2;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const int ProcessCommandLineInformation = 60;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder buf, ref int size);
        [DllImport("ntdll.dll")]
        static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int retLen);

        /// <summary>All processes: pid, parent pid and exe file name.</summary>
        public static List<ProcInfo> Snapshot()
        {
            var list = new List<ProcInfo>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                var e = new PROCESSENTRY32W();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
                if (!Process32FirstW(snap, ref e)) return list;
                do
                {
                    list.Add(new ProcInfo { Pid = (int)e.th32ProcessID, ParentPid = (int)e.th32ParentProcessID, ExeName = e.szExeFile });
                } while (Process32NextW(snap, ref e));
            }
            finally
            {
                CloseHandle(snap);
            }
            return list;
        }

        /// <summary>Full path of the process's executable, or null.</summary>
        public static string GetImagePath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        /// <summary>The process's command line, or null. Uses NtQueryInformationProcess, falling back to WMI.</summary>
        public static string GetCommandLine(int pid)
        {
            string s = GetCommandLineNative(pid);
            if (s != null) return s;
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject o in results)
                    {
                        object v = o["CommandLine"];
                        if (v != null) return v.ToString();
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        static string GetCommandLineNative(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            IntPtr buf = IntPtr.Zero;
            try
            {
                int len;
                NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out len);
                if (len <= 0 || len > 1 << 20) return null;
                buf = Marshal.AllocHGlobal(len);
                if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, len, out len) != 0) return null;
                // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; }
                int byteLength = (ushort)Marshal.ReadInt16(buf);
                IntPtr str = Marshal.ReadIntPtr(buf, IntPtr.Size);
                if (str == IntPtr.Zero) return null;
                return Marshal.PtrToStringUni(str, byteLength / 2);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                CloseHandle(h);
            }
        }

        public static bool IsAlive(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | 0x00100000 /* SYNCHRONIZE */, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                return WaitForSingleObject(h, 0) == 0x102; // WAIT_TIMEOUT means still running
            }
            finally
            {
                CloseHandle(h);
            }
        }

        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr h, uint ms);

        // ---- Windows of a process ----

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int pid);

        public static List<IntPtr> TopLevelWindows(int pid)
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr l)
            {
                int owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if (owner == pid) list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        const uint WM_ENDSESSION = 0x0016;
        const uint SMTO_ABORTIFHUNG = 0x2;

        /// <summary>
        /// Tells the process's windows that the Windows session is ending. Claude handles this by
        /// quitting properly (the same path as Quit in its tray menu). Returns the number of windows told.
        /// </summary>
        public static int SendEndSession(int pid)
        {
            int count = 0;
            foreach (IntPtr hwnd in TopLevelWindows(pid))
            {
                IntPtr result;
                SendMessageTimeoutW(hwnd, WM_ENDSESSION, new IntPtr(1), IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result);
                count++;
            }
            return count;
        }
    }
}
