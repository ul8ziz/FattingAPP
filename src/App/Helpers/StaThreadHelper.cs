using System;
using System.Threading;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>
    /// Runs a delegate on a dedicated STA thread. Required for CTK/SDNET COM-based APIs
    /// to avoid E_INVALID_STATE when calls are made from MTA/ThreadPool.
    /// </summary>
    public static class StaThreadHelper
    {
        /// <summary>
        /// Runs func on a dedicated STA thread and returns the result. Blocks until complete.
        /// </summary>
        public static T RunOnStaThread<T>(Func<T> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            T? result = default;
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (captured != null)
                throw new InvalidOperationException("STA thread threw.", captured);

            return result!;
        }

        /// <summary>
        /// Runs action on a dedicated STA thread. Blocks until complete.
        /// </summary>
        public static void RunOnStaThread(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RunOnStaThread<object?>(() => { action(); return null; });
        }
    }
}
