using System;
using System.Reflection;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services.Diagnostics
{
    /// <summary>Gathers app/session/device context for diagnostic entries.</summary>
    internal static class DiagnosticContextGatherer
    {
        private static string? _cachedVersion;
        private static string? _currentScreen;

        /// <summary>Current screen name (ConnectDevices, Fitting, Audiogram). Set by MainView on navigation.</summary>
        public static string? CurrentScreen
        {
            get => _currentScreen;
            set => _currentScreen = value;
        }

        public static string AppVersion
        {
            get
            {
                if (_cachedVersion != null)
                    return _cachedVersion;
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    _cachedVersion = attr?.InformationalVersion ?? "1.0.0";
                }
                catch
                {
                    _cachedVersion = "1.0.0";
                }
                return _cachedVersion;
            }
        }

        public static void PopulateContext(DiagnosticEntry entry, string? screenContext = null, int? memoryIndex = null, DeviceSide? side = null)
        {
            if (entry == null) return;

            if (!string.IsNullOrEmpty(screenContext))
                entry.ScreenContext = screenContext;
            if (memoryIndex.HasValue)
            {
                entry.MemoryIndex = memoryIndex.Value;
                entry.MemoryLabel = $"Memory {memoryIndex.Value + 1}";
            }
            if (side.HasValue)
                entry.DeviceSide = side.Value.ToString();

            try
            {
                var appState = AppSessionState.Instance;
                entry.DeviceConnected = appState.ConnectedLeft || appState.ConnectedRight;
                entry.LeftSerial = appState.LeftSerialId ?? string.Empty;
                entry.RightSerial = appState.RightSerialId ?? string.Empty;
                entry.FirmwareId = appState.LeftFirmwareId ?? appState.RightFirmwareId ?? string.Empty;
            }
            catch { /* ignore */ }

            try
            {
                var session = DeviceSessionService.Instance;
                if (!memoryIndex.HasValue)
                {
                    entry.MemoryIndex = session.SelectedMemoryIndex;
                    entry.MemoryLabel = $"Memory {session.SelectedMemoryIndex + 1}";
                }
            }
            catch { /* ignore */ }

            try
            {
                var mgr = FittingSessionManager.Instance;
                entry.LibraryName = mgr.ParamFileName ?? mgr.Library?.LoadedLibraryName ?? string.Empty;
            }
            catch { /* ignore */ }
        }

        public static string GetScreenContext()
        {
            return _currentScreen ?? string.Empty;
        }
    }
}
