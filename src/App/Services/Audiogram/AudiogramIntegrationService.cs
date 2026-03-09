using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Applies prescription mapping result to session snapshot; marks memory dirty. No direct SDK calls.</summary>
    public sealed class AudiogramIntegrationService : IAudiogramIntegrationService
    {
        private readonly DeviceSessionService _session = DeviceSessionService.Instance;

        public void ApplyToFitting(DeviceMappingResult mappingResult, DeviceSide side, int memoryIndex)
        {
            if (mappingResult?.ParameterUpdates == null || mappingResult.ParameterUpdates.Count == 0)
                return;

            var (leftSnap, rightSnap) = _session.GetSnapshotsForMemory(memoryIndex);
            var snapshot = side == DeviceSide.Left ? leftSnap : rightSnap;
            if (snapshot == null)
            {
                Debug.WriteLine($"[AudiogramIntegration] No snapshot for {side} memory {memoryIndex}; cannot apply.");
                return;
            }

            int applied = 0;
            foreach (var kv in mappingResult.ParameterUpdates)
            {
                var paramId = kv.Key;
                var value = kv.Value;
                if (string.IsNullOrEmpty(paramId)) continue;

                if (TryFindAndSetParameter(snapshot, paramId, value))
                    applied++;
            }

            _session.SetMemorySnapshot(side, memoryIndex, snapshot);
            _session.MarkMemoryDirty(side, memoryIndex);
            Debug.WriteLine($"[AudiogramIntegration] Applied {applied} parameter updates to {side} memory {memoryIndex}.");
        }

        private static bool TryFindAndSetParameter(DeviceSettingsSnapshot snapshot, string paramId, object value)
        {
            foreach (var category in snapshot.Categories)
            {
                foreach (var section in category.Sections)
                {
                    foreach (var item in section.Items)
                    {
                        if (string.Equals(item.Id, paramId, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Value = ConvertValue(value, item.SettingDataType);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static object? ConvertValue(object value, SettingItem.DataType dataType)
        {
            if (value == null) return null;
            try
            {
                switch (dataType)
                {
                    case SettingItem.DataType.Int:
                        return value is int i ? i : Convert.ToInt32(value);
                    case SettingItem.DataType.Double:
                        return value is double d ? d : Convert.ToDouble(value);
                    case SettingItem.DataType.Bool:
                        return value is bool b ? b : Convert.ToBoolean(value);
                    case SettingItem.DataType.String:
                        return value.ToString();
                    default:
                        return value;
                }
            }
            catch
            {
                return value;
            }
        }
    }
}
