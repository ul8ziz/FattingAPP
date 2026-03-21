using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services.Diagnostics
{
    /// <summary>Backend diagnostics service. Records errors, warnings, and critical events to disk. No UI.</summary>
    public sealed class DiagnosticService
    {
        private static readonly Lazy<DiagnosticService> _instance = new(() => new DiagnosticService());
        public static DiagnosticService Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _baseDir;
        private readonly int _recentBufferSize = 10;
        private readonly List<string> _recentLines = new();

        private DiagnosticService()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
        }

        /// <summary>Records a full diagnostic entry.</summary>
        public void Record(DiagnosticEntry entry)
        {
            if (entry == null) return;
            lock (_lock)
            {
                try
                {
                    EnsureFolders();
                    var dateStr = entry.TimestampUtc.ToString("yyyyMMdd");
                    var timeStr = entry.TimestampUtc.ToString("HH:mm:ss.fff");

                    // Timeline log: [Timestamp] [Category] [Severity] Message | key=value | key=value
                    var timelinePath = Path.Combine(_baseDir, "logs", "diagnostics", $"timeline_{dateStr}.log");
                    var line = FormatTimelineLine(entry, timeStr);
                    AppendLine(timelinePath, line);
                    AddRecent(line);

                    // Errors JSON (Error and Critical only)
                    if (entry.Severity == DiagnosticSeverity.Error || entry.Severity == DiagnosticSeverity.Critical)
                    {
                        var errorsPath = Path.Combine(_baseDir, "logs", "errors", $"errors_{dateStr}.json");
                        AppendJsonLine(errorsPath, entry);
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        ScanDiagnostics.WriteLine($"[DiagnosticService] Failed to record: {ex.Message}");
                    }
                    catch { /* ignore */ }
                }
            }
        }

        /// <summary>Records an exception from DiagnosticBridge (Device layer). Maps string category to enum.</summary>
        public void RecordFromBridge(string operation, string category, Exception ex, string? messageOverride)
        {
            var cat = ParseCategory(category);
            var entry = BuildEntryFromException(operation, cat, DiagnosticSeverity.Error, ex, null, null, null, null);
            if (!string.IsNullOrEmpty(messageOverride))
                entry.Message = messageOverride;
            Record(entry);
        }

        /// <summary>Records a warning from DiagnosticBridge (Device layer).</summary>
        public void RecordWarningFromBridge(string operation, string category, string message)
        {
            var entry = BuildEntry(operation, ParseCategory(category), DiagnosticSeverity.Warning, message, null, null, null, null);
            Record(entry);
        }

        private static DiagnosticCategory ParseCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return DiagnosticCategory.General;
            return category.ToUpperInvariant() switch
            {
                "UNHANDLEDEXCEPTION" or "UNHANDLED" => DiagnosticCategory.UnhandledException,
                "SDK" => DiagnosticCategory.SDK,
                "COMMUNICATION" => DiagnosticCategory.Communication,
                "DEVICE" => DiagnosticCategory.Device,
                "SCAN" => DiagnosticCategory.Scan,
                "CONNECTION" => DiagnosticCategory.Connection,
                "PERSISTENCE" => DiagnosticCategory.Persistence,
                "VALIDATION" => DiagnosticCategory.Validation,
                "SESSION" => DiagnosticCategory.Session,
                _ => DiagnosticCategory.General
            };
        }

        /// <summary>Records an exception with Error severity.</summary>
        public void RecordException(string operation, DiagnosticCategory category, Exception ex, string? screenContext = null, int? memoryIndex = null, DeviceSide? side = null, Dictionary<string, string>? customContext = null)
        {
            var entry = BuildEntryFromException(operation, category, DiagnosticSeverity.Error, ex, screenContext, memoryIndex, side, customContext);
            Record(entry);
        }

        /// <summary>Records a warning.</summary>
        public void RecordWarning(string operation, DiagnosticCategory category, string message, string? screenContext = null, int? memoryIndex = null, DeviceSide? side = null, Dictionary<string, string>? customContext = null)
        {
            var entry = BuildEntry(operation, category, DiagnosticSeverity.Warning, message, screenContext, memoryIndex, side, customContext);
            Record(entry);
        }

        /// <summary>Records a critical (fatal) event and writes a crash report.</summary>
        public void RecordCritical(string operation, DiagnosticCategory category, Exception ex, string? screenContext = null)
        {
            var entry = BuildEntryFromException(operation, category, DiagnosticSeverity.Critical, ex, screenContext, null, null, null);
            Record(entry);
            WriteCrashReport(entry);
        }

        /// <summary>Writes a crash report file for fatal failures.</summary>
        public void WriteCrashReport(DiagnosticEntry entry)
        {
            if (entry == null) return;
            lock (_lock)
            {
                try
                {
                    EnsureFolders();
                    var ts = entry.TimestampUtc.ToString("yyyyMMdd_HHmmss");
                    var path = Path.Combine(_baseDir, "logs", "crash", $"crash_{ts}.txt");
                    var sb = new StringBuilder();
                    sb.AppendLine("# Crash Report");
                    sb.AppendLine();
                    sb.AppendLine($"Timestamp (UTC): {entry.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine($"App Version: {DiagnosticContextGatherer.AppVersion}");
                    sb.AppendLine($"OS: {Environment.OSVersion}");
                    sb.AppendLine($"Runtime: {RuntimeInformation.OSDescription}");
                    sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
                    sb.AppendLine();
                    sb.AppendLine("## Exception");
                    sb.AppendLine($"Type: {entry.ExceptionType}");
                    sb.AppendLine($"Message: {entry.Message}");
                    if (!string.IsNullOrEmpty(entry.StackTrace))
                    {
                        sb.AppendLine();
                        sb.AppendLine("StackTrace:");
                        sb.AppendLine(entry.StackTrace);
                    }
                    if (!string.IsNullOrEmpty(entry.InnerException))
                    {
                        sb.AppendLine();
                        sb.AppendLine("Inner: " + entry.InnerException);
                    }
                    sb.AppendLine();
                    sb.AppendLine("## Context");
                    sb.AppendLine($"Operation: {entry.Operation}");
                    sb.AppendLine($"Screen: {entry.ScreenContext}");
                    sb.AppendLine($"Memory: {entry.MemoryLabel}");
                    sb.AppendLine($"Device Connected: {entry.DeviceConnected}");
                    sb.AppendLine($"Left Serial: {entry.LeftSerial}");
                    sb.AppendLine($"Right Serial: {entry.RightSerial}");
                    sb.AppendLine($"Library: {entry.LibraryName}");
                    sb.AppendLine($"Firmware: {entry.FirmwareId}");
                    sb.AppendLine();
                    sb.AppendLine("## Recent Events");
                    foreach (var recent in _recentLines)
                        sb.AppendLine(recent);
                    File.WriteAllText(path, sb.ToString());
                }
                catch (Exception ex)
                {
                    try { ScanDiagnostics.WriteLine($"[DiagnosticService] Crash report failed: {ex.Message}"); } catch { }
                }
            }
        }

        private DiagnosticEntry BuildEntryFromException(string operation, DiagnosticCategory category, DiagnosticSeverity severity, Exception ex, string? screenContext, int? memoryIndex, DeviceSide? side, Dictionary<string, string>? customContext)
        {
            var unwrapped = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            var entry = new DiagnosticEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Severity = severity,
                Category = category,
                Operation = operation ?? OperationContext.Current ?? string.Empty,
                ExceptionType = unwrapped.GetType().FullName ?? "Unknown",
                Message = unwrapped.Message ?? string.Empty,
                StackTrace = unwrapped.StackTrace ?? string.Empty,
                InnerException = unwrapped.InnerException != null ? FlattenInner(unwrapped.InnerException) : string.Empty,
                CustomContext = customContext,
                UserTriggered = true
            };
            if (!string.IsNullOrEmpty(operation))
                entry.Operation = operation;
            else if (!string.IsNullOrEmpty(OperationContext.Current))
                entry.Operation = OperationContext.Current;
            DiagnosticContextGatherer.PopulateContext(entry, screenContext ?? DiagnosticContextGatherer.GetScreenContext(), memoryIndex, side);
            return entry;
        }

        private DiagnosticEntry BuildEntry(string operation, DiagnosticCategory category, DiagnosticSeverity severity, string message, string? screenContext, int? memoryIndex, DeviceSide? side, Dictionary<string, string>? customContext)
        {
            var entry = new DiagnosticEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Severity = severity,
                Category = category,
                Operation = operation ?? OperationContext.Current ?? string.Empty,
                Message = message ?? string.Empty,
                CustomContext = customContext,
                UserTriggered = true
            };
            DiagnosticContextGatherer.PopulateContext(entry, screenContext ?? DiagnosticContextGatherer.GetScreenContext(), memoryIndex, side);
            return entry;
        }

        private static string FlattenInner(Exception ex)
        {
            var parts = new List<string>();
            var current = ex;
            while (current != null)
            {
                parts.Add($"{current.GetType().Name}: {current.Message}");
                current = current.InnerException;
            }
            return string.Join(" | ", parts);
        }

        private string FormatTimelineLine(DiagnosticEntry entry, string timeStr)
        {
            var sb = new StringBuilder();
            sb.Append($"[{timeStr}] [{entry.Category}] [{entry.Severity}] {entry.Message}");
            if (!string.IsNullOrEmpty(entry.Operation))
                sb.Append($" | operation={entry.Operation}");
            if (!string.IsNullOrEmpty(entry.ScreenContext))
                sb.Append($" | screen={entry.ScreenContext}");
            if (entry.MemoryIndex.HasValue)
                sb.Append($" | memory={entry.MemoryLabel}");
            if (!string.IsNullOrEmpty(entry.DeviceSide))
                sb.Append($" | side={entry.DeviceSide}");
            if (entry.DeviceConnected.HasValue)
                sb.Append($" | connected={entry.DeviceConnected.Value}");
            if (!string.IsNullOrEmpty(entry.ExceptionType))
                sb.Append($" | exception={entry.ExceptionType}");
            return sb.ToString();
        }

        private void EnsureFolders()
        {
            foreach (var sub in new[] { "logs", "logs/diagnostics", "logs/errors", "logs/crash" })
            {
                var dir = Path.Combine(_baseDir, sub);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
        }

        private void AppendLine(string path, string line)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }

        private void AppendJsonLine(string path, DiagnosticEntry entry)
        {
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
            File.AppendAllText(path, json + Environment.NewLine);
        }

        private void AddRecent(string line)
        {
            _recentLines.Add(line);
            while (_recentLines.Count > _recentBufferSize)
                _recentLines.RemoveAt(0);
        }
    }
}
