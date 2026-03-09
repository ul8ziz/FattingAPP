using System.Collections.Generic;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Builds graph series for audiogram threshold curve (Hz vs dB HL) and optional target gain overlay.</summary>
    public interface IAudiogramGraphService
    {
        IReadOnlyList<GraphSeries> BuildAudiogramSeries(EarAudiogram? ear);
        IReadOnlyList<GraphSeries> BuildTargetGainSeries(PrescriptionTargets? targets);
    }
}
