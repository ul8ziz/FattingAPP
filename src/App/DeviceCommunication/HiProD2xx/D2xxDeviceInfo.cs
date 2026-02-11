namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Device info from D2XX enumeration (GetDeviceList). COM port may be disabled; D2XX still valid.
    /// </summary>
    public sealed class D2xxDeviceInfo
    {
        public string Description { get; init; } = "";
        public string SerialNumber { get; init; } = "";
        public uint Type { get; init; }
        public uint Flags { get; init; }
        public uint Id { get; init; }
        public uint LocId { get; init; }
        public int Index { get; init; }

        public override string ToString() =>
            $"[{Index}] {Description} S/N={SerialNumber} Type={Type} Id={Id} LocId={LocId}";
    }
}
