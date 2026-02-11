# HI-PRO D2XX Communication

The app uses **FTDI D2XX** (not COM/SerialPort) to detect and communicate with the HI-PRO programmer. D2XX works even when the COM port is disabled in Device Manager.

## Setup

1. **Platform: x86 (32-bit)**  
   Build and run with `Platform=x86`. The HI-PRO/FTDI DLLs are 32-bit. Do not use AnyCPU for the app that loads them.

2. **DLL location**  
   Place **ftd2xx.dll** and **FTD2XX_NET.dll** in one of:
   - The application output folder (e.g. `src\App\bin\x86\Debug\net10.0-windows\`)
   - `C:\Program Files (x86)\HI-PRO`

   At startup the app calls `SetDllDirectory` for the app folder and for `C:\Program Files (x86)\HI-PRO`, then verifies that `ftd2xx.dll` exists and that FTD2XX_NET can be loaded.

3. **No COM port required**  
   Wired programmer scan uses D2XX enumeration only. COM2 (or any COM port) does not need to be free for detection. HiProMonitor service is not required.

## Common Errors and Fixes

| Issue | Cause | Fix |
|-------|--------|-----|
| **ftd2xx.dll not found** | DLL not in app dir or HI-PRO dir | Copy `ftd2xx.dll` and `FTD2XX_NET.dll` from HI-PRO install to `src\App\NativeDeps\HI-PRO\` and rebuild, or install HI-PRO driver so `C:\Program Files (x86)\HI-PRO` contains them. |
| **Wrong architecture** | 64-bit process loading x86 DLLs | Build with `-p:Platform=x86`. Ensure the running exe is 32-bit. |
| **GetNumberOfDevices = 0** | Driver mode (VCP vs D2XX) or device not visible | In Device Manager, ensure the HI-PRO device uses the D2XX driver (e.g. FTDIBUS) if required. Unplug/replug USB. Run **D2XX Self-Test** from the app for a full report. |
| **COM port “in use”** | Another app has the COM port open | For **wired scan** this is irrelevant: the app uses D2XX only. COM2 being busy does not block D2XX detection. You can still run “Scan Wired” and see HI-PRO if D2XX sees it. |
| **OpenByIndex / OpenBySerialNumber fails** | Device already open elsewhere, or wrong index/serial | Close other apps that might use the device (e.g. HI-PRO Configuration). Use index `0` for a single device. |

## How to Run Self-Test

1. Open the app and go to **Connect Devices**.
2. In the **D2XX Diagnostics** section, click **D2XX Self-Test**.
3. The test will:
   - Enumerate D2XX devices
   - Open the first device (index 0)
   - Query RX queue status
   - Write a 1-byte probe
   - Read any available bytes for 500 ms
   - Close the device
4. A report is shown in the diagnostics area: process arch, OS, DLL path, device list, open status, timeouts, RX/TX info, and any error.

Use this report to confirm that D2XX is loaded, devices are enumerated, and open/read/write work.

## Module Layout

- **D2xxLoader** – `SetDllDirectory`, resolve `ftd2xx.dll`, `EnsureD2xxLoaded()`.
- **D2xxDeviceInfo** – Device info from enumeration (Description, SerialNumber, Type, Flags, ID, LocId).
- **D2xxTransport** – Open/close, Write, ReadExact, ReadAvailable, GetRxQueueStatus; SemaphoreSlim for single-device access.
- **HiProProtocol** – Placeholder for protocol framing (TODO when spec is available).
- **HiProService** – Public API: `ListDevicesAsync`, `ConnectAsync`, `DisconnectAsync`, `SendAndReceiveAsync`, `RunSelfTestAsync`; events `OnStatusChanged`, `OnDiagnostics`.
- **HiProDiagnostics** – Builds the structured self-test report.

All under `src/App/DeviceCommunication/HiProD2xx/`.
