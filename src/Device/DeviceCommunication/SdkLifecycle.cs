using System.Diagnostics;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>Global SDK lifecycle state. Prevents init during teardown and re-entrancy.</summary>
    public enum SdkLifecycleState
    {
        Uninitialized,
        Initializing,
        Ready,
        Disposing,
        Disposed
    }

    /// <summary>Single source of truth for SDK lifecycle. Used by DeviceSessionService and ConnectDevicesView.</summary>
    public static class SdkLifecycle
    {
        private static volatile SdkLifecycleState _state = SdkLifecycleState.Uninitialized;

        public static SdkLifecycleState State => _state;

        public static bool IsReady => _state == SdkLifecycleState.Ready;
        public static bool IsDisposingOrDisposed => _state == SdkLifecycleState.Disposing || _state == SdkLifecycleState.Disposed;
        public static bool CanInitialize => _state == SdkLifecycleState.Uninitialized || _state == SdkLifecycleState.Disposed;

        public static void SetState(SdkLifecycleState value)
        {
            _state = value;
            Debug.WriteLine($"[SdkLifecycle] state={value}");
        }
    }
}
