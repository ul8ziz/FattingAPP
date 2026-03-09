using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Helpers;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>
    /// Maps prescription targets to E7111V2 WDRC parameters using GraphParameterMap.
    /// Soft -> low-level gain params; medium/loud -> high-level gain params.
    /// Per Sound Designer SDK / E7111V2 parameter naming (GraphParameterMap.json).
    /// </summary>
    public sealed class E7111V2ParameterMappingService : IParameterMappingService
    {
        private readonly IGraphParameterMappingService _graphMapping;

        public E7111V2ParameterMappingService(IGraphParameterMappingService graphMapping)
        {
            _graphMapping = graphMapping ?? throw new ArgumentNullException(nameof(graphMapping));
        }

        public DeviceMappingResult MapTargetsToDevice(
            PrescriptionTargets targets,
            string? libraryOrProductKey,
            DeviceSettingsSnapshot? currentSnapshot)
        {
            var result = new DeviceMappingResult();
            if (targets?.GainsByFrequencyAndLevel == null)
                return result;

            if (!_graphMapping.HasMappingFor(libraryOrProductKey))
            {
                Debug.WriteLine("[E7111V2Mapping] No graph mapping for " + (libraryOrProductKey ?? "null") + "; skipping.");
                return result;
            }

            var levelToParamId = _graphMapping.GetFreqGainParameterIdsByLevel(libraryOrProductKey);
            if (levelToParamId.Count == 0)
                return result;

            var valuesById = currentSnapshot != null ? SnapshotParameterAccess.GetParameterValuesById(currentSnapshot) : new Dictionary<string, object?>();

            // Graph levels 40, 55 (soft) -> low-level gain; 70, 85, 100 (medium/loud) -> high-level gain
            var graphLevels = new[] { 40, 55, 70, 85, 100 };
            foreach (var level in graphLevels)
            {
                if (!levelToParamId.TryGetValue(level, out var paramId) || string.IsNullOrEmpty(paramId))
                    continue;

                PrescriptionInputLevel prescLevel = level <= 55 ? PrescriptionInputLevel.Soft : level <= 85 ? PrescriptionInputLevel.Medium : PrescriptionInputLevel.Loud;
                double gainDb = 0;
                foreach (var kv in targets.GainsByFrequencyAndLevel)
                {
                    if (kv.Value != null && kv.Value.TryGetValue(prescLevel, out var g))
                        gainDb += g;
                }
                if (targets.GainsByFrequencyAndLevel.Count > 0)
                    gainDb /= targets.GainsByFrequencyAndLevel.Count;

                object value;
                if (valuesById.TryGetValue(paramId, out var existing) && existing != null)
                {
                    if (existing is double d) value = d + gainDb;
                    else if (existing is int i) value = i + (int)Math.Round(gainDb);
                    else if (existing is float f) value = (float)(f + gainDb);
                    else value = gainDb;
                }
                else
                {
                    value = gainDb;
                }

                result.ParameterUpdates.Add(new KeyValuePair<string, object>(paramId, value));
            }

            Debug.WriteLine($"[E7111V2Mapping] Produced {result.ParameterUpdates.Count} parameter updates for {libraryOrProductKey}.");
            return result;
        }
    }
}
