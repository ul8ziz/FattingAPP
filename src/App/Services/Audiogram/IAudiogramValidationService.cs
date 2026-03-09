using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Validates audiogram data (frequency set, range checks, completeness).</summary>
    public interface IAudiogramValidationService
    {
        AudiogramValidationResult Validate(AudiogramSession? session);

        /// <summary>Validates only the specified ears. Use when generating WDRC for connected ear(s) only.</summary>
        AudiogramValidationResult Validate(AudiogramSession? session, bool validateLeft, bool validateRight);

        AudiogramValidationResult Validate(EarAudiogram? ear);
    }
}
