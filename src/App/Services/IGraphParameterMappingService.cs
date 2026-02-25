using System.Collections.Generic;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Resolves graph parameter mappings per library/product. Uses GraphParameterMap.json.</summary>
    public interface IGraphParameterMappingService
    {
        /// <summary>Whether a mapping exists for the given library/product key.</summary>
        bool HasMappingFor(string? libraryOrProductKey);

        /// <summary>Gets frequency gain mapping: level (e.g. 40, 55) -> parameter Id for gain at that input level.</summary>
        IReadOnlyDictionary<int, string> GetFreqGainParameterIdsByLevel(string? libraryOrProductKey);

        /// <summary>Gets center frequency parameter Ids (one per band) for frequency gain X-axis.</summary>
        IReadOnlyList<string> GetFreqGainCenterFrequencyParamIds(string? libraryOrProductKey);

        /// <summary>Gets I/O curve mapping: frequency in Hz -> parameter Id(s) for input/output curve at that frequency.</summary>
        IReadOnlyDictionary<int, IReadOnlyList<string>> GetInputOutputParamIdsByFrequencyHz(string? libraryOrProductKey);
    }
}
