using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public class ProgrammerScanner
    {
        private readonly SdkManager _sdkManager;
        private readonly List<string> _wiredProgrammers = new() 
        { 
            Constants.HiPro, 
            Constants.Dsp3, 
            Constants.Caa, 
            Constants.Promira 
        };
        private readonly List<string> _wirelessProgrammers = new() 
        { 
            Constants.Noahlink, 
            Constants.Rsl10 
        };

        public ProgrammerScanner(SdkManager sdkManager)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
        }

        public async Task<List<ProgrammerInfo>> ScanForWiredProgrammersAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var foundProgrammers = new List<ProgrammerInfo>();
            var productManager = _sdkManager.ProductManager;

            // Get available COM ports
            var comPorts = SerialPort.GetPortNames();
            progress?.Report(10);

            int totalChecks = _wiredProgrammers.Count * comPorts.Length;
            int currentCheck = 0;

            foreach (var programmerName in _wiredProgrammers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                foreach (var portName in comPorts)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        // Try to create communication interface
                        var commAdaptor = productManager.CreateCommunicationInterface(
                            programmerName, 
                            CommunicationPort.kLeft, 
                            $"port={portName}");

                        if (commAdaptor != null)
                        {
                            // Test if programmer is actually available
                            try
                            {
                                var isAvailable = commAdaptor.CheckDevice();
                                if (isAvailable)
                                {
                                    foundProgrammers.Add(new ProgrammerInfo
                                    {
                                        Id = foundProgrammers.Count + 1,
                                        Name = GetProgrammerDisplayName(programmerName),
                                        Type = ProgrammerType.Wired,
                                        InterfaceName = programmerName,
                                        Port = portName,
                                        IsAvailable = true
                                    });
                                }
                            }
                            catch
                            {
                                // Programmer not available on this port
                            }
                            finally
                            {
                                commAdaptor?.CloseDevice();
                            }
                        }
                    }
                    catch
                    {
                        // Failed to create interface, skip this port
                    }

                    currentCheck++;
                    var progressPercent = 10 + (int)((currentCheck / (double)totalChecks) * 80);
                    progress?.Report(progressPercent);
                }
            }

            progress?.Report(100);
            return foundProgrammers;
        }

        public async Task<List<ProgrammerInfo>> ScanForWirelessProgrammersAsync(
            string programmerName, 
            string? comPort = null,
            IProgress<int>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            var foundProgrammers = new List<ProgrammerInfo>();
            var productManager = _sdkManager.ProductManager;

            try
            {
                progress?.Report(10);

                // Setup wireless programmer
                WirelessProgrammerType wirelessType;
                string wirelessComPort = comPort ?? "";

                if (programmerName == Constants.Rsl10)
                {
                    wirelessType = WirelessProgrammerType.kRSL10;
                    if (string.IsNullOrEmpty(wirelessComPort))
                    {
                        // Try to find RSL10 COM port
                        var comPorts = System.IO.Ports.SerialPort.GetPortNames();
                        wirelessComPort = comPorts.FirstOrDefault() ?? "";
                    }
                }
                else
                {
                    wirelessType = WirelessProgrammerType.kNoahlinkWireless;
                }

                // Setup BLE driver path for NOAHlink
                if (programmerName == Constants.Noahlink)
                {
                    var driverPath = Environment.ExpandEnvironmentVariables("%USERPROFILE%/.sounddesigner/nlw");
                    if (Directory.Exists(driverPath))
                    {
                        productManager.BLEDriverPath = Path.GetFullPath(driverPath);
                    }
                }

                progress?.Report(30);

                // Start scan
                var monitor = productManager.BeginScanForWirelessDevices(
                    wirelessType, 
                    wirelessComPort, 
                    CommunicationPort.kLeft, 
                    "", 
                    false);

                var scanStartTime = DateTime.Now;
                var scanTimeout = TimeSpan.FromMilliseconds(Constants.ScanTimeoutMs);

                var scannedDevices = new List<IConnectedDevice>();

                // Wait for scan to complete or timeout
                while (DateTime.Now - scanStartTime < scanTimeout)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        productManager.EndScanForWirelessDevices(monitor);
                        break;
                    }

                    await Task.Delay(200, cancellationToken);

                    // Get connected devices
                    var connectedDevices = productManager.WirelessGetConnectedDevices();
                    if (connectedDevices != null)
                    {
                        foreach (IConnectedDevice device in connectedDevices)
                        {
                            if (!scannedDevices.Any(d => d.Id == device.Id))
                            {
                                scannedDevices.Add(device);
                            }
                        }
                    }

                    if (monitor.IsFinished)
                    {
                        break;
                    }

                    var elapsed = (DateTime.Now - scanStartTime).TotalMilliseconds;
                    var progressPercent = 30 + (int)((elapsed / Constants.ScanTimeoutMs) * 60);
                    progress?.Report(Math.Min(progressPercent, 90));
                }

                monitor.GetResult();
                productManager.EndScanForWirelessDevices(monitor);

                // Add found programmers
                int id = 1;
                foreach (var device in scannedDevices)
                {
                    foundProgrammers.Add(new ProgrammerInfo
                    {
                        Id = id++,
                        Name = $"{GetProgrammerDisplayName(programmerName)} - {device.Name}",
                        Type = ProgrammerType.Wireless,
                        InterfaceName = programmerName,
                        DeviceId = device.Id,
                        SerialNumber = device.Name,
                        IsAvailable = true
                    });
                }

                progress?.Report(100);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to scan for wireless programmers: {ex.Message}", ex);
            }

            return foundProgrammers;
        }

        public async Task<List<ProgrammerInfo>> ScanAllProgrammersAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var allProgrammers = new List<ProgrammerInfo>();

            // Scan wired programmers
            progress?.Report(0);
            var wiredProgrammers = await ScanForWiredProgrammersAsync(
                new Progress<int>(p => progress?.Report(p / 2)), 
                cancellationToken);
            allProgrammers.AddRange(wiredProgrammers);

            // Scan wireless programmers
            progress?.Report(50);
            try
            {
                var noahlinkProgrammers = await ScanForWirelessProgrammersAsync(
                    Constants.Noahlink,
                    null,
                    new Progress<int>(p => progress?.Report(50 + (p / 4))),
                    cancellationToken);
                allProgrammers.AddRange(noahlinkProgrammers);
            }
            catch
            {
                // NOAHlink not available, continue
            }

            try
            {
                var rsl10Programmers = await ScanForWirelessProgrammersAsync(
                    Constants.Rsl10,
                    null,
                    new Progress<int>(p => progress?.Report(75 + (p / 4))),
                    cancellationToken);
                allProgrammers.AddRange(rsl10Programmers);
            }
            catch
            {
                // RSL10 not available, continue
            }

            progress?.Report(100);
            return allProgrammers;
        }

        private string GetProgrammerDisplayName(string interfaceName)
        {
            return interfaceName switch
            {
                Constants.HiPro => "HI-PRO 2",
                Constants.Dsp3 => "DSP3",
                Constants.Caa => "Communication Accelerator Adaptor",
                Constants.Promira => "Promira",
                Constants.Noahlink => "NOAHlink",
                Constants.Rsl10 => "RSL10",
                _ => interfaceName
            };
        }
    }
}
