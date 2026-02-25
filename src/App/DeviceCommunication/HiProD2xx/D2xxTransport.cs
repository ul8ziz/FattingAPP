using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FTD2XX_NET;

namespace Ul8ziz.FittingApp.App.DeviceCommunication.HiProD2xx
{
    /// <summary>
    /// Low-level D2XX read/write with retry, timeouts, and serialized access (SemaphoreSlim).
    /// Owns a single FTDI instance; only one open device at a time.
    /// </summary>
    public sealed class D2xxTransport : IDisposable
    {
        private const uint FT_PURGE_RX = 0x01;
        private const uint FT_PURGE_TX = 0x02;
        private const int PollSleepMs = 10;
        private const int WriteRetryCount = 3;

        private readonly FTDI _ftdi;
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public D2xxTransport()
        {
            _ftdi = new FTDI();
        }

        public bool IsOpen => !_disposed && _ftdi.IsOpen;

        /// <summary>
        /// Opens device by serial number (preferred) or by index. Configures timeouts, latency, USB params, purges buffers.
        /// </summary>
        public void Open(string? serialNumber, int index = 0, Action<string>? log = null)
        {
            log ??= _ => { };
            ThrowIfDisposed();

            FTDI.FT_STATUS st;
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                st = _ftdi.OpenBySerialNumber(serialNumber.Trim());
                if (st == FTDI.FT_STATUS.FT_OK)
                    log($"[D2XX] OpenBySerialNumber({serialNumber}) OK");
                else
                    throw new D2xxException((uint)st, "OpenBySerialNumber", $"FT_STATUS={st}");
            }
            else
            {
                st = _ftdi.OpenByIndex((uint)index);
                if (st != FTDI.FT_STATUS.FT_OK)
                    throw new D2xxException((uint)st, "OpenByIndex", $"index={index}");
                log($"[D2XX] OpenByIndex({index}) OK");
            }

            try
            {
                _ftdi.ResetDevice();
                _ftdi.Purge(FT_PURGE_RX | FT_PURGE_TX);
                _ftdi.SetTimeouts(5000, 5000);
                _ftdi.SetLatency(16);
                log("[D2XX] Device configured: Reset, Purge, Timeouts=5000, Latency=16");
            }
            catch (Exception ex)
            {
                try { _ftdi.Close(); } catch { }
                throw new D2xxException(0, "ConfigureAfterOpen", ex);
            }
        }

        public void Close()
        {
            if (_disposed) return;
            try
            {
                if (_ftdi.IsOpen)
                    _ftdi.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[D2XX] Close: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes data with full write verification and retries. Throws D2xxException on failure.
        /// </summary>
        public void Write(byte[] data, int offset, int length, CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || length <= 0 || offset + length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            ThrowIfDisposed();
            if (!_ftdi.IsOpen) throw new InvalidOperationException("Device not open.");

            _opLock.Wait(cancellationToken);
            try
            {
                int totalWritten = 0;
                for (int retry = 0; retry < WriteRetryCount; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint written = 0;
                    int toWrite = length - totalWritten;
                    byte[] chunk = toWrite == length && offset == 0
                        ? data
                        : data.AsSpan(offset + totalWritten, toWrite).ToArray();
                    var st = _ftdi.Write(chunk, chunk.Length, ref written);
                    if (st != FTDI.FT_STATUS.FT_OK)
                        throw new D2xxException((uint)st, "Write");
                    totalWritten += (int)written;
                    if (totalWritten >= length) return;
                }
                throw new D2xxException(0, "Write", $"Only {totalWritten} of {length} bytes written after {WriteRetryCount} retries.");
            }
            finally
            {
                _opLock.Release();
            }
        }

        /// <summary>
        /// Reads exactly bytesToRead bytes within timeoutMs, using GetQueueStatus polling. Returns when enough data or timeout.
        /// </summary>
        public byte[] ReadExact(int bytesToRead, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (bytesToRead <= 0) return Array.Empty<byte>();
            ThrowIfDisposed();
            if (!_ftdi.IsOpen) throw new InvalidOperationException("Device not open.");

            _opLock.Wait(cancellationToken);
            try
            {
                var buffer = new byte[bytesToRead];
                int totalRead = 0;
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

                while (totalRead < bytesToRead)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline)
                        throw new D2xxException(0, "ReadExact", $"Timeout after {timeoutMs}ms (got {totalRead}/{bytesToRead} bytes).");

                    uint rxBytes = 0;
                    var st = _ftdi.GetRxBytesAvailable(ref rxBytes);
                    if (st != FTDI.FT_STATUS.FT_OK)
                        throw new D2xxException((uint)st, "GetRxBytesAvailable");

                    if (rxBytes > 0)
                    {
                        uint toRead = Math.Min(rxBytes, (uint)(bytesToRead - totalRead));
                        uint read = 0;
                        var chunk = new byte[toRead];
                        st = _ftdi.Read(chunk, toRead, ref read);
                        if (st != FTDI.FT_STATUS.FT_OK)
                            throw new D2xxException((uint)st, "Read");
                        Buffer.BlockCopy(chunk, 0, buffer, totalRead, (int)read);
                        totalRead += (int)read;
                    }
                    else
                    {
                        Task.Delay(PollSleepMs, cancellationToken).GetAwaiter().GetResult();
                    }
                }
                return buffer;
            }
            finally
            {
                _opLock.Release();
            }
        }

        /// <summary>
        /// Reads available bytes up to maxBytes, waiting up to timeoutMs. Returns whatever was read (may be less than maxBytes).
        /// </summary>
        public byte[] ReadAvailable(int maxBytes, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (maxBytes <= 0) return Array.Empty<byte>();
            ThrowIfDisposed();
            if (!_ftdi.IsOpen) throw new InvalidOperationException("Device not open.");

            _opLock.Wait(cancellationToken);
            try
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var list = new System.Collections.Generic.List<byte>();

                while (list.Count < maxBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline) break;

                    uint rxBytes = 0;
                    var st = _ftdi.GetRxBytesAvailable(ref rxBytes);
                    if (st != FTDI.FT_STATUS.FT_OK)
                        throw new D2xxException((uint)st, "GetRxBytesAvailable");

                    if (rxBytes > 0)
                    {
                        uint toRead = Math.Min(rxBytes, (uint)(maxBytes - list.Count));
                        var chunk = new byte[toRead];
                        uint read = 0;
                        st = _ftdi.Read(chunk, toRead, ref read);
                        if (st != FTDI.FT_STATUS.FT_OK)
                            throw new D2xxException((uint)st, "Read");
                        for (int i = 0; i < (int)read; i++)
                            list.Add(chunk[i]);
                    }
                    else
                    {
                        Task.Delay(PollSleepMs, cancellationToken).GetAwaiter().GetResult();
                    }
                }
                return list.ToArray();
            }
            finally
            {
                _opLock.Release();
            }
        }

        /// <summary>
        /// Gets number of bytes available in RX queue.
        /// </summary>
        public uint GetRxQueueStatus()
        {
            ThrowIfDisposed();
            if (!_ftdi.IsOpen) return 0;
            uint rx = 0;
            _ftdi.GetRxBytesAvailable(ref rx);
            return rx;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Close();
            _opLock.Dispose();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(D2xxTransport));
        }
    }
}
