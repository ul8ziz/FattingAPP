using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ul8ziz.FittingApp.App.Services.Diagnostics;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.App.Helpers;
using Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx;

namespace Ul8ziz.FittingApp.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Startup: D2XX-first loader (SetDllDirectory, EnsureD2xxLoaded), then legacy COM2 guard only for CTK fallback.
        /// Wired scan uses D2XX (HiProService); COM2 guard is not required for D2XX detection.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Register DiagnosticBridge so Device layer can record diagnostics
            var diag = DiagnosticService.Instance;
            DiagnosticBridge.RecordExceptionCallback = (op, cat, ex, msg) => diag.RecordFromBridge(op, cat, ex, msg);
            DiagnosticBridge.RecordWarningCallback = (op, cat, msg) => diag.RecordWarningFromBridge(op, cat, msg);

            // Global exception handlers for backend diagnostics
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // D2XX-first: configure DLL search and verify ftd2xx/FTD2XX_NET load
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            void D2xxLog(string msg)
            {
                System.Diagnostics.Debug.WriteLine(msg);
                ScanDiagnostics.WriteLine(msg);
            }
            D2xxLoader.ConfigureDllSearchPaths(D2xxLog);
            var resolved = D2xxLoader.ResolvedFtd2xxPath;
            D2xxLog($"[Startup] ftd2xx.dll resolved: {(resolved != null && File.Exists(resolved) ? resolved : "(not found)")}");
            var loaded = D2xxLoader.EnsureD2xxLoaded(D2xxLog);
            D2xxLog($"[Startup] D2XX loaded: {loaded}");

            SdkConfiguration.SetAppDllDirectoryFirst();
            DiagnosticRunner.LogLoadedModulePaths();
            if (Environment.Is64BitProcess)
                ScanDiagnostics.WriteLine("WARNING: Process is 64-bit; HI-PRO/FTDI DLLs are x86. Set PlatformTarget to x86.");

            DiagnosticRunner.RunDiagnosticsAsync(AppDomain.CurrentDomain.BaseDirectory, autoStopStarkeyInspire: null);

            // No COM2 check at startup: wired scan uses D2XX only; COM port state is irrelevant for HI-PRO detection.
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var screen = DiagnosticContextGatherer.GetScreenContext();
                DiagnosticService.Instance.RecordCritical("DispatcherUnhandled", DiagnosticCategory.UnhandledException, e.Exception, screen);
                ScanDiagnostics.WriteLine($"[Diagnostics] UI unhandled exception: {e.Exception?.Message}");
            }
            catch { /* ignore */ }
            // Preserve default behavior: e.Handled stays false so app can crash if needed
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    var screen = DiagnosticContextGatherer.GetScreenContext();
                    DiagnosticService.Instance.RecordCritical("AppDomainUnhandled", DiagnosticCategory.UnhandledException, ex, screen);
                    ScanDiagnostics.WriteLine($"[Diagnostics] AppDomain unhandled (IsTerminating={e.IsTerminating}): {ex.Message}");
                }
            }
            catch { /* ignore */ }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var ex = e.Exception?.GetBaseException() ?? e.Exception;
                if (ex != null)
                {
                    var screen = DiagnosticContextGatherer.GetScreenContext();
                    DiagnosticService.Instance.RecordCritical("UnobservedTask", DiagnosticCategory.UnhandledException, ex, screen);
                    ScanDiagnostics.WriteLine($"[Diagnostics] Unobserved task exception: {ex.Message}");
                }
                e.SetObserved();
            }
            catch { /* ignore */ }
        }
    }
}
