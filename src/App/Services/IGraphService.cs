using System.Collections.Generic;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Builds graph series from cached snapshot using parameter mapping.</summary>
    public interface IGraphService
    {
        /// <summary>
        /// Builds frequency gain curves (X: Hz, Y: dB) for the given memory and selected input levels.
        /// Uses mapping to resolve parameter IDs; returns empty list if mapping not configured.
        /// </summary>
        IReadOnlyList<GraphSeries> BuildFrequencyGainCurves(
            DeviceSettingsSnapshot? snapshot,
            string? libraryOrProductKey,
            int memoryIndex,
            IReadOnlyList<int> selectedLevels,
            string octaveMode);

        /// <summary>
        /// Builds I/O curves (X: dB SPL input, Y: dB SPL output) for the given memory and frequencies.
        /// Uses mapping to resolve parameter IDs; returns empty list if mapping not configured.
        /// </summary>
        IReadOnlyList<GraphSeries> BuildInputOutputCurves(
            DeviceSettingsSnapshot? snapshot,
            string? libraryOrProductKey,
            int memoryIndex,
            IReadOnlyList<int> selectedFrequenciesHz);

        /// <summary>Message when mapping is not configured for the current product (non-blocking).</summary>
        string? GetMappingNotConfiguredMessage(string? libraryOrProductKey);
    }
}
