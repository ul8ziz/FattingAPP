namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    public enum DeviceSide
    {
        Left,
        Right
    }

    public class DeviceInfo
    {
        public DeviceSide Side { get; set; }
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Firmware { get; set; } = string.Empty;
        public int? BatteryLevel { get; set; }
        public string? HybridId { get; set; }
        public string? HybridSerial { get; set; }
        public string? ProductId { get; set; }
        public string? ChipId { get; set; }
        public bool IsDetected { get; set; }
    }
}
