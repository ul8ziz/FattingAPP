using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Applies prescription mapping result to session snapshot and marks memory dirty.</summary>
    public interface IAudiogramIntegrationService
    {
        /// <summary>Applies mapping result to snapshot for the given side and memory, then marks dirty. No SDK call.</summary>
        void ApplyToFitting(DeviceMappingResult mappingResult, DeviceSide side, int memoryIndex);
    }
}
