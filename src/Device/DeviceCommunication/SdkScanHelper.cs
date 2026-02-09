using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using SDLib;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Helpers for HI-PRO scan: SDK communication interface enumeration (Strategy A) and TryCheckDevice.
    /// All methods are static and safe to call from the scan STA thread.
    /// </summary>
    public static class SdkScanHelper
    {
        /// <summary>
        /// Gets the list of communication interface strings from the SDK if available (Strategy A).
        /// Uses reflection for GetCommunicationInterfaceCount / GetCommunicationInterfaceString.
        /// Prefers entries containing "HI-PRO" or VID 0C33 / PID 0012.
        /// Returns empty list if SDK does not expose these methods.
        /// </summary>
        public static IReadOnlyList<string> GetSdkCommunicationInterfaces(IProductManager productManager)
        {
            var list = new List<string>();
            if (productManager == null) return list;
            try
            {
                var type = productManager.GetType();
                var countMethod = type.GetMethod("GetCommunicationInterfaceCount", BindingFlags.Public | BindingFlags.Instance);
                var stringMethod = type.GetMethod("GetCommunicationInterfaceString", BindingFlags.Public | BindingFlags.Instance);
                if (countMethod == null || stringMethod == null)
                    return list;
                if (countMethod.GetParameters().Length != 0)
                    return list;
                var countParam = stringMethod.GetParameters();
                if (countParam.Length != 1 || countParam[0].ParameterType != typeof(int))
                    return list;

                var count = (int)(countMethod.Invoke(productManager, null) ?? 0);
                for (int i = 0; i < count; i++)
                {
                    var s = stringMethod.Invoke(productManager, new object[] { i })?.ToString();
                    if (!string.IsNullOrEmpty(s))
                        list.Add(s);
                }
                // Prefer HI-PRO / VID_0C33 PID_0012
                if (list.Count > 1)
                {
                    var preferred = list.Where(x =>
                        x.IndexOf("HI-PRO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (x.IndexOf("0C33", StringComparison.OrdinalIgnoreCase) >= 0 && x.IndexOf("0012", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    if (preferred.Count > 0)
                    {
                        list = preferred.Concat(list.Except(preferred)).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SdkScanHelper] GetSdkCommunicationInterfaces: {ex.Message}");
                ScanDiagnostics.WriteLine($"[SdkScanHelper] GetSdkCommunicationInterfaces: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Tries to create a communication interface with the given settings, call CheckDevice, then close.
        /// Caller must ensure this runs on the dedicated STA thread and one attempt at a time.
        /// </summary>
        /// <param name="productManager">Product manager (same thread as caller).</param>
        /// <param name="programmerName">e.g. Constants.HiPro.</param>
        /// <param name="settings">Interface string or e.g. "port=COM2" or "port=2".</param>
        /// <param name="errorMessage">Detailed error if not found.</param>
        /// <returns>True if CheckDevice returned true.</returns>
        public static bool TryCheckDevice(
            IProductManager productManager,
            string programmerName,
            string settings,
            out string errorMessage)
        {
            errorMessage = "";
            ICommunicationAdaptor? adaptor = null;
            try
            {
                adaptor = productManager.CreateCommunicationInterface(
                    programmerName,
                    CommunicationPort.kLeft,
                    settings ?? "");
                if (adaptor == null)
                {
                    errorMessage = "CreateCommunicationInterface returned null";
                    return false;
                }
                bool found = adaptor.CheckDevice();
                if (found)
                    return true;
                errorMessage = "CheckDevice returned false";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"{ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                return false;
            }
            finally
            {
                if (adaptor != null)
                {
                    try { adaptor.CloseDevice(); }
                    catch { /* ignore */ }
                }
            }
        }
    }
}
