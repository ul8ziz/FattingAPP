using System;
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
        private IProduct? _product;

        public DeviceConnectionService(SdkManager sdkManager)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
            _product = _sdkManager.GetProduct();
        }

        public bool IsConnected(DeviceSide side)
        {
            var connection = side == DeviceSide.Left ? _leftConnection : _rightConnection;
            if (connection == null)
                return false;

            try
            {
                return connection.CheckDevice();
            }
            catch
            {
                return false;
            }
        }

        public async Task<Models.DeviceInfo> ConnectAsync(
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

                // Store connection
                if (side == DeviceSide.Left)
                    _leftConnection = commAdaptor;
                else
                    _rightConnection = commAdaptor;

                progress?.Report(20);

                // Detect device - following SDK example pattern
                SDLib.IDeviceInfo? deviceInfo = null;
                try
                {
                    var detectMonitor = commAdaptor.BeginDetectDevice();
                    if (detectMonitor != null)
                    {
                        var lastProgress = -1;
                        
                        while (!detectMonitor.IsFinished)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                System.Diagnostics.Debug.WriteLine($"Device connection cancelled for {side} side");
                                try
                                {
                                    detectMonitor.GetResult(); // Check for errors
                                    commAdaptor.EndDetectDevice(detectMonitor);
                                }
                                catch
                                {
                                    // Ignore cleanup errors
                                }
                                throw new OperationCanceledException();
                            }

                            await Task.Delay(300, cancellationToken);
                            
                            // Update progress if available
                            var maxSteps = detectMonitor.ProgressMaximum;
                            var currentProgress = detectMonitor.GetProgressValue();
                            if (maxSteps > 0 && lastProgress != currentProgress)
                            {
                                lastProgress = currentProgress;
                                var progressPercent = 20 + (int)(((double)currentProgress / maxSteps) * 40);
                                progress?.Report(progressPercent);
                            }
                        }
                        
                        // Get result and check for errors
                        detectMonitor.GetResult();
                        
                        // Get device information
                        deviceInfo = commAdaptor.EndDetectDevice(detectMonitor);
                        progress?.Report(60);
                        
                        if (deviceInfo == null)
                        {
                            throw new InvalidOperationException("Device detection failed");
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Device detected on {side} side: ProductId={deviceInfo.ProductId}, SerialId={deviceInfo.SerialId}");
                    }
                    else
                    {
                        throw new InvalidOperationException("Failed to begin device detection");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cleanup connection on cancellation
                    if (side == DeviceSide.Left)
                        _leftConnection = null;
                    else
                        _rightConnection = null;
                    try
                    {
                        commAdaptor.CloseDevice();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error detecting device on {side} side: {ex.Message}");
                    // Cleanup connection on error
                    if (side == DeviceSide.Left)
                        _leftConnection = null;
                    else
                        _rightConnection = null;
                    try
                    {
                        commAdaptor.CloseDevice();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                    throw;
                }

                progress?.Report(60);

                // Unlock parameters if locked
                if (deviceInfo.ParameterLockState)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(Constants.ParameterLockKey))
                        {
                            // Convert hex string to int array (simplified - may need proper conversion)
                            // For now, skip unlock if key is not provided
                        }
                    }
                    catch
                    {
                        // Ignore unlock errors
                    }
                }

                progress?.Report(70);

                // Initialize product if available - following SDK example pattern
                if (_product != null)
                {
                    var initMonitor = _product.BeginInitializeDevice(commAdaptor);
                    if (initMonitor != null)
                    {
                        var lastProgress = -1;
                        
                        while (!initMonitor.IsFinished)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                System.Diagnostics.Debug.WriteLine($"Product initialization cancelled for {side} side");
                                try
                                {
                                    initMonitor.GetResult(); // Check for errors
                                    _product.EndInitializeDevice(initMonitor);
                                }
                                catch
                                {
                                    // Ignore cleanup errors
                                }
                                throw new OperationCanceledException();
                            }

                            await Task.Delay(100, cancellationToken);
                            
                            // Update progress if available
                            var maxSteps = initMonitor.ProgressMaximum;
                            var currentProgress = initMonitor.GetProgressValue();
                            if (maxSteps > 0 && lastProgress != currentProgress)
                            {
                                lastProgress = currentProgress;
                                var progressPercent = 70 + (int)(((double)currentProgress / maxSteps) * 25);
                                progress?.Report(progressPercent);
                            }
                        }
                        
                        // Get result and check for errors
                        initMonitor.GetResult();
                        
                        // End initialization
                        var isConfigured = _product.EndInitializeDevice(initMonitor);
                        progress?.Report(100);
                        
                        if (isConfigured)
                        {
                            System.Diagnostics.Debug.WriteLine($"Device on {side} side initialized successfully");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Device on {side} side not fully configured, but connection is valid");
                        }
                    }
                }

                progress?.Report(100);

                // Convert to our DeviceInfo model
                return new Models.DeviceInfo
                {
                    Side = side,
                    Model = deviceInfo.ProductId.ToString(),
                    SerialNumber = deviceInfo.SerialId != 0 ? deviceInfo.SerialId.ToString() : (deviceInfo.HybridSerial != 0 ? deviceInfo.HybridSerial.ToString() : "Unknown"),
                    Firmware = deviceInfo.FirmwareId.ToString(),
                    HybridId = deviceInfo.HybridId != 0 ? deviceInfo.HybridId.ToString() : null,
                    HybridSerial = deviceInfo.HybridSerial != 0 ? deviceInfo.HybridSerial.ToString() : null,
                    ProductId = deviceInfo.ProductId != 0 ? deviceInfo.ProductId.ToString() : null,
                    ChipId = deviceInfo.ChipId != 0 ? deviceInfo.ChipId.ToString() : null,
                    IsDetected = true
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to connect to {side} device: {ex.Message}", ex);
            }
        }

        public async Task DisconnectAsync(DeviceSide side)
        {
            var connection = side == DeviceSide.Left ? _leftConnection : _rightConnection;
            
            if (connection != null)
            {
                try
                {
                    connection.CloseDevice();
                }
                catch
                {
                    // Ignore disconnect errors
                }
                finally
                {
                    if (side == DeviceSide.Left)
                        _leftConnection = null;
                    else
                        _rightConnection = null;
                }
            }

            await Task.CompletedTask;
        }

        public void Cleanup()
        {
            try
            {
                _leftConnection?.CloseDevice();
                _rightConnection?.CloseDevice();
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                _leftConnection = null;
                _rightConnection = null;
            }
        }
    }
}
