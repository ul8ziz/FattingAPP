using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.ServiceProcess;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>
    /// COM2-unlock preflight: check port, enumerate blocking processes/services, optional fix with user consent.
    /// Logs to AppBaseDir/logs/hpro_preflight.log with timestamps. No system DLL modifications.
    /// </summary>
    public static class HiproComPortGuard
    {
        private static readonly Regex NameRegex = new Regex(@"(Inspire|Starkey|Updater|HiPro|Monitor)", RegexOptions.IgnoreCase);
        private const int SerialTimeoutMs = 1500;
        private const int FixWaitMs = 800;
        private const long MaxLogBytes = 3 * 1024 * 1024;

        private static string PreflightLogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "logs", "hpro_preflight.log");

        /// <summary>Last result from CheckAndOptionallyFix or after Fix. ConnectDevicesView uses this to allow or block wired scan.</summary>
        public static PreflightResult? LastResult { get; private set; }

        /// <summary>
        /// Checks whether the COM port can be opened. On failure, enumerates matching processes/services and admin status.
        /// Does not fix; use Fix() separately with user consent.
        /// </summary>
        public static PreflightResult CheckAndOptionallyFix(string port = "COM2")
        {
            var result = new PreflightResult { PortName = port };
            var lines = new List<string>();

            void Log(string msg)
            {
                lines.Add($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {msg}");
            }

            try
            {
                Log("=== HiproComPortGuard Check ===");
                result.IsAdmin = WindowsAdmin.IsRunningAsAdmin();
                Log($"Port: {port} IsAdmin: {result.IsAdmin}");

                // 1) Try open/close with short timeouts
                try
                {
                    using var sp = new SerialPort(port, 9600, Parity.None, 8, StopBits.One);
                    sp.ReadTimeout = SerialTimeoutMs;
                    sp.WriteTimeout = SerialTimeoutMs;
                    sp.Open();
                    sp.Close();
                    result.IsFree = true;
                    Log($"{port} open/close: OK (port free)");
                }
                catch (Exception ex)
                {
                    result.IsFree = false;
                    result.BlockedReason = $"{ex.GetType().Name}: {ex.Message}";
                    Log($"{port} open/close: FAILED - {result.BlockedReason}");
                }

                if (!result.IsFree)
                {
                    // 2) Enumerate processes: Inspire|Starkey|Updater|HiPro|Monitor
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            var name = p.ProcessName ?? "";
                            if (NameRegex.IsMatch(name))
                            {
                                string? path = null;
                                try { path = p.MainModule?.FileName; } catch { }
                                result.Processes.Add(new ProcessEntry { Name = name, Id = p.Id, Path = path });
                                Log($"  Process: {name} PID={p.Id}");
                            }
                        }
                        finally { p.Dispose(); }
                        }
                    // 3) Enumerate services: name or display name matches
                    try
                    {
                        foreach (var sc in ServiceController.GetServices())
                        {
                            try
                            {
                                var name = sc.ServiceName ?? "";
                                var display = sc.DisplayName ?? "";
                                if (NameRegex.IsMatch(name) || NameRegex.IsMatch(display))
                                {
                                    result.Services.Add(new ServiceEntry { Name = name, DisplayName = display, Status = sc.Status.ToString() });
                                    Log($"  Service: {name} ({sc.Status})");
                                }
                            }
                            finally { sc.Dispose(); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  Services enum: {ex.Message}");
                    }
                }

                Log("=== Check end ===");
            }
            catch (Exception ex)
            {
                result.BlockedReason = "Check error: " + ex.Message;
                Log($"Check error: {ex}");
            }

            WritePreflightLog(lines);
            LastResult = result;
            return result;
        }

        /// <summary>
        /// If admin and user has consented: stop matching services, kill matching processes, wait, re-test port.
        /// Logs every action to hpro_preflight.log. Ignores individual stop/kill errors.
        /// </summary>
        public static PreflightResult Fix(string port, PreflightResult previousResult)
        {
            var lines = new List<string>();
            void Log(string msg)
            {
                lines.Add($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {msg}");
            }

            Log("=== HiproComPortGuard Fix (user consented) ===");
            if (!previousResult.IsAdmin)
            {
                Log("Not admin; skip fix.");
                WritePreflightLog(lines);
                return previousResult;
            }

            // Stop services first
            foreach (var s in previousResult.Services)
            {
                try
                {
                    using var sc = new ServiceController(s.Name);
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        Log($"Service stopped: {s.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Service stop failed {s.Name}: {ex.Message}");
                }
            }

            // Kill processes
            foreach (var p in previousResult.Processes)
            {
                try
                {
                    using var proc = Process.GetProcessById(p.Id);
                    proc.Kill();
                    Log($"Process killed: {p.Name} PID={p.Id}");
                }
                catch (Exception ex)
                {
                    Log($"Process kill failed {p.Name} PID={p.Id}: {ex.Message}");
                }
            }

            Log($"Waiting {FixWaitMs}ms...");
            System.Threading.Thread.Sleep(FixWaitMs);
            Log("Re-testing port...");
            var newResult = CheckAndOptionallyFix(port);
            WritePreflightLog(lines);
            LastResult = newResult;
            return newResult;
        }

        private static void WritePreflightLog(List<string> lines)
        {
            try
            {
                var dir = Path.GetDirectoryName(PreflightLogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var path = PreflightLogPath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length >= MaxLogBytes)
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
            public bool IsFree { get; set; }
            public string? BlockedReason { get; set; }
            public List<ProcessEntry> Processes { get; } = new List<ProcessEntry>();
            public List<ServiceEntry> Services { get; } = new List<ServiceEntry>();
            public bool IsAdmin { get; set; }
        }

        public class ProcessEntry
        {
            public string Name { get; set; } = "";
            public int Id { get; set; }
            public string? Path { get; set; }
        }

        public class ServiceEntry
        {
            public string Name { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Status { get; set; } = "";
        }
    }
}
