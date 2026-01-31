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
using System.Windows.Media;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Views
{
    /// <summary>
    /// Interaction logic for ConnectDevicesView.xaml
    /// </summary>
    public partial class ConnectDevicesView : UserControl, INotifyPropertyChanged
    {
        private bool _isSearching;
        private bool _hasFoundProgrammers;
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
            
            // Initialize SDK services (lazy initialization)
            InitializeSdkServices();
        }

        private void InitializeSdkServices()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Initializing SDK services...");
                
                _sdkManager = new SdkManager();
                _sdkManager.Initialize();
                
                if (!_sdkManager.IsInitialized)
                {
                    throw new InvalidOperationException("SDK initialization returned false");
                }
                
                _programmerScanner = new ProgrammerScanner(_sdkManager);
                _deviceDiscoveryService = new DeviceDiscoveryService(_sdkManager);
                _deviceConnectionService = new DeviceConnectionService(_sdkManager);
                
                System.Diagnostics.Debug.WriteLine("SDK services initialized successfully");
            }
            catch (Exception ex)
            {
                // SDK initialization failed - will show error when user tries to scan
                System.Diagnostics.Debug.WriteLine($"SDK initialization failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-throw to be caught by caller
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

        // Event Handlers
        private async void SearchProgrammers_Click(object sender, RoutedEventArgs e)
        {
            // Debug: Confirm event is being called
            System.Diagnostics.Debug.WriteLine("=== SearchProgrammers_Click STARTED ===");
            
            // Force UI update immediately - use BeginInvoke for immediate update
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsSearching = true;
                HasFoundProgrammers = false;
                FoundProgrammersMessage = "Searching for programmers...";
                _timeoutSecondsRemaining = ScanTimeoutSeconds;
                StartTimeoutTimer();
                OnPropertyChanged(nameof(IsSearching));
                OnPropertyChanged(nameof(HasFoundProgrammers));
                OnPropertyChanged(nameof(FoundProgrammersMessage));
                OnPropertyChanged(nameof(TimeoutMessage));
                OnPropertyChanged(nameof(CanCancelSearch));
            }), System.Windows.Threading.DispatcherPriority.Normal);

            try
            {
                System.Diagnostics.Debug.WriteLine("Starting search process...");

                // Cancel any previous scan
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Check if cancellation was requested before starting
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine("Cancellation requested before scan started");
                    return;
                }

                // Ensure SDK is initialized
                if (_sdkManager == null || !_sdkManager.IsInitialized)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FoundProgrammersMessage = "Initializing SDK...";
                    });
                    
                    System.Diagnostics.Debug.WriteLine("Initializing SDK...");
                    InitializeSdkServices();
                    
                    if (_sdkManager == null || !_sdkManager.IsInitialized)
                    {
                        var errorMsg = "SDK initialization failed. Please check SDK configuration and ensure library files are available.";
                        System.Diagnostics.Debug.WriteLine($"ERROR: {errorMsg}");
                        throw new InvalidOperationException(errorMsg);
                    }
                    
                    System.Diagnostics.Debug.WriteLine("SDK initialized successfully");
                }

                if (_programmerScanner == null)
                {
                    var errorMsg = "Programmer scanner not available.";
                    System.Diagnostics.Debug.WriteLine($"ERROR: {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Programmers.Clear();
                    FoundProgrammersMessage = "Scanning for wired and wireless programmers...";
                });

                System.Diagnostics.Debug.WriteLine("Starting scan for programmers...");

                var progress = new Progress<int>(p =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FoundProgrammersMessage = $"Scanning... {p}%";
                    });
                    System.Diagnostics.Debug.WriteLine($"Scan progress: {p}%");
                });

                // Scan for all programmers
                var foundProgrammers = await _programmerScanner.ScanAllProgrammersAsync(
                    progress,
                    _cancellationTokenSource.Token);
                
                System.Diagnostics.Debug.WriteLine($"Scan completed. Found {foundProgrammers.Count} programmers");

                // Convert to ViewModels on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StopTimeoutTimer();
                    
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

                        // Store ProgrammerInfo reference for later use
                        programmerVm.ProgrammerInfo = programmerInfo;

                        programmerVm.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(ProgrammerViewModel.IsSelected))
                            {
                                if (programmerVm.IsSelected)
                                {
                                    _selectedProgrammerInfo = programmerInfo;
                                }
                                OnPropertyChanged(nameof(CanDiscover));
                                OnPropertyChanged(nameof(HasSelectedProgrammer));
                                OnPropertyChanged(nameof(SelectedProgrammerName));
                            }
                        };

                        Programmers.Add(programmerVm);
                    }

                    IsSearching = false;
                    HasFoundProgrammers = foundProgrammers.Count > 0;
                    FoundProgrammersMessage = foundProgrammers.Count > 0 
                        ? $"Found {foundProgrammers.Count} programmer(s)" 
                        : "No programmers found. Please check connections and try again.";
                    
                    OnPropertyChanged(nameof(IsSearching));
                    OnPropertyChanged(nameof(HasProgrammers));
                    OnPropertyChanged(nameof(CanDiscover));
                    OnPropertyChanged(nameof(HasFoundProgrammers));
                    OnPropertyChanged(nameof(FoundProgrammersMessage));
                    OnPropertyChanged(nameof(TimeoutMessage));
                    OnPropertyChanged(nameof(CanCancelSearch));
                });
                
                System.Diagnostics.Debug.WriteLine("=== SearchProgrammers_Click completed successfully ===");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Scan was cancelled");
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
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StopTimeoutTimer();
                    IsSearching = false;
                    HasFoundProgrammers = false;
                    FoundProgrammersMessage = $"Error: {ex.Message}";
                    OnPropertyChanged(nameof(IsSearching));
                    OnPropertyChanged(nameof(HasFoundProgrammers));
                    OnPropertyChanged(nameof(FoundProgrammersMessage));
                    OnPropertyChanged(nameof(TimeoutMessage));
                    OnPropertyChanged(nameof(CanCancelSearch));
                    
                    // Show error message to user
                    MessageBox.Show(
                        $"Failed to scan for programmers:\n\n{ex.Message}\n\nPlease check:\n- SDK files are available at: {SdkConfiguration.SdkPath}\n- Library file exists: {SdkConfiguration.GetLibraryPath()}\n- Config file exists: {SdkConfiguration.GetConfigPath()}\n- Programmers are connected\n- Drivers are installed",
                        "Scan Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        private void ProgrammerItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ProgrammerViewModel programmer)
            {
                foreach (var p in Programmers)
                {
                    p.IsSelected = false;
                }
                programmer.IsSelected = true;
                _selectedProgrammerInfo = programmer.ProgrammerInfo;
                OnPropertyChanged(nameof(CanDiscover));
                OnPropertyChanged(nameof(HasSelectedProgrammer));
                OnPropertyChanged(nameof(SelectedProgrammerName));
            }
        }

        private async void DiscoverDevices_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProgrammerInfo == null)
            {
                MessageBox.Show("Please select a programmer first.", "No Programmer Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsDiscovering = true;
            HasFoundDevices = false;
            DiscoveryProgress = 0;
            FoundDevicesMessage = string.Empty;

            try
            {
                // Cancel any previous discovery
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                if (_deviceDiscoveryService == null)
                {
                    throw new InvalidOperationException("Device discovery service not available.");
                }

                var progress = new Progress<int>(p =>
                {
                    DiscoveryProgress = p;
                });

                // Discover both devices
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
                        IsSelected = false
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
                else
                {
                    LeftDevice = null;
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
                        IsSelected = false
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
                else
                {
                    RightDevice = null;
                }

                var deviceCount = (leftDeviceInfo != null ? 1 : 0) + (rightDeviceInfo != null ? 1 : 0);
                HasFoundDevices = deviceCount > 0;
                FoundDevicesMessage = deviceCount > 0 
                    ? $"Found {deviceCount} hearing aid(s)" 
                    : "No hearing aids detected. Please check connections and try again.";
                
                IsDiscovering = false;
                OnPropertyChanged(nameof(HasDevices));
                OnPropertyChanged(nameof(CanConnect));
            }
            catch (OperationCanceledException)
            {
                IsDiscovering = false;
                FoundDevicesMessage = "Discovery cancelled";
            }
            catch (Exception ex)
            {
                IsDiscovering = false;
                HasFoundDevices = false;
                FoundDevicesMessage = $"Error: {ex.Message}";
                
                // Show error message to user
                MessageBox.Show(
                    $"Failed to discover devices:\n{ex.Message}",
                    "Discovery Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _deviceDiscoveryService?.Cleanup();
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

                for (int i = 0; i < devicesToConnect.Count; i++)
                {
                    var (device, side) = devicesToConnect[i];
                    ConnectionStatusMessage = $"Connecting to {device.Side} device...";

                    var progress = new Progress<int>(p =>
                    {
                        // Calculate overall progress across all devices
                        var baseProgress = (i * 100);
                        var deviceProgress = (int)(p * (100.0 / devicesToConnect.Count));
                        ConnectionProgress = baseProgress + deviceProgress;
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
                try
                {
                    if (LeftDevice?.IsSelected == true)
                        await _deviceConnectionService?.DisconnectAsync(DeviceSide.Left);
                    if (RightDevice?.IsSelected == true)
                        await _deviceConnectionService?.DisconnectAsync(DeviceSide.Right);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
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
        public string Name { get; set; }
        public string SerialNumber { get; set; }
        public string Firmware { get; set; }
        public string Port { get; set; }
        
        // Store ProgrammerInfo for SDK operations
        public Ul8ziz.FittingApp.Device.DeviceCommunication.Models.ProgrammerInfo? ProgrammerInfo { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
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
