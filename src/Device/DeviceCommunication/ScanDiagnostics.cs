using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Writes scan and environment diagnostics to Console and to log.txt in AppBaseDir. Flushes after each line so logs are unavoidable.
    /// </summary>
    public static class ScanDiagnostics
    {
        private static readonly object _lock = new object();
        private static string? _logFilePath;
        private static StreamWriter? _logWriter;

        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                    _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "log.txt");
                return _logFilePath;
            }
        }

        /// <summary>Flush after each line so logs appear no matter what. Console + file with AutoFlush.</summary>
        public static void WriteLine(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            lock (_lock)
            {
                Console.WriteLine(message);
                Console.Out.Flush();
                try
                {
                    if (_logWriter == null)
                    {
                        _logWriter = new StreamWriter(LogFilePath, append: true) { AutoFlush = true };
                    }
                    _logWriter.WriteLine(message);
                    _logWriter.Flush();
                }
                catch (Exception ex)
                {
                    try { Console.WriteLine("ScanDiagnostics.Write failed: " + ex.Message); Console.Out.Flush(); } catch { }
                }
            }
        }

        /// <summary>Execute func and return result; on exception return "exception: " + ex.Message (so logs are never empty).</summary>
        public static string SafeGet(Func<string> func)
        {
            try
            {
                return func() ?? "";
            }
            catch (Exception ex)
            {
                return "exception: " + (ex.Message ?? ex.ToString());
            }
        }

        public static void WriteStartupAndScanPreamble()
        {
            var arch = IntPtr.Size == 8 ? "x64" : "x86";
            WriteLine("========== Scan diagnostics ==========");
            WriteLine($"Environment.Version: {Environment.Version}");
            WriteLine($"RuntimeInformation.FrameworkDescription: {RuntimeInformation.FrameworkDescription}");
            WriteLine($"AppContext.TargetFrameworkName: {AppContext.TargetFrameworkName}");
            WriteLine($"Process architecture: {arch}");
            WriteLine($"Process path / AppBaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
            WriteLine($"SD_CONFIG_PATH: {Environment.GetEnvironmentVariable("SD_CONFIG_PATH") ?? "(not set)"}");
            var sdConfig = SdkConfiguration.GetConfigPath();
            WriteLine($"SD_CONFIG exists: {File.Exists(sdConfig)}");
            WriteLine($"HI-PRO path: {SdkConfiguration.HiProDriverPath}");
            WriteLine($"HI-PRO exists: {Directory.Exists(SdkConfiguration.HiProDriverPath)}");
            var ctkPath = SdkConfiguration.FindCtkPath();
            WriteLine($"CTK path: {ctkPath ?? "(not found)"}");
            WriteLine($"CTK exists: {ctkPath != null && Directory.Exists(ctkPath)}");
            try
            {
                var ports = System.IO.Ports.SerialPort.GetPortNames();
                WriteLine($"COM ports (SerialPort.GetPortNames): [{string.Join(", ", ports)}]");
            }
            catch (Exception ex)
            {
                WriteLine($"COM ports error: {ex.Message}");
            }
            var hiproCom = HiproWmiHelper.GetHiproComPortFromWmi("0C33", "0012");
            WriteLine($"WMI HI-PRO COM port (VID_0C33 PID_0012): {hiproCom ?? "(none)"}");
            LogFtd2xxLoadedPath();
            WriteLine("========================================");
        }

        /// <summary>
        /// Writes HI-PRO diagnostic snapshot to logs\hpro_diag.log: bitness, paths, PATH, and loaded ftd2xx path.
        /// Call at app startup. Creates logs folder if missing.
        /// </summary>
        public static void WriteHiproDiagLog()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var logDir = Path.Combine(baseDir, "logs");
            var logPath = Path.Combine(logDir, "hpro_diag.log");
            var lines = new List<string>
            {
                "=== hpro_diag.log " + DateTime.UtcNow.ToString("o") + " (UTC) ===",
                "Environment.Is64BitProcess: " + Environment.Is64BitProcess,
                "Environment.Is64BitOperatingSystem: " + Environment.Is64BitOperatingSystem,
                "Environment.CurrentDirectory: " + Environment.CurrentDirectory,
                "AppDomain.CurrentDomain.BaseDirectory (exe dir): " + baseDir,
                "PATH: " + (Environment.GetEnvironmentVariable("PATH") ?? "(null)"),
                "ftd2xx loaded path: " + GetFtd2xxLoadedPath(),
                "=== end ==="
            };
            try
            {
                Directory.CreateDirectory(logDir);
                File.AppendAllText(logPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                try { WriteLine("WriteHiproDiagLog failed: " + ex.Message); } catch { }
            }
        }

        private static string GetFtd2xxLoadedPath()
        {
            try
            {
                foreach (ProcessModule? mod in Process.GetCurrentProcess().Modules)
                {
                    if (mod == null) continue;
                    if ((mod.ModuleName ?? "").IndexOf("ftd2xx", StringComparison.OrdinalIgnoreCase) >= 0)
                        return mod.FileName ?? "(unknown)";
                }
            }
            catch (Exception ex) { return "exception: " + ex.Message; }
            return "not loaded yet";
        }

        /// <summary>
        /// Logs where ftd2xx.dll is loaded from (Process.Modules). Call after SDK/CTK have had a chance to load native DLLs.
        /// </summary>
        public static void LogFtd2xxLoadedPath()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                foreach (ProcessModule? mod in process.Modules)
                {
                    if (mod == null) continue;
                    var name = mod.ModuleName ?? "";
                    if (name.IndexOf("ftd2xx", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        WriteLine($"ftd2xx.dll loaded from (FullPath): {mod.FileName ?? "(unknown)"}");
                        return;
                    }
                }
                WriteLine("ftd2xx.dll not loaded yet");
            }
            catch (Exception ex)
            {
                WriteLine($"ftd2xx.dll path check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs the CTK communication interface list from the SDK (Strategy A). Call before scan when ProductManager is available.
        /// </summary>
        public static void WriteSdkInterfaceList(SDLib.IProductManager? productManager)
        {
            if (productManager == null) return;
            var list = SdkScanHelper.GetSdkCommunicationInterfaces(productManager);
            WriteLine("CTK communication interface list from SDK: [" + string.Join(", ", list) + "]");
        }

        /// <summary>
        /// Force CTK interface dump unconditionally. Marker 12345 proves execution. No early return before dumping.
        /// Call immediately after "SDK initialized successfully" in the same method.
        /// </summary>
        public static void DumpCtkInterfaces(SDLib.IProductManager? productManager)
        {
            WriteLine("=== CTK INTERFACE DUMP (MARKER 12345) ===");
            try
            {
                if (productManager == null)
                {
                    WriteLine("CTK_IF_COUNT = 0");
                    WriteLine("CTK Comm Interfaces: ProductManager is null");
                }
                else
                {
                    int count = SdkScanHelper.GetCommunicationInterfaceCount(productManager);
                    WriteLine("CTK_IF_COUNT = " + count);
                    for (int i = 0; i < count; i++)
                    {
                        string value = SafeGet(() => SdkScanHelper.GetCommunicationInterfaceString(productManager, i) ?? "(null)");
                        WriteLine("CTK_IF[" + i + "] = " + value);
                    }
                    string detailed = SdkScanHelper.GetDetailedErrorString(productManager);
                    if (!string.IsNullOrEmpty(detailed))
                        WriteLine("CTK GetDetailedErrorString = " + detailed);
                    string err = SdkScanHelper.GetErrorString(productManager);
                    if (!string.IsNullOrEmpty(err))
                        WriteLine("CTK GetErrorString = " + err);
                }
            }
            catch (Exception ex)
            {
                WriteLine("CTK_IF_DUMP_FAILED: " + (ex.ToString() ?? ex.Message));
            }
            WriteLine("=== CTK INTERFACE DUMP DONE (MARKER 12345) ===");
        }

        /// <summary>
        /// Logs current CTK error strings (call after a failed attempt to capture SDK state).
        /// </summary>
        public static void LogCtkErrors(SDLib.IProductManager? productManager)
        {
            if (productManager == null) return;
            var detailed = SdkScanHelper.GetDetailedErrorString(productManager);
            var err = SdkScanHelper.GetErrorString(productManager);
            if (!string.IsNullOrEmpty(detailed))
                WriteLine($"[CTK] GetDetailedErrorString: {detailed}");
            if (!string.IsNullOrEmpty(err))
                WriteLine($"[CTK] GetErrorString: {err}");
        }

        private static readonly object _scanLogLock = new object();
        private const long MaxScanLogBytes = 3 * 1024 * 1024;

        private static string ScanLogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "logs", "hpro_scan.log");

        /// <summary>Appends lines to logs/hpro_scan.log with timestamp. Used for SDException and scan diagnostics.</summary>
        public static void AppendToScanLog(params string[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            lock (_scanLogLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(ScanLogPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    var path = ScanLogPath;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length >= MaxScanLogBytes)
                    {
                        var backup = path + ".old";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(path, backup);
                    }
                    var prefix = "[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z] ";
                    File.AppendAllLines(path, lines.Select(l => prefix + l));
                }
                catch { /* best-effort */ }
            }
        }

        /// <summary>
        /// Force detailed error logging for every SDLib.SDException. Logs Message, HResult, InnerException, stack trace, and CTK error strings to console, log.txt, and logs/hpro_scan.log. Does not swallow errors.
        /// </summary>
        public static void LogSdExceptionDetails(SDLib.IProductManager? productManager, Exception ex)
        {
            if (ex == null) return;
            WriteLine("SD_EXCEPTION Message: " + (ex.Message ?? ""));
            WriteLine("SD_EXCEPTION HResult: " + ex.HResult.ToString());
            if (ex.InnerException != null)
                WriteLine("SD_EXCEPTION InnerException: " + ex.InnerException.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace))
                WriteLine("SD_EXCEPTION StackTrace: " + ex.StackTrace);
            WriteLine("SD_EXCEPTION ToString: " + ex.ToString());
            WriteLine("CTK_DETAILED_ERROR: " + SafeGet(() => GetDetailedErrorStringReflective(productManager)));
            WriteLine("CTK_ERROR: " + SafeGet(() => GetErrorStringReflective(productManager)));

            var scanLines = new List<string>
            {
                "SD_EXCEPTION Message: " + (ex.Message ?? ""),
                "SD_EXCEPTION HResult: " + ex.HResult,
                ex.InnerException != null ? "SD_EXCEPTION InnerException: " + ex.InnerException.Message : "SD_EXCEPTION InnerException: (none)",
                string.IsNullOrEmpty(ex.StackTrace) ? "SD_EXCEPTION StackTrace: (none)" : "SD_EXCEPTION StackTrace: " + ex.StackTrace,
                "SD_EXCEPTION ToString: " + ex.ToString(),
                "CTK_DETAILED_ERROR: " + SafeGet(() => GetDetailedErrorStringReflective(productManager)),
                "CTK_ERROR: " + SafeGet(() => GetErrorStringReflective(productManager))
            };
            AppendToScanLog(scanLines.ToArray());
        }

        /// <summary>Get pm.GetDetailedErrorString() via reflection; if method not found return "No GetDetailedErrorString method found".</summary>
        public static string GetDetailedErrorStringReflective(SDLib.IProductManager? productManager)
        {
            if (productManager == null) return "(pm null)";
            try
            {
                var m = productManager.GetType().GetMethod("GetDetailedErrorString", BindingFlags.Public | BindingFlags.Instance);
                if (m == null || m.GetParameters().Length != 0)
                    return "No GetDetailedErrorString method found";
                return m.Invoke(productManager, null)?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                return "GetDetailedErrorString reflection failed: " + ex.Message;
            }
        }

        /// <summary>Get pm.GetErrorString() via reflection; if method not found return "No GetErrorString method found".</summary>
        public static string GetErrorStringReflective(SDLib.IProductManager? productManager)
        {
            if (productManager == null) return "(pm null)";
            try
            {
                var m = productManager.GetType().GetMethod("GetErrorString", BindingFlags.Public | BindingFlags.Instance);
                if (m == null || m.GetParameters().Length != 0)
                    return "No GetErrorString method found";
                return m.Invoke(productManager, null)?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                return "GetErrorString reflection failed: " + ex.Message;
            }
        }

        /// <summary>Right before HI-PRO CheckDevice: dump Process.Modules for ftd2xx.dll, HI-PRO.dll, sdnet.dll. Log MODULE: name => path; if ftd2xx not found log MODULE NOT LOADED: ftd2xx.dll</summary>
        public static void LogModuleProvenance()
        {
            bool ftd2xxFound = false;
            try
            {
                var process = Process.GetCurrentProcess();
                foreach (ProcessModule? mod in process.Modules)
                {
                    if (mod == null) continue;
                    var name = mod.ModuleName ?? "";
                    if (name.IndexOf("ftd2xx", StringComparison.OrdinalIgnoreCase) >= 0) ftd2xxFound = true;
                    if (name.IndexOf("ftd2xx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("HI-PRO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("sdnet", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        WriteLine("MODULE: " + name + " => " + (mod.FileName ?? "(unknown)"));
                    }
                }
                if (!ftd2xxFound)
                    WriteLine("MODULE NOT LOADED: ftd2xx.dll");
            }
            catch (Exception ex)
            {
                WriteLine("MODULE provenance failed: " + ex.Message);
            }
        }

        /// <summary>For each target name, print MODULE: {name} => {fullpath} or MODULE: {name} => NOT_LOADED. Uses Process.GetCurrentProcess().Modules.</summary>
        public static void DumpLoadedModules(string[] targetNames)
        {
            if (targetNames == null || targetNames.Length == 0) return;
            try
            {
                var process = Process.GetCurrentProcess();
                var modules = new List<(string Name, string Path)>();
                foreach (ProcessModule? mod in process.Modules)
                {
                    if (mod == null) continue;
                    modules.Add((mod.ModuleName ?? "", mod.FileName ?? ""));
                }
                foreach (string name in targetNames)
                {
                    var match = modules.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || (m.Name != null && m.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));
                    bool loaded = match.Path != null && match.Path.Length > 0;
                    if (loaded)
                        Console.WriteLine("MODULE: " + name + " => " + match.Path);
                    else
                        Console.WriteLine("MODULE: " + name + " => NOT_LOADED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DumpLoadedModules failed: " + ex.Message);
            }
        }

        public static bool IsSdException(Exception? ex)
        {
            if (ex == null) return false;
            if (ex.GetType().Name.IndexOf("SDException", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return IsSdException(ex.InnerException);
        }
    }
}
