using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Wrapper for Sound Designer SDK (sdnet) to read/write device settings.
    /// Follows the official Programmer's Guide flow:
    ///   InitializeDevice → ReadParameters → WriteParameters (all via Begin/End async pattern).
    /// </summary>
    public interface ISoundDesignerService
    {
        /// <summary>
        /// Initialize product with device via BeginInitializeDevice/EndInitializeDevice.
        /// Returns true if device is configured and ready for Read/Write.
        /// This is the FITTING initialization — NOT ConfigureDevice (manufacturing).
        /// See Programmer's Guide Section 6.3.
        /// </summary>
        Task<bool> InitializeDeviceAsync(IProduct product, ICommunicationAdaptor adaptor, CancellationToken cancellationToken);

        /// <summary>Read all settings from device into in-memory snapshot using BeginReadParameters.</summary>
        Task<DeviceSettingsSnapshot> ReadAllSettingsAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSide side, IProgress<string>? progress, CancellationToken cancellationToken);

        /// <summary>Reads one memory snapshot (0-7) using SDK memory context selection + batched ReadParameters.</summary>
        Task<DeviceSettingsSnapshot> ReadMemorySnapshotAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSide side, int memoryIndex, IProgress<string>? progress, CancellationToken cancellationToken);

        /// <summary>Write modified settings to device using BeginWriteParameters and optionally verify with read-back.</summary>
        /// <param name="onWriteFailed">Optional: called with the SDK error message when write fails (e.g. for user-facing toast).</param>
        /// <param name="selectedMemoryIndex">Optional: current memory index (0-7) for save logging.</param>
        Task<bool> WriteSettingsAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, IProgress<string>? progress, CancellationToken cancellationToken, Action<string>? onWriteFailed = null, int? selectedMemoryIndex = null);

        /// <summary>Writes one memory snapshot (0-7) using SDK memory context selection + batched WriteParameters.</summary>
        Task<bool> WriteMemorySnapshotAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, int memoryIndex, IProgress<string>? progress, CancellationToken cancellationToken, Action<string>? onWriteFailed = null);

        /// <summary>NVM-only: Restore from NVM then read from RAM for one memory. Semantics per SDK sample presuite_memory_switch.py (set CurrentMemory then ReadParameters loads NVM into RAM).</summary>
        Task<DeviceSettingsSnapshot> ReloadFromNvmAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSide side, int memoryIndex, IProgress<string>? progress, CancellationToken cancellationToken);

        /// <summary>NVM-only: Burn current snapshot to NVM for one memory (WriteParameters for selected memory; per presuite_memory_switch.py WriteParameters(memoryNumber) writes to NVM).</summary>
        Task<bool> BurnMemoryToNvmAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, int memoryIndex, IProgress<string>? progress, CancellationToken cancellationToken, Action<string>? onWriteFailed = null);

        /// <summary>NVM-only: Verify a memory matches NVM by reloading and comparing key values. Returns (success, failureMessage).</summary>
        Task<(bool Verified, string? FailureMessage)> VerifyMemoryMatchesNvmAsync(IProduct product, ICommunicationAdaptor adaptor, DeviceSettingsSnapshot snapshot, int memoryIndex, int maxItemsToCheck, CancellationToken cancellationToken);

        /// <summary>Write a single parameter immediately (for Live Mode). Throttling should be applied by caller.</summary>
        Task WriteParameterAsync(IProduct product, ICommunicationAdaptor adaptor, SettingItem item, CancellationToken cancellationToken);
    }
}
