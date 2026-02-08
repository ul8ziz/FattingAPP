using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public class DeviceDiscoveryService
    {
        private readonly SdkManager _sdkManager;

        public DeviceDiscoveryService(SdkManager sdkManager)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
        }

        /// <summary>
        /// Discovers a device on the specified side using the selected programmer.
        /// Follows SDK example: BeginDetectDevice -> poll IsFinished -> GetResult -> EndDetectDevice
        /// </summary>
        public async Task<Models.DeviceInfo?> DiscoverDeviceAsync(
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

                // Create communication interface - following SDK example
                if (programmer.Type == ProgrammerType.Wired)
                {
                    // SDK example: CreateCommunicationInterface(programmer, port, "")
                    commAdaptor = productManager.CreateCommunicationInterface(
                        programmer.InterfaceName,
                        port,
                        "");
                }
                else
                {
                    if (string.IsNullOrEmpty(programmer.DeviceId))
                        throw new InvalidOperationException("Device ID is required for wireless programmers");

                    commAdaptor = productManager.CreateWirelessCommunicationInterface(programmer.DeviceId);
                }

                if (commAdaptor == null)
                    throw new InvalidOperationException("Failed to create communication interface");

                progress?.Report(20);

                // Detect device - following SDK example pattern exactly
                // From Connection.GetDeviceInformationBlocking():
                //   var monitor = CommAdaptor?.BeginDetectDevice();
                //   while (!monitor?.IsFinished ?? false) { ... }
                //   monitor?.GetResult();
                //   ConnectedDeviceInfo = CommAdaptor?.EndDetectDevice(monitor);
                var monitor = commAdaptor.BeginDetectDevice();
                if (monitor == null)
                    throw new InvalidOperationException("BeginDetectDevice returned null");

                var lastProgress = -1;

                while (!monitor.IsFinished)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await Task.Delay(300, cancellationToken);

                    var maxSteps = monitor.ProgressMaximum;
                    var pr = monitor.GetProgressValue();
                    if (maxSteps > 0 && lastProgress != pr)
                    {
                        lastProgress = pr;
                        progress?.Report(20 + (int)(((double)pr / maxSteps) * 70));
                    }
                }

                // Check for errors
                monitor.GetResult();

                // Get device information
                var sdkDeviceInfo = commAdaptor.EndDetectDevice(monitor);
                progress?.Report(100);

                if (sdkDeviceInfo == null)
                {
                    Debug.WriteLine($"No device detected on {side} side");
                    return null;
                }

                Debug.WriteLine($"Device discovered on {side}: Product={sdkDeviceInfo.ProductId}, Serial={sdkDeviceInfo.SerialId}");

                return new Models.DeviceInfo
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
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Device discovery cancelled for {side} side");
                throw; // Preserve cancellation signal
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error discovering device on {side} side: {ex.Message}");
                throw new InvalidOperationException($"Failed to discover device on {side} side: {ex.Message}", ex);
            }
            finally
            {
                // Close the adaptor used for discovery
                try { commAdaptor?.CloseDevice(); }
                catch { /* ignore close errors */ }
            }
        }

        /// <summary>
        /// Discovers devices on both sides. Continues if one side fails.
        /// </summary>
        public async Task<(Models.DeviceInfo? Left, Models.DeviceInfo? Right)> DiscoverBothDevicesAsync(
            ProgrammerInfo programmer,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Models.DeviceInfo? leftDevice = null;
            Models.DeviceInfo? rightDevice = null;

            // Discover left device
            progress?.Report(0);
            try
            {
                leftDevice = await DiscoverDeviceAsync(
                    programmer, DeviceSide.Left,
                    new Progress<int>(p => progress?.Report(p / 2)),
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Left device discovery failed: {ex.Message}");
            }

            // Discover right device
            progress?.Report(50);
            try
            {
                rightDevice = await DiscoverDeviceAsync(
                    programmer, DeviceSide.Right,
                    new Progress<int>(p => progress?.Report(50 + (p / 2))),
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Right device discovery failed: {ex.Message}");
            }

            progress?.Report(100);
            return (leftDevice, rightDevice);
        }
    }
}
