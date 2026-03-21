using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Sound Designer SDK wrapper following the official Programmer's Guide flow:
    ///   1. Product.BeginInitializeDevice(adaptor) → EndInitializeDevice → bool IsConfigured
    ///   2. Product.BeginReadParameters(ParameterSpace) → poll → GetResult()
    ///   3. Product.BeginWriteParameters(ParameterSpace) → poll → GetResult()
    ///
    /// CRITICAL THREADING RULE: All SDK calls MUST run on the UI/STA thread where the
    /// COM objects were created. Do NOT use Task.Run() — that would move calls to a
    /// ThreadPool/MTA thread, causing Access Violation (0xc0000005) in sdnet.dll.
    /// Instead, use async/await with Task.Delay() for polling, which yields to the
    /// Dispatcher and keeps the UI responsive (same pattern as DeviceConnectionService).
    ///
    /// NEVER calls ConfigureDevice — that is a Manufacturing operation (Section 9.4).
    /// </summary>
    public sealed class SoundDesignerService : ISoundDesignerService
    {
        private const int PollIntervalMs = 50;
        private const int MemoryCount = 8;

        // NVM write discovery (per plan: reflection on ParameterSpace and BeginWriteParameters).
        // Vendor sample presuite_memory_switch.py: WriteParameters(memoryNumber) writes to NVM; WriteParameters(kActiveMemory) writes RAM only.
        private static bool _nvmDiscoveryLogged;
        private static bool? _nvmWriteAvailable; // null = not yet discovered; true/false after first discovery
        private static Func<IProduct, int, object?>? _beginWriteParametersInt; // product, memoryIndex -> IAsyncResult-like monitor
        private static Func<int, ParameterSpace?>? _nvmSpaceForMemoryIndex; // memoryIndex -> kNvmMemoryN (when no int overload)
        private static ParameterSpace? _systemNvmSpace; // kSystemNvmMemory when available

        /// <summary>
        /// Discovers NVM write API via reflection: ParameterSpace enum/static fields and IProduct.BeginWriteParameters overloads.
        /// Logs to Debug and ScanDiagnostics. Call once before first write (e.g. from WriteMemorySnapshotAsync).
        /// See plan: fix_nvm_persistence_on_reconnect_2e09772b.plan.md; vendor sample presuite_memory_switch.py.
        /// </summary>
        private static void DiscoverNvmWriteApi(IProduct product)
        {
            if (_nvmDiscoveryLogged)
                return;
            _nvmDiscoveryLogged = true;

            try
            {
                var paramSpaceType = typeof(ParameterSpace);
                var productType = product.GetType();

                // Log ParameterSpace: enum values or static fields (e.g. kNvmMemory0..7, kSystemNvmMemory)
                var paramSpaceLines = new List<string>();
                if (paramSpaceType.IsEnum)
                {
                    foreach (var name in Enum.GetNames(paramSpaceType))
                        paramSpaceLines.Add(name);
                }
                else
                {
                    foreach (var fi in paramSpaceType.GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (fi.FieldType == paramSpaceType || fi.FieldType == typeof(int))
                        {
                            try
                            {
                                var val = fi.GetValue(null);
                                paramSpaceLines.Add($"{fi.Name}={val}");
                            }
                            catch { paramSpaceLines.Add(fi.Name + "=(get failed)"); }
                        }
                    }
                }
                var paramSpaceLog = "ParameterSpace: " + (paramSpaceLines.Count > 0 ? string.Join(", ", paramSpaceLines) : "(none)");
                Debug.WriteLine("[NVM] " + paramSpaceLog);
                ScanDiagnostics.WriteLine("[NVM] " + paramSpaceLog);

                // Log BeginWriteParameters overloads (signature and parameter types)
                var writeMethods = productType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "BeginWriteParameters").ToArray();
                foreach (var m in writeMethods)
                {
                    var ps = m.GetParameters();
                    var sig = m.Name + "(" + string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name)) + ")";
                    Debug.WriteLine("[NVM] " + sig);
                    ScanDiagnostics.WriteLine("[NVM] " + sig);
                }

                // Prefer BeginWriteParameters(int memoryIndex) for NVM write (per Python WriteParameters(memoryNumber))
                MethodInfo? intOverload = null;
                foreach (var m in writeMethods)
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && (ps[0].ParameterType == typeof(int) || ps[0].ParameterType == typeof(long)))
                    {
                        intOverload = m;
                        break;
                    }
                }
                if (intOverload != null)
                {
                    _beginWriteParametersInt = (prod, memIdx) =>
                    {
                        try
                        {
                            return intOverload.Invoke(prod, new object[] { memIdx });
                        }
                        catch (Exception ex)
                        {
                            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                            Debug.WriteLine($"[NVM] BeginWriteParameters({memIdx}) failed: {inner.Message}");
                            ScanDiagnostics.WriteLine($"[NVM] BeginWriteParameters({memIdx}) failed: {inner.Message}");
                            return null;
                        }
                    };
                    _nvmWriteAvailable = true;
                    Debug.WriteLine("[NVM] NVM write available via BeginWriteParameters(int memoryIndex)");
                    ScanDiagnostics.WriteLine("[NVM] NVM write available via BeginWriteParameters(int memoryIndex)");
                    return;
                }

                // Fallback: try ParameterSpace kNvmMemory0..kNvmMemory7 and kSystemNvmMemory
                ParameterSpace? nvmSpaceForMemory = null;
                ParameterSpace? systemNvmSpace = null;
                if (paramSpaceType.IsEnum)
                {
                    var names = Enum.GetNames(paramSpaceType);
                    for (int i = 0; i < MemoryCount; i++)
                    {
                        var name = "kNvmMemory" + i;
                        if (Enum.TryParse(paramSpaceType, name, ignoreCase: true, out var parsed))
                        {
                            nvmSpaceForMemory = (ParameterSpace)parsed!;
                            break;
                        }
                    }
                    if (Enum.TryParse(paramSpaceType, "kSystemNvmMemory", ignoreCase: true, out var sysNvm))
                        systemNvmSpace = (ParameterSpace)sysNvm;
                }
                else
                {
                    for (int i = 0; i < MemoryCount; i++)
                    {
                        var fi = paramSpaceType.GetField("kNvmMemory" + i, BindingFlags.Public | BindingFlags.Static);
                        if (fi != null && fi.FieldType == paramSpaceType)
                        {
                            nvmSpaceForMemory = (ParameterSpace?)fi.GetValue(null);
                            break;
                        }
                    }
                    var sysFi = paramSpaceType.GetField("kSystemNvmMemory", BindingFlags.Public | BindingFlags.Static);
                    if (sysFi != null && sysFi.FieldType == paramSpaceType)
                        systemNvmSpace = (ParameterSpace?)sysFi.GetValue(null);
                }
                if (nvmSpaceForMemory != null || systemNvmSpace != null)
                {
                    // Build resolver for any memory index (kNvmMemory0..7)
                    _systemNvmSpace = systemNvmSpace;
                    if (paramSpaceType.IsEnum)
                    {
                        var nvmSpaces = new ParameterSpace?[MemoryCount];
                        for (int i = 0; i < MemoryCount; i++)
                        {
                            var name = "kNvmMemory" + i;
                            if (Enum.TryParse(paramSpaceType, name, ignoreCase: true, out var parsed))
                                nvmSpaces[i] = (ParameterSpace)parsed!;
                        }
                        _nvmSpaceForMemoryIndex = idx => (idx >= 0 && idx < MemoryCount) ? nvmSpaces[idx] : null;
                    }
                    else
                    {
                        var fields = new System.Reflection.FieldInfo?[MemoryCount];
                        for (int i = 0; i < MemoryCount; i++)
                            fields[i] = paramSpaceType.GetField("kNvmMemory" + i, BindingFlags.Public | BindingFlags.Static);
                        _nvmSpaceForMemoryIndex = idx =>
                        {
                            if (idx < 0 || idx >= MemoryCount || fields[idx] == null) return null;
                            try { return (ParameterSpace?)fields[idx]!.GetValue(null); } catch { return null; }
                        };
                    }
                    _nvmWriteAvailable = true;
                    Debug.WriteLine("[NVM] NVM write available via ParameterSpace (kNvmMemoryN / kSystemNvmMemory)");
                    ScanDiagnostics.WriteLine("[NVM] NVM write available via ParameterSpace (kNvmMemoryN / kSystemNvmMemory)");
                    return;
                }

                _nvmWriteAvailable = false;
                Debug.WriteLine("[NVM] Not supported by docs: no NVM write API found; persistence may fail after disconnect.");
                ScanDiagnostics.WriteLine("[NVM] Not supported by docs: no NVM write API found; persistence may fail after disconnect.");
            }
            catch (Exception ex)
            {
                _nvmWriteAvailable = false;
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                Debug.WriteLine($"[NVM] Discovery error: {inner.Message}");
                ScanDiagnostics.WriteLine("[NVM] Discovery error: " + inner.Message);
            }
        }

        // =========================================================================
        // InitializeDevice — Required before ReadParameters / WriteParameters
        // =========================================================================

        /// <summary>
        /// Calls Product.BeginInitializeDevice(adaptor) / EndInitializeDevice.
        /// Returns true if device is configured and ready for Read/Write.
        /// Returns false if device is not configured with this product.
        /// This is the FITTING initialization, NOT ConfigureDevice (manufacturing).
        /// See Programmer's Guide Section 6.3.
        ///
        /// IMPORTANT: Must be called from the UI/STA thread — SDK COM objects have
        /// thread affinity. Polling via await Task.Delay keeps the UI responsive.
        /// </summary>
        public async Task<bool> InitializeDeviceAsync(IProduct product, ICommunicationAdaptor adaptor, CancellationToken ct)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (adaptor == null) throw new ArgumentNullException(nameof(adaptor));

            ct.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            Debug.WriteLine("[SoundDesigner] BeginInitializeDevice...");

            // Begin* must run on the same STA thread where the SDK objects were created.
            // Do NOT wrap in Task.Run() — that would move to a ThreadPool/MTA thread
            // and cause an Access Violation in sdnet.dll.
            var monitor = product.BeginInitializeDevice(adaptor);
            if (monitor == null)
            {
                Debug.WriteLine("[SoundDesigner] BeginInitializeDevice returned null");
                return false;
            }

            // Poll on the same thread; await Task.Delay yields to the Dispatcher
            // so the UI stays responsive (same pattern as DeviceConnectionService.ConnectAsync).
            while (!monitor.IsFinished)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(PollIntervalMs, ct);
            }

            stopwatch.Stop();

            // Check for errors first
            try
            {
                monitor.GetResult();
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException t ? t.InnerException ?? ex : ex;
                Debug.WriteLine($"[SoundDesigner] InitializeDevice error: {inner.Message}");
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(null, ex);
                throw;
            }

            // EndInitializeDevice returns bool: true = device is configured
            bool isConfigured = product.EndInitializeDevice(monitor);
            Debug.WriteLine($"[SoundDesigner] InitializeDevice completed in {stopwatch.ElapsedMilliseconds} ms, IsConfigured={isConfigured}");

            return isConfigured;
        }

        // =========================================================================
        // ReadParameters — Uses BeginReadParameters / polling / GetResult
        // =========================================================================

        /// <summary>
        /// Reads all parameter spaces from the device and builds an in-memory snapshot.
        ///
        /// IMPORTANT: Must be called from the UI/STA thread — SDK COM objects have
        /// thread affinity. Do NOT wrap in Task.Run(). Polling via await Task.Delay
        /// keeps the UI responsive (same proven pattern as DeviceConnectionService).
        /// </summary>
        public async Task<DeviceSettingsSnapshot> ReadAllSettingsAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSide side,
            IProgress<string>? progress, CancellationToken ct)
        {
            // Backward-compatible entry point: default to Memory 1 (index 0).
            return await ReadMemorySnapshotAsync(product, adaptor, side, memoryIndex: 0, progress, ct);
        }

        public async Task<DeviceSettingsSnapshot> ReadMemorySnapshotAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSide side, int memoryIndex,
            IProgress<string>? progress, CancellationToken ct)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (adaptor == null) throw new ArgumentNullException(nameof(adaptor));
            ValidateMemoryIndex(memoryIndex);

            ct.ThrowIfCancellationRequested();
            progress?.Report($"Reading Memory {memoryIndex + 1} parameters…");

            TrySelectMemoryContext(product, memoryIndex);
            Debug.WriteLine($"[WriteVerify] READ START: side={side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} — reading from device");
            ScanDiagnostics.WriteLine($"[WriteVerify] READ START: side={side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} — reading from device");

            var spacesToRead = new List<ParameterSpace>
            {
                ParameterSpace.kActiveMemory,
                ParameterSpace.kSystemActiveMemory
            };

            long totalTime = 0;
            foreach (var space in spacesToRead)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var sw = Stopwatch.StartNew();
                    var monitor = product.BeginReadParameters(space);
                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(PollIntervalMs, ct);
                    }

                    monitor.GetResult();
                    sw.Stop();
                    totalTime += sw.ElapsedMilliseconds;
                    Debug.WriteLine($"[SoundDesigner] ReadParameters({space}) memory={memoryIndex + 1}: {sw.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException t ? t.InnerException ?? ex : ex;
                    Debug.WriteLine($"[SoundDesigner] ReadParameters({space}) memory={memoryIndex + 1} error: {inner.Message}");
                    if (ScanDiagnostics.IsSdException(ex))
                        ScanDiagnostics.LogSdExceptionDetails(null, ex);
                }
            }

            Debug.WriteLine($"[SoundDesigner] Read memory={memoryIndex + 1} totalMs={totalTime}");
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Building snapshot for Memory {memoryIndex + 1}…");

            var snapshot = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, memoryIndex, side);
            var itemCount = snapshot?.Categories.SelectMany(c => c.Sections.SelectMany(s => s.Items)).Count() ?? 0;
            Debug.WriteLine($"[WriteVerify] READ COMPLETE: side={side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} params={itemCount} totalMs={totalTime}");
            ScanDiagnostics.WriteLine($"[WriteVerify] READ COMPLETE: side={side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} params={itemCount} totalMs={totalTime}");
            progress?.Report("Done.");
            return snapshot;
        }

        // Synchronous version for backward compatibility (called from Dispatcher lambda)
        public DeviceSettingsSnapshot ReadAllSettingsSync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSide side,
            IProgress<string>? progress, CancellationToken ct)
        {
            // Delegate to async version and block — only call from non-UI thread
            return ReadAllSettingsAsync(product, adaptor, side, progress, ct).GetAwaiter().GetResult();
        }

        // =========================================================================
        // WriteParameters — Uses BeginWriteParameters / polling / GetResult
        // =========================================================================

        /// <summary>
        /// Writes parameters to the device and verifies with a read-back.
        ///
        /// IMPORTANT: Must be called from the UI/STA thread — SDK COM objects have
        /// thread affinity. Polling via await Task.Delay keeps the UI responsive.
        /// </summary>
        /// <param name="onWriteFailed">If provided, called with the unwrapped error message when write fails (e.g. E_UNCONFIGURED_DEVICE). Use for user-facing toast.</param>
        /// <param name="selectedMemoryIndex">Optional: current memory index (0-7) for save logging.</param>
        public async Task<bool> WriteSettingsAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot,
            IProgress<string>? progress, CancellationToken ct,
            Action<string>? onWriteFailed = null,
            int? selectedMemoryIndex = null)
        {
            return await WriteMemorySnapshotAsync(
                product,
                adaptor,
                snapshot,
                selectedMemoryIndex ?? 0,
                progress,
                ct,
                onWriteFailed);
        }

        public async Task<bool> WriteMemorySnapshotAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, int memoryIndex,
            IProgress<string>? progress, CancellationToken ct,
            Action<string>? onWriteFailed = null)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (adaptor == null) throw new ArgumentNullException(nameof(adaptor));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ValidateMemoryIndex(memoryIndex);

            ct.ThrowIfCancellationRequested();
            progress?.Report($"Writing Memory {memoryIndex + 1} to device…");

            TrySelectMemoryContext(product, memoryIndex);

            var spacesToWrite = new List<ParameterSpace>
            {
                ParameterSpace.kActiveMemory,
                ParameterSpace.kSystemActiveMemory
            };
            var spacesLog = string.Join(", ", spacesToWrite);
            var memoryLog = (memoryIndex + 1).ToString();
            Debug.WriteLine($"[WriteVerify] BEFORE WRITE: side={snapshot.Side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} targetSpaces=[{spacesLog}]");
            ScanDiagnostics.WriteLine($"[WriteVerify] BEFORE WRITE: side={snapshot.Side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} targetSpaces=[{spacesLog}]");
            Debug.WriteLine($"[SoundDesigner] Save started: memory={memoryLog}, spaces=[{spacesLog}]");
            ScanDiagnostics.WriteLine($"[SoundDesigner] Save started: memory={memoryLog}, spaces=[{spacesLog}]");

            // Rebind to fresh SDK parameter refs for this memory context.
            // Cached snapshots can hold stale refs after memory switches.
            var writeSnapshot = BuildWritableSnapshotForMemory(product, snapshot, memoryIndex);

            // Apply edited snapshot values to the product's Parameter objects before writing.
            // Without this, BeginWriteParameters sends the product's in-memory state (from the last
            // ReadParameters), so user changes are never sent and "return to settings" shows old data.
            SoundDesignerSettingsEnumerator.ApplySnapshotValuesToSdkParameters(writeSnapshot);

            try
            {
                foreach (var space in spacesToWrite)
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = Stopwatch.StartNew();
                    var monitor = product.BeginWriteParameters(space);

                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(PollIntervalMs, ct);
                    }

                    monitor.GetResult(); // throws on SDK error
                    sw.Stop();
                    Debug.WriteLine($"[SoundDesigner] WriteParameters({space}) memory={memoryLog}: {sw.ElapsedMilliseconds} ms");
                    ScanDiagnostics.WriteLine($"[SoundDesigner] WriteParameters({space}) memory={memoryLog}: {sw.ElapsedMilliseconds} ms, result=OK");
                }

                // NVM write (per plan; vendor sample presuite_memory_switch.py: WriteParameters(memoryNumber) → NVM).
                // Discovery runs once and logs ParameterSpace / BeginWriteParameters overloads; then we call NVM write when available.
                DiscoverNvmWriteApi(product);
                if (_nvmWriteAvailable == true)
                {
                    if (_beginWriteParametersInt != null)
                    {
                        ct.ThrowIfCancellationRequested();
                        var nvmMonitor = _beginWriteParametersInt(product, memoryIndex);
                        if (nvmMonitor != null)
                        {
                            var swNvm = Stopwatch.StartNew();
                            try
                            {
                                var isFinishedProp = nvmMonitor.GetType().GetProperty("IsFinished", BindingFlags.Public | BindingFlags.Instance);
                                var getResultMethod = nvmMonitor.GetType().GetMethod("GetResult", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
                                if (isFinishedProp != null && getResultMethod != null)
                                {
                                    while (!(bool)isFinishedProp.GetValue(nvmMonitor)!)
                                    {
                                        ct.ThrowIfCancellationRequested();
                                        await Task.Delay(PollIntervalMs, ct);
                                    }
                                    getResultMethod.Invoke(nvmMonitor, null);
                                    swNvm.Stop();
                                    Debug.WriteLine($"[SoundDesigner] WriteParameters(NVM M{memoryIndex + 1}): {swNvm.ElapsedMilliseconds} ms");
                                    ScanDiagnostics.WriteLine($"[SoundDesigner] WriteParameters(NVM M{memoryIndex + 1}): {swNvm.ElapsedMilliseconds} ms, result=OK");
                                }
                            }
                            catch (Exception ex)
                            {
                                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                                throw new InvalidOperationException($"NVM write failed: {inner.Message}", ex);
                            }
                        }
                    }
                    else if (_nvmSpaceForMemoryIndex != null)
                    {
                        // ParameterSpace path: write kSystemNvmMemory then kNvmMemoryN (per Python read order; write order may vary by SDK).
                        if (_systemNvmSpace != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            var monitorSys = product.BeginWriteParameters(_systemNvmSpace.Value);
                            while (!monitorSys.IsFinished)
                            {
                                ct.ThrowIfCancellationRequested();
                                await Task.Delay(PollIntervalMs, ct);
                            }
                            monitorSys.GetResult();
                            Debug.WriteLine($"[SoundDesigner] WriteParameters(kSystemNvmMemory) memory={memoryLog}: OK");
                        }
                        var nvmSpace = _nvmSpaceForMemoryIndex(memoryIndex);
                        if (nvmSpace != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            var swNvm = Stopwatch.StartNew();
                            var monitorNvm = product.BeginWriteParameters(nvmSpace.Value);
                            while (!monitorNvm.IsFinished)
                            {
                                ct.ThrowIfCancellationRequested();
                                await Task.Delay(PollIntervalMs, ct);
                            }
                            monitorNvm.GetResult();
                            swNvm.Stop();
                            Debug.WriteLine($"[SoundDesigner] WriteParameters({nvmSpace}) memory={memoryLog}: {swNvm.ElapsedMilliseconds} ms");
                            ScanDiagnostics.WriteLine($"[SoundDesigner] WriteParameters(NVM {nvmSpace}) memory={memoryLog}: {swNvm.ElapsedMilliseconds} ms, result=OK");
                        }
                    }
                }

                progress?.Report("Verifying…");

                Debug.WriteLine($"[WriteVerify] WRITE COMPLETE: memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} — starting read-back verification");
                ScanDiagnostics.WriteLine($"[WriteVerify] WRITE COMPLETE: memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} — starting read-back verification");

                // Read back for verification (same memory, same spaces)
                foreach (var space in spacesToWrite)
                {
                    ct.ThrowIfCancellationRequested();
                    var monitor = product.BeginReadParameters(space);
                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(PollIntervalMs, ct);
                    }
                    monitor.GetResult();
                }

                // Verification: compare written snapshot with device read-back (same memory)
                var (verified, verifyFailure) = SoundDesignerSettingsEnumerator.VerifySnapshotAfterReadBack(writeSnapshot, maxItemsToCheck: 50);
                if (!verified && !string.IsNullOrEmpty(verifyFailure))
                {
                    Debug.WriteLine($"[WriteVerify] VERIFICATION FAILED: side={snapshot.Side} memoryIndex={memoryIndex} reason={verifyFailure}");
                    ScanDiagnostics.WriteLine($"[WriteVerify] VERIFICATION FAILED: side={snapshot.Side} memoryIndex={memoryIndex} reason={verifyFailure}");
                    Debug.WriteLine($"[SoundDesigner] Save verification failed: {verifyFailure}");
                    ScanDiagnostics.WriteLine($"[SoundDesigner] Save verification failed: {verifyFailure}");
                    onWriteFailed?.Invoke(verifyFailure);
                    progress?.Report("Verification failed.");
                    return false;
                }

                Debug.WriteLine($"[WriteVerify] VERIFICATION PASSED: side={snapshot.Side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} — device write confirmed");
                ScanDiagnostics.WriteLine($"[WriteVerify] VERIFICATION PASSED: side={snapshot.Side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} — device write confirmed");
                progress?.Report("Saved.");
                ScanDiagnostics.WriteLine("[SoundDesigner] Save completed: result=success");
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                var message = inner.Message ?? ex.Message ?? "Unknown error.";
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(null, ex);
                ScanDiagnostics.WriteLine($"[SoundDesigner] WriteToDevice failed: {message}");
                Debug.WriteLine($"[SoundDesigner] WriteToDevice failed: {message}");
                onWriteFailed?.Invoke(message);
                progress?.Report("Write failed.");
                return false;
            }
        }

        // =========================================================================
        // NVM-only API — Semantics per SDK sample presuite_memory_switch.py
        // (Read and Write NVM testing; Active memory and memory switching).
        // Restore = set CurrentMemory + ReadParameters (NVM→RAM); Burn = WriteParameters(memory).
        // =========================================================================

        /// <summary>NVM-only: Restore from NVM then read from RAM for one memory. Logs [NVM] Restore M#.</summary>
        public async Task<DeviceSettingsSnapshot> ReloadFromNvmAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSide side, int memoryIndex,
            IProgress<string>? progress, CancellationToken ct)
        {
            ValidateMemoryIndex(memoryIndex);
            Debug.WriteLine($"[NVM] Restore M{memoryIndex + 1}");
            ScanDiagnostics.WriteLine($"[NVM] Restore M{memoryIndex + 1}");
            return await ReadMemorySnapshotAsync(product, adaptor, side, memoryIndex, progress, ct);
        }

        /// <summary>NVM-only: Burn snapshot to NVM for one memory. Logs [NVM] Burn M#.</summary>
        public async Task<bool> BurnMemoryToNvmAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, int memoryIndex,
            IProgress<string>? progress, CancellationToken ct,
            Action<string>? onWriteFailed = null)
        {
            ValidateMemoryIndex(memoryIndex);
            Debug.WriteLine($"[NVM] Burn M{memoryIndex + 1}");
            ScanDiagnostics.WriteLine($"[NVM] Burn M{memoryIndex + 1}");
            return await WriteMemorySnapshotAsync(product, adaptor, snapshot, memoryIndex, progress, ct, onWriteFailed);
        }

        /// <summary>NVM-only: Reload from NVM then compare key values. Logs [NVM] Verify M# OK/FAIL.</summary>
        public async Task<(bool Verified, string? FailureMessage)> VerifyMemoryMatchesNvmAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, int memoryIndex,
            int maxItemsToCheck, CancellationToken ct)
        {
            ValidateMemoryIndex(memoryIndex);
            try
            {
                Debug.WriteLine($"[WriteVerify] VERIFY NVM: side={snapshot.Side} memoryIndex={memoryIndex} — reloading from device then comparing");
                ScanDiagnostics.WriteLine($"[WriteVerify] VERIFY NVM: side={snapshot.Side} memoryIndex={memoryIndex} — reloading from device then comparing");
                var afterReload = await ReloadFromNvmAsync(product, adaptor, snapshot.Side, memoryIndex, null, ct);
                var (verified, failureMessage) = SoundDesignerSettingsEnumerator.VerifySnapshotAfterReadBack(snapshot, afterReload, maxItemsToCheck);
                if (verified)
                {
                    Debug.WriteLine($"[WriteVerify] VERIFY NVM PASSED: side={snapshot.Side} memoryIndex={memoryIndex} — values match after reconnect read");
                    ScanDiagnostics.WriteLine($"[WriteVerify] VERIFY NVM PASSED: side={snapshot.Side} memoryIndex={memoryIndex} — values match after reconnect read");
                    Debug.WriteLine($"[NVM] Verify M{memoryIndex + 1} OK");
                    ScanDiagnostics.WriteLine($"[NVM] Verify M{memoryIndex + 1} OK");
                }
                else
                {
                    Debug.WriteLine($"[WriteVerify] VERIFY NVM FAILED: side={snapshot.Side} memoryIndex={memoryIndex} reason={failureMessage}");
                    ScanDiagnostics.WriteLine($"[WriteVerify] VERIFY NVM FAILED: side={snapshot.Side} memoryIndex={memoryIndex} reason={failureMessage}");
                    Debug.WriteLine($"[NVM] Verify M{memoryIndex + 1} FAIL: {failureMessage}");
                    ScanDiagnostics.WriteLine($"[NVM] Verify M{memoryIndex + 1} FAIL: {failureMessage}");
                }
                return (verified, failureMessage);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                var msg = inner.Message ?? ex.Message ?? "Unknown error.";
                Debug.WriteLine($"[NVM] Verify M{memoryIndex + 1} FAIL: {msg}");
                ScanDiagnostics.WriteLine($"[NVM] Verify M{memoryIndex + 1} FAIL: {msg}");
                return (false, msg);
            }
        }

        private static DeviceSettingsSnapshot BuildWritableSnapshotForMemory(IProduct product, DeviceSettingsSnapshot sourceSnapshot, int memoryIndex)
        {
            try
            {
                var fresh = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, memoryIndex, sourceSnapshot.Side);
                var sourceValuesById = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var cat in sourceSnapshot.Categories)
                {
                    foreach (var sec in cat.Sections)
                    {
                        foreach (var item in sec.Items)
                        {
                            if (!string.IsNullOrEmpty(item.Id))
                                sourceValuesById[item.Id] = item.Value;
                        }
                    }
                }

                int mapped = 0;
                foreach (var cat in fresh.Categories)
                {
                    foreach (var sec in cat.Sections)
                    {
                        foreach (var item in sec.Items)
                        {
                            if (!string.IsNullOrEmpty(item.Id) && sourceValuesById.TryGetValue(item.Id, out var value))
                            {
                                item.Value = value;
                                mapped++;
                            }
                        }
                    }
                }

                Debug.WriteLine($"[SoundDesigner] Rebound writable snapshot for memory={memoryIndex + 1}, mappedValues={mapped}");
                return fresh;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SoundDesigner] Rebind writable snapshot failed for memory={memoryIndex + 1}: {ex.Message}");
                return sourceSnapshot;
            }
        }

        private static void ValidateMemoryIndex(int memoryIndex)
        {
            if (memoryIndex < 0 || memoryIndex >= MemoryCount)
                throw new ArgumentOutOfRangeException(nameof(memoryIndex), $"Memory index must be 0..{MemoryCount - 1}.");
        }

        /// <summary>
        /// Tries to switch product active memory context explicitly for SDK correctness.
        /// Different firmware/library combinations can expose either SwitchToMemory(int) or SelectMemoryIndex(int).
        /// </summary>
        private static void TrySelectMemoryContext(IProduct product, int memoryIndex)
        {
            var productType = product.GetType();
            object[] args = { memoryIndex };

            try
            {
                var switchMethod = productType.GetMethod("SwitchToMemory", BindingFlags.Public | BindingFlags.Instance);
                if (switchMethod != null)
                {
                    switchMethod.Invoke(product, args);
                    Debug.WriteLine($"[Memory] SDK context selected via SwitchToMemory({memoryIndex + 1})");
                    return;
                }

                var selectMethod = productType.GetMethod("SelectMemoryIndex", BindingFlags.Public | BindingFlags.Instance);
                if (selectMethod != null)
                {
                    selectMethod.Invoke(product, args);
                    Debug.WriteLine($"[Memory] SDK context selected via SelectMemoryIndex({memoryIndex + 1})");
                    return;
                }

                Debug.WriteLine($"[Memory] SDK context select not exposed; continue with snapshot memory={memoryIndex + 1}");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                Debug.WriteLine($"[Memory] SDK context select failed for memory={memoryIndex + 1}: {inner.Message}");
            }
        }

        // Synchronous version for backward compatibility
        public bool WriteSettingsSync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot,
            IProgress<string>? progress, CancellationToken ct)
        {
            return WriteSettingsAsync(product, adaptor, snapshot, progress, ct).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Writes a single parameter (active memory) to the device.
        ///
        /// IMPORTANT: Must be called from the UI/STA thread.
        /// </summary>
        public async Task WriteParameterAsync(
            IProduct product, ICommunicationAdaptor adaptor, SettingItem item, CancellationToken ct)
        {
            if (product == null || adaptor == null || item == null) return;

            try
            {
                ct.ThrowIfCancellationRequested();

                // Write active memory (contains the parameter we just changed in the SDK cache)
                var monitor = product.BeginWriteParameters(ParameterSpace.kActiveMemory);
                while (!monitor.IsFinished)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(PollIntervalMs, ct);
                }
                monitor.GetResult();
                Debug.WriteLine("[SoundDesigner] WriteParameter (Active) OK");
            }
            catch (Exception ex)
            {
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(null, ex);
                Debug.WriteLine($"[SoundDesigner] WriteParameter: {ex.Message}");
            }
        }
    }
}
