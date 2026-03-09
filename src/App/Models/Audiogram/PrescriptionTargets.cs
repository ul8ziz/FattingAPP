using System.Collections.Generic;

namespace Ul8ziz.FittingApp.App.Models.Audiogram
{
    /// <summary>Input level categories for prescription targets (soft / medium / loud).</summary>
    public enum PrescriptionInputLevel
    {
        Soft,
        Medium,
        Loud
    }

    /// <summary>Target gains from prescription: per frequency (Hz) and input level (soft/medium/loud), gain in dB.</summary>
    public sealed class PrescriptionTargets
    {
        /// <summary>Key: frequency Hz (e.g. 250, 500, 1000). Value: gains by input level in dB.</summary>
        public Dictionary<int, Dictionary<PrescriptionInputLevel, double>> GainsByFrequencyAndLevel { get; set; } =
            new Dictionary<int, Dictionary<PrescriptionInputLevel, double>>();
    }
}
