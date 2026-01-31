using System;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public class DeviceDiscoveryService
    {
        private readonly SdkManager _sdkManager;
        private ICommunicationAdaptor? _commAdaptor;

        public DeviceDiscoveryService(SdkManager sdkManager)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
        }

        public async Task<Models.DeviceInfo?> DiscoverDeviceAsync(
            ProgrammerInfo programmer,
            DeviceSide side,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                progress?.Report(0);

                var productManager = _sdkManager.ProductManager;
                var port = side == DeviceSide.Left ? CommunicationPort.kLeft : CommunicationPort.kRight;

                // Create communication interface
                ICommunicationAdaptor? commAdaptor = null;

                if (programmer.Type == ProgrammerType.Wired)
                {
                    var settings = $"port={programmer.Port}";
                    commAdaptor = productManager.CreateCommunicationInterface(
                        programmer.InterfaceName,
                        port,
                        settings);
                }
                else // Wireless
                {
                    if (!string.IsNullOrEmpty(programmer.DeviceId))
                    {
                        commAdaptor = productManager.CreateWirelessCommunicationInterface(programmer.DeviceId);
                    }
                    else
                    {
                        throw new InvalidOperationException("Device ID is required for wireless programmers");
                    }
                }

                if (commAdaptor == null)
                {
                    throw new InvalidOperationException("Failed to create communication interface");
                }

                _commAdaptor = commAdaptor;
                progress?.Report(20);

                // Detect device - following SDK example pattern
                var monitor = commAdaptor.BeginDetectDevice();
                if (monitor != null)
                {
                    var lastProgress = -1;
                    
                    while (!monitor.IsFinished)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            System.Diagnostics.Debug.WriteLine($"Device discovery cancelled for {side} side");
                            try
                            {
                                monitor.GetResult(); // Check for errors
                                commAdaptor.EndDetectDevice(monitor);
                            }
                            catch
                            {
                                // Ignore cleanup errors
                            }
                            throw new OperationCanceledException();
                        }

                        await Task.Delay(300, cancellationToken);
                        
                        // Update progress if available
                        var maxSteps = monitor.ProgressMaximum;
                        var currentProgress = monitor.GetProgressValue();
                        if (maxSteps > 0 && lastProgress != currentProgress)
                        {
                            lastProgress = currentProgress;
                            var progressPercent = 20 + (int)(((double)currentProgress / maxSteps) * 70);
                            progress?.Report(progressPercent);
                        }
                    }
                    
                    // Get result and check for errors
                    monitor.GetResult();
                    
                    // Get device information
                    var sdkDeviceInfo = commAdaptor.EndDetectDevice(monitor);
                    progress?.Report(100);
                    
                    if (sdkDeviceInfo == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"No device detected on {side} side");
                        return null;
                    }

                    System.Diagnostics.Debug.WriteLine($"Device discovered on {side} side: ProductId={sdkDeviceInfo.ProductId}, SerialId={sdkDeviceInfo.SerialId}");

                    // Convert to our DeviceInfo model
                    return new Models.DeviceInfo
                    {
                        Side = side,
                        Model = sdkDeviceInfo.ProductId.ToString(),
                        SerialNumber = sdkDeviceInfo.SerialId != 0 ? sdkDeviceInfo.SerialId.ToString() : (sdkDeviceInfo.HybridSerial != 0 ? sdkDeviceInfo.HybridSerial.ToString() : "Unknown"),
                        Firmware = sdkDeviceInfo.FirmwareId.ToString(),
                        HybridId = sdkDeviceInfo.HybridId != 0 ? sdkDeviceInfo.HybridId.ToString() : null,
                        HybridSerial = sdkDeviceInfo.HybridSerial != 0 ? sdkDeviceInfo.HybridSerial.ToString() : null,
                        ProductId = sdkDeviceInfo.ProductId != 0 ? sdkDeviceInfo.ProductId.ToString() : null,
                        ChipId = sdkDeviceInfo.ChipId != 0 ? sdkDeviceInfo.ChipId.ToString() : null,
                        IsDetected = true
                    };
                }
                
                return null;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"Device discovery cancelled for {side} side");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error discovering device on {side} side: {ex.Message}");
                throw new InvalidOperationException($"Failed to discover device on {side} side: {ex.Message}", ex);
            }
            finally
            {
                // Cleanup communication adaptor on error
                if (_commAdaptor != null)
                {
                    try
                    {
                        _commAdaptor.CloseDevice();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                    _commAdaptor = null;
                }
            }
        }

        public async Task<(Models.DeviceInfo? Left, Models.DeviceInfo? Right)> DiscoverBothDevicesAsync(
            ProgrammerInfo programmer,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Models.DeviceInfo? leftDeviceInfo = null;
            Models.DeviceInfo? rightDeviceInfo = null;

            try
            {
                // Discover left device
                progress?.Report(0);
                leftDeviceInfo = await DiscoverDeviceAsync(
                    programmer,
                    DeviceSide.Left,
                    new Progress<int>(p => progress?.Report(p / 2)),
                    cancellationToken);

                // Discover right device
                progress?.Report(50);
                rightDeviceInfo = await DiscoverDeviceAsync(
                    programmer,
                    DeviceSide.Right,
                    new Progress<int>(p => progress?.Report(50 + (p / 2))),
                    cancellationToken);

                progress?.Report(100);
            }
            catch
            {
                // If discovery fails, return what we have
            }

            return (leftDeviceInfo, rightDeviceInfo);
        }

        public void Cleanup()
        {
            try
            {
                _commAdaptor?.CloseDevice();
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                _commAdaptor = null;
            }
        }
    }
}
