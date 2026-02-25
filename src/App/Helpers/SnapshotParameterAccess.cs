using System;
using System.Collections.Generic;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>Flattens a device snapshot into a parameter Id -> Value map for graph and mapping use.</summary>
    public static class SnapshotParameterAccess
    {
        /// <summary>Builds a read-only dictionary of parameter Id to current Value from the snapshot.</summary>
        public static IReadOnlyDictionary<string, object?> GetParameterValuesById(DeviceSettingsSnapshot? snapshot)
        {
            if (snapshot == null) return new Dictionary<string, object?>();

            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in snapshot.Categories)
            {
                foreach (var section in category.Sections)
                {
                    foreach (var item in section.Items)
                    {
                        if (!string.IsNullOrEmpty(item.Id))
                            dict[item.Id] = item.Value;
                    }
                }
            }
            return dict;
        }
    }
}
