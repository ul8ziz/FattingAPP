# HI-PRO / FTDI / CTK Diagnostic Report

This report documents diagnostics for handshake failure with HI-PRO (FTDI) and the fixes applied **inside the project** (no system DLL changes).

---

## 1) Summary — Top 3 Likely Causes (Evidence-Based)

Evidence from script runs and app logs:

| # | Likely cause | Evidence (script/log) |
|---|----------------|------------------------|
| 1 | **COM2 locked** | `check-comlock.ps1`: "Access to the port 'COM2' is denied." Suspected: **Inspire.UpdaterService**, **InspireUpdaterSDK**, **Starkey.InspireSupport.Service** (hearing/fitting stack). HiProMonitor = Stopped. |
| 2 | **Wrong ftd2xx.dll loaded** | `check-ftdi.ps1`: **SysWOW64** has `ftd2xx.dll` (same 219496 bytes, 3.02.07). HI-PRO and app output also have it. If app folder is not first in DLL search, loader may use SysWOW64. **Fix applied:** `SetAppDllDirectoryFirst()` at startup. |
| 3 | **Architecture mismatch** | `inspect-output-dlls.ps1`: All SDK DLLs in app output are **x86**. App is built **x86** → no mismatch. If process were 64-bit, handshake would fail. |

**Typical causes (reference):**

- **Wrong ftd2xx.dll loaded:** System32 or SysWOW64 copy loaded instead of app/HI-PRO folder → check `hpro_diag.log` "ftd2xx loaded path" and `check-ftdi.ps1` output.
- **Architecture mismatch:** x64 process loading x86 DLLs (or vice versa) → check `hpro_diag.log` "Is64BitProcess" and `inspect-output-dlls.ps1` CPU column.
- **COM2 locked:** Another process or service holding COM2 → check `check-comlock.ps1` and "COM2 open attempt" / "Suspected processes".

---

## 2) File Inventory

### 2.1 App output folder (e.g. `src\App\bin\Debug\net10.0-windows\`)

Run: `.\scripts\inspect-output-dlls.ps1 -OutputDir "src\App\bin\Debug\net10.0-windows"`

| File name | Full path | Size | LastWriteTime | CPU (x86/x64) |
|-----------|------------|------|---------------|----------------|
| sdnet.dll | ...\net10.0-windows\sdnet.dll | 82944 | 2025-12-19 | x86 |
| SDCOM.dll | ...\net10.0-windows\SDCOM.dll | 320000 | 2025-12-19 | x86 |
| FTChipID.dll | ...\net10.0-windows\FTChipID.dll | 65144 | 2013-03-04 | x86 |
| ftd2xx.dll | ...\net10.0-windows\ftd2xx.dll | 219496 | 2013-01-18 | x86 |
| FTD2XX_NET.dll | ...\net10.0-windows\FTD2XX_NET.dll | 75232 | 2008-12-12 | x86 |
| HiProWrapper.dll | ...\net10.0-windows\HiProWrapper.dll | — | — | N/A (not present) |
| SoundDesigner.*.dll | ...\net10.0-windows\ | various | 2025-12-19 | x86 |

**Mismatch:** If "CPU" is x86 but process is 64-bit (see `hpro_diag.log`), or x64 DLL with x86 process → handshake can fail. Current: all x86, app is x86 → OK.

### 2.2 HI-PRO folder (`C:\Program Files (x86)\HI-PRO\`)

From `check-ftdi.ps1` explicit path checks:

- `ftd2xx.dll` — exists: **yes**, 219496 bytes, 3.02.07, 2013-01-18
- `FTD2XX_NET.dll` — exists: **yes**, 75232 bytes, 1.0.6.0, 2008-12-12
- `FTChipID.dll` — exists: **yes**, 65144 bytes, 1.1.0.0, 2013-03-04

**Note:** `C:\Windows\SysWOW64\ftd2xx.dll` also exists (same size/version). Do **not** delete; use app/HI-PRO DLL search order instead.

---

## 3) Driver Snapshot (VCP vs D2XX)

- **VCP (Virtual COM):** Device appears as COM port; uses system serial stack (e.g. FTSER2K).
- **D2XX:** Direct FTDI driver; no COM port (or optional).

To see which mode the device uses (optional, run in elevated PowerShell):

```powershell
Get-PnpDevice -Class Ports | Where-Object { $_.FriendlyName -match "HI-PRO|FTDI|COM2" } | Format-List *
Get-PnpDeviceProperty -InstanceId (Get-PnpDevice -Class Ports | Where-Object { $_.FriendlyName -match "COM2" } | Select-Object -First 1).InstanceId
```

| Finding | Value |
|---------|--------|
| Device service (e.g. FTSER2K = VCP) | *(paste or "not run")* |
| COM2 present | *(yes/no)* |

---

## 4) COM2 Lock Status

Run: `.\scripts\check-comlock.ps1`

| Item | Result |
|------|--------|
| COM2 accessible? | **LOCKED** — "Access to the port 'COM2' is denied." |
| Suspected process(es) | **Inspire.UpdaterService**, **InspireUpdaterSDK**, **Starkey.InspireSupport.Service** (and other matching names from script). Close Inspire/Starkey apps or stop their services to free COM2. |
| Related service(s) | HiProMonitor = Stopped; jhi_service = Running (Intel). |

---

## 5) Recommended Fixes (Project-Only, Applied)

These are implemented in the codebase; no system DLL deletion or modification.

### 5.1 DLL load order (app folder first)

- **Where:** `App.xaml.cs` → `OnStartup` calls `SdkConfiguration.SetAppDllDirectoryFirst()` before any other logic.
- **Effect:** `SetDllDirectory(app folder)` so the loader prefers `ftd2xx.dll` from the application output folder over System32/SysWOW64.
- **Note:** When a scan runs, `SdkConfiguration.SetupEnvironment()` sets `SetDllDirectory(HI-PRO)` so during scan the HI-PRO folder is used. If you need the **app folder always first**, do not override in `SetupEnvironment` or switch to `AddDllDirectory` (see code comments).

### 5.2 Startup diagnostics and 64-bit warning

- **Where:** `ScanDiagnostics.WriteHiproDiagLog()` writes to `logs\hpro_diag.log`: `Is64BitProcess`, `Is64BitOperatingSystem`, current directory, base directory, PATH, and **actual loaded path of ftd2xx.dll** (or "not loaded yet").
- **Where:** `App.xaml.cs` → `OnStartup` calls `WriteHiproDiagLog()` and `LogFtd2xxLoadedPath()`; if `Environment.Is64BitProcess == true` a warning is written (vendor DLLs are usually x86).
- **Effect:** You can verify from logs where `ftd2xx.dll` is loaded and whether process bitness matches DLLs.

### 5.3 Explicit x86 build

- **Where:** `src\App\Ul8ziz.FittingApp.App.csproj`: `PlatformTarget` is set to **x86** (and `Prefer32Bit` true).
- **Effect:** Process is 32-bit; matches typical HI-PRO/CTK DLLs (x86). Prevents x64 process loading x86 DLL mismatch.

### 5.4 Scripts added (no system changes)

| Script | Purpose |
|--------|--------|
| `scripts\check-ftdi.ps1` | Locate all `ftd2xx.dll` (where.exe + explicit paths: System32, SysWOW64, HI-PRO, app output). Reports versions/sizes. Does **not** modify or delete system DLLs. |
| `scripts\check-comlock.ps1` | List COM ports, try to open COM2; on failure list suspected processes and related services. |
| `scripts\inspect-output-dlls.ps1` | Inventory SDK/FTDI DLLs in app output folder with PE architecture (x86/x64). |

---

## How to Reproduce Diagnostics

1. Build the app (e.g. Debug, net10.0-windows).
2. Run from repo root:
   - `.\scripts\check-ftdi.ps1 -AppOutputPath "src\App\bin\Debug\net10.0-windows"`
   - `.\scripts\check-comlock.ps1`
   - `.\scripts\inspect-output-dlls.ps1 -OutputDir "src\App\bin\Debug\net10.0-windows"`
3. Run the app once (connect screen is enough); then check:
   - `logs\hpro_diag.log` (in app base directory)
   - `log.txt` (in app base directory)
4. Fill sections 1–4 above with the script and log output.

**Rules:** No conclusion without script/log evidence. Do **not** delete or replace system DLLs as a first fix; use DLL search order (app folder first) and correct CPU target (x86) in the project.
