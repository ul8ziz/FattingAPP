using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ul8ziz.FittingApp.App.ViewModels;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Runs session end flow: stop live mode, optional save to device, disconnect, then invoke callbacks for toast and navigate. SDK work off UI thread.</summary>
    public static class SessionEndService
    {
        /// <summary>Unwrap TargetInvocationException so logs and UI show the real error.</summary>
        private static string GetDisplayMessage(Exception ex)
        {
            if (ex is TargetInvocationException tie && tie.InnerException != null)
                return tie.InnerException.Message ?? ex.Message;
            return ex.Message ?? "Unknown error.";
        }

        private static Exception? GetInner(Exception ex)
        {
            return ex is TargetInvocationException tie ? tie.InnerException : null;
        }

        /// <summary>True if the device serial indicates unprogrammed (SDK will return E_UNCONFIGURED_DEVICE on write).</summary>
        private static bool IsUnprogrammed(string? serialId)
        {
            return string.IsNullOrEmpty(serialId) || serialId == "-1" || serialId == "0";
        }

        /// <summary>Runs on UI/STA thread. Saves all memories (optionally only dirty ones).</summary>
        private static async Task<(bool Success, string? FailureReason)> RunSaveAllOnDispatcherAsync(DeviceSessionService session, bool onlyDirty)
        {
            var product = session.SdkManager?.GetProduct();
            var conn = session.ConnectionService;
            if (product == null || conn == null)
            {
                ScanDiagnostics.WriteLine("[SessionEnd] SaveToDevice skipped (no product or connection).");
                return (false, "No product or connection.");
            }

            var appState = AppSessionState.Instance;
            bool leftUnprogrammed = session.LeftConnected && IsUnprogrammed(appState.LeftSerialId);
            bool rightUnprogrammed = session.RightConnected && IsUnprogrammed(appState.RightSerialId);
            if (leftUnprogrammed || rightUnprogrammed)
            {
                if (leftUnprogrammed) System.Diagnostics.Debug.WriteLine("SessionEnd: SaveToDevice (Left) skipped — unprogrammed device.");
                if (rightUnprogrammed) System.Diagnostics.Debug.WriteLine("SessionEnd: SaveToDevice (Right) skipped — unprogrammed device.");
                ScanDiagnostics.WriteLine("[SessionEnd] SaveToDevice skipped (unprogrammed device).");
                return (false, "Device(s) unprogrammed (Serial=-1 or 0); save to device not available. Use a programmed device or manufacturing tools.");
            }

            // Gate: ensure device is initialized and configured before WriteParameters.
            bool leftNeedsSave = session.LeftConnected;
            bool rightNeedsSave = session.RightConnected;
            if (leftNeedsSave)
            {
                try
                {
                    await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(session, DeviceSide.Left, default);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SessionEnd] EnsureInitializedAndConfigured(Left) failed: {ex.Message}");
                    ScanDiagnostics.WriteLine($"[SessionEnd] EnsureInitializedAndConfigured(Left) failed: " + ex.Message);
                    return (false, session.LastConfigError ?? ex.Message);
                }
            }
            if (rightNeedsSave)
            {
                try
                {
                    await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(session, DeviceSide.Right, default);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SessionEnd] EnsureInitializedAndConfigured(Right) failed: {ex.Message}");
                    ScanDiagnostics.WriteLine($"[SessionEnd] EnsureInitializedAndConfigured(Right) failed: " + ex.Message);
                    return (false, session.LastConfigError ?? ex.Message);
                }
            }

            bool leftConfigured = !leftNeedsSave || session.IsSideConfigured(DeviceSide.Left);
            bool rightConfigured = !rightNeedsSave || session.IsSideConfigured(DeviceSide.Right);
            if (!leftConfigured || !rightConfigured)
            {
                if (!leftConfigured) System.Diagnostics.Debug.WriteLine("SessionEnd: SaveToDevice (Left) skipped — device not configured.");
                if (!rightConfigured) System.Diagnostics.Debug.WriteLine("SessionEnd: SaveToDevice (Right) skipped — device not configured.");
                ScanDiagnostics.WriteLine("[SessionEnd] SaveToDevice skipped (device not configured).");
                return (false, session.LastConfigError ?? "Device not configured; cannot save. Ensure the device has been initialized for fitting.");
            }

            var soundDesigner = new SoundDesignerService();
            string? failureReason = null;
            var allOk = true;
            System.Diagnostics.Debug.WriteLine($"[NVM] SaveAll onlyDirty={onlyDirty.ToString().ToLowerInvariant()} totalDirty={session.GetDirtyMemoryCount()}");

            for (int memoryIndex = 0; memoryIndex < 8; memoryIndex++)
            {
                bool leftDirty = session.IsMemoryDirty(DeviceSide.Left, memoryIndex);
                bool rightDirty = session.IsMemoryDirty(DeviceSide.Right, memoryIndex);
                if (onlyDirty && !leftDirty && !rightDirty)
                    continue;

                if (leftNeedsSave && (!onlyDirty || leftDirty))
                {
                    var (leftSnap, _) = session.GetSnapshotsForMemory(memoryIndex);
                    if (leftSnap != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"SessionEnd: SaveToDevice (Left) memory={memoryIndex + 1}");
                        var adaptor = conn.GetConnection(DeviceSide.Left);
                        if (adaptor != null)
                        {
                            var ok = await soundDesigner.BurnMemoryToNvmAsync(product, adaptor, leftSnap, memoryIndex, null, default, onWriteFailed: msg => failureReason = msg ?? failureReason);
                            if (ok)
                            {
                                var (verified, _) = await soundDesigner.VerifyMemoryMatchesNvmAsync(product, adaptor, leftSnap, memoryIndex, maxItemsToCheck: 20, default);
                                if (!verified) { failureReason = failureReason ?? "Verify failed (Left M" + (memoryIndex + 1) + ")"; ok = false; }
                            }
                            if (!ok) allOk = false;
                            if (ok) session.ClearMemoryDirty(DeviceSide.Left, memoryIndex);
                        }
                    }
                }

                if (rightNeedsSave && (!onlyDirty || rightDirty))
                {
                    var (_, rightSnap) = session.GetSnapshotsForMemory(memoryIndex);
                    if (rightSnap != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"SessionEnd: SaveToDevice (Right) memory={memoryIndex + 1}");
                        var adaptor = conn.GetConnection(DeviceSide.Right);
                        if (adaptor != null)
                        {
                            var ok = await soundDesigner.BurnMemoryToNvmAsync(product, adaptor, rightSnap, memoryIndex, null, default, onWriteFailed: msg => failureReason = msg ?? failureReason);
                            if (ok)
                            {
                                var (verified, _) = await soundDesigner.VerifyMemoryMatchesNvmAsync(product, adaptor, rightSnap, memoryIndex, maxItemsToCheck: 20, default);
                                if (!verified) { failureReason = failureReason ?? "Verify failed (Right M" + (memoryIndex + 1) + ")"; ok = false; }
                            }
                            if (!ok) allOk = false;
                            if (ok) session.ClearMemoryDirty(DeviceSide.Right, memoryIndex);
                        }
                    }
                }
            }
            return (allOk, failureReason);
        }

        public static async Task ExecuteEndSessionAsync(
            EndSessionDialogResult result,
            Action<string> showToast,
            Action navigateToConnect,
            Dispatcher dispatcher,
            Action? showWaitingDialog = null,
            Action? hideWaitingDialog = null)
        {
            if (result == EndSessionDialogResult.Cancel)
                return;

            var session = DeviceSessionService.Instance;
            var saveToDevice = result == EndSessionDialogResult.SaveAndEnd;

            try
            {
                if (saveToDevice && showWaitingDialog != null)
                {
                    try { await dispatcher.InvokeAsync(showWaitingDialog); }
                    catch (Exception ex)
                    {
                        var inner = GetInner(ex) ?? ex;
                        System.Diagnostics.Debug.WriteLine($"SessionEnd: ShowWaitingDialog failed: {inner.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine("SessionEnd: StopLiveMode");
                session.NotifyStopLiveMode();
                await Task.Delay(150).ConfigureAwait(false);

                // Save to device must run on the UI/STA thread — SDK COM objects have thread affinity.
                // InvokeAsync(Func<Task<...>>) so we await the full async save and get an explicit result (no closure).
                var saveSucceeded = false;
                string? saveFailureReason = null;
                if (saveToDevice)
                {
                    System.Diagnostics.Debug.WriteLine("[EndSession] SaveAndEnd => SaveAllMemoriesToNvm onlyDirty=true");
                    try
                    {
                        var resultTask = dispatcher.InvokeAsync(() => RunSaveAllOnDispatcherAsync(session, onlyDirty: true)).Task;
                        var (success, reason) = await await resultTask;
                        saveSucceeded = success;
                        saveFailureReason = reason;
                    }
                    catch (Exception ex)
                    {
                        saveFailureReason = saveFailureReason ?? GetDisplayMessage(ex);
                        System.Diagnostics.Debug.WriteLine($"SessionEnd: SaveToDevice error: {saveFailureReason}");
                        ScanDiagnostics.WriteLine($"[SessionEnd] SaveToDevice failed: {saveFailureReason}");
                    }
                }

                // On save failure: do not report session ended; show error, then disconnect and navigate so user can reselect headset.
                if (saveToDevice && !saveSucceeded)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        hideWaitingDialog?.Invoke();
                        string errorMessage = string.IsNullOrEmpty(saveFailureReason)
                            ? "Save to device failed. Please select the headset again and reconnect."
                            : "Save to device failed: " + saveFailureReason + " Please select the headset again and reconnect.";
                        showToast(errorMessage);
                        System.Diagnostics.Debug.WriteLine("SessionEnd: Save failed — disconnecting and navigating to Connect (connection inactive).");
                    });
                    await session.ClearSessionAsync().ConfigureAwait(false);
                    AppSessionState.Instance.SetNotConnected();
                    await dispatcher.InvokeAsync(() =>
                    {
                        AppSessionState.Instance.RequestRestartDiscovery = true;
                        navigateToConnect();
                        System.Diagnostics.Debug.WriteLine("SessionEnd: NavigateToConnect after save failure");
                    });
                    return;
                }

                // Allow device time to flush WriteParameters to NVM before disconnecting.
                if (saveToDevice && saveSucceeded)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("[SessionEnd] Post-save delay complete (500 ms)");
                }

                System.Diagnostics.Debug.WriteLine("SessionEnd: Disconnect (safe teardown)");
                await session.ClearSessionAsync().ConfigureAwait(false);
                AppSessionState.Instance.SetNotConnected();

                await dispatcher.InvokeAsync(() =>
                {
                    hideWaitingDialog?.Invoke();
                    string toastMessage;
                    if (saveToDevice)
                        toastMessage = "Saved to device. Session ended.";
                    else
                        toastMessage = "Session ended without saving.";
                    showToast(toastMessage);
                    AppSessionState.Instance.RequestRestartDiscovery = true;
                    navigateToConnect();
                    System.Diagnostics.Debug.WriteLine("SessionEnd: RestartDiscovery");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionEnd error: {ex.Message}");
                await dispatcher.InvokeAsync(() =>
                {
                    hideWaitingDialog?.Invoke();
                    showToast("Session end error: " + GetDisplayMessage(ex));
                });
                try
                {
                    await session.ClearSessionAsync().ConfigureAwait(false);
                }
                catch (Exception clearEx)
                {
                    System.Diagnostics.Debug.WriteLine($"SessionEnd ClearSessionAsync error: {clearEx.Message}");
                }
                AppSessionState.Instance.SetNotConnected();
                await dispatcher.InvokeAsync(() => navigateToConnect());
            }
        }
    }
}
