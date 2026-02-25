using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;
using DeviceInfoModel = Ul8ziz.FittingApp.Device.DeviceCommunication.Models.DeviceInfo;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Hearing-aid discovery over HI-PRO using Sound Designer SDK only. No COM/SerialPort.
    /// Discover Left and Right independently; partial success (one side found) is returned, not thrown.
    /// Only fails when SDK init fails (config/library/CTK) or both sides fail with a fatal error.
    /// </summary>
    public sealed class HiProWiredDiscovery
    {
        private readonly Action<string>? _logDiagnostics;

        public HiProWiredDiscovery(Action<string>? logDiagnostics = null)
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
        /// Runs on a dedicated STA thread (required for SDK/CTK).
        /// </summary>
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

        /// <summary>
        /// Detects hearing aids on both Left and Right. Returns DetectResult with partial success; does not throw for single-side failure.
        /// </summary>
        public Task<DetectResult> DetectBothAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => RunOnStaThread(() => DoDetectBothOnSta(cancellationToken)), cancellationToken);
        }

        private DetectResult DoDetectBothOnSta(CancellationToken ct)
        {
            var result = new DetectResult();

            Log("--- Sound Designer environment ---");
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var sdConfigPath = Environment.GetEnvironmentVariable("SD_CONFIG_PATH") ?? "";
            var libraryPath = SdkConfiguration.GetLibraryPath();
            var ctkPath = SdkConfiguration.FindCtkPath();
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            var pathEntries = pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            Log($"App architecture: {(IntPtr.Size == 8 ? "x64" : "x86")}");
            Log($"App base: {appBase}");
            Log($"SD_CONFIG_PATH: {sdConfigPath}");
            Log($"Library path: {libraryPath}");
            Log($"CTK path: {ctkPath ?? "(not found)"}");
            Log($"PATH (first 5): {string.Join("; ", pathEntries.Length > 5 ? pathEntries.AsSpan(0, 5).ToArray() : pathEntries)}");

            SdkManager? sdk = null;
            try
            {
                Log("DetectBoth: setting up SDK environment");
                SdkConfiguration.SetupEnvironment();
                sdk = new SdkManager();
                sdk.Initialize();
            }
            catch (Exception ex)
            {
                Log($"SDK init failed (fatal): {ex.Message}");
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(null, ex);
                result.Errors.Add($"SDK initialization: {ex.Message}");
                throw new InvalidOperationException("SDK initialization failed. Check sd.config, library file, and CTK Runtime (x86).", ex);
            }

            try
            {
                var pm = sdk!.ProductManager;
                Log($"CTK interface count: {SdkScanHelper.GetCommunicationInterfaceCount(pm)}");

                try
                {
                    result.Left = DoDetectOne(pm, CommunicationPort.kLeft, DeviceSide.Left, ct);
                    Log($"Left result: {(result.Left != null ? $"Found FirmwareId={result.Left.Firmware} ProductId={result.Left.ProductId} SerialId={result.Left.SerialNumber}" : "NotFound")}");
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    if (IsNonFatalDiscoveryError(ex))
                    {
                        var msg = $"[Left] {ex.Message}";
                        if (ScanDiagnostics.IsSdException(ex))
                        {
                            ScanDiagnostics.LogSdExceptionDetails(pm, ex);
                            msg += " HResult=" + ex.HResult;
                        }
                        result.Errors.Add(msg);
                        Log($"Left: non-fatal, recording error. {msg}");
                    }
                    else
                    {
                        Log($"Left: fatal error: {ex.Message}");
                        throw;
                    }
                }

                ct.ThrowIfCancellationRequested();

                try
                {
                    result.Right = DoDetectOne(pm, CommunicationPort.kRight, DeviceSide.Right, ct);
                    Log($"Right result: {(result.Right != null ? $"Found FirmwareId={result.Right.Firmware} ProductId={result.Right.ProductId} SerialId={result.Right.SerialNumber}" : "NotFound")}");
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    if (IsNonFatalDiscoveryError(ex))
                    {
                        var msg = $"[Right] {ex.Message}";
                        if (ScanDiagnostics.IsSdException(ex))
                        {
                            ScanDiagnostics.LogSdExceptionDetails(pm, ex);
                            msg += " HResult=" + ex.HResult;
                        }
                        result.Errors.Add(msg);
                        Log($"Right: non-fatal, recording error. {msg}");
                    }
                    else
                    {
                        Log($"Right: fatal error: {ex.Message}");
                        throw;
                    }
                }
            }
            finally
            {
                sdk?.Dispose();
            }

            return result;
        }

        private static bool IsNonFatalDiscoveryError(Exception ex)
        {
            var msg = (ex.Message ?? "") + (ex.InnerException?.Message ?? "");
            var full = ex.ToString() ?? "";
            if (msg.IndexOf("E_NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("E_NOT_DETECTED", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (full.IndexOf("E_NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (full.IndexOf("E_NOT_DETECTED", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("No device", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("not detected", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private DeviceInfoModel? DoDetectOne(
            IProductManager productManager,
            CommunicationPort port,
            DeviceSide side,
            CancellationToken ct)
        {
            ICommunicationAdaptor? adaptor = null;
            try
            {
                Log($"Creating adaptor: HI-PRO, port={port}, settings=\"\"");
                adaptor = productManager.CreateCommunicationInterface(Constants.HiPro, port, "");
                if (adaptor == null)
                {
                    var err = SdkScanHelper.GetDetailedErrorString(productManager);
                    Log($"CreateCommunicationInterface returned null. GetDetailedErrorString: {err}");
                    throw new InvalidOperationException($"Could not create HI-PRO interface. {err}");
                }
                Log("Adaptor created; calling BeginDetectDevice");

                var monitor = adaptor.BeginDetectDevice();
                if (monitor == null)
                {
                    Log("BeginDetectDevice returned null");
                    throw new InvalidOperationException("Device detection did not start.");
                }

                while (!monitor.IsFinished)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(300);
                }

                monitor.GetResult();
                var sdkDeviceInfo = adaptor.EndDetectDevice(monitor);

                if (sdkDeviceInfo == null)
                {
                    Log($"No device on {side}");
                    return null;
                }

                Log($"Device on {side}: ProductId={sdkDeviceInfo.ProductId}, SerialId={sdkDeviceInfo.SerialId}, FirmwareId={sdkDeviceInfo.FirmwareId}");
                return new DeviceInfoModel
                {
                    Side = side,
                    Model = sdkDeviceInfo.ProductId.ToString(),
                    SerialNumber = sdkDeviceInfo.SerialId != 0
                        ? sdkDeviceInfo.SerialId.ToString()
                        : (sdkDeviceInfo.HybridSerial != 0 ? sdkDeviceInfo.HybridSerial.ToString() : "Unknown"),
                    Firmware = sdkDeviceInfo.FirmwareId.ToString(),
                    HybridId = sdkDeviceInfo.HybridId != 0 ? sdkDeviceInfo.HybridId.ToString() : null,
                    HybridSerial = sdkDeviceInfo.HybridSerial != 0 ? sdkDeviceInfo.HybridSerial.ToString() : null,
                    ProductId = sdkDeviceInfo.ProductId != 0 ? sdkDeviceInfo.ProductId.ToString() : null,
                    ChipId = sdkDeviceInfo.ChipId != 0 ? sdkDeviceInfo.ChipId.ToString() : null,
                    IsDetected = true
                };
            }
            finally
            {
                if (adaptor != null)
                {
                    try { adaptor.CloseDevice(); }
                    catch (Exception ex) { Log($"CloseDevice: {ex.Message}"); }
                }
            }
        }
    }
}
