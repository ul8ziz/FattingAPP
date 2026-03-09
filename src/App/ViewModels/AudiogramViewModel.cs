using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Services.Audiogram;
using Ul8ziz.FittingApp.App.Views;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>
    /// ViewModel for the Audiogram screen.
    ///
    /// Key design rules:
    /// - Memory selection is synchronized with DeviceSessionService (single source of truth).
    /// - Audiogram clinical data (L/R ear AC/BC/UCL) is stored PER MEMORY in _audioSessionsByMemory.
    /// - Switching memory saves the current clinical data then loads the saved data for the new memory.
    /// - Generate WDRC targets ONLY physically connected device(s) — disconnected sides are ignored.
    /// - IsAudiogramEnabled gates all input/output when no device is connected.
    /// - Post-connection: if audiogram data already exists for the active memory, it is populated automatically.
    ///
    /// sounddesigner_programmers_guide.pdf §6.6 — prescription and WDRC mapping is app-layer;
    /// SDK only exposes raw WDRC parameters via ParameterSpace.kNvmMemoryN (N = memoryIndex + 1).
    /// </summary>
    public sealed class AudiogramViewModel : INotifyPropertyChanged
    {
        // ── Services ────────────────────────────────────────────────────────────
        private readonly FittingSessionManager _sessionMgr;
        private readonly DeviceSessionService _session;
        private readonly AppSessionState _appSession;
        private readonly IAudiogramValidationService _validationService;
        private readonly IAudiogramPersistenceService _persistenceService;
        private readonly IPrescriptionEngine _prescriptionEngine;
        private readonly IParameterMappingService _parameterMappingService;
        private readonly IAudiogramIntegrationService _integrationService;

        // ── Navigation callback (injected by MainView) ─────────────────────────
        private readonly Action<string>? _requestNavigate;

        // ── Per-memory audiogram storage ───────────────────────────────────────
        // Key = memory index (0-based). One AudiogramSession per memory.
        // Populated when user enters clinical data and switches memory (autosave on switch).
        private readonly Dictionary<int, AudiogramSession> _audioSessionsByMemory = new();

        // ── Currently active audiogram session (mirrors _audioSessionsByMemory[_selectedMemoryIndex]) ──
        private AudiogramSession? _audiogramSession;

        // ── UI state ────────────────────────────────────────────────────────────
        private string _statusMessage = string.Empty;
        private string _validationMessage = string.Empty;
        private bool _isGeneratingWdrc;
        private bool _prevConnectedLeft;
        private bool _prevConnectedRight;

        // ── Mode selection ─────────────────────────────────────────────────────
        private AudiogramInputMode _activeInputMode = AudiogramInputMode.AC;

        // ── Memory selector ────────────────────────────────────────────────────
        private int _selectedMemoryIndex;

        // ── Chart point collections ────────────────────────────────────────────
        private readonly ObservableCollection<AudiogramChartPoint> _rightEarChartPoints = new();
        private readonly ObservableCollection<AudiogramChartPoint> _leftEarChartPoints  = new();

        // ── Numeric table rows ─────────────────────────────────────────────────
        private readonly ObservableCollection<AudiogramTypeRowViewModel> _rightEarRows = new();
        private readonly ObservableCollection<AudiogramTypeRowViewModel> _leftEarRows  = new();

        private static readonly int[] StandardFrequencies = { 250, 500, 1000, 2000, 3000, 4000, 6000, 8000 };
        private static readonly string[] MemoryNames =
            { "Memory 1", "Memory 2", "Memory 3", "Memory 4",
              "Memory 5", "Memory 6", "Memory 7", "Memory 8" };

        private static readonly JsonSerializerOptions _jsonOpts =
            new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        /// <summary>Fixed app-data path that persists audiogram data across app restarts.</summary>
        private static string GetAppDataAudiogramPath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Ul8ziz", "FittingApp", "audiogram_sessions.json");

        // ── Constructor ────────────────────────────────────────────────────────
        public AudiogramViewModel(Action<string>? requestNavigate = null)
        {
            _requestNavigate = requestNavigate;

            var mappingService = new GraphParameterMappingService();
            _sessionMgr          = FittingSessionManager.Instance;
            _session             = DeviceSessionService.Instance;
            _appSession          = AppSessionState.Instance;
            _validationService   = new AudiogramValidationService();
            _persistenceService  = new AudiogramPersistenceService();
            _prescriptionEngine  = new PrescriptionEngineStub();
            _parameterMappingService = new E7111V2ParameterMappingService(mappingService);
            _integrationService  = new AudiogramIntegrationService();

            // Capture initial connection state for edge-detection
            _prevConnectedLeft  = _appSession.ConnectedLeft;
            _prevConnectedRight = _appSession.ConnectedRight;

            // Mirror the shared selected memory index
            _selectedMemoryIndex = _session.SelectedMemoryIndex;

            // Synchronize memory selection when FittingViewModel (or anything else) changes it
            _session.PropertyChanged += OnSessionPropertyChanged;

            // Update per-ear visibility and connection gate when connection state changes
            _appSession.PropertyChanged += OnAppSessionPropertyChanged;

            // Build empty table rows for both ears
            RebuildTableRows();

            // Restore persisted audiogram data from previous app run (synchronous, tiny JSON file)
            LoadFromAppData();

            // PTA display updates whenever chart points change
            _rightEarChartPoints.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RightPtaText));
            _leftEarChartPoints.CollectionChanged  += (_, _) => OnPropertyChanged(nameof(LeftPtaText));

            // Commands
            SaveAudiogramCommand   = new RelayCommand(_ => SaveAudiogram(),     _ => !_isGeneratingWdrc);
            GenerateWdrcCommand    = new RelayCommand(_ => GenerateWdrc(),      _ => !_isGeneratingWdrc && IsAudiogramEnabled);
            OpenFittingCommand     = new RelayCommand(_ => OpenFitting(),        _ => true);
            ClearRightCommand      = new RelayCommand(_ => ClearEar(DeviceSide.Right), _ => true);
            ClearLeftCommand       = new RelayCommand(_ => ClearEar(DeviceSide.Left),  _ => true);
            CopyRightToLeftCommand = new RelayCommand(_ => CopyEar(DeviceSide.Right, DeviceSide.Left), _ => true);
            CopyLeftToRightCommand = new RelayCommand(_ => CopyEar(DeviceSide.Left,  DeviceSide.Right), _ => true);
            SetInputModeCommand    = new RelayCommand(p => SetInputMode(p), _ => true);
            RightChartClickCommand = new RelayCommand(p => OnChartClick(DeviceSide.Right, p), _ => true);
            LeftChartClickCommand  = new RelayCommand(p => OnChartClick(DeviceSide.Left,  p), _ => true);
            LoadAudiogramCommand   = new RelayCommand(_ => LoadAudiogram(), _ => true);

            // If a device is already connected when this VM is created (user navigated back to
            // Audiogram while connected), populate immediately — the edge-detection in
            // OnAppSessionPropertyChanged only fires on *changes*, not the initial state.
            if (_appSession.ConnectedLeft || _appSession.ConnectedRight)
                LoadAudiogramForMemory(_selectedMemoryIndex);
        }

        // ── Public properties ──────────────────────────────────────────────────

        public IReadOnlyList<string> MemoryDisplayNames => MemoryNames;

        /// <summary>
        /// Mirrors DeviceSessionService.SelectedMemoryIndex (0-based).
        /// "Memory 1" in UI = index 0 = SDK kNvmMemory1.
        /// Setting this:
        ///   1. Saves current clinical data to _audioSessionsByMemory[old index].
        ///   2. Updates DeviceSessionService (shared source of truth → notifies FittingViewModel).
        ///   3. Loads saved clinical data for the new memory (if any).
        /// </summary>
        public int SelectedMemoryIndex
        {
            get => _selectedMemoryIndex;
            set
            {
                if (_selectedMemoryIndex == value) return;

                // Save current audiogram data for the old memory before switching
                SaveCurrentMemoryAudiogram();

                int oldIndex = _selectedMemoryIndex;
                _selectedMemoryIndex = value;

                // Propagate to shared service so FittingViewModel switches too
                _session.SetSelectedMemoryIndex(value);

                OnPropertyChanged();

                // Load audiogram data for the new memory
                LoadAudiogramForMemory(value);

                Debug.WriteLine(
                    $"[Audiogram] Memory switched: UI='{MemoryNames[value]}' " +
                    $"InternalIndex={value} (was {oldIndex}) " +
                    $"SDKSpace=kNvmMemory{value + 1} " +
                    $"ConnectedLeft={_appSession.ConnectedLeft} ConnectedRight={_appSession.ConnectedRight}");
            }
        }

        public AudiogramInputMode ActiveInputMode
        {
            get => _activeInputMode;
            set { _activeInputMode = value; OnPropertyChanged(); NotifyModeFlags(); }
        }

        // Convenience booleans for toggle-button IsChecked bindings
        public bool ModeIsAC         => _activeInputMode == AudiogramInputMode.AC;
        public bool ModeIsBC         => _activeInputMode == AudiogramInputMode.BC;
        public bool ModeIsUCL        => _activeInputMode == AudiogramInputMode.UCL;
        public bool ModeIsMasked     => _activeInputMode == AudiogramInputMode.Masked;
        public bool ModeIsNoResponse => _activeInputMode == AudiogramInputMode.NoResponse;
        public bool ModeIsPTA        => _activeInputMode == AudiogramInputMode.PTA;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set { _validationMessage = value ?? string.Empty; OnPropertyChanged(); }
        }

        public bool IsGeneratingWdrc
        {
            get => _isGeneratingWdrc;
            private set { _isGeneratingWdrc = value; OnPropertyChanged(); }
        }

        // ── Connection state properties ────────────────────────────────────────

        /// <summary>
        /// True when at least one hearing aid is physically connected.
        /// Gates Generate WDRC and is exposed to XAML for disabling the entire action bar.
        /// </summary>
        public bool IsAudiogramEnabled => _appSession.ConnectedLeft || _appSession.ConnectedRight;

        /// <summary>True when the right hearing aid is connected. Drives right-ear panel visibility.</summary>
        public bool IsRightEarEnabled => _appSession.ConnectedRight;

        /// <summary>True when the left hearing aid is connected. Drives left-ear panel visibility.</summary>
        public bool IsLeftEarEnabled => _appSession.ConnectedLeft;

        /// <summary>True when neither ear is connected — shows the "connect a device" banner.</summary>
        public bool ShowNoConnectionMessage => !_appSession.ConnectedLeft && !_appSession.ConnectedRight;

        // ── Pure Tone Average display (500 / 1000 / 2000 Hz AC) ──────────────
        // Shows the 3-frequency PTA for each ear. Updates live as the user enters thresholds.
        // "N/A" when fewer than all 3 standard PTA frequencies have AC thresholds entered.

        public string RightPtaText => ComputePta(DeviceSide.Right);
        public string LeftPtaText  => ComputePta(DeviceSide.Left);

        private string ComputePta(DeviceSide side)
        {
            var chartPts   = side == DeviceSide.Right ? _rightEarChartPoints : _leftEarChartPoints;
            var ptaFreqs   = new[] { 500, 1000, 2000 };
            var values     = new List<int>(3);
            foreach (int hz in ptaFreqs)
            {
                var pt = chartPts.FirstOrDefault(
                    p => Math.Abs(p.FrequencyHz - hz) < 0.1
                      && p.PointType == AudiogramPointType.AC
                      && !p.IsNoResponse);
                if (pt == null) return "N/A";
                values.Add(pt.DbHL);
            }
            return $"{(int)Math.Round(values.Average())} dB";
        }

        // Chart point collections (bound to AudiogramChartControl.Points)
        public ObservableCollection<AudiogramChartPoint> RightEarChartPoints => _rightEarChartPoints;
        public ObservableCollection<AudiogramChartPoint> LeftEarChartPoints  => _leftEarChartPoints;

        // Table row collections (bound to DataGrid in numeric view)
        public ObservableCollection<AudiogramTypeRowViewModel> RightEarRows => _rightEarRows;
        public ObservableCollection<AudiogramTypeRowViewModel> LeftEarRows  => _leftEarRows;

        // ── Commands ────────────────────────────────────────────────────────────
        public ICommand SaveAudiogramCommand      { get; }
        public ICommand GenerateWdrcCommand       { get; }
        public ICommand OpenFittingCommand        { get; }
        public ICommand ClearRightCommand         { get; }
        public ICommand ClearLeftCommand          { get; }
        public ICommand CopyRightToLeftCommand    { get; }
        public ICommand CopyLeftToRightCommand    { get; }
        public ICommand SetInputModeCommand       { get; }
        public ICommand RightChartClickCommand    { get; }
        public ICommand LeftChartClickCommand     { get; }
        public ICommand LoadAudiogramCommand      { get; }

        // ── Memory synchronization (from DeviceSessionService) ─────────────────
        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DeviceSessionService.SelectedMemoryIndex)) return;
            int incoming = _session.SelectedMemoryIndex;
            if (_selectedMemoryIndex == incoming) return;

            // Save current before switching (same as SelectedMemoryIndex setter but without re-calling SetSelectedMemoryIndex)
            SaveCurrentMemoryAudiogram();

            int oldIndex = _selectedMemoryIndex;
            _selectedMemoryIndex = incoming;
            OnPropertyChanged(nameof(SelectedMemoryIndex));

            LoadAudiogramForMemory(incoming);

            Debug.WriteLine(
                $"[Audiogram] Memory synced from session: UI='{MemoryNames[incoming]}' " +
                $"InternalIndex={incoming} (was {oldIndex}) " +
                $"SDKSpace=kNvmMemory{incoming + 1}");
        }

        // ── Connection state synchronization ───────────────────────────────────
        private void OnAppSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppSessionState.ConnectedRight)
                               or nameof(AppSessionState.ConnectedLeft)
                               or nameof(AppSessionState.ConnectionState))
            {
                bool nowLeft  = _appSession.ConnectedLeft;
                bool nowRight = _appSession.ConnectedRight;

                OnPropertyChanged(nameof(IsRightEarEnabled));
                OnPropertyChanged(nameof(IsLeftEarEnabled));
                OnPropertyChanged(nameof(ShowNoConnectionMessage));
                OnPropertyChanged(nameof(IsAudiogramEnabled));

                // Edge: device(s) newly connected → load audiogram for current memory
                bool newConnectionDetected =
                    (nowLeft  && !_prevConnectedLeft) ||
                    (nowRight && !_prevConnectedRight);

                if (newConnectionDetected)
                {
                    int memIdx = _session.SelectedMemoryIndex;
                    Debug.WriteLine(
                        $"[Audiogram] Device connected: Left={nowLeft} Right={nowRight} " +
                        $"ActiveMemory='{MemoryNames[memIdx]}' InternalIndex={memIdx} " +
                        $"SDKSpace=kNvmMemory{memIdx + 1}");

                    LoadAudiogramForMemory(memIdx);
                }

                // Edge: device(s) disconnected → report stale context cleared
                bool disconnected =
                    (!nowLeft  && _prevConnectedLeft) ||
                    (!nowRight && _prevConnectedRight);

                if (disconnected)
                {
                    Debug.WriteLine(
                        $"[Audiogram] Device disconnected: Left={nowLeft} Right={nowRight} — " +
                        "stale device context cleared; audiogram data retained in memory store.");

                    if (!nowLeft && !nowRight)
                        StatusMessage = "Device disconnected. Connect a hearing aid to re-enable Audiogram.";
                }

                _prevConnectedLeft  = nowLeft;
                _prevConnectedRight = nowRight;
            }
        }

        // ── Mode selection ─────────────────────────────────────────────────────
        private void SetInputMode(object? parameter)
        {
            if (parameter is string s && Enum.TryParse<AudiogramInputMode>(s, out var mode))
                ActiveInputMode = mode;
            else if (parameter is AudiogramInputMode m)
                ActiveInputMode = m;
        }

        private void NotifyModeFlags()
        {
            OnPropertyChanged(nameof(ModeIsAC));
            OnPropertyChanged(nameof(ModeIsBC));
            OnPropertyChanged(nameof(ModeIsUCL));
            OnPropertyChanged(nameof(ModeIsMasked));
            OnPropertyChanged(nameof(ModeIsNoResponse));
            OnPropertyChanged(nameof(ModeIsPTA));
        }

        // ── Chart click interaction ────────────────────────────────────────────
        private void OnChartClick(DeviceSide side, object? parameter)
        {
            if (parameter is not ValueTuple<double, int> coords) return;
            (double freqHz, int dbHL) = coords;

            var pointType = _activeInputMode switch
            {
                AudiogramInputMode.BC  => AudiogramPointType.BC,
                AudiogramInputMode.UCL => AudiogramPointType.UCL,
                _                      => AudiogramPointType.AC
            };

            bool isNoResponse = _activeInputMode == AudiogramInputMode.NoResponse;
            bool isMasked     = _activeInputMode == AudiogramInputMode.Masked;

            if (_activeInputMode == AudiogramInputMode.PTA) return;

            var chartCol = side == DeviceSide.Right ? _rightEarChartPoints : _leftEarChartPoints;
            var existing = chartCol.FirstOrDefault(p => Math.Abs(p.FrequencyHz - freqHz) < 0.1
                                                     && p.PointType == pointType);
            if (existing != null)
            {
                chartCol.Remove(existing);
            }
            else
            {
                chartCol.Add(new AudiogramChartPoint
                {
                    FrequencyHz  = freqHz,
                    DbHL         = dbHL,
                    PointType    = pointType,
                    IsNoResponse = isNoResponse,
                    IsMasked     = isMasked
                });
            }

            SyncChartToRows(side);
        }

        // ── Table ↔ Chart synchronization ─────────────────────────────────────
        private void SyncChartToRows(DeviceSide side)
        {
            var rows     = side == DeviceSide.Right ? _rightEarRows : _leftEarRows;
            var chartPts = side == DeviceSide.Right ? _rightEarChartPoints : _leftEarChartPoints;

            foreach (var row in rows)
            {
                for (int i = 0; i < StandardFrequencies.Length; i++)
                {
                    int hz = StandardFrequencies[i];
                    var match = chartPts.FirstOrDefault(
                        p => Math.Abs(p.FrequencyHz - hz) < 0.1 && p.PointType == row.PointType && !p.IsNoResponse);
                    row.SetValueForIndex(i, match != null ? match.DbHL.ToString() : null);
                }
            }
        }

        private void SyncRowsToChart(DeviceSide side)
        {
            var rows     = side == DeviceSide.Right ? _rightEarRows : _leftEarRows;
            var chartCol = side == DeviceSide.Right ? _rightEarChartPoints : _leftEarChartPoints;

            var toRemove = chartCol.Where(p => !p.IsNoResponse).ToList();
            foreach (var p in toRemove) chartCol.Remove(p);

            foreach (var row in rows)
            {
                for (int i = 0; i < StandardFrequencies.Length; i++)
                {
                    string? val = row.GetValueForIndex(i);
                    if (int.TryParse(val, out int db))
                    {
                        chartCol.Add(new AudiogramChartPoint
                        {
                            FrequencyHz = StandardFrequencies[i],
                            DbHL        = db,
                            PointType   = row.PointType
                        });
                    }
                }
            }
        }

        // ── App-data persistence (survives app restarts) ───────────────────────

        /// <summary>
        /// Synchronously reads the per-memory audiogram store from app-data on startup.
        /// The file is small (< 50 KB) so synchronous I/O is acceptable here.
        /// </summary>
        private void LoadFromAppData()
        {
            try
            {
                var path = GetAppDataAudiogramPath();
                if (!File.Exists(path)) return;

                using var fs = File.OpenRead(path);
                var dict = JsonSerializer.Deserialize<Dictionary<int, AudiogramSession>>(fs, _jsonOpts);
                if (dict == null) return;

                foreach (var kv in dict)
                    _audioSessionsByMemory[kv.Key] = kv.Value;

                Debug.WriteLine($"[Audiogram] Startup: restored {dict.Count} session(s) from app-data.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audiogram] LoadFromAppData failed (non-fatal): {ex.Message}");
            }
        }

        /// <summary>
        /// Fire-and-forget: writes all non-empty per-memory sessions to app-data so they
        /// survive app restarts. Called after every explicit "Save Audiogram" or file load.
        /// </summary>
        private async void AutoSaveToAppDataAsync()
        {
            try
            {
                var snapshot = _audioSessionsByMemory
                    .Where(kv => (kv.Value.RightEarAudiogram?.Points.Count ?? 0) > 0
                              || (kv.Value.LeftEarAudiogram?.Points.Count  ?? 0) > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                await _persistenceService.SaveSessionsAsync(snapshot, GetAppDataAudiogramPath())
                    .ConfigureAwait(false);

                Debug.WriteLine($"[Audiogram] AutoSave: wrote {snapshot.Count} session(s) to app-data.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audiogram] AutoSaveToAppData failed (non-fatal): {ex.Message}");
            }
        }

        // ── Per-memory audiogram save / load ───────────────────────────────────

        /// <summary>
        /// Saves the current UI state into _audioSessionsByMemory[_selectedMemoryIndex].
        /// Called automatically before any memory switch.
        /// </summary>
        private void SaveCurrentMemoryAudiogram()
        {
            var session = BuildSessionFromRows();
            if ((session.RightEarAudiogram?.Points.Count ?? 0) > 0 || (session.LeftEarAudiogram?.Points.Count ?? 0) > 0)
                _audioSessionsByMemory[_selectedMemoryIndex] = session;
        }

        /// <summary>
        /// Loads the saved audiogram for the given memory index into the UI.
        /// If no data exists for that memory, clears the UI and shows an informational message.
        /// </summary>
        private void LoadAudiogramForMemory(int memIdx)
        {
            if (_audioSessionsByMemory.TryGetValue(memIdx, out var savedSession))
            {
                _audiogramSession = savedSession;
                PopulateFromSession();
                Debug.WriteLine(
                    $"[Audiogram] Loaded from session store: Memory='{MemoryNames[memIdx]}' " +
                    $"Index={memIdx} " +
                    $"LeftPts={savedSession.LeftEarAudiogram?.Points.Count ?? 0} " +
                    $"RightPts={savedSession.RightEarAudiogram?.Points.Count ?? 0}");
                StatusMessage = $"Audiogram data loaded for {MemoryNames[memIdx]}.";
            }
            else
            {
                // No data for this memory — clear to clean clinical state
                _audiogramSession = null;
                _rightEarChartPoints.Clear();
                _leftEarChartPoints.Clear();
                RebuildTableRows();
                ValidationMessage = string.Empty;

                string connectedSide = _appSession.ConnectedLeft && _appSession.ConnectedRight ? "both ears"
                    : _appSession.ConnectedLeft ? "left ear"
                    : _appSession.ConnectedRight ? "right ear"
                    : "no device";
                StatusMessage = $"No audiogram data for {MemoryNames[memIdx]} ({connectedSide} active). Enter thresholds above.";

                Debug.WriteLine(
                    $"[Audiogram] No data for Memory='{MemoryNames[memIdx]}' Index={memIdx} — clean state shown.");
            }
        }

        // ── Session → UI population ────────────────────────────────────────────

        /// <summary>Called from AudiogramView.xaml.cs on Loaded and after LoadAudiogramForMemory.</summary>
        public void PopulateFromSession()
        {
            if (_audiogramSession == null)
            {
                // Nothing to show — ensure UI is clean
                _rightEarChartPoints.Clear();
                _leftEarChartPoints.Clear();
                RebuildTableRows();
                return;
            }

            _rightEarChartPoints.Clear();
            _leftEarChartPoints.Clear();
            RebuildTableRows();

            if (_audiogramSession.RightEarAudiogram != null)
                LoadEarFromSession(_audiogramSession.RightEarAudiogram, _rightEarRows, _rightEarChartPoints);
            if (_audiogramSession.LeftEarAudiogram != null)
                LoadEarFromSession(_audiogramSession.LeftEarAudiogram, _leftEarRows, _leftEarChartPoints);

            // PTA display does not auto-fire from CollectionChanged during bulk load; notify explicitly
            OnPropertyChanged(nameof(RightPtaText));
            OnPropertyChanged(nameof(LeftPtaText));
        }

        private static void LoadEarFromSession(
            EarAudiogram ear,
            ObservableCollection<AudiogramTypeRowViewModel> rows,
            ObservableCollection<AudiogramChartPoint> chart)
        {
            foreach (var pt in ear.Points)
            {
                chart.Add(new AudiogramChartPoint
                {
                    FrequencyHz = pt.FrequencyHz,
                    DbHL        = pt.ThresholdDbHL ?? 0,
                    PointType   = pt.PointType
                });

                var row = rows.FirstOrDefault(r => r.PointType == pt.PointType);
                if (row == null) continue;
                int idx = Array.IndexOf(StandardFrequencies, (int)Math.Round(pt.FrequencyHz));
                if (idx >= 0 && pt.ThresholdDbHL.HasValue)
                    row.SetValueForIndex(idx, pt.ThresholdDbHL.Value.ToString());
            }
        }

        // ── Clear / Copy ────────────────────────────────────────────────────────
        private void ClearEar(DeviceSide side)
        {
            if (side == DeviceSide.Right)
            {
                _rightEarChartPoints.Clear();
                foreach (var r in _rightEarRows) r.Clear();
            }
            else
            {
                _leftEarChartPoints.Clear();
                foreach (var r in _leftEarRows) r.Clear();
            }
            ValidationMessage = string.Empty;
        }

        private void CopyEar(DeviceSide from, DeviceSide to)
        {
            var srcPts  = from == DeviceSide.Right ? _rightEarChartPoints : _leftEarChartPoints;
            var dstPts  = to   == DeviceSide.Right ? _rightEarChartPoints : _leftEarChartPoints;
            var srcRows = from == DeviceSide.Right ? _rightEarRows : _leftEarRows;
            var dstRows = to   == DeviceSide.Right ? _rightEarRows : _leftEarRows;

            dstPts.Clear();
            foreach (var p in srcPts)
                dstPts.Add(new AudiogramChartPoint
                {
                    FrequencyHz  = p.FrequencyHz,
                    DbHL         = p.DbHL,
                    PointType    = p.PointType,
                    IsNoResponse = p.IsNoResponse,
                    IsMasked     = p.IsMasked
                });

            for (int ri = 0; ri < dstRows.Count && ri < srcRows.Count; ri++)
                for (int i = 0; i < StandardFrequencies.Length; i++)
                    dstRows[ri].SetValueForIndex(i, srcRows[ri].GetValueForIndex(i));

            StatusMessage = $"Copied {from} → {to}.";
        }

        // ── Build empty table rows ─────────────────────────────────────────────
        private void RebuildTableRows()
        {
            _rightEarRows.Clear();
            _rightEarRows.Add(new AudiogramTypeRowViewModel(AudiogramPointType.AC));
            _rightEarRows.Add(new AudiogramTypeRowViewModel(AudiogramPointType.BC));
            _rightEarRows.Add(new AudiogramTypeRowViewModel(AudiogramPointType.UCL));

            _leftEarRows.Clear();
            _leftEarRows.Add(new AudiogramTypeRowViewModel(AudiogramPointType.AC));
            _leftEarRows.Add(new AudiogramTypeRowViewModel(AudiogramPointType.BC));
            _leftEarRows.Add(new AudiogramTypeRowViewModel(AudiogramPointType.UCL));
        }

        // ── Generate WDRC ──────────────────────────────────────────────────────
        // sounddesigner_programmers_guide.pdf §6.6 — prescription and WDRC mapping is app-layer.
        // SDK exposes WDRC parameters via ParameterSpace.kNvmMemoryN where N = memoryIndex + 1.
        // Only connected devices are targeted; disconnected sides are not touched.
        private void GenerateWdrc()
        {
            try
            {
                IsGeneratingWdrc = true;
                ValidationMessage = string.Empty;
                StatusMessage = string.Empty;

                // ── Resolve connected device(s) ───────────────────────────────────
                bool connLeft  = _appSession.ConnectedLeft;
                bool connRight = _appSession.ConnectedRight;
                int  memIdx    = _selectedMemoryIndex;
                string memLabel = MemoryNames[memIdx];

                // Resolve which SDK ParameterSpace this maps to
                // sounddesigner_programmers_guide.pdf §5.3: kNvmMemory1 = index 0, kNvmMemory2 = index 1, etc.
                string sdkSpace = $"kNvmMemory{memIdx + 1}";

                Debug.WriteLine(
                    $"[Audiogram] GenerateWDRC START " +
                    $"UIMemory='{memLabel}' InternalIndex={memIdx} SDKSpace={sdkSpace} " +
                    $"ConnectedLeft={connLeft} ConnectedRight={connRight}");

                if (!connLeft && !connRight)
                {
                    StatusMessage = "No hearing aid connected. Connect a device to generate WDRC.";
                    Debug.WriteLine("[Audiogram] GenerateWDRC ABORTED — no connected device.");
                    return;
                }

                // ── Save current UI state into per-memory store ───────────────────
                SyncRowsToSession();

                // ── Validate only connected ear(s) ────────────────────────────────
                var validation = _validationService.Validate(_audiogramSession, connLeft, connRight);
                if (!validation.IsValid)
                {
                    ValidationMessage = "Cannot generate: " + string.Join("; ", validation.Errors);
                    StatusMessage = "Validation failed. Correct errors before generating.";
                    Debug.WriteLine("[Audiogram] GenerateWDRC ABORTED — validation failed: " + string.Join("; ", validation.Errors));
                    return;
                }
                if (validation.Warnings.Count > 0)
                    ValidationMessage = "Warnings: " + string.Join("; ", validation.Warnings);

                // ── Run prescription engine ───────────────────────────────────────
                // Stub returns flat 0 dB targets; replace PrescriptionEngineStub with NAL-NL2/DSL-v5 when implemented.
                var targets = _prescriptionEngine.ComputeTargets(_audiogramSession, null);

                // ── Resolve device snapshot for the selected memory ───────────────
                var libraryKey = _sessionMgr?.Library?.LoadedLibraryName
                                 ?? _sessionMgr?.ParamFileName
                                 ?? string.Empty;
                var (leftSnap, rightSnap) = _session.GetSnapshotsForMemory(memIdx);

                // Only use snapshots for sides that are physically connected
                var targetLeftSnap  = connLeft  ? leftSnap  : null;
                var targetRightSnap = connRight ? rightSnap : null;

                if (targetLeftSnap == null && targetRightSnap == null)
                {
                    StatusMessage =
                        $"No device snapshot available for {memLabel} (index {memIdx}). " +
                        "Load fitting parameters first on the Fitting screen.";
                    Debug.WriteLine(
                        $"[Audiogram] GenerateWDRC ABORTED — no snapshot for memIdx={memIdx} " +
                        $"leftSnap={leftSnap != null} rightSnap={rightSnap != null} " +
                        $"connLeft={connLeft} connRight={connRight}");
                    return;
                }

                // Log snapshot availability
                Debug.WriteLine(
                    $"[Audiogram] Snapshots for {memLabel}: " +
                    $"Left={(targetLeftSnap != null ? "available" : connLeft ? "MISSING" : "not connected")} " +
                    $"Right={(targetRightSnap != null ? "available" : connRight ? "MISSING" : "not connected")}");

                // ── Map prescription targets → WDRC parameter updates ─────────────
                // Use any available snapshot as parameter structure reference.
                var refSnap = targetLeftSnap ?? targetRightSnap!;
                var mappingResult = _parameterMappingService.MapTargetsToDevice(targets, libraryKey, refSnap);

                Debug.WriteLine(
                    $"[Audiogram] Mapping result: {mappingResult.ParameterUpdates.Count} parameter updates " +
                    $"for libraryKey='{libraryKey}'");

                if (mappingResult.ParameterUpdates.Count == 0)
                {
                    StatusMessage = $"No parameter mapping available for '{libraryKey}'. WDRC not updated. " +
                                    "Ensure GraphParameterMap.json is loaded.";
                    return;
                }

                // ── Apply to connected side(s) only ───────────────────────────────
                // Ezairo_7111_V2_firmware_bundle_user_reference.pdf §7.4 — WDRC channel gains.
                // AudiogramIntegrationService writes to snapshot cache + marks memory dirty.
                // No direct SDK write here — use Fitting screen → Save to NVM to commit.
                int appliedCount = 0;
                if (connLeft && targetLeftSnap != null)
                {
                    _integrationService.ApplyToFitting(mappingResult, DeviceSide.Left, memIdx);
                    appliedCount++;
                    Debug.WriteLine(
                        $"[Audiogram] Applied {mappingResult.ParameterUpdates.Count} updates to Left " +
                        $"memory={memLabel} (index={memIdx} SDKSpace={sdkSpace})");
                }
                if (connRight && targetRightSnap != null)
                {
                    _integrationService.ApplyToFitting(mappingResult, DeviceSide.Right, memIdx);
                    appliedCount++;
                    Debug.WriteLine(
                        $"[Audiogram] Applied {mappingResult.ParameterUpdates.Count} updates to Right " +
                        $"memory={memLabel} (index={memIdx} SDKSpace={sdkSpace})");
                }

                // ── Status report ─────────────────────────────────────────────────
                string sideLabel = (connLeft && connRight) ? "both ears"
                    : connLeft  ? "left ear"
                    : "right ear";
                StatusMessage =
                    $"WDRC parameters generated for {memLabel} ({sideLabel}). " +
                    $"Open Fitting to review and save to device.";

                Debug.WriteLine(
                    $"[Audiogram] GenerateWDRC COMPLETE " +
                    $"Memory='{memLabel}' Index={memIdx} SDKSpace={sdkSpace} " +
                    $"Sides={sideLabel} Updates={mappingResult.ParameterUpdates.Count}");
            }
            catch (Exception ex)
            {
                StatusMessage = "Generate WDRC failed: " + ex.Message;
                Debug.WriteLine($"[Audiogram] GenerateWDRC ERROR: {ex}");
            }
            finally
            {
                IsGeneratingWdrc = false;
            }
        }

        // ── Open Fitting ────────────────────────────────────────────────────────
        private void OpenFitting()
        {
            _requestNavigate?.Invoke("Fitting");
        }

        // ── Save / Load Audiogram ──────────────────────────────────────────────
        private async void SaveAudiogram()
        {
            SyncRowsToSession();
            if (_audiogramSession == null || (_audiogramSession.LeftEarAudiogram?.Points.Count == 0
                && _audiogramSession.RightEarAudiogram?.Points.Count == 0))
            {
                StatusMessage = "Nothing to save — enter audiogram data first.";
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Audiogram JSON (*.json)|*.json|All files (*.*)|*.*",
                Title  = "Save Audiogram"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                await _persistenceService.SaveAsync(_audiogramSession, dlg.FileName).ConfigureAwait(true);
                StatusMessage = $"Audiogram saved ({MemoryNames[_selectedMemoryIndex]}).";

                // Also persist to app-data so data survives app restarts
                AutoSaveToAppDataAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Save error: " + ex.Message;
            }
        }

        private async void LoadAudiogram()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Audiogram JSON (*.json)|*.json|All files (*.*)|*.*",
                Title  = "Load Audiogram"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var session = await _persistenceService.LoadAsync(dlg.FileName).ConfigureAwait(true);
                if (session != null)
                {
                    _audiogramSession = session;
                    _audioSessionsByMemory[_selectedMemoryIndex] = session;
                    PopulateFromSession();
                    ValidationMessage = string.Empty;
                    StatusMessage = $"Audiogram loaded into {MemoryNames[_selectedMemoryIndex]}.";
                    Debug.WriteLine(
                        $"[Audiogram] Loaded from file into Memory='{MemoryNames[_selectedMemoryIndex]}' " +
                        $"Index={_selectedMemoryIndex}");

                    // Persist to app-data so data survives app restarts
                    AutoSaveToAppDataAsync();
                }
                else
                {
                    StatusMessage = "Failed to load audiogram file.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Load error: " + ex.Message;
            }
        }

        // ── Sync rows → AudiogramSession (for validation / prescription / save) ─
        private void SyncRowsToSession()
        {
            var session = BuildSessionFromRows();
            _audiogramSession = session;
            _audioSessionsByMemory[_selectedMemoryIndex] = session;
        }

        private AudiogramSession BuildSessionFromRows()
        {
            var session = new AudiogramSession();
            session.RightEarAudiogram = BuildEarAudiogram(DeviceSide.Right, _rightEarRows);
            session.LeftEarAudiogram  = BuildEarAudiogram(DeviceSide.Left,  _leftEarRows);
            return session;
        }

        private static EarAudiogram BuildEarAudiogram(
            DeviceSide side, ObservableCollection<AudiogramTypeRowViewModel> rows)
        {
            var ear = new EarAudiogram { Side = side };
            foreach (var row in rows)
            {
                for (int i = 0; i < StandardFrequencies.Length; i++)
                {
                    if (int.TryParse(row.GetValueForIndex(i), out int val))
                    {
                        ear.Points.Add(new AudiogramPoint(
                            StandardFrequencies[i], val, null, row.PointType));
                    }
                }
            }
            return ear;
        }

        // ── INotifyPropertyChanged ─────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Cleanup when VM is no longer needed ───────────────────────────────
        ~AudiogramViewModel()
        {
            _session.PropertyChanged    -= OnSessionPropertyChanged;
            _appSession.PropertyChanged -= OnAppSessionPropertyChanged;
        }
    }

    /// <summary>Input mode for audiogram point entry.</summary>
    public enum AudiogramInputMode
    {
        AC,
        BC,
        UCL,
        Masked,
        NoResponse,
        PTA
    }
}
