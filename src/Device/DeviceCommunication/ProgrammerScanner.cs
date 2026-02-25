using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public class ProgrammerScanner
    {
        private readonly SdkManager _sdkManager;

        // C) Guard scan with SemaphoreSlim(1,1). All CTK/sdnet calls run on one thread; no Task.Run/ThreadPool in scan path.
        private static readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);

        // Supported wired programmer names (must match sd.config interface_name).
        // Currently HI-PRO only; CAA and Promira can be re-added to the list later if needed.
        private readonly List<string> _wiredProgrammers = new()
        {
            Constants.HiPro,     // HI-PRO
        };

        // Wireless programmer interface names (must match sd.config: NOAHlink, and SDK for RSL10).
        private static readonly List<string> _wirelessProgrammers = new()
        {
            "NOAHlink",  // sd.config interface_name
            Constants.Rsl10,
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
                    if (ScanDiagnostics.IsSdException(ex))
                        ScanDiagnostics.LogSdExceptionDetails(productManager, ex);
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
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(_sdkManager?.ProductManager, ex);
                progress?.Report(100);
                return new ScanResult();
            }
        }

        /// <summary>
        /// Synchronous scan for programmers. Must be called from an STA thread (e.g. UI thread).
        /// Only one scan runs at a time (SemaphoreSlim). Uses SerialPort.GetPortNames() for real COM ports.
        /// If scan runs on the UI thread, pass yieldToUI so that the Cancel button can be processed (e.g. pump Dispatcher messages).
        /// </summary>
        /// <param name="progress">Progress reporter 0-100.</param>
        /// <param name="cancellationToken">Cancellation token; check when user clicks Cancel.</param>
        /// <param name="yieldToUI">Optional: invoke to process UI messages (e.g. Dispatcher.PushFrame) so Cancel is responsive.</param>
        public ScanResult ScanAllProgrammersSync(
            IProgress<int>? progress,
            CancellationToken cancellationToken,
            Action? yieldToUI = null)
        {
            _scanLock.Wait(cancellationToken);
            try
            {
                Debug.WriteLine("[ScanAllProgrammersSync] Acquired scan lock; starting sync scan on current thread.");
                var result = new ScanResult();
                ScanForWiredProgrammersSyncCore(result, progress, cancellationToken, yieldToUI);
                ScanForWirelessProgrammersSyncCore(result, progress, cancellationToken, yieldToUI);
                progress?.Report(100);
                Debug.WriteLine($"[ScanAllProgrammersSync] Scan complete. Found: {result.FoundProgrammers.Count} (wired + wireless)");
                return result;
            }
            finally
            {
                _scanLock.Release();
                Debug.WriteLine("[ScanAllProgrammersSync] Released scan lock.");
            }
        }

        /// <summary>
        /// Synchronous scan for wired programmers only (e.g. HI-PRO). Same thread and yield rules as ScanAllProgrammersSync.
        /// </summary>
        public ScanResult ScanWiredOnlySync(
            IProgress<int>? progress,
            CancellationToken cancellationToken,
            Action? yieldToUI = null)
        {
            _scanLock.Wait(cancellationToken);
            try
            {
                Debug.WriteLine("[ScanWiredOnlySync] Starting wired-only scan.");
                Console.WriteLine("=== SCAN_PATCH_MARKER_888 ===");
                var result = new ScanResult();
                ScanForWiredProgrammersSyncCore(result, progress, cancellationToken, yieldToUI);
                progress?.Report(100);
                Debug.WriteLine($"[ScanWiredOnlySync] Complete. Found: {result.FoundProgrammers.Count}");
                return result;
            }
            finally
            {
                _scanLock.Release();
            }
        }

        /// <summary>
        /// Synchronous scan for wireless programmers only (e.g. NOAHlink, RSL10). Same thread and yield rules as ScanAllProgrammersSync.
        /// </summary>
        public ScanResult ScanWirelessOnlySync(
            IProgress<int>? progress,
            CancellationToken cancellationToken,
            Action? yieldToUI = null)
        {
            _scanLock.Wait(cancellationToken);
            try
            {
                Debug.WriteLine("[ScanWirelessOnlySync] Starting wireless-only scan.");
                var result = new ScanResult();
                ScanForWirelessProgrammersSyncCore(result, progress, cancellationToken, yieldToUI);
                progress?.Report(100);
                Debug.WriteLine($"[ScanWirelessOnlySync] Complete. Found: {result.FoundProgrammers.Count}");
                return result;
            }
            finally
            {
                _scanLock.Release();
            }
        }

        /// <summary>
        /// Core synchronous scan logic for wired programmers. Caller must hold _scanLock and run on STA thread.
        /// HI-PRO: Strategy A (SDK interface enumeration) then Strategy B (WMI-detected COM port only).
        /// Progress 0-50%. yieldToUI is invoked periodically so the UI can process Cancel.
        /// </summary>
        private void ScanForWiredProgrammersSyncCore(
            ScanResult result,
            IProgress<int>? progress,
            CancellationToken cancellationToken,
            Action? yieldToUI = null)
        {
            var productManager = _sdkManager.ProductManager;

            // CTK interface dump already done in SdkManager.Initialize() right after "SDK initialized successfully"
            ScanDiagnostics.WriteStartupAndScanPreamble();
            LogEnvironmentDiagnostics();
            Thread.Sleep(400);
            progress?.Report(5);

            int totalChecks = _wiredProgrammers.Count;
            int currentCheck = 0;

            foreach (var programmerName in _wiredProgrammers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yieldToUI?.Invoke();

                var attempt = new ScanAttemptResult
                {
                    ProgrammerName = programmerName,
                    DisplayName = GetProgrammerDisplayName(programmerName)
                };

                try
                {
                    Debug.WriteLine($"[Scan] Trying programmer: {programmerName}");

                    if (programmerName == Constants.HiPro)
                    {
                        HiproScanStrategyAThenB(productManager, result, attempt, programmerName, cancellationToken, yieldToUI);
                    }
                    else
                    {
                        // CAA, Promira: default USB
                        if (SdkScanHelper.TryCheckDevice(productManager, programmerName, "", out var err))
                        {
                            attempt.Found = true;
                            result.FoundProgrammers.Add(CreateProgrammerInfo(result.FoundProgrammers.Count + 1, attempt.DisplayName, programmerName, "USB"));
                        }
                        else
                        {
                            attempt.ErrorCode = "NOT_CONNECTED";
                            attempt.ErrorMessage = err;
                        }
                    }
                }
                catch (Exception ex)
                {
                    attempt.Found = false;
                    attempt.ErrorMessage = ex.Message;
                    Debug.WriteLine($"[Scan] EXCEPTION for {programmerName}: {ex.GetType().Name}: {ex.Message}");
                    if (ScanDiagnostics.IsSdException(ex))
                    {
                        Console.WriteLine("SD_EXCEPTION: " + ex.Message);
                        Console.WriteLine(ex.ToString());
                        ScanDiagnostics.LogSdExceptionDetails(productManager, ex);
                    }
                    var fullMsg = ex.ToString() ?? "";
                    if (fullMsg.Contains("E_UNKNOWN_NAME")) attempt.ErrorCode = "E_UNKNOWN_NAME";
                    else if (fullMsg.Contains("E_INVALID_STATE")) attempt.ErrorCode = "E_INVALID_STATE";
                    else if (fullMsg.Contains("E_NOT_FOUND")) attempt.ErrorCode = "E_NOT_FOUND";
                    else attempt.ErrorCode = "UNKNOWN";
                    attempt.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                    if (ex.InnerException != null) attempt.ErrorMessage += $" | Inner: {ex.InnerException.Message}";
                }

                result.AllAttempts.Add(attempt);
                currentCheck++;
                progress?.Report(5 + (int)((currentCheck / (double)totalChecks) * 45));
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Returns the CTK communication interface list (same order as DumpCtkInterfaces). Used for Strategy A.
        /// </summary>
        private static List<string> GetCtkInterfaceList(IProductManager productManager)
        {
            var list = new List<string>();
            int count = SdkScanHelper.GetCommunicationInterfaceCount(productManager);
            for (int i = 0; i < count; i++)
            {
                var s = SdkScanHelper.GetCommunicationInterfaceString(productManager, i);
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Strategy A: CTK interface strings (HI-PRO filtered if present). Strategy B: COM2-only fallback.
        /// One attempt at a time; TryCheckDevice does create/check/close. No concurrent CTK calls.
        /// </summary>
        private void HiproScanStrategyAThenB(
            IProductManager productManager,
            ScanResult result,
            ScanAttemptResult attempt,
            string programmerName,
            CancellationToken cancellationToken,
            Action? yieldToUI)
        {
            Thread.Sleep(300);
            yieldToUI?.Invoke();

            // 4) Module dump right before attempting HI-PRO scan
            ScanDiagnostics.DumpLoadedModules(new[] { "ftd2xx.dll", "FTChipID.dll", "HI-PRO.dll", "HiProWrapper.dll", "sdnet.dll" });

            // Strategy A: CTK interface strings. If any contains "HI-PRO", try only those; else try all. Log CTK errors after each failure.
            var rawInterfaces = GetCtkInterfaceList(productManager);
            var interfaces = rawInterfaces.Count > 0 && rawInterfaces.Any(s => s.IndexOf("HI-PRO", StringComparison.OrdinalIgnoreCase) >= 0)
                ? rawInterfaces.Where(s => s.IndexOf("HI-PRO", StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                : rawInterfaces;
            if (interfaces.Count > 0)
            {
                ScanDiagnostics.WriteLine($"[Scan] HI-PRO Strategy A: trying {interfaces.Count} SDK interface(s)");
                foreach (var ifStr in interfaces)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yieldToUI?.Invoke();
                    if (SdkScanHelper.TryCheckDevice(productManager, programmerName, ifStr, out var err))
                    {
                        attempt.Found = true;
                        var portLabel = ifStr.IndexOf("COM", StringComparison.OrdinalIgnoreCase) >= 0 ? ifStr : "USB";
                        result.FoundProgrammers.Add(CreateProgrammerInfo(result.FoundProgrammers.Count + 1, attempt.DisplayName, programmerName, portLabel));
                        ScanDiagnostics.WriteLine($"[Scan] CONFIRMED: HI-PRO via SDK interface: {ifStr}");
                        return;
                    }
                    ScanDiagnostics.WriteLine($"[Scan] HI-PRO interface '{ifStr}': {err}");
                    ScanDiagnostics.LogCtkErrors(productManager);
                    Thread.Sleep(80);
                }
            }

            // Strategy B: COM2 legacy only. Try "COM2", "COM2:", "\\\\.\\COM2", then port=COM2, port=2. Never COM3/COM4. Log CTK errors after each failure.
            const string com2Fallback = "COM2";
            var com2Variants = new List<string> { "COM2", "COM2:", "\\\\.\\COM2" };
            var hiproCom = HiproWmiHelper.GetHiproComPortFromWmi("0C33", "0012");
            if (!string.IsNullOrEmpty(hiproCom) && string.Equals(hiproCom, com2Fallback, StringComparison.OrdinalIgnoreCase))
            {
                int portNum = ParseComNumber(hiproCom);
                com2Variants.Add($"port={hiproCom}");
                if (portNum > 0) com2Variants.Add($"port={portNum}");
            }
            else if (string.IsNullOrEmpty(hiproCom))
            {
                com2Variants.Add("port=COM2");
                com2Variants.Add("port=2");
            }
            ScanDiagnostics.WriteLine($"[Scan] HI-PRO Strategy B: trying COM2 only, {com2Variants.Count} variant(s)");
            foreach (var settings in com2Variants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yieldToUI?.Invoke();
                if (SdkScanHelper.TryCheckDevice(productManager, programmerName, settings, out var err))
                {
                    attempt.Found = true;
                    result.FoundProgrammers.Add(CreateProgrammerInfo(result.FoundProgrammers.Count + 1, attempt.DisplayName, programmerName, com2Fallback));
                    ScanDiagnostics.WriteLine($"[Scan] CONFIRMED: HI-PRO on COM2 via '{settings}'");
                    return;
                }
                ScanDiagnostics.WriteLine($"[Scan] COM2 variant '{settings}': {err}");
                ScanDiagnostics.LogCtkErrors(productManager);
                if (IsPortInUseExceptionFromMessage(err))
                    attempt.ComPortsInUse.Add(com2Fallback);
                Thread.Sleep(80);
            }

            attempt.ErrorCode = "NOT_CONNECTED";
            attempt.ErrorMessage = interfaces.Count > 0
                ? "Interface enumeration and COM2 fallback did not detect HI-PRO"
                : (rawInterfaces.Count == 0 ? "No CTK communication interfaces; COM2 fallback tried" : "COM2 fallback tried but not detected");
            attempt.HiproComPortsTriedCount = 1;
            ScanDiagnostics.WriteLine("[Scan] HI-PRO NOT CONNECTED");
            // Success criteria: If CTK_IF_COUNT == 0 => CTK communication modules not properly registered/loaded. If D2XX count > 0 but CTK fails => CTK interface selection/init/STA issue (run HiproD2xxProbe).
        }

        private static bool IsPortInUseExceptionFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("being used", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ProgrammerInfo CreateProgrammerInfo(int id, string displayName, string interfaceName, string port)
        {
            return new ProgrammerInfo
            {
                Id = id,
                Name = displayName,
                Type = ProgrammerType.Wired,
                InterfaceName = interfaceName,
                Port = port,
                IsAvailable = true
            };
        }

        private static ProgrammerInfo CreateWirelessProgrammerInfo(int id, string displayName, string interfaceName, string deviceId)
        {
            return new ProgrammerInfo
            {
                Id = id,
                Name = displayName,
                Type = ProgrammerType.Wireless,
                InterfaceName = interfaceName,
                DeviceId = deviceId,
                Port = null,
                IsAvailable = true
            };
        }

        /// <summary>
        /// Scans for wireless programmers (NOAHlink, RSL10). Calls BeginScanForWirelessDevices first if available (required by SDK),
        /// then CreateWirelessCommunicationInterface. Caller must hold _scanLock and run on STA thread.
        /// </summary>
        private void ScanForWirelessProgrammersSyncCore(ScanResult result, IProgress<int>? progress, CancellationToken cancellationToken, Action? yieldToUI = null)
        {
            var productManager = _sdkManager.ProductManager;

            cancellationToken.ThrowIfCancellationRequested();
            yieldToUI?.Invoke();

            // SDK requires BeginScanForWirelessDevices before CreateWirelessCommunicationInterface (E_CALL_SCAN otherwise)
            if (TryBeginScanForWirelessDevices(productManager))
            {
                // Allow time for wireless scan to discover devices; yield periodically so Cancel works
                for (int i = 0; i < 25 && !cancellationToken.IsCancellationRequested; i++)
                {
                    Thread.Sleep(100);
                    yieldToUI?.Invoke();
                }
            }

            int total = _wirelessProgrammers.Count;
            int current = 0;

            foreach (var interfaceName in _wirelessProgrammers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yieldToUI?.Invoke();

                var attempt = new ScanAttemptResult
                {
                    ProgrammerName = interfaceName,
                    DisplayName = GetProgrammerDisplayName(interfaceName)
                };

                try
                {
                    Debug.WriteLine($"[Scan] Wireless: trying {interfaceName}");

                    // Try empty device ID first (some SDKs use it for "first available" or default).
                    ICommunicationAdaptor? commAdaptor = null;
                    try
                    {
                        commAdaptor = productManager.CreateWirelessCommunicationInterface("");
                        if (commAdaptor != null)
                        {
                            bool check = false;
                            try { check = commAdaptor.CheckDevice(); }
                            catch (Exception ex) { Debug.WriteLine($"[Scan] Wireless {interfaceName} CheckDevice: {ex.Message}"); }
                            if (check)
                            {
                                attempt.Found = true;
                                result.FoundProgrammers.Add(CreateWirelessProgrammerInfo(result.FoundProgrammers.Count + 1, attempt.DisplayName, interfaceName, ""));
                                Debug.WriteLine($"[Scan] CONFIRMED: Wireless {interfaceName} (default)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Scan] Wireless {interfaceName} CreateWirelessCommunicationInterface(\"\"): {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        if (commAdaptor != null && !attempt.Found)
                        {
                            try { commAdaptor.CloseDevice(); } catch { }
                        }
                    }

                    // Optional: try to enumerate wireless device IDs via reflection (e.g. GetWirelessDeviceList).
                    if (!attempt.Found)
                    {
                        var deviceIds = TryGetWirelessDeviceIdsFromSdk(productManager);
                        foreach (var deviceId in deviceIds)
                        {
                            if (attempt.Found) break;
                            cancellationToken.ThrowIfCancellationRequested();
                            yieldToUI?.Invoke();
                            commAdaptor = null;
                            try
                            {
                                commAdaptor = productManager.CreateWirelessCommunicationInterface(deviceId);
                                if (commAdaptor != null)
                                {
                                    bool check = false;
                                    try { check = commAdaptor.CheckDevice(); }
                                    catch { }
                                    if (check)
                                    {
                                        attempt.Found = true;
                                        result.FoundProgrammers.Add(CreateWirelessProgrammerInfo(result.FoundProgrammers.Count + 1, attempt.DisplayName, interfaceName, deviceId));
                                        Debug.WriteLine($"[Scan] CONFIRMED: Wireless {interfaceName} DeviceId={deviceId}");
                                    }
                                }
                            }
                            catch { /* ignore */ }
                            finally
                            {
                                if (commAdaptor != null && !attempt.Found) try { commAdaptor.CloseDevice(); } catch { }
                            }
                        }
                    }

                    if (!attempt.Found)
                    {
                        attempt.ErrorCode = "NOT_CONNECTED";
                        attempt.ErrorMessage = "No wireless programmer detected. Ensure Bluetooth is on and NOAHlink/RSL10 is in range.";
                        Debug.WriteLine($"[Scan] Wireless {interfaceName}: NOT CONNECTED");
                    }
                }
                catch (Exception ex)
                {
                    attempt.Found = false;
                    attempt.ErrorCode = "UNKNOWN";
                    attempt.ErrorMessage = ex.Message;
                    Debug.WriteLine($"[Scan] Wireless {interfaceName} EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                }

                result.AllAttempts.Add(attempt);
                current++;
                progress?.Report(50 + (int)((current / (double)total) * 50));
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Calls BeginScanForWirelessDevices on the product manager (required by SDK before CreateWirelessCommunicationInterface).
        /// Tries all overloads (0 args, int, string, etc.). If return is IAsyncResult, calls EndScanForWirelessDevices after wait.
        /// Returns true if the call was made successfully.
        /// </summary>
        private static bool TryBeginScanForWirelessDevices(IProductManager productManager)
        {
            if (productManager == null) return false;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            Type? type = productManager.GetType();
            while (type != null)
            {
                foreach (var method in type.GetMethods(flags))
                {
                    if (!method.Name.Equals("BeginScanForWirelessDevices", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var parameters = method.GetParameters();
                    string sig = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
                    Debug.WriteLine($"[Scan] Wireless: trying BeginScanForWirelessDevices({sig}) on {type.Name}");
                    object[]? args = TryBuildArguments(parameters);
                    if (args == null)
                        continue;
                    try
                    {
                        object? result = method.Invoke(productManager, args);
                        Debug.WriteLine($"[Scan] Wireless: BeginScanForWirelessDevices({sig}) called successfully on {type.Name}");
                        if (result != null)
                            TryEndScanForWirelessDevices(productManager, result);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        string inner = ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "";
                        Debug.WriteLine($"[Scan] Wireless: BeginScanForWirelessDevices({sig}) failed: {ex.Message}{inner}");
                    }
                }
                type = type.BaseType;
            }
            try
            {
                var t = productManager.GetType();
                var scanMethods = t.GetMethods(flags).Where(m => m.Name.IndexOf("Scan", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0).Select(m => m.Name).Distinct().ToList();
                if (scanMethods.Count > 0)
                    Debug.WriteLine($"[Scan] Wireless: ProductManager type={t.FullName}; scan/wireless methods: [{string.Join(", ", scanMethods)}]");
            }
            catch { /* ignore */ }
            return false;
        }

        /// <summary>Builds argument array for BeginScanForWirelessDevices; returns null if we cannot build sensible args.</summary>
        private static object[]? TryBuildArguments(ParameterInfo[] parameters)
        {
            if (parameters.Length == 0) return Array.Empty<object>();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                Type pt = p.ParameterType;
                if (pt == typeof(int)) { args[i] = 5000; continue; }
                if (pt == typeof(long)) { args[i] = 5000L; continue; }
                if (pt == typeof(string)) { args[i] = ""; continue; }
                if (pt == typeof(bool)) { args[i] = false; continue; }
                if (pt.IsValueType && Nullable.GetUnderlyingType(pt) == null) { args[i] = Activator.CreateInstance(pt)!; continue; }
                return null;
            }
            return args;
        }

        /// <summary>Calls EndScanForWirelessDevices if result is IAsyncResult or Task; waits for completion.</summary>
        private static void TryEndScanForWirelessDevices(IProductManager productManager, object asyncResult)
        {
            if (productManager == null || asyncResult == null) return;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            Type? type = productManager.GetType();
            while (type != null)
            {
                var endMethod = type.GetMethods(flags).FirstOrDefault(m =>
                    m.Name.Equals("EndScanForWirelessDevices", StringComparison.OrdinalIgnoreCase) &&
                    m.GetParameters().Length == 1);
                if (endMethod != null)
                {
                    try
                    {
                        if (asyncResult is System.IAsyncResult iar && !iar.IsCompleted)
                        {
                            try
                            {
                                if (iar.AsyncWaitHandle != null)
                                    iar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(6));
                            }
                            catch { /* ignore */ }
                        }
                        endMethod.Invoke(productManager, new[] { asyncResult });
                        Debug.WriteLine("[Scan] Wireless: EndScanForWirelessDevices called");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Scan] Wireless: EndScanForWirelessDevices: {ex.Message}");
                    }
                    return;
                }
                type = type.BaseType;
            }
        }

        /// <summary>
        /// Tries to get wireless device IDs from SDK via reflection (e.g. GetWirelessDeviceList, EnumerateDevices).
        /// Returns empty list if no such API is found.
        /// </summary>
        private static List<string> TryGetWirelessDeviceIdsFromSdk(IProductManager productManager)
        {
            var list = new List<string>();
            try
            {
                var type = productManager.GetType();
                foreach (var method in type.GetMethods())
                {
                    if (method.ReturnType == typeof(string[]) || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition().Name == "IEnumerable`1"))
                    {
                        if (method.Name.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            method.Name.IndexOf("Device", StringComparison.OrdinalIgnoreCase) >= 0 && method.Name.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var parameters = method.GetParameters();
                            if (parameters.Length == 0)
                            {
                                var ret = method.Invoke(productManager, null);
                                if (ret is string[] arr)
                                {
                                    list.AddRange(arr);
                                    break;
                                }
                                if (ret is System.Collections.IEnumerable enumerable and not string)
                                {
                                    foreach (var item in enumerable)
                                        if (item?.ToString() is string s && !string.IsNullOrEmpty(s))
                                            list.Add(s);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
            return list;
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
                "NOAHlink" => "NOAHlink Wireless",
                Constants.Rsl10 => "RSL10",
                _ => interfaceName
            };
        }
    }
}
