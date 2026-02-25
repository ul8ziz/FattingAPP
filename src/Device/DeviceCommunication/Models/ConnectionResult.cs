using System.Collections.Generic;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>
    /// Result of connecting to selected devices. Per-ear status; overall success when at least one connected.
    /// </summary>
    public sealed class ConnectionResult
    {
        public bool Success { get; set; }
        public bool LeftConnected { get; set; }
        public bool RightConnected { get; set; }
        public string? LeftError { get; set; }
        public string? RightError { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
