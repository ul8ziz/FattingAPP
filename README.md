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

### HI-PRO Programmer

For HI-PRO programmer support:

1. Install the HI-PRO driver (version 4.02 or later, available from Otometrics®).
2. Add the HI-PRO installation directory to your system PATH:
   ```
   C:\Program Files (x86)\HI-PRO;
   ```

This PATH entry is required for the SDK to locate the HI-PRO communication libraries.

### Troubleshooting programmer scan

- **"No programmers found"**  
  Ensure the programmer is connected via USB, powered on, and the correct drivers are installed.

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

## Notes

- The `SoundDesignerSDK/` folder is treated as read-only. Do not modify files within this directory.
- SDK runtime files are copied into `src/Device/Libs/` by the setup scripts. These files are tracked in the repository.
- The application targets **Windows x86 (32-bit)** because the CTK HI-PRO communication module (`HI-PRO.dll`) is only available in 32-bit. The SDK provides both win32 and win64 binaries; this project uses `win32`.
- **Programmer scan:** The scan runs on the UI thread (same thread as SDK initialization) to satisfy CTK/SDNET COM thread affinity and avoid E_INVALID_STATE. The window may be briefly unresponsive during the scan. Wireless scan calls `BeginScanForWirelessDevices` before creating wireless interfaces to satisfy the SDK (avoids E_CALL_SCAN).
- **SDK Path:** The project expects the SDK at `./SoundDesignerSDK/` relative to the repository root. If your SDK is in a different location (e.g., `C:\Users\...\Ezairo Pre Suite Firmware, Sound Designer and SDK\SoundDesignerSDK\SoundDesignerSDK`), ensure the **inner** `SoundDesignerSDK` folder (the one containing `binaries/`, `documentation/`, `samples/`) is accessible. Setup scripts should reference this SDK root path.