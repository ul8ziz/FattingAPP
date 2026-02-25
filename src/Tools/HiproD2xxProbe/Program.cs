// F) HiproD2xxProbe: FTDI D2XX standalone test. net10.0, x86. References FTD2XX_NET.dll from HI-PRO folder.
// Prints: GetNumberOfDevices result + count; for each device: Description, SerialNumber, ID, Type.
// Success criteria (next step):
//   If D2XX count == 0 => FTDI driver mode/conflict prevents USB-direct access; CTK cannot detect HI-PRO
//   If D2XX count > 0 but CTK fails => CTK interface selection/init/STA issue

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetDllDirectory(string lpPathName);
}

static class Program
{
    static void Main()
    {
        Console.WriteLine("========== HiproD2xxProbe (FTDI D2XX) ==========");
        Console.WriteLine($"AppBaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");

        string? logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "hipro_d2xx_probe_log.txt");
        void Log(string msg)
        {
            Console.WriteLine(msg);
            try { File.AppendAllText(logPath, msg + Environment.NewLine); } catch { }
        }

        string hipro = @"C:\Program Files (x86)\HI-PRO";
        if (Directory.Exists(hipro))
            Native.SetDllDirectory(hipro);
        try
        {
            RunD2XXProbe(Log);
        }
        catch (Exception ex)
        {
            Log($"Fatal: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Log($"Inner: {ex.InnerException.Message}");
        }

        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
    }

    static void RunD2XXProbe(Action<string> log)
    {
        Assembly? asm = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name?.IndexOf("FTD2XX", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                asm = a;
                break;
            }
        }
        if (asm == null)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string hiproPath = Path.Combine(@"C:\Program Files (x86)\HI-PRO", "FTD2XX_NET.dll");
            string localPath = Path.Combine(baseDir, "FTD2XX_NET.dll");
            string pathToLoad = File.Exists(localPath) ? localPath : (File.Exists(hiproPath) ? hiproPath : localPath);
            try
            {
                asm = Assembly.LoadFrom(pathToLoad);
            }
            catch (Exception ex)
            {
                log($"FTD2XX_NET not loaded: {ex.Message}. Copy FTD2XX_NET.dll and ftd2xx.dll to app dir or run from 'C:\\Program Files (x86)\\HI-PRO'.");
                return;
            }
        }

        Type? ftdiType = asm.GetType("FTD2XX_NET.FTDI");
        if (ftdiType == null)
        {
            log("FTDI type not found in FTD2XX_NET.");
            return;
        }

        object? ftdi = Activator.CreateInstance(ftdiType);
        if (ftdi == null)
        {
            log("Failed to create FTDI instance.");
            return;
        }

        var getNumMethod = ftdiType.GetMethod("GetNumberOfDevices", new[] { typeof(uint).MakeByRefType() });
        if (getNumMethod == null)
        {
            log("GetNumberOfDevices not found.");
            TryClose(ftdi, ftdiType);
            return;
        }

        uint count = 0;
        object[] numArgs = { count };
        getNumMethod.Invoke(ftdi, numArgs);
        count = (uint)(numArgs[0] ?? 0u);

        log($"GetNumberOfDevices result: count = {count}");
        if (count == 0)
        {
            log("SUCCESS CRITERIA: D2XX count == 0 => FTDI driver mode/conflict prevents USB-direct access; CTK cannot detect HI-PRO.");
            TryClose(ftdi, ftdiType);
            return;
        }

        Type? nodeType = asm.GetType("FTD2XX_NET.FTDI+FT_DEVICE_INFO_NODE") ?? asm.GetType("FTD2XX_NET.FT_DEVICE_INFO_NODE");
        if (nodeType == null)
        {
            log("FT_DEVICE_INFO_NODE type not found.");
            TryClose(ftdi, ftdiType);
            return;
        }

        Array? deviceList = Array.CreateInstance(nodeType, (int)count);
        var getListMethod = ftdiType.GetMethod("GetDeviceList", new[] { deviceList!.GetType() });
        if (getListMethod == null)
        {
            log("GetDeviceList not found.");
            TryClose(ftdi, ftdiType);
            return;
        }

        getListMethod.Invoke(ftdi, new object[] { deviceList });
        log($"GetDeviceList => {count} device(s). For each device: Description, SerialNumber, ID, Type:");

        for (int i = 0; i < (int)count; i++)
        {
            object? node = deviceList.GetValue(i);
            if (node == null) continue;
            string desc = GetProp(node, "Description") ?? "";
            string serial = GetProp(node, "SerialNumber") ?? "";
            string id = GetProp(node, "ID") ?? GetProp(node, "LocId") ?? "";
            string type = GetProp(node, "Type") ?? "";
            log($"  [{i}] Description={desc} SerialNumber={serial} ID={id} Type={type}");
        }

        log("SUCCESS CRITERIA: D2XX count > 0 => If CTK still fails, issue is CTK interface selection/init/STA.");
        TryClose(ftdi, ftdiType);
    }

    static string? GetProp(object obj, string name)
    {
        var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p?.GetValue(obj)?.ToString();
    }

    static void TryClose(object ftdi, Type ftdiType)
    {
        var close = ftdiType.GetMethod("Close", Type.EmptyTypes);
        try { close?.Invoke(ftdi, null); } catch { }
    }
}
