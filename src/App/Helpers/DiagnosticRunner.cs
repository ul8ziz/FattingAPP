using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ul8ziz.FittingApp.Device.DeviceCommunication;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>
    /// Runs HI-PRO preflight + diagnostic PowerShell scripts at startup (before any scan) or on demand.
    /// Writes logs to logs\hpro_diag.log and captures stdout/stderr to logs\diagnostics\startup_runner_*.txt.
    /// When run from app, report is also written to logs\diagnostics\HI-PRO_DiagnosticReport.md.
    /// Config: appsettings.json "Diagnostics:AutoStopBlockers" = true and elevated → stop Inspire/Starkey/HiProTrayApp only.
    /// Never throws; if scripts are missing or fail, app continues normally.
    /// </summary>
    public static class DiagnosticRunner
    {
        private const string ScriptRelativePath = "scripts\\diagnostics\\05_summary_runner.ps1";
        private const string LogsFolder = "logs";
        private const string DiagnosticsFolder = "logs\\diagnostics";
        private const string DiagLogFile = "logs\\hpro_diag.log";
        private const int StartupTimeoutMs = 15000; // 10-20 sec range

        /// <summary>
        /// Reads Diagnostics:AutoStopBlockers from appsettings.json in app base directory.
        /// When true and process is elevated, startup diagnostics will pass -AutoStopStarkeyInspire to stop only Inspire/Starkey/HiProTrayApp.
        /// </summary>
        public static bool GetAutoStopBlockersFromConfig()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "";
                string path = Path.Combine(baseDir, "appsettings.json");
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Diagnostics", out var diag) &&
                    diag.TryGetProperty("AutoStopBlockers", out var val))
                    return val.GetBoolean();
            }
            catch { /* non-fatal */ }
            return false;
        }

        /// <summary>
        /// Tries to find the repo root by walking up from app base directory until
        /// scripts\diagnostics\05_summary_runner.ps1 exists.
        /// </summary>
        public static string? FindRepoRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(dir))
                dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";

            dir = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    break;
                string scriptPath = Path.Combine(dir, ScriptRelativePath);
                if (File.Exists(scriptPath))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static void SafeAppendLog(string? repoRoot, string message)
        {
            if (string.IsNullOrEmpty(repoRoot)) return;
            try
            {
                string logPath = Path.Combine(repoRoot, DiagLogFile);
                string? logDir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                string line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch { /* never crash */ }
        }

        /// <summary>
        /// Logs actual loaded module paths (GetModuleHandle + GetModuleFileName) for HiProWrapper.dll and ftd2xx.dll.
        /// Writes to logs\diagnostics\loaded_modules.txt (under repo root or app base) and to hpro_diag.log / Trace.
        /// Call at startup; modules may show "not loaded yet" until SDK/CTK loads them.
        /// </summary>
        public static void LogLoadedModulePaths()
        {
            try
            {
                string? hiPro = NativeDllResolver.GetHiProWrapperLoadedPath();
                string? ftd2xx = NativeDllResolver.GetFtd2xxLoadedPath();
                string h = hiPro ?? "(not loaded yet)";
                string f = ftd2xx ?? "(not loaded yet)";
                var lines = new[]
                {
                    $"[{DateTime.UtcNow:O}] Loaded module paths (GetModuleHandle + GetModuleFileName)",
                    "HiProWrapper.dll: " + h,
                    "ftd2xx.dll: " + f,
                    ""
                };
                string content = string.Join(Environment.NewLine, lines);
                Trace.WriteLine("HI-PRO loaded modules: HiProWrapper=" + h + "; ftd2xx=" + f);
                ScanDiagnostics.WriteLine("HiProWrapper.dll loaded from: " + h);
                ScanDiagnostics.WriteLine("ftd2xx.dll loaded from: " + f);

                string? baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir)) baseDir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string diagDir = Path.Combine(baseDir, "logs", "diagnostics");
                    try
                    {
                        if (!Directory.Exists(diagDir)) Directory.CreateDirectory(diagDir);
                        string loadedPath = Path.Combine(diagDir, "loaded_modules.txt");
                        File.WriteAllText(loadedPath, content, Encoding.UTF8);
                    }
                    catch { /* non-fatal */ }
                }

                string? repoRoot = FindRepoRoot();
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    SafeAppendLog(repoRoot, "HiProWrapper.dll loaded from: " + h);
                    SafeAppendLog(repoRoot, "ftd2xx.dll loaded from: " + f);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("LogLoadedModulePaths error: " + ex.Message);
            }
        }

        /// <summary>
        /// Runs the diagnostic summary script (synchronous). Reports written to docs/ and logs/diagnostics/ by the script.
        /// When reportToLogsDiagnostics is true, also writes logs\diagnostics\HI-PRO_DiagnosticReport.md.
        /// </summary>
        public static bool RunDiagnostics(string? appOutputDir = null, bool? autoStopStarkeyInspire = null, bool reportToLogsDiagnostics = true)
        {
            string? repoRoot = FindRepoRoot();
            if (string.IsNullOrEmpty(repoRoot))
            {
                Trace.WriteLine("HI-PRO Diagnostics: Scripts not found (not running from repo root). Skipping.");
                return false;
            }

            string scriptPath = Path.Combine(repoRoot, ScriptRelativePath);
            if (!File.Exists(scriptPath))
            {
                Trace.WriteLine("HI-PRO Diagnostics: 05_summary_runner.ps1 not found. Skipping.");
                return false;
            }

            string appDir = appOutputDir ?? AppDomain.CurrentDomain.BaseDirectory ?? "";
            appDir = Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar);

            bool doAutoStop = autoStopStarkeyInspire ?? (GetAutoStopBlockersFromConfig() && WindowsAdmin.IsRunningAsAdmin());
            var args = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\" -AppOutputDir \"{appDir}\""
                + (doAutoStop ? " -AutoStopStarkeyInspire" : "")
                + (reportToLogsDiagnostics ? " -ReportToLogsDiagnostics" : "");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = args,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                    return false;
                process.WaitForExit(120000);
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(stderr))
                    Trace.WriteLine("HI-PRO Diagnostics stderr: " + stderr);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("HI-PRO Diagnostics failed to run: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Runs preflight + diagnostics in the background at startup (before any scan). Does not block UI.
        /// Timeout 15s; stdout/stderr written to logs\diagnostics\startup_runner_stdout.txt and startup_runner_stderr.txt;
        /// report also to logs\diagnostics\HI-PRO_DiagnosticReport.md. Log lines appended to logs\hpro_diag.log.
        /// If appsettings.json Diagnostics:AutoStopBlockers=true and process is elevated, passes -AutoStopStarkeyInspire. Never throws.
        /// </summary>
        public static void RunDiagnosticsAsync(string? appOutputDir = null, bool? autoStopStarkeyInspire = null)
        {
            _ = Task.Run(() =>
            {
                string? repoRoot = null;
                try
                {
                    repoRoot = FindRepoRoot();
                    if (string.IsNullOrEmpty(repoRoot))
                    {
                        Trace.WriteLine("HI-PRO Diagnostics: Scripts not found. Skipping startup diagnostic run.");
                        return;
                    }

                    string scriptPath = Path.Combine(repoRoot, ScriptRelativePath);
                    if (!File.Exists(scriptPath))
                    {
                        SafeAppendLog(repoRoot, "Startup diagnostics skipped: 05_summary_runner.ps1 not found.");
                        return;
                    }

                    string diagDir = Path.Combine(repoRoot, DiagnosticsFolder);
                    if (!Directory.Exists(diagDir))
                        Directory.CreateDirectory(diagDir);

                    string appDir = appOutputDir ?? AppDomain.CurrentDomain.BaseDirectory ?? "";
                    appDir = Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar);

                    bool doAutoStop = autoStopStarkeyInspire ?? (GetAutoStopBlockersFromConfig() && WindowsAdmin.IsRunningAsAdmin());
                    if (doAutoStop)
                        SafeAppendLog(repoRoot, "Startup diagnostics: AutoStopBlockers enabled (elevated); will stop Inspire/Starkey/HiProTrayApp only.");
                    SafeAppendLog(repoRoot, "Startup diagnostics: running 05_summary_runner.ps1 (timeout " + StartupTimeoutMs + " ms).");

                    var args = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\" -AppOutputDir \"{appDir}\""
                        + (doAutoStop ? " -AutoStopStarkeyInspire" : "")
                        + " -ReportToLogsDiagnostics";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = args,
                        WorkingDirectory = repoRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null)
                    {
                        SafeAppendLog(repoRoot, "Startup diagnostics: failed to start process.");
                        return;
                    }

                    var stdoutPath = Path.Combine(diagDir, "startup_runner_stdout.txt");
                    var stderrPath = Path.Combine(diagDir, "startup_runner_stderr.txt");

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    bool completed = process.WaitForExit(StartupTimeoutMs);
                    if (!completed)
                    {
                        try { process.Kill(); } catch { }
                        SafeAppendLog(repoRoot, "Startup diagnostics: timed out after " + StartupTimeoutMs + " ms.");
                        File.WriteAllText(stdoutPath, "(timeout)\r\n", Encoding.UTF8);
                        File.WriteAllText(stderrPath, "Process timed out and was terminated.\r\n", Encoding.UTF8);
                        return;
                    }

                    string stdout = stdoutTask.GetAwaiter().GetResult();
                    string stderr = stderrTask.GetAwaiter().GetResult();

                    try
                    {
                        File.WriteAllText(stdoutPath, stdout ?? "", Encoding.UTF8);
                        File.WriteAllText(stderrPath, stderr ?? "", Encoding.UTF8);
                    }
                    catch { /* non-fatal */ }

                    SafeAppendLog(repoRoot, "Startup diagnostics: exit code " + process.ExitCode + ".");
                    if (!string.IsNullOrEmpty(stderr))
                        SafeAppendLog(repoRoot, "Stderr: " + stderr.Replace("\r\n", " ").Replace("\n", " "));
                }
                catch (Exception ex)
                {
                    SafeAppendLog(repoRoot ?? "", "Startup diagnostics error: " + ex.Message);
                    Trace.WriteLine("HI-PRO Diagnostics async error: " + ex.Message);
                }
            });
        }
    }
}
