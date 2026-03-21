using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>JSON file persistence for audiogram sessions.</summary>
    public sealed class AudiogramPersistenceService : IAudiogramPersistenceService
    {
        // JsonStringEnumConverter: enums serialized as their string name ("AC", "Left", etc.)
        // instead of integers. This makes persisted JSON human-readable and robust against
        // enum value reordering. Also accepts integer values during read (backward compat).
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public async Task SaveAsync(AudiogramSession session, string filePath)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", nameof(filePath));

            EnsureDirectory(filePath);
            await using var fs = File.Create(filePath);
            await JsonSerializer.SerializeAsync(fs, session, JsonOptions).ConfigureAwait(false);
        }

        public async Task<AudiogramSession?> LoadAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            if (!File.Exists(filePath)) return null;

            await using var fs = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<AudiogramSession>(fs, JsonOptions).ConfigureAwait(false);
        }

        public async Task SaveSessionsAsync(IReadOnlyDictionary<int, AudiogramSession> sessionsByMemory, string filePath)
        {
            if (sessionsByMemory == null) throw new ArgumentNullException(nameof(sessionsByMemory));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", nameof(filePath));

            EnsureDirectory(filePath);
            await using var fs = File.Create(filePath);
            await JsonSerializer.SerializeAsync(fs, sessionsByMemory, JsonOptions).ConfigureAwait(false);
        }

        public async Task<IReadOnlyDictionary<int, AudiogramSession>?> LoadSessionsAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            if (!File.Exists(filePath)) return null;

            await using var fs = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<Dictionary<int, AudiogramSession>>(fs, JsonOptions)
                .ConfigureAwait(false);
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
