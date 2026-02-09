using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;
using Ul8ziz.FittingApp.App.Helpers;

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
            set { _isDiscovering = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDiscover)); }
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
            set { _foundDevicesMessage = value; OnPropertyChanged(); }
        }

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
            set { _leftDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(SelectedDevicesCount)); OnPropertyChanged(nameof(CanConnect)); }
        }

        private HearingAidViewModel? _rightDevice;
        public HearingAidViewModel? RightDevice
        {
            get => _rightDevice;
            set { _rightDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(SelectedDevicesCount)); OnPropertyChanged(nameof(CanConnect)); }
        }

        public bool HasDevices => LeftDevice != null || RightDevice != null;

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

        private enum ScanKind { Wired, Wireless }

        // Event Handlers
        private void SearchWiredProgrammers_Click(object sender, RoutedEventArgs e) => RunScanAsync(ScanKind.Wired);
        private void SearchWirelessProgrammers_Click(object sender, RoutedEventArgs e) => RunScanAsync(ScanKind.Wireless);

        private async void RunScanAsync(ScanKind kind)
        {
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

                ProgrammerScanner.ScanResult scanResult;
                if (kind == ScanKind.Wired)
                {
                    // All CTK/sdnet calls for wired scan on one dedicated STA thread; use fresh SDK on that thread to avoid E_INVALID_STATE
                    FoundProgrammersMessage = "Scanning wired (STA)...";
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    scanResult = StaThreadHelper.RunOnStaThread(() =>
                    {
                        SdkConfiguration.SetupEnvironment();
                        using var sdk = new SdkManager();
                        sdk.Initialize();
                        var scanner = new ProgrammerScanner(sdk);
                        return scanner.ScanWiredOnlySync(progress, token, null);
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

                var foundProgrammers = scanResult.FoundProgrammers;
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
                        ? "No wired programmers found. Connect HI-PRO and try again."
                        : "No wireless programmers found. Turn on Bluetooth and try again.";

                    var diagnostics = scanResult.GetDiagnosticSummary();
                    var fullDetail = scanResult.GetFullDiagnosticDetail();
                    var hasHiproUnknownName = scanResult.AllAttempts.Any(a =>
                        string.Equals(a.ErrorCode, "E_UNKNOWN_NAME", StringComparison.OrdinalIgnoreCase) &&
                        (a.ProgrammerName?.Contains("HI-PRO", StringComparison.OrdinalIgnoreCase) ?? false));
                    var hasHiproNotConnected = scanResult.AllAttempts.Any(a =>
                        string.Equals(a.ErrorCode, "NOT_CONNECTED", StringComparison.OrdinalIgnoreCase) &&
                        (a.ProgrammerName?.Contains("HI-PRO", StringComparison.OrdinalIgnoreCase) ?? false));
                    var ctkInstalled = SdkConfiguration.IsCtkInstalled();

                    var pleaseCheck = kind == ScanKind.Wired
                        ? "Please check:\n• HI-PRO programmer is connected via USB\n• The programmer is powered on\n• Correct drivers are installed"
                        : "Please check:\n• Bluetooth is on, NOAHlink/RSL10 in range and paired";
                    if (kind == ScanKind.Wired)
                    {
                        if (!ctkInstalled)
                            pleaseCheck += "\n• CTK Runtime: Install CTKRuntime64.msi from SDK redistribution folder (required for HI-PRO)";
                        if (hasHiproUnknownName)
                            pleaseCheck += "\n• HI-PRO: Ensure HI-PRO driver (v4.02+) is installed and \"C:\\Program Files (x86)\\HI-PRO\" is in system PATH; restart the app after changing PATH.";
                        if (fullDetail.Contains("E_INVALID_STATE", StringComparison.OrdinalIgnoreCase))
                            pleaseCheck += "\n• HI-PRO (E_INVALID_STATE): Another process is likely using the programmer (e.g. HiProMonitorService, Starkey Inspire). Close them: run \"powershell -ExecutionPolicy Bypass -File scripts\\close-programmer-apps.ps1 -Close\" from the project folder, confirm with Y, then restart this app and click Scan Wired again.";
                    }

                    System.Diagnostics.Debug.WriteLine("=== FULL SCAN DIAGNOSTIC ===");
                    System.Diagnostics.Debug.WriteLine(fullDetail);
                    System.Diagnostics.Debug.WriteLine("=== END DIAGNOSTIC ===");

                    var title = kind == ScanKind.Wired ? "No Wired Programmers Found" : "No Wireless Programmers Found";
                    MessageBox.Show(
                        $"No {(kind == ScanKind.Wired ? "wired" : "wireless")} programmers were detected.\n\nScan Results:\n{diagnostics}\n\nTechnical Details:\n{fullDetail}\n\n{pleaseCheck}",
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
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(CanConnect));

            try
            {
                // Cancel any previous operation
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();

                if (_deviceDiscoveryService == null)
                    throw new InvalidOperationException("Device discovery service not available.");

                var progress = new Progress<int>(p =>
                {
                    DiscoveryProgress = p;
                    FoundDevicesMessage = $"Discovering hearing aids... {p}%";
                });

                // Discover both sides
                var (leftDeviceInfo, rightDeviceInfo) = await _deviceDiscoveryService.DiscoverBothDevicesAsync(
                    _selectedProgrammerInfo,
                    progress,
                    _cancellationTokenSource.Token);

                // Update Left Device
                if (leftDeviceInfo != null)
                {
                    var leftVm = new HearingAidViewModel
                    {
                        Side = "Left",
                        Model = leftDeviceInfo.Model,
                        SerialNumber = leftDeviceInfo.SerialNumber,
                        Firmware = leftDeviceInfo.Firmware,
                        BatteryLevel = leftDeviceInfo.BatteryLevel ?? 0,
                        IsSelected = true // Auto-select found devices
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

                // Update Right Device
                if (rightDeviceInfo != null)
                {
                    var rightVm = new HearingAidViewModel
                    {
                        Side = "Right",
                        Model = rightDeviceInfo.Model,
                        SerialNumber = rightDeviceInfo.SerialNumber,
                        Firmware = rightDeviceInfo.Firmware,
                        BatteryLevel = rightDeviceInfo.BatteryLevel ?? 0,
                        IsSelected = true // Auto-select found devices
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

                var deviceCount = (leftDeviceInfo != null ? 1 : 0) + (rightDeviceInfo != null ? 1 : 0);
                HasFoundDevices = deviceCount > 0;

                if (deviceCount > 0)
                {
                    FoundDevicesMessage = $"Found {deviceCount} hearing aid(s)";
                    System.Diagnostics.Debug.WriteLine($"Discovery complete: {deviceCount} device(s) found");
                }
                else
                {
                    FoundDevicesMessage = "No hearing aids detected. Check that hearing aids are placed on the programmer.";
                    System.Diagnostics.Debug.WriteLine("Discovery complete: no devices found");
                }
                
                IsDiscovering = false;
                OnPropertyChanged(nameof(HasDevices));
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
                FoundDevicesMessage = $"Discovery failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Discovery error: {ex.Message}");
                
                MessageBox.Show(
                    $"Failed to discover hearing aids:\n\n{ex.Message}\n\nMake sure hearing aids are placed on the programmer.",
                    "Discovery Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

            try
            {
                // Cancel any previous connection
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                if (_deviceConnectionService == null)
                {
                    throw new InvalidOperationException("Device connection service not available.");
                }

                var totalDevices = devicesToConnect.Count;
                for (int i = 0; i < totalDevices; i++)
                {
                    var (device, side) = devicesToConnect[i];
                    ConnectionStatusMessage = $"Connecting to {device.Side} device...";

                    var deviceIndex = i; // Capture for closure
                    var progress = new Progress<int>(p =>
                    {
                        // Correct formula: each device gets an equal share of 100%
                        ConnectionProgress = ((deviceIndex * 100) + p) / totalDevices;
                    });

                    var deviceInfo = await _deviceConnectionService.ConnectAsync(
                        _selectedProgrammerInfo,
                        side,
                        progress,
                        _cancellationTokenSource.Token);

                    // Update device information with actual data from SDK
                    device.Model = deviceInfo.Model;
                    device.SerialNumber = deviceInfo.SerialNumber;
                    device.Firmware = deviceInfo.Firmware;
                }

                ConnectionProgress = 100;
                ConnectionStatusMessage = "Successfully connected to all devices";
                await Task.Delay(500);

                IsConnecting = false;
                IsConnected = true;

                // Show success message
                MessageBox.Show(
                    "Successfully connected to all selected devices!",
                    "Connection Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Auto-navigate after 1 second (optional)
                await Task.Delay(1000);
                // Navigation will be handled by MainView
            }
            catch (OperationCanceledException)
            {
                IsConnecting = false;
                ConnectionStatusMessage = "Connection cancelled";
                MessageBox.Show("Connection was cancelled.", "Connection Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                IsConnected = false;
                ConnectionStatusMessage = $"Connection failed: {ex.Message}";
                
                // Show error message to user
                MessageBox.Show(
                    $"Failed to connect to devices:\n{ex.Message}",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Cleanup failed connections
                if (_deviceConnectionService != null)
                {
                    try
                    {
                        if (LeftDevice?.IsSelected == true)
                            await _deviceConnectionService.DisconnectAsync(DeviceSide.Left);
                    }
                    catch { /* ignore cleanup errors */ }
                    try
                    {
                        if (RightDevice?.IsSelected == true)
                            await _deviceConnectionService.DisconnectAsync(DeviceSide.Right);
                    }
                    catch { /* ignore cleanup errors */ }
                }
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
