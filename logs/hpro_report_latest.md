# HI-PRO Diagnostic Report

Generated: **2026-02-11T10:36:44.3215230+11:00**
 | App output dir: src\App\bin\Debug\net10.0-windows

## 1. Ports inventory

| PortName | FriendlyName | Manufacturer | DriverService | DriverVersion |
|----------|--------------|---------------|---------------|---------------|
| COM2 | HI-PRO (COM2) | FTDI | FTSER2K | 2.8.28.0 |
| COM2 | HI-PRO (COM2) | FTDI | FTSER2K | 2.8.28.0 |
| COM3 | Standard Serial over Bluetooth link (COM3) | Microsoft | BTHMODEM | 10.0.26100.2161 |
| COM3 | Standard Serial over Bluetooth link (COM3) | Standard Serial over Bluetooth link (COM3) | BTHMODEM | 10.0.26100.2161 |
| COM4 | Standard Serial over Bluetooth link (COM4) | Microsoft | BTHMODEM | 10.0.26100.2161 |
| COM4 | Standard Serial over Bluetooth link (COM4) | Standard Serial over Bluetooth link (COM4) | BTHMODEM | 10.0.26100.2161 |
|  | HI-PRO | FTDI | FTDIBUS | 2.8.28.0 |

## 2. SerialNumber / Description evidence

```
Port: COM2 | FriendlyName: HI-PRO (COM2) | SerialNumber:  | ContainerId: 
Port: COM2 | FriendlyName: HI-PRO (COM2) | SerialNumber:  | ContainerId: {D0EC4957-0D57-5D9E-B99E-939684A63B67}
Port: COM3 | FriendlyName: Standard Serial over Bluetooth link (COM3) | SerialNumber:  | ContainerId: {00000000-0000-0000-FFFF-FFFFFFFFFFFF}
Port: COM3 | FriendlyName: Standard Serial over Bluetooth link (COM3) | SerialNumber:  | ContainerId: 
Port: COM4 | FriendlyName: Standard Serial over Bluetooth link (COM4) | SerialNumber:  | ContainerId: {E081824B-6D08-5A0E-A2C1-FE8368E017EA}
Port: COM4 | FriendlyName: Standard Serial over Bluetooth link (COM4) | SerialNumber:  | ContainerId: 
Port:  | FriendlyName: HI-PRO | SerialNumber:  | ContainerId: {D0EC4957-0D57-5D9E-B99E-939684A63B67}
```

## 3. Handshake / connectivity attempts (per port)

*This is a basic open/close test only; it does NOT prove HI-PRO protocol handshake.*

| Port | Success | Detail |
|------|---------|--------|
| COM2 | True | 9600: OK |
| COM3 | True | 9600: OK |
| COM4 | False | 9600: Exception calling "Open" with "0" argument(s): "The semaphore timeout period has expired. "; 115200: Exception calling "Open" with "0" argument(s): "The semaphore timeout period has expired. " |

## 4. Port locked status and COM2

- **COM2 exists:** Yes
- **COM2 open (basic test):** True
- **COM2 status:** COM2 open OK
- **Note:** Basic connectivity/lock test only; does NOT prove HI-PRO handshake.
- **COM2 device service (driver):** FTSER2K

Port lock test:
- COM2: Free
- COM3: Free
- COM4: Free (Exception calling "Open" with "0" argument(s): "The semaphore timeout period has expired.
")

## 5. Blocker processes and services

**Processes found:**
| ProcessName | Id | Path |
|-------------|-----|------|
| Starkey.InspireSupport.Service.exe | 4440 |  |
| Inspire.UpdaterService.exe | 4488 |  |
| InspireUpdaterSDK.exe | 4912 |  |

**Services found:**
| ServiceName | DisplayName | Status | StartType |
|-------------|-------------|--------|-----------|
| GoogleUpdaterService144.0.7547.0 | Google Updater Service (GoogleUpdaterService144.0.7547.0) | Running | Auto |
| HiProMonitor | HiPro Monitor | Stopped | Manual |
| InspireApplicationUpdater | Inspire Application Updater | Running | Auto |
| InspireSupportService | Inspire Support Service | Running | Auto |
| UpdaterService | Inspire Updater | Running | Auto |

**Auto-stop (Starkey/Inspire only):**
- Stopped services: None
- Stopped processes: None
- Remained: services None; processes None


## 6. DLL integrity

Process architecture: **x64**

| Location | FileName | Size | PE | FileVersion | SHA256 |
|----------|----------|------|-----|-------------|--------|
| SysWOW64 | ftd2xx.dll | 219496 | x86 | 3.02.07 | 0F635C52339255F1... |
| CTK | ftd2xx.dll | 219496 | x86 | 3.02.07 | 0F635C52339255F1... |
| CTK | FTD2XX_NET.dll | 75232 | x86 | 1.0.6.0 | B4EB08BD0BD06C45... |
| CTK\communication_modules | HI-PRO.dll | 40960 | x86 | 1.17.0 | 9AEF35CE33EEA82D... |

**Warnings:**
- Process is 64-bit; HI-PRO/CTK vendor DLLs are typically x86. Prefer x86 (32-bit) app for HI-PRO.
- HiProWrapper.dll not found in scanned locations.
- x86 DLL at C:\WINDOWS\SysWOW64\ftd2xx.dll loaded by x64 process may cause load failures.
- x86 DLL at src\App\bin\Debug\net10.0-windows\ftd2xx.dll loaded by x64 process may cause load failures.
- x86 DLL at src\App\bin\Debug\net10.0-windows\FTD2XX_NET.dll loaded by x64 process may cause load failures.
- x86 DLL at C:\Program Files (x86)\Common Files\SignaKlara\CTK\communication_modules\HI-PRO.dll loaded by x64 process may cause load failures.

## 7. Conclusions (top 3 likely causes)

1. **Starkey/Inspire or related processes** are running. Evidence: 3 process(es) matching blocker pattern. These can hold COM2 and prevent HI-PRO handshake.
2. **Starkey/Inspire or related services** are present. Evidence: 5 service(s). Stopping them (e.g. with -AutoStopStarkeyInspire when elevated) may free the port.
3. **DLL/architecture mismatch.** Evidence: Process is 64-bit; HI-PRO/CTK vendor DLLs are typically x86. Prefer x86 (32-bit) app for HI-PRO. HiProWrapper.dll not found in scanned locations. x86 DLL at C:\WINDOWS\SysWOW64\ftd2xx.dll loaded by x64 process may cause load failures. x86 DLL at src\App\bin\Debug\net10.0-windows\ftd2xx.dll loaded by x64 process may cause load failures. x86 DLL at src\App\bin\Debug\net10.0-windows\FTD2XX_NET.dll loaded by x64 process may cause load failures. x86 DLL at C:\Program Files (x86)\Common Files\SignaKlara\CTK\communication_modules\HI-PRO.dll loaded by x64 process may cause load failures.. Ensure app runs as x86 when using HI-PRO/CTK x86 DLLs.

## 8. Recommended fixes (project-only first)

1. **Run diagnostics from repo root:** ``.\scripts\diagnostics\05_summary_runner.ps1 -AppOutputDir "src\App\bin\Debug\net10.0-windows"``
2. **If COM2 is locked:** Close Inspire/Starkey apps; or run with ``-AutoStopStarkeyInspire`` (elevated) to stop only Starkey/Inspire services and processes.
3. **If COM2 missing:** Reassign HI-PRO to COM2 in Device Manager (Port Settings â†’ Advanced â†’ COM Port Number) if possible.
4. **If DLL/arch mismatch:** Build and run the app as **x86** (PlatformTarget=x86); do not modify system DLLs.
5. **Verify CTK/HI-PRO paths:** Ensure ``C:\Program Files (x86)\HI-PRO`` and CTK ``communication_modules`` exist and contain expected DLLs.

---
## Evidence: PowerShell commands used
```
powershell.exe -ExecutionPolicy Bypass -File C:\Users\haifa\Downloads\Fatting_App\FattingAPP\FattingAPP\scripts\diagnostics\01_ports_inventory.ps1
powershell.exe -ExecutionPolicy Bypass -File C:\Users\haifa\Downloads\Fatting_App\FattingAPP\FattingAPP\scripts\diagnostics\02_handshake_tests.ps1 -PortsJson '<from 01>'
powershell.exe -ExecutionPolicy Bypass -File C:\Users\haifa\Downloads\Fatting_App\FattingAPP\FattingAPP\scripts\diagnostics\03_dll_integrity.ps1 -AppOutputDir "src\App\bin\Debug\net10.0-windows"
powershell.exe -ExecutionPolicy Bypass -File C:\Users\haifa\Downloads\Fatting_App\FattingAPP\FattingAPP\scripts\diagnostics\04_port_blockers.ps1 -PortsJson '<from 01>'
```

