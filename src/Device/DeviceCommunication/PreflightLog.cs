using System;
using System.IO;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Simple file append logger for HI-PRO preflight. Rotates when file exceeds 5MB.
    /// </summary>
    internal static class PreflightLog
    {
        private static readonly object Lock = new object();
        private const long MaxFileBytes = 5 * 1024 * 1024; // 5MB
        private static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "logs");
        private static string LogPath => Path.Combine(LogDirectory, "hpro_diag.log");

        public static void Append(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    var path = LogPath;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length >= MaxFileBytes)
                    {
                        var backup = path + ".old";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(path, backup);
                    }
                    File.AppendAllText(path, message + Environment.NewLine);
                }
                catch
                {
                    // Best-effort; do not throw
                }
            }
        }

        public static void AppendLine(string line)
        {
            Append(line);
        }
    }
}
