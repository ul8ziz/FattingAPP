using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Single gate for device initialization/configuration per Sound Designer Programmer's Guide.
    /// Ensures InitializeDevice (and optionally ConfigureDevice when InitializeDevice returns false) is run
    /// before any ReadParameters/WriteParameters. All SDK access is serialized via SdkGate.
    /// Call from UI/STA thread so sdnet.dll COM objects stay on the correct thread.
    /// </summary>
    public static class DeviceInitializationService
    {
        private static readonly SoundDesignerService _soundDesigner = new SoundDesignerService();

        /// <summary>
        /// Ensures the device for the given side is initialized and configured.
        /// If already configured, returns immediately. Otherwise runs InitializeDevice(adaptor) inside SdkGate.
        /// If InitializeDevice returns false, attempts ConfigureDevice (manufacturing) then re-initialize; on failure sets LastConfigError.
        /// On E_UNCONFIGURED_DEVICE from SDK, sets IsConfigured=false and LastConfigError.
        /// </summary>
        /// <param name="session">Active session (must have SdkManager and ConnectionService).</param>
        /// <param name="side">Side to ensure (Left or Right).</param>
        /// <param name="ct">Cancellation.</param>
        /// <exception cref="InvalidOperationException">When not connected or no product/adaptor.</exception>
        public static async Task EnsureInitializedAndConfiguredAsync(
            DeviceSessionService session,
            DeviceSide side,
            CancellationToken ct = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (!session.IsDeviceConnected)
            {
                Log("[EnsureInit] Step failed: device not connected.");
                ScanDiagnostics.WriteLine("[EnsureInit] Step failed: device not connected.");
                throw new InvalidOperationException("Device is not connected. Connect first.");
            }

            var product = session.SdkManager?.GetProduct();
            var adaptor = session.ConnectionService?.GetConnection(side);
            if (product == null || adaptor == null)
            {
                Log($"[EnsureInit] Step failed: no product or adaptor for {side}.");
                ScanDiagnostics.WriteLine($"[EnsureInit] Step failed: no product or adaptor for {side}.");
                throw new InvalidOperationException($"No product or adaptor for {side}. Reconnect and try again.");
            }

            await SdkGate.RunAsync($"EnsureInitializedAndConfigured_{side}", ct, async () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // If already marked configured, verify with one ReadParameters probe (catches E_UNCONFIGURED_DEVICE for unprogrammed devices).
                    if (session.IsSideConfigured(side))
                    {
                        try
                        {
                            var monitor = product.BeginReadParameters(ParameterSpace.kActiveMemory);
                            while (!monitor.IsFinished)
                            {
                                ct.ThrowIfCancellationRequested();
                                await Task.Delay(50, ct);
                            }
                            monitor.GetResult();
                            sw.Stop();
                            System.Diagnostics.Debug.WriteLine($"[Perf] EnsureInit {side} probe OK ms={sw.ElapsedMilliseconds}");
                            Log($"[EnsureInit] {side} already configured; probe OK.");
                            ScanDiagnostics.WriteLine($"[EnsureInit] {side} already configured; probe OK.");
                            return;
                        }
                        catch (Exception ex)
                        {
                            var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                            var msg = inner?.Message ?? ex.Message ?? "";
                            if (msg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                session.SetSideConfigured(side, false);
                                session.SetLastConfigError("E_UNCONFIGURED_DEVICE: The device must be configured before its parameters may be read or written.");
                                Log($"[EnsureInit] {side}: probe failed with E_UNCONFIGURED_DEVICE — treating as not configured.");
                                ScanDiagnostics.WriteLine($"[EnsureInit] {side}: probe failed with E_UNCONFIGURED_DEVICE — treating as not configured.");
                                return;
                            }
                            throw;
                        }
                    }

                    Log($"[EnsureInit] {side}: running InitializeDevice...");
                    ScanDiagnostics.WriteLine($"[EnsureInit] {side}: running InitializeDevice...");

                    bool isConfigured = await _soundDesigner.InitializeDeviceAsync(product, adaptor, ct);

                    session.SetSideInitialized(side, true);

                    if (isConfigured)
                    {
                        // InitializeDevice can return true for unprogrammed devices; ReadParameters then throws E_UNCONFIGURED_DEVICE.
                        // Probe once so we only set configured=true when read actually works.
                        try
                        {
                            var monitor = product.BeginReadParameters(ParameterSpace.kActiveMemory);
                            while (!monitor.IsFinished)
                            {
                                ct.ThrowIfCancellationRequested();
                                await Task.Delay(50, ct);
                            }
                            monitor.GetResult();
                            session.SetSideConfigured(side, true);
                            session.SetLastConfigError(null);
                            Log($"[EnsureInit] {side}: InitializeDevice + ReadParameters probe OK — configured.");
                            ScanDiagnostics.WriteLine($"[EnsureInit] {side}: InitializeDevice + ReadParameters probe OK — configured.");
                            return;
                        }
                        catch (Exception readEx)
                        {
                            var readInner = readEx is TargetInvocationException rt ? rt.InnerException : readEx;
                            var readMsg = readInner?.Message ?? readEx.Message ?? "";
                            if (readMsg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                session.SetSideConfigured(side, false);
                                session.SetLastConfigError("E_UNCONFIGURED_DEVICE: The device must be configured before its parameters may be read or written.");
                                Log($"[EnsureInit] {side}: InitializeDevice returned true but ReadParameters threw E_UNCONFIGURED_DEVICE — treating as not configured.");
                                ScanDiagnostics.WriteLine($"[EnsureInit] {side}: InitializeDevice returned true but ReadParameters threw E_UNCONFIGURED_DEVICE — treating as not configured.");
                                return;
                            }
                            throw;
                        }
                    }

                    Log($"[EnsureInit] {side}: InitializeDevice returned false; device not configured.");
                    ScanDiagnostics.WriteLine($"[EnsureInit] {side}: InitializeDevice returned false; device not configured.");

                    session.SetSideConfigured(side, false);
                    session.SetLastConfigError("InitializeDevice returned false. Device must be configured (e.g. via manufacturing tools) before Read/Write.");

                    // Optional: ConfigureDevice (manufacturing) per Programmer's Guide Section 9.4.
                    // SDK may expose BeginConfigureDevice / EndConfigureDevice on IProduct; if not, use manufacturing tool.
                    bool configureAttempted = TryConfigureDevice(product, adaptor, side, session, ct);
                    if (configureAttempted)
                    {
                        Log($"[EnsureInit] {side}: re-running InitializeDevice after ConfigureDevice...");
                        ScanDiagnostics.WriteLine($"[EnsureInit] {side}: re-running InitializeDevice after ConfigureDevice...");
                        isConfigured = await _soundDesigner.InitializeDeviceAsync(product, adaptor, ct);
                        if (isConfigured)
                        {
                            try
                            {
                                var monitor = product.BeginReadParameters(ParameterSpace.kActiveMemory);
                                while (!monitor.IsFinished) { ct.ThrowIfCancellationRequested(); await Task.Delay(50, ct); }
                                monitor.GetResult();
                                session.SetSideConfigured(side, true);
                                session.SetLastConfigError(null);
                                Log($"[EnsureInit] {side}: re-InitializeDevice + probe OK — configured.");
                                ScanDiagnostics.WriteLine($"[EnsureInit] {side}: re-InitializeDevice + probe OK — configured.");
                            }
                            catch (Exception reReadEx)
                            {
                                var reMsg = (reReadEx is TargetInvocationException rte ? rte.InnerException : reReadEx)?.Message ?? reReadEx.Message ?? "";
                                if (reMsg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    session.SetSideConfigured(side, false);
                                    session.SetLastConfigError("E_UNCONFIGURED_DEVICE: The device must be configured before its parameters may be read or written.");
                                }
                            }
                        }
                        else
                        {
                            session.SetLastConfigError("ConfigureDevice ran but InitializeDevice still returned false. Device may require manufacturing programming.");
                            Log($"[EnsureInit] {side}: re-InitializeDevice still false.");
                            ScanDiagnostics.WriteLine($"[EnsureInit] {side}: re-InitializeDevice still false.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                    var msg = inner?.Message ?? ex.Message ?? "";
                    Log($"[EnsureInit] {side}: exception — {msg}");
                    ScanDiagnostics.WriteLine($"[EnsureInit] {side}: exception — {msg}");

                    if (msg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        session.SetSideConfigured(side, false);
                        session.SetLastConfigError("E_UNCONFIGURED_DEVICE: The device must be configured before its parameters may be read or written.");
                        Log($"[EnsureInit] {side}: E_UNCONFIGURED_DEVICE — treating as not configured.");
                        ScanDiagnostics.WriteLine($"[EnsureInit] {side}: E_UNCONFIGURED_DEVICE — treating as not configured.");
                        return;
                    }

                    session.SetLastConfigError(msg);
                    throw;
                }
            }).ConfigureAwait(true);
        }

        /// <summary>
        /// Attempts ConfigureDevice (manufacturing) if the SDK exposes it. Returns true if the call was attempted and validation passed.
        /// </summary>
        private static bool TryConfigureDevice(
            IProduct product,
            ICommunicationAdaptor adaptor,
            DeviceSide side,
            DeviceSessionService session,
            CancellationToken ct)
        {
            return RunConfigureOneSideSync(product, adaptor, side, session, null, ct);
        }

        /// <summary>
        /// Configured = ReadParameters accepted; ConfigureDevice is a manufacturing step (Programmer's Guide Section 9.4)
        /// that writes a full parameter set to an unprogrammed device so that ReadParameters/WriteParameters then succeed.
        /// Runs ConfigureDevice (manufacturing) for each connected side that is not yet configured, with library match,
        /// param application, SDK ConfigureDevice call, and validation. Entire flow runs inside SdkGate (one-at-a-time).
        /// Call from UI when user clicks "Configure Device"; caller must set SetConfigureRunning(true) before and false after.
        /// </summary>
        public static async Task RunConfigureDeviceAsync(DeviceSessionService session, IProgress<string>? progress, CancellationToken ct = default)
        {
            if (session == null || !session.IsDeviceConnected)
                return;
            var product = session.SdkManager?.GetProduct();
            if (product == null) return;

            ConfigureDeviceLog("START");
            progress?.Report("Configuring device…");

            await SdkGate.RunAsync("ConfigureDevice", ct, async () =>
            {
                try
                {
                    if (!EnsureLibraryMatchesFirmware(session))
                        return;

                    var firmwareId = session.DeviceFirmwareId ?? "?";
                    var leftSerial = session.LeftSerial ?? "-";
                    var rightSerial = session.RightSerial ?? "-";
                    var libName = session.SdkManager?.LoadedFirmwareId != null
                        ? session.SdkManager.LoadedFirmwareId + ".library"
                        : "?";
                    ConfigureDeviceLog($"using side=Left/Right firmware={firmwareId} serial L={leftSerial} R={rightSerial} library={libName}");

                    bool anySuccess = false;
                    if (session.LeftConnected && !session.IsSideConfigured(DeviceSide.Left))
                    {
                        var adaptor = session.ConnectionService?.GetConnection(DeviceSide.Left);
                        if (adaptor != null)
                        {
                            progress?.Report("Configuring Left…");
                            bool ok = RunConfigureOneSideSync(product, adaptor, DeviceSide.Left, session, progress, ct);
                            if (ok) anySuccess = true;
                        }
                    }
                    if (session.RightConnected && !session.IsSideConfigured(DeviceSide.Right) && !ct.IsCancellationRequested)
                    {
                        var adaptor = session.ConnectionService?.GetConnection(DeviceSide.Right);
                        if (adaptor != null)
                        {
                            progress?.Report("Configuring Right…");
                            bool ok = RunConfigureOneSideSync(product, adaptor, DeviceSide.Right, session, progress, ct);
                            if (ok) anySuccess = true;
                        }
                    }

                    if (!anySuccess && (session.LeftConnected && !session.IsSideConfigured(DeviceSide.Left) || session.RightConnected && !session.IsSideConfigured(DeviceSide.Right)))
                    {
                        if (string.IsNullOrEmpty(session.LastConfigError))
                            session.SetLastConfigError("Configure failed or SDK does not expose ConfigureDevice. Use vendor manufacturing tools.");
                        ConfigureDeviceLog("END (no side configured)");
                    }
                    else
                        ConfigureDeviceLog("END");
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                    var msg = inner?.Message ?? ex.Message ?? "";
                    ConfigureDeviceLog($"END exception: {msg}");
                    ScanDiagnostics.WriteLine($"[ConfigureDevice] END exception: {msg}");
                    session.SetLastConfigError(msg);
                }
            }).ConfigureAwait(true);

            if (session.IsConfigured)
                progress?.Report("Device configured. Reading settings…");
        }

        private static void ConfigureDeviceLog(string message)
        {
            var line = $"[ConfigureDevice] {DateTime.Now:HH:mm:ss.fff} {message}";
            Debug.WriteLine(line);
            ScanDiagnostics.WriteLine(line);
        }

        private static bool EnsureLibraryMatchesFirmware(DeviceSessionService session)
        {
            var firmwareId = session.DeviceFirmwareId;
            if (string.IsNullOrWhiteSpace(firmwareId))
            {
                session.SetLastConfigError("Device firmware unknown. Connect first.");
                ConfigureDeviceLog("blocked: firmware unknown");
                return false;
            }

            var sm = session.SdkManager;
            if (sm == null)
            {
                session.SetLastConfigError("SDK not available.");
                return false;
            }

            if (string.Equals(sm.LoadedFirmwareId, firmwareId, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                sm.ReloadForFirmware(firmwareId);
                ConfigureDeviceLog($"library reloaded for firmware={firmwareId}");
                return true;
            }
            catch (Exception ex)
            {
                var msg = $"Library mismatch: need {firmwareId}. {ex.Message}";
                session.SetLastConfigError(msg);
                ConfigureDeviceLog($"blocked: {msg}");
                return false;
            }
        }

        private static void ApplyParamTemplateIfAvailable(IProduct product, string firmwareId, IProgress<string>? progress)
        {
            var libraryFileName = firmwareId + ".library";
            var paramPath = ParamFileService.FindParamForLibrary(libraryFileName);
            if (string.IsNullOrEmpty(paramPath) || !File.Exists(paramPath))
            {
                ConfigureDeviceLog("APPLY_PARAM: no .param file found");
                return;
            }

            try
            {
                // Use sync load to avoid deadlock: we are inside SdkGate on STA thread; GetAwaiter().GetResult() on LoadAsync can block forever.
                var paramFile = ParamFileService.Load(paramPath);
                if (paramFile == null) return;
                progress?.Report("Applying parameter template…");
                ConfigureDeviceLog("APPLY_PARAM: loading and applying template");
                int applied = LibraryService.ApplyParamToProduct(product, paramFile, 0);
                ConfigureDeviceLog($"APPLY_PARAM: applied {applied} parameters (memory 0)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigureDevice] APPLY_PARAM failed: {ex.Message}");
                ScanDiagnostics.WriteLine($"[ConfigureDevice] APPLY_PARAM failed: " + ex.Message);
            }
        }

        private static bool RunConfigureOneSideSync(
            IProduct product,
            ICommunicationAdaptor adaptor,
            DeviceSide side,
            DeviceSessionService session,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var firmwareId = session.DeviceFirmwareId ?? "";
            var serial = side == DeviceSide.Left ? session.LeftSerial : session.RightSerial;
            ConfigureDeviceLog($"using side={side} firmware={firmwareId} serial={serial} library={session.SdkManager?.LoadedFirmwareId ?? "?"}");

            ApplyParamTemplateIfAvailable(product, firmwareId, progress);

            progress?.Report("Calling ConfigureDevice…");
            ConfigureDeviceLog("CALL ConfigureDevice ...");

            object? monitor = null;
            try
            {
                var productType = product.GetType();
                var beginMethod = productType.GetMethod("BeginConfigureDevice", new[] { typeof(ICommunicationAdaptor) });
                if (beginMethod != null)
                {
                    monitor = beginMethod.Invoke(product, new object[] { adaptor });
                    if (monitor != null)
                    {
                        var isFinishedProp = monitor.GetType().GetProperty("IsFinished", BindingFlags.Public | BindingFlags.Instance);
                        while (isFinishedProp != null && !(bool)(isFinishedProp.GetValue(monitor) ?? false))
                        {
                            ct.ThrowIfCancellationRequested();
                            Task.Delay(50, ct).GetAwaiter().GetResult();
                        }
                        var getResultMethod = monitor.GetType().GetMethod("GetResult", Type.EmptyTypes);
                        if (getResultMethod != null)
                            getResultMethod.Invoke(monitor, null);
                        var endMethod = productType.GetMethod("EndConfigureDevice", new[] { monitor.GetType() });
                        if (endMethod != null)
                            endMethod.Invoke(product, new object[] { monitor });
                    }
                }
                else
                {
                    var syncMethod = productType.GetMethod("ConfigureDevice", new[] { typeof(ICommunicationAdaptor) });
                    if (syncMethod != null)
                        syncMethod.Invoke(product, new object[] { adaptor });
                    else
                    {
                        session.SetLastConfigError("SDK does not expose ConfigureDevice. Use vendor manufacturing tools to configure the device.");
                        ConfigureDeviceLog("CALL ConfigureDevice: API not found");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                var msg = inner?.Message ?? ex.Message ?? "";
                ConfigureDeviceLog($"CALL ConfigureDevice failed: {msg}");
                ScanDiagnostics.WriteLine($"[ConfigureDevice] CALL ConfigureDevice failed: " + msg);
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(session.SdkManager?.ProductManager, ex);
                session.SetLastConfigError(msg);
                session.SetSideConfigured(side, false);
                return false;
            }

            progress?.Report("Validating…");
            ConfigureDeviceLog("VALIDATE ReadParameters ...");

            bool validated = ValidateConfiguredWithReadParameters(product, ct);
            if (validated)
            {
                session.SetSideConfigured(side, true);
                session.SetLastConfigError(null);
                ConfigureDeviceLog("VALIDATE ReadParameters ... success");
                return true;
            }

            session.SetSideConfigured(side, false);
            session.SetLastConfigError("ConfigureDevice ran but ReadParameters still failed (E_UNCONFIGURED_DEVICE). Device may need manufacturing tools.");
            ConfigureDeviceLog("VALIDATE ReadParameters ... fail");
            return false;
        }

        /// <summary>Tries ReadParameters for kSystemActiveMemory then kActiveMemory; returns true if one succeeds without E_UNCONFIGURED_DEVICE.</summary>
        private static bool ValidateConfiguredWithReadParameters(IProduct product, CancellationToken ct)
        {
            var spacesToTry = new[] { ParameterSpace.kSystemActiveMemory, ParameterSpace.kActiveMemory };
            foreach (var space in spacesToTry)
            {
                try
                {
                    var monitor = product.BeginReadParameters(space);
                    while (!monitor.IsFinished)
                    {
                        ct.ThrowIfCancellationRequested();
                        Task.Delay(50, ct).GetAwaiter().GetResult();
                    }
                    monitor.GetResult();
                    return true;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                    var msg = inner?.Message ?? ex.Message ?? "";
                    if (msg.IndexOf("E_UNCONFIGURED_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    throw;
                }
            }
            return false;
        }

        private static void Log(string message)
        {
            Debug.WriteLine(message);
        }
    }
}
