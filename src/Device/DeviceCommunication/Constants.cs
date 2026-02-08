namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public static class Constants
    {
        // Supported wired programmer names - must match SDK's sd.config
        // From SDK sample: "Communication Accelerator Adaptor" is default
        public const string HiPro = "HI-PRO";
        public const string Dsp3 = "DSP3";
        public const string Caa = "Communication Accelerator Adaptor";
        public const string Promira = "Promira";

        // Supported wireless programmers  
        // From SDK sample: "Noahlink" and "RSL10"
        public const string Noahlink = "Noahlink";
        public const string Rsl10 = "RSL10";

        // Timeouts (from SDK sample constants)
        public const int ScanTimeoutMs = 15000;     // SDK: ScanTimeMs = 15000
        public const int ConnectTimeoutMs = 10000;   // SDK: ConnectTimeoutMs = 10000
        public const int DiscoveryTimeoutMs = 30000;  // Detection can be slow

        // BLE Driver location for Noahlink (from SDK sample)
        public const string DriverLocation = "%USERPROFILE%/.sounddesigner/nlw";

        // COM port for RSL10 (from SDK sample)
        public const string COMPort = "COM10";

        // Parameter lock key (hex string, empty = no unlock)
        public const string ParameterLockKey = "";
    }
}
