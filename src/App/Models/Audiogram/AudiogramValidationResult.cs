using System.Collections.Generic;

namespace Ul8ziz.FittingApp.App.Models.Audiogram
{
    /// <summary>Result of audiogram validation: errors and warnings.</summary>
    public sealed class AudiogramValidationResult
    {
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public bool IsValid => Errors.Count == 0;
    }
}
