using System.Collections.Generic;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>
    /// Deterministic result of per-port wired discovery. Partial success: one ear can be found and the other fail.
    /// </summary>
    public sealed class DiscoveryResult
    {
        public bool FoundLeft { get; set; }
        public bool FoundRight { get; set; }

        public string? LeftSerialId { get; set; }
        public string? LeftFirmwareId { get; set; }
        public string? LeftProductId { get; set; }
        public string? LeftChipId { get; set; }
        public string? LeftHybridId { get; set; }
        public string? LeftHybridSerial { get; set; }
        public bool LeftParameterLockState { get; set; }

        public string? RightSerialId { get; set; }
        public string? RightFirmwareId { get; set; }
        public string? RightProductId { get; set; }
        public string? RightChipId { get; set; }
        public string? RightHybridId { get; set; }
        public string? RightHybridSerial { get; set; }
        public bool RightParameterLockState { get; set; }

        /// <summary>Per-side errors: normalized error code + original message.</summary>
        public List<DiscoveryError> Errors { get; set; } = new List<DiscoveryError>();

        public bool IsSuccess => FoundLeft || FoundRight;
    }

    public sealed class DiscoveryError
    {
        public string Side { get; set; } = ""; // "Left" or "Right"
        public string ErrorCode { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
