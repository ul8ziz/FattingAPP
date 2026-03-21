using System;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    public enum ProgrammerType
    {
        Wired,
        Wireless
    }

    public class ProgrammerInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ProgrammerType Type { get; set; }
        public string InterfaceName { get; set; } = string.Empty; // "HI-PRO", "NOAHlink", etc.
        public string? Port { get; set; } // COM port for wired programmers
        public string? DeviceId { get; set; } // Device ID for wireless programmers
        public string? SerialNumber { get; set; }
        public string? Firmware { get; set; }
        /// <summary>Optional description from hardware enumeration (e.g. D2XX Description).</summary>
        public string? Description { get; set; }
        public bool IsAvailable { get; set; }
    }
}
