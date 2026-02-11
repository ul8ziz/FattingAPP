using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Ul8ziz.FittingApp.App.Services;

namespace Ul8ziz.FittingApp.App.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : UserControl
    {
        private ConnectDevicesView? _connectDevicesView;
        private MainViewModel _viewModel;
        
        public MainView()
        {
            InitializeComponent();
            _viewModel = new MainViewModel
            {
                HasActiveSession = false,
                HasUnsavedChanges = false,
                UserName = "Dr. Sarah Johnson",
                UserRole = "Licensed Audiologist",
                CurrentView = CreateWelcomeView()
            };
            this.DataContext = _viewModel;
        }
        
        private UserControl CreateWelcomeView()
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
        
        private void NavigateToConnectDevices()
        {
            if (_connectDevicesView == null)
            {
                _connectDevicesView = new ConnectDevicesView();
            }
            
            _viewModel.CurrentView = _connectDevicesView;
        }
        
        private void ConnectDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToConnectDevices();
        }

        private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            bool ran = Helpers.DiagnosticRunner.RunDiagnostics(AppDomain.CurrentDomain.BaseDirectory, autoStopStarkeyInspire: false, reportToLogsDiagnostics: true);
            if (ran)
            {
                MessageBox.Show(
                    "Diagnostics completed. Reports saved to:\n\n• docs\\HI-PRO_Diagnostic_Report.md\n• docs\\reports\\HI-PRO_Report_*.md\n• logs\\diagnostics\\HI-PRO_DiagnosticReport.md\n• logs\\diagnostics\\*.json",
                    "HI-PRO Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Diagnostic scripts were not found. Run the app from the repository root (or ensure scripts\\diagnostics\\05_summary_runner.ps1 exists).",
                    "HI-PRO Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
    
    // ViewModel for MainView; connection state comes from AppSessionState
    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _hasActiveSession;
        private bool _hasUnsavedChanges;
        private string _userName = string.Empty;
        private string _userRole = string.Empty;
        private UserControl? _currentView;
        private ICommand? _endSessionCommand;
        private ICommand? _navigateCommand;

        public MainViewModel()
        {
            var session = AppSessionState.Instance;
            session.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        }

        public bool HasActiveSession
        {
            get => _hasActiveSession;
            set { _hasActiveSession = value; OnPropertyChanged(); }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { _hasUnsavedChanges = value; OnPropertyChanged(); }
        }

        public string ConnectionStatusText => AppSessionState.Instance.ConnectionStatusText;
        public SolidColorBrush? ConnectionStatusBrush => AppSessionState.Instance.ConnectionStatusBrush as SolidColorBrush;
        public string ConnectedDeviceDisplayName => AppSessionState.Instance.ConnectedDeviceDisplayName;
        public bool IsConnectedDeviceNameVisible => AppSessionState.Instance.IsConnectedDeviceNameVisible;
        public bool IsNavigationEnabled => AppSessionState.Instance.IsNavigationEnabled;

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string UserRole
        {
            get => _userRole;
            set { _userRole = value; OnPropertyChanged(); }
        }

        public UserControl? CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        public ICommand EndSessionCommand
        {
            get
            {
                return _endSessionCommand ??= new RelayCommand(
                    _ => EndSession(),
                    _ => HasActiveSession);
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

        private void EndSession()
        {
            HasActiveSession = false;
            HasUnsavedChanges = false;
            AppSessionState.Instance.SetNotConnected();
            CurrentView = CreateWelcomeView();
        }

        private UserControl CreateWelcomeView()
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
            if (!AppSessionState.Instance.IsNavigationEnabled && viewName != "ConnectDevices")
                return;
            UserControl? newView = viewName switch
            {
                "ConnectDevices" => new ConnectDevicesView(),
                "PatientManagement" => new UserControl
                {
                    Content = new TextBlock 
                    { 
                        Text = "Patient Management view\n\nThis view is not yet implemented.",
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(40)
                    }
                },
                "Audiogram" => new UserControl
                {
                    Content = new TextBlock 
                    { 
                        Text = "Audiogram view\n\nThis view is not yet implemented.",
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(40)
                    }
                },
                "Fitting" => new UserControl
                {
                    Content = new TextBlock 
                    { 
                        Text = "Fitting view\n\nThis view is not yet implemented.",
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(40)
                    }
                },
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
