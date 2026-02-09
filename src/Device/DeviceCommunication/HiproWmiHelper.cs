using System;
using System.Management;
using System.Text.RegularExpressions;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Resolves HI-PRO COM port from WMI by VID/PID so we only try the real HI-PRO port (e.g. COM2), not Bluetooth COM3/COM4.
    /// </summary>
    public static class HiproWmiHelper
    {
        private static readonly Regex ComPortRegex = new Regex(@"COM(\d+)", RegexOptions.IgnoreCase);

        /// <summary>
        /// Gets the COM port name (e.g. "COM2") for the HI-PRO device matching the given USB VID and PID.
        /// VID/PID are hex without 0x (e.g. "0C33", "0012").
        /// Returns null if not found.
        /// </summary>
        public static string? GetHiproComPortFromWmi(string vid = "0C33", string pid = "0012")
        {
            try
            {
                var vidNorm = (vid ?? "0C33").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                var pidNorm = (pid ?? "0012").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%COM%'");
                foreach (ManagementBaseObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";
                    bool isHiproVidPid = deviceId.IndexOf(vidNorm, StringComparison.OrdinalIgnoreCase) >= 0
                                         && deviceId.IndexOf(pidNorm, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isHiproVidPid)
                        continue;
                    var match = ComPortRegex.Match(name);
                    if (match.Success)
                        return "COM" + match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                ScanDiagnostics.WriteLine($"GetHiproComPortFromWmi error: {ex.Message}");
            }
            return null;
        }
    }
}
