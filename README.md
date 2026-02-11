# Ul8ziz.FittingApp

A Windows desktop application for hearing aid fitting, built with C# and WPF. The application integrates with the Ezairo Sound Designer SDK to communicate with hearing aid devices through wired programmers such as HI-PRO.

## Overview

Ul8ziz.FittingApp provides a modern, maintainable codebase for hearing aid fitting workflows. The solution follows a layered architecture that separates UI, business logic, device communication, and data persistence, enabling clean integration with the Ezairo Sound Designer SDK.

## Key Capabilities

- WPF-based user interface for fitting workflows
- Integration layer for Ezairo Sound Designer SDK
- Support for wired programmers (HI-PRO, DSP3, CAA, Promira)
- Patient data management and session tracking
- Modular architecture for maintainability and testing

## Technology Stack

- **.NET 10** with Windows Presentation Foundation (WPF) (required by current sdnet.dll; see note below)
- **C#** for application logic
- **Ezairo Sound Designer SDK** for device communication
- **SQLite** for local data persistence
- **xUnit** for unit testing

## Repository Structure

```
FattingAPP/
├── src/
│   ├── App/              # WPF UI project
│   ├── Core/             # Domain models and business logic interfaces
│   ├── Device/           # SDK interop layer and device communication
│   ├── Data/             # SQLite persistence layer
│   └── Shared/           # Shared utilities and common types
├── tests/                # Unit tests
├── docs/                 # Documentation
├── scripts/              # Build and setup scripts
└── SoundDesignerSDK/     # Ezairo SDK (read-only, must exist at repo root)
```

## Prerequisites

Before building or running the application, ensure the following are installed:

- **Windows 10/11**
- **Visual Studio 2022** with Desktop development workload (WPF)
- **.NET SDK 10.0** (the solution uses `global.json` to request SDK 10; required because `sdnet.dll` targets .NET 10)
- **Ezairo Sound Designer SDK** present at `./SoundDesignerSDK/` (read-only folder)
  - **Note:** If the SDK is located elsewhere (e.g., inside "Ezairo Pre Suite Firmware, Sound Designer and SDK" package), ensure the **inner** `SoundDesignerSDK` folder (containing `binaries/`, `documentation/`, `samples/`, etc.) is accessible. The SDK root should contain `binaries/win32/` with `sd.config` and DLLs.

## SDK Runtime Prerequisites

The Ezairo Sound Designer SDK requires additional runtime components. Install the following from the SDK's redistribution folder:

### Required for SDK

- **Microsoft Visual C++ 2022 Redistributable (x86)**
  - Location: `SoundDesignerSDK/redistribution/MS VC++ 2022 Redist (x86)/VC_redist.x86.exe`
  - **Note:** This is required for the SDK to function.

### Required for Wired Programmers (CTK)

If you plan to use wired programmers (other than Noahlink Wireless), install:

- **CTK Runtime**
  - Location: `SoundDesignerSDK/redistribution/CTK/CTKRuntime.msi` (32-bit, matching the app's x86 target)
  - **Note:** CTK installs to `C:\Program Files (x86)\Common Files\SignaKlara\CTK\`. The application auto-detects this path via the Windows registry (`HKLM\SOFTWARE\WOW6432Node\SignaKlara\CTK`). The 32-bit `communication_modules/` folder contains `HI-PRO.dll` which is required for HI-PRO programmer support.
- **Microsoft Visual C++ 2010 SP1 Redistributable (x86)**
  - Location: `SoundDesignerSDK/redistribution/MS VC++ 2010 Redist (x86)/vcredist_x86.exe`
  - **Note:** Required dependency for CTK.

## Programmer Setup

### HI-PRO Programmer (D2XX-first)

**Wired programmer scan uses FTDI D2XX only** (no COM port or HiProMonitor required). Detection works even when the COM port is disabled in Device Manager.

1. **DLLs:** Ensure **ftd2xx.dll** and **FTD2XX_NET.dll** are in the app output folder or in `C:\Program Files (x86)\HI-PRO`. The app copies them from `src/App/NativeDeps/HI-PRO/` when building for x86.
2. **Platform:** Build and run with **Platform=x86** (32-bit). The HI-PRO/FTDI DLLs are 32-bit.
3. At startup the app configures the DLL search path and verifies D2XX load. Diagnostic output is written to **log.txt** and Debug/Trace only (no diagnostics UI in the app).

See **[docs/HI-PRO_D2XX.md](docs/HI-PRO_D2XX.md)** for setup steps and common errors.

**Hearing-aid discovery (wired HI-PRO):** After "Scan Wired" finds HI-PRO via D2XX, use **Discover Devices**. **HiProWiredDiscoveryService** returns a **DiscoveryResult** (FoundLeft, FoundRight, per-side SerialId/FirmwareId/ProductId, Errors list). Discovery is per-port: Left is tried first, then Right; if one side fails (e.g. E_SEND_FAILURE, no device), the other is still returned—partial success is supported. Only the **detected** ear(s) are shown as device cards; the undetected ear card is not displayed. **Connection** works with a single selected ear: only detected-and-selected sides are connected; a **ConnectionResult** reports per-ear status. No COM/SerialPort. Wireless programmers use DeviceDiscoveryService.

**Connect Devices UX:** The "Discover & Connect Hearing Aids" section is a single card: left column has **Scan for Hearing Aids**, Rescan/Stop, status text, and progress; right column shows either an empty state ("No devices detected yet") or a list of **detected devices only** (ItemsControl). Footer: **Connect to Selected** (disabled when none selected; shows "Connecting…" while connecting). After a successful connection, the **top header** shows "Connected" (green) and the connected device name (e.g. model or FirmwareId with Left/Right/L+R). **Navigation** (Patient Management, Audiogram, Fitting, Session Summary) is enabled only when connected; **AppSessionState** holds connection state, display name, and per-ear identity (model, firmware, serial) for the session.

### Troubleshooting programmer scan

- **Wired (HI-PRO) scan uses D2XX only.**  
  No COM port or HiProMonitor is required. If "No wired programmers found" appears, check **log.txt** in the app folder for DLL path, device count, and D2XX status. See [docs/HI-PRO_D2XX.md](docs/HI-PRO_D2XX.md).

- **"No programmers found"**  
  Ensure the programmer is connected via USB, powered on, and the correct drivers are installed. For HI-PRO, ensure ftd2xx.dll is present (app folder or `C:\Program Files (x86)\HI-PRO`) and the app is built as x86.

- **HI-PRO: "Module not found" / E_UNKNOWN_NAME**  
  The SDK could not load the HI-PRO module. The app adds `C:\Program Files (x86)\HI-PRO` to the process PATH and DLL search path at startup. If you see this error:
  1. Confirm HI-PRO driver (v4.02 or later) is installed and `C:\Program Files (x86)\HI-PRO` exists.
  2. Add `C:\Program Files (x86)\HI-PRO` to the **system** PATH (Environment Variables → Path).
  3. Restart the application after changing PATH so it picks up the new value.

- **HI-PRO: "Driver OK, hardware not connected" (device on COM5+)**  
  The app scans **HI-PRO first** (before CAA and Promira) so the CTK is in a clean state, then tries the **default/USB path** and, if needed, **COM ports** (COM1–COM4 first, per HI-PRO Configuration Help). The app checks whether any COM port is in use by another program (e.g. HI-PRO Configuration); if so, it is listed in the "No Programmers Found" dialog. Close that program and scan again.  
  If you reassigned HI-PRO to **COM2** (or COM1–COM4) in Device Manager, the app tries **COM1–COM4 first**, then other ports, using minimal settings (`port=COM2`, `port=2`) so the SDK can detect the device. Ensure no other app (e.g. HI-PRO Configuration or Starkey Inspire) has the port open when you scan.  
  If HI-PRO is connected via USB and appears on a COM port above COM4 (e.g. COM6), the SDK/CTK may only detect devices on **COM1–COM4**. The app tries all available COM ports; if the programmer is still not found:
  1. Close any other program using HI-PRO (e.g. HI-PRO Configuration or the manufacturer's fitting software).
  2. In **Device Manager** → **Ports (COM & LPT)** → select **HI-PRO (COMx)** → **Properties** → **Port Settings** → **Advanced**.
  3. Set **COM Port Number** to one of **COM1–COM4** if available.
  4. Restart the application and run the scan again.  
  *Note: Not all USB–serial drivers allow changing the COM port number; if the option is missing, the driver does not support it.*

- **CAA / Promira: "Driver OK, hardware not connected"**  
  The driver is installed but no device is detected. Connect the programmer via USB and ensure it is powered on.

- **HI-PRO: E_INVALID_STATE**  
  This usually means another process is using the HI-PRO driver (e.g. **HiProMonitorService**, **Starkey Inspire** services). The SDK/CTK then rejects our app’s connection. **Fix:** From the project root run `powershell -ExecutionPolicy Bypass -File scripts/close-programmer-apps.ps1 -Close`, confirm with **Y** to close those processes, then **restart this app** and click **Scan Wired** again. Also ensure HI-PRO is on COM1–COM4 in Device Manager if possible.

- **Wireless (NOAHlink / RSL10): E_CALL_SCAN**  
  The SDK requires `BeginScanForWirelessDevices` to be called before creating a wireless interface. The app calls it automatically before scanning wireless programmers and waits ~2.5 s for discovery. Ensure Bluetooth is on and the wireless programmer is in range and paired.

### How to verify HI-PRO scan (diagnostic logs)

After running a wired scan, the app writes diagnostic output to **Console** and to **log.txt** in the application base directory (e.g. `src\App\bin\Debug\net10.0-windows\log.txt`).

1. Run the app → **Connect Devices** → **Scan Wired** (or **Search for Programmers** → wired).
2. Open `log.txt` in the app folder (same folder as the executable).
3. Check the following:
   - **CTK Comm Interfaces Count = N** — Number of communication interfaces the CTK reports. If N is 0, the SDK does not see any interface (e.g. HI-PRO module not loaded).
   - **CTK_IF[0]=...**, **CTK_IF[1]=...** — Exact strings the CTK returns for each interface. If any contain `HI-PRO`, the app tries those first; otherwise it tries all; then COM2-only fallback (COM3/COM4 ignored).
   - **PATH (process)** — Strict order: AppBaseDir; HI-PRO; CTK; CTK\communication_modules (to avoid ftd2xx.dll conflicts).
   - **ftd2xx.dll loaded from: ...** — Where the native ftd2xx.dll was loaded from (confirms HI-PRO path is used first).
   - **\[Scan] CONFIRMED: HI-PRO via SDK interface: ...** — Strategy A (CTK interface string) succeeded.
   - **\[Scan] CONFIRMED: HI-PRO on COM2 via '...'** — Strategy B (COM2 fallback) succeeded.
   - **\[CTK] GetDetailedErrorString: ...** — Shown after each failed attempt and after SDException; use this to see why a given interface or COM2 variant failed.

If the scan still finds 0 programmers, the log will show whether the CTK lists HI-PRO as an interface and the exact error string for each try (e.g. `E_INVALID_STATE`, port in use, or CheckDevice returned false).

#### How to interpret results

- **CTK_IF count is 0** — CTK does not expose any communication interface (e.g. HI-PRO module not loaded or wrong driver mode). Run the **Ftd2xxTest** console app (see below) to see if D2XX sees the device; if D2XX also reports 0 devices, the driver may be in VCP-only mode or another process holds the device.
- **D2XX device count is 0** (from Ftd2xxTest) — CTK cannot open HI-PRO via USB-direct; likely driver mode (VCP vs D2XX) or conflict. Ensure no other app uses HI-PRO; try closing HI-PRO Configuration and Starkey services, then run Ftd2xxTest again.
- **D2XX lists the device** — CTK should be able to open it; the issue is likely CTK init, interface selection, or threading. Confirm log shows **SetDllDirectory(HI-PRO)** first and **PATH** with HI-PRO before CTK; scan runs on a single STA thread with no parallel tries.
- **Next actions:** If CTK_IF count > 0 but no "HI-PRO" string, the app tries all CTK_IF strings then COM2-only fallback. If all fail, check **\[CTK] GetDetailedErrorString** after each attempt; fix port conflict or driver state accordingly.

### Verify COM and USB from the console (without running the app)

You can check that COM ports and USB devices (e.g. HI-PRO) are visible before opening the application:

- **PowerShell script (recommended):** From the project root run  
  `powershell -ExecutionPolicy Bypass -File scripts/check-com-usb.ps1`  
  This lists: COM ports (as .NET sees them), COM/Serial/HI-PRO devices from WMI, USB devices, and HI-PRO specifically.

- **One-liners:**
  - COM port names (same as the app uses):  
    `powershell -Command "[System.IO.Ports.SerialPort]::GetPortNames()"`
  - HI-PRO and COM devices:  
    `powershell -Command "Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'HI-PRO|COM\d+' } | Select-Object Name, Status"`

- **Close programs using the programmer (so the app can use HI-PRO):**  
  `powershell -ExecutionPolicy Bypass -File scripts/close-programmer-apps.ps1`  
  This checks which COM ports are in use and lists known programmer-related processes (e.g. HI-PRO Configuration, Starkey Inspire services). To close them:  
  `powershell -ExecutionPolicy Bypass -File scripts/close-programmer-apps.ps1 -Close`  
  (You will be asked to confirm before any process is closed.)

### Diagnostics (HI-PRO preflight + diagnostic report)

An automated **preflight + diagnostics** workflow runs at **app startup (before any scan)** in the background (15s timeout) and can be run manually from the sidebar. It generates a single Markdown report to help understand HI-PRO scan/handshake failures.

**Output files:**
- `docs\HI-PRO_Diagnostic_Report.md` — main report
- `docs\reports\HI-PRO_Report_YYYYMMDD_HHMMSS.md` — timestamped copy
- **`logs\diagnostics\HI-PRO_DiagnosticReport.md`** — report written when run from app (startup or Diagnostics button)
- `logs\diagnostics\ports.json`, `handshake.json`, `dlls.json`, `blockers.json` — raw JSON
- `logs\hpro_diag.log` — startup run log
- `logs\diagnostics\startup_runner_stdout.txt`, `startup_runner_stderr.txt` — captured when run from app

**From the app:** Use the **Diagnostics** button in the sidebar to re-run on demand. At startup, the app runs preflight + diagnostics in the background (no UI block) before any SDK/scan; if scripts are missing, the app continues normally.

**Config (appsettings.json):** In the app output directory (next to the .exe), `appsettings.json` can include:
- **`Diagnostics:AutoStopBlockers`** (boolean, default `false`). When `true` and the app is **run as Administrator**, startup diagnostics will pass `-AutoStopStarkeyInspire` to the script, which stops **only** Inspire/Starkey/HiProTrayApp-related services and processes (no other system services). If not elevated, auto-stop is not attempted; blockers are only reported with instructions.

**From the command line (repo root):**

```powershell
.\scripts\diagnostics\05_summary_runner.ps1 -AppOutputDir "src\App\bin\x86\Debug\net10.0-windows"
```

To optionally stop Starkey/Inspire services and processes (PowerShell **run as Administrator**):

```powershell
.\scripts\diagnostics\05_summary_runner.ps1 -AppOutputDir "src\App\bin\x86\Debug\net10.0-windows" -AutoStopStarkeyInspire
```

Report sections: 1) Ports inventory, 2) Handshake/connectivity (basic open/close ≠ HI-PRO handshake), 3) Port lock status, 4) Blocker processes/services, 5) DLL integrity (AppBitness from EXE PE), 6) Evidence (commands + errors), 7) Conclusions (top 3 causes + recommended fixes). No system DLLs or drivers are modified; auto-stop is opt-in and targets Starkey/Inspire only.

**Diagnostics script updates (evidence-based report):** The summary runner (`05_summary_runner.ps1`) now merges all JSON outputs correctly: handshake table from `PerPortResults`/`Summary`, blocker tables from `BlockerProcesses`/`BlockerServices`, and DLL section with **AppBitness** (from app EXE PE in `-AppOutputDir`, preferring `Ul8ziz.FittingApp.App.exe`) and **PowerShellBitness** (runner process, not the app). Evidence section lists exact command lines; Errors section shows only exceptions (no JSON dumps). All scripts write to `logs\diagnostics\` using resolved full paths so the report is complete when run from repo root. **DLL scan** explicitly includes `C:\Program Files (x86)\HI-PRO` so **HiProWrapper.dll** is detected and listed in the report (Location, FileVersion, PE, SHA256).

**Automatic HI-PRO preflight at startup:** At app startup (before any scan), the app runs the diagnostic PowerShell workflow in the background and writes the report to `logs\diagnostics\HI-PRO_DiagnosticReport.md` (and to `docs\` as before). Config flag **`Diagnostics:AutoStopBlockers`** in `appsettings.json` (next to the exe): when `true` and the app is elevated, startup diagnostics stop only Inspire/Starkey/HiProTrayApp blockers; when not elevated, blockers are reported with clear instructions. DLL search order: `SetDllDirectory(AppBaseDir)` is set first at startup; the loaded ftd2xx.dll path is logged during scan (see log.txt / ScanDiagnostics). **Loaded module evidence:** At startup the app logs **HiProWrapper.dll** and **ftd2xx.dll** loaded paths (GetModuleHandle + GetModuleFileName) to `logs\diagnostics\loaded_modules.txt`, `logs\hpro_diag.log`, and `log.txt` (or "(not loaded yet)" until SDK/CTK load them). **CopyLocal (build):** After each build, `scripts\copy-hipro-dlls.ps1` copies **HiProWrapper.dll** (and optionally ftd2xx.dll, FTD2XX_NET.dll) from `C:\Program Files (x86)\HI-PRO` into the app output dir **only if missing**; log in `logs\diagnostics\copy_local.log`. No system DLLs are modified.

### FTDI D2XX direct test (Ftd2xxTest)

A separate console project (**src/Tools/Ftd2xxTest**) checks whether HI-PRO is visible via FTDI D2XX (USB-direct), independent of CTK. This helps distinguish driver mode (VCP vs D2XX) from CTK/interface issues.

**How to run:**

1. Build the solution (or build `src\Tools\Ftd2xxTest\Ftd2xxTest.csproj` with `dotnet build -p:Platform=x86`).
2. Copy **FTD2XX_NET.dll** and **ftd2xx.dll** from `C:\Program Files (x86)\HI-PRO` into the Ftd2xxTest output folder (e.g. `src\Tools\Ftd2xxTest\bin\Debug\net10.0\`), or run the exe from the HI-PRO folder.
3. Run **Ftd2xxTest.exe** (x86). It prints to the console and writes **ftd2xx_test_log.txt** in the same folder.

**What to look for:**

- **GetNumberOfDevices => count = 0** — D2XX does not see any device; CTK cannot open HI-PRO via USB-direct (driver may be in VCP mode or device in use).
- **GetNumberOfDevices => count ≥ 1** and device list with **Description**, **SerialNumber**, **ID**, **Type** — D2XX sees HI-PRO; if the main app still finds 0, the issue is likely CTK init, interface selection, or threading (see log.txt from the main app).

## Setup Steps

### 1. Copy SDK Runtime Files

Copy the required Windows SDK runtime files into the solution's device library folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\copy-sdk-deps.ps1 -Platform x86
```

This script copies the necessary DLLs and configuration files from `SoundDesignerSDK/binaries/win32/` into `src/Device/Libs/`.

### 2. Verify SDK Setup

Verify that the SDK folder structure and copied runtime files are present:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-sdk.ps1 -Platform x86
```

This script checks for required SDK folders and validates that runtime dependencies are correctly copied.

### 3. Build the Solution

Open `Ul8ziz.FittingApp.sln` in Visual Studio 2022 and build the solution. The application targets **.NET 10** and **x86 (32-bit)** to match the supplied `sdnet.dll` and ensure compatibility with the CTK HI-PRO communication module.

#### Build instructions

1. Install **.NET SDK 10.0** if needed (see [downloads](https://dotnet.microsoft.com/download)).
2. Close Visual Studio (and any running instance of the app).
3. Delete `bin` and `obj` folders in the solution and in each project (e.g. `src\App\bin`, `src\App\obj`, `src\Device\DeviceCommunication\bin`, `src\Device\DeviceCommunication\obj`).
4. From the solution root, run:
   - `dotnet --info` (confirm .NET SDK in use)
   - `dotnet --list-sdks` (ensure 10.0.x is listed)
   - `dotnet clean`
   - `dotnet build`
5. Reopen the solution in Visual Studio.
6. Verify:
   - Build output folder is `src\App\bin\Debug\net10.0-windows`.
   - Run the app and use "Search for Programmers" to verify HI-PRO scan works.

**Current target: .NET 10.** The `sdnet.dll` in `src/Device/Libs/` is built for .NET 10 (System.Runtime 10.0), so the solution targets `net10.0-windows` / `net10.0` and `global.json` requests SDK 10. If you obtain an sdnet built for .NET 8, you can switch the projects back to `net8.0-windows` / `net8.0` and use .NET 8 SDK.

## Documentation

- **Setup Guide:** For detailed step-by-step setup instructions, including prerequisites installation and troubleshooting, see [docs/Setup.md](docs/Setup.md).
- **Scan Requirements:** For comprehensive requirements and troubleshooting guide for programmer scanning, see [docs/SCAN_REQUIREMENTS.md](docs/SCAN_REQUIREMENTS.md).

### HI-PRO preflight and diagnostics (files changed/added)

- **HI-PRO preflight (startup, before CTK):** Runs asynchronously at app startup. **Steps:** (A) Write diagnostics to `logs/hpro_diag.log` (timestamp, bitness, paths, PATH trimmed, FTDI DLL candidates, loaded ftd2xx path via GetModuleHandle/GetModuleFileName); (B) Set DLL directory to AppBase first; (C) Detect HI-PRO device and confirm VCP driver (FTSER2K) via PnP; (D) Check COM2 availability (open/close test); (E) If COM2 locked and **AutoFreeComPortOnStartup** is true: enumerate suspect processes (Inspire, Starkey, HiPro, etc.), log PID/path, attempt CloseMainWindow then Kill, set HiProMonitor service to Manual; (F) Retry COM2 open up to 3 times; (G) If still locked, abort wired scan and show clear UI message. No system DLL modification.
- **Config switch:** `HiproPreflight.AutoFreeComPortOnStartup` (default `true`). When `false`, preflight only detects and logs; does not kill processes or change services.
- **Added:** `src/Device/DeviceCommunication/NativeDllResolver.cs` — P/Invoke: SetDllDirectory, AddDllDirectory, GetModuleHandle, GetModuleFileName.
- **Added:** `src/Device/DeviceCommunication/PreflightLog.cs` — File append logger for preflight with rotation at 5MB.
- **Added:** `src/Device/DeviceCommunication/HiproPreflight.cs` — `RunAsync()`, `EnsureCompletedAsync()`, `LastResult`, Steps A–G, `SetHiProDllDirectoryForScan()`, `RestoreAppDllDirectoryAfterScan()`.
- **Modified:** `src/Device/DeviceCommunication/SdkConfiguration.cs` — Uses `NativeDllResolver.SetDllDirectoryPath`; `SetAppDllDirectoryFirst()` at startup.
- **Added:** `src/App/Helpers/HiproPreflight.cs` — `PreflightResult Run(string portName = "COM2")`, logs to `logs/hpro_preflight.log`; lists processes/services (Inspire|Starkey|Updater|HiPro|Monitor), COM2 open test (9600 8N1), `mode COM2`; `TryMitigateAndRetest()` with user consent (stop services, kill processes). **Added:** `src/App/Helpers/WindowsAdmin.cs` — `IsRunningAsAdmin()`.
- **Modified:** `src/App/App.xaml.cs` — OnStartup: `SetAppDllDirectoryFirst()`, then `Helpers.HiproPreflight.Run("COM2")`; if blocked and admin, dialog "Stop them now?"; if still blocked, message with PIDs; scan does not run until preflight passes.
- **Modified:** `src/App/Views/ConnectDevicesView.xaml.cs` — Before wired scan: `Helpers.HiproPreflight.Run("COM2")`; if `!IsPortFree` show message and abort. After wired scan: `DeviceCommunication.HiproPreflight.RestoreAppDllDirectoryAfterScan()`.
- **Modified:** `src/Device/DeviceCommunication/ScanDiagnostics.cs` — `LogSdExceptionDetails` now logs Message, HResult, InnerException, StackTrace; CTK_IF[i], log.txt.
- **Modified:** `src/Device/DeviceCommunication/ProgrammerScanner.cs` — SDException logging on all catch paths, SemaphoreSlim/STA, adaptive Strategy A + COM2-only Strategy B.
- **Added:** `scripts/check-ftdi.ps1`, `scripts/check-comlock.ps1`, `scripts/inspect-output-dlls.ps1` — FTDI/COM diagnostics (no system DLL changes).
- **Added:** `DiagnosticsReport.md` — HI-PRO diagnostic report template.
- **Added:** `src/Tools/Ftd2xxTest/`, `src/Tools/HiproD2xxProbe/` — D2XX probe console apps.
- **Modified:** `Ul8ziz.FittingApp.sln` — Tools projects.

## Notes

- The `SoundDesignerSDK/` folder is treated as read-only. Do not modify files within this directory.
- SDK runtime files are copied into `src/Device/Libs/` by the setup scripts. These files are tracked in the repository.
- The application targets **Windows x86 (32-bit)** because the CTK HI-PRO communication module (`HI-PRO.dll`) is only available in 32-bit. The SDK provides both win32 and win64 binaries; this project uses `win32`. The WPF app project enforces **PlatformTarget=x86** and copies **ftd2xx.dll**, **FTD2XX_NET.dll**, **HiProWrapper.dll** from **src/App/Vendor/HI-PRO/** into the build output (`$(TargetDir)`) on every build; a **VerifyHiproDlls** target fails the build if any of these are missing. Populate `src/App/Vendor/HI-PRO/` once from `C:\Program Files (x86)\HI-PRO` if needed (see that folder's README.txt).
- **Programmer scan:** Wired scan runs on **one dedicated STA thread** (no Task.Run/ThreadPool), with **SemaphoreSlim(1,1)** so only one scan at a time and no parallel CTK calls. Adaptive logic: try CTK interface strings (HI-PRO first if present), then all CTK_IF, then **COM2-only** fallback (COM3/COM4 ignored). **SetDllDirectory(HI-PRO)** is set before CTK so ftd2xx.dll loads from HI-PRO first; process PATH is set to AppBaseDir; HI-PRO; CTK; CTK\communication_modules. Wireless scan calls `BeginScanForWirelessDevices` before creating wireless interfaces (avoids E_CALL_SCAN).
- **SDK Path:** The project expects the SDK at `./SoundDesignerSDK/` relative to the repository root. If your SDK is in a different location (e.g., `C:\Users\...\Ezairo Pre Suite Firmware, Sound Designer and SDK\SoundDesignerSDK\SoundDesignerSDK`), ensure the **inner** `SoundDesignerSDK` folder (the one containing `binaries/`, `documentation/`, `samples/`) is accessible. Setup scripts should reference this SDK root path.