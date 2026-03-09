using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Builds GraphSeries for audiogram threshold curve (frequency Hz vs dB HL) and optional target gain overlay.</summary>
    public sealed class AudiogramGraphService : IAudiogramGraphService
    {
        public IReadOnlyList<GraphSeries> BuildAudiogramSeries(EarAudiogram? ear)
        {
            if (ear?.Points == null || ear.Points.Count == 0)
                return Array.Empty<GraphSeries>();

            var thresholdPoints = ear.Points
                .Where(p => p.ThresholdDbHL.HasValue)
                .OrderBy(p => p.FrequencyHz)
                .Select(p => new GraphPoint { X = p.FrequencyHz, Y = p.ThresholdDbHL!.Value })
                .ToList();

            if (thresholdPoints.Count == 0)
                return Array.Empty<GraphSeries>();

            var series = new GraphSeries
            {
                Label = ear.Side.ToString() + " threshold",
                Points = thresholdPoints,
                Color = ear.Side == DeviceSide.Left ? Colors.DarkBlue : Colors.DarkRed
            };

            return new[] { series };
        }

        public IReadOnlyList<GraphSeries> BuildTargetGainSeries(PrescriptionTargets? targets)
        {
            if (targets?.GainsByFrequencyAndLevel == null || targets.GainsByFrequencyAndLevel.Count == 0)
                return Array.Empty<GraphSeries>();

            var seriesList = new List<GraphSeries>();
            var colors = new[] { Colors.DarkGreen, Colors.DarkOrange, Colors.Purple };
            var levels = new[] { PrescriptionInputLevel.Soft, PrescriptionInputLevel.Medium, PrescriptionInputLevel.Loud };
            int idx = 0;
            foreach (var level in levels)
            {
                var points = targets.GainsByFrequencyAndLevel
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new GraphPoint { X = kv.Key, Y = kv.Value.TryGetValue(level, out var g) ? g : 0 })
                    .ToList();
                if (points.Count > 0)
                {
                    seriesList.Add(new GraphSeries
                    {
                        Label = level.ToString(),
                        Points = points,
                        Color = idx < colors.Length ? colors[idx] : Colors.Gray
                    });
                    idx++;
                }
            }
            return seriesList;
        }
    }
}
