using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services.Audiogram
{
    /// <summary>Maps prescription targets to device parameter Id and value updates.</summary>
    public interface IParameterMappingService
    {
        DeviceMappingResult MapTargetsToDevice(
            PrescriptionTargets targets,
            string? libraryOrProductKey,
            DeviceSettingsSnapshot? currentSnapshot);
    }
}
