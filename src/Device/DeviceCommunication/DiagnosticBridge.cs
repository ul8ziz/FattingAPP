using System;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Bridge for Device layer to record diagnostics without referencing App.
    /// App sets RecordException and RecordWarning in OnStartup.
    /// </summary>
    public static class DiagnosticBridge
    {
        /// <summary>Callback for exceptions. Parameters: operation, category, exception, messageOverride.</summary>
        public static Action<string, string, Exception, string?>? RecordExceptionCallback { get; set; }

        /// <summary>Callback for warnings. Parameters: operation, category, message.</summary>
        public static Action<string, string, string>? RecordWarningCallback { get; set; }

        /// <summary>Records an exception from Device layer. No-op if App has not registered.</summary>
        public static void RecordException(string operation, string category, Exception ex, string? messageOverride = null)
        {
            try
            {
                RecordExceptionCallback?.Invoke(operation, category, ex, messageOverride);
            }
            catch
            {
                // Do not let diagnostic recording cause secondary failures
            }
        }

        /// <summary>Records a warning from Device layer.</summary>
        public static void RecordWarning(string operation, string category, string message)
        {
            try
            {
                RecordWarningCallback?.Invoke(operation, category, message);
            }
            catch { }
        }
    }
}
