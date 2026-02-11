// FTDI D2XX direct test: confirms whether HI-PRO is visible via D2XX (USB-direct) vs VCP/COM.
// Run from HI-PRO folder or ensure ftd2xx.dll + FTD2XX_NET.dll are in app dir or HI-PRO path.
// net10.0, x86 to match the main app and HI-PRO driver.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetDllDirectory(string lpPathName);
}

// FTD2XX_NET types: namespace FTD2XX_NET, class FTDI, FT_DEVICE_INFO_NODE
// If reference not found, use reflection so the project builds without FTD2XX_NET.dll in lib\

static class Program
{
    static void Main()
    {
        Console.WriteLine("========== FTDI D2XX Direct Test (x86, net10.0) ==========");
        Console.WriteLine($"AppBaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");

        string? logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "ftd2xx_test_log.txt");
        void Log(string msg)
        {
            Console.WriteLine(msg);
            try { File.AppendAllText(logPath, msg + Environment.NewLine); } catch { }
        }

        // Prefer HI-PRO dir for ftd2xx.dll so D2XX can load
        string hipro = @"C:\Program Files (x86)\HI-PRO";
        if (Directory.Exists(hipro))
            Native.SetDllDirectory(hipro);
        try
        {
            RunD2XXTest(Log);
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

    static void RunD2XXTest(Action<string> log)
    {
        // Load FTD2XX_NET and call GetNumberOfDevices / GetDeviceList via reflection so we tolerate missing DLL
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
            string hiproPath = @"C:\Program Files (x86)\HI-PRO\FTD2XX_NET.dll";
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

        // GetNumberOfDevices(ref uint)
        var getNumMethod = ftdiType.GetMethod("GetNumberOfDevices", new[] { typeof(uint).MakeByRefType() });
        if (getNumMethod == null)
        {
            log("GetNumberOfDevices not found.");
            return;
        }

        uint count = 0;
        object[] numArgs = { count };
        object? numResult = getNumMethod.Invoke(ftdi, numArgs);
        count = (uint)(numArgs[0] ?? 0u);

        // FT_STATUS or similar may be returned; we care about count
        log($"GetNumberOfDevices => count = {count}");
        if (count == 0)
        {
            log("D2XX reports 0 devices => CTK cannot open HI-PRO via USB-direct; likely driver mode (VCP vs D2XX) or conflict.");
            TryClose(ftdi, ftdiType);
            return;
        }

        // GetDeviceList(FT_DEVICE_INFO_NODE[])
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
        log($"GetDeviceList => {count} device(s). Logging Description, SerialNumber, ID, Type:");

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

        log("D2XX lists the device => CTK should be able to open it; issue may be CTK init/interface selection/threading.");
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
