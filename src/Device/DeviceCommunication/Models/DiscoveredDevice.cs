using SDLib;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>
    /// Result of device detection on one programmer port (Left or Right) per Programmer's Guide Section 5.3.2.
    /// </summary>
    public sealed class DiscoveredDevice
    {
        /// <summary>Port (kLeft or kRight).</summary>
        public CommunicationPort Port { get; set; }

        /// <summary>Side for UI binding (Left or Right).</summary>
        public DeviceSide Side { get; set; }

        /// <summary>True if a device was detected and identity info is present.</summary>
        public bool Found { get; set; }

        /// <summary>Error code when Found is false (e.g. E_INVALID_STATE, E_NOT_FOUND).</summary>
        public string? ErrorCode { get; set; }

        /// <summary>Full error message when Found is false.</summary>
        public string? ErrorMessage { get; set; }

        // Device identity (when Found == true) — minimum FirmwareId; ProductId/SerialId/ChipId if available
        public string? FirmwareId { get; set; }
        public string? ProductId { get; set; }
        public string? SerialId { get; set; }
        public string? ChipId { get; set; }
        public string? HybridId { get; set; }
        public string? HybridSerial { get; set; }
        /// <summary>True when device parameters are locked (from IDeviceInfo.ParameterLockState).</summary>
        public bool ParameterLockState { get; set; }
    }
}
