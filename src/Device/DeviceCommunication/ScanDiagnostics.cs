using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Writes scan and environment diagnostics to Console and to log.txt in AppBaseDir.
    /// </summary>
    public static class ScanDiagnostics
    {
        private static readonly object _lock = new object();
        private static string? _logFilePath;

        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                    _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "log.txt");
                return _logFilePath;
            }
        }

        public static void WriteLine(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            lock (_lock)
            {
                Console.WriteLine(message);
                try
                {
                    File.AppendAllText(LogFilePath, message + Environment.NewLine);
                }
                catch
                {
                    // ignore file errors
                }
            }
        }

        public static void WriteStartupAndScanPreamble()
        {
            var arch = IntPtr.Size == 8 ? "x64" : "x86";
            WriteLine("========== Scan diagnostics ==========");
            WriteLine($"Environment.Version: {Environment.Version}");
            WriteLine($"RuntimeInformation.FrameworkDescription: {RuntimeInformation.FrameworkDescription}");
            WriteLine($"AppContext.TargetFrameworkName: {AppContext.TargetFrameworkName}");
            WriteLine($"Process architecture: {arch}");
            WriteLine($"Process path / AppBaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
            WriteLine($"SD_CONFIG_PATH: {Environment.GetEnvironmentVariable("SD_CONFIG_PATH") ?? "(not set)"}");
            var sdConfig = SdkConfiguration.GetConfigPath();
            WriteLine($"SD_CONFIG exists: {File.Exists(sdConfig)}");
            WriteLine($"HI-PRO path: {SdkConfiguration.HiProDriverPath}");
            WriteLine($"HI-PRO exists: {Directory.Exists(SdkConfiguration.HiProDriverPath)}");
            var ctkPath = SdkConfiguration.FindCtkPath();
            WriteLine($"CTK path: {ctkPath ?? "(not found)"}");
            WriteLine($"CTK exists: {ctkPath != null && Directory.Exists(ctkPath)}");
            try
            {
                var ports = System.IO.Ports.SerialPort.GetPortNames();
                WriteLine($"COM ports (SerialPort.GetPortNames): [{string.Join(", ", ports)}]");
            }
            catch (Exception ex)
            {
                WriteLine($"COM ports error: {ex.Message}");
            }
            var hiproCom = HiproWmiHelper.GetHiproComPortFromWmi("0C33", "0012");
            WriteLine($"WMI HI-PRO COM port (VID_0C33 PID_0012): {hiproCom ?? "(none)"}");
            WriteLine("========================================");
        }

        /// <summary>
        /// Logs the CTK communication interface list from the SDK (Strategy A). Call before scan when ProductManager is available.
        /// </summary>
        public static void WriteSdkInterfaceList(SDLib.IProductManager? productManager)
        {
            if (productManager == null) return;
            var list = SdkScanHelper.GetSdkCommunicationInterfaces(productManager);
            WriteLine("CTK communication interface list from SDK: [" + string.Join(", ", list) + "]");
        }
    }
}
