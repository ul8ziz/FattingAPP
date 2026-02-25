using System;
using System.IO;
using System.Windows;
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
    }
}
