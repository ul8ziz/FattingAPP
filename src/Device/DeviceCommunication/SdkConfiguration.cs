using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Resolves all SDK paths relative to AppContext.BaseDirectory.
    /// Assets layout in output:
    ///   Assets/SoundDesigner/sd.config
    ///   Assets/SoundDesigner/products/*.library
    ///   Assets/SoundDesigner/products/*.param   (optional)
    /// No hardcoded external paths for library/config; HI-PRO driver path is OS-level.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class SdkConfiguration
    {
        // --- Embedded asset paths (relative to AppContext.BaseDirectory) ---

        /// <summary>Subfolder under the app output directory containing all SDK assets.</summary>
        public static string SdkAssetsSubfolder { get; set; } = Path.Combine("Assets", "SoundDesigner");

        /// <summary>Subfolder under SdkAssetsSubfolder containing *.library and *.param files.</summary>
        public static string ProductsSubfolder { get; set; } = "products";

        /// <summary>Config file name (inside SdkAssetsSubfolder).</summary>
        public static string ConfigFileName { get; set; } = "sd.config";

        // --- External dependency paths (OS-level, not part of the project) ---

        /// <summary>HI-PRO driver path (installed by Otometrics HI-PRO v4.02+).</summary>
        public static string HiProDriverPath { get; set; } = @"C:\Program Files (x86)\HI-PRO";

        /// <summary>CTK search paths (installed by SignaKlara).</summary>
        private static readonly string[] CtkSearchPaths = new[]
        {
            @"C:\Program Files (x86)\CTK",
            @"C:\Program Files\CTK",
            @"C:\Program Files (x86)\Common Files\SignaKlara\CTK",
            @"C:\Program Files\Common Files\SignaKlara\CTK",
        };

        // =========================================================================
        // Path resolution (all relative to AppContext.BaseDirectory)
        // =========================================================================

        /// <summary>Root directory for all SDK assets (absolute, from AppContext.BaseDirectory).</summary>
        public static string GetSdkAssetsPath()
        {
            return Path.Combine(AppContext.BaseDirectory, SdkAssetsSubfolder);
        }

        /// <summary>Directory containing *.library and *.param product files.</summary>
        public static string GetProductsPath()
        {
            return Path.Combine(GetSdkAssetsPath(), ProductsSubfolder);
        }

        /// <summary>Full path to sd.config inside the embedded assets.</summary>
        public static string GetConfigPath()
        {
            var path = Path.Combine(GetSdkAssetsPath(), ConfigFileName);
            if (File.Exists(path))
                Debug.WriteLine($"Config found: {path}");
            else
                Debug.WriteLine($"WARNING: Config not found at: {path}");
            return path;
        }

        /// <summary>
        /// Gets the full path to a specific library file inside the products folder.
        /// Falls back to output root for backward compatibility.
        /// </summary>
        public static string GetLibraryPath(string? libraryFileName = null)
        {
            var fileName = libraryFileName ?? "E7160SL.library";

            // Primary: Assets/SoundDesigner/products/
            var productsDir = GetProductsPath();
            var primaryPath = Path.Combine(productsDir, fileName);
            if (File.Exists(primaryPath))
            {
                Debug.WriteLine($"Library found: {primaryPath}");
                return primaryPath;
            }

            // Fallback: output root (backward compat)
            var rootPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(rootPath))
            {
                Debug.WriteLine($"Library found (root fallback): {rootPath}");
                return rootPath;
            }

            Debug.WriteLine($"WARNING: Library not found: {primaryPath}");
            return primaryPath;
        }

        /// <summary>Enumerates all *.library files from the products directory.</summary>
        public static string[] EnumerateLibraryFiles()
        {
            var productsDir = GetProductsPath();
            if (!Directory.Exists(productsDir))
            {
                Debug.WriteLine($"[SdkConfiguration] Products directory not found: {productsDir}");
                return Array.Empty<string>();
            }
            var files = Directory.GetFiles(productsDir, "*.library", SearchOption.TopDirectoryOnly);
            Debug.WriteLine($"[SdkConfiguration] Found {files.Length} library file(s) in {productsDir}");
            return files;
        }

        /// <summary>Enumerates all *.param files from the products directory.</summary>
        public static string[] EnumerateParamFiles()
        {
            var productsDir = GetProductsPath();
            if (!Directory.Exists(productsDir))
                return Array.Empty<string>();
            return Directory.GetFiles(productsDir, "*.param", SearchOption.TopDirectoryOnly);
        }

        // =========================================================================
        // CTK / HI-PRO
        // =========================================================================

        /// <summary>Finds the actual CTK installation path by checking known locations and the registry.</summary>
        public static string? FindCtkPath()
        {
            foreach (var path in CtkSearchPaths)
            {
                if (Directory.Exists(path))
                {
                    Debug.WriteLine($"CTK found at: {path}");
                    return path;
                }
            }
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\SignaKlara\CTK")
                             ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SignaKlara\CTK");
                if (key != null)
                {
                    var value = key.GetValue("")?.ToString();
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

        public static bool IsCtkInstalled() => FindCtkPath() != null;

        // =========================================================================
        // Environment setup
        // =========================================================================

        /// <summary>
        /// Validates that required native DLLs and environment are present.
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

        /// <summary>Sets the app folder as the first DLL search directory. Call at startup before any CTK/sdnet.</summary>
        public static void SetAppDllDirectoryFirst()
        {
            var appDir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(appDir) || !Directory.Exists(appDir)) return;
            if (NativeDllResolver.SetDllDirectoryPath(appDir))
                Debug.WriteLine($"SetDllDirectory(app) set at startup: {appDir}");
            else
                Debug.WriteLine($"WARNING: SetDllDirectory(app) failed: {appDir}");
        }

        /// <summary>
        /// Sets up SDK environment:
        /// 0. SetDllDirectory(HI-PRO) FIRST so ftd2xx.dll loads from HI-PRO before CTK.
        /// 1. SD_CONFIG_PATH for sd.config (from embedded assets).
        /// 2. Replace process PATH with strict order: AppBaseDir; HI-PRO; CTK; CTK\communication_modules.
        /// </summary>
        public static void SetupEnvironment()
        {
            ValidateEnvironment();

            var appDir = AppContext.BaseDirectory;

            // 0. SetDllDirectory HI-PRO first
            if (Directory.Exists(HiProDriverPath))
            {
                if (NativeDllResolver.SetDllDirectoryPath(HiProDriverPath))
                    Debug.WriteLine($"SetDllDirectory(HI-PRO) set first: {HiProDriverPath}");
                else
                    Debug.WriteLine($"WARNING: SetDllDirectory failed for HI-PRO: {HiProDriverPath}");
            }

            // 1. Set SD_CONFIG_PATH ALWAYS to the Assets copy.
            // The SDK resolves relative paths inside sd.config from the config file's directory.
            // If we point to the root copy (bin\sd.config), relative paths like "products\"
            // won't resolve to Assets\SoundDesigner\products\ — causing library load failures
            // or silent corruption. NEVER use the root copy.
            var assetsConfigPath = GetConfigPath();
            if (File.Exists(assetsConfigPath))
            {
                Environment.SetEnvironmentVariable("SD_CONFIG_PATH", assetsConfigPath);
                Debug.WriteLine($"SD_CONFIG_PATH set to: {assetsConfigPath}");
            }
            else
            {
                Debug.WriteLine($"CRITICAL: sd.config not found at expected path: {assetsConfigPath}");
            }

            // 2. Replace PATH with strict order
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
