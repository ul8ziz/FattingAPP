using System.IO;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    public static class SdkConfiguration
    {
        // Path to SDK folder (can be overridden)
        public static string SdkPath { get; set; } = @"E:\Work\Fatting App\SoundDesignerSDK";

        // Library file name (E7111V2.library or E7160SL.library)
        public static string LibraryName { get; set; } = "E7160SL.library";

        // Config file name
        public static string ConfigFileName { get; set; } = "sd.config";

        public static string GetLibraryPath()
        {
            var libraryPath = Path.Combine(SdkPath, "products", LibraryName);
            if (!File.Exists(libraryPath))
            {
                // Try alternative location
                libraryPath = Path.Combine(SdkPath, "binaries", "win64", LibraryName);
            }
            return libraryPath;
        }

        public static string GetConfigPath()
        {
            // First try to use the config from Device/Libs
            var localConfigPath = Path.Combine("src", "Device", "Libs", ConfigFileName);
            if (File.Exists(localConfigPath))
            {
                return Path.GetFullPath(localConfigPath);
            }

            // Fallback to SDK path
            var configPath = Path.Combine(SdkPath, "binaries", "win64", ConfigFileName);
            if (File.Exists(configPath))
            {
                return configPath;
            }

            // Use local copy if available
            return Path.Combine("src", "Device", "Libs", ConfigFileName);
        }

        public static void SetupEnvironment()
        {
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                System.Environment.SetEnvironmentVariable("SD_CONFIG_PATH", configPath);
            }
        }
    }
}
