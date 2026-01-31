namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public static class Constants
    {
        // Supported wired programmers
        public const string HiPro = "HI-PRO";
        public const string Dsp3 = "DSP3";
        public const string Caa = "Communication Accelerator Adaptor";
        public const string Promira = "Promira";

        // Supported wireless programmers
        public const string Noahlink = "NOAHlink";
        public const string Rsl10 = "RSL10";

        // Timeouts
        public const int ScanTimeoutMs = 15000;
        public const int ConnectTimeoutMs = 10000;
        public const int DiscoveryTimeoutMs = 5000;

        // Parameter lock key (if needed)
        public const string ParameterLockKey = "";
    }
}
