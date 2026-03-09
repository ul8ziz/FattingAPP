# Ul8ziz.FittingApp

A Windows desktop application for hearing aid fitting, built with C# and WPF. The application integrates with the Ezairo Sound Designer SDK to communicate with hearing aid devices through wired programmers such as HI-PRO.

## Overview

Ul8ziz.FittingApp provides a modern, maintainable codebase for hearing aid fitting workflows. The solution follows a layered architecture that separates UI, business logic, device communication, and data persistence, enabling clean integration with the Ezairo Sound Designer SDK.

## Key Capabilities

- WPF-based user interface for fitting workflows
- Integration layer for Ezairo Sound Designer SDK
- Support for wired programmers (HI-PRO, DSP3, CAA, Promira)
- **Library-first / offline-first workflow**: browse parameters from the embedded *.library file without a device connected; device values overlay on connection
- **Embedded SDK assets**: sd.config, *.library, *.param files are bundled in `Assets/SoundDesigner/` inside the output—no external absolute paths required
- **LibraryService** enumerates available libraries, loads offline, builds parameter metadata; **FittingSessionManager** manages session lifecycle (offline → device attach → save)
- **FirmwareId → library auto-mapping**: on device connection, the app validates that the loaded library matches the detected firmware and auto-switches if needed
- **InitializeDevice gate**: `BeginInitializeDevice(adaptor)` / `EndInitializeDevice` is called before any `ReadParameters`/`WriteParameters` (Programmer's Guide Section 6.3).
- **Configure Device (Manufacturing)**: When the device is connected but not configured (e.g. unprogrammed, or `ReadParameters` throws `E_UNCONFIGURED_DEVICE`), a **Configure** button is shown on the Fitting page. It runs the SDK’s ConfigureDevice (Section 9.4): ensures library matches firmware, applies a .param template if present, calls ConfigureDevice, then validates with ReadParameters. On success, the device accepts Read/Write and Save is enabled. End Session and Connect Devices are disabled while Configure is running (SdkGate prevents concurrent SDK use).
- Session management (End Session dialog: Save & End, End Without Saving, Cancel) with automatic return to discovery
- Single connection-status display in the app shell header (no duplicate status on Fitting page)
- **Functional verification:** SDK calls are serialized via `SdkGate`; read/write only run after the device is configured. HI-PRO preflight and diagnostic reports can be generated via startup or command-line scripts (see Diagnostics section below); there is no in-app Diagnostics page.
- Modular architecture for maintainability and testing

## Technology Stack

- **.NET 10** with Windows Presentation Foundation (WPF) (required by current sdnet.dll; see note below)
- **C#** for application logic
- **Ezairo Sound Designer SDK** for device communication (sdnet.dll, CTK)
- **x86 (32-bit)** for HI-PRO/FTDI compatibility; build with `Platform=x86`

## Repository Structure

The solution contains the WPF app, the device/SDK layer, and optional tools. SDK assets are embedded in the App output; no separate Core/Data/Shared projects in the solution.

```
FattingAPP/
├── Ul8ziz.FittingApp.sln
├── global.json                    # .NET SDK 10.0
├── src/
│   ├── App/                       # WPF application (entry: Ul8ziz.FittingApp.App)
│   │   ├── Views/, ViewModels/, Services/, Helpers/, Models/
│   │   ├── DeviceCommunication/   # HiPro D2XX (D2xxLoader, HiProService, etc.)
│   │   ├── Assets/SoundDesigner/   # sd.config, *.library, *.param
│   │   ├── NativeDeps/HI-PRO/     # ftd2xx.dll, FTD2XX_NET.dll, HiProWrapper.dll
│   │   └── Properties/PublishProfiles/Release-win-x86.pubxml
│   ├── Device/
│   │   └── DeviceCommunication/   # SDK interop, SdkGate, scan, discovery, LibraryService, etc.
│   └── Tools/
│       ├── Ftd2xxTest/            # D2XX probe console app
│       └── HiproD2xxProbe/        # HI-PRO D2XX probe
├── installer/                     # Inno Setup script (FittingApp.iss) → setup.exe
├── scripts/                       # publish-release.ps1, diagnostics, check-ftdi, etc.
├── docs/                          # HI-PRO_D2XX, FITTING_DEVELOPER_NOTE, SCAN_REQUIREMENTS, etc.
├── publish/                       # Publish output (gitignored): FittingApp/, *.zip, *-Setup-*.exe
└── SoundDesignerSDK/              # Ezairo SDK (read-only; must exist at repo root for setup)
```

### Build and installer summary

| Action | Command | Output |
|--------|---------|--------|
| Build | `dotnet build` (or Visual Studio, config **x86**) | `src\App\bin\x86\Debug\net10.0-windows\` or Release |
| Publish (self-contained) | `.\scripts\publish-release.ps1` | `publish\FittingApp\` (run `Ul8ziz.FittingApp.App.exe` without installing .NET) |
| ZIP for distribution | `.\scripts\publish-release.ps1 -CreateZip` | `publish\FittingApp-Release.zip` |
| Windows installer | `.\scripts\publish-release.ps1 -BuildInstaller` | `publish\FittingApp-Setup-1.0.0.exe` (Inno Setup; downloaded to `tools\InnoSetup` if needed) |

Publish profile: `src/App/Properties/PublishProfiles/Release-win-x86.pubxml` (win-x86, self-contained). The installer script is `installer/FittingApp.iss`; it installs to Program Files (x86), Start Menu, Add/Remove Programs, optional desktop icon, and requires Windows 10+.

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

**Detecting errors when testing or building:** Build errors appear in **View → Output** with **Show output from: Build**. When you run with F5, switch the Output window to **Show output from: Debug** to see all runtime messages (startup, discovery, save, SDK errors). If you run without the debugger, open **log.txt** in the same folder as the executable (e.g. `src\App\bin\x86\Debug\net10.0-windows\log.txt`). See **[docs/FITTING_DEVELOPER_NOTE.md](docs/FITTING_DEVELOPER_NOTE.md)** for the full table and an **"Interpreting your debug output"** section that explains lines like `TargetInvocationException`, `E_UNCONFIGURED_DEVICE`, `Serial=-1` (unprogrammed device), and `E_SEND_FAILURE` (e.g. Right port).

See **[docs/HI-PRO_D2XX.md](docs/HI-PRO_D2XX.md)** for setup steps and common errors.

**Hearing-aid discovery (wired HI-PRO):** After "Scan Wired" finds HI-PRO via D2XX, use **Discover Devices**. **HiProWiredDiscoveryService** returns a **DiscoveryResult** (FoundLeft, FoundRight, per-side SerialId/FirmwareId/ProductId, Errors list). Discovery is per-port: Left is tried first, then Right; if one side fails (e.g. E_SEND_FAILURE, no device), the other is still returned—partial success is supported. Only the **detected** ear(s) are shown as device cards; the undetected ear card is not displayed. **Connection** works with a single selected ear: only detected-and-selected sides are connected; a **ConnectionResult** reports per-ear status. No COM/SerialPort. Wireless programmers use DeviceDiscoveryService.

**Connect Devices UX:** The Connect Devices screen uses a two-panel layout: **Setup** (left) — Product Library dropdown, Programmer selection (cards with radio), and Advanced expander; **Discover & Connect** (right) — **Scan for Hearing Aids** button, Rescan/Stop, then a list of **detected devices only** (cards with checkbox, serial/firmware, battery bar, Rescan/Stop). A **status bar** under the title shows programmer state (e.g. "HI-PRO Found | USB OK | Driver OK") with Scan Wired, Scan Wireless, Help, and Cancel. Footer: **Selected: Left (1)** summary and green **Connect** button (disabled when none selected; shows "Connecting…" while connecting). After a successful connection, the app **automatically navigates to the Fitting** screen; the **top header** (app shell only) shows "Connected" (green) and the connected device name (e.g. model or FirmwareId with Left/Right/L+R). **Navigation** (Audiogram, Fitting, Session Summary) is enabled only when connected; **AppSessionState** holds connection state, display name, and per-ear identity (model, firmware, serial). **Connection status is shown only in the global top header**—no duplicate status block on the Fitting page.

**Navigation:** The sidebar contains **Connect Devices**, **Audiogram**, **Fitting**, and **Session Summary** (disabled until implemented). All screens bind to a single **AppSessionState** for connection status; only the shell header updates when connection changes.

**Session Management:** There is no patient management or diagnostics in the flow. **DeviceSessionService** tracks the active session: connected devices (left/right, serial/firmware), active Product and adaptors, **dirty** state (settings changed since last read/save), and **Live Display** (live mode) state. When the user clicks **End Session** (prominent button in the top bar when a session is active), a **modal dialog** opens with three actions only (no text input):
- **Save to Device & End** — Stops live mode, commits current parameters to each connected hearing aid via the SDK write/commit mechanism. A **waiting dialog** ("Saving to device… Please wait.") is shown during the save. Save is **skipped** with a clear message if: (1) **unprogrammed devices** (Serial=-1 or 0); (2) **device not configured** (InitializeDevice did not return true for that side). In those cases the app does not call WriteParameters and returns a failure reason. **If save to device fails** (write or verification), the session is **not** ended as success: the error message is displayed (banner and log), the app disconnects and navigates to **Connect Devices** in **connection inactive** state so the user can **select the headset again** and reconnect. Only when save **succeeds** does the app show "Saved to device. Session ended." and then navigate to Connect Devices.
- **End Without Saving** — Does not write to the device; stops live mode; disconnects and releases resources; shows banner "Session ended without saving."
- **Cancel** — Closes the dialog and returns to the Fitting screen unchanged.

If only one device is connected (e.g. left ear only), the dialog states that saving will apply only to the connected device(s). After **Save & End** (on success) or **End Without Saving**, the app force-disconnects, navigates back to **Connect Devices** (with connection inactive so headset selection is available), and **automatically starts a new wired discovery scan** after a short debounce (500 ms). Session-end steps are logged (e.g. `SessionEnd: StopLiveMode`, `SessionEnd: SaveToDevice (Left/Right)`, `SessionEnd: Disconnect`, `SessionEnd: RestartDiscovery` or `SessionEnd: NavigateToConnect after save failure`). SDK calls run off the UI thread; UI updates use the Dispatcher; resources are released in `finally` blocks.

**Save-Settings workflow (formalized):** The app follows a clear save pipeline: **Connected → Configured → Synced → Dirty → Save → Written → Verified**. UI edits update the SDK parameter cache (via `ApplySnapshotValuesToSdkParameters`); the device is updated only when **Save to Device** or **Save & End** runs `WriteParameters`. **Configuration gate:** Both Fitting **Save to Device** and **Session-end Save** check that the device is configured (`IsDeviceConfigured` / `IsSideConfigured`). If not configured, Save is disabled (Fitting) or skipped (session end) with message: "Cannot save – device must be programmed/configured." After Connect, the app runs **SyncDeviceStateFromConnectionAsync** and **EnsureInitializedAndConfiguredAsync** so session state reflects actual read capability; if the SDK throws **E_UNCONFIGURED_DEVICE** (e.g. unprogrammed device), `IsDeviceConfigured` is set false and **LastConfigError** is set so Save stays disabled and the status line shows "Not configured." **Fitting status line** under the toolbar shows: Connected | Configured / Not configured | Dirty / Saved. **Save logging:** Every save logs selected memory index, ParameterSpaces written, duration per space, and result to Debug and `log.txt` (ScanDiagnostics). **Verification:** After write, the app re-reads the same ParameterSpaces and compares a sample of parameters (up to 50) with the snapshot; on mismatch, save fails with an actionable message (e.g. "Verification failed: parameter 'X' did not match after read-back.").

**Device initialization gate (single gate):** All Read/Write to the device are guarded by **DeviceInitializationService.EnsureInitializedAndConfiguredAsync(session, side, ct)**. It runs inside **SdkGate** (single SemaphoreSlim) so only one SDK operation runs at a time; it must be called from the UI/STA thread. It ensures InitializeDevice(adaptor) has completed and, if the SDK reports not configured or throws **E_UNCONFIGURED_DEVICE**, sets **IsConfigured** false and **LastConfigError**. Session state is exposed on **DeviceSessionService**: **IsDeviceConnected**, **IsInitialized**, **IsConfigured**, **LastConfigError**, **DeviceFirmwareId** / **LeftSerial** / **RightSerial** / **LeftModel** / **RightModel**. The gate is called: (1) immediately after **Connect** succeeds (per side); (2) at the start of **LoadSettingsAsync** (FittingViewModel) before any ReadParameters; (3) before **SaveToDeviceAsync** (FittingViewModel) and before **SessionEndService.RunSaveOnDispatcherAsync**. If **IsConfigured** is false, the UI shows "Connected | Not configured", disables Save to Device, and shows a banner with **LastConfigError** (e.g. "Device must be configured (InitializeDevice returned false)."). Logs use the prefix `[EnsureInit]` and are written to Debug and ScanDiagnostics so the exact step that failed is visible.

**Patch points (guard applied):**  
- `src/App/Services/DeviceInitializationService.cs`: **EnsureInitializedAndConfiguredAsync** (gate implementation).  
- `src/App/Services/DeviceSessionService.cs`: state model (**IsDeviceConnected**, **IsInitialized**, **IsConfigured**, **LastConfigError**, **SetSideInitialized** / **SetSideConfigured** / **SetLastConfigError**).  
- `src/App/Views/ConnectDevicesView.xaml.cs`: after **SetSession** and **SyncDeviceStateFromConnectionAsync**, calls **EnsureInitializedAndConfiguredAsync** for Left and Right.  
- `src/App/ViewModels/FittingViewModel.cs`: **LoadSettingsAsync** (guard at start before ReadParameters); **SaveToDeviceAsync** (guard before WriteParameters); **PropertyChanged** subscription to session so status/tooltip update when **IsConfigured** changes.  
- `src/App/Services/SessionEndService.cs`: **RunSaveOnDispatcherAsync** (guard before **WriteSettingsAsync**).  
- `src/App/Services/FittingSessionManager.cs`: **IsDeviceConfigured** now uses **DeviceSessionService.Instance.IsConfigured** when session is active.  
- `src/Device/DeviceCommunication/SdkGate.cs`: **RunAsync** preserves synchronization context so SDK continuations stay on STA thread.

**Fitting page (dynamic tabs from device parameters):** Tabs are **not hardcoded**. When the user tries **Save to Device** with an unprogrammed device (Serial=-1 or 0), the error banner shows the unprogrammed message and the **Retry** button is hidden (retrying would not help until a programmed device is connected); for other errors, Retry re-reads parameters from the device. **Save to Device** is disabled when the device is not configured; the button tooltip shows "Cannot save – device must be programmed/configured." when applicable. After connection, the app reads device parameters following the official SDK Programmer's Guide flow:
1. **InitializeDevice(adaptor)** is called first (via `BeginInitializeDevice` / `EndInitializeDevice` async pattern, Section 6.3). This returns `IsConfigured=true` if the device is ready.
2. **ReadParameters** is called for system memory (`kSystemActiveMemory`) and active program memory (`kActiveMemory`) using `BeginReadParameters` / polling / `GetResult()` (Section 6.6). All SDK calls run on the **UI/STA thread** with `await Task.Delay()` for non-blocking polling — **never** inside `Task.Run()`, because the SDK COM objects have thread affinity and calling them from a ThreadPool/MTA thread causes an Access Violation (0xc0000005) in `sdnet.dll`.
3. Parameters are enumerated from `Product.Memories` → `ParameterMemoryList` → `ParameterMemory.Parameters` → `ParameterList` → each `Parameter`.
4. Each parameter's `LongModuleName` (or `ShortModuleName`) determines which **tab** it belongs to. Tabs are created dynamically—only modules that have parameters for the connected product/firmware appear.
5. If the Memories API is not available (older SDK), the service falls back to reflection-based enumeration.

**Configure Device (Manufacturing):** If the device is connected but not configured (e.g. unprogrammed, or `ReadParameters` throws `E_UNCONFIGURED_DEVICE`), the Fitting page shows a **Configure** button. Clicking it runs a full Configure flow (Section 9.4): library is matched to firmware, a .param template is applied if available, the SDK’s ConfigureDevice is invoked, and success is validated with ReadParameters. After a successful configure, the device accepts Read/Write and Save is enabled. The normal fitting sequence is `InitializeDevice` → (optionally **Configure Device** if not configured) → `ReadParameters` → `WriteParameters`.

### E7111V2 Memory-Aware Fitting Workflow

The app now follows a **memory-aware, library-first** workflow specifically designed for the Ezairo 7111 V2:

1. **Single-memory display**: Instead of rendering all 8 memories at once (4760 parameters), the app shows only the selected memory (~595 parameters). A **Memory Selector** (ComboBox: Memory 1-8) in the Fitting toolbar lets the user switch between memory profiles instantly.

2. **Automatic .param loading**: When a library is selected (e.g., `E7111V2.library`), the app auto-loads the matching `.param` file (`E7111V2.param`) from the products directory. This applies the preset values to the offline product, so parameter controls show meaningful defaults instead of zeros.

3. **Per-memory snapshots**: `SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, memoryIndex, side)` enumerates only one `ParameterMemory` (via `Product.Memories[index]`). Each module tab has a single section instead of 8 stacked Memory sections.

4. **True UI Virtualization**: The Fitting page uses a `Grid` layout (no outer `ScrollViewer`) with `ListView` + `VirtualizingStackPanel` for parameter rendering. This gives the list a constrained height so only visible items (~10-15) are rendered, not all ~300+ in a tab. The previous approach wrapped everything in a `ScrollViewer`, which gave infinite height and defeated virtualization.

5. **Cached Tab Items**: `FittingViewModel` pre-builds `SettingItemViewModel` wrappers once per snapshot load (`BuildItemCaches`), stored per-tab in a `Dictionary`. Tab switching now just filters the cached list — no object recreation. This eliminates the `BuildFilteredCategories` bottleneck that created new ViewModels on every tab change.

6. **Professional Per-Type Parameter UI**: Each parameter row uses a 3-column layout:
   - **Bool**: Label on the left, CheckBox toggle on the right
   - **Double/Int**: Label + Slider with min/max labels + formatted value with unit
   - **Enum**: Label + ComboBox dropdown
   - **String**: Label + read-only value display
   
   Parameters also show description subtitles and step-aware slider snapping.

7. **Premium parameter field redesign (Fitting page):** Parameter controls use a modern, enterprise-grade UI:
   - **ParameterFieldCard** (`Views/Controls/ParameterFieldCard.xaml`): Each parameter is rendered as a card row with title, optional description, value control on the right, unit badge, optional reset button, and dirty (unsaved) indicator.
   - **ParameterFieldStyles.xaml**: Shared styles for card, labels, unit badge, modern ComboBox, Slider, Toggle switch, and numeric input. Collapsible **Expander** per tab shows group header (tab title + param count).
   - **Virtualization**: ListView with VirtualizingStackPanel is retained for smooth scrolling with many parameters. Bindings and parameter load/save logic are unchanged.

8. **Graphs panel (Fitting page):** Sound Designer–style graphs sidebar:
   - **Show graphs / Hide graphs** toggle in the Fitting toolbar; collapsible left sidebar with **Freq. Gain**, **I/O (Input/Output)**, **Input Signal** (placeholder), **Transducers** (placeholder).
   - **Freq. Gain**: Curve levels (40, 55, 70, 85, 100 dB) with checkboxes, octave selector (1/1, 1/2, 1/3), and **PlotControl** (Canvas + Polyline, axes, grid, legend). **I/O**: Input/Output curves with frequency legend.
   - **Data**: Graphs are computed from **cached snapshot** only; no device read on graph update. **Refresh** triggers a single batch read then redraw. **IGraphService** + **GraphService** build series; **GraphParameterMap.json** (per library/product) defines parameter IDs for freq gain and I/O; **GraphParameterMappingService** loads the map. If mapping is missing, a non-blocking message is shown.
   - **PlotControl** (`Views/Controls/PlotControl.xaml`): WPF-native lightweight plot (axes, grid, multiple series). **GraphsPanelViewModel** uses debounced updates (≈100 ms) and virtualized curve list. Works in **offline** (library) and **live** (device) modes.
   - **E7111V2 graph mapping**: `GraphParameterMap.json` includes entries for **E7111V2** and **E7111V2.library** with WDRC gain param IDs (`X_WDRC_LowLevelGain`, `X_WDRC_HighLevelGain`) for Freq. Gain levels (40–100 dB) and I/O frequencies (1000–6500 Hz), so the Audiogram graphs show real data when an E7111V2 session is active. **Parameter list export**: When the Graphs panel receives a snapshot for E7111V2, the app writes **ParameterIds_E7111V2.txt** once per session to the app base directory (Id, Name, ModuleName per parameter) for mapping discovery or debugging.

7. **TryGetValueDirect (reflection fix)**: The `_failedGetters` cache in `SoundDesignerSettingsEnumerator` caused cross-contamination: marking `BooleanValue` as "failed" for a Double parameter blocked it for all subsequent Boolean parameters (since all SDK Parameters share one .NET Type). Value-specific properties now use `TryGetValueDirect()` which bypasses the global cache.

**Fitting performance plan (Sound Designer–style):** To avoid slow UI and dispatcher storms with large parameter sets, the Fitting page uses:

- **Lazy tabs and groups:** Only tab headers (names + group counts) are built at load. When the user selects a tab, that tab’s **group list** is built (metadata only). When a group **Expander** is opened, **rows** (SettingItemViewModel) for that group are built and assigned once on the UI thread. Descriptors: **TabDescriptor** (Id, Title, GroupsCount, IsLoaded, Groups), **GroupDescriptor** (Id, Title, ParamsCount, IsLoaded, Rows). APIs: **EnsureTabLoadedAsync(tabId)**, **EnsureGroupLoadedAsync(side, groupId)**.
- **Settings cache and dirty buffer:** **SettingsCache** holds snapshots per side; **DirtyBuffer** tracks unsaved changes by (Side, ParamId). After a batched read (one per side when entering Fitting or after Configure), UI reads from cache. Value changes update the snapshot and add to the dirty buffer; **Save to Device** writes the snapshot, clears the buffer, and invalidates cache for that side. **InvalidateCacheForSide(side)** is used after writes, configure, reconnect, or library switch.
- **Writes buffered:** Slider/Combo changes update cache + mark dirty; no per-slider SDK write. Writes happen only on **Save to Device** (or optional debounced commit if enabled later).
- **Single-assign updates:** Filtered and group row collections are built off-thread where possible and assigned once on the UI thread to avoid dispatcher storms. Search/filter uses a **200 ms debounce**.
- **Virtualized ListView:** Each group’s parameter list uses **VirtualizingStackPanel.IsVirtualizing="True"**, **VirtualizationMode="Recycling"**, **ScrollViewer.CanContentScroll="True"**. Row UI uses **DataTemplates** (Slider, Combo, Toggle, Numeric, Readonly) via **ParameterRowTemplate** and **ParameterFieldCard**.
- **\[Perf\] logging:** Stopwatch timings are written to Debug output: BuildTabHeaders, LoadTab, LoadGroup, ReadSpace (per side), RenderRows, SaveToDevice (with dirty count). Use these to verify improvements.

**Files:** `src/App/Services/SettingsCache.cs` (cache + DirtyBuffer), `src/App/Models/TabDescriptor.cs`, `src/App/Models/GroupDescriptor.cs`, `FittingViewModel.cs` (cache, descriptors, lazy load, debounce), `FittingView.xaml` (ItemsControl of LeftGroups/RightGroups, Expander per group, ListView per group Rows).

8. **ParamFileService**: New service for loading/saving `.param` files (JSON format matching SDK sample `Param.cs`). Supports `LoadAsync`, `SaveAsync`, `CreateFromProduct`, and `FindParamForLibrary`.

9. **LibraryService extensions**: `GetMemory(index)`, `GetSystemMemory()`, `ApplyParamToProduct(paramFile, memIndex)` — applies `.param` values to the SDK product using the `SetValueFromFile` pattern from the SDK sample.

10. **SessionPhase expansion**: `NoSession → OfflineLibrary → ParamApplied → DeviceAttached → Configured → Synced`

11. **Fast loading (single-memory snapshot):** When reading from a connected device, `SoundDesignerService.ReadAllSettingsAsync` builds the parameter list with **BuildSnapshotForMemory(product, 0, side)** instead of **BuildFullSnapshot**. That enumerates only Memory 0 (~595 parameters) instead of all 8 memories (~4760), which:
    - Greatly reduces "Reading device parameters…" time and avoids thousands of reflection/TargetInvocationException logs.
    - Keeps the fitting UI responsive; the Memory selector still allows switching to other memories (each loads that memory’s snapshot on demand via the same enumerator).

12. **E_UNCONFIGURED_DEVICE:** The SDK can return `IsConfigured=true` from `InitializeDevice` but still throw **E_UNCONFIGURED_DEVICE** when calling `ReadParameters` (e.g. unprogrammed or incompletely configured device). When that happens, the app still builds a snapshot from Memory 0 (library/default values) so the UI shows parameters; the device may need to be configured (e.g. via manufacturing tools) before read/write will succeed. The **Hard gate** in `FittingSessionManager` skips `ReadParameters` entirely only when `InitializeDevice` returns `IsConfigured=false`.

13. **Loading UX:** A consistent loading design is used during any async load (parameters, save, refresh). **Fitting:** Semi-transparent overlay with a centered card (white, subtle shadow), indeterminate progress bar, and status message (e.g. "Reading device parameters…", "Saving to Left device…"). **Connect Devices:** Discovery shows a card with status text and indeterminate bar; connection shows a card with determinate progress and message. Shared styles in `Styles.xaml`: `LoadingOverlayCardStyle`, `LoadingIndeterminateProgressBarStyle`, `LoadingMessageStyle`, `LoadingOverlayBrush`.

**Files added**: `ParamFileService.cs`
**Files modified**: `SoundDesignerSettingsEnumerator.cs`, `LibraryService.cs`, `FittingSessionManager.cs`, `FittingViewModel.cs`, `FittingView.xaml`, `ConnectDevicesView.xaml.cs`

### SDK Safety Architecture (Access Violation Prevention)

The following safeguards prevent the `0xC0000005 Access Violation` crash that occurs when SDK state is corrupted:

1. **Single ProductManager ownership:** Exactly **one** `ProductManager` (via **one** `SdkManager`) exists at any time. It is created only in **DeviceSessionService.EnsureSdkReadyForScanAsync** (inside **SdkGate**). **LibraryService**, **DeviceDiscoveryService**, **DeviceConnectionService**, and **HiProWiredDiscoveryService** all use the **same** shared `SdkManager` reference—no second ProductManager for discovery or connection.

2. **SdkGate (global serialization):** All `sdnet.dll` calls are serialized through **SdkGate** (`SdkGate.cs`): `Run`, `Run<T>`, `RunAsync`, `InvokeAsync`/`InvokeAsync<T>`. The gate uses a single `SemaphoreSlim(1,1)` so only one SDK operation runs at a time. New work is **rejected** when **BeginDispose()** has been called. **DrainAsync()** waits until all running work completes; **RunCleanupToDispose** runs teardown (Cleanup + Dispose) on the gate and then marks the gate disposed. **ResetForNewSession()** clears the disposed state so a new session can start. Logging: `[SdkGate] enqueue/start/end op=...`, `[Dispose] cancel -> drain -> close -> dispose complete`.

3. **SdkLifecycle state:** A global **SdkLifecycle** state (`Uninitialized` → `Initializing` → `Ready` → `Disposing` → `Disposed`) prevents re-initialization during teardown. **ConnectDevicesView** does not call SDK init when **HasActiveSession** or **SdkLifecycle.IsDisposingOrDisposed**. **SdkManager.Initialize** / **ReloadForFirmware** throw if lifecycle is Disposing/Disposed.

4. **Safe session teardown (ClearSessionAsync):** End Session uses **DeviceSessionService.ClearSessionAsync()**: (A) Stop Live Mode, (B) Cancel in-flight operations (session CTS), (C) **SdkGate.BeginDispose()** then **await SdkGate.DrainAsync()**, (D) **SdkGate.RunCleanupToDispose** to run **ConnectionService.Cleanup()** and **SdkManager.Dispose()** on the gate, (E) clear references and **SdkGate.ResetForNewSession()**. **SessionEndService** awaits **ClearSessionAsync()** before navigating or restarting discovery—no use-after-free.

5. **Wired discovery uses shared SdkManager:** **HiProWiredDiscoveryService** takes the **shared SdkManager** in its constructor (from **EnsureSdkReadyForScanAsync**). All discovery SDK calls (GetCommunicationInterfaceCount, CreateCommunicationInterface, BeginDetectDevice, EndDetectDevice, CloseDevice) run inside **SdkGate.Run**. No separate local SdkManager is created for discovery.

6. **Firmware-matched library reload (`SdkManager.ReloadForFirmware`):** Before connecting to a device, the app runs **ReloadForFirmware(detectedFirmware)** **inside SdkGate** (from ConnectDevicesView). If the library did not match the device firmware, the correct library is loaded; using a mismatched `IProduct` with `BeginInitializeDevice` is a primary cause of Access Violation.

7. **Hard gates in ConnectDevicesView:** Pre-connect: blocks if FirmwareId is empty. Post-connect: rejects only if firmware is empty; Serial=-1/0 is normal for unprogrammed devices.

8. **Hard gates in FittingSessionManager / DeviceInitializationService:** Read/Write and Configure are guarded by **EnsureInitializedAndConfiguredAsync** and run through **SdkGate** where required.

9. **SD_CONFIG_PATH enforcement:** `SdkConfiguration.SetupEnvironment()` sets `SD_CONFIG_PATH` to `Assets\SoundDesigner\sd.config` so the SDK resolves paths correctly.

Parameter controls are rendered by `Parameter.Type`:
- **Boolean** → Checkbox (`BooleanValue`)
- **Double** → Slider + numeric display (`DoubleValue`, `DoubleMin`, `DoubleMax`)
- **Integer** → Slider + numeric display (`Value`, `Min`, `Max`)
- **Indexed/Text list** → Dropdown (`ListValues`, `TextListValues`)

Each parameter shows `Name`, `Units`, and `Description` (as tooltip). Min/max validation is enforced. A **Refresh** button re-reads all parameters from the device. An error banner with **Retry** appears if the connection service is unavailable.

Only **Left Ear** and/or **Right Ear** cards for **connected** sides are shown (no card for a missing side). The page shows **only** fitting controls (tabs, parameters) and top actions (**Save to Device**, **Live Mode**, **Refresh**); it does **not** repeat connection status (that appears only in the app shell header). **Enable Live Mode** (debounced write on change) and **Save to Device** (write + read-back via `WriteParameters`/`ReadParameters`) are supported. Before writing, the edited snapshot values are applied to the SDK product's Parameter objects (`ApplySnapshotValuesToSdkParameters`) so that the device receives the user's changes and returning to the settings view shows the saved data. SDException is caught, logged to `log.txt`, and shown as a user-friendly message; one side can fail while the other loads.

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
- **`logs\diagnostics\HI-PRO_DiagnosticReport.md`** — report written when run from app startup
- `logs\diagnostics\ports.json`, `handshake.json`, `dlls.json`, `blockers.json` — raw JSON
- `logs\hpro_diag.log` — startup run log
- `logs\diagnostics\startup_runner_stdout.txt`, `startup_runner_stderr.txt` — captured when run from app

**From the app:** At startup, the app runs preflight + diagnostics in the background (no UI block) before any SDK/scan; if scripts are missing, the app continues normally. To re-run diagnostics, use the command line (see below).

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

### Creating an installable release

To build a **runnable, self-contained** version that you can copy to another PC or package for installation:

1. **Publish the app** (from the repository root):
   ```powershell
   .\scripts\publish-release.ps1
   ```
   This builds a Release, **win-x86**, **self-contained** output into `publish\FittingApp\`. No .NET runtime needs to be installed on the target machine.

2. **Optional: create a ZIP** for distribution:
   ```powershell
   .\scripts\publish-release.ps1 -CreateZip
   ```
   This creates `publish\FittingApp-Release.zip`. Unzip on the target PC and run `Ul8ziz.FittingApp.App.exe`.

3. **Conventional Windows installer (setup.exe)**  
   From repo root run:
   ```powershell
   .\scripts\publish-release.ps1 -BuildInstaller
   ```
   This publishes the app and builds **FittingApp-Setup-1.0.0.exe** in `publish\`. If Inno Setup is not installed, the script downloads and installs it once into `tools\InnoSetup`. The resulting setup is a standard Windows installer: wizard, default path under Program Files (x86), Start Menu group, Add/Remove Programs, optional desktop icon. Run the setup on the target PC to install like any other application (Windows 10+).

**One-liner for full release (publish + ZIP + setup.exe):**
   ```powershell
   .\scripts\publish-release.ps1 -CreateZip -BuildInstaller
   ```

**Note:** The published app is 32-bit (x86) and includes HI-PRO/FTDI native DLLs. Target machines still need the [SDK runtime prerequisites](#sdk-runtime-prerequisites) (e.g. CTK Runtime, VC++ redistributables) if they use wired programmers.

## Documentation

- **[docs/HI-PRO_D2XX.md](docs/HI-PRO_D2XX.md)** — D2XX setup, x86, DLL locations, common errors.
- **[docs/FITTING_DEVELOPER_NOTE.md](docs/FITTING_DEVELOPER_NOTE.md)** — Interpreting debug output (log.txt, Output window), build vs runtime, key types and locations.
- **[docs/SCAN_REQUIREMENTS.md](docs/SCAN_REQUIREMENTS.md)** — Programmer scan requirements and troubleshooting.
- **Setup:** [docs/Setup.md](docs/Setup.md) (if present); otherwise see [Prerequisites](#prerequisites) and [Setup Steps](#setup-steps) in this README.
- **Installer:** [installer/README.md](installer/README.md) — How to build the Windows setup (Inno Setup) manually.

### HI-PRO preflight and diagnostics (files changed/added)

- **HI-PRO preflight (startup, before CTK):** Runs asynchronously at app startup. **Steps:** (A) Write diagnostics to `logs/hpro_diag.log` (timestamp, bitness, paths, PATH trimmed, FTDI DLL candidates, loaded ftd2xx path via GetModuleHandle/GetModuleFileName); (B) Set DLL directory to AppBase first; (C) Detect HI-PRO device and confirm VCP driver (FTSER2K) via PnP; (D) Check COM2 availability (open/close test); (E) If COM2 locked and **AutoFreeComPortOnStartup** is true: enumerate suspect processes (Inspire, Starkey, HiPro, etc.), log PID/path, attempt CloseMainWindow then Kill, set HiProMonitor service to Manual; (F) Retry COM2 open up to 3 times; (G) If still locked, abort wired scan and show clear UI message. No system DLL modification.
- **Config switch:** `HiproPreflight.AutoFreeComPortOnStartup` (default `true`). When `false`, preflight only detects and logs; does not kill processes or change services.
- **Added:** `src/Device/DeviceCommunication/NativeDllResolver.cs` — P/Invoke: SetDllDirectory, AddDllDirectory, GetModuleHandle, GetModuleFileName.
- **Added:** `src/Device/DeviceCommunication/PreflightLog.cs` — File append logger for preflight with rotation at 5MB.
- **Added:** `src/Device/DeviceCommunication/HiproPreflight.cs` — `RunAsync()`, `EnsureCompletedAsync()`, `LastResult`, Steps A–G, `SetHiProDllDirectoryForScan()`, `RestoreAppDllDirectoryAfterScan()`.
- **Modified:** `src/Device/DeviceCommunication/SdkConfiguration.cs` — Uses `NativeDllResolver.SetDllDirectoryPath`; `SetAppDllDirectoryFirst()` at startup.
- **Added:** `src/App/Helpers/WindowsAdmin.cs` — `IsRunningAsAdmin()`.
- **Modified:** `src/App/App.xaml.cs` — OnStartup: `SetAppDllDirectoryFirst()`, then `Helpers.HiproPreflight.Run("COM2")`; if blocked and admin, dialog "Stop them now?"; if still blocked, message with PIDs; scan does not run until preflight passes.
- **Modified:** `src/App/Views/ConnectDevicesView.xaml.cs` — Before wired scan: `Helpers.HiproPreflight.Run("COM2")`; if `!IsPortFree` show message and abort. After wired scan: `DeviceCommunication.HiproPreflight.RestoreAppDllDirectoryAfterScan()`.
- **Modified:** `src/Device/DeviceCommunication/ScanDiagnostics.cs` — `LogSdExceptionDetails` now logs Message, HResult, InnerException, StackTrace; CTK_IF[i], log.txt.
- **Modified:** `src/Device/DeviceCommunication/ProgrammerScanner.cs` — SDException logging on all catch paths, SemaphoreSlim/STA, adaptive Strategy A + COM2-only Strategy B.
- **Added:** `scripts/check-ftdi.ps1`, `scripts/check-comlock.ps1`, `scripts/inspect-output-dlls.ps1` — FTDI/COM diagnostics (no system DLL changes).
- **Added:** `DiagnosticsReport.md` — HI-PRO diagnostic report template.
- **Added:** `src/Tools/Ftd2xxTest/`, `src/Tools/HiproD2xxProbe/` — D2XX probe console apps.
- **Modified:** `Ul8ziz.FittingApp.sln` — Tools projects.

### HI-PRO preflight and diagnostics (files changed/added)

- **HI-PRO preflight (startup, before CTK):** Runs asynchronously at app startup. **Steps:** (A) Write diagnostics to `logs/hpro_diag.log` (timestamp, bitness, paths, PATH trimmed, FTDI DLL candidates, loaded ftd2xx path via GetModuleHandle/GetModuleFileName); (B) Set DLL directory to AppBase first; (C) Detect HI-PRO device and confirm VCP driver (FTSER2K) via PnP; (D) Check COM2 availability (open/close test); (E) If COM2 locked and **AutoFreeComPortOnStartup** is true: enumerate suspect processes (Inspire, Starkey, HiPro, etc.), log PID/path, attempt CloseMainWindow then Kill, set HiProMonitor service to Manual; (F) Retry COM2 open up to 3 times; (G) If still locked, abort wired scan and show clear UI message. No system DLL modification.
- **Config switch:** `HiproPreflight.AutoFreeComPortOnStartup` (default `true`). When `false`, preflight only detects and logs; does not kill processes or change services.
- **Added:** `src/Device/DeviceCommunication/NativeDllResolver.cs` — P/Invoke: SetDllDirectory, AddDllDirectory, GetModuleHandle, GetModuleFileName.
- **Added:** `src/Device/DeviceCommunication/PreflightLog.cs` — File append logger for preflight with rotation at 5MB.
- **Added:** `src/Device/DeviceCommunication/HiproPreflight.cs` — `RunAsync()`, `EnsureCompletedAsync()`, `LastResult`, Steps A–G, `SetHiProDllDirectoryForScan()`, `RestoreAppDllDirectoryAfterScan()`.
- **Modified:** `src/Device/DeviceCommunication/SdkConfiguration.cs` — Uses `NativeDllResolver.SetDllDirectoryPath`; `SetAppDllDirectoryFirst()` at startup.
- **Added:** `src/App/Helpers/WindowsAdmin.cs` — `IsRunningAsAdmin()`.
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
- The application targets **Windows x86 (32-bit)** because the CTK HI-PRO communication module (`HI-PRO.dll`) is only available in 32-bit. The WPF app enforces **PlatformTarget=x86** and copies **ftd2xx.dll**, **FTD2XX_NET.dll**, **HiProWrapper.dll** from **src/App/NativeDeps/HI-PRO/** into the build output on every build; **VerifyNativeDeps** fails the build if any are missing. Populate **src/App/NativeDeps/HI-PRO/** from `C:\Program Files (x86)\HI-PRO` if needed (see that folder's README.txt).
- **Programmer scan:** Wired scan runs on **one dedicated STA thread** (no Task.Run/ThreadPool), with **SemaphoreSlim(1,1)** so only one scan at a time and no parallel CTK calls. Adaptive logic: try CTK interface strings (HI-PRO first if present), then all CTK_IF, then **COM2-only** fallback (COM3/COM4 ignored). **SetDllDirectory(HI-PRO)** is set before CTK so ftd2xx.dll loads from HI-PRO first; process PATH is set to AppBaseDir; HI-PRO; CTK; CTK\communication_modules. Wireless scan calls `BeginScanForWirelessDevices` before creating wireless interfaces (avoids E_CALL_SCAN).
- **SDK Path:** The project expects the SDK at `./SoundDesignerSDK/` relative to the repository root. If your SDK is in a different location (e.g., `C:\Users\...\Ezairo Pre Suite Firmware, Sound Designer and SDK\SoundDesignerSDK\SoundDesignerSDK`), ensure the **inner** `SoundDesignerSDK` folder (the one containing `binaries/`, `documentation/`, `samples/`) is accessible. Setup scripts should reference this SDK root path.