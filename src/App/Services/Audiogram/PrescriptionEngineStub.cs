using System.Collections.Generic;
using System.Linq;
using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Stub prescription engine: returns flat 0 dB targets. Replace with NAL-NL2/DSL-v5 when implemented.</summary>
    public sealed class PrescriptionEngineStub : IPrescriptionEngine
    {
        public PrescriptionTargets ComputeTargets(AudiogramSession? session, PrescriptionOptions? options = null)
        {
            var targets = new PrescriptionTargets();
            if (session == null) return targets;

            var frequencies = new HashSet<int>();
            if (session.LeftEarAudiogram?.Points != null)
                foreach (var p in session.LeftEarAudiogram.Points)
                    frequencies.Add((int)p.FrequencyHz);
            if (session.RightEarAudiogram?.Points != null)
                foreach (var p in session.RightEarAudiogram.Points)
                    frequencies.Add((int)p.FrequencyHz);

            if (frequencies.Count == 0)
                frequencies = new HashSet<int>(StandardAudiogramFrequencies.Hz);

            foreach (var hz in frequencies)
            {
                targets.GainsByFrequencyAndLevel[hz] = new Dictionary<PrescriptionInputLevel, double>
                {
                    [PrescriptionInputLevel.Soft] = 0,
                    [PrescriptionInputLevel.Medium] = 0,
                    [PrescriptionInputLevel.Loud] = 0
                };
            }

            return targets;
        }
    }
}
