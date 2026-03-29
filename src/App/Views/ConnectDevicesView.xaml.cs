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
using Ul8ziz.FittingApp.App.Services.Diagnostics;

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

        private string? _lastSdkError;

        // SDK Services
        private SdkManager? _sdkManager;
        private ProgrammerScanner? _programmerScanner;
        private DeviceDiscoveryService? _deviceDiscoveryService;
        private DeviceConnectionService? _deviceConnectionService;
        private ProgrammerInfo? _selectedProgrammerInfo;
        private CancellationTokenSource? _cancellationTokenSource;

        // Library Selection
        private ObservableCollection<LibraryInfo> _availableLibraries = new ObservableCollection<LibraryInfo>();
        public ObservableCollection<LibraryInfo> AvailableLibraries
        {
            get => _availableLibraries;
            set { _availableLibraries = value; OnPropertyChanged(); }
        }

        private LibraryInfo? _selectedLibrary;
        public LibraryInfo? SelectedLibrary
        {
            get => _selectedLibrary;
            set
            {
                if (_selectedLibrary != value)
                {
                    _selectedLibrary = value;
                    OnPropertyChanged();
                    if (_selectedLibrary != null)
                    {
                        _ = LoadDefaultLibraryAsync(_selectedLibrary.FullPath);
                    }
                }
            }
        }

        // D2XX-first: HI-PRO via FTDI D2XX (no COM/CTK for wired scan)
        private readonly HiProService _hiProService = new HiProService();

        /// <summary>Invoked when connection to device(s) succeeds. MainView sets this to navigate to Quick Fitting.</summary>
        public Action? OnConnectionSucceeded { get; set; }

        public ConnectDevicesView()
        {
            InitializeComponent();
            DataContext = this;
            
            // SDK init is deferred to first scan to avoid constructor crash
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (AvailableLibraries.Count == 0)
            {
                // Do not init if session is active or teardown is in progress
                if (DeviceSessionService.Instance.HasActiveSession || SdkLifecycle.IsDisposingOrDisposed)
                {
                    System.Diagnostics.Debug.WriteLine("[ConnectDevices] Loaded: skip SDK init (active session or teardown)");
                    return;
                }
                Dispatcher.BeginInvoke(new Action(async () => await InitializeSdkServicesAsync()), DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Lazily initializes SDK services using the single shared SdkManager from DeviceSessionService.
        /// Skips if session is active or lifecycle is Disposing/Disposed. Never creates ProductManager outside SdkManager.
        /// </summary>
        private async Task InitializeSdkServicesAsync()
        {
            if (SdkLifecycle.IsDisposingOrDisposed || DeviceSessionService.Instance.HasActiveSession)
            {
                System.Diagnostics.Debug.WriteLine("[ConnectDevices] InitializeSdkServicesAsync skipped (teardown or active session)");
                return;
            }
            if (_sdkManager?.IsInitialized == true)
                return;

            _lastSdkError = null;

            try
            {
                System.Diagnostics.Debug.WriteLine("=== Initializing SDK services (shared SdkManager) ===");
                var libraryPath = SdkConfiguration.GetLibraryPath();
                var configPath = SdkConfiguration.GetConfigPath();
                System.Diagnostics.Debug.WriteLine($"Library Path: {libraryPath}");
                System.Diagnostics.Debug.WriteLine($"Config Path: {configPath}");

                // Enumerate libraries (no SDK required)
                var availableLibraries = LibraryService.EnumerateLibraries();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailableLibraries.Clear();
                    foreach (var lib in availableLibraries)
                        AvailableLibraries.Add(lib);
                    if (AvailableLibraries.Count > 0 && SelectedLibrary == null)
                        SelectedLibrary = AvailableLibraries[0];
                });

                // Single SdkManager: create/init via gate from DeviceSessionService
                var sdkManager = await DeviceSessionService.Instance.EnsureSdkReadyForScanAsync(libraryPath).ConfigureAwait(true);
                _sdkManager = sdkManager;
                _programmerScanner = new ProgrammerScanner(_sdkManager);
                _deviceDiscoveryService = new DeviceDiscoveryService(_sdkManager);
                _deviceConnectionService = new DeviceConnectionService(_sdkManager);

                System.Diagnostics.Debug.WriteLine("=== SDK services initialized (shared SdkManager) ===");
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.RecordException("SdkInit", DiagnosticCategory.Connection, ex, "ConnectDevices", null, null);
                var errorDetail = ex.Message;
                if (ex.InnerException != null)
                    errorDetail += $"\nCause: {ex.InnerException.Message}";
                System.Diagnostics.Debug.WriteLine($"=== SDK initialization FAILED ===\n{errorDetail}");
                _lastSdkError = errorDetail;
                _sdkManager = null;
                _programmerScanner = null;
                _deviceDiscoveryService = null;
                _deviceConnectionService = null;
            }
        }

        /// <summary>Loads the default library into FittingSessionManager for offline parameter browsing.
        /// Also auto-loads the matching .param file if available (e.g., E7111V2.param for E7111V2.library).</summary>
        private async Task LoadDefaultLibraryAsync(string libraryPath)
        {
            try
            {
                await FittingSessionManager.Instance.CreateOfflineSessionAsync(libraryPath);
                System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Default library loaded for offline browsing: {System.IO.Path.GetFileName(libraryPath)}");

                // Auto-load matching .param file (e.g., E7111V2.param for E7111V2.library)
                var libraryFileName = System.IO.Path.GetFileName(libraryPath);
                var paramPath = ParamFileService.FindParamForLibrary(libraryFileName);
                if (paramPath != null)
                {
                    try
                    {
                        await FittingSessionManager.Instance.ApplyParamFileAsync(paramPath);
                        System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Auto-loaded .param: {System.IO.Path.GetFileName(paramPath)}");
                        OnPropertyChanged(nameof(ParamFileStatus));
                    }
                    catch (Exception paramEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Failed to auto-load .param: {paramEx.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ConnectDevices] No matching .param file found for {libraryFileName}");
                }
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.RecordException("LoadLibrary", DiagnosticCategory.Persistence, ex, "ConnectDevices", null, null);
                System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Failed to load default library: {ex.Message}");
            }
        }

        /// <summary>Status text showing loaded .param file name (for UI display).</summary>
        public string ParamFileStatus => FittingSessionManager.Instance.ParamFileName != null
            ? $"Preset: {FittingSessionManager.Instance.ParamFileName}"
            : "No preset loaded";

        // Properties
        public bool IsSearching
        {
            get => _isSearching;
            set { _isSearching = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBarText)); }
        }

        public bool HasFoundProgrammers
        {
            get => _hasFoundProgrammers;
            set { _hasFoundProgrammers = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBarText)); }
        }

        public string FoundProgrammersMessage
        {
            get => _foundProgrammersMessage;
            set { _foundProgrammersMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBarText)); }
        }

        public bool ScanCompleted
        {
            get => _scanCompleted;
            set { _scanCompleted = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScanFoundNothing)); OnPropertyChanged(nameof(StatusBarText)); }
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
            set { _leftDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(ShowLeftDevice)); OnPropertyChanged(nameof(SelectedDevicesCount)); OnPropertyChanged(nameof(SelectedDevicesSummary)); OnPropertyChanged(nameof(CanConnect)); }
        }

        private HearingAidViewModel? _rightDevice;
        public HearingAidViewModel? RightDevice
        {
            get => _rightDevice;
            set { _rightDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(ShowRightDevice)); OnPropertyChanged(nameof(SelectedDevicesCount)); OnPropertyChanged(nameof(SelectedDevicesSummary)); OnPropertyChanged(nameof(CanConnect)); }
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

        /// <summary>Summary text for footer, e.g. "Selected: Left (1)".</summary>
        public string SelectedDevicesSummary
        {
            get
            {
                if (SelectedDevicesCount == 0) return "No devices selected";
                var parts = new List<string>();
                if (LeftDevice?.IsSelected == true) parts.Add("Left");
                if (RightDevice?.IsSelected == true) parts.Add("Right");
                return $"Selected: {string.Join(", ", parts)} ({SelectedDevicesCount})";
            }
        }

        /// <summary>Status bar text when programmer is found (reference: "HI-PRO Found | USB OK | Driver OK").</summary>
        public string StatusBarText
        {
            get
            {
                if (IsSearching) return "Searching...";
                if (ScanFoundNothing) return FoundProgrammersMessage;
                if (HasFoundProgrammers)
                {
                    var name = SelectedProgrammerName;
                    if (string.IsNullOrEmpty(name)) name = "Programmer";
                    return $"{name} Found | USB OK | Driver OK";
                }
                return "Scan and select a programmer to begin";
            }
        }

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

        /// <summary>Resets connection UI state so the view shows "connection inactive" and allows headset selection (e.g. after session end or save failure).</summary>
        public void ResetForInactiveConnection()
        {
            IsConnected = false;
            OnPropertyChanged(nameof(CanConnect));
        }

        /// <summary>Called after session end when navigating back to Connect. Starts wired discovery after 500ms debounce.</summary>
        public void StartWiredDiscoveryAfterDebounce()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                RunScanAsync(ScanKind.Wired);
            };
            timer.Start();
        }

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
                            Description = dev.Description,
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
                        await InitializeSdkServicesAsync().ConfigureAwait(true);
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
                        Description = programmerInfo.Description,
                        Backend = programmerInfo.Type == ProgrammerType.Wired ? "D2XX" : "Wireless",
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

                // Auto-select when only one programmer is found so it clearly shows as selected
                if (foundProgrammers.Count == 1)
                {
                    Programmers[0].IsSelected = true;
                    _selectedProgrammerInfo = foundProgrammers[0];
                    OnPropertyChanged(nameof(CanDiscover));
                    OnPropertyChanged(nameof(HasSelectedProgrammer));
                    OnPropertyChanged(nameof(SelectedProgrammerName));
                    OnPropertyChanged(nameof(StatusBarText));
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
                        ? "Wired scan uses D2XX only. Check log.txt in the app folder for details."
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
                DiagnosticService.Instance.RecordException("ScanProgrammers", DiagnosticCategory.Scan, ex, "ConnectDevices", null, null);
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
                OnPropertyChanged(nameof(StatusBarText));

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
                    await InitializeSdkServicesAsync().ConfigureAwait(true);
                    if (_sdkManager == null || !_sdkManager.IsInitialized)
                    {
                        var msg = string.IsNullOrEmpty(_lastSdkError)
                            ? "SDK could not be initialized. Check sd.config, library file, and CTK Runtime (x86)."
                            : "SDK initialization failed: " + _lastSdkError;
                        throw new InvalidOperationException(msg);
                    }

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

                    // Wired discovery uses the SHARED SdkManager (single ProductManager, all calls through SdkGate).
                    var wiredDiscoveryService = new HiProWiredDiscoveryService(_sdkManager!, LogDiscovery);
                    var discoveryResult = await wiredDiscoveryService.DetectBothAsync(ct).ConfigureAwait(true);
                    leftDeviceInfo = discoveryResult.FoundLeft ? new DeviceInfo
                    {
                        Side = DeviceSide.Left,
                        Model = !string.IsNullOrEmpty(discoveryResult.LeftProductId) && discoveryResult.LeftProductId != "0"
                            ? discoveryResult.LeftProductId
                            : discoveryResult.LeftFirmwareId ?? string.Empty,
                        SerialNumber = discoveryResult.LeftSerialId ?? "Unknown",
                        Firmware = discoveryResult.LeftFirmwareId ?? string.Empty,
                        ProductId = discoveryResult.LeftProductId,
                        ChipId = discoveryResult.LeftChipId,
                        HybridId = discoveryResult.LeftHybridId,
                        HybridSerial = discoveryResult.LeftHybridSerial,
                        ParameterLockState = discoveryResult.LeftParameterLockState,
                        IsDetected = true
                    } : null;
                    rightDeviceInfo = discoveryResult.FoundRight ? new DeviceInfo
                    {
                        Side = DeviceSide.Right,
                        Model = !string.IsNullOrEmpty(discoveryResult.RightProductId) && discoveryResult.RightProductId != "0"
                            ? discoveryResult.RightProductId
                            : discoveryResult.RightFirmwareId ?? string.Empty,
                        SerialNumber = discoveryResult.RightSerialId ?? "Unknown",
                        Firmware = discoveryResult.RightFirmwareId ?? string.Empty,
                        ProductId = discoveryResult.RightProductId,
                        ChipId = discoveryResult.RightChipId,
                        HybridId = discoveryResult.RightHybridId,
                        HybridSerial = discoveryResult.RightHybridSerial,
                        ParameterLockState = discoveryResult.RightParameterLockState,
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
                    await InitializeSdkServicesAsync().ConfigureAwait(true);
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
                        Model = !string.IsNullOrEmpty(leftDeviceInfo.Model) ? leftDeviceInfo.Model : leftDeviceInfo.Firmware,
                        SerialNumber = leftDeviceInfo.SerialNumber,
                        Firmware = leftDeviceInfo.Firmware,
                        ProductId = leftDeviceInfo.ProductId,
                        ChipId = leftDeviceInfo.ChipId,
                        BatteryLevel = leftDeviceInfo.BatteryLevel ?? 0,
                        HasBatterySupport = leftDeviceInfo.BatteryLevel.HasValue && leftDeviceInfo.BatteryLevel.Value > 0,
                        ParameterLockState = leftDeviceInfo.ParameterLockState,
                        Status = "Detected",
                        IsSelected = true
                    };
                    leftVm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(HearingAidViewModel.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedDevicesCount));
                            OnPropertyChanged(nameof(SelectedDevicesSummary));
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
                        Model = !string.IsNullOrEmpty(rightDeviceInfo.Model) ? rightDeviceInfo.Model : rightDeviceInfo.Firmware,
                        SerialNumber = rightDeviceInfo.SerialNumber,
                        Firmware = rightDeviceInfo.Firmware,
                        ProductId = rightDeviceInfo.ProductId,
                        ChipId = rightDeviceInfo.ChipId,
                        BatteryLevel = rightDeviceInfo.BatteryLevel ?? 0,
                        HasBatterySupport = rightDeviceInfo.BatteryLevel.HasValue && rightDeviceInfo.BatteryLevel.Value > 0,
                        ParameterLockState = rightDeviceInfo.ParameterLockState,
                        Status = "Detected",
                        IsSelected = true
                    };
                    rightVm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(HearingAidViewModel.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedDevicesCount));
                            OnPropertyChanged(nameof(SelectedDevicesSummary));
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
                DiagnosticService.Instance.RecordException("DiscoverDevices", DiagnosticCategory.Scan, ex, "ConnectDevices", null, null);
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
            OnPropertyChanged(nameof(SelectedDevicesSummary));
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

            // ========== HARD GATE: Validate firmware and serial from discovery ==========
            // Detect the firmware from the first selected device (Left preferred).
            string? detectedFirmware = null;
            foreach (var (device, side) in devicesToConnect)
            {
                if (!string.IsNullOrWhiteSpace(device.Firmware))
                {
                    detectedFirmware = device.Firmware;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(detectedFirmware))
            {
                System.Diagnostics.Debug.WriteLine("[ConnectDevices] HARD GATE: No firmware detected on any selected device. Aborting connect.");
                MessageBox.Show(
                    "No firmware ID was detected during device discovery.\n\nPlease re-seat the hearing aid on the programmer and try Discover again.",
                    "Cannot Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Note: Serial=-1 or SerialId=0 from discovery/connect is NORMAL for devices
            // that haven't been through manufacturing configuration. It does NOT indicate
            // a handshake failure. The firmware ID is the reliable indicator of a valid device.

            IsConnecting = true;
            ConnectionProgress = 0;
            ConnectionStatusMessage = string.Empty;
            AppSessionState.Instance.ConnectionState = ConnectionState.Connecting;

            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                await InitializeSdkServicesAsync().ConfigureAwait(true);
                if (_sdkManager == null || _deviceConnectionService == null)
                {
                    var msg = string.IsNullOrEmpty(_lastSdkError)
                        ? "SDK could not be initialized. Check sd.config, library file, and CTK Runtime (x86)."
                        : "SDK initialization failed: " + _lastSdkError;
                    throw new InvalidOperationException(msg);
                }

                // ========== CRITICAL: Reload SDK with library matching detected firmware (inside SdkGate) ==========
                try
                {
                    ConnectionStatusMessage = "Matching library to device firmware...";
                    System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Reloading SDK for detected firmware: {detectedFirmware}");
                    await SdkGate.InvokeAsync(() =>
                    {
                        _sdkManager!.ReloadForFirmware(detectedFirmware);
                        _deviceConnectionService = new DeviceConnectionService(_sdkManager);
                    }, "ReloadForFirmware").ConfigureAwait(true);
                    System.Diagnostics.Debug.WriteLine($"[ConnectDevices] SDK reloaded OK. Product matches firmware: {detectedFirmware}");
                }
                catch (Exception ex)
                {
                    DiagnosticService.Instance.RecordException("ReloadForFirmware", DiagnosticCategory.Connection, ex, "ConnectDevices", null, null);
                    System.Diagnostics.Debug.WriteLine($"[ConnectDevices] ReloadForFirmware FAILED: {ex.Message}");
                    throw new InvalidOperationException(
                        $"No library found for firmware '{detectedFirmware}'.\n\n" +
                        "Ensure the matching .library file is in Assets\\SoundDesigner\\products\\.", ex);
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

                        // Post-connect validation: reject ONLY if firmware is empty (real failure).
                        // Serial=-1 is normal for unprogrammed devices — NOT a handshake error.
                        if (string.IsNullOrEmpty(deviceInfo.Firmware))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Post-connect HARD GATE: {side} has empty Firmware after connect. Connection may be corrupted.");
                            throw new InvalidOperationException($"Device on {side} returned no firmware ID after connection. Re-seat and retry.");
                        }

                        if (deviceInfo.SerialNumber == "-1" || deviceInfo.SerialNumber == "0")
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConnectDevices] INFO: {side} Serial={deviceInfo.SerialNumber} — unprogrammed device (normal for dev/test units).");
                        }

                        device.Model = !string.IsNullOrEmpty(deviceInfo.Model) ? deviceInfo.Model : deviceInfo.Firmware;
                        device.SerialNumber = deviceInfo.SerialNumber;
                        device.Firmware = deviceInfo.Firmware;
                        device.ProductId = deviceInfo.ProductId;
                        device.ChipId = deviceInfo.ChipId;
                        device.ParameterLockState = deviceInfo.ParameterLockState;
                        device.Status = "Connected";
                        if (side == DeviceSide.Left) result.LeftConnected = true;
                        else result.RightConnected = true;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticService.Instance.RecordException("ConnectDevice", DiagnosticCategory.Connection, ex, "ConnectDevices", null, side);
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
                    DeviceSessionService.Instance.SetSession(
                        _sdkManager!,
                        _deviceConnectionService!,
                        _selectedProgrammerInfo!,
                        displayName,
                        result.LeftConnected,
                        result.RightConnected);
                    // Set device identity so Configure Device (manufacturing) can match library to firmware.
                    var firmwareId = (result.LeftConnected ? LeftDevice?.Firmware : null) ?? (result.RightConnected ? RightDevice?.Firmware : null)
                        ?? _sdkManager!.LoadedFirmwareId;
                    DeviceSessionService.Instance.SetDeviceIdentity(
                        firmwareId,
                        result.LeftConnected ? LeftDevice?.SerialNumber : null,
                        result.RightConnected ? RightDevice?.SerialNumber : null,
                        result.LeftConnected ? LeftDevice?.Model : null,
                        result.RightConnected ? RightDevice?.Model : null);
                    // Sync FittingSessionManager so IsDeviceConfigured reflects actual read capability
                    // (e.g. E_UNCONFIGURED_DEVICE on unprogrammed device → Save stays disabled).
                    var product = _sdkManager!.GetProduct();
                    if (product != null)
                    {
                        try
                        {
                            await FittingSessionManager.Instance.SyncDeviceStateFromConnectionAsync(
                                product,
                                _deviceConnectionService!.GetConnection(DeviceSide.Left),
                                _deviceConnectionService!.GetConnection(DeviceSide.Right),
                                result.LeftConnected,
                                result.RightConnected,
                                _cancellationTokenSource?.Token ?? default);
                        }
                        catch (Exception syncEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConnectDevices] SyncDeviceStateFromConnection failed: {syncEx.Message}");
                        }
                    }
                    // Gate: ensure session state reflects actual init/configured (e.g. E_UNCONFIGURED_DEVICE → Not Configured).
                    var session = DeviceSessionService.Instance;
                    if (result.LeftConnected)
                    {
                        try
                        {
                            await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(session, DeviceSide.Left, _cancellationTokenSource?.Token ?? default);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConnectDevices] EnsureInitializedAndConfigured(Left) failed: {ex.Message}");
                        }
                    }
                    if (result.RightConnected)
                    {
                        try
                        {
                            await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(session, DeviceSide.Right, _cancellationTokenSource?.Token ?? default);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConnectDevices] EnsureInitializedAndConfigured(Right) failed: {ex.Message}");
                        }
                    }
                    var successMsg = result.Errors.Count > 0
                        ? $"Connected to selected device(s).\n\n{string.Join("\n", result.Errors)}"
                        : "Successfully connected to all selected devices.";
                    MessageBox.Show(successMsg, "Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                    _ = LoadDeviceInfoAsync();
                    OnConnectionSucceeded?.Invoke();
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
                DiagnosticService.Instance.RecordException("Connect", DiagnosticCategory.Connection, ex, "ConnectDevices", null, null);
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

        /// <summary>Loads device identity and attempts to read battery voltage from Product.BatteryAverageVoltage (sounddesigner_programmers_guide.pdf §7 Live Display).</summary>
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

                // Try to read battery voltage from Product.BatteryAverageVoltage (documented API)
                var product = _sdkManager?.GetProduct();
                if (product != null)
                {
                    try
                    {
                        var voltage = TryReadBatteryAverageVoltage(product);
                        if (voltage.HasValue)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (LeftDevice != null) { LeftDevice.BatteryVoltageV = voltage; OnPropertyChanged(nameof(LeftDevice)); }
                                if (RightDevice != null) { RightDevice.BatteryVoltageV = voltage; OnPropertyChanged(nameof(RightDevice)); }
                            });
                            System.Diagnostics.Debug.WriteLine($"[ConnectDevices] BatteryAverageVoltage: {voltage:F3} V");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConnectDevices] Battery read: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConnectDevices] LoadDeviceInfoAsync error: {ex.Message}");
            }
        }

        /// <summary>Tries to read Product.BatteryAverageVoltage via reflection. Returns null if not supported.</summary>
        private static double? TryReadBatteryAverageVoltage(SDLib.IProduct product)
        {
            if (product == null) return null;
            try
            {
                var prop = product.GetType().GetProperty("BatteryAverageVoltage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null || !prop.CanRead) return null;
                var val = prop.GetValue(product);
                if (val is double d && d >= 0) return d;
                if (val is float f && f >= 0) return f;
                if (val is int i && i >= 0) return i / 1000.0; // assume mV
                return null;
            }
            catch { return null; }
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
        /// <summary>Optional description from hardware (e.g. D2XX device description).</summary>
        public string? Description { get; set; }
        /// <summary>Communication backend: D2XX for wired, Wireless for wireless.</summary>
        public string Backend { get; set; } = string.Empty;
        
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
        private double? _batteryVoltageV;

        public string Side { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Firmware { get; set; } = string.Empty;
        public string? ProductId { get; set; }
        public string? ChipId { get; set; }
        public int BatteryLevel { get; set; }
        /// <summary>True when battery level (percentage) is available from SDK/device. When false, battery percentage UI is hidden.</summary>
        public bool HasBatterySupport { get; set; }
        /// <summary>Battery voltage in volts (from Product.BatteryAverageVoltage when available). Null when not readable.</summary>
        public double? BatteryVoltageV { get => _batteryVoltageV; set { _batteryVoltageV = value; OnPropertyChanged(); } }
        /// <summary>True when device parameters are locked.</summary>
        public bool ParameterLockState { get; set; }
        /// <summary>Connection status: Detected, Connected, etc.</summary>
        public string Status { get; set; } = "Detected";

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
