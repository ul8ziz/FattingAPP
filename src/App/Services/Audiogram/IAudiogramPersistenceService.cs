using System.Collections.Generic;
using System.Threading.Tasks;
using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Save/load audiogram session to/from file.</summary>
    public interface IAudiogramPersistenceService
    {
        Task SaveAsync(AudiogramSession session, string filePath);
        Task<AudiogramSession?> LoadAsync(string filePath);

        /// <summary>Save all per-memory sessions to a file (e.g. app-data path for persistence across restarts).</summary>
        Task SaveSessionsAsync(IReadOnlyDictionary<int, AudiogramSession> sessionsByMemory, string filePath);

        /// <summary>Load per-memory sessions from file. Returns null if file is missing or invalid.</summary>
        Task<IReadOnlyDictionary<int, AudiogramSession>?> LoadSessionsAsync(string filePath);
    }
}
