using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Wired discovery of hearing aids via HI-PRO using Sound Designer SDK.
    /// Uses the SHARED SdkManager and runs all sdnet calls through SdkGate (one ProductManager, no concurrent native calls).
    /// </summary>
    public sealed class HiProWiredDiscoveryService
    {
        private const int RetryDelayMinMs = 200;
        private const int RetryDelayMaxMs = 500;
        private const int MaxRetriesForBusy = 2;

        private readonly SdkManager? _sharedSdk;
        private readonly Action<string>? _logDiagnostics;

        /// <summary>Use this constructor: pass the shared SdkManager from DeviceSessionService.EnsureSdkReadyForScanAsync. All sdnet calls run through SdkGate.</summary>
        public HiProWiredDiscoveryService(SdkManager sharedSdk, Action<string>? logDiagnostics = null)
        {
            _sharedSdk = sharedSdk ?? throw new ArgumentNullException(nameof(sharedSdk));
            _logDiagnostics = logDiagnostics;
        }

        [Obsolete("Use constructor that accepts SdkManager. This constructor creates no SDK context and DetectBothAsync will throw.")]
        public HiProWiredDiscoveryService(Action<string>? logDiagnostics = null)
        {
            _sharedSdk = null;
            _logDiagnostics = logDiagnostics;
        }

        [Obsolete("Discovery must use shared SdkManager. The productManager parameter is ignored. Use constructor with SdkManager.")]
        public HiProWiredDiscoveryService(Action<string>? logDiagnostics, IProductManager? productManager)
            : this(logDiagnostics)
        {
        }

        private void Log(string message)
        {
            _logDiagnostics?.Invoke(message);
            Debug.WriteLine($"[HiProWiredDiscovery] {message}");
            ScanDiagnostics.WriteLine($"[HiProWiredDiscovery] {message}");
        }

        /// <summary>
        /// Detects devices on both Left and Right ports. Per-port: one side can fail (e.g. E_SEND_FAILURE) without failing the whole discovery.
        /// Returns a deterministic DiscoveryResult with FoundLeft/FoundRight, device ids, and per-side errors.
        /// Runs on the calling thread (must be STA/UI thread).
        /// </summary>
        public async Task<DiscoveryResult> DetectBothAsync(CancellationToken ct)
        {
            return await DoDetectBothAsync(ct);
        }

        private async Task<DiscoveryResult> DoDetectBothAsync(CancellationToken ct)
        {
            var discoveryResult = new DiscoveryResult();
            var sessionStart = DateTime.UtcNow;
            Log($"=== HiProWiredDiscoveryService.DetectBoth session start {sessionStart:O} ===");

            if (_sharedSdk == null || !_sharedSdk.IsInitialized)
            {
                Log("SDK not available (shared SdkManager null or not initialized).");
                throw new InvalidOperationException("Wired discovery requires shared SdkManager. Use DeviceSessionService.EnsureSdkReadyForScanAsync first.");
            }

            IProductManager pm = _sharedSdk.ProductManager;

            int ifCount = SdkGate.Run("GetInterfaceCount", () => SdkScanHelper.GetCommunicationInterfaceCount(pm));
            Log($"GetCommunicationInterfaceCount() = {ifCount}");

            string? selectedInterfaceName = null;
            for (int i = 0; i < ifCount; i++)
            {
                var iCapture = i;
                var name = SdkGate.Run($"GetInterfaceString_{iCapture}", () => SdkScanHelper.GetCommunicationInterfaceString(pm, iCapture));
                Log($"GetCommunicationInterfaceString({i}) = \"{name ?? "(null)"}\"");
                if (!string.IsNullOrEmpty(name) && string.Equals(name.Trim(), Constants.HiPro, StringComparison.OrdinalIgnoreCase))
                {
                    selectedInterfaceName = name.Trim();
                    Log($"Selected HI-PRO interface (exact match): \"{selectedInterfaceName}\"");
                    break;
                }
                if (!string.IsNullOrEmpty(name) && name.IndexOf("HI-PRO", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selectedInterfaceName = name;
                    Log($"Selected HI-PRO interface (contains match): \"{selectedInterfaceName}\"");
                    break;
                }
            }

            if (string.IsNullOrEmpty(selectedInterfaceName))
            {
                Log("No HI-PRO interface found in SDK list. Using Constants.HiPro.");
                selectedInterfaceName = Constants.HiPro;
            }

            var leftResult = await DetectOnePortAsync(pm, selectedInterfaceName, CommunicationPort.kLeft, ct);
            ct.ThrowIfCancellationRequested();

            DiscoveredDevice rightResult;
            try
            {
                rightResult = await DetectOnePortAsync(pm, selectedInterfaceName, CommunicationPort.kRight, ct);
            }
            catch (Exception ex)
            {
                Log($"[Right] Right-port detection threw unhandled exception (non-fatal): {ex.Message}");
                rightResult = new DiscoveredDevice
                {
                    Port = CommunicationPort.kRight,
                    Side = DeviceSide.Right,
                    Found = false,
                    ErrorCode = "E_RIGHT_EXCEPTION",
                    ErrorMessage = ex.Message
                };
            }

            discoveryResult.FoundLeft = leftResult.Found;
            discoveryResult.FoundRight = rightResult.Found;
            if (leftResult.Found)
            {
                discoveryResult.LeftSerialId = leftResult.SerialId;
                discoveryResult.LeftFirmwareId = leftResult.FirmwareId;
                discoveryResult.LeftProductId = leftResult.ProductId;
                discoveryResult.LeftChipId = leftResult.ChipId;
                discoveryResult.LeftHybridId = leftResult.HybridId;
                discoveryResult.LeftHybridSerial = leftResult.HybridSerial;
                discoveryResult.LeftParameterLockState = leftResult.ParameterLockState;
            }
            else if (!string.IsNullOrEmpty(leftResult.ErrorCode) || !string.IsNullOrEmpty(leftResult.ErrorMessage))
                discoveryResult.Errors.Add(new DiscoveryError { Side = "Left", ErrorCode = leftResult.ErrorCode ?? "", Message = leftResult.ErrorMessage ?? "" });
            if (rightResult.Found)
            {
                discoveryResult.RightSerialId = rightResult.SerialId;
                discoveryResult.RightFirmwareId = rightResult.FirmwareId;
                discoveryResult.RightProductId = rightResult.ProductId;
                discoveryResult.RightChipId = rightResult.ChipId;
                discoveryResult.RightHybridId = rightResult.HybridId;
                discoveryResult.RightHybridSerial = rightResult.HybridSerial;
                discoveryResult.RightParameterLockState = rightResult.ParameterLockState;
            }
            else if (!string.IsNullOrEmpty(rightResult.ErrorCode) || !string.IsNullOrEmpty(rightResult.ErrorMessage))
                discoveryResult.Errors.Add(new DiscoveryError { Side = "Right", ErrorCode = rightResult.ErrorCode ?? "", Message = rightResult.ErrorMessage ?? "" });

            var sessionEnd = DateTime.UtcNow;
            Log($"=== HiProWiredDiscoveryService.DetectBoth session end {sessionEnd:O} duration={(sessionEnd - sessionStart).TotalMilliseconds:F0} ms FoundLeft={discoveryResult.FoundLeft} FoundRight={discoveryResult.FoundRight} ===");

            return discoveryResult;
        }

        private async Task<DiscoveredDevice> DetectOnePortAsync(
            IProductManager productManager,
            string interfaceName,
            CommunicationPort port,
            CancellationToken ct)
        {
            var portLabel = port == CommunicationPort.kLeft ? "Left" : "Right";
            var start = DateTime.UtcNow;
            Log($"[{portLabel}] --- port detection start {start:O} ---");

            for (int attempt = 0; attempt <= MaxRetriesForBusy; attempt++)
            {
                if (attempt > 0)
                {
                    var delay = Random.Shared.Next(RetryDelayMinMs, RetryDelayMaxMs + 1);
                    Log($"[{portLabel}] Retry {attempt}/{MaxRetriesForBusy} after {delay} ms (ALREADY_OPEN/BUSY)");
                    await Task.Delay(delay, ct);
                }

                ICommunicationAdaptor? adaptor = null;
                object? monitor = null; // BeginDetectDevice returns SDK type (IAsyncResult-like) with IsFinished, GetResult
                try
                {
                    adaptor = SdkGate.Run($"CreateAdaptor_{portLabel}", () => productManager.CreateCommunicationInterface(interfaceName, port, ""));
                    if (adaptor == null)
                    {
                        var err = SdkGate.Run($"GetError_{portLabel}", () => SdkScanHelper.GetDetailedErrorString(productManager));
                        Log($"[{portLabel}] CreateCommunicationInterface returned null. GetDetailedErrorString: {err}");
                        var fail = new DiscoveredDevice
                        {
                            Port = port,
                            Side = port == CommunicationPort.kLeft ? DeviceSide.Left : DeviceSide.Right,
                            Found = false,
                            ErrorCode = "E_CREATE_FAILED",
                            ErrorMessage = "CreateCommunicationInterface returned null. " + (err ?? "")
                        };
                        LogResult(portLabel, start, fail, null);
                        return fail;
                    }

                    Log($"[{portLabel}] Adaptor created; calling BeginDetectDevice");
                    monitor = SdkGate.Run($"BeginDetect_{portLabel}", () => adaptor.BeginDetectDevice());
                    if (monitor == null)
                    {
                        SdkGate.Run($"CloseAdaptor_{portLabel}_null", () => CloseAdaptorSync(adaptor, portLabel));
                        var fail = new DiscoveredDevice
                        {
                            Port = port,
                            Side = port == CommunicationPort.kLeft ? DeviceSide.Left : DeviceSide.Right,
                            Found = false,
                            ErrorCode = "E_BEGIN_DETECT",
                            ErrorMessage = "BeginDetectDevice returned null"
                        };
                        LogResult(portLabel, start, fail, null);
                        return fail;
                    }

                    // Poll IsFinished (SDK type has IsFinished property)
                    var monitorRef = monitor;
                    while (!((dynamic)monitorRef).IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(300, ct);
                    }

                    IDeviceInfo? sdkDeviceInfo = SdkGate.Run($"EndDetect_{portLabel}", () =>
                    {
                        ((dynamic)monitorRef).GetResult();
                        var info = adaptor.EndDetectDevice((SDLib.IAsyncResult)monitorRef);
                        CloseAdaptorSync(adaptor, portLabel);
                        return info;
                    });
                    adaptor = null;

                    if (sdkDeviceInfo == null)
                    {
                        var result = new DiscoveredDevice { Port = port, Side = port == CommunicationPort.kLeft ? DeviceSide.Left : DeviceSide.Right, Found = false, ErrorCode = "E_NO_DEVICE", ErrorMessage = "No device detected" };
                        LogResult(portLabel, start, result, null);
                        return result;
                    }

                    var success = new DiscoveredDevice
                    {
                        Port = port,
                        Side = port == CommunicationPort.kLeft ? DeviceSide.Left : DeviceSide.Right,
                        Found = true,
                        FirmwareId = sdkDeviceInfo.FirmwareId.ToString(),
                        ProductId = sdkDeviceInfo.ProductId != 0 ? sdkDeviceInfo.ProductId.ToString() : null,
                        SerialId = sdkDeviceInfo.SerialId != 0 ? sdkDeviceInfo.SerialId.ToString() : null,
                        ChipId = sdkDeviceInfo.ChipId != 0 ? sdkDeviceInfo.ChipId.ToString() : null,
                        HybridId = sdkDeviceInfo.HybridId != 0 ? sdkDeviceInfo.HybridId.ToString() : null,
                        HybridSerial = sdkDeviceInfo.HybridSerial != 0 ? sdkDeviceInfo.HybridSerial.ToString() : null,
                        ParameterLockState = sdkDeviceInfo.ParameterLockState
                    };
                    LogResult(portLabel, start, success, sdkDeviceInfo);
                    return success;
                }
                catch (Exception ex)
                {
                    if (adaptor != null)
                        SdkGate.Run($"CloseAdaptor_{portLabel}_ex", () => CloseAdaptorSync(adaptor, portLabel));
                    adaptor = null;

                    if (ScanDiagnostics.IsSdException(ex))
                        ScanDiagnostics.LogSdExceptionDetails(productManager, ex);

                    var errorCode = GetErrorCode(ex);
                    var isBusy = IsAlreadyOpenOrBusy(ex);
                    if (isBusy && attempt < MaxRetriesForBusy)
                        continue;

                    var fail = new DiscoveredDevice
                    {
                        Port = port,
                        Side = port == CommunicationPort.kLeft ? DeviceSide.Left : DeviceSide.Right,
                        Found = false,
                        ErrorCode = errorCode,
                        ErrorMessage = ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : "")
                    };
                    LogResult(portLabel, start, fail, null);
                    Log($"[{portLabel}] Exception details: {ex}");
                    return fail;
                }
            }

            var finalFail = new DiscoveredDevice
            {
                Port = port,
                Side = port == CommunicationPort.kLeft ? DeviceSide.Left : DeviceSide.Right,
                Found = false,
                ErrorCode = "E_BUSY_RETRIES",
                ErrorMessage = "ALREADY_OPEN/BUSY after max retries"
            };
            LogResult(portLabel, start, finalFail, null);
            return finalFail;
        }

        private void CloseAdaptorSync(ICommunicationAdaptor? adaptor, string portLabel)
        {
            if (adaptor == null) return;
            try
            {
                adaptor.CloseDevice();
                Log($"[{portLabel}] CloseDevice() completed");
            }
            catch (Exception ex)
            {
                Log($"[{portLabel}] CloseDevice: {ex.Message}");
            }
        }

        private void LogResult(string portLabel, DateTime start, DiscoveredDevice result, IDeviceInfo? sdkInfo)
        {
            var end = DateTime.UtcNow;
            var durationMs = (end - start).TotalMilliseconds;
            Log($"[{portLabel}] --- port detection end {end:O} duration={durationMs:F0} ms Found={result.Found} ---");
            if (result.Found)
            {
                Log($"[{portLabel}] DeviceInfo: FirmwareId={result.FirmwareId} ProductId={result.ProductId} SerialId={result.SerialId} ChipId={result.ChipId}");
                if (sdkInfo != null)
                    Log($"[{portLabel}] SDK DeviceInfo: FirmwareId={sdkInfo.FirmwareId} ProductId={sdkInfo.ProductId} SerialId={sdkInfo.SerialId} ChipId={sdkInfo.ChipId}");
            }
            else
                Log($"[{portLabel}] Error: ErrorCode={result.ErrorCode} ErrorMessage={result.ErrorMessage}");
        }

        private static string GetErrorCode(Exception ex)
        {
            var msg = (ex.Message ?? "") + " " + (ex.InnerException?.Message ?? "");
            var full = ex.ToString() ?? "";
            if (full.IndexOf("E_SEND_FAILURE", StringComparison.OrdinalIgnoreCase) >= 0) return "E_SEND_FAILURE";
            if (full.IndexOf("E_INVALID_STATE", StringComparison.OrdinalIgnoreCase) >= 0) return "E_INVALID_STATE";
            if (full.IndexOf("E_NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0) return "E_NOT_FOUND";
            if (full.IndexOf("E_UNKNOWN_NAME", StringComparison.OrdinalIgnoreCase) >= 0) return "E_UNKNOWN_NAME";
            if (full.IndexOf("E_NOT_DETECTED", StringComparison.OrdinalIgnoreCase) >= 0) return "E_NOT_DETECTED";
            if (msg.IndexOf("E_SEND_FAILURE", StringComparison.OrdinalIgnoreCase) >= 0) return "E_SEND_FAILURE";
            if (msg.IndexOf("sending data", StringComparison.OrdinalIgnoreCase) >= 0) return "E_SEND_FAILURE";
            if (msg.IndexOf("E_INVALID_STATE", StringComparison.OrdinalIgnoreCase) >= 0) return "E_INVALID_STATE";
            if (msg.IndexOf("E_NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0) return "E_NOT_FOUND";
            return "E_SD_EXCEPTION";
        }

        /// <summary>Returns user-facing message for normalized error code (for UI / logging).</summary>
        public static string GetUserMessageForErrorCode(string errorCode, string side)
        {
            return errorCode switch
            {
                "E_SEND_FAILURE" => $"Communication error on {side} port. Check cable, seating, and contacts.",
                "E_NOT_FOUND" => $"No device on HI-PRO {side} port.",
                "E_NOT_DETECTED" => $"No hearing aid detected on {side} port.",
                "E_INVALID_STATE" => "Another application may be using HI-PRO. Close other fitting software and try again.",
                "E_UNKNOWN_NAME" => "HI-PRO driver or CTK not found. Install HI-PRO driver and CTK Runtime (x86).",
                _ => string.IsNullOrEmpty(errorCode) ? "" : $"Error on {side}: {errorCode}"
            };
        }

        private static bool IsAlreadyOpenOrBusy(Exception ex)
        {
            var msg = (ex.Message ?? "") + (ex.InnerException?.Message ?? "") + (ex.ToString() ?? "");
            return msg.IndexOf("E_INVALID_STATE", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("ALREADY_OPEN", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("busy", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("already open", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
