using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.ServiceProcess;
using Ul8ziz.FittingApp.Device.DeviceCommunication;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>
    /// Evidence-based HI-PRO preflight: checks COM port, bitness, HI-PRO folder, and lists blocking processes/services.
    /// Logs to AppBaseDir/logs/hpro_preflight.log with timestamps.
    /// </summary>
    public static class HiproPreflight
    {
        private static readonly Regex ProcessServiceRegex = new Regex(@"(Inspire|Starkey|Updater|HiPro|Monitor)", RegexOptions.IgnoreCase);
        private const int SerialTimeoutMs = 1500;
        private const int MaxLogFileBytes = 3 * 1024 * 1024; // 3MB rotation

        private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "logs", "hpro_preflight.log");

        /// <summary>Last result from Run(). Used by ConnectDevicesView to decide whether to allow wired scan.</summary>
        public static PreflightResult? LastResult { get; private set; }

        /// <summary>
        /// Runs preflight for the given COM port. Logs diagnostics and returns a structured result.
        /// </summary>
        public static PreflightResult Run(string portName = "COM2")
        {
            var result = new PreflightResult { PortName = portName };
            var lines = new List<string>();

            void Log(string msg)
            {
                var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {msg}";
                lines.Add(line);
            }

            try
            {
                Log("=== HI-PRO Preflight ===");
                result.IsAdmin = WindowsAdmin.IsRunningAsAdmin();
                Log($"Is64BitProcess: {Environment.Is64BitProcess}");
                Log($"Is64BitOperatingSystem: {Environment.Is64BitOperatingSystem}");
                Log($"IsAdmin: {result.IsAdmin}");
                Log($"AppBaseDirectory: {AppDomain.CurrentDomain.BaseDirectory ?? ""}");

                // HI-PRO folder and required DLLs
                var hiProPath = SdkConfiguration.HiProDriverPath;
                result.HiProFolderExists = Directory.Exists(hiProPath);
                Log($"HI-PRO folder exists: {result.HiProFolderExists} ({hiProPath})");
                var requiredDlls = new[] { "ftd2xx.dll", "FTChipID.dll", "FTD2XX_NET.dll" };
                foreach (var dll in requiredDlls)
                {
                    var path = Path.Combine(hiProPath, dll);
                    var exists = File.Exists(path);
                    Log($"  {dll}: {exists}");
                }

                // Matching processes
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var name = p.ProcessName ?? "";
                        if (ProcessServiceRegex.IsMatch(name))
                        {
                            string? path = null;
                            try { path = p.MainModule?.FileName; } catch { }
                            result.MatchingProcesses.Add(new ProcessInfo { Name = name, Id = p.Id, Path = path });
                            Log($"  Process: {name} PID={p.Id} Path={path ?? "(n/a)"}");
                        }
                    }
                    finally { p.Dispose(); }
                }

                // Matching services
                try
                {
                    foreach (var sc in ServiceController.GetServices())
                    {
                        try
                        {
                            if (ProcessServiceRegex.IsMatch(sc.ServiceName))
                            {
                                result.MatchingServices.Add(new ServiceInfo { Name = sc.ServiceName, Status = sc.Status.ToString() });
                                Log($"  Service: {sc.ServiceName} Status={sc.Status}");
                            }
                        }
                        finally { sc.Dispose(); }
                    }
                }
                catch (Exception ex)
                {
                    Log($"  Services enumeration failed: {ex.Message}");
                }

                // Attempt to open COM port (9600, 8N1, short timeout)
                try
                {
                    using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                    port.ReadTimeout = SerialTimeoutMs;
                    port.WriteTimeout = SerialTimeoutMs;
                    port.Open();
                    port.Close();
                    result.IsPortFree = true;
                    Log($"{portName} open test: OK (port free)");
                }
                catch (Exception ex)
                {
                    result.IsPortFree = false;
                    result.BlockedReason = $"{ex.GetType().Name}: {ex.Message}";
                    Log($"{portName} open test: FAILED - {result.BlockedReason}");
                }

                // mode COM2
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c mode {portName}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var stdout = proc.StandardOutput.ReadToEnd();
                        var stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(3000);
                        Log($"mode {portName} stdout: {stdout?.Trim() ?? "(null)"}");
                        if (!string.IsNullOrWhiteSpace(stderr))
                            Log($"mode {portName} stderr: {stderr.Trim()}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"mode {portName} failed: {ex.Message}");
                }

                Log("=== Preflight end ===");
            }
            catch (Exception ex)
            {
                Log($"Preflight error: {ex}");
                result.BlockedReason = "Preflight error: " + ex.Message;
            }

            WriteLogLines(lines);
            LastResult = result;
            return result;
        }

        /// <summary>Stops matching services and kills matching processes; then re-tests COM port. Logs each action.</summary>
        public static PreflightResult TryMitigateAndRetest(string portName, PreflightResult previousResult)
        {
            var lines = new List<string>();
            void Log(string msg)
            {
                var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {msg}";
                lines.Add(line);
            }

            Log("=== Mitigation (user consented) ===");

            // Stop services first
            foreach (var svc in previousResult.MatchingServices)
            {
                try
                {
                    using var sc = new ServiceController(svc.Name);
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        Log($"Service stopped: {svc.Name}");
                    }
                    else
                        Log($"Service not running: {svc.Name}");
                }
                catch (Exception ex)
                {
                    Log($"Service stop failed {svc.Name}: {ex.Message}");
                }
            }

            // Kill processes
            foreach (var proc in previousResult.MatchingProcesses)
            {
                try
                {
                    using var p = Process.GetProcessById(proc.Id);
                    p.Kill();
                    Log($"Process killed: {proc.Name} PID={proc.Id}");
                }
                catch (Exception ex)
                {
                    Log($"Process kill failed {proc.Name} PID={proc.Id}: {ex.Message}");
                }
            }

            Log("Re-testing COM port...");
            System.Threading.Thread.Sleep(500);
            var newResult = Run(portName);
            WriteLogLines(lines);
            LastResult = newResult;
            return newResult;
        }

        private static void WriteLogLines(List<string> lines)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var path = LogPath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length >= MaxLogFileBytes)
                {
                    var backup = path + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(path, backup);
                }
                File.AppendAllLines(path, lines);
            }
            catch { /* best-effort */ }
        }

        public class PreflightResult
        {
            public string PortName { get; set; } = "COM2";
            public bool IsPortFree { get; set; }
            public string? BlockedReason { get; set; }
            public List<ProcessInfo> MatchingProcesses { get; } = new List<ProcessInfo>();
            public List<ServiceInfo> MatchingServices { get; } = new List<ServiceInfo>();
            public bool IsAdmin { get; set; }
            public bool HiProFolderExists { get; set; }
        }

        public class ProcessInfo
        {
            public string Name { get; set; } = "";
            public int Id { get; set; }
            public string? Path { get; set; }
        }

        public class ServiceInfo
        {
            public string Name { get; set; } = "";
            public string Status { get; set; } = "";
        }
    }
}
