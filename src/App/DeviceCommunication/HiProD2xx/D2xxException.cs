namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Exception thrown when an FTDI D2XX operation fails. Contains FT_STATUS and context.
    /// </summary>
    public class D2xxException : Exception
    {
        /// <summary>FTDI status code (e.g. from FTD2XX_NET.FTDI).</summary>
        public uint FtStatus { get; }

        /// <summary>Operation that failed (e.g. "OpenByIndex", "Write").</summary>
        public string Context { get; }

        public D2xxException(uint ftStatus, string context, string? message = null)
            : base(message ?? $"D2XX error: {context} (FT_STATUS=0x{ftStatus:X})")
        {
            FtStatus = ftStatus;
            Context = context ?? "";
        }

        public D2xxException(uint ftStatus, string context, Exception inner)
            : base($"D2XX error: {context} (FT_STATUS=0x{ftStatus:X})", inner)
        {
            FtStatus = ftStatus;
            Context = context ?? "";
        }
    }
}
