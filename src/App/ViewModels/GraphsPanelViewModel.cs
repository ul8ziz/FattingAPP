using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Ul8ziz.FittingApp.App.Helpers;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Views;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>ViewModel for the collapsible Graphs sidebar: Freq Gain, I/O, placeholders; debounced updates.</summary>
    public class GraphsPanelViewModel : INotifyPropertyChanged
    {
        private const int DebounceMs = 100;

        private readonly IGraphService _graphService;
        private readonly IGraphParameterMappingService _mappingService;
        private System.Timers.Timer? _debounceTimer;
        private DeviceSettingsSnapshot? _snapshot;
        private int _memoryIndex;
        private string? _libraryOrProductKey;
        private string _selectedOctave = "1/3 Octave";
        private string _freqGainPreset = "Freq. Gain";

        public GraphsPanelViewModel(
            IGraphService graphService,
            IGraphParameterMappingService mappingService)
        {
            _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));

            RefreshGraphsCommand = new RelayCommand(_ => RefreshGraphsImmediate(), _ => true);

            // Default curve levels (dB SPL)
            var levels = new ObservableCollection<CurveLevelItem>();
            foreach (var level in new[] { 40, 55, 70, 85, 100 })
                levels.Add(new CurveLevelItem(level, true));
            FreqGainCurveLevels = levels;

            SelectedFreqGainLevels = new HashSet<int>(levels.Where(l => l.IsSelected).Select(l => l.Level));

            OctaveOptions = new[] { "1/1 Octave", "1/2 Octave", "1/3 Octave" };
            IOFrequencies = new ObservableCollection<int>(new[] { 1000, 2500, 4000, 6500 });
            SelectedIOFrequencies = new HashSet<int>(IOFrequencies);

            FreqGainPlot = new PlotControlViewModel { XAxisLabel = "Hz", YAxisLabel = "dB", NoDataMessage = "Select levels and refresh." };
            IOPlot = new PlotControlViewModel { XAxisLabel = "dB SPL (Input)", YAxisLabel = "dB SPL (Output)", NoDataMessage = "Select frequencies and refresh." };

            foreach (var item in FreqGainCurveLevels)
                item.PropertyChanged += (_, _) => { UpdateSelectedFreqLevels(); DebouncedRefresh(); };
        }

        public ICommand RefreshGraphsCommand { get; }

        public ObservableCollection<CurveLevelItem> FreqGainCurveLevels { get; }
        public HashSet<int> SelectedFreqGainLevels { get; }
        public string[] OctaveOptions { get; }
        public string SelectedOctave { get => _selectedOctave; set { _selectedOctave = value ?? OctaveOptions[0]; OnPropertyChanged(); DebouncedRefresh(); } }
        public string FreqGainPreset { get => _freqGainPreset; set { _freqGainPreset = value ?? "Freq. Gain"; OnPropertyChanged(); } }

        public ObservableCollection<int> IOFrequencies { get; }
        public HashSet<int> SelectedIOFrequencies { get; }

        public PlotControlViewModel FreqGainPlot { get; }
        public PlotControlViewModel IOPlot { get; }

        public string? MappingNotConfiguredMessage => _graphService.GetMappingNotConfiguredMessage(_libraryOrProductKey);

        /// <summary>Called when snapshot, memory, or library key changes (e.g. from FittingViewModel).</summary>
        public void SetDataSource(DeviceSettingsSnapshot? snapshot, int memoryIndex, string? libraryOrProductKey)
        {
            _snapshot = snapshot;
            _memoryIndex = memoryIndex;
            _libraryOrProductKey = libraryOrProductKey;
            ParameterListExport.ExportOnceIfE7111V2(snapshot, libraryOrProductKey);
            OnPropertyChanged(nameof(MappingNotConfiguredMessage));
            DebouncedRefresh();
        }

        private void UpdateSelectedFreqLevels()
        {
            SelectedFreqGainLevels.Clear();
            foreach (var item in FreqGainCurveLevels.Where(l => l.IsSelected))
                SelectedFreqGainLevels.Add(item.Level);
        }

        private void DebouncedRefresh()
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Timers.Timer(DebounceMs) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                try
                {
                    var app = Application.Current;
                    if (app?.Dispatcher == null) return;
                    app.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            RefreshGraphsImmediate();
                        }
                        finally
                        {
                            _debounceTimer?.Dispose();
                            _debounceTimer = null;
                        }
                    });
                }
                catch (Exception)
                {
                    _debounceTimer?.Dispose();
                    _debounceTimer = null;
                }
            };
            _debounceTimer.Start();
        }

        public void RefreshGraphsImmediate()
        {
            Debug.WriteLine("[GraphsPanel] RefreshGraphsImmediate");
            UpdateSelectedFreqLevels();
            var levels = SelectedFreqGainLevels.ToList();
            var freqs = SelectedIOFrequencies.ToList();

            var freqSeries = _graphService.BuildFrequencyGainCurves(_snapshot, _libraryOrProductKey, _memoryIndex, levels, SelectedOctave);
            FreqGainPlot.SetSeries(freqSeries);
            if (freqSeries.Count == 0 && !string.IsNullOrEmpty(MappingNotConfiguredMessage))
                FreqGainPlot.NoDataMessage = MappingNotConfiguredMessage;
            else
                FreqGainPlot.NoDataMessage = "No data";

            var ioSeries = _graphService.BuildInputOutputCurves(_snapshot, _libraryOrProductKey, _memoryIndex, freqs);
            IOPlot.SetSeries(ioSeries);
            if (ioSeries.Count == 0 && !string.IsNullOrEmpty(MappingNotConfiguredMessage))
                IOPlot.NoDataMessage = MappingNotConfiguredMessage;
            else
                IOPlot.NoDataMessage = "No data";

            // Redraw plot controls (they bind to FreqGainPlot/IOPlot)
            OnPropertyChanged(nameof(FreqGainPlot));
            OnPropertyChanged(nameof(IOPlot));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One curve level row: level value (dB), selected, color chip.</summary>
    public class CurveLevelItem : INotifyPropertyChanged
    {
        private int _level;
        private bool _isSelected;

        public CurveLevelItem(int level, bool isSelected = true)
        {
            _level = level;
            _isSelected = isSelected;
        }

        public int Level { get => _level; set { _level = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
