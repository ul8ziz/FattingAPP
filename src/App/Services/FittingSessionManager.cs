using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Central session manager following the official SDK Programmer's Guide flow:
    ///
    ///   1. CreateOfflineSession(libraryPath) — loads library, builds offline parameter metadata
    ///   2. AttachDeviceAsync(product, adaptor, side) — calls InitializeDevice, reads parameters
    ///   3. SaveToDeviceAsync() — writes cached dirty changes to device
    ///   4. EndSession() — disconnects, clears state
    ///
    /// CRITICAL THREADING RULE: All SDK calls MUST run on the STA thread where the
    /// COM objects were created. Do NOT use Task.Run() or ConfigureAwait(false).
    ///
    /// HARD GATES:
    ///   - ReadParameters is BLOCKED unless InitializeDevice returned IsConfigured=true.
    ///   - WriteParameters is BLOCKED unless IsDeviceConfigured is true.
    ///   - AttachDeviceAsync validates firmware match before proceeding.
    ///
    /// NEVER calls ConfigureDevice — that is a Manufacturing operation (Section 9.4).
    /// Uses BeginInitializeDevice / EndInitializeDevice for fitting initialization (Section 6.3).
    /// Uses BeginReadParameters / BeginWriteParameters for device I/O (Section 6.6).
    /// </summary>
    public sealed class FittingSessionManager : INotifyPropertyChanged, IDisposable
    {
        private static FittingSessionManager? _instance;
        public static FittingSessionManager Instance => _instance ??= new FittingSessionManager();

        private readonly LibraryService _libraryService = new();
        private readonly SoundDesignerService _soundDesigner = new();

        // --- State ---
        private SessionPhase _phase = SessionPhase.NoSession;
        private bool _isDeviceInitialized;
        private bool _hasDirtyChanges;
        private DeviceSettingsSnapshot? _leftSnapshot;
        private DeviceSettingsSnapshot? _rightSnapshot;
        private DeviceSettingsSnapshot? _leftOfflineSnapshot;
        private DeviceSettingsSnapshot? _rightOfflineSnapshot;

        // --- Memory selection (E7111V2 workflow) ---
        private int _selectedMemoryIndex;
        private ParamFile? _loadedParamFile;
        private string? _loadedParamFilePath;

        // --- Device references (set after attach) ---
        private IProduct? _deviceProduct;
        private ICommunicationAdaptor? _leftAdaptor;
        private ICommunicationAdaptor? _rightAdaptor;
        private bool _leftConnected;
        private bool _rightConnected;

        private FittingSessionManager() { }

        // =========================================================================
        // Properties
        // =========================================================================

        public SessionPhase Phase
        {
            get => _phase;
            private set { _phase = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOffline)); OnPropertyChanged(nameof(IsDeviceAttached)); }
        }

        public bool IsOffline => _phase == SessionPhase.OfflineLibrary || _phase == SessionPhase.ParamApplied;
        public bool IsDeviceAttached => _phase == SessionPhase.DeviceAttached || _phase == SessionPhase.Configured || _phase == SessionPhase.Synced;

        /// <summary>True when device is ready for Read/Write. Uses DeviceSessionService when session is active (single source of truth).</summary>
        public bool IsDeviceConfigured => DeviceSessionService.Instance.HasActiveSession
            ? DeviceSessionService.Instance.IsConfigured
            : _isDeviceInitialized;

        public bool HasDirtyChanges
        {
            get => _hasDirtyChanges;
            set { _hasDirtyChanges = value; OnPropertyChanged(); }
        }

        public LibraryService Library => _libraryService;

        /// <summary>Currently selected memory index (0-7). Default is 0 (Memory 1).</summary>
        public int SelectedMemoryIndex
        {
            get => _selectedMemoryIndex;
            set
            {
                if (value < 0 || value > 7) return;
                if (_selectedMemoryIndex != value)
                {
                    _selectedMemoryIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedMemoryName));
                }
            }
        }

        /// <summary>Display name for the selected memory (e.g., "Memory 1").</summary>
        public string SelectedMemoryName => $"Memory {_selectedMemoryIndex + 1}";

        /// <summary>The loaded .param file (null if none loaded).</summary>
        public ParamFile? LoadedParamFile => _loadedParamFile;

        /// <summary>File name of the loaded .param file (for display).</summary>
        public string? ParamFileName => _loadedParamFilePath != null
            ? System.IO.Path.GetFileName(_loadedParamFilePath) : null;

        /// <summary>Left snapshot: device values if attached, else offline metadata.</summary>
        public DeviceSettingsSnapshot? LeftSnapshot
        {
            get => _leftSnapshot ?? _leftOfflineSnapshot;
            private set { _leftSnapshot = value; OnPropertyChanged(); }
        }

        /// <summary>Right snapshot: device values if attached, else offline metadata.</summary>
        public DeviceSettingsSnapshot? RightSnapshot
        {
            get => _rightSnapshot ?? _rightOfflineSnapshot;
            private set { _rightSnapshot = value; OnPropertyChanged(); }
        }

        public bool LeftConnected => _leftConnected;
        public bool RightConnected => _rightConnected;

        // =========================================================================
        // 1. Create offline session (library-first)
        // =========================================================================

        /// <summary>
        /// Loads a library and creates offline parameter snapshots.
        /// Now uses single-memory snapshots (Memory 0 by default) for E7111V2 workflow.
        /// UI can display tabs/controls/ranges without any device connected.
        /// LibraryService calls are synchronous — safe to run on UI thread.
        /// </summary>
        public async Task CreateOfflineSessionAsync(string libraryPath, CancellationToken ct = default)
        {
            // LibraryService.LoadLibraryAsync is synchronous (Task.CompletedTask).
            // Run on UI thread since SDK COM objects need STA.
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                _libraryService.LoadLibraryAsync(libraryPath, ct).GetAwaiter().GetResult();

                // Build snapshot for a single memory (default: Memory 0) — ~595 params instead of 4760
                _selectedMemoryIndex = 0;
                _leftOfflineSnapshot = _libraryService.BuildSnapshotForMemory(_selectedMemoryIndex, DeviceSide.Left);
                _rightOfflineSnapshot = null; // Offline: only Left panel with defaults
            }, System.Windows.Threading.DispatcherPriority.Send, ct);

            _leftSnapshot = null;
            _rightSnapshot = null;
            _isDeviceInitialized = false;
            _loadedParamFile = null;
            _loadedParamFilePath = null;
            HasDirtyChanges = false;

            Phase = SessionPhase.OfflineLibrary;

            Debug.WriteLine($"[SessionManager] Offline session created: {_libraryService.ProductDescription} (Memory {_selectedMemoryIndex})");
            OnPropertyChanged(nameof(LeftSnapshot));
            OnPropertyChanged(nameof(RightSnapshot));
            OnPropertyChanged(nameof(SelectedMemoryIndex));
            OnPropertyChanged(nameof(ParamFileName));
        }

        /// <summary>
        /// Loads and applies a .param file to the offline product.
        /// After applying, rebuilds the snapshot for the selected memory.
        /// Must be called after CreateOfflineSessionAsync.
        /// </summary>
        public async Task ApplyParamFileAsync(string paramPath, CancellationToken ct = default)
        {
            if (!_libraryService.IsLibraryLoaded)
            {
                Debug.WriteLine("[SessionManager] ApplyParamFile: no library loaded");
                return;
            }

            var paramFile = await ParamFileService.LoadAsync(paramPath, ct);
            if (paramFile == null)
            {
                Debug.WriteLine($"[SessionManager] ApplyParamFile: failed to load {paramPath}");
                return;
            }

            // Apply on UI thread (SDK objects need STA)
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Apply to the selected memory
                int applied = _libraryService.ApplyParamToProduct(paramFile, _selectedMemoryIndex);
                Debug.WriteLine($"[SessionManager] Applied {applied} params from {System.IO.Path.GetFileName(paramPath)} to Memory {_selectedMemoryIndex}");

                // Also apply system params if available
                if (paramFile.System != null)
                {
                    int sysApplied = _libraryService.ApplySystemParams(paramFile);
                    Debug.WriteLine($"[SessionManager] Applied {sysApplied} system params");
                }

                _loadedParamFile = paramFile;
                _loadedParamFilePath = paramPath;

                // Rebuild snapshot with the applied values
                _leftOfflineSnapshot = _libraryService.BuildSnapshotForMemory(_selectedMemoryIndex, DeviceSide.Left);
            }, System.Windows.Threading.DispatcherPriority.Send, ct);

            Phase = SessionPhase.ParamApplied;

            OnPropertyChanged(nameof(LeftSnapshot));
            OnPropertyChanged(nameof(ParamFileName));
            Debug.WriteLine($"[SessionManager] ParamFile applied: {System.IO.Path.GetFileName(paramPath)}");
        }

        /// <summary>
        /// Switches to a different memory index (0-7).
        /// Applies the .param values for that memory (if loaded), then rebuilds the snapshot.
        /// Must be called on the UI thread.
        /// </summary>
        public void SwitchMemory(int memoryIndex)
        {
            if (memoryIndex < 0 || memoryIndex > 7) return;
            if (!_libraryService.IsLibraryLoaded) return;

            _selectedMemoryIndex = memoryIndex;

            // Apply .param values for this memory if a param file is loaded
            if (_loadedParamFile != null && memoryIndex < _loadedParamFile.Memory.Count)
            {
                _libraryService.ApplyParamToProduct(_loadedParamFile, memoryIndex);
            }

            // Rebuild snapshot for the new memory
            _leftOfflineSnapshot = _libraryService.BuildSnapshotForMemory(memoryIndex, DeviceSide.Left);

            OnPropertyChanged(nameof(SelectedMemoryIndex));
            OnPropertyChanged(nameof(SelectedMemoryName));
            OnPropertyChanged(nameof(LeftSnapshot));

            Debug.WriteLine($"[SessionManager] Switched to Memory {memoryIndex + 1}");
        }

        // =========================================================================
        // 2. Attach device (after connection)
        // =========================================================================

        /// <summary>
        /// Syncs session manager state from an existing connection (e.g. after Connect in ConnectDevicesView).
        /// Does not run InitializeDevice again. Probes one ReadParameters (current adaptor); if
        /// E_UNCONFIGURED_DEVICE (e.g. unprogrammed device), sets IsDeviceConfigured false so Save stays disabled.
        /// Call on UI/STA thread.
        /// </summary>
        public async Task SyncDeviceStateFromConnectionAsync(
            IProduct product,
            ICommunicationAdaptor? leftAdaptor,
            ICommunicationAdaptor? rightAdaptor,
            bool leftConnected,
            bool rightConnected,
            CancellationToken ct = default)
        {
            _deviceProduct = product;
            _leftAdaptor = leftAdaptor;
            _rightAdaptor = rightAdaptor;
            _leftConnected = leftConnected;
            _rightConnected = rightConnected;
            Phase = SessionPhase.DeviceAttached;

            bool readOk = false;
            if (leftConnected || rightConnected)
            {
                try
                {
                    var monitor = product.BeginReadParameters(ParameterSpace.kActiveMemory);
                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(50, ct);
                    }
                    monitor.GetResult();
                    readOk = true;
                }
                catch (Exception ex)
                {
                    var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException : ex;
                    var msg = inner?.Message ?? ex.Message ?? "";
                    if (msg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                        Debug.WriteLine("[SessionManager] E_UNCONFIGURED_DEVICE on read — treating device as not configured for Save.");
                }
            }

            _isDeviceInitialized = readOk;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(LeftConnected));
                OnPropertyChanged(nameof(RightConnected));
                OnPropertyChanged(nameof(IsDeviceConfigured));
            }, System.Windows.Threading.DispatcherPriority.Send);
        }

        /// <summary>
        /// Attaches a connected device to the session.
        /// Follows official SDK flow:
        ///   1. BeginInitializeDevice(adaptor) → EndInitializeDevice → IsConfigured
        ///   2. BeginReadParameters(ParameterSpace) → poll → GetResult
        /// If library mismatch is detected, returns the expected library path.
        /// All SDK I/O runs in Task.Run() following the official C# sample.
        /// </summary>
        public async Task<string?> AttachDeviceAsync(
            IProduct product, ICommunicationAdaptor adaptor, DeviceSide side,
            string? firmwareId, IProgress<string>? progress, CancellationToken ct)
        {
            _deviceProduct = product;
            if (side == DeviceSide.Left)
            {
                _leftAdaptor = adaptor;
                _leftConnected = true;
            }
            else
            {
                _rightAdaptor = adaptor;
                _rightConnected = true;
            }

            // Validate library match
            if (!string.IsNullOrEmpty(firmwareId) && _libraryService.IsLibraryLoaded)
            {
                var match = LibraryService.FindLibraryForFirmware(firmwareId);
                var currentLib = _libraryService.LoadedLibraryName;
                if (match != null && currentLib != null &&
                    !currentLib.StartsWith(match.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[SessionManager] Library mismatch: loaded={currentLib}, expected={match.FileName} for firmware={firmwareId}");
                    return match.FullPath; // Caller should reload this library
                }
            }

            // Step 1: InitializeDevice (Programmer's Guide Section 6.3)
            // This is the FITTING initialization — NOT ConfigureDevice (manufacturing).
            // Do NOT use ConfigureAwait(false) — SDK COM objects require STA thread affinity.
            progress?.Report("Initializing device…");
            bool isConfigured = await _soundDesigner.InitializeDeviceAsync(product, adaptor, ct);

            _isDeviceInitialized = isConfigured;
            Debug.WriteLine($"[SessionManager] InitializeDevice completed: IsConfigured={isConfigured}");

            // ========== HARD GATE: Block ReadParameters unless device is configured ==========
            // If InitializeDevice returned false, the SDK state is incomplete.
            // Calling ReadParameters on an unconfigured device corrupts SDK internal state
            // and can cause Access Violation (0xC0000005) in sdnet.dll.
            if (!isConfigured)
            {
                Debug.WriteLine("[SessionManager] HARD GATE: Device NOT configured — skipping ReadParameters entirely.");
                progress?.Report("Device not configured. Parameters unavailable.");

                // Still transition to DeviceAttached so the UI knows a device is present,
                // but the snapshot will remain null (offline fallback used).
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Phase = SessionPhase.DeviceAttached;
                    OnPropertyChanged(nameof(LeftConnected));
                    OnPropertyChanged(nameof(RightConnected));
                    OnPropertyChanged(nameof(IsDeviceConfigured));
                }, System.Windows.Threading.DispatcherPriority.Send, ct);

                return null;
            }

            // Step 2: ReadParameters (Programmer's Guide Section 6.6)
            // Only runs when InitializeDevice confirmed IsConfigured=true.
            progress?.Report("Reading device parameters…");
            DeviceSettingsSnapshot? snapshot = null;
            try
            {
                // Do NOT use ConfigureAwait(false) — SDK COM objects require STA thread affinity.
                snapshot = await _soundDesigner.ReadAllSettingsAsync(product, adaptor, side, progress, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] ReadAllSettings failed: {ex.Message}");
                var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException : ex;
                var msg = inner?.Message ?? ex.Message ?? "";
                if (msg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _isDeviceInitialized = false;
                    Debug.WriteLine("[SessionManager] E_UNCONFIGURED_DEVICE — device not configured for Save.");
                }
                // Continue — partial data or offline snapshot is better than nothing
            }

            // Update snapshots on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (side == DeviceSide.Left)
                    LeftSnapshot = snapshot;
                else
                    RightSnapshot = snapshot;

                Phase = SessionPhase.DeviceAttached;
                OnPropertyChanged(nameof(LeftConnected));
                OnPropertyChanged(nameof(RightConnected));
                OnPropertyChanged(nameof(IsDeviceConfigured));
            }, System.Windows.Threading.DispatcherPriority.Send, ct);

            return null; // No mismatch
        }

        // =========================================================================
        // 3. Save to device (batch write)
        // =========================================================================

        /// <summary>
        /// Writes all dirty changes to the device using BeginWriteParameters.
        /// Batch: writes once per side, not per parameter.
        /// </summary>
        public async Task<bool> SaveToDeviceAsync(IProgress<string>? progress, CancellationToken ct)
        {
            if (_deviceProduct == null || Phase != SessionPhase.DeviceAttached) return false;

            // ========== HARD GATE: Block WriteParameters unless device is configured ==========
            if (!_isDeviceInitialized)
            {
                Debug.WriteLine("[SessionManager] HARD GATE: SaveToDevice BLOCKED — device not configured. WriteParameters would corrupt SDK state.");
                progress?.Report("Cannot save: device is not fully configured.");
                return false;
            }

            bool allOk = true;

            if (_leftConnected && _leftSnapshot != null && _leftAdaptor != null)
            {
                progress?.Report("Saving to Left device…");
                // Do NOT use ConfigureAwait(false) — SDK COM objects require STA thread affinity.
                var ok = await _soundDesigner.WriteSettingsAsync(_deviceProduct, _leftAdaptor, _leftSnapshot, progress, ct, selectedMemoryIndex: _selectedMemoryIndex);
                if (!ok) allOk = false;
            }

            if (_rightConnected && _rightSnapshot != null && _rightAdaptor != null)
            {
                progress?.Report("Saving to Right device…");
                // Do NOT use ConfigureAwait(false) — SDK COM objects require STA thread affinity.
                var ok = await _soundDesigner.WriteSettingsAsync(_deviceProduct, _rightAdaptor, _rightSnapshot, progress, ct, selectedMemoryIndex: _selectedMemoryIndex);
                if (!ok) allOk = false;
            }

            if (allOk) HasDirtyChanges = false;
            return allOk;
        }

        // =========================================================================
        // 4. Refresh (re-read from device, use cache until called)
        // =========================================================================

        /// <summary>Re-reads parameters from device, replacing the cached snapshot.</summary>
        public async Task RefreshFromDeviceAsync(DeviceSide side, IProgress<string>? progress, CancellationToken ct)
        {
            if (_deviceProduct == null) return;

            // ========== HARD GATE: Block ReadParameters unless device is configured ==========
            if (!_isDeviceInitialized)
            {
                Debug.WriteLine("[SessionManager] HARD GATE: RefreshFromDevice BLOCKED — device not configured.");
                progress?.Report("Cannot refresh: device is not fully configured.");
                return;
            }

            var adaptor = side == DeviceSide.Left ? _leftAdaptor : _rightAdaptor;
            if (adaptor == null) return;

            // Do NOT use ConfigureAwait(false) — SDK COM objects require STA thread affinity.
            var snapshot = await _soundDesigner.ReadAllSettingsAsync(_deviceProduct, adaptor, side, progress, ct);

            // Update on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (side == DeviceSide.Left)
                    LeftSnapshot = snapshot;
                else
                    RightSnapshot = snapshot;
            }, System.Windows.Threading.DispatcherPriority.Send, ct);
        }

        // =========================================================================
        // 5. End session
        // =========================================================================

        public void EndSession()
        {
            _leftSnapshot = null;
            _rightSnapshot = null;
            _leftOfflineSnapshot = null;
            _rightOfflineSnapshot = null;
            _deviceProduct = null;
            _leftAdaptor = null;
            _rightAdaptor = null;
            _leftConnected = false;
            _rightConnected = false;
            _isDeviceInitialized = false;
            _selectedMemoryIndex = 0;
            _loadedParamFile = null;
            _loadedParamFilePath = null;
            HasDirtyChanges = false;
            Phase = SessionPhase.NoSession;
            _libraryService.UnloadLibrary();
            Debug.WriteLine("[SessionManager] Session ended");
        }

        public void Dispose()
        {
            EndSession();
            _libraryService.Dispose();
        }

        // =========================================================================
        // INotifyPropertyChanged
        // =========================================================================

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Session lifecycle phases (expanded for E7111V2 workflow).</summary>
    public enum SessionPhase
    {
        /// <summary>No session active. Show library selector / welcome screen.</summary>
        NoSession,
        /// <summary>Library loaded, parameters visible (offline). No device connected.</summary>
        OfflineLibrary,
        /// <summary>.param file has been applied to the offline product.</summary>
        ParamApplied,
        /// <summary>Device connected, configured, and parameters read from hardware.</summary>
        DeviceAttached,
        /// <summary>Device initialized and configured — ready for read/write.</summary>
        Configured,
        /// <summary>Device parameters synced with local snapshot.</summary>
        Synced
    }
}
