# Fitting Page – Developer Note

## Testing and building – where to see errors and output

When you **build** or **run** the app, nothing is hidden—output goes to specific places. Use these to detect errors:

| What you do | Where to look | What you see |
|-------------|----------------|--------------|
| **Build** (Ctrl+Shift+B or Build menu) | **View → Output** → **Show output from: Build** | Compile errors and warnings. If the build fails, the error list and Build output window show the cause. |
| **Run with debugging** (F5) | **View → Output** → **Show output from: Debug** | All runtime output: `Debug.WriteLine` (e.g. `[SessionEnd]`, `[SoundDesigner]`, `[HiProWiredDiscovery]`, `[ConnectDevices]`, startup logs). **You must select "Debug" in the Output window dropdown**—if you leave it on "Build", you will not see any of this. |
| **Run without debugging** (Ctrl+F5) or run the .exe | **log.txt** in the same folder as the executable | Same diagnostic lines as Debug output. Path example: `src\App\bin\x86\Debug\net10.0-windows\log.txt` (or `x86\Debug\net10.0-windows` when building for x86). `ScanDiagnostics.WriteLine` writes to both the console and **log.txt**. |
| **First-chance exceptions** (e.g. SDException) | **Output** window (Debug) with **Exception Messages** enabled | Right‑click the Output window → ensure options that log exception messages are **checked** so you see lines like `Exception thrown: 'SDLib.SDException' in sdnet.dll`. Uncheck **Thrown** only for `TargetInvocationException` (Debug → Windows → Exception Settings) to avoid breaking on every reflection wrap. |

**Summary:** Build errors → **Output, Build**. Runtime and device/save errors → **Output, Debug** when debugging, or **log.txt** next to the .exe when not. The toast in the app also shows save/connection failures.

### SDK documentation rule (mandatory)

All SoundDesigner SDK behavior (NVM Restore/Burn, Memory count and switching, Configure/Initialize sequence, Save and End, and any call that can trigger E_UNCONFIGURED_DEVICE or access violations) **must** be justified by the vendor docs in:

`C:\Users\haifa\Downloads\Fatting_App\Ezairo Pre Suite Firmware, Sound Designer and SDK\SoundDesignerSDK\SoundDesignerSDK\documentation`

- Before changing any workflow step, locate the relevant section in those docs and follow it.
- In code, add a short comment with **doc filename + section/title** (e.g. `// sounddesigner_programmers_guide.pdf §6.3`).
- If a required API or behavior is **not documented** there, do not implement it; log **"Not supported by docs"** and keep the feature disabled.

See **`.cursor/rules/rules.mdc`** §0 for the full rule.

### Interpreting your debug output

When you run with F5 and select **Output → Debug**, you may see the following. All of these are **expected** in normal runs:

| What you see | Meaning |
|--------------|--------|
| `Exception thrown: 'System.Reflection.TargetInvocationException' in DeviceCommunication.dll` (during EnumerateSingleMemory / BuildSnapshotForMemory) | The SDK threw while the app was reading a parameter property via reflection (e.g. Value, Min, Max). The code catches it and continues; the **real** error is in `InnerException`. This is normal during offline or library enumeration. To reduce noise: **Exception Settings** → uncheck **Thrown** for **TargetInvocationException**. |
| `Exception thrown: 'System.Reflection.TargetInvocationException' in System.Private.CoreLib.dll` (during ApplyParam / param file load) | Same as above: reflection when applying a .param file to the offline product. Caught and handled; enumeration continues. |
| `[ConnectDevices] INFO: Left Serial=-1 — unprogrammed device` or `Device detected on Left: Product=0, Serial=0` | The connected device is **unprogrammed** (Serial -1 or 0). The SDK does **not** allow ReadParameters/WriteParameters on such devices. |
| `[SoundDesigner] ReadParameters(kActiveMemory) error: E_UNCONFIGURED_DEVICE` | Expected when the device is unprogrammed. The app still builds the UI from the **library** (default/param file values); it does not read live values from the device. |
| `[Right] Error: ErrorCode=E_SEND_FAILURE` / `[Right] --- port detection end ... Found=False` | No device on the Right port (or cable/hardware issue). Only the Left (or the side that succeeded) is used. |
| `[FittingVM] Item cache built: 16 left tabs, 0 right tabs, 1088 left items total` | Fitting page loaded successfully; only the left device is connected. |
| `[NVM] Restore M1` / `[NVM] Burn M1` / `[NVM] Verify M1 OK` | NVM-only flow: restore from NVM, burn to NVM, verify after save (per SDK sample presuite_memory_switch.py). |
| `[NVM] Verify M# FAIL: ...` | Save verification failed after burn; parameter mismatch after read-back. Some parameters (e.g. **X_AuxiliaryAttenuation**) are skipped in verification on known device/firmware variance — see **KnownVerifySkipIds** in `SoundDesignerSettingsEnumerator`. |

**Why “data not saved” or “settings didn’t change” when I save?** If the only connected device has **Serial=-1 or 0** (unprogrammed), the SDK will not accept writes. The app skips the write and shows: *"Device(s) unprogrammed (Serial=-1 or 0); save to device not available."* To actually save to the device, use a **programmed** hearing aid (non-zero Serial).

## Overview

The Fitting page appears after a successful connection to a hearing aid (Left and/or Right) via HI-PRO and the Sound Designer SDK (sdnet.dll). It shows device settings in **dynamic tabs** derived from each parameter’s **LongModuleName** (one tab per module; e.g. 9 tabs for E7111V2), with a **Memory 1–8** selector. Only the selected memory is read from the device on first load; other memories load on demand when the user switches the selector. The page supports **Live Mode** (debounced immediate write) and **Save to Device** (batch write with read-back). If only one side is connected, only that side’s UI card is shown.

## How Settings Are Discovered (SDK)

- **Initialization:** `SdkConfiguration.SetupEnvironment()`, then `SdkManager` with `sd.config` and `E7160SL.library`, then `IProduct` from the library. Same as Connect flow.
- **Connection:** `DeviceSessionService` holds `SdkManager` and `DeviceConnectionService` after Connect. Fitting uses `ConnectionService.GetConnection(DeviceSide.Left/Right)` to get the `ICommunicationAdaptor` per side.
- **Read:** The app uses **ReloadFromNvmAsync** when loading from device (on connect and memory switch) — logs `[NVM] Restore M#`, then **ReadMemorySnapshotAsync**, which:
  1. Calls `TrySelectMemoryContext(product, memoryIndex)` (SwitchToMemory or SelectMemoryIndex via reflection) so the SDK targets the correct memory.
  2. Calls `ReadParameters` for `kActiveMemory` and `kSystemActiveMemory` (BeginReadParameters / polling / GetResult()).
  3. Builds the snapshot via **SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, memoryIndex, side)** (~595 parameters per memory).
  **ReadAllSettingsAsync** is a backward-compatible wrapper that calls `ReadMemorySnapshotAsync(..., memoryIndex: 0)`.
- **Enumeration:** **SoundDesignerSettingsEnumerator** builds one **SettingCategory** per distinct **LongModuleName** from the parameters (grouped in `BuildSnapshotForMemory`). Only modules that have parameters appear as tabs; there is no hardcoded list of 13 tabs. Parameters come from `Product.Memories[memoryIndex].Parameters` (and system memory where applicable). Each collection element is inspected for properties such as Id, Name, DisplayName, ParameterId, Value, Unit, Min, Max, Step, ReadOnly, DataType, EnumValues and turned into a `SettingItem`.
- **Result:** `DeviceSettingsSnapshot` with one `SettingCategory` per module (dynamic count), each containing `SettingSection` and `SettingItem` list. Each `SettingItem` has DisplayName, CurrentValue (Value), DataType, Unit, Min/Max/Step, ReadOnly, and ParameterId for read/write.

## How Tab Mapping Is Determined

- **SoundDesignerSettingsEnumerator** builds one **SettingCategory** per distinct **LongModuleName** from the parameters (grouped in `BuildSnapshotForMemory`). Only modules that have parameters appear as tabs; there is no hardcoded list of tab IDs or titles.
- Parameters are enumerated from **Product.Memories[memoryIndex].Parameters** (and system memory where applicable). The **source** for the tab title is each parameter’s **LongModuleName** (or ShortModuleName); no separate CategoryToTabId dictionary is used for the primary path.
- To align with the SDK: if the Programmer’s Guide documents parameter group IDs or module names that differ from LongModuleName, extend the enumerator to use them while keeping tabs dynamic (one category per group/module).

## Live Mode and Save to Device

- **Live Mode ON:** UI changes update the in-memory snapshot. A debounce timer (400 ms) triggers a single `WriteSettingsAsync` after the last change, so the device is not flooded.
- **Live Mode OFF:** Changes stay local until the user clicks **Save to Device**.
- **Read (NVM-only):** On connect and memory load from device, the app uses **ReloadFromNvmAsync** (logs `[NVM] Restore M#`), which selects memory context then calls `ReadMemorySnapshotAsync` — semantics per SDK sample **presuite_memory_switch.py** (set CurrentMemory then ReadParameters loads NVM into RAM).
- **Save (NVM-only):** **Save Current Memory** / **Save ALL Memories** call **BurnMemoryToNvmAsync** (logs `[NVM] Burn M#`) then **VerifyMemoryMatchesNvmAsync** (logs `[NVM] Verify M# OK/FAIL`). Only Burn to NVM counts as save; no separate "Write to RAM" is exposed. **Persistence after disconnect** requires the SDK to support an **NVM write** (e.g. `BeginWriteParameters(int memoryIndex)` or `ParameterSpace.kNvmMemoryN`). The app discovers this at runtime via reflection; if no NVM write API is found, it logs **"Not supported by docs: no NVM write API found; persistence may fail after disconnect."** and continues with RAM-only write—saved values may then not appear after reconnect. Status line shows **Persisted to NVM** after success.
- **Reload from NVM:** Toolbar button reloads current memory from NVM (Restore + Read from RAM) for the selected memory.
- **Partial failure:** If one side (e.g. Right) fails to load or write, the other (Left) still loads and displays; error message is shown for the failed side.

## Session-end save (variable saving) flow

When the user chooses **Save to Device & End**, the flow that writes parameter (variable) values to the device is:

1. **SessionEndService** — When the user chooses Save to Device & End, it calls **RunSaveAllOnDispatcherAsync(session, onlyDirty: true)** on the UI thread. That uses **BurnMemoryToNvmAsync** and **VerifyMemoryMatchesNvmAsync** per (side, memory) — NVM-only; logs `[NVM] Burn M#`, `[NVM] Verify M# OK/FAIL`. After success, waits 500 ms before ClearSessionAsync.
2. **RunSaveAllOnDispatcherAsync** — For each dirty memory per side: **session.GetSnapshotsForMemory(memoryIndex)** → **SoundDesignerService.BurnMemoryToNvmAsync(...)** → **VerifyMemoryMatchesNvmAsync(...)** → ClearMemoryDirty on success.
3. **BurnMemoryToNvmAsync** — Logs `[NVM] Burn M#`, then calls **WriteMemorySnapshotAsync** (TrySelectMemoryContext, BuildWritableSnapshotForMemory, ApplySnapshotValuesToSdkParameters, WriteParameters for RAM, then **NVM write when SDK exposes it**—e.g. BeginWriteParameters(memoryIndex) or ParameterSpace.kNvmMemoryN—per vendor sample **presuite_memory_switch.py**; if no NVM write is found by reflection, logs "Not supported by docs" and only RAM is written), read-back, verify.
4. **SoundDesignerSettingsEnumerator.ApplySnapshotValuesToSdkParameters** / **TrySetParameterValue** — Pushes each snapshot item’s value back into the SDK Parameter objects (`SdkParameterRef`) so WriteParameters sends the user’s edits; without this, the device would receive stale in-memory state.

So “variable saving” = per (side, memory): GetSnapshotsForMemory → WriteMemorySnapshotAsync (TrySelectMemoryContext → BuildWritableSnapshotForMemory → ApplySnapshotValuesToSdkParameters → WriteParameters → verify) → ClearMemoryDirty on success. The save **result** (success/failure reason) is returned from the dispatcher delegate and used for the toast; do not rely on closure or TaskCompletionSource when changing this flow.

## Audiogram flow (input → validation → prescription → apply to session)

The **Audiogram** screen provides a clinical input layer that feeds into the same session snapshot and save path as the Fitting page:

1. **Input:** User enters thresholds (dB HL) in the table (standard frequencies 250–8000 Hz) for left and/or right ear. Data is held in `AudiogramSession` / `EarAudiogram` (see `App.Models.Audiogram`). Optional Load/Save to JSON file via `IAudiogramPersistenceService`.
2. **Validation:** `IAudiogramValidationService` validates frequency set, range (e.g. -10 to 120 dB HL), and completeness. Result (errors/warnings) is shown in the UI; Validate runs before prescription.
3. **Prescription:** `IPrescriptionEngine.ComputeTargets(session, options)` returns `PrescriptionTargets` (target gains per frequency and input level: soft/medium/loud). **Prescription formulas (e.g. NAL-NL2, DSL-v5) are not implemented yet;** the current implementation is a stub (`PrescriptionEngineStub`) that returns flat 0 dB. The interface is the extension point for adding real formulas when required and documented.
4. **Parameter mapping:** `IParameterMappingService.MapTargetsToDevice(targets, productKey, snapshot)` produces a `DeviceMappingResult` (parameter Id → value list). For E7111V2, `E7111V2ParameterMappingService` uses `GraphParameterMap.json` (WDRC gain param IDs by level 40/55/70/85/100 dB) to output updates. No direct SDK call; mapping only produces a list of (paramId, value).
5. **Apply to Fitting:** `IAudiogramIntegrationService.ApplyToFitting(mappingResult, side, memoryIndex)` gets the current snapshot from `DeviceSessionService.GetSnapshotsForMemory`, applies the parameter updates in place (finds `SettingItem` by Id, sets Value), calls `SetMemorySnapshot` and `MarkMemoryDirty`, then the caller refreshes the graphs panel. All device writes still go through the existing path (Fitting Save or Session End → `ApplySnapshotValuesToSdkParameters` → `WriteMemorySnapshotAsync`). No duplicate save path; no new SdkGate usage from Audiogram.

**Key types:** `App.Models.Audiogram` (AudiogramPoint, EarAudiogram, AudiogramSession, PrescriptionTargets, DeviceMappingResult), `App.Services.Audiogram.*` (validation, persistence, prescription stub, parameter mapping, integration), `App.ViewModels.AudiogramViewModel`, `App.Views.AudiogramView`.

## Configure Device (Manufacturing)

**Configured** means the device accepts **ReadParameters** (and thus WriteParameters). **ConfigureDevice** is a manufacturing step (Programmer's Guide Section 9.4) that writes a full parameter set to an unprogrammed device so that Read/Write then succeed. The Fitting page shows a **Configure** button only when there is an active session, at least one side is connected, and that side is **not** configured (e.g. `ReadParameters` throws `E_UNCONFIGURED_DEVICE`). The flow runs entirely inside **SdkGate** (one-at-a-time with other SDK use). It: (1) ensures the loaded library matches device firmware; (2) applies a .param template (e.g. `E7111V2.param`) to the session product if present; (3) calls the SDK’s ConfigureDevice (via reflection: `BeginConfigureDevice`/`EndConfigureDevice` or sync `ConfigureDevice`); (4) validates with ReadParameters; (5) sets the side configured and clears LastConfigError on success. Logging uses a dedicated scope: `[ConfigureDevice] HH:mm:ss.fff ...`. End Session and Connect Devices are disabled while Configure is running (`DeviceSessionService.IsConfigureRunning`). See `DeviceInitializationService.RunConfigureDeviceAsync` and `RunConfigureOneSideSync`.

## Key Types and Locations

| Item | Location |
|------|----------|
| Session | `App.Services.DeviceSessionService` |
| Configure Device flow | `App.Services.DeviceInitializationService` (RunConfigureDeviceAsync, EnsureLibraryMatchesFirmware, RunConfigureOneSideSync) |
| Read/write | `Device.ISoundDesignerService` / `SoundDesignerService` |
| Enumeration + dynamic tabs from LongModuleName | `Device.SoundDesignerSettingsEnumerator` |
| Models | `Device.Models`: `DeviceSettingsSnapshot`, `SettingCategory`, `SettingSection`, `SettingItem` (DisplayName, ParameterId, Value, DataType, Unit, Min, Max, Step, ReadOnly) |
| Fitting UI | `App.Views.FittingView`, `App.ViewModels.FittingViewModel` |

## TargetInvocationException and SDK Reflection

When the app uses **reflection** on the Sound Designer SDK (e.g. `PropertyInfo.GetValue` / `SetValue` on `IProduct` / Parameter objects), any exception thrown by the SDK is wrapped by .NET as **`System.Reflection.TargetInvocationException`**. The **real** error is in **`InnerException`**.

- **During library load / parameter enumeration:** Many `TargetInvocationException` messages can appear in the Debug output. These come from reading parameter properties (Value, DoubleValue, Min, Max, etc.) via reflection; the SDK throws for some parameters (e.g. wrong type, read-only, or uninitialized in offline product). The code catches them and continues (e.g. `TryGetValueDirect` returns null). **This is expected and does not break the app.**
- **During Save to Device:** If you see `[SettingsEnumerator] TrySetParameterValue(Value) failed: E_INVALID_OPERATION`, the SDK is rejecting the set. This typically happens when the **device is unprogrammed** (e.g. Serial=-1). The SDK reports `InitializeDevice` as configured, but **ReadParameters** / **WriteParameters** still return **E_UNCONFIGURED_DEVICE** or parameter setters throw **E_INVALID_OPERATION**. The session-end flow correctly shows **"Save to device failed. Session ended."** and the app continues.

**Clear indicators when save or device operations fail:**

- **Toast:** When "Save to Device & End" fails, the banner shows the **SDK error** (e.g. "Save to device failed: E_UNCONFIGURED_DEVICE: The device must be configured... Session ended.").
- **Log file:** All write failures are written to **log.txt** in the app directory with a single line: `[SoundDesigner] WriteToDevice failed: ...` and `[SessionEnd] SaveToDevice failed: ...`.
- **Debug output:** The same message is written with `Debug.WriteLine` so it appears in the Visual Studio **Output** window when debugging.

**To reduce duplicate TargetInvocationException lines (optional):**

1. **Exception Settings:** **Debug → Windows → Exception Settings** → **System.Reflection** → **uncheck "Thrown"** for **TargetInvocationException** so the debugger does not break on every reflection-wrapped SDK exception.
2. **Keep "Exception Messages" checked** in the Output window (right‑click Output) so you still see other first-chance exceptions when diagnosing device or save issues.

Reflection helpers are marked with **`[DebuggerNonUserCode]`**. To inspect the real SDK error, use **Exception.InnerException** (e.g. `SDLib.SDException`).

## Extending for Full SDK Parameter API

The current flow uses **Product.Memories** → **ParameterMemory** → **Parameters** and groups by **LongModuleName** in **BuildSnapshotForMemory**. If the Programmer’s Guide documents a different API (e.g. parameter blocks, domains, or module list), extend the enumerator to call it and continue grouping by LongModuleName (or equivalent) so tabs remain dynamic. Keep the reflection-based fallback so that products that expose parameters via other shapes still populate the same snapshot structure.
