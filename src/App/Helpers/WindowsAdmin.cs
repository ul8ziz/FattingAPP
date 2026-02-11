using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Ul8ziz.FittingApp.App.Helpers
{
    /// <summary>
    /// Helper to detect whether the current process has administrator privileges.
    /// </summary>
    public static class WindowsAdmin
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, uint TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

        private const uint TOKEN_QUERY = 0x0008;
        private const uint TokenElevation = 20;

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_ELEVATION
        {
            public uint TokenIsElevated;
        }

        /// <summary>
        /// Returns true if the current process is running with administrator (elevated) privileges.
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                try
                {
                    IntPtr tokenHandle = IntPtr.Zero;
                    if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_QUERY, out tokenHandle))
                        return false;
                    try
                    {
                        uint length;
                        GetTokenInformation(tokenHandle, TokenElevation, IntPtr.Zero, 0, out length);
                        if (length == 0) return false;
                        IntPtr buffer = Marshal.AllocHGlobal((int)length);
                        try
                        {
                            if (!GetTokenInformation(tokenHandle, TokenElevation, buffer, length, out length))
                                return false;
                            var elevation = Marshal.PtrToStructure<TOKEN_ELEVATION>(buffer);
                            return elevation.TokenIsElevated != 0;
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buffer);
                        }
                    }
                    finally
                    {
                        if (tokenHandle != IntPtr.Zero)
                            CloseHandle(tokenHandle);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
