using System;
using System.Diagnostics;
using System.IO;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>Exports snapshot parameter Id, Name, and ModuleName to a file for graph mapping discovery (e.g. E7111V2).</summary>
    public static class ParameterListExport
    {
        private static readonly object _lock = new object();
        private static bool _e7111V2DumpDoneThisSession;

        /// <summary>If the library key is E7111V2 and we have a snapshot, writes ParameterIds_E7111V2.txt once per session to the app base directory.</summary>
        public static void ExportOnceIfE7111V2(DeviceSettingsSnapshot? snapshot, string? libraryOrProductKey)
        {
            if (snapshot == null || string.IsNullOrEmpty(libraryOrProductKey)) return;
            if (!libraryOrProductKey.Contains("E7111V2", StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                if (_e7111V2DumpDoneThisSession) return;
                _e7111V2DumpDoneThisSession = true;
            }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(baseDir, "ParameterIds_E7111V2.txt");
                using var writer = new StreamWriter(path, append: false);
                writer.WriteLine("# Parameter list for E7111V2 — use these Id values in GraphParameterMap.json");
                writer.WriteLine("# Format: Id\tName\tModuleName");
                writer.WriteLine();

                foreach (var category in snapshot.Categories)
                {
                    foreach (var section in category.Sections)
                    {
                        foreach (var item in section.Items)
                        {
                            var id = item.Id ?? "";
                            var name = item.Name ?? "";
                            var module = item.ModuleName ?? "";
                            writer.WriteLine($"{id}\t{name}\t{module}");
                        }
                    }
                }

                Debug.WriteLine($"[ParameterListExport] Wrote {path}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParameterListExport] Export failed: {ex.Message}");
            }
        }
    }
}
