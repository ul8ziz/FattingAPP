using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Global serialization gate for ALL sdnet.dll / SDK calls.
    /// Guarantees one-at-a-time execution, no re-entrancy, and safe teardown (drain then reject).
    /// All sdnet calls MUST go through SdkGate; never call from UI thread or ThreadPool directly.
    /// </summary>
    public static class SdkGate
    {
        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static int _running;
        private static bool _disposing;
        private static bool _disposed;

        /// <summary>True after BeginDispose() and drain; no new work is accepted.</summary>
        public static bool IsDisposed => _disposed;

        /// <summary>True when disposal has started; new work will be rejected.</summary>
        public static bool IsDisposing => _disposing;

        /// <summary>Runs synchronous work on the gate. Use for SDK calls that must not run in parallel.</summary>
        public static void Run(string label, Action action)
        {
            if (_disposing || _disposed)
            {
                Debug.WriteLine($"[SdkGate] enqueue op={label} REJECTED (disposing/disposed)");
                throw new InvalidOperationException("SdkGate is disposing or disposed; no new work accepted.");
            }
            _gate.Wait();
            Interlocked.Increment(ref _running);
            var sw = Stopwatch.StartNew();
            try
            {
                Debug.WriteLine($"[SdkGate] start op={label}");
                action();
            }
            finally
            {
                sw.Stop();
                Interlocked.Decrement(ref _running);
                _gate.Release();
                Debug.WriteLine($"[SdkGate] end op={label} ms={sw.ElapsedMilliseconds}");
            }
        }

        /// <summary>Runs synchronous work and returns a value.</summary>
        public static T Run<T>(string label, Func<T> func)
        {
            if (_disposing || _disposed)
            {
                Debug.WriteLine($"[SdkGate] enqueue op={label} REJECTED (disposing/disposed)");
                throw new InvalidOperationException("SdkGate is disposing or disposed; no new work accepted.");
            }
            _gate.Wait();
            Interlocked.Increment(ref _running);
            var sw = Stopwatch.StartNew();
            try
            {
                Debug.WriteLine($"[SdkGate] start op={label}");
                return func();
            }
            finally
            {
                sw.Stop();
                Interlocked.Decrement(ref _running);
                _gate.Release();
                Debug.WriteLine($"[SdkGate] end op={label} ms={sw.ElapsedMilliseconds}");
            }
        }

        /// <summary>Runs async work on the gate. Do not use ConfigureAwait(false) inside the delegate so SDK stays on captured (STA) context.</summary>
        public static async Task RunAsync(string label, CancellationToken ct, Func<Task> asyncAction)
        {
            if (_disposing || _disposed)
            {
                Debug.WriteLine($"[SdkGate] enqueue op={label} REJECTED (disposing/disposed)");
                throw new InvalidOperationException("SdkGate is disposing or disposed; no new work accepted.");
            }
            Debug.WriteLine($"[SdkGate] enqueue op={label}");
            await _gate.WaitAsync(ct);
            Interlocked.Increment(ref _running);
            var sw = Stopwatch.StartNew();
            try
            {
                Debug.WriteLine($"[SdkGate] start op={label}");
                await asyncAction();
            }
            finally
            {
                sw.Stop();
                Interlocked.Decrement(ref _running);
                _gate.Release();
                Debug.WriteLine($"[SdkGate] end op={label} ms={sw.ElapsedMilliseconds}");
            }
        }

        /// <summary>Runs async work and returns a value.</summary>
        public static async Task<T> RunAsync<T>(string label, CancellationToken ct, Func<Task<T>> asyncFunc)
        {
            if (_disposing || _disposed)
            {
                Debug.WriteLine($"[SdkGate] enqueue op={label} REJECTED (disposing/disposed)");
                throw new InvalidOperationException("SdkGate is disposing or disposed; no new work accepted.");
            }
            Debug.WriteLine($"[SdkGate] enqueue op={label}");
            await _gate.WaitAsync(ct);
            Interlocked.Increment(ref _running);
            var sw = Stopwatch.StartNew();
            try
            {
                Debug.WriteLine($"[SdkGate] start op={label}");
                return await asyncFunc();
            }
            finally
            {
                sw.Stop();
                Interlocked.Decrement(ref _running);
                _gate.Release();
                Debug.WriteLine($"[SdkGate] end op={label} ms={sw.ElapsedMilliseconds}");
            }
        }

        /// <summary>Runs synchronous work via gate (for callers that need Task API).</summary>
        public static Task InvokeAsync(Action work, string opName)
        {
            return Task.Run(() => Run(opName, work));
        }

        /// <summary>Runs synchronous work via gate and returns result.</summary>
        public static Task<T> InvokeAsync<T>(Func<T> work, string opName)
        {
            return Task.Run(() => Run(opName, work));
        }

        /// <summary>Marks gate as disposing; new work will be rejected. Call before DrainAsync.</summary>
        public static void BeginDispose()
        {
            _disposing = true;
            Debug.WriteLine("[SdkGate] BeginDispose — rejecting new work");
        }

        /// <summary>Waits until all currently running gate work has completed. Does NOT set _disposed; use RunCleanupToDispose for that.</summary>
        public static async Task DrainAsync()
        {
            Debug.WriteLine("[SdkGate] DrainAsync — waiting for running work to complete");
            while (Volatile.Read(ref _running) != 0)
                await Task.Delay(25).ConfigureAwait(false);
            Debug.WriteLine("[SdkGate] DrainAsync complete — no work running");
        }

        /// <summary>Runs cleanup on the gate and marks disposed. Call after DrainAsync when _running==0. Use for ConnectionService.Cleanup + SdkManager.Dispose.</summary>
        public static void RunCleanupToDispose(string label, Action cleanup)
        {
            if (!_disposing)
                Debug.WriteLine("[SdkGate] RunCleanupToDispose called without BeginDispose");
            _gate.Wait();
            try
            {
                Debug.WriteLine($"[SdkGate] start op={label} (cleanup-to-dispose)");
                cleanup();
            }
            finally
            {
                _disposed = true;
                _gate.Release();
                Debug.WriteLine("[Dispose] cancel -> drain -> close -> dispose complete");
            }
        }

        /// <summary>Resets gate after full teardown so a new session can start. Use only when no SDK references remain.</summary>
        public static void ResetForNewSession()
        {
            _disposing = false;
            _disposed = false;
            Debug.WriteLine("[SdkLifecycle] state=Reset — gate ready for new session");
        }
    }
}
