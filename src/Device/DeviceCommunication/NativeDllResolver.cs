using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// P/Invoke helpers for DLL search order and loaded module path.
    /// Used by HI-PRO preflight to ensure ftd2xx loads from AppBase or HI-PRO, not System32/SysWOW64.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class NativeDllResolver
    {
        private const string Kernel32 = "kernel32.dll";

        [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string NewDirectory);

        [DllImport(Kernel32, SetLastError = true)]
        private static extern uint SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetModuleFileName(IntPtr hModule, System.Text.StringBuilder lpFilename, int nSize);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

        /// <summary>Sets the DLL search directory (replaces previous). Used to prefer AppBase or HI-PRO.</summary>
        public static bool SetDllDirectoryPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return SetDllDirectory(path);
        }

        /// <summary>Adds a directory to the DLL search path (Windows 8+). Returns cookie or IntPtr.Zero on failure.</summary>
        public static IntPtr AddDllDirectoryPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
            return AddDllDirectory(path);
        }

        /// <summary>Gets the full path of the loaded module by name (e.g. "ftd2xx.dll"). Returns null if not loaded.</summary>
        public static string? GetLoadedModulePath(string moduleName)
        {
            IntPtr h = GetModuleHandleW(moduleName);
            if (h == IntPtr.Zero) return null;
            var sb = new System.Text.StringBuilder(520);
            int len = GetModuleFileName(h, sb, sb.Capacity);
            if (len <= 0) return null;
            return sb.ToString();
        }

        /// <summary>Tries common names (with/without extension) and returns the first loaded path, or null.</summary>
        public static string? GetFtd2xxLoadedPath()
        {
            return GetLoadedModulePath("ftd2xx.dll")
                   ?? GetLoadedModulePath("ftd2xx");
        }

        /// <summary>Gets the full path of HiProWrapper.dll if loaded, or null.</summary>
        public static string? GetHiProWrapperLoadedPath()
        {
            return GetLoadedModulePath("HiProWrapper.dll")
                   ?? GetLoadedModulePath("HiProWrapper");
        }
    }
}
