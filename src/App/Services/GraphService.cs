using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ul8ziz.FittingApp.App.Helpers;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;
using System.Windows.Media;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Builds frequency gain and I/O curve series from snapshot using GraphParameterMap.</summary>
    public sealed class GraphService : IGraphService
    {
        private readonly IGraphParameterMappingService _mapping;
        private static readonly double[] ThirdOctaveHz = { 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12000 };
        private static readonly double[] HalfOctaveHz = { 125, 250, 500, 1000, 2000, 4000, 8000, 12000 };
        private static readonly double[] FullOctaveHz = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        public GraphService(IGraphParameterMappingService mapping)
        {
            _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        }

        public IReadOnlyList<GraphSeries> BuildFrequencyGainCurves(
            DeviceSettingsSnapshot? snapshot,
            string? libraryOrProductKey,
            int memoryIndex,
            IReadOnlyList<int> selectedLevels,
            string octaveMode)
        {
            if (snapshot == null || selectedLevels == null || selectedLevels.Count == 0)
                return Array.Empty<GraphSeries>();

            if (!_mapping.HasMappingFor(libraryOrProductKey))
            {
                Debug.WriteLine("[GraphService] Freq gain: no mapping for " + (libraryOrProductKey ?? "null"));
                return Array.Empty<GraphSeries>();
            }

            var levelToParamId = _mapping.GetFreqGainParameterIdsByLevel(libraryOrProductKey);
            if (levelToParamId.Count == 0)
                return Array.Empty<GraphSeries>();

            var valuesById = SnapshotParameterAccess.GetParameterValuesById(snapshot);
            var freqPoints = GetFreqPointsForOctave(octaveMode);
            var seriesList = new List<GraphSeries>();
            var colors = new[] { Colors.DarkBlue, Colors.DarkGreen, Colors.DarkOrange, Colors.Purple, Colors.DarkRed };

            int colorIndex = 0;
            foreach (var level in selectedLevels)
            {
                if (!levelToParamId.TryGetValue(level, out var paramId))
                    continue;
                if (!valuesById.TryGetValue(paramId, out var raw) || raw == null)
                    continue;

                // If mapping gives one gain value per band, we need multiple param IDs (centerFreqParamIds + one gain array per level).
                // Simplified: assume one param holds array or we have one gain per frequency from multiple params.
                // For a minimal implementation: use centerFreqParamIds to get X, and gainParamIdByLevel to get one gain value per level - so we need gain per frequency.
                // Without product-specific mapping we generate placeholder curve: flat at 0 dB or from a single gain value.
                var gainValue = ToDouble(raw);
                var points = freqPoints.Select(f => new GraphPoint { X = f, Y = gainValue }).ToList();
                var color = colorIndex < colors.Length ? colors[colorIndex] : Colors.Gray;
                seriesList.Add(new GraphSeries { Label = $"{level} dB", Points = points, Color = color });
                colorIndex++;
            }

            Debug.WriteLine($"[GraphService] Freq gain: built {seriesList.Count} curves for levels [{string.Join(", ", selectedLevels)}]");
            return seriesList;
        }

        public IReadOnlyList<GraphSeries> BuildInputOutputCurves(
            DeviceSettingsSnapshot? snapshot,
            string? libraryOrProductKey,
            int memoryIndex,
            IReadOnlyList<int> selectedFrequenciesHz)
        {
            if (snapshot == null || selectedFrequenciesHz == null || selectedFrequenciesHz.Count == 0)
                return Array.Empty<GraphSeries>();

            if (!_mapping.HasMappingFor(libraryOrProductKey))
            {
                Debug.WriteLine("[GraphService] I/O: no mapping for " + (libraryOrProductKey ?? "null"));
                return Array.Empty<GraphSeries>();
            }

            var paramIdsByHz = _mapping.GetInputOutputParamIdsByFrequencyHz(libraryOrProductKey);
            if (paramIdsByHz.Count == 0)
                return Array.Empty<GraphSeries>();

            var valuesById = SnapshotParameterAccess.GetParameterValuesById(snapshot);
            var seriesList = new List<GraphSeries>();
            var colors = new[] { Colors.DarkBlue, Colors.DarkGreen, Colors.DarkOrange, Colors.Purple };

            int colorIndex = 0;
            foreach (var hz in selectedFrequenciesHz)
            {
                if (!paramIdsByHz.TryGetValue(hz, out var paramIds) || paramIds == null || paramIds.Count == 0)
                    continue;

                // Placeholder: linear I/O 1:1 from 20 to 110 dB SPL
                var points = new List<GraphPoint>();
                for (int spl = 20; spl <= 110; spl += 5)
                    points.Add(new GraphPoint { X = spl, Y = spl });
                var color = colorIndex < colors.Length ? colors[colorIndex] : Colors.Gray;
                seriesList.Add(new GraphSeries { Label = $"{hz} Hz", Points = points, Color = color });
                colorIndex++;
            }

            Debug.WriteLine($"[GraphService] I/O: built {seriesList.Count} curves for frequencies [{string.Join(", ", selectedFrequenciesHz)}] Hz");
            return seriesList;
        }

        public string? GetMappingNotConfiguredMessage(string? libraryOrProductKey)
        {
            if (_mapping.HasMappingFor(libraryOrProductKey)) return null;
            return "Graph mapping not configured for this product.";
        }

        private static double[] GetFreqPointsForOctave(string octaveMode)
        {
            return octaveMode switch
            {
                "1/3" or "1/3 Octave" => ThirdOctaveHz,
                "1/2" or "1/2 Octave" => HalfOctaveHz,
                _ => FullOctaveHz
            };
        }

        private static double ToDouble(object? value)
        {
            if (value == null) return 0;
            if (value is double d) return d;
            if (value is int i) return i;
            if (value is float f) return f;
            return double.TryParse(value.ToString(), out var v) ? v : 0;
        }
    }
}
