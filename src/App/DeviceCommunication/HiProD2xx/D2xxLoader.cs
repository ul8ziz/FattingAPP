using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FTD2XX_NET;

namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Ensures ftd2xx.dll can be loaded and FTD2XX_NET is usable. Call at app startup.
    /// </summary>
    public static class D2xxLoader
    {
        private const string Ftd2xxDll = "ftd2xx.dll";
        private const string Ftd2xxNetDll = "FTD2XX_NET.dll";
        private const string HiProPath = @"C:\Program Files (x86)\HI-PRO";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static string? _resolvedFtd2xxPath;
        private static bool _ensureCalled;

        /// <summary>
        /// Directory where ftd2xx.dll was found (app base or HI-PRO). Null if not found.
        /// </summary>
        public static string? ResolvedFtd2xxPath => _resolvedFtd2xxPath;

        /// <summary>
        /// Configures DLL search order: app base first, then HI-PRO. Verifies ftd2xx.dll exists.
        /// Call once at startup before any FTD2XX_NET usage.
        /// </summary>
        public static void ConfigureDllSearchPaths(Action<string>? log = null)
        {
            log ??= _ => { };
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            appBase = Path.GetFullPath(appBase).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // SetDllDirectory replaces the previous; we set app base first so local copy wins
            if (!string.IsNullOrEmpty(appBase) && Directory.Exists(appBase))
            {
                if (SetDllDirectory(appBase))
                    log($"[D2XX] SetDllDirectory(app): {appBase}");
                else
                    log($"[D2XX] SetDllDirectory(app) failed: {appBase}");
            }

            if (Directory.Exists(HiProPath))
            {
                if (SetDllDirectory(HiProPath))
                    log($"[D2XX] SetDllDirectory(HI-PRO): {HiProPath}");
            }

            // Resolve where ftd2xx.dll actually is (may already be loaded from app base)
            _resolvedFtd2xxPath = ResolveFtd2xxPath(appBase, log);
            if (_resolvedFtd2xxPath != null)
                log($"[D2XX] ftd2xx.dll resolved: {_resolvedFtd2xxPath}");
            else
                log("[D2XX] ftd2xx.dll not found in app base or HI-PRO");
        }

        /// <summary>
        /// Attempts to load FTD2XX_NET and create an FTDI instance. Returns true if successful.
        /// Call ConfigureDllSearchPaths first.
        /// </summary>
        public static bool EnsureD2xxLoaded(Action<string>? log = null)
        {
            log ??= _ => { };
            if (_ensureCalled)
            {
                log("[D2XX] EnsureD2xxLoaded already called; skipping.");
                return _resolvedFtd2xxPath != null;
            }
            _ensureCalled = true;

            if (_resolvedFtd2xxPath == null)
                ConfigureDllSearchPaths(log);

            try
            {
                var ftdi = new FTDI();
                // Touch a simple method to force native load
                uint count = 0;
                var status = ftdi.GetNumberOfDevices(ref count);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    log($"[D2XX] GetNumberOfDevices returned {status}");
                    return false;
                }
                log($"[D2XX] FTD2XX_NET loaded; GetNumberOfDevices={count}");
                return true;
            }
            catch (Exception ex)
            {
                log($"[D2XX] EnsureD2xxLoaded failed: {ex.Message}");
                Debug.WriteLine($"[D2XX] EnsureD2xxLoaded: {ex}");
                return false;
            }
        }

        private static string? ResolveFtd2xxPath(string appBase, Action<string> log)
        {
            var appDll = Path.Combine(appBase, Ftd2xxDll);
            if (File.Exists(appDll))
                return appDll;
            var hiproDll = Path.Combine(HiProPath, Ftd2xxDll);
            if (File.Exists(hiproDll))
                return hiproDll;
            return null;
        }

        /// <summary>
        /// Returns a short diagnostics summary for logs: process arch, ftd2xx path, loaded path, D2XX device count.
        /// Use before wired discovery so logs show which ftd2xx is used and that x86 matches.
        /// </summary>
        public static string GetDiagnosticsSummary()
        {
            var arch = IntPtr.Size == 8 ? "x64" : "x86";
            var resolved = ResolvedFtd2xxPath ?? "(not resolved)";
            var loaded = Ul8ziz.FittingApp.Device.DeviceCommunication.NativeDllResolver.GetFtd2xxLoadedPath() ?? "(not loaded yet)";
            uint deviceCount = 0;
            try
            {
                var ftdi = new FTDI();
                ftdi.GetNumberOfDevices(ref deviceCount);
            }
            catch (Exception)
            {
                deviceCount = 0;
            }
            return $"[D2XX] Process arch={arch} | ftd2xx resolved={resolved} | loaded={loaded} | GetNumberOfDevices={deviceCount}";
        }
    }
}
