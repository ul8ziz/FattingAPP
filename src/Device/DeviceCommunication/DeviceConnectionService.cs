using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public class DeviceConnectionService
    {
        private readonly SdkManager _sdkManager;
        private ICommunicationAdaptor? _leftConnection;
        private ICommunicationAdaptor? _rightConnection;
        private bool _leftConfigured;
        private bool _rightConfigured;

        public DeviceConnectionService(SdkManager sdkManager)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
        }

        /// <summary>True if the side was successfully initialized (EndInitializeDevice returned true). Read/Write must only run when configured.</summary>
        public bool IsSideConfigured(DeviceSide side)
        {
            return side == DeviceSide.Left ? _leftConfigured : _rightConfigured;
        }

        public bool IsConnected(DeviceSide side)
        {
            var connection = side == DeviceSide.Left ? _leftConnection : _rightConnection;
            if (connection == null) return false;
            try { return connection.CheckDevice(); }
            catch { return false; }
        }

        /// <summary>Returns the communication adaptor for the given side for fitting read/write. Null if not connected.</summary>
        public ICommunicationAdaptor? GetConnection(DeviceSide side)
        {
            return side == DeviceSide.Left ? _leftConnection : _rightConnection;
        }

        /// <summary>
        /// Connects to a device following SDK example pattern:
        /// 1. CreateCommunicationInterface(programmer, port, "")
        /// 2. BeginDetectDevice -> poll IsFinished -> GetResult -> EndDetectDevice
        /// 3. UnlockParameters if locked
        /// 4. BeginInitializeDevice -> poll IsFinished -> GetResult -> EndInitializeDevice
        /// </summary>
        public async Task<Models.DeviceInfo> ConnectAsync(
            ProgrammerInfo programmer,
            DeviceSide side,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ICommunicationAdaptor? commAdaptor = null;

            try
            {
                progress?.Report(0);

                var productManager = _sdkManager.ProductManager;
                var port = side == DeviceSide.Left ? CommunicationPort.kLeft : CommunicationPort.kRight;

                // Step 1: Create communication interface
                if (programmer.Type == ProgrammerType.Wired)
                {
                    commAdaptor = productManager.CreateCommunicationInterface(
                        programmer.InterfaceName, port, "");
                }
                else
                {
                    if (string.IsNullOrEmpty(programmer.DeviceId))
                        throw new InvalidOperationException("Device ID is required for wireless programmers");
                    commAdaptor = productManager.CreateWirelessCommunicationInterface(programmer.DeviceId);
                }

                if (commAdaptor == null)
                    throw new InvalidOperationException("Failed to create communication interface");

                // Store connection reference
                if (side == DeviceSide.Left) _leftConnection = commAdaptor;
                else _rightConnection = commAdaptor;

                progress?.Report(10);

                // Step 2: Detect device - following SDK example exactly
                var detectMonitor = commAdaptor.BeginDetectDevice();
                if (detectMonitor == null)
                    throw new InvalidOperationException("BeginDetectDevice returned null");

                var lastPr = -1;
                while (!detectMonitor.IsFinished)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(300, cancellationToken);

                    var maxSteps = detectMonitor.ProgressMaximum;
                    var pr = detectMonitor.GetProgressValue();
                    if (maxSteps > 0 && lastPr != pr)
                    {
                        lastPr = pr;
                        progress?.Report(10 + (int)(((double)pr / maxSteps) * 40));
                    }
                }

                detectMonitor.GetResult(); // throws on error
                var deviceInfo = commAdaptor.EndDetectDevice(detectMonitor);

                if (deviceInfo == null)
                    throw new InvalidOperationException("Device detection failed - no device found");

                Debug.WriteLine($"Device detected on {side}: Product={deviceInfo.ProductId}, Serial={deviceInfo.SerialId}");
                progress?.Report(50);

                // Step 3: Unlock parameters if locked
                if (deviceInfo.ParameterLockState && !string.IsNullOrEmpty(Constants.ParameterLockKey))
                {
                    Debug.WriteLine("Parameters locked, attempting unlock...");
                    // TODO: Implement proper hex key conversion when key is provided
                }

                progress?.Report(60);

                // Step 4: Initialize product if available
                var product = _sdkManager.GetProduct();
                if (product != null)
                {
                    try
                    {
                        var initMonitor = product.BeginInitializeDevice(commAdaptor);
                        if (initMonitor != null)
                        {
                            lastPr = -1;
                            while (!initMonitor.IsFinished)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await Task.Delay(100, cancellationToken);

                                var maxSteps = initMonitor.ProgressMaximum;
                                var pr = initMonitor.GetProgressValue();
                                if (maxSteps > 0 && lastPr != pr)
                                {
                                    lastPr = pr;
                                    progress?.Report(60 + (int)(((double)pr / maxSteps) * 35));
                                }
                            }

                            initMonitor.GetResult(); // throws on error
                            var isConfigured = product.EndInitializeDevice(initMonitor);
                            if (side == DeviceSide.Left) _leftConfigured = isConfigured;
                            else _rightConfigured = isConfigured;
                            Debug.WriteLine($"Device init on {side}: configured={isConfigured}");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Device init warning on {side}: {ex.Message}");
                        if (side == DeviceSide.Left) _leftConfigured = false;
                        else _rightConfigured = false;
                        // Continue - connection is still valid even if init fails
                    }
                }

                progress?.Report(100);

                return new Models.DeviceInfo
                {
                    Side = side,
                    Model = deviceInfo.ProductId.ToString(),
                    SerialNumber = deviceInfo.SerialId != 0
                        ? deviceInfo.SerialId.ToString()
                        : (deviceInfo.HybridSerial != 0 ? deviceInfo.HybridSerial.ToString() : "Unknown"),
                    Firmware = deviceInfo.FirmwareId.ToString(),
                    HybridId = deviceInfo.HybridId != 0 ? deviceInfo.HybridId.ToString() : null,
                    HybridSerial = deviceInfo.HybridSerial != 0 ? deviceInfo.HybridSerial.ToString() : null,
                    ProductId = deviceInfo.ProductId != 0 ? deviceInfo.ProductId.ToString() : null,
                    ChipId = deviceInfo.ChipId != 0 ? deviceInfo.ChipId.ToString() : null,
                    IsDetected = true,
                    ParameterLockState = deviceInfo.ParameterLockState
                };
            }
            catch (OperationCanceledException)
            {
                // Clean up on cancellation - don't wrap in InvalidOperationException
                CleanupConnection(side, commAdaptor);
                throw; // Preserve cancellation signal!
            }
            catch (Exception ex)
            {
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(null, ex);
                CleanupConnection(side, commAdaptor);
                throw new InvalidOperationException($"Failed to connect to {side} device: {ex.Message}", ex);
            }
        }

        private void CleanupConnection(DeviceSide side, ICommunicationAdaptor? adaptor)
        {
            if (side == DeviceSide.Left) { _leftConnection = null; _leftConfigured = false; }
            else { _rightConnection = null; _rightConfigured = false; }
            try { adaptor?.CloseDevice(); }
            catch { /* ignore */ }
        }

        public Task DisconnectAsync(DeviceSide side)
        {
            var connection = side == DeviceSide.Left ? _leftConnection : _rightConnection;
            if (connection != null)
            {
                try { connection.CloseDevice(); }
                catch { /* ignore */ }
                finally
                {
                    if (side == DeviceSide.Left) { _leftConnection = null; _leftConfigured = false; }
                    else { _rightConnection = null; _rightConfigured = false; }
                }
            }
            return Task.CompletedTask;
        }

        public void Cleanup()
        {
            try { _leftConnection?.CloseDevice(); }
            catch { /* ignore */ }
            finally { _leftConnection = null; _leftConfigured = false; }

            try { _rightConnection?.CloseDevice(); }
            catch { /* ignore */ }
            finally { _rightConnection = null; _rightConfigured = false; }
        }
    }
}
