using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Builds a structured diagnostic report for D2XX: process arch, OS, DLL paths, devices, open status, etc.
    /// </summary>
    public static class HiProDiagnostics
    {
        public static string BuildReport(
            string? resolvedFtd2xxPath,
            bool d2xxLoaded,
            IReadOnlyList<D2xxDeviceInfo>? devices,
            bool isOpen,
            int? readTimeoutMs,
            int? writeTimeoutMs,
            byte? latency,
            uint? rxBytes,
            uint? txBytes,
            string? lastError)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== HI-PRO D2XX Diagnostic Report ===");
            sb.AppendLine($"Generated: {DateTime.UtcNow:O} (UTC)");
            sb.AppendLine();
            sb.AppendLine("--- Environment ---");
            sb.AppendLine($"Process architecture: {(IntPtr.Size == 8 ? "x64" : "x86")}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"App base: {AppDomain.CurrentDomain.BaseDirectory}");
            sb.AppendLine();
            sb.AppendLine("--- D2XX loader ---");
            sb.AppendLine($"ftd2xx.dll resolved: {resolvedFtd2xxPath ?? "(not found)"}");
            sb.AppendLine($"FTD2XX_NET loaded (EnsureD2xxLoaded): {d2xxLoaded}");
            sb.AppendLine();
            sb.AppendLine("--- Devices (D2XX enumeration) ---");
            if (devices == null || devices.Count == 0)
                sb.AppendLine("  (none)");
            else
            {
                foreach (var d in devices)
                    sb.AppendLine($"  {d}");
            }
            sb.AppendLine();
            sb.AppendLine("--- Connection ---");
            sb.AppendLine($"Device open: {isOpen}");
            if (readTimeoutMs.HasValue) sb.AppendLine($"Read timeout (ms): {readTimeoutMs}");
            if (writeTimeoutMs.HasValue) sb.AppendLine($"Write timeout (ms): {writeTimeoutMs}");
            if (latency.HasValue) sb.AppendLine($"Latency: {latency}");
            if (rxBytes.HasValue) sb.AppendLine($"RX queue bytes: {rxBytes}");
            if (txBytes.HasValue) sb.AppendLine($"TX bytes waiting: {txBytes}");
            if (!string.IsNullOrEmpty(lastError))
                sb.AppendLine($"Last error: {lastError}");
            sb.AppendLine();
            sb.AppendLine("=== End Report ===");
            return sb.ToString();
        }
    }
}
