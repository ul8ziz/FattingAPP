using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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
            
            // Create ViewModel
            _viewModel = new MainViewModel
            {
                HasActiveSession = false,
                HasUnsavedChanges = false,
                ConnectionStatusText = "Not Connected",
                ConnectionStatusBrush = new SolidColorBrush(Colors.Gray),
                UserName = "Dr. Sarah Johnson",
                UserRole = "Licensed Audiologist",
                IsDeviceReady = false,
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
    }
    
    // Simple ViewModel for MainView
    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _hasActiveSession;
        private bool _hasUnsavedChanges;
        private string _connectionStatusText = string.Empty;
        private SolidColorBrush? _connectionStatusBrush;
        private string _userName = string.Empty;
        private string _userRole = string.Empty;
        private bool _isDeviceReady;
        private UserControl? _currentView;
        private ICommand? _endSessionCommand;
        private ICommand? _navigateCommand;

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

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set { _connectionStatusText = value; OnPropertyChanged(); }
        }

        public SolidColorBrush? ConnectionStatusBrush
        {
            get => _connectionStatusBrush;
            set { _connectionStatusBrush = value; OnPropertyChanged(); }
        }

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

        public bool IsDeviceReady
        {
            get => _isDeviceReady;
            set { _isDeviceReady = value; OnPropertyChanged(); }
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
            IsDeviceReady = false;
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
            // Handle navigation to different views
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
