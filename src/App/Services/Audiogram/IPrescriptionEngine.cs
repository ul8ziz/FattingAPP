using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Options for prescription computation (extension point for NAL-NL2/DSL-v5).</summary>
    public sealed class PrescriptionOptions
    {
        /// <summary>Prescription method name (e.g. "NAL-NL2", "DSL-v5"). Stub ignores.</summary>
        public string? Method { get; set; }
    }

    /// <summary>Computes prescription targets from audiogram. Extension point for NAL-NL2/DSL-v5.</summary>
    public interface IPrescriptionEngine
    {
        PrescriptionTargets ComputeTargets(AudiogramSession? session, PrescriptionOptions? options = null);
    }
}
