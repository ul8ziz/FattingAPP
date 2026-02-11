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
    /// Wired discovery of hearing aids via HI-PRO using Sound Designer SDK exactly as in the Programmer's Guide:
    /// Section 5.1 GetCommunicationInterfaceCount / GetCommunicationInterfaceString,
    /// Section 5.2 CreateCommunicationInterface(interface_name, port, ""),
    /// Section 5.3.2 BeginDetectDevice / EndDetectDevice,
    /// Section 5.3.5 Close() after each port. Sequential Left then Right; no parallel opens.
    /// </summary>
    public sealed class HiProWiredDiscoveryService
    {
        private const int RetryDelayMinMs = 200;
        private const int RetryDelayMaxMs = 500;
        private const int MaxRetriesForBusy = 2;

        private readonly Action<string>? _logDiagnostics;

        public HiProWiredDiscoveryService(Action<string>? logDiagnostics = null)
        {
            _logDiagnostics = logDiagnostics;
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
        /// </summary>
        public Task<DiscoveryResult> DetectBothAsync(CancellationToken ct)
        {
            return Task.Run(() => RunOnStaThread(() => DoDetectBoth(ct)), ct);
        }

        private static T RunOnStaThread<T>(Func<T> func)
        {
            T? result = default;
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try { result = func(); }
                catch (Exception ex) { captured = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (captured != null)
                throw new InvalidOperationException("Discovery failed.", captured);
            return result!;
        }

        private DiscoveryResult DoDetectBoth(CancellationToken ct)
        {
            var discoveryResult = new DiscoveryResult();
            var sessionStart = DateTime.UtcNow;
            Log($"=== HiProWiredDiscoveryService.DetectBoth session start {sessionStart:O} ===");

            SdkManager? sdk = null;
            try
            {
                Log("Setting up SDK environment (sd.config, .library)");
                SdkConfiguration.SetupEnvironment();
                sdk = new SdkManager();
                sdk.Initialize();
            }
            catch (Exception ex)
            {
                Log($"SDK init failed (fatal): {ex.Message}");
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(null, ex);
                throw new InvalidOperationException("SDK initialization failed. Check sd.config and .library.", ex);
            }

            var pm = sdk!.ProductManager;
            int ifCount = SdkScanHelper.GetCommunicationInterfaceCount(pm);
            Log($"GetCommunicationInterfaceCount() = {ifCount}");

            string? selectedInterfaceName = null;
            for (int i = 0; i < ifCount; i++)
            {
                var name = SdkScanHelper.GetCommunicationInterfaceString(pm, i);
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

            // Sequential: Left -> Close -> Right -> Close (Programmer's Guide 5.3.5)
            var leftResult = DetectOnePort(pm, selectedInterfaceName, CommunicationPort.kLeft, ct);
            ct.ThrowIfCancellationRequested();
            var rightResult = DetectOnePort(pm, selectedInterfaceName, CommunicationPort.kRight, ct);

            discoveryResult.FoundLeft = leftResult.Found;
            discoveryResult.FoundRight = rightResult.Found;
            if (leftResult.Found)
            {
                discoveryResult.LeftSerialId = leftResult.SerialId;
                discoveryResult.LeftFirmwareId = leftResult.FirmwareId;
                discoveryResult.LeftProductId = leftResult.ProductId;
            }
            else if (!string.IsNullOrEmpty(leftResult.ErrorCode) || !string.IsNullOrEmpty(leftResult.ErrorMessage))
                discoveryResult.Errors.Add(new DiscoveryError { Side = "Left", ErrorCode = leftResult.ErrorCode ?? "", Message = leftResult.ErrorMessage ?? "" });
            if (rightResult.Found)
            {
                discoveryResult.RightSerialId = rightResult.SerialId;
                discoveryResult.RightFirmwareId = rightResult.FirmwareId;
                discoveryResult.RightProductId = rightResult.ProductId;
            }
            else if (!string.IsNullOrEmpty(rightResult.ErrorCode) || !string.IsNullOrEmpty(rightResult.ErrorMessage))
                discoveryResult.Errors.Add(new DiscoveryError { Side = "Right", ErrorCode = rightResult.ErrorCode ?? "", Message = rightResult.ErrorMessage ?? "" });

            var sessionEnd = DateTime.UtcNow;
            Log($"=== HiProWiredDiscoveryService.DetectBoth session end {sessionEnd:O} duration={(sessionEnd - sessionStart).TotalMilliseconds:F0} ms FoundLeft={discoveryResult.FoundLeft} FoundRight={discoveryResult.FoundRight} ===");
            sdk.Dispose();
            return discoveryResult;
        }

        private DiscoveredDevice DetectOnePort(
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
                    Thread.Sleep(delay);
                }

                ICommunicationAdaptor? adaptor = null;
                try
                {
                    adaptor = productManager.CreateCommunicationInterface(interfaceName, port, "");
                    if (adaptor == null)
                    {
                        var err = SdkScanHelper.GetDetailedErrorString(productManager);
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
                    var monitor = adaptor.BeginDetectDevice();
                    if (monitor == null)
                    {
                        CloseAdaptor(adaptor, portLabel);
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

                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        Thread.Sleep(300);
                    }

                    monitor.GetResult();
                    var sdkDeviceInfo = adaptor.EndDetectDevice(monitor);
                    CloseAdaptor(adaptor, portLabel);
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
                        HybridSerial = sdkDeviceInfo.HybridSerial != 0 ? sdkDeviceInfo.HybridSerial.ToString() : null
                    };
                    LogResult(portLabel, start, success, sdkDeviceInfo);
                    return success;
                }
                catch (Exception ex)
                {
                    CloseAdaptor(adaptor, portLabel);
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

        private void CloseAdaptor(ICommunicationAdaptor? adaptor, string portLabel)
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
