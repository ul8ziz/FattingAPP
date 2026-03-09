using System.Collections.Generic;

namespace Ul8ziz.FittingApp.App.Models.Audiogram
{
    /// <summary>Result of mapping prescription targets to device parameters: parameter Id and value pairs to apply to snapshot.</summary>
    public sealed class DeviceMappingResult
    {
        /// <summary>Parameter Id (e.g. X_WDRC_LowLevelGain[0]) to value (numeric or string per parameter type).</summary>
        public List<KeyValuePair<string, object>> ParameterUpdates { get; set; } = new();
    }
}
