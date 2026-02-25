using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            ct.ThrowIfCancellationRequested();
            progress?.Report("Reading device parameters…");

            // Read each ParameterSpace using the async Begin/End pattern (Programmer's Guide Section 6.6)
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

                    // Begin* runs on the calling (STA) thread — quick, non-blocking.
                    var monitor = product.BeginReadParameters(space);

                    // Poll with await Task.Delay — yields to Dispatcher, keeps UI responsive.
                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(PollIntervalMs, ct);
                    }

                    monitor.GetResult(); // throws on SDK error
                    sw.Stop();
                    totalTime += sw.ElapsedMilliseconds;
                    Debug.WriteLine($"[SoundDesigner] ReadParameters({space}): {sw.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException t ? t.InnerException ?? ex : ex;
                    Debug.WriteLine($"[SoundDesigner] ReadParameters({space}) error: {inner.Message}");

                    if (ScanDiagnostics.IsSdException(ex))
                        ScanDiagnostics.LogSdExceptionDetails(null, ex);

                    // Continue with other spaces — partial read is better than no read
                }
            }

            Debug.WriteLine($"[SoundDesigner] Read All Memories: {totalTime} ms");

            ct.ThrowIfCancellationRequested();
            progress?.Report("Building parameter list…");

            // Build snapshot for ONE memory only (Memory 0 = 595 params).
            // BuildFullSnapshot would enumerate all 8 memories (4760 params) and cause
            // thousands of reflection/TargetInvocationExceptions — major cause of "so late in loading".
            // Must stay on the STA thread (SDK COM objects).
            var snapshot = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, 0, side);
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
            ct.ThrowIfCancellationRequested();
            progress?.Report("Writing to device…");

            var spacesToWrite = new List<ParameterSpace>
            {
                ParameterSpace.kActiveMemory,
                ParameterSpace.kSystemActiveMemory
            };
            var spacesLog = string.Join(", ", spacesToWrite);
            var memoryLog = selectedMemoryIndex.HasValue ? (selectedMemoryIndex.Value + 1).ToString() : "?";
            Debug.WriteLine($"[SoundDesigner] Save started: memory={memoryLog}, spaces=[{spacesLog}]");
            ScanDiagnostics.WriteLine($"[SoundDesigner] Save started: memory={memoryLog}, spaces=[{spacesLog}]");

            // Apply edited snapshot values to the product's Parameter objects before writing.
            // Without this, BeginWriteParameters sends the product's in-memory state (from the last
            // ReadParameters), so user changes are never sent and "return to settings" shows old data.
            SoundDesignerSettingsEnumerator.ApplySnapshotValuesToSdkParameters(snapshot);

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
                    Debug.WriteLine($"[SoundDesigner] WriteParameters({space}): {sw.ElapsedMilliseconds} ms");
                    ScanDiagnostics.WriteLine($"[SoundDesigner] WriteParameters({space}): {sw.ElapsedMilliseconds} ms, result=OK");
                }

                progress?.Report("Verifying…");

                // Read back for verification
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

                // Optional verification: compare a subset of parameters with current product state
                var (verified, verifyFailure) = SoundDesignerSettingsEnumerator.VerifySnapshotAfterReadBack(snapshot, maxItemsToCheck: 50);
                if (!verified && !string.IsNullOrEmpty(verifyFailure))
                {
                    Debug.WriteLine($"[SoundDesigner] Save verification failed: {verifyFailure}");
                    ScanDiagnostics.WriteLine($"[SoundDesigner] Save verification failed: {verifyFailure}");
                    onWriteFailed?.Invoke(verifyFailure);
                    progress?.Report("Verification failed.");
                    return false;
                }

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
