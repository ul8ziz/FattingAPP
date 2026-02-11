using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;
using Ul8ziz.FittingApp.App.Helpers;
using Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx;
using Ul8ziz.FittingApp.App.Services;

namespace Ul8ziz.FittingApp.App.Views
{
    /// <summary>
    /// Interaction logic for ConnectDevicesView.xaml
    /// </summary>
    public partial class ConnectDevicesView : UserControl, INotifyPropertyChanged
    {
        private bool _isSearching;
        private bool _hasFoundProgrammers;
        private bool _scanCompleted; // true after scan finishes (even with 0 results)
        private string _foundProgrammersMessage = string.Empty;
        private bool _isDiscovering;
        private double _discoveryProgress;
        private System.Windows.Threading.DispatcherTimer? _timeoutTimer;
        private int _timeoutSecondsRemaining;
        private const int ScanTimeoutSeconds = 15; // From Constants.ScanTimeoutMs / 1000
        private bool _hasFoundDevices;
        private string _foundDevicesMessage = string.Empty;
        private bool _isConnecting;
        private double _connectionProgress;
        private string _connectionStatusMessage = string.Empty;
        private bool _isConnected;

        // SDK Services
        private SdkManager? _sdkManager;
        private ProgrammerScanner? _programmerScanner;
        private DeviceDiscoveryService? _deviceDiscoveryService;
        private DeviceConnectionService? _deviceConnectionService;
        private ProgrammerInfo? _selectedProgrammerInfo;
        private CancellationTokenSource? _cancellationTokenSource;

        // D2XX-first: HI-PRO via FTDI D2XX (no COM/CTK for wired scan)
        private readonly HiProService _hiProService = new HiProService();

        public ConnectDevicesView()
        {
            InitializeComponent();
            DataContext = this;
            
            // SDK init is deferred to first scan to avoid constructor crash
        }

        /// <summary>
        /// Lazily initializes SDK services. Called before first scan.
        /// Never throws - sets _sdkManager to null on failure.
        /// </summary>
        private string? _lastSdkError;
        
        private void InitializeSdkServices()
        {
            if (_sdkManager?.IsInitialized == true)
                return; // Already initialized

            _lastSdkError = null;

            try
            {
                System.Diagnostics.Debug.WriteLine("=== Initializing SDK services ===");
                System.Diagnostics.Debug.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                System.Diagnostics.Debug.WriteLine($"Environment.Version: {Environment.Version}");
                System.Diagnostics.Debug.WriteLine($"AppContext.TargetFrameworkName: {AppContext.TargetFrameworkName}");
                System.Diagnostics.Debug.WriteLine($"App Base Dir: {AppDomain.CurrentDomain.BaseDirectory}");
                System.Diagnostics.Debug.WriteLine($"Library Path: {SdkConfiguration.GetLibraryPath()}");
                System.Diagnostics.Debug.WriteLine($"Config Path: {SdkConfiguration.GetConfigPath()}");
                
                _sdkManager = new SdkManager();
                _sdkManager.Initialize();
                
                _programmerScanner = new ProgrammerScanner(_sdkManager);
                _deviceDiscoveryService = new DeviceDiscoveryService(_sdkManager);
                _deviceConnectionService = new DeviceConnectionService(_sdkManager);
                
                System.Diagnostics.Debug.WriteLine("=== SDK services initialized successfully ===");
            }
            catch (Exception ex)
            {
                // Build detailed error message including inner exceptions
                var errorDetail = ex.Message;
                if (ex.InnerException != null)
                    errorDetail += $"\nCause: {ex.InnerException.Message}";
                
                System.Diagnostics.Debug.WriteLine($"=== SDK initialization FAILED ===");
                System.Diagnostics.Debug.WriteLine($"Error: {errorDetail}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                
                _lastSdkError = errorDetail;
                _sdkManager = null;
                _programmerScanner = null;
                _deviceDiscoveryService = null;
                _deviceConnectionService = null;
            }
        }

        // Properties
        public bool IsSearching
        {
            get => _isSearching;
            set { _isSearching = value; OnPropertyChanged(); }
        }

        public bool HasFoundProgrammers
        {
            get => _hasFoundProgrammers;
            set { _hasFoundProgrammers = value; OnPropertyChanged(); }
        }

        public string FoundProgrammersMessage
        {
            get => _foundProgrammersMessage;
            set { _foundProgrammersMessage = value; OnPropertyChanged(); }
        }

        public bool ScanCompleted
        {
            get => _scanCompleted;
            set { _scanCompleted = value; OnPropertyChanged(); }
        }

        /// <summary>True when scan finished but found nothing</summary>
        public bool ScanFoundNothing => ScanCompleted && !HasFoundProgrammers && !IsSearching;

        public string TimeoutMessage
        {
            get
            {
                if (!IsSearching || _timeoutSecondsRemaining <= 0)
                    return string.Empty;
                return $"Time remaining: {_timeoutSecondsRemaining}s";
            }
        }

        public bool CanCancelSearch => IsSearching;

        private ObservableCollection<ProgrammerViewModel> _programmers = new ObservableCollection<ProgrammerViewModel>();
        public ObservableCollection<ProgrammerViewModel> Programmers
        {
            get => _programmers;
            set { _programmers = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasProgrammers)); OnPropertyChanged(nameof(CanDiscover)); }
        }

        public bool HasProgrammers => Programmers?.Count > 0;

        public bool HasSelectedProgrammer => Programmers?.Any(p => p.IsSelected) ?? false;

        public string SelectedProgrammerName
        {
            get
            {
                var selected = Programmers?.FirstOrDefault(p => p.IsSelected);
                return selected?.Name ?? string.Empty;
            }
        }

        public bool CanDiscover => HasProgrammers && Programmers.Any(p => p.IsSelected) && !IsDiscovering;

        public bool IsDiscovering
        {
            get => _isDiscovering;
            set { _isDiscovering = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDiscover)); OnPropertyChanged(nameof(StatusText)); }
        }

        public double DiscoveryProgress
        {
            get => _discoveryProgress;
            set { _discoveryProgress = value; OnPropertyChanged(); }
        }

        public bool HasFoundDevices
        {
            get => _hasFoundDevices;
            set { _hasFoundDevices = value; OnPropertyChanged(); }
        }

        public string FoundDevicesMessage
        {
            get => _foundDevicesMessage;
            set { _foundDevicesMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        /// <summary>Single status line for merged Discover & Connect: Idle / Scanning / Completed.</summary>
        public string StatusText => IsDiscovering ? "Searching for devices…" : (string.IsNullOrEmpty(_foundDevicesMessage) ? "Ready to scan." : _foundDevicesMessage);

        private ObservableCollection<HearingAidViewModel> _devices = new ObservableCollection<HearingAidViewModel>();
        public ObservableCollection<HearingAidViewModel> Devices
        {
            get => _devices;
            set { _devices = value; OnPropertyChanged(); }
        }

        private HearingAidViewModel? _leftDevice;
        public HearingAidViewModel? LeftDevice
        {
            get => _leftDevice;
            set { _leftDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(ShowLeftDevice)); OnPropertyChanged(nameof(SelectedDevicesCount)); OnPropertyChanged(nameof(CanConnect)); }
        }

        private HearingAidViewModel? _rightDevice;
        public HearingAidViewModel? RightDevice
        {
            get => _rightDevice;
            set { _rightDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(ShowRightDevice)); OnPropertyChanged(nameof(SelectedDevicesCount)); OnPropertyChanged(nameof(CanConnect)); }
        }

        public bool HasDevices => LeftDevice != null || RightDevice != null;

        /// <summary>Syncs the Devices list from LeftDevice/RightDevice (detected only). Used by merged Discover & Connect UI.</summary>
        private void SyncDevicesCollection()
        {
            Devices.Clear();
            if (LeftDevice != null) Devices.Add(LeftDevice);
            if (RightDevice != null) Devices.Add(RightDevice);
        }

        public int SelectedDevicesCount => (LeftDevice?.IsSelected == true ? 1 : 0) + (RightDevice?.IsSelected == true ? 1 : 0);

        public bool CanConnect => SelectedDevicesCount > 0 && !IsConnecting && !IsConnected;

        public bool IsConnecting
        {
            get => _isConnecting;
            set { _isConnecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
        }

        public double ConnectionProgress
        {
            get => _connectionProgress;
            set { _connectionProgress = value; OnPropertyChanged(); }
        }

        public string ConnectionStatusMessage
        {
            get => _connectionStatusMessage;
            set { _connectionStatusMessage = value; OnPropertyChanged(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
        }

        /// <summary>True when Left ear was detected and card should be shown.</summary>
        public bool ShowLeftDevice => LeftDevice != null;
        /// <summary>True when Right ear was detected and card should be shown.</summary>
        public bool ShowRightDevice => RightDevice != null;

        private enum ScanKind { Wired, Wireless }

        // Event Handlers
        private void SearchWiredProgrammers_Click(object sender, RoutedEventArgs e) => RunScanAsync(ScanKind.Wired);
        private void SearchWirelessProgrammers_Click(object sender, RoutedEventArgs e) => RunScanAsync(ScanKind.Wireless);

        private async void RunScanAsync(ScanKind kind)
        {
            Console.WriteLine("=== PATCH_ACTIVE_MARKER_777 === " + DateTime.Now.ToString("O"));
            Console.WriteLine("ExePath=" + Environment.ProcessPath);
            Console.WriteLine("BaseDir=" + AppContext.BaseDirectory);
            Console.WriteLine("EntryAsm=" + System.Reflection.Assembly.GetEntryAssembly()?.Location);
            Console.WriteLine("ThisAsm=" + typeof(Ul8ziz.FittingApp.Device.DeviceCommunication.ProgrammerScanner).Assembly.Location);
            System.Diagnostics.Debug.WriteLine($"=== RunScanAsync ({kind}) STARTED ===");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            IsSearching = true;
            HasFoundProgrammers = false;
            ScanCompleted = false;
            FoundProgrammersMessage = kind == ScanKind.Wired ? "Searching for wired programmers..." : "Searching for wireless programmers...";
            Programmers.Clear();
            _timeoutSecondsRemaining = ScanTimeoutSeconds;
            StartTimeoutTimer();

            try
            {
                var progress = new Progress<int>(p => { FoundProgrammersMessage = $"Scanning... {p}%"; });
                var token = _cancellationTokenSource.Token;

                ProgrammerScanner.ScanResult? scanResult = null;
                if (kind == ScanKind.Wired)
                {
                    // D2XX-only: no COM/SerialPort, no CTK/sdnet for HI-PRO detection
                    FoundProgrammersMessage = "Scanning wired (D2XX)...";
                    var devices = await _hiProService.ListDevicesAsync(token).ConfigureAwait(true);
                    scanResult = new ProgrammerScanner.ScanResult();
                    int id = 1;
                    foreach (var dev in devices)
                    {
                        scanResult.FoundProgrammers.Add(new ProgrammerInfo
                        {
                            Id = id++,
                            Name = "HI-PRO",
                            Type = ProgrammerType.Wired,
                            InterfaceName = Constants.HiPro,
                            Port = "D2XX",
                            SerialNumber = string.IsNullOrEmpty(dev.SerialNumber) ? dev.Description : dev.SerialNumber,
                            Firmware = "N/A",
                            IsAvailable = true
                        });
                        scanResult.AllAttempts.Add(new ProgrammerScanner.ScanAttemptResult
                        {
                            ProgrammerName = Constants.HiPro,
                            DisplayName = "HI-PRO",
                            Found = true
                        });
                    }
                    if (devices.Count == 0)
                        scanResult.AllAttempts.Add(new ProgrammerScanner.ScanAttemptResult
                        {
                            ProgrammerName = Constants.HiPro,
                            DisplayName = "HI-PRO",
                            Found = false,
                            ErrorCode = "NOT_CONNECTED",
                            ErrorMessage = "No D2XX devices found. Connect HI-PRO via USB (D2XX works even if COM port is disabled)."
                        });
                }
                else
                {
                    if (_sdkManager == null || !_sdkManager.IsInitialized)
                    {
                        FoundProgrammersMessage = "Initializing SDK...";
                        System.Diagnostics.Debug.WriteLine("Initializing SDK...");
                        InitializeSdkServices();
                        if (_sdkManager == null || !_sdkManager.IsInitialized)
                        {
                            var detail = _lastSdkError ?? "Unknown error";
                            throw new InvalidOperationException($"SDK initialization failed.\n\n{detail}");
                        }
                    }
                    if (_programmerScanner == null)
                        throw new InvalidOperationException("Programmer scanner not available.");
                    FoundProgrammersMessage = "Scanning wireless...";
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    scanResult = _programmerScanner.ScanWirelessOnlySync(progress, token, YieldToUI);
                }

                StopTimeoutTimer();

                var foundProgrammers = scanResult!.FoundProgrammers;
                System.Diagnostics.Debug.WriteLine($"Scan completed. Found {foundProgrammers.Count} programmers");
                    
                foreach (var programmerInfo in foundProgrammers)
                {
                    var programmerVm = new ProgrammerViewModel
                    {
                        Id = programmerInfo.Id,
                        Name = programmerInfo.Name,
                        SerialNumber = programmerInfo.SerialNumber ?? "N/A",
                        Firmware = programmerInfo.Firmware ?? "N/A",
                        Port = programmerInfo.Port ?? programmerInfo.DeviceId ?? "N/A",
                        IsSelected = false
                    };

                    programmerVm.ProgrammerInfo = programmerInfo;

                    programmerVm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(ProgrammerViewModel.IsSelected))
                        {
                            if (programmerVm.IsSelected)
                                _selectedProgrammerInfo = programmerInfo;
                            OnPropertyChanged(nameof(CanDiscover));
                            OnPropertyChanged(nameof(HasSelectedProgrammer));
                            OnPropertyChanged(nameof(SelectedProgrammerName));
                        }
                    };

                    Programmers.Add(programmerVm);
                }

                // Notify UI that the collection changed
                OnPropertyChanged(nameof(HasProgrammers));
                OnPropertyChanged(nameof(Programmers));

                IsSearching = false;
                ScanCompleted = true;
                HasFoundProgrammers = foundProgrammers.Count > 0;
                OnPropertyChanged(nameof(ScanFoundNothing));
                
                if (foundProgrammers.Count > 0)
                {
                    FoundProgrammersMessage = $"Found {foundProgrammers.Count} programmer(s)";
                }
                else
                {
                    FoundProgrammersMessage = kind == ScanKind.Wired
                        ? "No wired programmers found. Connect HI-PRO via USB and try again. (D2XX works even if COM port is disabled.)"
                        : "No wireless programmers found. Turn on Bluetooth and try again.";

                    var diagnostics = kind == ScanKind.Wired
                        ? "Wired scan uses D2XX only. Run \"D2XX Self-Test\" from the Diagnostics section for details."
                        : scanResult.GetDiagnosticSummary();
                    var fullDetail = kind == ScanKind.Wired
                        ? "D2XX enumeration returned 0 devices. Ensure ftd2xx.dll is in app folder or C:\\Program Files (x86)\\HI-PRO."
                        : scanResult.GetFullDiagnosticDetail();
                    var pleaseCheck = kind == ScanKind.Wired
                        ? "Please check:\n• HI-PRO is connected via USB\n• ftd2xx.dll and FTD2XX_NET.dll are in app folder or C:\\Program Files (x86)\\HI-PRO\n• Build with Platform=x86 (32-bit)"
                        : "Please check:\n• Bluetooth is on, NOAHlink/RSL10 in range and paired";

                    System.Diagnostics.Debug.WriteLine("=== FULL SCAN DIAGNOSTIC ===");
                    System.Diagnostics.Debug.WriteLine(fullDetail);
                    System.Diagnostics.Debug.WriteLine("=== END DIAGNOSTIC ===");

                    var title = kind == ScanKind.Wired ? "No Wired Programmers Found" : "No Wireless Programmers Found";
                    MessageBox.Show(
                        $"No {(kind == ScanKind.Wired ? "wired" : "wireless")} programmers were detected.\n\n{diagnostics}\n\n{pleaseCheck}",
                        title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                System.Diagnostics.Debug.WriteLine($"=== RunScanAsync ({kind}) completed successfully ===");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Scan was cancelled");
                StopTimeoutTimer();
                IsSearching = false;
                ScanCompleted = true;
                HasFoundProgrammers = false;
                FoundProgrammersMessage = "Scan cancelled by user";
                OnPropertyChanged(nameof(IsSearching));
                OnPropertyChanged(nameof(ScanCompleted));
                OnPropertyChanged(nameof(HasFoundProgrammers));
                OnPropertyChanged(nameof(ScanFoundNothing));
                OnPropertyChanged(nameof(FoundProgrammersMessage));
                OnPropertyChanged(nameof(CanCancelSearch));
                OnPropertyChanged(nameof(TimeoutMessage));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== SearchProgrammers ERROR ===");
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                StopTimeoutTimer();
                IsSearching = false;
                ScanCompleted = true;
                HasFoundProgrammers = false;
                OnPropertyChanged(nameof(ScanFoundNothing));
                FoundProgrammersMessage = $"Error: {ex.Message}";
                
                MessageBox.Show(
                    $"Failed to scan for programmers:\n\n{ex.Message}\n\nPlease check:\n- Library file exists: {SdkConfiguration.GetLibraryPath()}\n- Config file exists: {SdkConfiguration.GetConfigPath()}\n- App directory: {AppDomain.CurrentDomain.BaseDirectory}\n- Programmers are connected\n- Drivers are installed",
                    "Scan Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ProgrammerItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ProgrammerViewModel programmer)
            {
                // Deselect all, select clicked one
                foreach (var p in Programmers)
                    p.IsSelected = false;

                programmer.IsSelected = true;
                _selectedProgrammerInfo = programmer.ProgrammerInfo;
                OnPropertyChanged(nameof(CanDiscover));
                OnPropertyChanged(nameof(HasSelectedProgrammer));
                OnPropertyChanged(nameof(SelectedProgrammerName));

                // Auto-discover hearing aids when a programmer is selected
                await DiscoverHearingAidsAsync();
            }
        }

        private async void DiscoverDevices_Click(object sender, RoutedEventArgs e)
        {
            await DiscoverHearingAidsAsync();
        }

        private void StopDiscovery_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Discovers hearing aids connected to the selected programmer.
        /// Called automatically when selecting a programmer, or manually via button.
        /// </summary>
        private async Task DiscoverHearingAidsAsync()
        {
            if (_selectedProgrammerInfo == null)
            {
                MessageBox.Show("Please select a programmer first.", "No Programmer Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsDiscovering) return; // Already discovering

            System.Diagnostics.Debug.WriteLine($"=== Auto-discovering devices via {_selectedProgrammerInfo.Name} ===");

            IsDiscovering = true;
            HasFoundDevices = false;
            DiscoveryProgress = 0;
            FoundDevicesMessage = $"Discovering hearing aids via {_selectedProgrammerInfo.Name}...";
            LeftDevice = null;
            RightDevice = null;
            SyncDevicesCollection();
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(CanConnect));

            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                var ct = _cancellationTokenSource.Token;

                DeviceInfo? leftDeviceInfo = null;
                DeviceInfo? rightDeviceInfo = null;

                bool isWiredHipro = _selectedProgrammerInfo.Type == ProgrammerType.Wired
                    && (string.Equals(_selectedProgrammerInfo.InterfaceName, Constants.HiPro, StringComparison.OrdinalIgnoreCase) || _selectedProgrammerInfo.Port == "D2XX");

                if (isWiredHipro)
                {
                    // Wired HI-PRO: HiProWiredDiscoveryService per Programmer's Guide (SDK only; no COM/SerialPort).
                    // Returns IReadOnlyList<DiscoveredDevice> [Left, Right]; partial success allowed.
                    await _hiProService.DisconnectAsync(ct).ConfigureAwait(true);
                    var d2xxSummary = D2xxLoader.GetDiagnosticsSummary();
                    System.Diagnostics.Debug.WriteLine(d2xxSummary);
                    ScanDiagnostics.WriteLine(d2xxSummary);
                    try
                    {
                        var d2xxDevices = await _hiProService.ListDevicesAsync(ct).ConfigureAwait(true);
                        ScanDiagnostics.WriteLine($"[D2XX] Device count: {d2xxDevices.Count}");
                        foreach (var d in d2xxDevices)
                            ScanDiagnostics.WriteLine($"[D2XX] Device Index={d.Index} Serial={d.SerialNumber} Description={d.Description} Type={d.Type}");
                    }
                    catch (Exception ex)
                    {
                        ScanDiagnostics.WriteLine($"[D2XX] ListDevices error: {ex.Message}");
                    }

                    void LogDiscovery(string line)
                    {
                        System.Diagnostics.Debug.WriteLine(line);
                        ScanDiagnostics.WriteLine(line);
                    }

                    var wiredDiscoveryService = new HiProWiredDiscoveryService(LogDiscovery);
                    var discoveryResult = await wiredDiscoveryService.DetectBothAsync(ct).ConfigureAwait(true);
                    leftDeviceInfo = discoveryResult.FoundLeft ? new DeviceInfo
                    {
                        Side = DeviceSide.Left,
                        Model = discoveryResult.LeftProductId ?? string.Empty,
                        SerialNumber = discoveryResult.LeftSerialId ?? "Unknown",
                        Firmware = discoveryResult.LeftFirmwareId ?? string.Empty,
                        IsDetected = true
                    } : null;
                    rightDeviceInfo = discoveryResult.FoundRight ? new DeviceInfo
                    {
                        Side = DeviceSide.Right,
                        Model = discoveryResult.RightProductId ?? string.Empty,
                        SerialNumber = discoveryResult.RightSerialId ?? "Unknown",
                        Firmware = discoveryResult.RightFirmwareId ?? string.Empty,
                        IsDetected = true
                    } : null;
                    foreach (var err in discoveryResult.Errors)
                    {
                        var userMsg = HiProWiredDiscoveryService.GetUserMessageForErrorCode(err.ErrorCode, err.Side);
                        if (!string.IsNullOrEmpty(userMsg)) LogDiscovery($"[{err.Side}] {userMsg}");
                    }
                }
                else
                {
                    // Wireless: use existing DeviceDiscoveryService (requires SDK initialized)
                    InitializeSdkServices();
                    if (_deviceDiscoveryService == null)
                    {
                        var msg = string.IsNullOrEmpty(_lastSdkError)
                            ? "SDK could not be initialized. Check sd.config, library file, and CTK Runtime (x86)."
                            : "SDK initialization failed: " + _lastSdkError;
                        throw new InvalidOperationException(msg);
                    }

                    var progress = new Progress<int>(p =>
                    {
                        DiscoveryProgress = p;
                        FoundDevicesMessage = $"Discovering hearing aids... {p}%";
                    });

                    (leftDeviceInfo, rightDeviceInfo) = await _deviceDiscoveryService.DiscoverBothDevicesAsync(
                        _selectedProgrammerInfo,
                        progress,
                        ct).ConfigureAwait(true);
                }

                // Update Left Device: set only when detected; clear when not
                LeftDevice = null;
                if (leftDeviceInfo != null)
                {
                    var leftVm = new HearingAidViewModel
                    {
                        Side = "Left",
                        Model = leftDeviceInfo.Model,
                        SerialNumber = leftDeviceInfo.SerialNumber,
                        Firmware = leftDeviceInfo.Firmware,
                        BatteryLevel = leftDeviceInfo.BatteryLevel ?? 0,
                        IsSelected = true
                    };
                    leftVm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(HearingAidViewModel.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedDevicesCount));
                            OnPropertyChanged(nameof(CanConnect));
                        }
                    };
                    LeftDevice = leftVm;
                }

                // Update Right Device: set only when detected; clear when not
                RightDevice = null;
                if (rightDeviceInfo != null)
                {
                    var rightVm = new HearingAidViewModel
                    {
                        Side = "Right",
                        Model = rightDeviceInfo.Model,
                        SerialNumber = rightDeviceInfo.SerialNumber,
                        Firmware = rightDeviceInfo.Firmware,
                        BatteryLevel = rightDeviceInfo.BatteryLevel ?? 0,
                        IsSelected = true
                    };
                    rightVm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(HearingAidViewModel.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedDevicesCount));
                            OnPropertyChanged(nameof(CanConnect));
                        }
                    };
                    RightDevice = rightVm;
                }

                SyncDevicesCollection();
                var deviceCount = (leftDeviceInfo != null ? 1 : 0) + (rightDeviceInfo != null ? 1 : 0);
                HasFoundDevices = deviceCount > 0;

                if (deviceCount > 0)
                    FoundDevicesMessage = deviceCount == 1
                        ? "Found 1 hearing aid(s). Select and connect."
                        : "Found 2 hearing aid(s). Select and connect.";
                else
                    FoundDevicesMessage = "No hearing aids found. Place devices on programmer and rescan.";
                System.Diagnostics.Debug.WriteLine($"Discovery complete: {deviceCount} device(s) found");
                IsDiscovering = false;
                OnPropertyChanged(nameof(HasDevices));
                OnPropertyChanged(nameof(ShowLeftDevice));
                OnPropertyChanged(nameof(ShowRightDevice));
                OnPropertyChanged(nameof(SelectedDevicesCount));
                OnPropertyChanged(nameof(CanConnect));
            }
            catch (OperationCanceledException)
            {
                IsDiscovering = false;
                FoundDevicesMessage = "Discovery cancelled";
                System.Diagnostics.Debug.WriteLine("Discovery cancelled");
            }
            catch (Exception ex)
            {
                IsDiscovering = false;
                HasFoundDevices = false;
                var msg = ex.Message;
                if (msg.IndexOf("SDK initialization failed", StringComparison.OrdinalIgnoreCase) >= 0)
                    FoundDevicesMessage = msg;
                else
                    FoundDevicesMessage = "No hearing aids detected on HI-PRO left/right port.";
                System.Diagnostics.Debug.WriteLine($"Discovery error: {ex.Message}");
                MessageBox.Show(
                    string.IsNullOrEmpty(_lastSdkError)
                        ? $"Discovery failed: {ex.Message}\n\nMake sure hearing aids are placed on the programmer."
                        : $"SDK initialization failed: {_lastSdkError}",
                    "Discovery",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DeviceCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Toggle selection when clicking on the card (not the checkbox)
            if (sender is Border border && border.DataContext is HearingAidViewModel device)
            {
                device.IsSelected = !device.IsSelected;
            }
        }
        
        private void DeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged(nameof(SelectedDevicesCount));
            OnPropertyChanged(nameof(CanConnect));
        }

        private async void ConnectDevices_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProgrammerInfo == null)
            {
                MessageBox.Show("Please select a programmer first.", "No Programmer Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var devicesToConnect = new List<(HearingAidViewModel Device, DeviceSide Side)>();
            if (LeftDevice?.IsSelected == true) devicesToConnect.Add((LeftDevice, DeviceSide.Left));
            if (RightDevice?.IsSelected == true) devicesToConnect.Add((RightDevice, DeviceSide.Right));

            if (devicesToConnect.Count == 0)
            {
                MessageBox.Show("Please select at least one device to connect.", "No Device Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsConnecting = true;
            ConnectionProgress = 0;
            ConnectionStatusMessage = string.Empty;
            AppSessionState.Instance.ConnectionState = ConnectionState.Connecting;

            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                InitializeSdkServices();
                if (_deviceConnectionService == null)
                {
                    var msg = string.IsNullOrEmpty(_lastSdkError)
                        ? "SDK could not be initialized. Check sd.config, library file, and CTK Runtime (x86)."
                        : "SDK initialization failed: " + _lastSdkError;
                    throw new InvalidOperationException(msg);
                }

                var result = new ConnectionResult();
                var totalDevices = devicesToConnect.Count;
                for (int i = 0; i < totalDevices; i++)
                {
                    var (device, side) = devicesToConnect[i];
                    ConnectionStatusMessage = $"Connecting to {device.Side} device...";
                    var deviceIndex = i;
                    var progress = new Progress<int>(p =>
                    {
                        ConnectionProgress = totalDevices > 0 ? ((deviceIndex * 100) + p) / totalDevices : 0;
                    });
                    try
                    {
                        var deviceInfo = await _deviceConnectionService.ConnectAsync(
                            _selectedProgrammerInfo!,
                            side,
                            progress,
                            _cancellationTokenSource.Token);
                        device.Model = deviceInfo.Model;
                        device.SerialNumber = deviceInfo.SerialNumber;
                        device.Firmware = deviceInfo.Firmware;
                        if (side == DeviceSide.Left) result.LeftConnected = true;
                        else result.RightConnected = true;
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                        if (msg.IndexOf("E_SEND_FAILURE", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("sending data", StringComparison.OrdinalIgnoreCase) >= 0)
                            msg = "Communication error. Check cable, seating, and contacts.";
                        if (side == DeviceSide.Left) { result.LeftError = msg; result.Errors.Add("Left: " + msg); }
                        else { result.RightError = msg; result.Errors.Add("Right: " + msg); }
                    }
                }
                result.Success = result.LeftConnected || result.RightConnected;
                ConnectionProgress = 100;
                IsConnecting = false;
                if (result.Success)
                {
                    IsConnected = true;
                    ConnectionStatusMessage = result.LeftConnected && result.RightConnected
                        ? "Connected to both devices"
                        : result.LeftConnected ? "Connected to Left device" : "Connected to Right device";
                    string displayName = BuildConnectedDeviceDisplayName(result);
                    AppSessionState.Instance.SetConnected(
                        displayName,
                        result.LeftConnected,
                        result.RightConnected,
                        result.LeftConnected ? LeftDevice?.Model : null,
                        result.LeftConnected ? LeftDevice?.Firmware : null,
                        result.LeftConnected ? LeftDevice?.SerialNumber : null,
                        result.RightConnected ? RightDevice?.Model : null,
                        result.RightConnected ? RightDevice?.Firmware : null,
                        result.RightConnected ? RightDevice?.SerialNumber : null);
                    var successMsg = result.Errors.Count > 0
                        ? $"Connected to selected device(s).\n\n{string.Join("\n", result.Errors)}"
                        : "Successfully connected to all selected devices.";
                    MessageBox.Show(successMsg, "Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                    _ = LoadDeviceInfoAsync();
                }
                else
                {
                    IsConnected = false;
                    ConnectionStatusMessage = "Connection failed";
                    AppSessionState.Instance.ConnectionState = ConnectionState.ConnectionFailed;
                    MessageBox.Show(
                        string.IsNullOrEmpty(_lastSdkError)
                            ? "Could not connect to devices.\n\n" + string.Join("\n", result.Errors)
                            : "SDK initialization failed: " + _lastSdkError,
                        "Connection",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    if (_deviceConnectionService != null)
                    {
                        try { await _deviceConnectionService.DisconnectAsync(DeviceSide.Left); } catch { }
                        try { await _deviceConnectionService.DisconnectAsync(DeviceSide.Right); } catch { }
                    }
                }
                await Task.Delay(500);
            }
            catch (OperationCanceledException)
            {
                IsConnecting = false;
                ConnectionStatusMessage = "Connection cancelled";
                AppSessionState.Instance.ConnectionState = ConnectionState.NotConnected;
                MessageBox.Show("Connection was cancelled.", "Connection Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                IsConnected = false;
                ConnectionStatusMessage = "Connection failed";
                AppSessionState.Instance.ConnectionState = ConnectionState.ConnectionFailed;
                var msg = ex.Message;
                if (msg.IndexOf("SDK initialization failed", StringComparison.OrdinalIgnoreCase) >= 0)
                    MessageBox.Show(msg, "Connection", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    MessageBox.Show("Could not connect: " + msg, "Connection", MessageBoxButton.OK, MessageBoxImage.Error);
                if (_deviceConnectionService != null)
                {
                    try { await _deviceConnectionService.DisconnectAsync(DeviceSide.Left); } catch { }
                    try { await _deviceConnectionService.DisconnectAsync(DeviceSide.Right); } catch { }
                }
            }
        }

        private string BuildConnectedDeviceDisplayName(ConnectionResult result)
        {
            string side = result.LeftConnected && result.RightConnected ? "L+R" : result.LeftConnected ? "Left" : result.RightConnected ? "Right" : string.Empty;
            string name = "Hearing Aid";
            if (result.LeftConnected && !string.IsNullOrWhiteSpace(LeftDevice?.Model))
                name = LeftDevice!.Model;
            else if (result.RightConnected && !string.IsNullOrWhiteSpace(RightDevice?.Model))
                name = RightDevice!.Model;
            else if (result.LeftConnected && !string.IsNullOrWhiteSpace(LeftDevice?.Firmware))
                name = LeftDevice!.Firmware;
            else if (result.RightConnected && !string.IsNullOrWhiteSpace(RightDevice?.Firmware))
                name = RightDevice!.Firmware;
            return string.IsNullOrEmpty(side) ? name : $"{name} ({side})";
        }

        private async System.Threading.Tasks.Task LoadDeviceInfoAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ConnectDevices] LoadDeviceInfoAsync: loading device identity and fitting data.");
                var session = AppSessionState.Instance;
                if (session.ConnectedLeft)
                    System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Left: Model={session.LeftModelName} Firmware={session.LeftFirmwareId} Serial={session.LeftSerialId}");
                if (session.ConnectedRight)
                    System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Right: Model={session.RightModelName} Firmware={session.RightFirmwareId} Serial={session.RightSerialId}");
                await System.Threading.Tasks.Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConnectDevices] LoadDeviceInfoAsync error: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Timeout management
        private void StartTimeoutTimer()
        {
            StopTimeoutTimer();
            
            _timeoutTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timeoutTimer.Tick += TimeoutTimer_Tick;
            _timeoutTimer.Start();
        }

        private void StopTimeoutTimer()
        {
            if (_timeoutTimer != null)
            {
                _timeoutTimer.Stop();
                _timeoutTimer.Tick -= TimeoutTimer_Tick;
                _timeoutTimer = null;
            }
            _timeoutSecondsRemaining = 0;
        }

        private void TimeoutTimer_Tick(object? sender, EventArgs e)
        {
            if (_timeoutSecondsRemaining > 0)
            {
                _timeoutSecondsRemaining--;
                OnPropertyChanged(nameof(TimeoutMessage));
                
                if (_timeoutSecondsRemaining <= 0)
                {
                    // Timeout reached - cancel the scan
                    System.Diagnostics.Debug.WriteLine("Scan timeout reached - cancelling scan");
                    _cancellationTokenSource?.Cancel();
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StopTimeoutTimer();
                        IsSearching = false;
                        HasFoundProgrammers = false;
                        FoundProgrammersMessage = "Scan timeout: No programmers found within the time limit. Please check connections and try again.";
                        OnPropertyChanged(nameof(IsSearching));
                        OnPropertyChanged(nameof(HasFoundProgrammers));
                        OnPropertyChanged(nameof(FoundProgrammersMessage));
                        OnPropertyChanged(nameof(TimeoutMessage));
                        OnPropertyChanged(nameof(CanCancelSearch));
                    });
                }
            }
        }

        /// <summary>
        /// Pumps the dispatcher so pending UI messages (e.g. Cancel button click) are processed during the synchronous scan.
        /// </summary>
        private static void YieldToUI()
        {
            var frame = new DispatcherFrame();
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        // Cancel search handler
        public void CancelSearch()
        {
            System.Diagnostics.Debug.WriteLine("=== Cancel search requested by user ===");
            
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                System.Diagnostics.Debug.WriteLine("Cancellation token set to cancelled");
            }
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                StopTimeoutTimer();
                IsSearching = false;
                HasFoundProgrammers = false;
                FoundProgrammersMessage = "Scan cancelled by user";
                OnPropertyChanged(nameof(IsSearching));
                OnPropertyChanged(nameof(HasFoundProgrammers));
                OnPropertyChanged(nameof(FoundProgrammersMessage));
                OnPropertyChanged(nameof(TimeoutMessage));
                OnPropertyChanged(nameof(CanCancelSearch));
            });
        }

        private void CancelSearch_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("CancelSearch_Click event fired");
            CancelSearch();
        }

    }

    // ViewModels
    public class ProgrammerViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Firmware { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        
        // Store ProgrammerInfo for SDK operations
        public Ul8ziz.FittingApp.Device.DeviceCommunication.Models.ProgrammerInfo? ProgrammerInfo { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HearingAidViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Side { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Firmware { get; set; } = string.Empty;
        public int BatteryLevel { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
