using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    [SupportedOSPlatform("windows")]
    public static class SdkConfiguration
    {

        // Library file name (from SDK samples Constants.cs)
        public static string LibraryName { get; set; } = "E7160SL.library";

        // Config file name
        public static string ConfigFileName { get; set; } = "sd.config";

        // HI-PRO driver path (required by SDK - see SDK Readme)
        // "Install the latest HI-PRO driver (v4.02) from Otometrics;
        //  add C:\Program Files (x86)\HI-PRO to your system path"
        public static string HiProDriverPath { get; set; } = @"C:\Program Files (x86)\HI-PRO";

        // CTK paths - checked in order of priority
        private static readonly string[] CtkSearchPaths = new[]
        {
            @"C:\Program Files (x86)\CTK",
            @"C:\Program Files\CTK",
            @"C:\Program Files (x86)\Common Files\SignaKlara\CTK",
            @"C:\Program Files\Common Files\SignaKlara\CTK",
        };

        /// <summary>
        /// Finds the actual CTK installation path by checking known locations and the registry.
        /// </summary>
        public static string? FindCtkPath()
        {
            // 1. Check known filesystem paths
            foreach (var path in CtkSearchPaths)
            {
                if (Directory.Exists(path))
                {
                    Debug.WriteLine($"CTK found at: {path}");
                    return path;
                }
            }

            // 2. Check registry for CTK installation (SignaKlara\CTK key)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\SignaKlara\CTK")
                             ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SignaKlara\CTK");
                if (key != null)
                {
                    var value = key.GetValue("")?.ToString(); // default value contains CTKManager.dll path
                    if (!string.IsNullOrEmpty(value))
                    {
                        var dir = Path.GetDirectoryName(value);
                        if (dir != null && Directory.Exists(dir))
                        {
                            Debug.WriteLine($"CTK found via registry: {dir}");
                            return dir;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WARNING: Error reading CTK registry: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Checks if CTK Runtime is installed on the system.
        /// </summary>
        public static bool IsCtkInstalled()
        {
            return FindCtkPath() != null;
        }

        /// <summary>
        /// Gets the path to the product library file.
        /// </summary>
        public static string GetLibraryPath()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(appDir, LibraryName);
            if (File.Exists(localPath))
            {
                Debug.WriteLine($"Library found: {localPath}");
                return localPath;
            }

            Debug.WriteLine($"WARNING: Library not found at: {localPath}");
            return localPath;
        }

        /// <summary>
        /// Gets the path to the sd.config file.
        /// </summary>
        public static string GetConfigPath()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(appDir, ConfigFileName);
            if (File.Exists(localPath))
            {
                Debug.WriteLine($"Config found: {localPath}");
                return localPath;
            }

            Debug.WriteLine($"WARNING: Config not found at: {localPath}");
            return localPath;
        }

        /// <summary>
        /// Validates that required native DLLs and environment are present. Call before any SDLib usage.
        /// Throws with a clear message if something is missing.
        /// </summary>
        public static void ValidateEnvironment()
        {
            var errors = new List<string>();

            if (!Directory.Exists(HiProDriverPath))
                errors.Add($"HI-PRO driver path not found: {HiProDriverPath}");

            var configPath = GetConfigPath();
            if (!File.Exists(configPath))
                errors.Add($"sd.config not found: {configPath}");

            var libraryPath = GetLibraryPath();
            if (!File.Exists(libraryPath))
                errors.Add($"Library file not found: {libraryPath}");

            var ctkPath = FindCtkPath();
            if (ctkPath == null)
                errors.Add("CTK Runtime not installed. Required for HI-PRO.");
            else
            {
                var commPath = Path.Combine(ctkPath, "communication_modules");
                if (!Directory.Exists(commPath))
                    errors.Add($"CTK communication_modules not found: {commPath}");
                else
                {
                    var hiProDll = Path.Combine(commPath, "HI-PRO.dll");
                    if (!File.Exists(hiProDll))
                        errors.Add($"HI-PRO.dll not found in CTK: {hiProDll}");
                }
            }

            if (errors.Count > 0)
                throw new InvalidOperationException("SDK environment validation failed:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// Sets the app folder as the first DLL search directory so ftd2xx.dll loads from app output (not System32).
        /// Call at app startup before any SDK/native load. Does not modify system paths.
        /// </summary>
        /// <summary>Sets the app folder as the first DLL search directory. Call at startup before any CTK/sdnet.</summary>
        public static void SetAppDllDirectoryFirst()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            if (string.IsNullOrEmpty(appDir) || !Directory.Exists(appDir)) return;
            if (NativeDllResolver.SetDllDirectoryPath(appDir))
                Debug.WriteLine($"SetDllDirectory(app) set at startup: {appDir}");
            else
                Debug.WriteLine($"WARNING: SetDllDirectory(app) failed: {appDir}");
        }

        /// <summary>
        /// Sets up SDK environment:
        /// 0. SetDllDirectory(HI-PRO) FIRST so ftd2xx.dll loads from HI-PRO before CTK.
        /// 1. SD_CONFIG_PATH for sd.config
        /// 2. Replace process PATH with strict order: AppBaseDir; HI-PRO; CTK; CTK\communication_modules (avoids ftd2xx conflicts).
        /// </summary>
        public static void SetupEnvironment()
        {
            ValidateEnvironment();

            var appDir = AppDomain.CurrentDomain.BaseDirectory ?? "";

            // 0. SetDllDirectory HI-PRO before any CTK usage so loader finds ftd2xx from HI-PRO first (switch for scan; restore after via HiproPreflight.RestoreAppDllDirectoryAfterScan)
            if (Directory.Exists(HiProDriverPath))
            {
                if (NativeDllResolver.SetDllDirectoryPath(HiProDriverPath))
                    Debug.WriteLine($"SetDllDirectory(HI-PRO) set first: {HiProDriverPath}");
                else
                    Debug.WriteLine($"WARNING: SetDllDirectory failed for HI-PRO: {HiProDriverPath}");
            }

            // 1. Set SD_CONFIG_PATH
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                Environment.SetEnvironmentVariable("SD_CONFIG_PATH", configPath);
                Debug.WriteLine($"SD_CONFIG_PATH set to: {configPath}");
            }
            else
            {
                Debug.WriteLine($"WARNING: sd.config not found at: {configPath}");
            }

            // 2. Replace PATH with strict order: AppBaseDir, HI-PRO, CTK, CTK\communication_modules (no other folders to avoid ftd2xx conflicts)
            var ctkPath = FindCtkPath();
            var parts = new List<string> { appDir.TrimEnd('\\', '/'), HiProDriverPath };
            if (!string.IsNullOrEmpty(ctkPath) && Directory.Exists(ctkPath))
            {
                parts.Add(ctkPath);
                var commModules = Path.Combine(ctkPath, "communication_modules");
                if (Directory.Exists(commModules))
                    parts.Add(commModules);
            }
            var newPath = string.Join(";", parts);
            Environment.SetEnvironmentVariable("PATH", newPath);
            Debug.WriteLine($"PATH set (HI-PRO before CTK): {newPath}");
            ScanDiagnostics.WriteLine($"PATH (process): {newPath}");
        }

    }
}
