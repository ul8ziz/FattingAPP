using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FTD2XX_NET;

namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Enumerates and opens HI-PRO programmer using FTDI D2XX only. No COM ports (SerialPort).
    /// Use ListDevices() then OpenByIndex(index) for verification; close before SDK uses the device.
    /// </summary>
    public sealed class D2xxHiProLocator : IDisposable
    {
        private const string HiProPath = @"C:\Program Files (x86)\HI-PRO";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private FTDI? _ftdi;
        private bool _disposed;
        private readonly Action<string>? _log;

        public D2xxHiProLocator(Action<string>? log = null)
        {
            _log = log ?? (s => { Debug.WriteLine(s); });
        }

        /// <summary>
        /// Ensures D2XX DLL search order: app base then HI-PRO. Call before first use.
        /// </summary>
        public static void EnsureDllSearchOrder(Action<string>? log = null)
        {
            log ??= _ => { };
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? "";
            appBase = Path.GetFullPath(appBase).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(appBase) && Directory.Exists(appBase))
            {
                SetDllDirectory(appBase);
                log($"[D2XX] SetDllDirectory(app): {appBase}");
            }
            if (Directory.Exists(HiProPath))
            {
                SetDllDirectory(HiProPath);
                log($"[D2XX] SetDllDirectory(HI-PRO): {HiProPath}");
            }
        }

        /// <summary>
        /// Returns list of D2XX devices: Index, SerialNumber, Description, Type. No COM port used.
        /// </summary>
        public IReadOnlyList<D2xxDeviceInfo> ListDevices()
        {
            ThrowIfDisposed();
            EnsureDllSearchOrder(_log);

            var ftdi = new FTDI();
            uint count = 0;
            var st = ftdi.GetNumberOfDevices(ref count);
            _log?.Invoke($"[D2XX] GetNumberOfDevices => {st}, count={count}");
            if (st != FTDI.FT_STATUS.FT_OK || count == 0)
                return Array.Empty<D2xxDeviceInfo>();

            var nodeType = typeof(FTDI).GetNestedType("FT_DEVICE_INFO_NODE")
                ?? typeof(FTDI).Assembly.GetType("FTD2XX_NET.FT_DEVICE_INFO_NODE");
            if (nodeType == null)
            {
                _log?.Invoke("[D2XX] FT_DEVICE_INFO_NODE type not found");
                return Array.Empty<D2xxDeviceInfo>();
            }

            var list = Array.CreateInstance(nodeType, (int)count);
            var getList = typeof(FTDI).GetMethod("GetDeviceList", new[] { list.GetType() });
            if (getList == null)
            {
                _log?.Invoke("[D2XX] GetDeviceList method not found");
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
            foreach (var d in result)
                _log?.Invoke($"[D2XX] Device: Index={d.Index} Serial={d.SerialNumber} Description={d.Description} Type={d.Type}");
            return result;
        }

        /// <summary>
        /// Opens device by index (0-based). Returns true if open succeeded. Call Close() before SDK discovery.
        /// </summary>
        public bool OpenByIndex(int index)
        {
            ThrowIfDisposed();
            Close();
            EnsureDllSearchOrder(_log);

            _ftdi = new FTDI();
            var st = _ftdi.OpenByIndex((uint)index);
            _log?.Invoke($"[D2XX] OpenByIndex({index}) => {st}");
            if (st != FTDI.FT_STATUS.FT_OK)
            {
                _ftdi = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Closes the opened device. Idempotent.
        /// </summary>
        public void Close()
        {
            if (_ftdi != null)
            {
                try
                {
                    if (_ftdi.IsOpen)
                        _ftdi.Close();
                }
                catch (Exception ex) { _log?.Invoke($"[D2XX] Close: {ex.Message}"); }
                _ftdi = null;
            }
        }

        /// <summary>
        /// Optional: verify device responds (e.g. queue status). No SerialPort. Call only when open.
        /// </summary>
        public bool VerifyHiProSignature()
        {
            if (_ftdi == null || !_ftdi.IsOpen) return false;
            try
            {
                uint rx = 0;
                var st = _ftdi.GetRxBytesAvailable(ref rx);
                _log?.Invoke($"[D2XX] VerifyHiProSignature GetRxBytesAvailable => {st}, rx={rx}");
                return st == FTDI.FT_STATUS.FT_OK;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[D2XX] VerifyHiProSignature: {ex.Message}");
                return false;
            }
        }

        public bool IsOpen => _ftdi?.IsOpen ?? false;

        public void Dispose()
        {
            if (_disposed) return;
            Close();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(D2xxHiProLocator));
        }

        private static D2xxDeviceInfo ToDeviceInfo(object node, int index)
        {
            var t = node.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            string GetStr(string name)
            {
                var p = t.GetProperty(name, flags);
                var f = t.GetField(name, flags);
                return (p?.GetValue(node) ?? f?.GetValue(node))?.ToString() ?? "";
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
    }
}
