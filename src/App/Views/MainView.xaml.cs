using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Services.Diagnostics;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : UserControl
    {
        private MainViewModel _viewModel;
        private ConnectDevicesView? _cachedConnectDevicesView;
        // AudiogramView is cached so the AudiogramViewModel (and its per-memory audiogram data) survives navigation.
        // Creating a new AudiogramView on every navigation would recreate the VM and lose all entered clinical data.
        private AudiogramView? _cachedAudiogramView;

        public MainView()
        {
            InitializeComponent();
            _viewModel = new MainViewModel()
            {
                CurrentView = CreateWelcomeView()
            };
            _viewModel.CreateConnectView = () =>
            {
                if (_cachedConnectDevicesView == null)
                {
                    _cachedConnectDevicesView = new ConnectDevicesView();
                    _cachedConnectDevicesView.OnConnectionSucceeded = () =>
                    {
                        _viewModel.CurrentNavKey = "Fitting";
                        _viewModel.CurrentView = new FittingView();
                    };
                }
                return _cachedConnectDevicesView;
            };
            _viewModel.CreateAudiogramView = () =>
            {
                if (_cachedAudiogramView == null)
                {
                    _cachedAudiogramView = new AudiogramView(navKey =>
                    {
                        if (!AppSessionState.Instance.IsNavigationEnabled) return;
                        _viewModel.CurrentNavKey = navKey;
                        _viewModel.CurrentView   = navKey == "Fitting" ? (UserControl)new FittingView() : GetOrCreateAudiogramView();
                    });
                }
                return _cachedAudiogramView;
            };
            _viewModel.ShowEndSessionDialog = () =>
            {
                var w = new EndSessionDialogWindow { Owner = Window.GetWindow(this) };
                w.ShowDialog();
                return w.Result;
            };
            _viewModel.NavigateToConnectAndRestartDiscovery = () =>
            {
                var connectView = _viewModel.CreateConnectView?.Invoke() ?? new ConnectDevicesView();
                connectView.ResetForInactiveConnection();
                _viewModel.CurrentNavKey = "ConnectDevices";
                _viewModel.CurrentView = connectView;
                connectView.StartWiredDiscoveryAfterDebounce();
            };
            // Wire audiogram flush: called before session end to guarantee disk persistence.
            // The lambda captures _cachedAudiogramView by reference — if the user never
            // navigated to Audiogram, _cachedAudiogramView is null and the flush is a no-op.
            _viewModel.FlushAudiogramAsync = async () =>
            {
                if (_cachedAudiogramView?.DataContext is ViewModels.AudiogramViewModel vm)
                    await vm.FlushAsync();
            };
            _viewModel.ShowToast = msg =>
            {
                _viewModel.SessionEndBannerText = msg;
                _viewModel.SessionEndBannerVisible = true;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    _viewModel.SessionEndBannerVisible = false;
                };
                timer.Start();
            };
            _viewModel.ShowWaitingDialog = () =>
            {
                try
                {
                    var owner = Window.GetWindow(this);
                    _sessionEndWaitingWindow = new SessionEndWaitingWindow();
                    if (owner != null) _sessionEndWaitingWindow.Owner = owner;
                    _sessionEndWaitingWindow.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ShowWaitingDialog: {ex.Message}");
                    _sessionEndWaitingWindow = null;
                }
            };
            _viewModel.HideWaitingDialog = () =>
            {
                _sessionEndWaitingWindow?.Close();
                _sessionEndWaitingWindow = null;
            };
            DataContext = _viewModel;
        }

        private SessionEndWaitingWindow? _sessionEndWaitingWindow;

        private static UserControl CreateWelcomeView()
        {
            return new UserControl
            {
                Content = new TextBlock
                {
                    Text = "Welcome to Hearing Aid Fitting System\n\nSelect a view from the sidebar to begin.",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(40)
                }
            };
        }

        private AudiogramView GetOrCreateAudiogramView() =>
            _viewModel.CreateAudiogramView?.Invoke() ?? new AudiogramView();

        private void NavigateToConnectDevices()
        {
            _viewModel.CurrentNavKey = "ConnectDevices";
            _viewModel.CurrentView = _viewModel.CreateConnectView?.Invoke() ?? new ConnectDevicesView();
        }

        private void ConnectDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToConnectDevices();
        }
    }
    
    // ViewModel for MainView; connection state from AppSessionState, session state from DeviceSessionService (single source of truth for header)
    public class MainViewModel : INotifyPropertyChanged
    {
        private UserControl? _currentView;
        private string _currentNavKey = string.Empty;
        private ICommand? _endSessionCommand;
        private ICommand? _navigateCommand;
        private bool _sessionEndBannerVisible;
        private string _sessionEndBannerText = string.Empty;

        public MainViewModel(UserControl? initialView = null)
        {
            var appSession = AppSessionState.Instance;
            appSession.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
            var deviceSession = DeviceSessionService.Instance;
            deviceSession.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceSessionService.HasActiveSession))
                    OnPropertyChanged(nameof(HasActiveSession));
                if (e.PropertyName == nameof(DeviceSessionService.HasDirty))
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                if (e.PropertyName == nameof(DeviceSessionService.IsConfigureRunning))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    OnPropertyChanged(nameof(CanNavigateToConnect));
                }
            };
        }

        public bool HasActiveSession => DeviceSessionService.Instance.HasActiveSession;
        public bool HasUnsavedChanges => DeviceSessionService.Instance.HasDirty;
        /// <summary>False while Configure Device is running (disables Connect Devices and End Session).</summary>
        public bool CanNavigateToConnect => !DeviceSessionService.Instance.IsConfigureRunning;

        public bool SessionEndBannerVisible
        {
            get => _sessionEndBannerVisible;
            set { _sessionEndBannerVisible = value; OnPropertyChanged(); }
        }
        public string SessionEndBannerText
        {
            get => _sessionEndBannerText;
            set { _sessionEndBannerText = value ?? ""; OnPropertyChanged(); }
        }

        public Func<ConnectDevicesView>? CreateConnectView { get; set; }
        /// <summary>
        /// Factory injected by MainView. Returns the single cached AudiogramView instance so the
        /// AudiogramViewModel (and its per-memory clinical data) survive sidebar navigation.
        /// </summary>
        public Func<AudiogramView>? CreateAudiogramView { get; set; }
        public Func<EndSessionDialogResult>? ShowEndSessionDialog { get; set; }
        public Action? NavigateToConnectAndRestartDiscovery { get; set; }
        public Action<string>? ShowToast { get; set; }
        public Action? ShowWaitingDialog { get; set; }
        public Action? HideWaitingDialog { get; set; }

        /// <summary>
        /// Injected by MainView. Flushes AudiogramViewModel clinical data to disk before
        /// the device session ends. Awaited in EndSession so the write is guaranteed to
        /// complete before SDK teardown begins.
        /// </summary>
        public Func<Task>? FlushAudiogramAsync { get; set; }

        public string ConnectionStatusText => AppSessionState.Instance.ConnectionStatusText;
        public SolidColorBrush? ConnectionStatusBrush => AppSessionState.Instance.ConnectionStatusBrush as SolidColorBrush;
        public string ConnectedDeviceDisplayName => AppSessionState.Instance.ConnectedDeviceDisplayName;
        public bool IsConnectedDeviceNameVisible => AppSessionState.Instance.IsConnectedDeviceNameVisible;
        public bool IsNavigationEnabled => AppSessionState.Instance.IsNavigationEnabled;

        public UserControl? CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        /// <summary>Current navigation key for sidebar highlight (ConnectDevices, Audiogram, Fitting, SessionSummary, or empty).</summary>
        public string CurrentNavKey
        {
            get => _currentNavKey;
            set { _currentNavKey = value ?? string.Empty; OnPropertyChanged(); }
        }

        public ICommand EndSessionCommand
        {
            get
            {
                return _endSessionCommand ??= new RelayCommand(
                    _ => EndSession(),
                    _ => HasActiveSession && !DeviceSessionService.Instance.IsConfigureRunning);
            }
        }

        public ICommand NavigateCommand
        {
            get
            {
                return _navigateCommand ??= new RelayCommand(
                    parameter => Navigate(parameter?.ToString() ?? string.Empty),
                    _ => true);
            }
        }

        private async void EndSession()
        {
            var result = ShowEndSessionDialog?.Invoke() ?? EndSessionDialogResult.Cancel;
            if (result == EndSessionDialogResult.Cancel)
                return;

            // Flush audiogram clinical data to disk BEFORE device disconnects.
            // FlushAsync is safe — it catches all exceptions internally and is a no-op
            // when no audiogram data exists. This guarantees persistence across restarts.
            if (FlushAudiogramAsync != null)
            {
                try   { await FlushAudiogramAsync(); }
                catch { /* FlushAsync itself does not throw; belt-and-suspenders guard */ }
            }

            var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _ = SessionEndService.ExecuteEndSessionAsync(result, ShowToast ?? (_ => { }), NavigateToConnectAndRestartDiscovery ?? (() => { }), dispatcher, ShowWaitingDialog, HideWaitingDialog);
        }

        private static UserControl CreateWelcomeView()
        {
            return new UserControl
            {
                Content = new TextBlock
                {
                    Text = "Welcome to Hearing Aid Fitting System\n\nSelect a view from the sidebar to begin.",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(40)
                }
            };
        }

        private void Navigate(string viewName)
        {
            // Audiogram, Fitting, and SessionSummary all require an active device connection.
            // Only Connect Devices is allowed without a connection.
            if (!AppSessionState.Instance.IsNavigationEnabled && viewName != "ConnectDevices")
                return;
            UserControl? newView = viewName switch
            {
                "ConnectDevices" => CreateConnectView?.Invoke() ?? new ConnectDevicesView(),
                // Return the SAME cached AudiogramView each time so its VM and per-memory data survive navigation.
                "Audiogram" => CreateAudiogramView?.Invoke() ?? new AudiogramView(),
                "Fitting" => new FittingView(),
                "SessionSummary" => new UserControl
                {
                    Content = new TextBlock 
                    { 
                        Text = "Session Summary view\n\nThis view is not yet implemented.",
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(40)
                    }
                },
                _ => CreateWelcomeView()
            };
            
            CurrentNavKey = viewName switch
            {
                "ConnectDevices" => "ConnectDevices",
                "Audiogram" => "Audiogram",
                "Fitting" => "Fitting",
                "SessionSummary" => "SessionSummary",
                _ => ""
            };
            DiagnosticContextGatherer.CurrentScreen = string.IsNullOrEmpty(CurrentNavKey) ? null : CurrentNavKey;
            CurrentView = newView;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add 
            { 
                if (_canExecute != null)
                {
                    CommandManager.RequerySuggested += value;
                }
            }
            remove 
            { 
                if (_canExecute != null)
                {
                    CommandManager.RequerySuggested -= value;
                }
            }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }
    }
}
