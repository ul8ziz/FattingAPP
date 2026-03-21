using System;
using System.Collections.Generic;
using System.Linq;
using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Standard audiogram frequencies (Hz) for validation and display.</summary>
    public static class StandardAudiogramFrequencies
    {
        public static readonly int[] Hz = { 250, 500, 1000, 2000, 3000, 4000, 6000, 8000 };

        public const int MinDbHL = -10;
        public const int MaxDbHL = 120;
        public const int MaxUclDbHL = 130;
    }

    /// <summary>Validates audiogram data: frequency set, range checks, completeness.</summary>
    public sealed class AudiogramValidationService : IAudiogramValidationService
    {
        public AudiogramValidationResult Validate(AudiogramSession? session)
        {
            return Validate(session, validateLeft: true, validateRight: true);
        }

        /// <summary>Validates only the specified ears. Disconnected ears are ignored (no errors or warnings for them).</summary>
        public AudiogramValidationResult Validate(AudiogramSession? session, bool validateLeft, bool validateRight)
        {
            var result = new AudiogramValidationResult();
            if (session == null)
            {
                result.Errors.Add("Audiogram session is null.");
                return result;
            }

            if (!validateLeft && !validateRight)
            {
                result.Errors.Add("No connected ear. Connect a hearing aid to generate WDRC.");
                return result;
            }

            if (validateLeft && session.LeftEarAudiogram != null)
                MergeResult(result, Validate(session.LeftEarAudiogram), "Left");
            if (validateRight && session.RightEarAudiogram != null)
                MergeResult(result, Validate(session.RightEarAudiogram), "Right");

            // Only warn about missing data for ears we are validating
            if (validateLeft && (session.LeftEarAudiogram == null || session.LeftEarAudiogram.Points == null || session.LeftEarAudiogram.Points.Count == 0))
                result.Warnings.Add("Left: No threshold points.");
            if (validateRight && (session.RightEarAudiogram == null || session.RightEarAudiogram.Points == null || session.RightEarAudiogram.Points.Count == 0))
                result.Warnings.Add("Right: No threshold points.");

            return result;
        }

        public AudiogramValidationResult Validate(EarAudiogram? ear)
        {
            var result = new AudiogramValidationResult();
            if (ear == null)
            {
                result.Errors.Add("Ear audiogram is null.");
                return result;
            }

            if (ear.Points == null || ear.Points.Count == 0)
            {
                result.Warnings.Add("No threshold points.");
                return result;
            }

            var seenFreq = new HashSet<double>();
            foreach (var p in ear.Points)
            {
                if (p.FrequencyHz <= 0 || p.FrequencyHz > 20000)
                    result.Errors.Add($"Invalid frequency: {p.FrequencyHz} Hz.");
                if (seenFreq.Contains(p.FrequencyHz))
                    result.Errors.Add($"Duplicate frequency: {p.FrequencyHz} Hz.");
                seenFreq.Add(p.FrequencyHz);

                if (p.ThresholdDbHL.HasValue)
                {
                    if (p.ThresholdDbHL.Value < StandardAudiogramFrequencies.MinDbHL ||
                        p.ThresholdDbHL.Value > StandardAudiogramFrequencies.MaxDbHL)
                        result.Errors.Add($"Threshold out of range ({p.FrequencyHz} Hz): {p.ThresholdDbHL} dB HL.");
                }

                if (p.UclDbHL.HasValue)
                {
                    if (p.UclDbHL.Value < StandardAudiogramFrequencies.MinDbHL ||
                        p.UclDbHL.Value > StandardAudiogramFrequencies.MaxUclDbHL)
                        result.Errors.Add($"UCL out of range ({p.FrequencyHz} Hz): {p.UclDbHL} dB HL.");
                }
            }

            return result;
        }

        private static void MergeResult(AudiogramValidationResult target, AudiogramValidationResult source, string prefix)
        {
            foreach (var e in source.Errors)
                target.Errors.Add($"{prefix}: {e}");
            foreach (var w in source.Warnings)
                target.Warnings.Add($"{prefix}: {w}");
        }
    }
}
