using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Services.Diagnostics;
using Ul8ziz.FittingApp.App.Views;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>
    /// ViewModel for Fitting page.
    /// Supports two modes:
    ///   1. Offline (library-first): tabs/parameters from library metadata, no device values.
    ///   2. Online (device attached): actual values read from device, with save/live mode.
    /// Tabs are generated dynamically from Parameter.LongModuleName — no hardcoded tab list.
    /// Uses FittingSessionManager for session lifecycle + caching + dirty tracking.
    /// </summary>
    public class FittingViewModel : INotifyPropertyChanged
    {
        private readonly DeviceSessionService _session = DeviceSessionService.Instance;
        private readonly FittingSessionManager _sessionMgr = FittingSessionManager.Instance;
        private readonly ISoundDesignerService _soundDesigner = new SoundDesignerService();
        private readonly SettingsCache _settingsCache = new SettingsCache();
        private readonly SemaphoreSlim _memoryIoLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _loadCts;
        private System.Timers.Timer? _liveModeDebounce;
        private System.Timers.Timer? _searchDebounce;

        private bool _isLiveModeEnabled;
        private bool _isLoading = false;
        private string _loadingMessage = "Loading device settings…";
        private string _searchText = string.Empty;
        private string _selectedTabId = string.Empty;
        private bool _hasUnsavedChanges;
        private DeviceSettingsSnapshot? _leftSnapshot;
        private DeviceSettingsSnapshot? _rightSnapshot;
        private List<Action>? _dirtyUnsubscribe;
        private string _errorMessage = string.Empty;
        private bool _showRetryInErrorBanner = true;
        private bool _isOfflineMode;
        private int _selectedMemoryIndex;
        private bool _isConfiguring;
        private bool _isMemoryLoadInProgress;
        private string _nvmSaveStatus = string.Empty;

        // Lazy: group rows built only when expander opens. Key = (tabId, groupId).
        private readonly Dictionary<(string TabId, string GroupId), List<SettingItemViewModel>> _leftGroupRowsCache = new();
        private readonly Dictionary<(string TabId, string GroupId), List<SettingItemViewModel>> _rightGroupRowsCache = new();
        // Per-tab item cache for flat list (used when not using group expanders)
        private readonly Dictionary<string, List<SettingItemViewModel>> _leftItemCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<SettingItemViewModel>> _rightItemCache = new(StringComparer.OrdinalIgnoreCase);
        // Flat filtered item collections for ListView binding (swapped on tab switch)
        private ObservableCollection<SettingItemViewModel> _filteredLeftItems = new();
        private ObservableCollection<SettingItemViewModel> _filteredRightItems = new();

        public FittingViewModel()
        {
            EnableLiveModeCommand = new RelayCommand(_ => ToggleLiveMode(), _ => !IsOfflineMode);
            SaveToDeviceCommand = new RelayCommand(_ => { _ = SaveCurrentMemoryToNvmAsync(CancellationToken.None); }, _ => !IsLoading && HasActiveSession && !IsOfflineMode && IsDeviceConfigured && IsCurrentMemoryDirty);
            SaveAllMemoriesCommand = new RelayCommand(_ => { _ = SaveAllMemoriesToNvmAsync(onlyDirty: true, CancellationToken.None); }, _ => !IsLoading && HasActiveSession && !IsOfflineMode && IsDeviceConfigured && HasAnyDirtyMemory);
            ConfigureDeviceCommand = new RelayCommand(_ => ConfigureDeviceAsync(), _ => !IsLoading && !IsConfiguring && HasActiveSession && !IsOfflineMode && !IsDeviceConfigured);
            SelectTabCommand = new RelayCommand(p => SelectTab(p?.ToString() ?? ""), _ => true);
            RefreshReadCommand = new RelayCommand(_ => LoadSettingsAsync(), _ => !IsLoading && HasActiveSession);
            ReloadFromNvmCommand = new RelayCommand(_ => { _ = ReloadFromNvmAsync(CancellationToken.None); }, _ => !IsLoading && HasActiveSession && !IsOfflineMode && IsDeviceConfigured);
            EnsureGroupLoadedLeftCommand = new RelayCommand(p => EnsureGroupLoadedAsync(DeviceSide.Left, p?.ToString() ?? ""), _ => true);
            EnsureGroupLoadedRightCommand = new RelayCommand(p => EnsureGroupLoadedAsync(DeviceSide.Right, p?.ToString() ?? ""), _ => true);
            SwitchMemoryCommand = new RelayCommand(_ => { }, _ => true); // Memory switching handled via SelectedMemoryIndex setter
            _session.RequestStopLiveMode += OnRequestStopLiveMode;
            _session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceSessionService.SelectedMemoryIndex))
                {
                    // Sync from shared source when Audiogram (or another screen) changes memory
                    int incoming = _session.SelectedMemoryIndex;
                    if (_selectedMemoryIndex != incoming)
                    {
                        _selectedMemoryIndex = incoming;
                        OnPropertyChanged(nameof(SelectedMemoryIndex));
                        OnPropertyChanged(nameof(SelectedMemoryName));
                        _ = OnMemoryChangedAsync();
                    }
                }
                else if (e.PropertyName == nameof(DeviceSessionService.IsConfigured) ||
                    e.PropertyName == nameof(DeviceSessionService.LastConfigError) ||
                    e.PropertyName == nameof(DeviceSessionService.LeftConnected) ||
                    e.PropertyName == nameof(DeviceSessionService.RightConnected) ||
                    e.PropertyName == nameof(DeviceSessionService.HasDirty))
                {
                    OnPropertyChanged(nameof(IsDeviceConfigured));
                    OnPropertyChanged(nameof(FittingStatusText));
                    OnPropertyChanged(nameof(SaveDisabledReason));
                    OnPropertyChanged(nameof(SaveToDeviceToolTip));
                    OnPropertyChanged(nameof(SaveAllMemoriesToolTip));
                    OnPropertyChanged(nameof(ShowConfigureButton));
                    OnPropertyChanged(nameof(ConfigureDeviceToolTip));
                    OnPropertyChanged(nameof(IsCurrentMemoryDirty));
                    OnPropertyChanged(nameof(HasAnyDirtyMemory));
                    CommandManager.InvalidateRequerySuggested();
                }
            };
        }

        /// <summary>Memory names for the Memory Selector ComboBox (Memory 1..8).</summary>
        public int[] AvailableMemories { get; } = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        public string[] MemoryNames { get; } = new[]
        {
            "Memory 1", "Memory 2", "Memory 3", "Memory 4",
            "Memory 5", "Memory 6", "Memory 7", "Memory 8"
        };

        /// <summary>Currently selected memory index (0-7). Changing this switches the visible parameters.</summary>
        public int SelectedMemoryIndex
        {
            get => _selectedMemoryIndex;
            set
            {
                if (value < 0 || value > 7) return;
                if (_selectedMemoryIndex != value)
                {
                    _selectedMemoryIndex = value;
                    _session.SetSelectedMemoryIndex(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedMemoryName));
                    _ = OnMemoryChangedAsync();
                }
            }
        }

        /// <summary>Display name of the selected memory.</summary>
        public string SelectedMemoryName => $"Memory {_selectedMemoryIndex + 1}";

        /// <summary>Name of the loaded .param file (for display).</summary>
        public string ParamFileName => _sessionMgr.ParamFileName ?? "(no .param loaded)";

        public ICommand SwitchMemoryCommand { get; }
        public ICommand ReloadFromNvmCommand { get; }
        public ICommand ConfigureDeviceCommand { get; }

        private void OnRequestStopLiveMode(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                _liveModeDebounce?.Stop();
                _liveModeDebounce?.Dispose();
                _liveModeDebounce = null;
                IsLiveModeEnabled = false;
                _session.SetLiveDisplayEnabled(false);
            });
        }

        public bool HasActiveSession => _session.HasActiveSession || _sessionMgr.Phase != SessionPhase.NoSession;
        public bool ShowLeftCard => _session.LeftConnected || _sessionMgr.Phase == SessionPhase.OfflineLibrary;
        public bool ShowRightCard => _session.RightConnected;

        /// <summary>True when the device has been initialized and is ready for Read/Write. Save is disabled when false.</summary>
        public bool IsDeviceConfigured => _sessionMgr.IsDeviceConfigured;

        /// <summary>Reason Save is disabled (e.g. device not configured). Empty when Save is available.</summary>
        public string SaveDisabledReason => (!IsOfflineMode && HasActiveSession && !IsDeviceConfigured)
            ? "Cannot save – device must be programmed/configured."
            : string.Empty;

        /// <summary>Tooltip for Save button: shows disabled reason or default save description (NVM-only).</summary>
        public string SaveToDeviceToolTip => !string.IsNullOrEmpty(SaveDisabledReason) ? SaveDisabledReason : "Save current memory to NVM";
        public string SaveAllMemoriesToolTip => !string.IsNullOrEmpty(SaveDisabledReason) ? SaveDisabledReason : "Save all dirty memories to NVM";
        public bool IsMemoryLoadInProgress
        {
            get => _isMemoryLoadInProgress;
            private set { _isMemoryLoadInProgress = value; OnPropertyChanged(); }
        }

        /// <summary>True while Configure Device is running; disables the Configure button to prevent repeated clicks.</summary>
        public bool IsConfiguring
        {
            get => _isConfiguring;
            private set
            {
                if (_isConfiguring == value) return;
                _isConfiguring = value;
                OnPropertyChanged();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>True when Configure button should be visible: active session, at least one connected side that is NOT configured.</summary>
        public bool ShowConfigureButton => HasActiveSession && !IsOfflineMode &&
            ((_session.LeftConnected && !_session.IsSideConfigured(DeviceSide.Left)) ||
             (_session.RightConnected && !_session.IsSideConfigured(DeviceSide.Right)));

        /// <summary>Tooltip for Configure button.</summary>
        public string ConfigureDeviceToolTip => "Attempt to configure device (manufacturing). If unavailable, use vendor tools.";

        /// <summary>Status line for Fitting page: Connected, Configured, Dirty/Saved, Persisted to NVM.</summary>
        public string FittingStatusText
        {
            get
            {
                if (IsOfflineMode) return "Offline (Library)";
                if (!HasActiveSession) return "Not connected";
                var parts = new List<string> { "Connected" };
                if (IsDeviceConfigured)
                    parts.Add("Configured");
                else
                    parts.Add("Not configured");
                if (IsDeviceConfigured)
                    parts.Add(HasUnsavedChanges ? "Dirty" : "Saved");
                if (!string.IsNullOrEmpty(_nvmSaveStatus))
                    parts.Add(_nvmSaveStatus);
                return string.Join(" | ", parts);
            }
        }

        /// <summary>True when viewing library metadata without a connected device.</summary>
        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            private set { _isOfflineMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeLabel)); }
        }

        /// <summary>Label for the current session mode.</summary>
        public string ModeLabel => IsOfflineMode ? "Offline (Library)" : "Device Connected";

        public bool IsLiveModeEnabled
        {
            get => _isLiveModeEnabled;
            set { _isLiveModeEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(LiveModeLabel)); }
        }
        public string LiveModeLabel => IsLiveModeEnabled ? "Live Mode: On" : "Live Mode: Off";

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }
        public string LoadingMessage
        {
            get => _loadingMessage;
            set { _loadingMessage = value ?? ""; OnPropertyChanged(); }
        }
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        /// <summary>When false, the error banner hides the Retry button (e.g. unprogrammed device — retrying read won't help).</summary>
        public bool ShowRetryInErrorBanner
        {
            get => _showRetryInErrorBanner;
            private set { _showRetryInErrorBanner = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? "";
                OnPropertyChanged();
                DebouncedUpdateFilteredItems();
            }
        }
        public string SelectedTabId
        {
            get => _selectedTabId;
            set
            {
                _selectedTabId = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedTabTitle));
                EnsureTabLoadedAsync(_selectedTabId);
                UpdateFilteredItems();
            }
        }

        /// <summary>Title of the currently selected tab (for Expander header).</summary>
        public string SelectedTabTitle => Tabs.FirstOrDefault(t => string.Equals(t.Id, SelectedTabId, StringComparison.OrdinalIgnoreCase))?.Title ?? SelectedTabId ?? "";

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { _hasUnsavedChanges = value; _session.SetDirty(value); OnPropertyChanged(); OnPropertyChanged(nameof(FittingStatusText)); }
        }

        public bool IsCurrentMemoryDirty =>
            (_session.LeftConnected && _session.IsMemoryDirty(DeviceSide.Left, SelectedMemoryIndex)) ||
            (_session.RightConnected && _session.IsMemoryDirty(DeviceSide.Right, SelectedMemoryIndex));

        public bool HasAnyDirtyMemory => _session.HasAnyDirtyMemory();

        public DeviceSettingsSnapshot? LeftSnapshot
        {
            get => _leftSnapshot;
            set
            {
                _leftSnapshot = value;
                FittingUiCustomization.ApplyToSnapshot(_leftSnapshot);
                _session.SetFittingSnapshots(_leftSnapshot, _rightSnapshot);
                if (_leftSnapshot != null) _settingsCache.SetSnapshot(DeviceSide.Left, _leftSnapshot);
                SubscribeSnapshotDirty(_leftSnapshot, _rightSnapshot);
                OnPropertyChanged();
            }
        }
        public DeviceSettingsSnapshot? RightSnapshot
        {
            get => _rightSnapshot;
            set
            {
                _rightSnapshot = value;
                FittingUiCustomization.ApplyToSnapshot(_rightSnapshot);
                _session.SetFittingSnapshots(_leftSnapshot, _rightSnapshot);
                if (_rightSnapshot != null) _settingsCache.SetSnapshot(DeviceSide.Right, _rightSnapshot);
                SubscribeSnapshotDirty(_leftSnapshot, _rightSnapshot);
                OnPropertyChanged();
            }
        }

        /// <summary>Dynamic tab list (headers only). Groups loaded when tab is selected.</summary>
        public ObservableCollection<TabDescriptor> Tabs { get; } = new ObservableCollection<TabDescriptor>();

        /// <summary>Groups for the selected tab (Left). Rows loaded when group expander opens.</summary>
        public ObservableCollection<GroupDescriptor> LeftGroups { get; } = new ObservableCollection<GroupDescriptor>();
        /// <summary>Groups for the selected tab (Right). Rows loaded when group expander opens.</summary>
        public ObservableCollection<GroupDescriptor> RightGroups { get; } = new ObservableCollection<GroupDescriptor>();

        /// <summary>Flat list of parameter ViewModels for the current tab (Left side). Bound to ListView.</summary>
        public ObservableCollection<SettingItemViewModel> FilteredLeftItems
        {
            get => _filteredLeftItems;
            private set { _filteredLeftItems = value; OnPropertyChanged(); }
        }
        /// <summary>Flat list of parameter ViewModels for the current tab (Right side). Bound to ListView.</summary>
        public ObservableCollection<SettingItemViewModel> FilteredRightItems
        {
            get => _filteredRightItems;
            private set { _filteredRightItems = value; OnPropertyChanged(); }
        }

        /// <summary>Count of parameters in the selected tab (sum of group ParamsCount).</summary>
        public int VisibleParamCount => LeftGroups.Sum(g => g.ParamsCount) + RightGroups.Sum(g => g.ParamsCount);

        /// <summary>True when the Quick Fitting synthetic tab is selected.</summary>
        // ----- Quick Fitting: side contexts for Left / Right panels (Quick Fitting screen shares this VM) -----
        private QuickFittingSideContext _qfLeftContext = new();
        private QuickFittingSideContext _qfRightContext = new();
        private double _qfNrMin, _qfNrMax = 15, _qfNrStep = 1;

        public QuickFittingSideContext QfLeftContext { get => _qfLeftContext; private set { _qfLeftContext = value; OnPropertyChanged(); } }
        public QuickFittingSideContext QfRightContext { get => _qfRightContext; private set { _qfRightContext = value; OnPropertyChanged(); } }
        public double QfNrMin => _qfNrMin;
        public double QfNrMax => _qfNrMax;
        public double QfNrStep => _qfNrStep;

        private void ApplyNrMasterDepth(DeviceSettingsSnapshot? snapshot, double depth)
        {
            if (snapshot == null) return;
            foreach (var cat in snapshot.Categories)
                foreach (var sec in cat.Sections)
                    foreach (var item in sec.Items)
                    {
                        if (item.Name.StartsWith("X_NR_MaxDepth[", StringComparison.OrdinalIgnoreCase))
                            item.Value = depth;
                    }
        }

        private void BuildQuickFittingVMs()
        {
            var left = new QuickFittingSideContext
            {
                FbcEnable = FindParamVm(LeftSnapshot, "X_FBC_Enable"),
                FbcGainMgmtEnable = FindParamVm(LeftSnapshot, "X_FBC_GainManagementEnable"),
                FbcGainMgmtLimit = FindParamVm(LeftSnapshot, "X_FBC_GainManagementLimit"),
                NrEnable = FindParamVm(LeftSnapshot, "X_NR_Enable"),
                MmiEnable = FindParamVm(LeftSnapshot, "X_MMI_Enable"),
                PowerOnDelay = FindParamVm(LeftSnapshot, "X_FWK_PowerOnDelay"),
            };
            left.NrMasterDepthChanged += v => ApplyNrMasterDepth(LeftSnapshot, v);

            var right = new QuickFittingSideContext
            {
                FbcEnable = FindParamVm(RightSnapshot, "X_FBC_Enable"),
                FbcGainMgmtEnable = FindParamVm(RightSnapshot, "X_FBC_GainManagementEnable"),
                FbcGainMgmtLimit = FindParamVm(RightSnapshot, "X_FBC_GainManagementLimit"),
                NrEnable = FindParamVm(RightSnapshot, "X_NR_Enable"),
                MmiEnable = FindParamVm(RightSnapshot, "X_MMI_Enable"),
                PowerOnDelay = FindParamVm(RightSnapshot, "X_FWK_PowerOnDelay"),
            };
            right.NrMasterDepthChanged += v => ApplyNrMasterDepth(RightSnapshot, v);

            // NR master slider: read first X_NR_MaxDepth value + range
            var nrSample = FindParam(LeftSnapshot, "X_NR_MaxDepth[0]") ?? FindParam(RightSnapshot, "X_NR_MaxDepth[0]");
            if (nrSample != null)
            {
                _qfNrMin = double.IsNaN(nrSample.Min) ? 0 : nrSample.Min;
                _qfNrMax = double.IsNaN(nrSample.Max) ? 15 : nrSample.Max;
                _qfNrStep = (double.IsNaN(nrSample.Step) || nrSample.Step <= 0) ? 1 : nrSample.Step;
                left.NrSliderMin = right.NrSliderMin = _qfNrMin;
                left.NrSliderMax = right.NrSliderMax = _qfNrMax;
                left.NrSliderStep = right.NrSliderStep = _qfNrStep;
                OnPropertyChanged(nameof(QfNrMin));
                OnPropertyChanged(nameof(QfNrMax));
                OnPropertyChanged(nameof(QfNrStep));
            }
            double initDepth = 6;
            if (nrSample?.Value is double d) initDepth = d;
            else if (nrSample?.Value != null && double.TryParse(nrSample.Value.ToString(), out var parsed)) initDepth = parsed;
            left.NrMasterDepth = initDepth;
            right.NrMasterDepth = initDepth;

            QfLeftContext = left;
            QfRightContext = right;
        }

        private static SettingItemViewModel? FindParamVm(DeviceSettingsSnapshot? snapshot, string paramName)
        {
            var item = FindParam(snapshot, paramName);
            return item != null ? new SettingItemViewModel(item) : null;
        }

        private static SettingItem? FindParam(DeviceSettingsSnapshot? snapshot, string paramName)
        {
            if (snapshot == null) return null;
            foreach (var cat in snapshot.Categories)
                foreach (var sec in cat.Sections)
                    foreach (var item in sec.Items)
                        if (string.Equals(item.Name, paramName, StringComparison.OrdinalIgnoreCase))
                            return item;
            return null;
        }

        public ICommand EnableLiveModeCommand { get; }
        public ICommand SaveToDeviceCommand { get; }
        public ICommand SaveAllMemoriesCommand { get; }
        public ICommand SelectTabCommand { get; }
        public ICommand RefreshReadCommand { get; }
        /// <summary>Call when left panel group Expander is expanded. Parameter = group Id.</summary>
        public ICommand EnsureGroupLoadedLeftCommand { get; }
        /// <summary>Call when right panel group Expander is expanded. Parameter = group Id.</summary>
        public ICommand EnsureGroupLoadedRightCommand { get; }

        /// <summary>Call when entering Fitting page. Loads from device if connected, or from library if offline.</summary>
        public void OnNavigatedTo()
        {
            if (_sessionMgr.IsOffline && !_session.HasActiveSession)
            {
                LoadOfflineFromLibrary();
            }
            else if (!IsLoading)
            {
                LoadSettingsAsync();
            }
        }

        /// <summary>Populates tabs from library metadata without device connection (single memory).</summary>
        private void LoadOfflineFromLibrary()
        {
            IsLoading = true;
            ErrorMessage = "";
            ShowRetryInErrorBanner = true;
            LoadingMessage = "Loading parameters from library…";
            IsOfflineMode = true;

            try
            {
                // Sync memory index from session manager
                _selectedMemoryIndex = _sessionMgr.SelectedMemoryIndex;
                _session.SetSelectedMemoryIndex(_selectedMemoryIndex);

                LeftSnapshot = _sessionMgr.LeftSnapshot;
                RightSnapshot = null; // Offline: show only left panel with library defaults
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to load library parameters: " + ex.Message;
                ShowRetryInErrorBanner = true;
                DiagnosticService.Instance.RecordException("LoadOffline", DiagnosticCategory.Persistence, ex, "Fitting", _selectedMemoryIndex, null);
            }
            finally
            {
                IsLoading = false;
                BuildItemCaches();
                RebuildDynamicTabs();
                BuildQuickFittingVMs();
                UpdateFilteredItems();
                OnPropertyChanged(nameof(HasActiveSession));
                OnPropertyChanged(nameof(ShowLeftCard));
                OnPropertyChanged(nameof(ShowRightCard));
                OnPropertyChanged(nameof(SelectedMemoryIndex));
                OnPropertyChanged(nameof(IsCurrentMemoryDirty));
                OnPropertyChanged(nameof(HasAnyDirtyMemory));
                OnPropertyChanged(nameof(ParamFileName));
                OnPropertyChanged(nameof(FittingStatusText));
            }
        }

        /// <summary>
        /// Called when user selects a memory.
        /// Pipeline:
        /// 1) set SelectedMemoryIndex
        /// 2) ensure memory snapshot is loaded (cache/device/offline)
        /// 3) swap current bound snapshots from cache
        /// 4) refresh dirty/UI state without rebuilding tab metadata
        /// </summary>
        private async Task OnMemoryChangedAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Memory] SelectedMemory={_selectedMemoryIndex + 1}");
                IsMemoryLoadInProgress = true;
                IsLoading = true;
                LoadingMessage = $"Loading Memory {_selectedMemoryIndex + 1}…";

                if (_session.LeftConnected)
                    await EnsureMemoryLoadedAsync(DeviceSide.Left, _selectedMemoryIndex, CancellationToken.None);
                if (_session.RightConnected)
                    await EnsureMemoryLoadedAsync(DeviceSide.Right, _selectedMemoryIndex, CancellationToken.None);

                if (!_session.HasActiveSession)
                {
                    // Offline path keeps existing behavior through session manager.
                    _sessionMgr.SwitchMemory(_selectedMemoryIndex);
                    LeftSnapshot = _sessionMgr.LeftSnapshot;
                    RightSnapshot = null;
                }
                else
                {
                    if (_session.TryGetMemorySnapshot(DeviceSide.Left, _selectedMemoryIndex, out var left))
                        LeftSnapshot = left;
                    if (_session.TryGetMemorySnapshot(DeviceSide.Right, _selectedMemoryIndex, out var right))
                        RightSnapshot = right;
                }

                BuildItemCaches();
                EnsureTabLoadedAsync(SelectedTabId);
                UpdateFilteredItems();
                RefreshSaveState();
                System.Diagnostics.Debug.WriteLine($"[FittingVM] Memory switched to {_selectedMemoryIndex + 1}");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error switching memory: {ex.Message}";
                ShowRetryInErrorBanner = true;
                DiagnosticService.Instance.RecordException("MemorySwitch", DiagnosticCategory.Device, ex, "Fitting", _selectedMemoryIndex, null);
            }
            finally
            {
                IsLoading = false;
                IsMemoryLoadInProgress = false;
            }
        }

        #region Live Mode

        private void ToggleLiveMode()
        {
            IsLiveModeEnabled = !IsLiveModeEnabled;
            _session.SetLiveDisplayEnabled(IsLiveModeEnabled);
            if (IsLiveModeEnabled)
            {
                _liveModeDebounce = new System.Timers.Timer(400) { AutoReset = false };
                _liveModeDebounce.Elapsed += (_, _) => Application.Current?.Dispatcher?.BeginInvoke(() => FlushLiveModeWrites());
            }
            else
            {
                _liveModeDebounce?.Stop();
                _liveModeDebounce?.Dispose();
                _liveModeDebounce = null;
            }
        }

        private void FlushLiveModeWrites()
        {
            if (!IsLiveModeEnabled || !HasActiveSession) return;
            var product = _session.SdkManager?.GetProduct();
            if (product == null) return;
            if (_session.LeftConnected && LeftSnapshot != null)
            {
                var adaptor = _session.ConnectionService?.GetConnection(DeviceSide.Left);
                if (adaptor != null)
                    _ = _soundDesigner.WriteSettingsAsync(product, adaptor, LeftSnapshot, null, CancellationToken.None);
            }
            if (_session.RightConnected && RightSnapshot != null)
            {
                var adaptor = _session.ConnectionService?.GetConnection(DeviceSide.Right);
                if (adaptor != null)
                    _ = _soundDesigner.WriteSettingsAsync(product, adaptor, RightSnapshot, null, CancellationToken.None);
            }
            _liveModeDebounce?.Start();
        }

        #endregion

        #region Tab selection

        private void SelectTab(string tabId)
        {
            if (string.IsNullOrEmpty(tabId)) return;
            SelectedTabId = tabId;
        }

        /// <summary>200ms debounce for search/filter to avoid dispatcher storms.</summary>
        private void DebouncedUpdateFilteredItems()
        {
            _searchDebounce?.Stop();
            _searchDebounce = new System.Timers.Timer(200) { AutoReset = false };
            _searchDebounce.Elapsed += (_, _) =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    UpdateFilteredItems();
                    _searchDebounce?.Dispose();
                    _searchDebounce = null;
                });
            };
            _searchDebounce.Start();
        }

        /// <summary>Build group list for the selected tab (metadata only). Rows loaded when group expander opens.</summary>
        public void EnsureTabLoadedAsync(string tabId)
        {
            if (string.IsNullOrEmpty(tabId)) return;
            var sw = Stopwatch.StartNew();
            var leftGroups = BuildGroupDescriptorsForTab(LeftSnapshot, tabId);
            var rightGroups = BuildGroupDescriptorsForTab(RightSnapshot, tabId);
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                LeftGroups.Clear();
                foreach (var g in leftGroups) LeftGroups.Add(g);
                RightGroups.Clear();
                foreach (var g in rightGroups) RightGroups.Add(g);
                // Load rows for all groups so expanded-by-default groups show content (Expanded event may not fire on first render)
                foreach (var g in leftGroups)
                    EnsureGroupLoadedAsync(DeviceSide.Left, g.Id);
                foreach (var g in rightGroups)
                    EnsureGroupLoadedAsync(DeviceSide.Right, g.Id);
                var tab = Tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.OrdinalIgnoreCase));
                if (tab != null) tab.IsLoaded = true;
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"[Perf] LoadTab tab={tabId} ms={sw.ElapsedMilliseconds} leftGroups={leftGroups.Count} rightGroups={rightGroups.Count}");
                OnPropertyChanged(nameof(LeftGroups));
                OnPropertyChanged(nameof(RightGroups));
                OnPropertyChanged(nameof(VisibleParamCount));
            });
        }

        private static List<GroupDescriptor> BuildGroupDescriptorsForTab(DeviceSettingsSnapshot? snapshot, string tabId)
        {
            var list = new List<GroupDescriptor>();
            if (snapshot == null) return list;
            var cat = snapshot.Categories.FirstOrDefault(c => string.Equals(c.Id, tabId, StringComparison.OrdinalIgnoreCase));
            if (cat == null) return list;
            foreach (var sec in cat.Sections)
            {
                var visibleCount = FittingUiCustomization.CountVisibleParams(sec, cat.Title);
                if (visibleCount > 0)
                    list.Add(new GroupDescriptor { Id = sec.Id, Title = sec.Title, ParamsCount = visibleCount, IsLoaded = false });
            }
            return list;
        }

        /// <summary>Build rows for a group when its expander opens. Call from View (e.g. Expander.Expanded).</summary>
        public void EnsureGroupLoadedAsync(DeviceSide side, string groupId)
        {
            var tabId = SelectedTabId;
            if (string.IsNullOrEmpty(tabId) || string.IsNullOrEmpty(groupId)) return;
            var snapshot = side == DeviceSide.Left ? LeftSnapshot : RightSnapshot;
            var cache = side == DeviceSide.Left ? _leftGroupRowsCache : _rightGroupRowsCache;
            var key = (tabId, groupId);
            if (cache.TryGetValue(key, out var cached))
            {
                var group = (side == DeviceSide.Left ? LeftGroups : RightGroups).FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
                if (group != null && !group.IsLoaded)
                {
                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        group.Rows = new ObservableCollection<SettingItemViewModel>(cached);
                        group.IsLoaded = true;
                        System.Diagnostics.Debug.WriteLine($"[Perf] LoadGroup tab={tabId} group={groupId} count={cached.Count} (cached)");
                    });
                }
                return;
            }
            var sw = Stopwatch.StartNew();
            var searchLower = SearchText?.Trim().ToLowerInvariant() ?? "";
            var rows = BuildRowsForGroup(snapshot, tabId, groupId, searchLower);
            cache[key] = rows;
            var groupToUpdate = (side == DeviceSide.Left ? LeftGroups : RightGroups).FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
            if (groupToUpdate != null)
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    groupToUpdate.Rows = new ObservableCollection<SettingItemViewModel>(rows);
                    groupToUpdate.IsLoaded = true;
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"[Perf] LoadGroup tab={tabId} group={groupId} count={rows.Count} ms={sw.ElapsedMilliseconds}");
                });
            }
        }

        private static List<SettingItemViewModel> BuildRowsForGroup(DeviceSettingsSnapshot? snapshot, string tabId, string groupId, string searchLower)
        {
            var list = new List<SettingItemViewModel>();
            if (snapshot == null) return list;
            var cat = snapshot.Categories.FirstOrDefault(c => string.Equals(c.Id, tabId, StringComparison.OrdinalIgnoreCase));
            var sec = cat?.Sections.FirstOrDefault(s => string.Equals(s.Id, groupId, StringComparison.OrdinalIgnoreCase));
            if (sec == null) return list;
            var tabTitle = cat?.Title ?? tabId;
            foreach (var item in sec.Items)
            {
                if (FittingUiCustomization.IsHiddenInTab(item, tabTitle)) continue;
                if (searchLower.Length > 0 && !MatchesSearch(item, searchLower)) continue;
                list.Add(new SettingItemViewModel(item));
            }
            return list;
        }

        private static bool MatchesSearch(SettingItem item, string searchLower)
        {
            return (item.Name?.ToLowerInvariant().Contains(searchLower) == true) ||
                   (item.DisplayName?.ToLowerInvariant().Contains(searchLower) == true) ||
                   (item.Description?.ToLowerInvariant().Contains(searchLower) == true) ||
                   (item.Id?.ToLowerInvariant().Contains(searchLower) == true);
        }

        /// <summary>
        /// Rebuilds the Tabs collection (headers only) from the union of Left and Right snapshot categories.
        /// Groups are loaded when user selects a tab.
        /// </summary>
        private void RebuildDynamicTabs()
        {
            var sw = Stopwatch.StartNew();
            var tabSet = new List<(string Id, string Title, int GroupsCount)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFromSnapshot(DeviceSettingsSnapshot? snapshot)
            {
                if (snapshot == null) return;
                foreach (var cat in snapshot.Categories)
                {
                    if (seen.Contains(cat.Id)) continue;
                    seen.Add(cat.Id);
                    tabSet.Add((cat.Id, cat.Title, cat.Sections.Count));
                }
            }

            AddFromSnapshot(LeftSnapshot);
            AddFromSnapshot(RightSnapshot);

            Tabs.Clear();
            foreach (var (id, title, groupsCount) in tabSet)
                Tabs.Add(new TabDescriptor { Id = id, Title = title, GroupsCount = groupsCount, IsLoaded = false });

            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[Perf] BuildTabHeaders ms={sw.ElapsedMilliseconds} count={Tabs.Count}");

            OnPropertyChanged(nameof(Tabs));
            OnPropertyChanged(nameof(SelectedTabTitle));

            if (Tabs.Count > 0 && !Tabs.Any(t => string.Equals(t.Id, SelectedTabId, StringComparison.OrdinalIgnoreCase)))
                SelectedTabId = Tabs[0].Id;
            else if (Tabs.Count == 0)
                SelectedTabId = "";
        }

        #endregion

        #region Dirty tracking

        private void SubscribeSnapshotDirty(DeviceSettingsSnapshot? left, DeviceSettingsSnapshot? right)
        {
            _dirtyUnsubscribe?.ForEach(u => u());
            _dirtyUnsubscribe = new List<Action>();
            void SubscribeSnapshot(DeviceSettingsSnapshot? snapshot, DeviceSide side)
            {
                if (snapshot == null) return;
                foreach (var cat in snapshot.Categories)
                    foreach (var sec in cat.Sections)
                        foreach (var item in sec.Items)
                        {
                            void Handler(object? o, PropertyChangedEventArgs e)
                            {
                                if (e.PropertyName == nameof(SettingItem.Value))
                                {
                                    _settingsCache.DirtyBuffer.Add(side, item.Id, item.Value);
                                    bool newlyDirty = _session.MarkMemoryDirty(side, SelectedMemoryIndex);
                                    if (newlyDirty)
                                    {
                                        _nvmSaveStatus = string.Empty;
                                        OnPropertyChanged(nameof(FittingStatusText));
                                        System.Diagnostics.Debug.WriteLine($"[Memory] Dirty side={side} memory={SelectedMemoryIndex + 1} changedParam={item.Id}");
                                    }
                                    HasUnsavedChanges = true;
                                    OnPropertyChanged(nameof(IsCurrentMemoryDirty));
                                    OnPropertyChanged(nameof(HasAnyDirtyMemory));
                                }
                            }
                            item.PropertyChanged += Handler;
                            _dirtyUnsubscribe.Add(() => item.PropertyChanged -= Handler);
                        }
            }
            SubscribeSnapshot(left, DeviceSide.Left);
            SubscribeSnapshot(right, DeviceSide.Right);
        }

        #endregion

        #region Load / Save

        private async void LoadSettingsAsync()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;
            _session.SetSelectedMemoryIndex(_selectedMemoryIndex);
            IsLoading = true;
            IsOfflineMode = false;
            ErrorMessage = "";
            ShowRetryInErrorBanner = true;
            LoadingMessage = "Reading device parameters…";
            LeftSnapshot = null;
            RightSnapshot = null;
            Tabs.Clear();
            _settingsCache.InvalidateAll();

            try
            {
                var product = _session.SdkManager?.GetProduct();
                var conn = _session.ConnectionService;
                if (product == null || conn == null)
                {
                    ErrorMessage = "Device connection service not available. Please reconnect and try again.";
                    IsLoading = false;
                    return;
                }

                // Sync selected memory from shared source (DeviceSessionService) so Audiogram and Fitting stay aligned
                _selectedMemoryIndex = _session.SelectedMemoryIndex;
                OnPropertyChanged(nameof(SelectedMemoryIndex));
                OnPropertyChanged(nameof(SelectedMemoryName));

                // If we already know the device is not configured (from a previous Ensure run), skip redundant
                // InitializeDevice + probe and just build library snapshots so LoadSettings finishes quickly.
                if (!_session.IsConfigured && (_session.LeftConnected || _session.RightConnected) && !string.IsNullOrEmpty(_session.LastConfigError))
                {
                    ErrorMessage = _session.LastConfigError;
                    if (_session.LeftConnected)
                    {
                        var leftOffline = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, _selectedMemoryIndex, DeviceSide.Left);
                        LeftSnapshot = leftOffline;
                        _session.SetMemorySnapshot(DeviceSide.Left, _selectedMemoryIndex, leftOffline);
                        System.Diagnostics.Debug.WriteLine($"[Memory] Load side=Left memory={_selectedMemoryIndex + 1} source=offline");
                    }
                    if (_session.RightConnected)
                    {
                        var rightOffline = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, _selectedMemoryIndex, DeviceSide.Right);
                        RightSnapshot = rightOffline;
                        _session.SetMemorySnapshot(DeviceSide.Right, _selectedMemoryIndex, rightOffline);
                        System.Diagnostics.Debug.WriteLine($"[Memory] Load side=Right memory={_selectedMemoryIndex + 1} source=offline");
                    }
                    System.Diagnostics.Debug.WriteLine("[FittingVM] LoadSettings: device already known not configured — skip Ensure, use library snapshots.");
                }
                else
                {

                // Gate: ensure device is initialized and configured before any ReadParameters.
                if (_session.LeftConnected)
                {
                    try
                    {
                        LoadingMessage = "Ensuring device initialized…";
                        await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(_session, DeviceSide.Left, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FittingVM] EnsureInitializedAndConfigured(Left) failed: {ex.Message}");
                        ScanDiagnostics.WriteLine($"[FittingVM] EnsureInitializedAndConfigured(Left) failed: " + ex.Message);
                        _session.SetLastConfigError(ex.Message);
                    }
                }
                if (_session.RightConnected && !ct.IsCancellationRequested)
                {
                    try
                    {
                        LoadingMessage = "Ensuring device initialized…";
                        await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(_session, DeviceSide.Right, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FittingVM] EnsureInitializedAndConfigured(Right) failed: {ex.Message}");
                        ScanDiagnostics.WriteLine($"[FittingVM] EnsureInitializedAndConfigured(Right) failed: " + ex.Message);
                        _session.SetLastConfigError(ex.Message);
                    }
                }

                if (!_session.IsConfigured && (_session.LeftConnected || _session.RightConnected))
                {
                    ErrorMessage = _session.LastConfigError ?? "Device must be configured (InitializeDevice returned false).";
                    System.Diagnostics.Debug.WriteLine("[FittingVM] LoadSettings: device not configured — ReadParameters blocked.");
                    ScanDiagnostics.WriteLine("[FittingVM] LoadSettings: device not configured — ReadParameters blocked.");
                }

                var progress = new Progress<string>(m => LoadingMessage = m);

                // Read Left side (only if device is configured — avoids E_UNCONFIGURED_DEVICE)
                if (_session.LeftConnected && _session.IsSideConfigured(DeviceSide.Left))
                {
                    var adaptor = conn.GetConnection(DeviceSide.Left);
                    if (adaptor != null)
                    {
                        try
                        {
                            LoadingMessage = "Reading Left device parameters…";
                            var readSw = System.Diagnostics.Stopwatch.StartNew();
                            var left = await _soundDesigner.ReloadFromNvmAsync(product, adaptor, DeviceSide.Left, _selectedMemoryIndex, progress, ct);
                            readSw.Stop();
                            var leftCount = left?.Categories.SelectMany(c => c.Sections.SelectMany(s => s.Items)).Count() ?? 0;
                            var leftSerial = AppSessionState.Instance.LeftSerialId;
                            System.Diagnostics.Debug.WriteLine($"[WriteVerify] RECONNECT LOAD: side=Left memoryIndex={_selectedMemoryIndex} memoryLabel=M{_selectedMemoryIndex + 1} serial={leftSerial ?? "?"} params={leftCount} ms={readSw.ElapsedMilliseconds}");
                            ScanDiagnostics.WriteLine($"[WriteVerify] RECONNECT LOAD: side=Left memoryIndex={_selectedMemoryIndex} memoryLabel=M{_selectedMemoryIndex + 1} serial={leftSerial ?? "?"} params={leftCount} ms={readSw.ElapsedMilliseconds}");
                            System.Diagnostics.Debug.WriteLine($"[Perf] ReadSpace side=Left ms={readSw.ElapsedMilliseconds} params={leftCount}");
                            System.Diagnostics.Debug.WriteLine($"[Memory] Load side=Left memory={_selectedMemoryIndex + 1} source=device");
                            LeftSnapshot = left;
                            _session.SetMemorySnapshot(DeviceSide.Left, _selectedMemoryIndex, left);
                        }
                        catch (Exception ex)
                        {
                            DiagnosticService.Instance.RecordException("ReadParameters", DiagnosticCategory.SDK, ex, "Fitting", _selectedMemoryIndex, DeviceSide.Left);
                            ErrorMessage = "Left: " + GetUserFriendlyMessage(ex);
                        }
                    }
                }
                else if (_session.LeftConnected && !_session.IsSideConfigured(DeviceSide.Left))
                {
                    // Show parameters from library so the UI has tabs/items; values are not from device.
                    var leftOffline = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, _selectedMemoryIndex, DeviceSide.Left);
                    LeftSnapshot = leftOffline;
                    _session.SetMemorySnapshot(DeviceSide.Left, _selectedMemoryIndex, leftOffline);
                    System.Diagnostics.Debug.WriteLine($"[Memory] Load side=Left memory={_selectedMemoryIndex + 1} source=offline");
                }

                // Read Right side (only if device is configured)
                if (_session.RightConnected && !ct.IsCancellationRequested && _session.IsSideConfigured(DeviceSide.Right))
                {
                    var adaptor = conn.GetConnection(DeviceSide.Right);
                    if (adaptor != null)
                    {
                        try
                        {
                            LoadingMessage = "Reading Right device parameters…";
                            var readSw = System.Diagnostics.Stopwatch.StartNew();
                            var right = await _soundDesigner.ReloadFromNvmAsync(product, adaptor, DeviceSide.Right, _selectedMemoryIndex, progress, ct);
                            readSw.Stop();
                            var rightCount = right?.Categories.SelectMany(c => c.Sections.SelectMany(s => s.Items)).Count() ?? 0;
                            var rightSerial = AppSessionState.Instance.RightSerialId;
                            System.Diagnostics.Debug.WriteLine($"[WriteVerify] RECONNECT LOAD: side=Right memoryIndex={_selectedMemoryIndex} memoryLabel=M{_selectedMemoryIndex + 1} serial={rightSerial ?? "?"} params={rightCount} ms={readSw.ElapsedMilliseconds}");
                            ScanDiagnostics.WriteLine($"[WriteVerify] RECONNECT LOAD: side=Right memoryIndex={_selectedMemoryIndex} memoryLabel=M{_selectedMemoryIndex + 1} serial={rightSerial ?? "?"} params={rightCount} ms={readSw.ElapsedMilliseconds}");
                            System.Diagnostics.Debug.WriteLine($"[Perf] ReadSpace side=Right ms={readSw.ElapsedMilliseconds} params={rightCount}");
                            System.Diagnostics.Debug.WriteLine($"[Memory] Load side=Right memory={_selectedMemoryIndex + 1} source=device");
                            RightSnapshot = right;
                            _session.SetMemorySnapshot(DeviceSide.Right, _selectedMemoryIndex, right);
                        }
                        catch (Exception ex)
                        {
                            DiagnosticService.Instance.RecordException("ReadParameters", DiagnosticCategory.SDK, ex, "Fitting", _selectedMemoryIndex, DeviceSide.Right);
                            ErrorMessage = string.IsNullOrEmpty(ErrorMessage) ? "Right: " + GetUserFriendlyMessage(ex) : ErrorMessage + "; Right: " + GetUserFriendlyMessage(ex);
                        }
                    }
                }
                else if (_session.RightConnected && !_session.IsSideConfigured(DeviceSide.Right))
                {
                    if (!string.IsNullOrEmpty(ErrorMessage)) ErrorMessage += " ";
                    ErrorMessage += "Right device is not configured. Parameters cannot be read.";
                    var rightOffline = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, _selectedMemoryIndex, DeviceSide.Right);
                    RightSnapshot = rightOffline;
                    _session.SetMemorySnapshot(DeviceSide.Right, _selectedMemoryIndex, rightOffline);
                    System.Diagnostics.Debug.WriteLine($"[Memory] Load side=Right memory={_selectedMemoryIndex + 1} source=offline");
                }

                } // end else (run Ensure + read/snapshots)
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DiagnosticService.Instance.RecordException("LoadSettings", DiagnosticCategory.Device, ex, "Fitting", _selectedMemoryIndex, null);
                ErrorMessage = "Error: " + GetUserFriendlyMessage(ex);
            }
            finally
            {
                IsLoading = false;
                BuildItemCaches();
                RebuildDynamicTabs();
                BuildQuickFittingVMs();
                UpdateFilteredItems();
                OnPropertyChanged(nameof(IsDeviceConfigured));
                OnPropertyChanged(nameof(SaveDisabledReason));
                OnPropertyChanged(nameof(SaveToDeviceToolTip));
                OnPropertyChanged(nameof(SaveAllMemoriesToolTip));
                OnPropertyChanged(nameof(ShowConfigureButton));
                OnPropertyChanged(nameof(ConfigureDeviceToolTip));
                OnPropertyChanged(nameof(FittingStatusText));
                OnPropertyChanged(nameof(IsCurrentMemoryDirty));
                OnPropertyChanged(nameof(HasAnyDirtyMemory));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async void ConfigureDeviceAsync()
        {
            if (_isConfiguring) return;
            if (!ShowConfigureButton) return;

            var result = MessageBox.Show(
                "Configure Device is a manufacturing operation. It will write a full parameter set to the device so that Read/Write can be used.\n\nContinue?",
                "Configure Device (Manufacturing)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            IsConfiguring = true;
            _session.SetConfigureRunning(true);
            IsLoading = true;
            LoadingMessage = "Configuring device…";
            ErrorMessage = "";

            var progress = new Progress<string>(m => LoadingMessage = m);

            try
            {
                await DeviceInitializationService.RunConfigureDeviceAsync(_session, progress, CancellationToken.None);

                if (_session.IsConfigured)
                {
                    LoadingMessage = "Device configured. Reading settings…";
                    LoadSettingsAsync();
                }
                else
                {
                    IsLoading = false;
                    ErrorMessage = _session.LastConfigError ?? "Configure failed. Use vendor manufacturing tools if needed.";
                }
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.RecordException("ConfigureDevice", DiagnosticCategory.Device, ex, "Fitting", null, null);
                System.Diagnostics.Debug.WriteLine($"[FittingVM] ConfigureDevice failed: {ex.Message}");
                ScanDiagnostics.WriteLine($"[FittingVM] ConfigureDevice failed: " + ex.Message);
                ErrorMessage = _session.LastConfigError ?? GetUserFriendlyMessage(ex);
                IsLoading = false;
            }
            finally
            {
                IsConfiguring = false;
                _session.SetConfigureRunning(false);
                OnPropertyChanged(nameof(IsDeviceConfigured));
                OnPropertyChanged(nameof(ShowConfigureButton));
                OnPropertyChanged(nameof(ConfigureDeviceToolTip));
                OnPropertyChanged(nameof(FittingStatusText));
            }
        }

        private static bool IsUnprogrammed(string? serialId)
        {
            return string.IsNullOrEmpty(serialId) || serialId == "-1" || serialId == "0";
        }

        private async Task EnsureMemoryLoadedAsync(DeviceSide side, int memoryIndex, CancellationToken ct)
        {
            if (memoryIndex < 0 || memoryIndex > 7) return;
            if (_session.TryGetMemorySnapshot(side, memoryIndex, out var cached) && cached != null)
            {
                System.Diagnostics.Debug.WriteLine($"[Memory] Load side={side} memory={memoryIndex + 1} source=cache");
                return;
            }

            var product = _session.SdkManager?.GetProduct();
            var conn = _session.ConnectionService;
            if (product == null || conn == null) return;

            await _memoryIoLock.WaitAsync(ct);
            try
            {
                if (_session.TryGetMemorySnapshot(side, memoryIndex, out cached) && cached != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Memory] Load side={side} memory={memoryIndex + 1} source=cache");
                    return;
                }

                if (!_session.IsSideConfigured(side))
                {
                    var offline = SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, memoryIndex, side);
                    _session.SetMemorySnapshot(side, memoryIndex, offline);
                    System.Diagnostics.Debug.WriteLine($"[Memory] Load side={side} memory={memoryIndex + 1} source=offline");
                    return;
                }

                var adaptor = conn.GetConnection(side);
                if (adaptor == null) return;

                // NVM-only: Restore from NVM then read from RAM (presuite_memory_switch.py).
                var progress = new Progress<string>(m => LoadingMessage = m);
                var snapshot = await _soundDesigner.ReloadFromNvmAsync(product, adaptor, side, memoryIndex, progress, ct);
                _session.SetMemorySnapshot(side, memoryIndex, snapshot);
                System.Diagnostics.Debug.WriteLine($"[Memory] Load side={side} memory={memoryIndex + 1} source=device");
                LogDeviceLoadFingerprint(side, memoryIndex + 1, snapshot);
            }
            finally
            {
                _memoryIoLock.Release();
            }
        }

        /// <summary>Logs a fingerprint of the loaded device snapshot (first param Id and value) for diagnostics when verifying persistence on reconnect.</summary>
        private static void LogDeviceLoadFingerprint(DeviceSide side, int memoryOneBased, DeviceSettingsSnapshot? snapshot)
        {
            if (snapshot == null) return;
            var first = snapshot.Categories
                .SelectMany(c => c.Sections.SelectMany(s => s.Items))
                .FirstOrDefault();
            if (first != null)
                System.Diagnostics.Debug.WriteLine($"[Memory] Load fingerprint side={side} memory={memoryOneBased} firstParam={first.Id} value={first.Value}");
        }

        private async void SaveToDeviceAsync()
        {
            await SaveCurrentMemoryToNvmAsync(CancellationToken.None);
        }

        /// <summary>NVM-only: Reload current memory from NVM then read from RAM for both sides. Logs [NVM] Restore M#.</summary>
        private async Task ReloadFromNvmAsync(CancellationToken ct)
        {
            if (!HasActiveSession || IsOfflineMode || !_session.IsConfigured) return;
            var product = _session.SdkManager?.GetProduct();
            var conn = _session.ConnectionService;
            if (product == null || conn == null) return;

            IsLoading = true;
            LoadingMessage = $"Reloading Memory {SelectedMemoryIndex + 1} from NVM…";
            try
            {
                if (_session.LeftConnected)
                {
                    await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(_session, DeviceSide.Left, ct);
                    var adaptor = conn.GetConnection(DeviceSide.Left);
                    if (adaptor != null && _session.IsSideConfigured(DeviceSide.Left))
                    {
                        var left = await _soundDesigner.ReloadFromNvmAsync(product, adaptor, DeviceSide.Left, _selectedMemoryIndex, new Progress<string>(m => LoadingMessage = m), ct);
                        _session.SetMemorySnapshot(DeviceSide.Left, _selectedMemoryIndex, left);
                        LeftSnapshot = left;
                    }
                }
                if (_session.RightConnected && !ct.IsCancellationRequested)
                {
                    await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(_session, DeviceSide.Right, ct);
                    var adaptor = conn.GetConnection(DeviceSide.Right);
                    if (adaptor != null && _session.IsSideConfigured(DeviceSide.Right))
                    {
                        var right = await _soundDesigner.ReloadFromNvmAsync(product, adaptor, DeviceSide.Right, _selectedMemoryIndex, new Progress<string>(m => LoadingMessage = m), ct);
                        _session.SetMemorySnapshot(DeviceSide.Right, _selectedMemoryIndex, right);
                        RightSnapshot = right;
                    }
                }
                BuildItemCaches();
                UpdateFilteredItems();
                RefreshSaveState();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveCurrentMemoryToNvmAsync(CancellationToken ct)
        {
            if (!HasActiveSession || IsOfflineMode) return;
            if (!IsCurrentMemoryDirty || !_session.IsConfigured) return;

            var product = _session.SdkManager?.GetProduct();
            var conn = _session.ConnectionService;
            if (product == null || conn == null) return;

            var appState = AppSessionState.Instance;
            bool leftUnprogrammed = _session.LeftConnected && IsUnprogrammed(appState.LeftSerialId);
            bool rightUnprogrammed = _session.RightConnected && IsUnprogrammed(appState.RightSerialId);
            if (leftUnprogrammed || rightUnprogrammed)
            {
                ErrorMessage = "Device(s) unprogrammed (Serial=-1 or 0). Save to device is not available. Use a programmed device or manufacturing tools.";
                ShowRetryInErrorBanner = false;
                return;
            }

            IsLoading = true;
            LoadingMessage = $"Saving Memory {SelectedMemoryIndex + 1}…";
            try
            {
                if (_session.LeftConnected)
                    await EnsureMemoryLoadedAsync(DeviceSide.Left, SelectedMemoryIndex, ct);
                if (_session.RightConnected)
                    await EnsureMemoryLoadedAsync(DeviceSide.Right, SelectedMemoryIndex, ct);
                var (left, right) = _session.GetSnapshotsForMemory(SelectedMemoryIndex);

                if (left != null && _session.LeftConnected)
                    await SaveMemoryForSideToNvmAsync(product, conn, DeviceSide.Left, SelectedMemoryIndex, left, ct);
                if (right != null && _session.RightConnected)
                    await SaveMemoryForSideToNvmAsync(product, conn, DeviceSide.Right, SelectedMemoryIndex, right, ct);

                RefreshSaveState();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task SaveAllMemoriesToNvmAsync(bool onlyDirty, CancellationToken ct)
        {
            if (!HasActiveSession || IsOfflineMode) return;

            var product = _session.SdkManager?.GetProduct();
            var conn = _session.ConnectionService;
            if (product == null || conn == null) return;

            var dirtyTotal = _session.GetDirtyMemoryCount();
            System.Diagnostics.Debug.WriteLine($"[NVM] SaveAll onlyDirty={onlyDirty.ToString().ToLowerInvariant()} totalDirty={dirtyTotal}");

            IsLoading = true;
            LoadingMessage = "Saving all memories to NVM…";
            try
            {
                for (int memoryIndex = 0; memoryIndex < 8; memoryIndex++)
                {
                    ct.ThrowIfCancellationRequested();
                    bool leftDirty = _session.IsMemoryDirty(DeviceSide.Left, memoryIndex);
                    bool rightDirty = _session.IsMemoryDirty(DeviceSide.Right, memoryIndex);
                    if (onlyDirty && !leftDirty && !rightDirty)
                        continue;

                    if (_session.LeftConnected)
                        await EnsureMemoryLoadedAsync(DeviceSide.Left, memoryIndex, ct);
                    if (_session.RightConnected)
                        await EnsureMemoryLoadedAsync(DeviceSide.Right, memoryIndex, ct);

                    var (left, right) = _session.GetSnapshotsForMemory(memoryIndex);
                    if (_session.LeftConnected && left != null && (!onlyDirty || leftDirty))
                        await SaveMemoryForSideToNvmAsync(product, conn, DeviceSide.Left, memoryIndex, left, ct);
                    if (_session.RightConnected && right != null && (!onlyDirty || rightDirty))
                        await SaveMemoryForSideToNvmAsync(product, conn, DeviceSide.Right, memoryIndex, right, ct);
                }

                RefreshSaveState();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveMemoryForSideAsync(
            SDLib.IProduct product,
            DeviceConnectionService conn,
            DeviceSide side,
            int memoryIndex,
            DeviceSettingsSnapshot snapshot,
            CancellationToken ct)
        {
            try
            {
                await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(_session, side, ct);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FittingVM] Save EnsureInit({side}) failed: {ex.Message}");
                ScanDiagnostics.WriteLine($"[FittingVM] Save EnsureInit({side}) failed: " + ex.Message);
                ErrorMessage = _session.LastConfigError ?? ex.Message;
                return;
            }

            var adaptor = conn.GetConnection(side);
            if (adaptor == null) return;

            System.Diagnostics.Debug.WriteLine($"[Save] SaveCurrent memory={memoryIndex + 1} side={side}");
            var ok = await _soundDesigner.WriteMemorySnapshotAsync(product, adaptor, snapshot, memoryIndex, null, ct);
            if (ok)
            {
                _session.ClearMemoryDirty(side, memoryIndex);
                _settingsCache.InvalidateCacheForSide(side);
                // Allow device time to flush to NVM before any follow-up read or disconnect.
                await Task.Delay(250, ct).ConfigureAwait(true);
            }
        }

        /// <summary>NVM-only: Burn snapshot to NVM, verify, clear dirty, set status. Logs [NVM] Burn M# and [NVM] Verify M#.</summary>
        private async Task SaveMemoryForSideToNvmAsync(
            SDLib.IProduct product,
            DeviceConnectionService conn,
            DeviceSide side,
            int memoryIndex,
            DeviceSettingsSnapshot snapshot,
            CancellationToken ct)
        {
            var appState = AppSessionState.Instance;
            var serialId = side == DeviceSide.Left ? appState.LeftSerialId : appState.RightSerialId;
            System.Diagnostics.Debug.WriteLine($"[WriteVerify] SAVE START: side={side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} serial={serialId ?? "?"}");
            ScanDiagnostics.WriteLine($"[WriteVerify] SAVE START: side={side} memoryIndex={memoryIndex} memoryLabel=M{memoryIndex + 1} serial={serialId ?? "?"}");

            try
            {
                await DeviceInitializationService.EnsureInitializedAndConfiguredAsync(_session, side, ct);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.RecordException("SaveEnsureInit", DiagnosticCategory.Device, ex, "Fitting", memoryIndex, side);
                System.Diagnostics.Debug.WriteLine($"[FittingVM] Save EnsureInit({side}) failed: {ex.Message}");
                ScanDiagnostics.WriteLine($"[FittingVM] Save EnsureInit({side}) failed: " + ex.Message);
                ErrorMessage = _session.LastConfigError ?? ex.Message;
                return;
            }

            var adaptor = conn.GetConnection(side);
            if (adaptor == null) return;

            string? failureReason = null;
            var ok = await _soundDesigner.BurnMemoryToNvmAsync(product, adaptor, snapshot, memoryIndex, null, ct, onWriteFailed: msg => failureReason = msg ?? failureReason);
            if (!ok)
            {
                var msg = failureReason ?? "Burn to NVM failed";
                DiagnosticService.Instance.RecordException("BurnToNvm", DiagnosticCategory.Device, new InvalidOperationException(msg), "Fitting", memoryIndex, side);
                if (!string.IsNullOrEmpty(failureReason)) ErrorMessage = failureReason;
                return;
            }

            var (verified, verifyMsg) = await _soundDesigner.VerifyMemoryMatchesNvmAsync(product, adaptor, snapshot, memoryIndex, maxItemsToCheck: 50, ct);
            if (!verified && !string.IsNullOrEmpty(verifyMsg))
            {
                DiagnosticService.Instance.RecordWarning("VerifyNvm", DiagnosticCategory.Device, verifyMsg, "Fitting", memoryIndex, side);
                ErrorMessage = verifyMsg;
                return;
            }

            _session.ClearMemoryDirty(side, memoryIndex);
            _settingsCache.InvalidateCacheForSide(side);
            _nvmSaveStatus = "Persisted to NVM (verified)";
            OnPropertyChanged(nameof(FittingStatusText));

            System.Diagnostics.Debug.WriteLine($"[WriteVerify] SAVE COMPLETE: side={side} memoryIndex={memoryIndex} serial={serialId ?? "?"} — write verified");
            ScanDiagnostics.WriteLine($"[WriteVerify] SAVE COMPLETE: side={side} memoryIndex={memoryIndex} serial={serialId ?? "?"} — write verified");

            // Allow device time to flush to NVM before any follow-up read or disconnect.
            await Task.Delay(250, ct).ConfigureAwait(true);
        }

        private void RefreshSaveState()
        {
            HasUnsavedChanges = _session.HasAnyDirtyMemory();
            OnPropertyChanged(nameof(IsCurrentMemoryDirty));
            OnPropertyChanged(nameof(HasAnyDirtyMemory));
            OnPropertyChanged(nameof(SaveToDeviceToolTip));
            OnPropertyChanged(nameof(SaveAllMemoriesToolTip));
            OnPropertyChanged(nameof(FittingStatusText));
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion

        #region Cached item collections (performance optimization)

        /// <summary>
        /// Builds the per-tab item caches from the current snapshots.
        /// Called once when a snapshot loads (not on every tab switch).
        /// SettingItemViewModel objects are created here and REUSED across tab switches.
        /// </summary>
        private void BuildItemCaches()
        {
            _leftItemCache.Clear();
            _rightItemCache.Clear();

            BuildCacheForSnapshot(LeftSnapshot, _leftItemCache);
            BuildCacheForSnapshot(RightSnapshot, _rightItemCache);

            System.Diagnostics.Debug.WriteLine(
                $"[FittingVM] Item cache built: {_leftItemCache.Count} left tabs, {_rightItemCache.Count} right tabs, " +
                $"{_leftItemCache.Values.Sum(v => v.Count)} left items total");
        }

        private static void BuildCacheForSnapshot(DeviceSettingsSnapshot? snapshot, Dictionary<string, List<SettingItemViewModel>> cache)
        {
            if (snapshot == null) return;
            foreach (var cat in snapshot.Categories)
            {
                var items = new List<SettingItemViewModel>();
                foreach (var sec in cat.Sections)
                    foreach (var item in sec.Items)
                        items.Add(new SettingItemViewModel(item));
                cache[cat.Id] = items;
                // Also index by Title for fuzzy tab matching
                if (!string.IsNullOrEmpty(cat.Title) && !cache.ContainsKey(cat.Title))
                    cache[cat.Title] = items;
            }
        }

        /// <summary>
        /// Updates FilteredLeftItems/FilteredRightItems from the cached per-tab items.
        /// Single assign on UI thread to avoid dispatcher storms.
        /// </summary>
        private void UpdateFilteredItems()
        {
            var searchLower = SearchText?.Trim().ToLowerInvariant() ?? "";
            var sw = Stopwatch.StartNew();
            var left = BuildFlatItemsFromCache(_leftItemCache, SelectedTabId, searchLower);
            var right = BuildFlatItemsFromCache(_rightItemCache, SelectedTabId, searchLower);
            FilteredLeftItems = left;
            FilteredRightItems = right;
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[Perf] RenderRows count={left.Count + right.Count} ms={sw.ElapsedMilliseconds}");
        }

        private static ObservableCollection<SettingItemViewModel> BuildFlatItemsFromCache(
            Dictionary<string, List<SettingItemViewModel>> cache, string tabId, string search)
        {
            var result = new ObservableCollection<SettingItemViewModel>();
            if (string.IsNullOrEmpty(tabId)) return result;

            // Try exact match first, then fallback to first tab
            if (!cache.TryGetValue(tabId, out var items))
            {
                // Fallback: if only one tab exists, show it regardless
                if (cache.Count > 0)
                    items = cache.Values.First();
                else
                    return result;
            }

            foreach (var item in items)
            {
                if (search.Length > 0 && !MatchesSearch(item, search)) continue;
                result.Add(item);
            }
            return result;
        }

        private static bool MatchesSearch(SettingItemViewModel item, string searchLower)
        {
            return (item.Name?.ToLowerInvariant().Contains(searchLower) == true) ||
                   (item.DisplayName?.ToLowerInvariant().Contains(searchLower) == true) ||
                   (item.Description?.ToLowerInvariant().Contains(searchLower) == true) ||
                   (item.Id?.ToLowerInvariant().Contains(searchLower) == true);
        }

        #endregion

        #region Error handling + logging

        private static string GetUserFriendlyMessage(Exception ex)
        {
            var msg = ex?.Message ?? "Unknown error";
            if (ex != null && ex.GetType().Name.IndexOf("SDException", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (msg.IndexOf("E_SEND_FAILURE", StringComparison.OrdinalIgnoreCase) >= 0) return "Communication error. Check cable and contacts.";
                if (msg.IndexOf("E_INVALID_STATE", StringComparison.OrdinalIgnoreCase) >= 0) return "Device busy. Close other fitting software and try again.";
                if (msg.IndexOf("E_NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0) return "Device not found. Reconnect and try again.";
            }
            return msg;
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class FittingTabItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        public string Id { get => _id; set { _id = value ?? ""; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? ""; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class SettingCategoryViewModel : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        public string Id { get => _id; set { _id = value ?? ""; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? ""; OnPropertyChanged(); } }
        public ObservableCollection<SettingSectionViewModel> Sections { get; } = new ObservableCollection<SettingSectionViewModel>();
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class SettingSectionViewModel : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        public string Id { get => _id; set { _id = value ?? ""; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? ""; OnPropertyChanged(); } }
        public ObservableCollection<SettingItemViewModel> Items { get; } = new ObservableCollection<SettingItemViewModel>();
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class SettingItemViewModel : INotifyPropertyChanged
    {
        private readonly SettingItem _model;
        public SettingItemViewModel(SettingItem model)
        {
            _model = model ?? new SettingItem();
            _model.PropertyChanged += (_, e) =>
            {
                OnPropertyChanged(e.PropertyName);
                // Also notify computed properties that depend on Value
                if (e.PropertyName == nameof(SettingItem.Value))
                {
                    OnPropertyChanged(nameof(ValueString));
                    OnPropertyChanged(nameof(ValueWithUnit));
                    OnPropertyChanged(nameof(FormattedValue));
                    OnPropertyChanged(nameof(SelectedEnumIndex));
                    OnPropertyChanged(nameof(BoolLabel));
                }
            };
        }

        // Core properties
        public string Id => _model.Id;
        public string Name => _model.Name;
        public string DisplayName => _model.DisplayName;
        public string Description => _model.Description;
        public string ModuleName => _model.ModuleName;
        public object? Value { get => _model.Value; set => _model.Value = value; }
        public string ValueString => _model.ValueString;
        public string Unit => _model.Unit;
        public double Min => _model.Min;
        public double Max => _model.Max;
        public double Step => _model.Step;
        public SettingItem.DataType SettingDataType => _model.SettingDataType;
        public bool ReadOnly => _model.ReadOnly;
        public string[] EnumValues => _model.EnumValues;
        public SettingItem Model => _model;

        // UI helper properties for professional rendering
        /// <summary>True when Step is a valid positive number (enables slider snapping).</summary>
        public bool HasStep => !double.IsNaN(_model.Step) && _model.Step > 0;
        /// <summary>True when Min and Max define a valid range (enables slider display).</summary>
        public bool HasRange => !double.IsNaN(_model.Min) && !double.IsNaN(_model.Max) && _model.Max > _model.Min;
        /// <summary>True when Description is available (shows subtitle row).</summary>
        public bool HasDescription => !string.IsNullOrWhiteSpace(_model.Description);
        /// <summary>Min label for slider (e.g. "-20").</summary>
        public string MinLabel => HasRange ? _model.Min.ToString("G5") : "";
        /// <summary>Max label for slider (e.g. "40").</summary>
        public string MaxLabel => HasRange ? _model.Max.ToString("G5") : "";
        /// <summary>Value with unit for display (e.g. "12.5 dB").</summary>
        public string ValueWithUnit => string.IsNullOrEmpty(_model.Unit) ? _model.ValueString : $"{_model.ValueString} {_model.Unit}";
        /// <summary>Formatted numeric value: rounds doubles for clean display.</summary>
        public string FormattedValue
        {
            get
            {
                if (_model.Value is double d) return HasStep ? d.ToString($"F{StepDecimals}") : d.ToString("G6");
                if (_model.Value is float f) return f.ToString("G6");
                return _model.ValueString;
            }
        }
        /// <summary>Safe TickFrequency: returns Step if valid, 0 otherwise (prevents NaN in slider).</summary>
        public double SafeTickFrequency => HasStep ? _model.Step : 0;
        /// <summary>For Bool parameters: "On" or "Off" for display next to checkbox.</summary>
        public string BoolLabel => _model.Value is true ? "On" : "Off";
        /// <summary>For Enum parameters: "N options" or comma-separated list (first 5) for options hint.</summary>
        public string EnumValuesCountLabel
        {
            get
            {
                if (_model.EnumValues == null || _model.EnumValues.Length == 0) return "—";
                if (_model.EnumValues.Length <= 5)
                    return string.Join(", ", _model.EnumValues);
                return $"{_model.EnumValues.Length} options";
            }
        }

        /// <summary>
        /// For Enum parameters: selected index in EnumValues array.
        /// Handles both integer-indexed (IndexedList) and string-valued (TextList) SDK parameters.
        /// The ComboBox binds to SelectedIndex via this property (not SelectedItem)
        /// because SDK Value is often int but EnumValues are string[].
        /// </summary>
        public int SelectedEnumIndex
        {
            get
            {
                if (_model.EnumValues.Length == 0 || _model.Value == null) return -1;
                // Try string match first (TextList parameters)
                var valStr = _model.Value.ToString() ?? "";
                var idx = Array.IndexOf(_model.EnumValues, valStr);
                if (idx >= 0) return idx;
                // Try as integer index (IndexedList parameters: Value = 0,1,2...)
                if (int.TryParse(valStr, out var intIdx) && intIdx >= 0 && intIdx < _model.EnumValues.Length)
                    return intIdx;
                // Try double → int (SDK sometimes returns double for integer values)
                if (_model.Value is double d)
                {
                    var fromD = (int)d;
                    if (fromD >= 0 && fromD < _model.EnumValues.Length) return fromD;
                }
                return -1;
            }
            set
            {
                if (value >= 0 && value < _model.EnumValues.Length)
                    _model.Value = value; // Store index (SDK expects int for IndexedList)
            }
        }
        /// <summary>Number of decimal places implied by Step (for display formatting).</summary>
        private int StepDecimals
        {
            get
            {
                if (!HasStep) return 1;
                var s = _model.Step.ToString("G");
                var dot = s.IndexOf('.');
                return dot < 0 ? 0 : s.Length - dot - 1;
            }
        }

        /// <summary>True when this parameter's value has changed since last save (UI only; can be wired to snapshot later).</summary>
        public bool IsDirty => false;

        /// <summary>Command to reset this parameter to default / last synced value. Null when reset is not available.</summary>
        public ICommand? ResetCommand => null;

        /// <summary>True when ResetCommand is available (for visibility binding).</summary>
        public bool HasResetCommand => ResetCommand != null;

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged(string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
