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

**Why “data not saved” or “settings didn’t change” when I save?** If the only connected device has **Serial=-1 or 0** (unprogrammed), the SDK will not accept writes. The app skips the write and shows: *"Device(s) unprogrammed (Serial=-1 or 0); save to device not available."* To actually save to the device, use a **programmed** hearing aid (non-zero Serial).

## Overview

The Fitting page appears after a successful connection to a hearing aid (Left and/or Right) via HI-PRO and the Sound Designer SDK (sdnet.dll). It shows **all** device settings in a tabbed UI (13 tabs), supports **Live Mode** (debounced immediate write) and **Save to Device** (batch write with read-back). If only one side is connected, only that side’s UI card is shown.

## How Settings Are Discovered (SDK)

- **Initialization:** `SdkConfiguration.SetupEnvironment()`, then `SdkManager` with `sd.config` and `E7160SL.library`, then `IProduct` from the library. Same as Connect flow.
- **Connection:** `DeviceSessionService` holds `SdkManager` and `DeviceConnectionService` after Connect. Fitting uses `ConnectionService.GetConnection(DeviceSide.Left/Right)` to get the `ICommunicationAdaptor` per side.
- **Read:** `ISoundDesignerService.ReadAllSettingsAsync`:
  1. Calls `IProduct.ReadFromDevice(adaptor)` via reflection (per Programmer’s Guide).
  2. Builds snapshot via **SoundDesignerSettingsEnumerator.BuildFullSnapshot(product, side)**.
- **Enumeration:** `SoundDesignerSettingsEnumerator`:
  - Defines all 13 tab IDs/titles (QuickFit, Fine Tuning, User Controls, Experience Manager, Feedback Canceller, Frequency Lowering, Sound Manager, Tinnitus, Memories, Indicators, Fitting Summary, Data Log, Ready. Set. Hear.).
  - Iterates `IProduct` with reflection: all public parameter-less methods and readable properties that return `IEnumerable` are invoked. Each collection element is inspected for properties such as Id, Name, DisplayName, ParameterId, Value, Unit, Min, Max, Step, ReadOnly, DataType, EnumValues and turned into a `SettingItem`.
  - **Tab mapping:** SDK category/module/source name (e.g. method or property name) is mapped to a tab using `CategoryToTabId` and `MapCategoryToTabId`. Known names (e.g. "Feedback", "General", "Gain") map to the corresponding tab; unknown sources map to QuickFit. Add entries to `CategoryToTabId` in `SoundDesignerSettingsEnumerator.cs` when the SDK exposes module/category names.
  - If the SDK provides parameter domains/modules (see **docs/sounddesigner_programmers_guide.pdf**), extend `EnumerateAllParameters` to call the documented API (e.g. GetParameterBlocks, GetParameters) and map by group ID or parameter name using the same tab mapping table.
- **Result:** `DeviceSettingsSnapshot` with 13 `SettingCategory` items (one per tab), each containing `SettingSection` and `SettingItem` list. Each `SettingItem` has DisplayName, CurrentValue (Value), DataType, Unit, Min/Max/Step, ReadOnly, and ParameterId for read/write.

## How Tab Mapping Is Determined

- **SoundDesignerSettingsEnumerator** holds a static `CategoryToTabId` dictionary (category/module name → tab Id).
- When parameters are enumerated from the product, the **source name** (method or property name that returned the collection) is used as the section title and mapped to a tab via `MapCategoryToTabId`. Substring matching is used (e.g. "FeedbackCanceller" in the name → Feedback Canceller tab).
- To align with the SDK: if the Programmer’s Guide documents parameter group IDs or module names, add them to `CategoryToTabId` so parameters land in the correct tab.

## Live Mode and Save to Device

- **Live Mode ON:** UI changes update the in-memory snapshot. A debounce timer (400 ms) triggers a single `WriteSettingsAsync` after the last change, so the device is not flooded.
- **Live Mode OFF:** Changes stay local until the user clicks **Save to Device**.
- **Save to Device:** Before writing, `SoundDesignerSettingsEnumerator.ApplySnapshotValuesToSdkParameters(snapshot)` applies the edited UI snapshot back to the SDK product's Parameter objects (so the device receives the user's changes; without this, `BeginWriteParameters` would send the product's previous in-memory state and "return to settings" would show old data). Then `WriteSettingsAsync` is called for each connected side (Left/Right), followed by read-back for verification and snapshot refresh. Full exception and stack trace are logged to `log.txt`; SDException is translated to a user-friendly message (e.g. E_SEND_FAILURE → "Communication error. Check cable and contacts.").
- **Partial failure:** If one side (e.g. Right) fails to load or write, the other (Left) still loads and displays; error message is shown for the failed side.

## Session-end save (variable saving) flow

When the user chooses **Save to Device & End**, the full section that writes parameter (variable) values to the device is:

1. **SessionEndService** (`App.Services.SessionEndService`) — Orchestration: gets snapshots via `GetSnapshotsForSave()`, runs save on the UI thread via `InvokeAsync(Func<Task<(bool, string?)>>)` so the async save is fully awaited, then shows the toast from the **returned** result (no closure over locals). Unprogrammed devices (Serial=-1 or 0) are skipped and a clear message is returned.
2. **SoundDesignerService.WriteSettingsAsync** (`Device.DeviceCommunication.SoundDesignerService`) — Applies the snapshot to the SDK product with `ApplySnapshotValuesToSdkParameters(snapshot)`, then calls `BeginWriteParameters` for `kActiveMemory` and `kSystemActiveMemory`; on failure invokes `onWriteFailed(message)`.
3. **SoundDesignerSettingsEnumerator.ApplySnapshotValuesToSdkParameters** / **TrySetParameterValue** (`Device.DeviceCommunication.SoundDesignerSettingsEnumerator`) — Pushes each snapshot item’s value back into the SDK Parameter objects (`SdkParameterRef`) so `BeginWriteParameters` sends the user’s edits; without this, the device would receive stale in-memory state.

So “variable saving” = snapshot values → ApplySnapshotValuesToSdkParameters (TrySetParameterValue per item) → BeginWriteParameters. The save **result** (success/failure reason) is returned from the dispatcher delegate and used for the toast; do not rely on closure or TaskCompletionSource when changing this flow.

## Configure Device (Manufacturing)

**Configured** means the device accepts **ReadParameters** (and thus WriteParameters). **ConfigureDevice** is a manufacturing step (Programmer's Guide Section 9.4) that writes a full parameter set to an unprogrammed device so that Read/Write then succeed. The Fitting page shows a **Configure** button only when there is an active session, at least one side is connected, and that side is **not** configured (e.g. `ReadParameters` throws `E_UNCONFIGURED_DEVICE`). The flow runs entirely inside **SdkGate** (one-at-a-time with other SDK use). It: (1) ensures the loaded library matches device firmware; (2) applies a .param template (e.g. `E7111V2.param`) to the session product if present; (3) calls the SDK’s ConfigureDevice (via reflection: `BeginConfigureDevice`/`EndConfigureDevice` or sync `ConfigureDevice`); (4) validates with ReadParameters; (5) sets the side configured and clears LastConfigError on success. Logging uses a dedicated scope: `[ConfigureDevice] HH:mm:ss.fff ...`. End Session and Connect Devices are disabled while Configure is running (`DeviceSessionService.IsConfigureRunning`). See `DeviceInitializationService.RunConfigureDeviceAsync` and `RunConfigureOneSideSync`.

## Key Types and Locations

| Item | Location |
|------|----------|
| Session | `App.Services.DeviceSessionService` |
| Configure Device flow | `App.Services.DeviceInitializationService` (RunConfigureDeviceAsync, EnsureLibraryMatchesFirmware, RunConfigureOneSideSync) |
| Read/write | `Device.ISoundDesignerService` / `SoundDesignerService` |
| Enumeration + tab mapping | `Device.SoundDesignerSettingsEnumerator` |
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

When the Programmer’s Guide documents the exact API (e.g. parameter blocks, domains, or module list):

1. In **SoundDesignerSettingsEnumerator.EnumerateAllParameters**, call the documented methods (e.g. `product.GetParameterBlocks()`) and iterate the returned objects.
2. Map each block’s category/group to a tab using `CategoryToTabId`.
3. For each parameter object, fill `SettingItem` (Id, Name, DisplayName, ParameterId, Value, Unit, Min, Max, Step, ReadOnly, DataType, EnumValues) from the SDK types.
4. Keep the reflection-based fallback so that products that expose parameters via other shapes still populate the 13 tabs.
