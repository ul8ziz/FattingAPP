using System.Collections.Generic;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>
    /// Result of hearing-aid discovery on Left and Right. Partial success allowed (one side found, one not).
    /// </summary>
    public sealed class DetectResult
    {
        public DeviceInfo? Left { get; set; }
        public DeviceInfo? Right { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public bool IsSuccess => Left != null || Right != null;
        public bool IsFatalFailure => Left == null && Right == null && Errors.Count > 0;
    }
}
