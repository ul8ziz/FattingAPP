using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public class ProgrammerScanner
    {
        private readonly SdkManager _sdkManager;

        // Supported wired programmer names (must match sd.config interface_name).
        // HI-PRO first so CTK state is clean (CAA CheckDevice can leave E_INVALID_STATE and break subsequent HI-PRO attempts).
        private readonly List<string> _wiredProgrammers = new()
        {
            Constants.HiPro,     // HI-PRO
            Constants.Caa,       // Communication Accelerator Adaptor
            Constants.Promira,   // Promira
        };

        public ProgrammerScanner(SdkManager sdkManager)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
        }

        /// <summary>
        /// Detailed scan result for each programmer attempt
        /// </summary>
        public class ScanAttemptResult
        {
            public string ProgrammerName { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public bool Found { get; set; }
            public string ErrorCode { get; set; } = "";
            public string ErrorMessage { get; set; } = "";
            /// <summary>When set, HI-PRO fallback tried this many COM ports in addition to default.</summary>
            public int? HiproComPortsTriedCount { get; set; }
            /// <summary>COM ports that appear to be in use by another program (e.g. HI-PRO Configuration).</summary>
            public List<string> ComPortsInUse { get; set; } = new();
        }

        /// <summary>
        /// Result containing found programmers and diagnostic info about all attempts
        /// </summary>
        public class ScanResult
        {
            public List<ProgrammerInfo> FoundProgrammers { get; set; } = new();
            public List<ScanAttemptResult> AllAttempts { get; set; } = new();
            
            public string GetDiagnosticSummary()
            {
                var lines = new List<string>();
                foreach (var attempt in AllAttempts)
                {
                    if (attempt.Found)
                        lines.Add($"  ✓ {attempt.DisplayName}: Connected and ready");
                    else if (attempt.ErrorCode == "NOT_CONNECTED")
                    {
                        var n = attempt.HiproComPortsTriedCount;
                        if (n.HasValue && n.Value >= 0)
                            lines.Add($"  ○ {attempt.DisplayName}: Driver OK, hardware not connected (tried {n.Value} COM port(s))");
                        else
                            lines.Add($"  ○ {attempt.DisplayName}: Driver OK, hardware not connected");
                        if (attempt.ComPortsInUse.Count > 0)
                            lines.Add($"  ⚠ Port(s) possibly in use by another program: {string.Join(", ", attempt.ComPortsInUse)}. Close HI-PRO Configuration or the manufacturer's software and try again.");
                    }
                    else if (attempt.ErrorCode == "E_INVALID_STATE")
                        lines.Add($"  ○ {attempt.DisplayName}: Driver OK, hardware not connected");
                    else if (attempt.ErrorCode == "E_UNKNOWN_NAME")
                        lines.Add($"  ✕ {attempt.DisplayName}: Module not found [{attempt.ErrorMessage}]");
                    else
                        lines.Add($"  ✕ {attempt.DisplayName}: [{attempt.ErrorCode}] {attempt.ErrorMessage}");
                }
                return string.Join("\n", lines);
            }

            /// <summary>
            /// Gets full technical detail for debug/support purposes
            /// </summary>
            public string GetFullDiagnosticDetail()
            {
                var lines = new List<string>();
                var hiproPath = SdkConfiguration.HiProDriverPath;
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                var pathHasHiPro = path.Contains(hiproPath, StringComparison.OrdinalIgnoreCase);
                var ctkPath = SdkConfiguration.FindCtkPath();
                var ctkExists = ctkPath != null;
                
                lines.Add($"Architecture: {(IntPtr.Size == 8 ? "64-bit" : "32-bit")}");
                lines.Add($"HI-PRO Path: {hiproPath}");
                lines.Add($"HI-PRO Path Exists: {System.IO.Directory.Exists(hiproPath)}");
                lines.Add($"PATH has HI-PRO: {pathHasHiPro}");
                lines.Add($"CTK Runtime Installed: {ctkExists}");
                if (ctkExists)
                    lines.Add($"CTK Path: {ctkPath}");
                else
                    lines.Add($"  ⚠ CTK Runtime is required for wired programmers (CAA, HI-PRO, Promira)");
                lines.Add("");
                foreach (var attempt in AllAttempts)
                {
                    lines.Add($"[{attempt.ProgrammerName}]");
                    lines.Add($"  Found: {attempt.Found}");
                    lines.Add($"  ErrorCode: {attempt.ErrorCode}");
                    lines.Add($"  ErrorMessage: {attempt.ErrorMessage}");
                    if (attempt.ComPortsInUse.Count > 0)
                        lines.Add($"  ComPortsInUse: {string.Join(", ", attempt.ComPortsInUse)}");
                }
                return string.Join("\n", lines);
            }
        }

        /// <summary>
        /// Scans for wired programmers following SDK pattern:
        /// CreateCommunicationInterface(programmer, port, "") with empty settings.
        /// </summary>
        public async Task<ScanResult> ScanForWiredProgrammersAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ScanResult();
            var productManager = _sdkManager.ProductManager;

            // Log diagnostic environment info
            LogEnvironmentDiagnostics();

            // Brief delay after SDK init so CTK/driver can settle before first CreateCommunicationInterface
            await Task.Delay(400, cancellationToken);

            progress?.Report(5);

            int totalChecks = _wiredProgrammers.Count;
            int currentCheck = 0;

            foreach (var programmerName in _wiredProgrammers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attempt = new ScanAttemptResult
                {
                    ProgrammerName = programmerName,
                    DisplayName = GetProgrammerDisplayName(programmerName)
                };

                ICommunicationAdaptor? commAdaptor = null;
                try
                {
                    Debug.WriteLine($"\n--- Trying programmer: {programmerName} ---");

                    // HI-PRO is scanned first (see _wiredProgrammers) so CTK state is clean.
                    // Only try default (USB) when there are no COM ports. If we create with "" and CheckDevice throws,
                    // the CTK is left in E_INVALID_STATE and all subsequent CreateCommunicationInterface(port=COMx) throw.
                    if (programmerName == Constants.HiPro)
                    {
                        string[] comPorts = SerialPort.GetPortNames() ?? Array.Empty<string>();
                        // Prefer COM1-COM4 first (legacy HI-PRO / HI-PRO Configuration Help), then rest by number
                        Array.Sort(comPorts, (a, b) =>
                            {
                                int na = ParseComNumber(a);
                                int nb = ParseComNumber(b);
                                bool aLegacy = na >= 1 && na <= 4;
                                bool bLegacy = nb >= 1 && nb <= 4;
                                if (aLegacy != bLegacy) return aLegacy ? -1 : 1;
                                return na.CompareTo(nb);
                            });

                        // Only try default (USB) when no COM ports exist; otherwise first Create(port=COMx) would see E_INVALID_STATE
                        if (comPorts.Length == 0)
                        {
                            try
                            {
                                commAdaptor = productManager.CreateCommunicationInterface(programmerName, CommunicationPort.kLeft, "");
                                if (commAdaptor != null)
                                {
                                    bool check = false;
                                    try { check = commAdaptor.CheckDevice(); }
                                    catch (Exception checkEx) { Debug.WriteLine($"  -> HI-PRO (default/USB): CheckDevice threw: {checkEx.Message}"); }
                                    Debug.WriteLine($"  -> HI-PRO (default/USB): CreateInterface OK, CheckDevice={check}");
                                    if (check)
                                    {
                                        attempt.Found = true;
                                        result.FoundProgrammers.Add(new ProgrammerInfo
                                        {
                                            Id = result.FoundProgrammers.Count + 1,
                                            Name = attempt.DisplayName,
                                            Type = ProgrammerType.Wired,
                                            InterfaceName = programmerName,
                                            Port = "USB",
                                            IsAvailable = true
                                        });
                                    }
                                }
                            }
                            catch (Exception ex) { Debug.WriteLine($"  -> HI-PRO (default/USB): {ex.GetType().Name}: {ex.Message}"); }
                            finally
                            {
                                if (commAdaptor != null && !attempt.Found) { try { commAdaptor.CloseDevice(); } catch { } commAdaptor = null; }
                            }
                        }

                        if (!attempt.Found && comPorts.Length > 0)
                        {
                            Debug.WriteLine($"  -> HI-PRO: trying {comPorts.Length} COM port(s): [{string.Join(", ", comPorts)}]");
                            foreach (string comPort in comPorts)
                            {
                                if (attempt.Found) break;
                                cancellationToken.ThrowIfCancellationRequested();

                                int portNum = ParseComNumber(comPort);
                                // Try minimal formats first (port=COM2, port=2) in case SDK only accepts these; then full settings
                                var formats = new List<string> { $"port={comPort}" };
                                if (portNum > 0)
                                    formats.Add($"port={portNum}");
                                if (portNum > 0)
                                {
                                    formats.Add($"port={comPort};hiprodriverversion=402;iovoltage=1.35V");
                                    formats.Add($"port={portNum};hiprodriverversion=402;iovoltage=1.35V");
                                    formats.Add($"comport={comPort};hiprodriverversion=402;iovoltage=1.35V");
                                    formats.Add($"comport={portNum};hiprodriverversion=402;iovoltage=1.35V");
                                }
                                else
                                {
                                    formats.Add($"port={comPort};hiprodriverversion=402;iovoltage=1.35V");
                                    formats.Add($"comport={comPort};hiprodriverversion=402;iovoltage=1.35V");
                                }
                                string[] portFormats = formats.ToArray();

                                foreach (string settings in portFormats)
                                {
                                    ICommunicationAdaptor? adaptor = null;
                                    try
                                    {
                                        adaptor = productManager.CreateCommunicationInterface(
                                            programmerName,
                                            CommunicationPort.kLeft,
                                            settings);
                                        if (adaptor != null)
                                        {
                                            bool check = adaptor.CheckDevice();
                                            Debug.WriteLine($"  -> {comPort} (settings: {settings}): CreateInterface OK, CheckDevice={check}");
                                            if (check)
                                            {
                                                attempt.Found = true;
                                                Debug.WriteLine($"  -> CONFIRMED: HI-PRO on {comPort}");
                                                result.FoundProgrammers.Add(new ProgrammerInfo
                                                {
                                                    Id = result.FoundProgrammers.Count + 1,
                                                    Name = attempt.DisplayName,
                                                    Type = ProgrammerType.Wired,
                                                    InterfaceName = programmerName,
                                                    Port = comPort,
                                                    IsAvailable = true
                                                });
                                                commAdaptor = adaptor;
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"  -> {comPort}: CreateCommunicationInterface returned null");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"  -> {comPort}: {ex.GetType().Name}: {ex.Message}");
                                        if (IsPortInUseException(ex) && !attempt.ComPortsInUse.Contains(comPort))
                                            attempt.ComPortsInUse.Add(comPort);
                                    }
                                    finally
                                    {
                                        if (adaptor != null && !attempt.Found)
                                        {
                                            try { adaptor.CloseDevice(); }
                                            catch { /* ignore */ }
                                        }
                                    }
                                    if (attempt.Found) break;
                                }
                            }
                        }

                        if (!attempt.Found)
                        {
                            attempt.ErrorCode = "NOT_CONNECTED";
                            attempt.ErrorMessage = comPorts.Length > 0
                                ? "Interface created but hardware not detected (tried COM ports)"
                                : "No COM ports available";
                            attempt.HiproComPortsTriedCount = comPorts.Length > 0 ? comPorts.Length : (int?)null;
                            Debug.WriteLine($"  -> NOT CONNECTED: {programmerName} (tried {comPorts.Length} COM port(s))");
                        }
                    }
                    else
                    {
                        // CAA, Promira: create with default settings, then CheckDevice
                        commAdaptor = productManager.CreateCommunicationInterface(
                            programmerName,
                            CommunicationPort.kLeft,
                            "");

                        if (commAdaptor != null)
                        {
                            Debug.WriteLine($"  -> CreateCommunicationInterface OK for {programmerName}");
                            bool isHardwarePresent = false;
                            try
                            {
                                isHardwarePresent = commAdaptor.CheckDevice();
                                Debug.WriteLine($"  -> CheckDevice for {programmerName}: {isHardwarePresent}");
                            }
                            catch (Exception checkEx)
                            {
                                Debug.WriteLine($"  -> CheckDevice threw for {programmerName}: {checkEx.Message}");
                                isHardwarePresent = false;
                            }

                            if (isHardwarePresent)
                            {
                                attempt.Found = true;
                                Debug.WriteLine($"  -> CONFIRMED: {programmerName} hardware is connected");
                                result.FoundProgrammers.Add(new ProgrammerInfo
                                {
                                    Id = result.FoundProgrammers.Count + 1,
                                    Name = attempt.DisplayName,
                                    Type = ProgrammerType.Wired,
                                    InterfaceName = programmerName,
                                    Port = "USB",
                                    IsAvailable = true
                                });
                            }
                            else
                            {
                                attempt.ErrorCode = "NOT_CONNECTED";
                                attempt.ErrorMessage = "Interface created but hardware not detected";
                                Debug.WriteLine($"  -> NOT CONNECTED: {programmerName} (interface OK, hardware absent)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    attempt.Found = false;
                    attempt.ErrorMessage = ex.Message;

                    // Log FULL error details
                    Debug.WriteLine($"  -> EXCEPTION for {programmerName}:");
                    Debug.WriteLine($"     Type: {ex.GetType().FullName}");
                    Debug.WriteLine($"     Message: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"     InnerException Type: {ex.InnerException.GetType().FullName}");
                        Debug.WriteLine($"     InnerException Msg: {ex.InnerException.Message}");
                        if (ex.InnerException.InnerException != null)
                            Debug.WriteLine($"     InnerInner Msg: {ex.InnerException.InnerException.Message}");
                    }
                    Debug.WriteLine($"     StackTrace: {ex.StackTrace}");

                    // Parse SDK error code
                    var fullMsg = ex.ToString(); // includes all inner exceptions
                    if (fullMsg.Contains("E_UNKNOWN_NAME"))
                        attempt.ErrorCode = "E_UNKNOWN_NAME";
                    else if (fullMsg.Contains("E_INVALID_STATE"))
                        attempt.ErrorCode = "E_INVALID_STATE";
                    else if (fullMsg.Contains("E_NOT_FOUND"))
                        attempt.ErrorCode = "E_NOT_FOUND";
                    else
                        attempt.ErrorCode = "UNKNOWN";

                    // Store the FULL error for diagnostics
                    attempt.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                    if (ex.InnerException != null)
                        attempt.ErrorMessage += $" | Inner: {ex.InnerException.Message}";
                }
                finally
                {
                    try { commAdaptor?.CloseDevice(); }
                    catch { /* ignore close errors */ }
                }

                result.AllAttempts.Add(attempt);

                currentCheck++;
                progress?.Report(5 + (int)((currentCheck / (double)totalChecks) * 90));

                await Task.Delay(100, cancellationToken);
            }

            progress?.Report(100);
            return result;
        }

        /// <summary>
        /// Logs detailed environment diagnostics for debugging programmer detection issues.
        /// </summary>
        private void LogEnvironmentDiagnostics()
        {
            Debug.WriteLine("\n====== SCAN ENVIRONMENT DIAGNOSTICS ======");
            Debug.WriteLine($"Process Architecture: {(IntPtr.Size == 8 ? "64-bit" : "32-bit")}");
            Debug.WriteLine($"OS: {Environment.OSVersion}");
            Debug.WriteLine($"AppBaseDir: {AppDomain.CurrentDomain.BaseDirectory}");

            // Check HI-PRO path
            var hiproPath = SdkConfiguration.HiProDriverPath;
            Debug.WriteLine($"\nHI-PRO Path: {hiproPath}");
            Debug.WriteLine($"HI-PRO Exists: {System.IO.Directory.Exists(hiproPath)}");
            if (System.IO.Directory.Exists(hiproPath))
            {
                try
                {
                    var files = System.IO.Directory.GetFiles(hiproPath, "*.dll");
                    Debug.WriteLine($"HI-PRO DLLs ({files.Length}):");
                    foreach (var f in files)
                        Debug.WriteLine($"  - {System.IO.Path.GetFileName(f)}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  Error listing HI-PRO files: {ex.Message}");
                }
            }

            // Check SD_CONFIG_PATH
            var sdConfig = Environment.GetEnvironmentVariable("SD_CONFIG_PATH");
            Debug.WriteLine($"\nSD_CONFIG_PATH: {sdConfig}");
            if (sdConfig != null)
                Debug.WriteLine($"SD_CONFIG exists: {System.IO.File.Exists(sdConfig)}");

            // Check CTK installation (required for wired programmers)
            var ctkPath = SdkConfiguration.FindCtkPath();
            Debug.WriteLine($"\nCTK Runtime Installed: {ctkPath != null}");
            if (ctkPath != null)
            {
                Debug.WriteLine($"CTK Path: {ctkPath}");
                var commModulesPath = System.IO.Path.Combine(ctkPath, "communication_modules");
                if (System.IO.Directory.Exists(commModulesPath))
                {
                    var commDlls = System.IO.Directory.GetFiles(commModulesPath, "*.dll");
                    Debug.WriteLine($"CTK Communication Modules ({commDlls.Length}):");
                    foreach (var f in commDlls)
                        Debug.WriteLine($"  - {System.IO.Path.GetFileName(f)}");
                }
            }
            else
            {
                Debug.WriteLine($"  ⚠ WARNING: CTK Runtime not found! Required for wired programmers (CAA, HI-PRO, Promira).");
            }

            // Check PATH for HI-PRO
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var containsHiPro = path.Contains("HI-PRO", StringComparison.OrdinalIgnoreCase);
            Debug.WriteLine($"\nPATH contains HI-PRO: {containsHiPro}");
            
            // Log first 5 PATH entries for context
            var pathEntries = path.Split(';');
            Debug.WriteLine($"PATH entries (first 10 of {pathEntries.Length}):");
            for (int i = 0; i < Math.Min(10, pathEntries.Length); i++)
                Debug.WriteLine($"  [{i}] {pathEntries[i]}");

            Debug.WriteLine("====== END DIAGNOSTICS ======\n");
        }

        /// <summary>
        /// Scans for all programmers and returns detailed results.
        /// </summary>
        public async Task<ScanResult> ScanAllProgrammersAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(0);

            try
            {
                var result = await ScanForWiredProgrammersAsync(
                    new Progress<int>(p => progress?.Report(p)),
                    cancellationToken);

                Debug.WriteLine($"\n=== Scan Summary ===");
                Debug.WriteLine(result.GetDiagnosticSummary());
                Debug.WriteLine($"=== Found: {result.FoundProgrammers.Count} ===\n");

                progress?.Report(100);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during scan: {ex.Message}");
                progress?.Report(100);
                return new ScanResult();
            }
        }

        /// <summary>
        /// Returns true if the COM port appears to be in use by another process (e.g. access denied, port in use).
        /// </summary>
        private static bool IsComPortInUse(string portName)
        {
            SerialPort? port = null;
            try
            {
                port = new SerialPort(portName);
                port.Open();
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (System.IO.IOException ex)
            {
                var msg = ex.Message ?? "";
                return msg.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("in use", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("being used", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("requested resource is in use", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try
                {
                    port?.Close();
                    port?.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        /// <summary>
        /// Returns true if the exception indicates the port/device is in use by another program.
        /// </summary>
        private static bool IsPortInUseException(Exception ex)
        {
            if (ex == null) return false;
            if (ex is UnauthorizedAccessException || ex is InvalidOperationException)
                return true;
            var msg = (ex.Message ?? "") + (ex.InnerException?.Message ?? "");
            return msg.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("in use", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("being used", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses COM port number for sorting (e.g. COM2 before COM10).
        /// </summary>
        private static int ParseComNumber(string comPort)
        {
            if (string.IsNullOrEmpty(comPort)) return 0;
            const string prefix = "COM";
            if (comPort.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(comPort.AsSpan(prefix.Length), out int n))
                return n;
            return 0;
        }

        private string GetProgrammerDisplayName(string interfaceName)
        {
            return interfaceName switch
            {
                Constants.HiPro => "HI-PRO",
                Constants.Dsp3 => "DSP3",
                Constants.Caa => "Communication Accelerator (CAA)",
                Constants.Promira => "Promira",
                Constants.Noahlink => "NOAHlink Wireless",
                Constants.Rsl10 => "RSL10",
                _ => interfaceName
            };
        }
    }
}
