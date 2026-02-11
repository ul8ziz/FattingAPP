using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Global connection/session state driving header and navigation. Singleton; INotifyPropertyChanged.
    /// </summary>
    public sealed class AppSessionState : INotifyPropertyChanged
    {
        private static readonly Lazy<AppSessionState> _instance = new Lazy<AppSessionState>(() => new AppSessionState());
        public static AppSessionState Instance => _instance.Value;

        private ConnectionState _connectionState = ConnectionState.NotConnected;
        private string _connectedDeviceDisplayName = string.Empty;
        private bool _connectedLeft;
        private bool _connectedRight;
        private string? _leftModelName;
        private string? _leftFirmwareId;
        private string? _leftSerialId;
        private string? _rightModelName;
        private string? _rightFirmwareId;
        private string? _rightSerialId;

        private AppSessionState() { }

        public ConnectionState ConnectionState
        {
            get => _connectionState;
            set { _connectionState = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); OnPropertyChanged(nameof(ConnectionStatusBrush)); OnPropertyChanged(nameof(IsNavigationEnabled)); OnPropertyChanged(nameof(IsConnectedDeviceNameVisible)); }
        }

        public string ConnectedDeviceDisplayName
        {
            get => _connectedDeviceDisplayName;
            set { _connectedDeviceDisplayName = value ?? ""; OnPropertyChanged(); }
        }

        public bool ConnectedLeft
        {
            get => _connectedLeft;
            set { _connectedLeft = value; OnPropertyChanged(); }
        }

        public bool ConnectedRight
        {
            get => _connectedRight;
            set { _connectedRight = value; OnPropertyChanged(); }
        }

        public string? LeftModelName { get => _leftModelName; set { _leftModelName = value; OnPropertyChanged(); } }
        public string? LeftFirmwareId { get => _leftFirmwareId; set { _leftFirmwareId = value; OnPropertyChanged(); } }
        public string? LeftSerialId { get => _leftSerialId; set { _leftSerialId = value; OnPropertyChanged(); } }
        public string? RightModelName { get => _rightModelName; set { _rightModelName = value; OnPropertyChanged(); } }
        public string? RightFirmwareId { get => _rightFirmwareId; set { _rightFirmwareId = value; OnPropertyChanged(); } }
        public string? RightSerialId { get => _rightSerialId; set { _rightSerialId = value; OnPropertyChanged(); } }

        public string ConnectionStatusText =>
            _connectionState switch
            {
                ConnectionState.NotConnected => "Not Connected",
                ConnectionState.Discovering => "Discovering…",
                ConnectionState.Discovered => "Discovered",
                ConnectionState.Connecting => "Connecting…",
                ConnectionState.Connected => "Connected",
                ConnectionState.ConnectionFailed => "Connection Failed",
                _ => "Not Connected"
            };

        public System.Windows.Media.Brush ConnectionStatusBrush =>
            _connectionState == ConnectionState.Connected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

        public bool IsNavigationEnabled => _connectionState == ConnectionState.Connected;

        public bool IsConnectedDeviceNameVisible => _connectionState == ConnectionState.Connected && !string.IsNullOrEmpty(_connectedDeviceDisplayName);

        public void SetConnected(string deviceDisplayName, bool left, bool right, string? leftModel, string? leftFirmware, string? leftSerial, string? rightModel, string? rightFirmware, string? rightSerial)
        {
            ConnectionState = ConnectionState.Connected;
            ConnectedDeviceDisplayName = deviceDisplayName;
            ConnectedLeft = left;
            ConnectedRight = right;
            LeftModelName = leftModel;
            LeftFirmwareId = leftFirmware;
            LeftSerialId = leftSerial;
            RightModelName = rightModel;
            RightFirmwareId = rightFirmware;
            RightSerialId = rightSerial;
        }

        public void SetNotConnected()
        {
            ConnectionState = ConnectionState.NotConnected;
            ConnectedDeviceDisplayName = string.Empty;
            ConnectedLeft = false;
            ConnectedRight = false;
            LeftModelName = LeftFirmwareId = LeftSerialId = RightModelName = RightFirmwareId = RightSerialId = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
