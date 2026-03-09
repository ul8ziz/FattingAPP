using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Holds the active device programming session (SdkManager + DeviceConnectionService) after successful connect.
    /// Tracks connected devices, dirty state, live display, and snapshots for session end (save to device).
    /// Cleared on End Session. Single source of truth for session state; connection status is in AppSessionState (header only).
    /// </summary>
    public sealed class DeviceSessionService : INotifyPropertyChanged
    {
        private static readonly Lazy<DeviceSessionService> _instance = new Lazy<DeviceSessionService>(() => new DeviceSessionService());
        public static DeviceSessionService Instance => _instance.Value;

        private SdkManager? _sdkManager;
        private DeviceConnectionService? _connectionService;
        private ProgrammerInfo? _programmerInfo;
        private string _programmerDisplayName = string.Empty;
        private bool _leftConnected;
        private bool _rightConnected;
        private bool _leftInitialized;
        private bool _rightInitialized;
        private bool _leftConfigured;
        private bool _rightConfigured;
        private string? _lastConfigError;
        private string? _deviceFirmwareId;
        private string? _leftSerial;
        private string? _rightSerial;
        private string? _leftModel;
        private string? _rightModel;
        private bool _hasDirty;
        private bool _isLiveDisplayEnabled;
        private bool _isConfigureRunning;
        private DeviceSettingsSnapshot? _leftSnapshot;
        private DeviceSettingsSnapshot? _rightSnapshot;
        private int _selectedMemoryIndex;
        private readonly Dictionary<(DeviceSide Side, int MemoryIndex), DeviceSettingsSnapshot> _memorySnapshots = new();
        private readonly HashSet<(DeviceSide Side, int MemoryIndex)> _dirtyMemories = new();

        private DeviceSessionService() { }

        /// <summary>Fired when session is about to end; subscribers (e.g. FittingViewModel) should stop live mode timers.</summary>
        public event EventHandler? RequestStopLiveMode;

        public bool HasActiveSession => _connectionService != null && (_leftConnected || _rightConnected);
        public bool LeftConnected => _leftConnected;
        public bool RightConnected => _rightConnected;

        /// <summary>True when at least one side has a connected adaptor (device detected).</summary>
        public bool IsDeviceConnected => _leftConnected || _rightConnected;

        /// <summary>True when InitializeDevice has completed for at least one side (may have returned configured=false).</summary>
        public bool IsInitialized => _leftInitialized || _rightInitialized;

        /// <summary>True when at least one side is ready for ReadParameters/WriteParameters. If false, Save and Read are blocked.</summary>
        public bool IsConfigured => _leftConfigured || _rightConfigured;

        /// <summary>True while Configure Device (manufacturing) is running. Disable End Session / Scan / Connect during this time to avoid concurrent SDK use.</summary>
        public bool IsConfigureRunning => _isConfigureRunning;

        /// <summary>Last configuration error (e.g. E_UNCONFIGURED_DEVICE or "InitializeDevice returned false").</summary>
        public string? LastConfigError { get => _lastConfigError; private set { _lastConfigError = value; OnPropertyChanged(nameof(LastConfigError)); } }

        /// <summary>Device firmware ID (e.g. E7111V2) when known.</summary>
        public string? DeviceFirmwareId => _deviceFirmwareId;
        public string? LeftSerial => _leftSerial;
        public string? RightSerial => _rightSerial;
        public string? LeftModel => _leftModel;
        public string? RightModel => _rightModel;

        /// <summary>True if the given side is configured (InitializeDevice succeeded for that side). Read/Write must only run when true.</summary>
        public bool IsSideConfigured(DeviceSide side) => side == DeviceSide.Left ? _leftConfigured : _rightConfigured;

        /// <summary>Called by DeviceInitializationService after InitializeDevice/ConfigureDevice. Updates session state and notifies.</summary>
        public void SetSideInitialized(DeviceSide side, bool value)
        {
            if (side == DeviceSide.Left) _leftInitialized = value; else _rightInitialized = value;
            OnPropertyChanged(nameof(IsInitialized));
        }

        /// <summary>Called by DeviceInitializationService when a side becomes configured or unconfigured.</summary>
        public void SetSideConfigured(DeviceSide side, bool value)
        {
            if (side == DeviceSide.Left) _leftConfigured = value; else _rightConfigured = value;
            OnPropertyChanged(nameof(IsConfigured));
            OnPropertyChanged(nameof(IsSideConfigured));
        }

        /// <summary>Sets the last configuration error message (e.g. E_UNCONFIGURED_DEVICE).</summary>
        public void SetLastConfigError(string? message)
        {
            _lastConfigError = message;
            OnPropertyChanged(nameof(LastConfigError));
        }

        /// <summary>Sets whether Configure Device is currently running. Call true at start, false at end so UI can disable End Session / Scan / Connect.</summary>
        public void SetConfigureRunning(bool running)
        {
            if (_isConfigureRunning == running) return;
            _isConfigureRunning = running;
            OnPropertyChanged(nameof(IsConfigureRunning));
        }

        /// <summary>Optionally set device identity when known (e.g. after Connect or discovery).</summary>
        public void SetDeviceIdentity(string? firmwareId, string? leftSerial, string? rightSerial, string? leftModel, string? rightModel)
        {
            _deviceFirmwareId = firmwareId;
            _leftSerial = leftSerial;
            _rightSerial = rightSerial;
            _leftModel = leftModel;
            _rightModel = rightModel;
            OnPropertyChanged(nameof(DeviceFirmwareId));
            OnPropertyChanged(nameof(LeftSerial));
            OnPropertyChanged(nameof(RightSerial));
            OnPropertyChanged(nameof(LeftModel));
            OnPropertyChanged(nameof(RightModel));
        }
        public string ProgrammerDisplayName => _programmerDisplayName;
        public SdkManager? SdkManager => _sdkManager;
        public DeviceConnectionService? ConnectionService => _connectionService;
        public ProgrammerInfo? ProgrammerInfo => _programmerInfo;
        public bool HasDirty { get => _hasDirty; private set { _hasDirty = value; OnPropertyChanged(nameof(HasDirty)); } }
        public bool IsLiveDisplayEnabled { get => _isLiveDisplayEnabled; private set { _isLiveDisplayEnabled = value; OnPropertyChanged(nameof(IsLiveDisplayEnabled)); } }
        public int SelectedMemoryIndex => _selectedMemoryIndex;

        /// <summary>Call after successful Connect. Stores references and initial configured state from connection.</summary>
        public void SetSession(
            SdkManager sdkManager,
            DeviceConnectionService connectionService,
            ProgrammerInfo programmerInfo,
            string programmerDisplayName,
            bool leftConnected,
            bool rightConnected)
        {
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _programmerInfo = programmerInfo;
            _programmerDisplayName = programmerDisplayName ?? string.Empty;
            _leftConnected = leftConnected;
            _rightConnected = rightConnected;
            // Copy initial init/configured state from connection (Connect already ran InitializeDevice per side).
            _leftInitialized = leftConnected;
            _rightInitialized = rightConnected;
            _leftConfigured = leftConnected && (connectionService?.IsSideConfigured(DeviceSide.Left) ?? false);
            _rightConfigured = rightConnected && (connectionService?.IsSideConfigured(DeviceSide.Right) ?? false);
            _lastConfigError = null;
            _hasDirty = false;
            _memorySnapshots.Clear();
            _dirtyMemories.Clear();
            _selectedMemoryIndex = 0;
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(LeftConnected));
            OnPropertyChanged(nameof(RightConnected));
            OnPropertyChanged(nameof(IsDeviceConnected));
            OnPropertyChanged(nameof(IsInitialized));
            OnPropertyChanged(nameof(IsConfigured));
            OnPropertyChanged(nameof(LastConfigError));
            OnPropertyChanged(nameof(ProgrammerDisplayName));
            OnPropertyChanged(nameof(SdkManager));
            OnPropertyChanged(nameof(ConnectionService));
            OnPropertyChanged(nameof(ProgrammerInfo));
            OnPropertyChanged(nameof(HasDirty));
            OnPropertyChanged(nameof(SelectedMemoryIndex));
        }

        /// <summary>Called by FittingViewModel when snapshots are loaded or updated (for Save on session end).</summary>
        public void SetFittingSnapshots(DeviceSettingsSnapshot? left, DeviceSettingsSnapshot? right)
        {
            _leftSnapshot = left;
            _rightSnapshot = right;
            SetMemorySnapshot(DeviceSide.Left, _selectedMemoryIndex, left);
            SetMemorySnapshot(DeviceSide.Right, _selectedMemoryIndex, right);
        }

        public void SetSelectedMemoryIndex(int memoryIndex)
        {
            if (memoryIndex < 0 || memoryIndex > 7) return;
            _selectedMemoryIndex = memoryIndex;
            OnPropertyChanged(nameof(SelectedMemoryIndex));
        }

        public void SetMemorySnapshot(DeviceSide side, int memoryIndex, DeviceSettingsSnapshot? snapshot)
        {
            if (memoryIndex < 0 || memoryIndex > 7 || snapshot == null) return;
            _memorySnapshots[(side, memoryIndex)] = snapshot;
        }

        public bool TryGetMemorySnapshot(DeviceSide side, int memoryIndex, out DeviceSettingsSnapshot? snapshot)
        {
            if (_memorySnapshots.TryGetValue((side, memoryIndex), out var found))
            {
                snapshot = found;
                return true;
            }

            snapshot = null;
            return false;
        }

        /// <summary>Called by FittingViewModel when parameters change.</summary>
        public void SetDirty(bool dirty)
        {
            if (!dirty)
            {
                _dirtyMemories.Clear();
            }
            HasDirty = dirty || _dirtyMemories.Count > 0;
        }

        /// <summary>Marks the given memory as dirty. Returns true if the memory was not already dirty.</summary>
        public bool MarkMemoryDirty(DeviceSide side, int memoryIndex)
        {
            if (memoryIndex < 0 || memoryIndex > 7) return false;
            bool newlyDirty = _dirtyMemories.Add((side, memoryIndex));
            HasDirty = true;
            return newlyDirty;
        }

        public void ClearMemoryDirty(DeviceSide side, int memoryIndex)
        {
            _dirtyMemories.Remove((side, memoryIndex));
            HasDirty = _dirtyMemories.Count > 0;
        }

        public bool IsMemoryDirty(DeviceSide side, int memoryIndex)
        {
            return _dirtyMemories.Contains((side, memoryIndex));
        }

        public bool HasAnyDirtyMemory()
        {
            return _dirtyMemories.Count > 0;
        }

        public int GetDirtyMemoryCount()
        {
            return _dirtyMemories.Count;
        }

        public IReadOnlyList<int> GetDirtyMemoryIndexesForSide(DeviceSide side)
        {
            return _dirtyMemories
                .Where(x => x.Side == side)
                .Select(x => x.MemoryIndex)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        /// <summary>Called by FittingViewModel when live display is toggled.</summary>
        public void SetLiveDisplayEnabled(bool enabled)
        {
            IsLiveDisplayEnabled = enabled;
        }

        /// <summary>Returns current snapshots for Save to Device & End. Call after RequestStopLiveMode.</summary>
        public (DeviceSettingsSnapshot? Left, DeviceSettingsSnapshot? Right) GetSnapshotsForSave()
        {
            return (_leftSnapshot, _rightSnapshot);
        }

        public (DeviceSettingsSnapshot? Left, DeviceSettingsSnapshot? Right) GetSnapshotsForMemory(int memoryIndex)
        {
            DeviceSettingsSnapshot? left = null;
            DeviceSettingsSnapshot? right = null;
            _memorySnapshots.TryGetValue((DeviceSide.Left, memoryIndex), out left);
            _memorySnapshots.TryGetValue((DeviceSide.Right, memoryIndex), out right);
            return (left, right);
        }

        /// <summary>Returns the single SdkManager for scan/connect. Creates it inside SdkGate if not yet created and lifecycle allows. Do not create ProductManager elsewhere.</summary>
        public async Task<SdkManager> EnsureSdkReadyForScanAsync(string libraryPath)
        {
            if (SdkLifecycle.IsDisposingOrDisposed)
                throw new InvalidOperationException("SDK is disposing or disposed; cannot prepare for scan.");
            if (HasActiveSession)
                throw new InvalidOperationException("Session is active; end session before reinitializing SDK for scan.");

            if (_sdkManager != null && _sdkManager.IsInitialized)
                return _sdkManager;

            return await SdkGate.InvokeAsync(() =>
            {
                if (_sdkManager == null)
                    _sdkManager = new SdkManager();
                if (!_sdkManager.IsInitialized)
                    _sdkManager.Initialize(libraryPath);
                return _sdkManager;
            }, "EnsureSdkReadyForScan").ConfigureAwait(false);
        }

        /// <summary>Raises RequestStopLiveMode so FittingViewModel can stop timers. Call before save/cleanup on session end.</summary>
        public void NotifyStopLiveMode()
        {
            RequestStopLiveMode?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Call on End Session. Stops live mode, cancels in-flight work, drains SDK gate, then closes adaptors and disposes SdkManager on the gate. No use-after-free.</summary>
        public async Task ClearSessionAsync()
        {
            if (_sdkManager == null && _connectionService == null)
            {
                ClearSessionStateOnly();
                return;
            }

            System.Diagnostics.Debug.WriteLine("[Dispose] ClearSessionAsync: cancel -> drain -> close -> dispose");
            SdkLifecycle.SetState(SdkLifecycleState.Disposing);

            // A) Stop Live Mode first (no more periodic reads)
            NotifyStopLiveMode();
            await Task.Delay(100).ConfigureAwait(false);

            // B) Cancel any in-flight operations (callers should pass CTS that we can cancel)
            _sessionCts?.Cancel();
            await Task.Delay(50).ConfigureAwait(false);

            // C) Drain SDK gate so no pending SDK work runs after we dispose
            SdkGate.BeginDispose();
            await SdkGate.DrainAsync().ConfigureAwait(false);

            // D) On gate: close adaptors, dispose connection, dispose SdkManager (then mark gate disposed)
            SdkGate.RunCleanupToDispose("ClearSession_Cleanup", () =>
            {
                try
                {
                    _connectionService?.Cleanup();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Dispose] ConnectionService.Cleanup: {ex.Message}");
                }
                try
                {
                    _sdkManager?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Dispose] SdkManager.Dispose: {ex.Message}");
                }
            });

            // E) Clear references and reset gate for next session
            ClearSessionStateOnly();
            SdkGate.ResetForNewSession();
            SdkLifecycle.SetState(SdkLifecycleState.Uninitialized);
            System.Diagnostics.Debug.WriteLine("[Dispose] ClearSessionAsync complete");
        }

        private CancellationTokenSource? _sessionCts;

        /// <summary>Provides a CancellationToken that is cancelled when ClearSessionAsync begins. Use for in-flight SDK work.</summary>
        public CancellationToken SessionCancellationToken => _sessionCts?.Token ?? default;

        /// <summary>Call when starting an operation that should be cancelled on session end. Returns a CTS that ClearSessionAsync will cancel.</summary>
        public CancellationTokenSource CreateSessionCancellationTokenSource()
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = new CancellationTokenSource();
            return _sessionCts;
        }

        private void ClearSessionStateOnly()
        {
            _sdkManager = null;
            _connectionService = null;
            _programmerInfo = null;
            _programmerDisplayName = string.Empty;
            _leftConnected = false;
            _rightConnected = false;
            _leftInitialized = false;
            _rightInitialized = false;
            _leftConfigured = false;
            _rightConfigured = false;
            _lastConfigError = null;
            _deviceFirmwareId = null;
            _leftSerial = null;
            _rightSerial = null;
            _leftModel = null;
            _rightModel = null;
            _hasDirty = false;
            _isLiveDisplayEnabled = false;
            _leftSnapshot = null;
            _rightSnapshot = null;
            _memorySnapshots.Clear();
            _dirtyMemories.Clear();
            _selectedMemoryIndex = 0;
            _isConfigureRunning = false;
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;
            RaiseSessionClearedPropertyChanged();
        }

        private void RaiseSessionClearedPropertyChanged()
        {
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(IsConfigureRunning));
            OnPropertyChanged(nameof(LeftConnected));
            OnPropertyChanged(nameof(RightConnected));
            OnPropertyChanged(nameof(IsDeviceConnected));
            OnPropertyChanged(nameof(IsInitialized));
            OnPropertyChanged(nameof(IsConfigured));
            OnPropertyChanged(nameof(LastConfigError));
            OnPropertyChanged(nameof(ProgrammerDisplayName));
            OnPropertyChanged(nameof(SdkManager));
            OnPropertyChanged(nameof(ConnectionService));
            OnPropertyChanged(nameof(ProgrammerInfo));
            OnPropertyChanged(nameof(HasDirty));
            OnPropertyChanged(nameof(IsLiveDisplayEnabled));
        }

        /// <summary>Call on End Session (sync fallback). Prefer ClearSessionAsync for safe teardown.</summary>
        public void ClearSession()
        {
            try
            {
                _connectionService?.Cleanup();
            }
            catch { /* ignore */ }
            try
            {
                _sdkManager?.Dispose();
            }
            catch { /* ignore */ }
            ClearSessionStateOnly();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
