using System;

namespace Ul8ziz.FittingApp.App.Models.Audiogram
{
    /// <summary>Complete audiogram session: left and right ear data plus optional metadata.</summary>
    public sealed class AudiogramSession
    {
        public EarAudiogram? LeftEarAudiogram { get; set; }
        public EarAudiogram? RightEarAudiogram { get; set; }
        public string? PatientId { get; set; }
        public DateTime? Date { get; set; }
        public string? Notes { get; set; }
    }
}
