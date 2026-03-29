using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ul8ziz.FittingApp.Device.DeviceCommunication;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Persists user-added external product library root folders (paths only; no file copies).
    /// </summary>
    public sealed class ExternalLibraryFoldersSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public List<string> ExternalLibraryFolders { get; set; } = new();

        public static string GetDefaultFilePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Ul8ziz", "FittingApp", "external_library_folders.json");

        public static ExternalLibraryFoldersSettings Load()
        {
            var path = GetDefaultFilePath();
            try
            {
                if (!File.Exists(path))
                    return new ExternalLibraryFoldersSettings();

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ExternalLibraryFoldersSettings>(json, JsonOptions);
                if (loaded?.ExternalLibraryFolders == null)
                    return new ExternalLibraryFoldersSettings();

                loaded.ExternalLibraryFolders = loaded.ExternalLibraryFolders
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(Directory.Exists)
                    .ToList();
                return loaded;
            }
            catch
            {
                return new ExternalLibraryFoldersSettings();
            }
        }

        public void Save()
        {
            var path = GetDefaultFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }

        /// <summary>Applies persisted folders to <see cref="ProductLibraryRegistry"/>.</summary>
        public void ApplyToRegistry()
        {
            ProductLibraryRegistry.Instance.ReplaceExternalRootFolders(ExternalLibraryFolders);
        }
    }
}
