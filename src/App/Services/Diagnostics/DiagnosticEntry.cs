using System;
using System.Collections.Generic;

namespace Ul8ziz.FittingApp.App.Services.Diagnostics
{
    /// <summary>Severity level for diagnostic entries.</summary>
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>Category for diagnostic entries.</summary>
    public enum DiagnosticCategory
    {
        UnhandledException,
        SDK,
        Communication,
        Device,
        Scan,
        Connection,
        Persistence,
        Validation,
        Session,
        General
    }

    /// <summary>Structured diagnostic record for errors, warnings, and critical events.</summary>
    public sealed class DiagnosticEntry
    {
        public DateTime TimestampUtc { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public DiagnosticCategory Category { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string ScreenContext { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public string InnerException { get; set; } = string.Empty;
        public int? MemoryIndex { get; set; }
        public string MemoryLabel { get; set; } = string.Empty;
        public string DeviceSide { get; set; } = string.Empty;
        public bool? DeviceConnected { get; set; }
        public string LeftSerial { get; set; } = string.Empty;
        public string RightSerial { get; set; } = string.Empty;
        public string LibraryName { get; set; } = string.Empty;
        public string FirmwareId { get; set; } = string.Empty;
        public string ProgrammerInfo { get; set; } = string.Empty;
        public bool? UserTriggered { get; set; }
        public Dictionary<string, string>? CustomContext { get; set; }
    }
}
