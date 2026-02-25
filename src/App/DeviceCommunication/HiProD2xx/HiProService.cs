using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FTD2XX_NET;

namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Public API for HI-PRO over D2XX: enumerate, connect, disconnect, send/receive. Single open device; diagnostics events.
    /// </summary>
    public sealed class HiProService
    {
        private readonly SemaphoreSlim _instanceLock = new SemaphoreSlim(1, 1);
        private D2xxTransport? _transport;
        private readonly List<string> _diagnosticsLog = new List<string>();
        private readonly object _logLock = new object();

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnDiagnostics;

        public bool IsOpen => _transport?.IsOpen ?? false;

        private void Log(string message)
        {
            lock (_logLock)
            {
                _diagnosticsLog.Add($"[{DateTime.UtcNow:O}] {message}");
                OnDiagnostics?.Invoke(this, message);
            }
        }

        /// <summary>
        /// Enumerate D2XX devices. Does not require COM ports; works when COM is disabled.
        /// </summary>
        public async Task<IReadOnlyList<D2xxDeviceInfo>> ListDevicesAsync(CancellationToken cancellationToken = default)
        {
            await _instanceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ftdi = new FTDI();
                uint count = 0;
                var st = ftdi.GetNumberOfDevices(ref count);
                if (st != FTDI.FT_STATUS.FT_OK)
                {
                    Log($"ListDevices: GetNumberOfDevices failed: {st}");
                    return Array.Empty<D2xxDeviceInfo>();
                }
                if (count == 0)
                    return Array.Empty<D2xxDeviceInfo>();

                var nodeType = typeof(FTDI).GetNestedType("FT_DEVICE_INFO_NODE")
                    ?? typeof(FTDI).Assembly.GetType("FTD2XX_NET.FT_DEVICE_INFO_NODE");
                if (nodeType == null)
                {
                    Log("ListDevices: FT_DEVICE_INFO_NODE type not found");
                    return Array.Empty<D2xxDeviceInfo>();
                }

                var list = Array.CreateInstance(nodeType, (int)count);
                var getList = typeof(FTDI).GetMethod("GetDeviceList", new[] { list.GetType() });
                if (getList == null)
                {
                    Log("ListDevices: GetDeviceList method not found");
                    return Array.Empty<D2xxDeviceInfo>();
                }
                getList.Invoke(ftdi, new object[] { list });

                var result = new List<D2xxDeviceInfo>();
                for (int i = 0; i < (int)count; i++)
                {
                    var node = list.GetValue(i);
                    if (node == null) continue;
                    result.Add(ToDeviceInfo(node, i));
                }
                return result;
            }
            finally
            {
                _instanceLock.Release();
            }
        }

        private static D2xxDeviceInfo ToDeviceInfo(object node, int index)
        {
            var t = node.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            string GetStr(string name)
            {
                var p = t.GetProperty(name, flags);
                var f = t.GetField(name, flags);
                var v = p?.GetValue(node) ?? f?.GetValue(node);
                return v?.ToString() ?? "";
            }
            uint GetUInt(string name)
            {
                var p = t.GetProperty(name, flags);
                var f = t.GetField(name, flags);
                var v = p?.GetValue(node) ?? f?.GetValue(node);
                return v is uint u ? u : (v is int i ? (uint)i : 0);
            }
            return new D2xxDeviceInfo
            {
                Index = index,
                Description = GetStr("Description"),
                SerialNumber = GetStr("SerialNumber"),
                Type = GetUInt("Type"),
                Flags = GetUInt("Flags"),
                Id = GetUInt("ID"),
                LocId = GetUInt("LocId")
            };
        }

        /// <summary>
        /// Connect to device by serial number (preferred) or by index. Only one device open at a time.
        /// </summary>
        public async Task ConnectAsync(string? serial = null, int index = 0, CancellationToken cancellationToken = default)
        {
            await _instanceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_transport?.IsOpen == true)
                {
                    Log("ConnectAsync: already connected; disconnecting first.");
                    _transport.Close();
                    _transport.Dispose();
                    _transport = null;
                }
                _transport = new D2xxTransport();
                _transport.Open(serial, index, Log);
                OnStatusChanged?.Invoke(this, "Connected");
            }
            finally
            {
                _instanceLock.Release();
            }
        }

        /// <summary>
        /// Disconnect and close the device.
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _instanceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_transport != null)
                {
                    _transport.Close();
                    _transport.Dispose();
                    _transport = null;
                    OnStatusChanged?.Invoke(this, "Disconnected");
                }
            }
            finally
            {
                _instanceLock.Release();
            }
        }

        /// <summary>
        /// Send request and read response until at least expectedMinBytes or timeout. Uses transport read loop with cancellation.
        /// </summary>
        public async Task<byte[]> SendAndReceiveAsync(byte[] request, int expectedMinBytes, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (_transport == null || !_transport.IsOpen)
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

            await _instanceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _transport.Write(request, 0, request.Length, cancellationToken);
                var response = await Task.Run(() =>
                    _transport.ReadExact(expectedMinBytes, timeoutMs, cancellationToken), cancellationToken).ConfigureAwait(false);
                return response;
            }
            finally
            {
                _instanceLock.Release();
            }
        }

        /// <summary>
        /// Run full self-test: enumerate, open, queue status, write probe, read 500ms, close. Returns diagnostic report.
        /// </summary>
        public async Task<string> RunSelfTestAsync(CancellationToken cancellationToken = default)
        {
            var steps = new List<string>();
            void Step(string msg) { steps.Add(msg); Log(msg); }

            Step("Self-test: start");
            string? resolvedPath = D2xxLoader.ResolvedFtd2xxPath;
            bool loaded = D2xxLoader.EnsureD2xxLoaded(Step);
            IReadOnlyList<D2xxDeviceInfo>? devices = null;
            bool isOpen = false;
            uint? rxBytes = null;
            string? lastError = null;

            try
            {
                devices = await ListDevicesAsync(cancellationToken).ConfigureAwait(false);
                Step($"Enumerate: {devices?.Count ?? 0} device(s)");
                if (devices == null || devices.Count == 0)
                {
                    return HiProDiagnostics.BuildReport(resolvedPath, loaded, devices, false, null, null, null, null, null,
                        "No D2XX devices found.");
                }

                await ConnectAsync(null, 0, cancellationToken).ConfigureAwait(false);
                isOpen = IsOpen;
                Step($"Open: {isOpen}");
                if (!isOpen)
                {
                    lastError = "Open failed.";
                    return HiProDiagnostics.BuildReport(resolvedPath, loaded, devices, false, null, null, null, null, null, lastError);
                }

                if (_transport != null)
                {
                    rxBytes = _transport.GetRxQueueStatus();
                    Step($"Queue status: RX={rxBytes}");
                    var probe = new byte[] { 0x00 };
                    _transport.Write(probe, 0, 1, cancellationToken);
                    Step("Probe write: 1 byte");
                    var read = _transport.ReadAvailable(4096, 500, cancellationToken);
                    Step($"Probe read (500ms): {read?.Length ?? 0} bytes");
                }

                await DisconnectAsync(cancellationToken).ConfigureAwait(false);
                Step("Self-test: closed");
            }
            catch (Exception ex)
            {
                lastError = $"{ex.GetType().Name}: {ex.Message}";
                Step($"Self-test error: {lastError}");
                try { await DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            }

            return HiProDiagnostics.BuildReport(resolvedPath, loaded, devices, isOpen, 5000, 5000, 16, rxBytes, null, lastError);
        }

        /// <summary>
        /// Get recent diagnostics lines for UI panel.
        /// </summary>
        public IReadOnlyList<string> GetDiagnosticsSnapshot()
        {
            lock (_logLock)
                return _diagnosticsLog.ToList();
        }
    }
}
