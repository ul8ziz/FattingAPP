using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// HI-PRO preflight pipeline: runs at startup before any CTK/sdnet init.
    /// Diagnostics, DLL order, VCP check, COM2 availability, optional auto-free, retry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class HiproPreflight
    {
        public const string Com2PortName = "COM2";
        private const string Ftser2kService = "FTSER2K";
        private const int PathTrimLength = 800;
        private const int Com2RetryCount = 3;
        private const int Com2RetryDelayMs = 500;

        /// <summary>When true (default), preflight may attempt to terminate locking processes and set HiProMonitor to Manual. When false, only detect and log.</summary>
        public static bool AutoFreeComPortOnStartup { get; set; } = true;

        private static HiproPreflightResult? _lastResult;
        private static Task<HiproPreflightResult>? _lastRunTask;
        private static readonly object _resultLock = new object();

        public static HiproPreflightResult? LastResult
        {
            get { lock (_resultLock) { return _lastResult; } }
            private set { lock (_resultLock) { _lastResult = value; } }
        }

        /// <summary>Gets the task for the last RunAsync. Await this before wired scan to ensure preflight is done.</summary>
        public static Task<HiproPreflightResult>? LastRunTask => _lastRunTask;

        /// <summary>Ensures preflight has completed; returns the result. Call before starting wired scan.</summary>
        public static async Task<HiproPreflightResult> EnsureCompletedAsync(CancellationToken cancellationToken = default)
        {
            var task = _lastRunTask;
            if (task == null) return HiproPreflightResult.NotRun();
            if (task.IsCompleted) return task.Result;
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs the full preflight pipeline asynchronously. Call from App.OnStartup (fire-and-forget or await on background).</summary>
        public static Task<HiproPreflightResult> RunAsync(CancellationToken cancellationToken = default)
        {
            var t = RunAsyncCore(cancellationToken);
            _lastRunTask = t;
            return t;
        }

        private static async Task<HiproPreflightResult> RunAsyncCore(CancellationToken cancellationToken)
        {
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var result = new HiproPreflightResult { AppBaseDirectory = appBase };

            try
            {
                // Step A: Diagnostics to logs/hpro_diag.log
                await Task.Run(() => StepA_WriteDiagnostics(result), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                // Step B: DLL search order — AppBase first (called from App.OnStartup before this; ensure again)
                await Task.Run(() => StepB_EnsureDllOrder(appBase), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                // Step C: Detect HI-PRO device and VCP (FTSER2K)
                await Task.Run(() => StepC_DetectHiproVcp(result), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                // Step D: Check COM2 availability
                bool com2Open = await Task.Run(() => StepD_TryOpenCom2(result), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (!com2Open && AutoFreeComPortOnStartup)
                {
                    // Step E: Try to free COM2 (terminate suspects, HiProMonitor Manual)
                    await Task.Run(() => StepE_TryFreeCom2(result), cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // Step F: Retry COM2 open up to 3 times
                for (int i = 0; i < Com2RetryCount && !com2Open; i++)
                {
                    await Task.Delay(Com2RetryDelayMs, cancellationToken).ConfigureAwait(false);
                    com2Open = await Task.Run(() => StepD_TryOpenCom2(result), cancellationToken).ConfigureAwait(false);
                }

                result.Com2Available = com2Open;
                if (!com2Open)
                {
                    result.Message = "COM2 is locked or inaccessible. Close other applications using the HI-PRO (e.g. Inspire, Starkey) and try again.";
                    PreflightLog.Append("Preflight: COM2 still locked after retries. HI-PRO scan will be aborted.");
                }
            }
            catch (OperationCanceledException)
            {
                result.Message = "Preflight was cancelled.";
                result.Com2Available = false;
            }
            catch (Exception ex)
            {
                result.Message = "Preflight error: " + ex.Message;
                result.Com2Available = false;
                PreflightLog.Append("Preflight exception: " + ex.ToString());
            }

            LastResult = result;
            return result;
        }

        // ——— Step A: Write diagnostics ———
        private static void StepA_WriteDiagnostics(HiproPreflightResult result)
        {
            var lines = new List<string>
            {
                "========== HI-PRO Preflight ==========",
                "Timestamp: " + DateTime.UtcNow.ToString("o") + " (UTC)",
                "Is64BitProcess: " + Environment.Is64BitProcess,
                "Is64BitOperatingSystem: " + Environment.Is64BitOperatingSystem,
                "AppBaseDirectory: " + (AppDomain.CurrentDomain.BaseDirectory ?? ""),
                "CurrentDirectory: " + Environment.CurrentDirectory
            };

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (path.Length > PathTrimLength)
                path = path.Substring(0, PathTrimLength) + "... [trimmed]";
            lines.Add("PATH: " + path);

            // FTDI-related DLL candidates
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var hiPro = SdkConfiguration.HiProDriverPath;
            var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ftd2xx.dll");
            var sysWow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "ftd2xx.dll");

            foreach (var dll in new[] { "ftd2xx.dll", "FTChipID.dll", "FTD2XX_NET.dll" })
            {
                lines.Add("DLL candidate " + dll + ":");
                lines.Add("  AppBase: " + Path.Combine(appBase, dll) + " Exists=" + File.Exists(Path.Combine(appBase, dll)));
                lines.Add("  HI-PRO:  " + Path.Combine(hiPro, dll) + " Exists=" + File.Exists(Path.Combine(hiPro, dll)));
                lines.Add("  System32: " + Path.Combine(Path.GetDirectoryName(sys32) ?? "", dll) + " Exists=" + File.Exists(Path.Combine(Path.GetDirectoryName(sys32) ?? "", dll)));
                var sysWowDir = Path.GetDirectoryName(sysWow) ?? "";
                lines.Add("  SysWOW64: " + Path.Combine(sysWowDir, dll) + " Exists=" + File.Exists(Path.Combine(sysWowDir, dll)));
            }

            var loadedPath = NativeDllResolver.GetFtd2xxLoadedPath();
            lines.Add("ftd2xx.dll loaded in process: " + (loadedPath ?? "(not loaded yet)"));
            result.Ftd2xxLoadedPath = loadedPath;

            foreach (var line in lines)
                PreflightLog.Append(line);
            PreflightLog.Append("");
        }

        // ——— Step B: DLL search order ———
        private static void StepB_EnsureDllOrder(string appBase)
        {
            if (string.IsNullOrEmpty(appBase) || !Directory.Exists(appBase)) return;
            if (NativeDllResolver.SetDllDirectoryPath(appBase))
                PreflightLog.Append("SetDllDirectory(AppBase) set: " + appBase);
            else
                PreflightLog.Append("WARNING: SetDllDirectory(AppBase) failed: " + appBase);
        }

        // ——— Step C: Detect HI-PRO and VCP (FTSER2K) ———
        private static void StepC_DetectHiproVcp(HiproPreflightResult result)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DeviceID, Service FROM Win32_PnPEntity WHERE Name LIKE '%COM2%' OR Name LIKE '%HI-PRO%'");
                foreach (ManagementBaseObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (name.IndexOf("COM2", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("HI-PRO", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";
                    var service = obj["Service"]?.ToString() ?? "";
                    result.Com2InstanceId = deviceId;
                    result.Com2DriverService = service;
                    PreflightLog.Append("PnP COM2/HI-PRO: InstanceId=" + deviceId + " Service=" + service);
                    if (!string.Equals(service, Ftser2kService, StringComparison.OrdinalIgnoreCase))
                        PreflightLog.Append("WARNING: Driver mode mismatch; expected VCP (FTSER2K). Got: " + service);
                }
            }
            catch (Exception ex)
            {
                PreflightLog.Append("StepC PnP query failed: " + ex.Message);
            }
        }

        // ——— Step D: Try open COM2 ———
        private static bool StepD_TryOpenCom2(HiproPreflightResult result)
        {
            try
            {
                using var port = new SerialPort(Com2PortName);
                port.Open();
                port.Close();
                PreflightLog.Append("COM2 open test: OK (port free)");
                return true;
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? ex.ToString();
                PreflightLog.Append("COM2 open test: FAILED - " + msg);
                if (msg.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Com2Locked = true;
                return false;
            }
        }

        // ——— Step E: Try to free COM2 (optional, safe) ———
        private static void StepE_TryFreeCom2(HiproPreflightResult result)
        {
            var suspectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Inspire.UpdaterService", "InspireUpdaterSDK", "Starkey.InspireSupport.Service"
            };
            var regex = new Regex(@"(Inspire|Starkey|Updater|HiPro|Monitor)", RegexOptions.IgnoreCase);

            var matches = new List<(string Name, int Id, string? Path)>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var name = p.ProcessName ?? "";
                        if (suspectNames.Contains(name) || regex.IsMatch(name))
                        {
                            string? path = null;
                            try { path = p.MainModule?.FileName; } catch { }
                            matches.Add((name, p.Id, path));
                            PreflightLog.Append("Lock suspect: " + name + " PID=" + p.Id + " Path=" + (path ?? "(n/a)"));
                        }
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                PreflightLog.Append("Process enumeration failed: " + ex.Message);
            }

            foreach (var (name, id, path) in matches)
                result.SuspectedProcesses.Add($"{name} (PID {id})");

            // Graceful close then Kill only for suspected PIDs (if AutoFreeComPortOnStartup)
            foreach (var m in matches)
            {
                var id = m.Id;
                try
                {
                    using var p = Process.GetProcessById(id);
                    var name = p.ProcessName ?? "";
                    try
                    {
                        if (p.CloseMainWindow())
                        {
                            PreflightLog.Append("CloseMainWindow sent to " + name + " (PID " + p.Id + ")");
                            p.WaitForExit(2000);
                        }
                        if (!p.HasExited)
                        {
                            p.Kill();
                            PreflightLog.Append("Kill " + name + " (PID " + p.Id + ")");
                        }
                    }
                    catch (Exception ex)
                    {
                        PreflightLog.Append("Not elevated or protected; cannot stop/kill " + name + ": " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    PreflightLog.Append("GetProcessById " + id + " failed: " + ex.Message);
                }
            }

            // HiProMonitor service: set StartupType = Manual (do not delete)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name = 'HiProMonitor'");
                foreach (ManagementBaseObject svc in searcher.Get())
                {
                    var mo = (ManagementObject)svc;
                    var startMode = mo["StartMode"]?.ToString() ?? "";
                    if (string.Equals(startMode, "Auto", StringComparison.OrdinalIgnoreCase))
                    {
                        using var inParams = mo.GetMethodParameters("Change");
                        inParams["StartMode"] = "Manual";
                        mo.InvokeMethod("Change", inParams, null);
                        PreflightLog.Append("HiProMonitor service set to Manual (was " + startMode + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                PreflightLog.Append("HiProMonitor service change (Manual): " + ex.Message + " (not elevated?)");
            }
        }

        /// <summary>Switch DLL directory to HI-PRO folder for scan. Call before CTK/sdnet scan.</summary>
        public static void SetHiProDllDirectoryForScan()
        {
            var hiPro = SdkConfiguration.HiProDriverPath;
            if (Directory.Exists(hiPro) && NativeDllResolver.SetDllDirectoryPath(hiPro))
                PreflightLog.Append("SetDllDirectory(HI-PRO) for scan: " + hiPro);
        }

        /// <summary>Restore DLL directory to AppBase after scan. Call after wired scan completes.</summary>
        public static void RestoreAppDllDirectoryAfterScan()
        {
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            if (!string.IsNullOrEmpty(appBase) && NativeDllResolver.SetDllDirectoryPath(appBase))
                PreflightLog.Append("RestoreAppDllDirectory: " + appBase);
        }
    }

    /// <summary>Result of HI-PRO preflight. Com2Available must be true for wired scan to proceed.</summary>
    public class HiproPreflightResult
    {
        public bool Com2Available { get; set; }
        public string Message { get; set; } = "";
        public string AppBaseDirectory { get; set; } = "";
        public string? Ftd2xxLoadedPath { get; set; }
        public bool Com2Locked { get; set; }
        public string? Com2InstanceId { get; set; }
        public string? Com2DriverService { get; set; }
        public List<string> SuspectedProcesses { get; } = new List<string>();

        public static HiproPreflightResult NotRun()
        {
            return new HiproPreflightResult { Message = "Preflight has not run yet.", Com2Available = false };
        }
    }
}
