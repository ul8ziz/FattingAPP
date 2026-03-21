using System;
using System.Threading;

namespace Ul8ziz.FittingApp.App.Services.Diagnostics
{
    /// <summary>Lightweight operation context for attaching active operation name to diagnostic entries.</summary>
    public static class OperationContext
    {
        private static readonly AsyncLocal<string?> _currentOperation = new();

        /// <summary>Current operation name, or null if none set.</summary>
        public static string? Current => _currentOperation.Value;

        /// <summary>Sets the current operation. Returns a disposable that clears it on dispose.</summary>
        public static IDisposable Begin(string operationName)
        {
            if (string.IsNullOrEmpty(operationName))
                return NullDisposable.Instance;

            var previous = _currentOperation.Value;
            _currentOperation.Value = operationName;
            return new Scope(() => _currentOperation.Value = previous);
        }

        /// <summary>Sets the current operation. Call Clear when done.</summary>
        public static void Set(string operationName)
        {
            _currentOperation.Value = string.IsNullOrEmpty(operationName) ? null : operationName;
        }

        /// <summary>Clears the current operation.</summary>
        public static void Clear()
        {
            _currentOperation.Value = null;
        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            private int _disposed;

            public Scope(Action onDispose)
            {
                _onDispose = onDispose ?? (() => { });
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    _onDispose();
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
