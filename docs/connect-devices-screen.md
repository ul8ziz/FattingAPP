# Connect Devices Screen

## Overview

The Connect Devices screen is the first step in the fitting workflow. It is responsible for discovering programmers (HI-PRO wired or wireless), discovering hearing aids on the programmer, and establishing a connection to the selected device(s). A successful connection enables navigation to Audiogram and Fitting; without a connection, those views remain disabled.

The screen uses a code-behind architecture: `ConnectDevicesView` implements `INotifyPropertyChanged` and acts as its own view model. There is no separate `ConnectDevicesViewModel` class.

---

## Screen Responsibilities

| Responsibility | Description |
|----------------|-------------|
| **Product library selection** | Enumerate available `.library` files, allow user to select one for offline parameter browsing and as the initial SDK product. |
| **Programmer discovery** | Scan for wired (HI-PRO via D2XX) or wireless programmers. |
| **Device discovery** | Detect hearing aids on the selected programmer (Left/Right ports). |
| **Device connection** | Connect to selected device(s), reload SDK for detected firmware, initialize session. |
| **Session establishment** | Populate `DeviceSessionService` and `AppSessionState` so the rest of the app can use the connected device(s). |

---

## UI Structure

The screen is laid out in a `ScrollViewer` with four main rows:

### Row 0: Header and Status Bar

- **Title:** "Connect Devices"
- **Status bar:** Dynamic background and text based on scan state:
  - **Gray:** Idle — "Scan and select a programmer to begin"
  - **Amber:** Scanning — "Searching..." with Cancel button visible
  - **Red:** Scan completed with no programmers — error message
  - **Green:** Programmers found — "{Name} Found | USB OK | Driver OK"
- **Buttons:** Scan Wired, Scan Wireless, Help, Cancel (visible only while scanning)

### Row 1: (Empty spacer)

### Row 2: Two-column layout

| Column | Content |
|--------|---------|
| **Left (Setup)** | Step 1: Product Library (ComboBox), Step 2: Programmer (clickable cards). Placeholder text when no programmers. |
| **Right (Discover & Connect)** | "Scan for Hearing Aids" button, Rescan/Stop when devices exist, discovery progress panel, device cards (Left/Right with checkboxes), Connect button area. |

### Row 3: Footer

- **Not connected:** Selected devices summary (e.g. "Selected: Left (1)") + Connect button
- **Connected:** Success message ("Successfully connected to devices!")
- **Connecting:** Progress bar and status message below footer

---

## Product Library Workflow

### How libraries are discovered

- **Source:** `LibraryService.EnumerateLibraries()` — scans `Assets\SoundDesigner\products\` for `.library` files.
- **When:** During `InitializeSdkServicesAsync()`, which runs on `UserControl_Loaded` when `AvailableLibraries.Count == 0` and no active session exists.
- **Result:** `AvailableLibraries` is populated; if none was selected, the first library is auto-selected.

### How selection works

- User selects from the ComboBox bound to `AvailableLibraries` with `DisplayMemberPath="FileName"`.
- The `SelectedLibrary` setter calls `LoadDefaultLibraryAsync(libraryPath)`.

### What happens when library changes

1. **Offline session:** `FittingSessionManager.Instance.CreateOfflineSessionAsync(libraryPath)` — creates an offline product for parameter browsing without a device.
2. **Param file:** `ParamFileService.FindParamForLibrary(libraryFileName)` looks for a matching `.param` file (e.g. `E7111V2.param` for `E7111V2.library`). If found, `FittingSessionManager.Instance.ApplyParamFileAsync(paramPath)` applies preset values.
3. **SDK reload:** Library selection does **not** trigger SDK reload at this stage. SDK reload happens only at **Connect** time, when the detected device firmware is known.

---

## Programmer Workflow

### How programmers are enumerated

| Scan type | Method | Backend |
|-----------|--------|---------|
| **Wired** | `HiProService.ListDevicesAsync()` | FTDI D2XX (ftd2xx.dll). No COM port, no CTK. |
| **Wireless** | `ProgrammerScanner.ScanWirelessOnlySync()` | SDK/CTK communication interfaces (e.g. NOAHlink, RSL10). |

Wired scan does **not** require SDK initialization. Wireless scan requires `_sdkManager` and `_programmerScanner`; if not initialized, `InitializeSdkServicesAsync()` is called first.

### HI-PRO detection (wired)

- Uses `HiProService` (D2XX) to enumerate FTDI devices.
- Each device becomes a `ProgrammerInfo` with `Name = "HI-PRO"`, `Type = Wired`, `Port = "D2XX"`, `InterfaceName = Constants.HiPro`.
- Serial number comes from D2XX device description or serial.
- If no D2XX devices are found, a single `ScanAttemptResult` with `Found = false` and `ErrorCode = "NOT_CONNECTED"` is added.

### Selected programmer

- User clicks a programmer card → `ProgrammerItem_MouseLeftButtonDown` deselects all, selects the clicked one, sets `_selectedProgrammerInfo = programmer.ProgrammerInfo`.
- Only one programmer can be selected at a time.
- When exactly one programmer is found, it is auto-selected.
- Selection triggers `DiscoverHearingAidsAsync()` automatically.

### Programmer metadata shown in UI

| Field | Source |
|-------|--------|
| Name | `ProgrammerInfo.Name` (e.g. "HI-PRO") |
| SerialNumber | D2XX device serial/description or "N/A" |
| Firmware | "N/A" for wired |
| Port | "D2XX" for wired, device ID for wireless |
| Backend | "D2XX" or "Wireless" |
| Description | D2XX device description (optional) |

---

## Scan Workflow

### Wired scan flow

1. User clicks **Scan Wired** → `RunScanAsync(ScanKind.Wired)`.
2. `Programmers` cleared, `IsSearching = true`, 15-second timeout timer started.
3. `HiProService.ListDevicesAsync(token)` enumerates D2XX devices.
4. Each device → `ProgrammerInfo` + `ProgrammerViewModel` added to `Programmers`.
5. If one programmer found → auto-select. `IsSearching = false`, `ScanCompleted = true`.
6. If zero found → `FoundProgrammersMessage` set to error text, `MessageBox` with troubleshooting hints.

### Wireless scan flow

1. User clicks **Scan Wireless** → `RunScanAsync(ScanKind.Wireless)`.
2. If SDK not initialized → `InitializeSdkServicesAsync()` first.
3. `ProgrammerScanner.ScanWirelessOnlySync(progress, token, YieldToUI)` runs (synchronous with UI yield).
4. Same post-processing as wired: build `ProgrammerViewModel` list, auto-select if one found.

### Scan timeout

- 15-second timer (`ScanTimeoutSeconds`). On expiry, `_cancellationTokenSource.Cancel()` is called, scan stops, `FoundProgrammersMessage` shows timeout message.

### Cancel

- User clicks **Cancel** → `CancelSearch_Click` → `CancelSearch()` → `_cancellationTokenSource.Cancel()`.

---

## Device Discovery Workflow

### When discovery runs

- Automatically when a programmer is selected (`ProgrammerItem_MouseLeftButtonDown`).
- Manually when user clicks **Scan for Hearing Aids** or **Rescan**.

### Wired HI-PRO discovery

- Uses `HiProWiredDiscoveryService` with the shared `SdkManager`.
- `HiProService.DisconnectAsync()` first to release any prior connection.
- `wiredDiscoveryService.DetectBothAsync(ct)` returns `DiscoveryResult` with `FoundLeft`, `FoundRight`, and per-side metadata (FirmwareId, ProductId, SerialId, ChipId, ParameterLockState, etc.).
- Partial success allowed: Left can be found while Right fails (or vice versa).
- Errors from `discoveryResult.Errors` are logged via `LogDiscovery` (Debug + ScanDiagnostics).

### Wireless discovery

- Uses `DeviceDiscoveryService.DiscoverBothDevicesAsync(selectedProgrammerInfo, progress, ct)`.
- Returns `(DeviceInfo? left, DeviceInfo? right)`.

### Device metadata (from detection)

| Field | Source |
|-------|--------|
| Side | Left / Right |
| Model | ProductId if non-zero, else FirmwareId |
| SerialNumber | From discovery (can be "Unknown", "-1", "0" for unprogrammed) |
| Firmware | FirmwareId from SDK discovery |
| ProductId, ChipId | From discovery result |
| ParameterLockState | From `IDeviceInfo.ParameterLockState` |
| BatteryLevel | From discovery when available |
| Status | "Detected" |

### Raw detection vs post-connect data

- **Detection:** Firmware, ProductId, ChipId, Serial, ParameterLockState come from the discovery/connect handshake. Serial `-1` or `0` is normal for unprogrammed devices.
- **Post-connect:** After `ConnectAsync`, `DeviceInfo` is merged into the ViewModel. `LoadDeviceInfoAsync` runs after success and may read `Product.BatteryAverageVoltage` (via reflection) for battery voltage display.

---

## Connection Workflow

### Pre-connect validation

1. **Programmer selected:** `_selectedProgrammerInfo != null`.
2. **At least one device selected:** `LeftDevice?.IsSelected` or `RightDevice?.IsSelected`.
3. **Firmware gate:** At least one selected device must have non-empty `Firmware`. If not, connect is aborted with a message to re-seat and re-discover.

### Connect sequence

1. `IsConnecting = true`, `AppSessionState.ConnectionState = Connecting`.
2. `InitializeSdkServicesAsync()` if needed.
3. **SDK reload:** `SdkGate.InvokeAsync(() => { _sdkManager.ReloadForFirmware(detectedFirmware); _deviceConnectionService = new DeviceConnectionService(_sdkManager); })`. The library is switched to match the detected device firmware (e.g. E7111V2).
4. For each selected device (Left, Right): `DeviceConnectionService.ConnectAsync(programmerInfo, side, progress, token)`.
5. Post-connect validation: if `deviceInfo.Firmware` is empty → treat as failure. Serial `-1` or `0` is **not** a failure (unprogrammed device).
6. On success: update ViewModel (Model, SerialNumber, Firmware, etc.), set `result.LeftConnected` / `result.RightConnected`.
7. On failure: store error in `result.LeftError` / `result.RightError`, add to `result.Errors`.

### Partial success

- `result.Success = result.LeftConnected || result.RightConnected`. If one side succeeds and the other fails, the success path is taken.
- `AppSessionState.SetConnected` and `DeviceSessionService.SetSession` receive the actual left/right flags.
- MessageBox shows success plus any partial errors (e.g. "Connected to Left device" with "Right: E_SEND_FAILURE").

### Post-connect integration

1. **AppSessionState.SetConnected** — updates header (connection status, device name).
2. **DeviceSessionService.SetSession** — stores `SdkManager`, `DeviceConnectionService`, programmer, display name, left/right flags.
3. **DeviceSessionService.SetDeviceIdentity** — firmware ID, serials, models for Configure Device and other features.
4. **FittingSessionManager.SyncDeviceStateFromConnectionAsync** — syncs session so `IsDeviceConfigured` reflects read capability (e.g. `E_UNCONFIGURED_DEVICE` on unprogrammed device).
5. **DeviceInitializationService.EnsureInitializedAndConfiguredAsync** — per side, ensures InitializeDevice/ConfigureDevice have run so Read/Write are valid.
6. **LoadDeviceInfoAsync** — loads device identity, attempts battery voltage read.
7. **OnConnectionSucceeded** — callback from MainView; navigates to Fitting.

---

## Session and Device State Integration

### When session is created

- On successful connect: `DeviceSessionService.Instance.SetSession(...)`.
- Session holds `SdkManager`, `DeviceConnectionService`, programmer, and left/right connection state.

### Offline session replacement

- Before connect: `FittingSessionManager` has an offline session from the selected library.
- After connect: `SyncDeviceStateFromConnectionAsync` merges device state. The product remains the same (reloaded for firmware), but now backed by live connections.
- `IsDeviceConfigured` is updated based on InitializeDevice/ConfigureDevice results (e.g. `E_UNCONFIGURED_DEVICE` → not configured).

### When session ends

- User chooses **End Session** → `SessionEndService.ExecuteEndSessionAsync` → SDK teardown, `DeviceSessionService.ClearSessionAsync`, `AppSessionState.SetNotConnected`.
- `NavigateToConnectAndRestartDiscovery` navigates back to Connect Devices, calls `ResetForInactiveConnection()` and `StartWiredDiscoveryAfterDebounce()` (500 ms delay, then wired scan).

---

## UI State Rules

| Condition | Effect |
|-----------|--------|
| `IsSearching` | Scan Wired/Wireless disabled, Cancel visible, status bar amber |
| `ScanCompleted && !HasFoundProgrammers` | `ScanFoundNothing` true, status bar red, error message |
| `HasFoundProgrammers` | Status bar green, "Scan for Hearing Aids" enabled when programmer selected |
| `CanDiscover` | `HasProgrammers && Any(p => p.IsSelected) && !IsDiscovering` |
| `CanConnect` | `SelectedDevicesCount > 0 && !IsConnecting && !IsConnected` |
| `IsConnected` | Footer shows success, Connect button hidden |
| `AppSessionState.IsNavigationEnabled` | True only when `ConnectionState == Connected` |
| `CanNavigateToConnect` | False while `DeviceSessionService.IsConfigureRunning` (Configure Device) |

### Audiogram and Fitting enable/disable

- **Audiogram** and **Fitting** sidebar buttons use `IsEnabled="{Binding IsNavigationEnabled}"`.
- `IsNavigationEnabled` is `AppSessionState.Instance.IsNavigationEnabled`, which is true only when `ConnectionState == Connected`.
- Thus both are disabled until a successful connection is established.

---

## Error Handling

| Scenario | Handling |
|----------|----------|
| **No programmers found (wired)** | MessageBox with "No wired programmers found...", hints (HI-PRO USB, ftd2xx.dll, x86 build). Status bar red. |
| **No programmers found (wireless)** | MessageBox with "No wireless programmers found...", hints (Bluetooth, NOAHlink/RSL10). |
| **Scan exception** | MessageBox with error, library/config path, app directory. `FoundProgrammersMessage` set to error. |
| **Scan cancelled** | `FoundProgrammersMessage = "Scan cancelled by user"`. |
| **Scan timeout** | Timer cancels scan, message "Scan timeout: No programmers found within the time limit." |
| **No programmer selected (discovery)** | MessageBox "Please select a programmer first." |
| **No device selected (connect)** | MessageBox "Please select at least one device to connect." |
| **No firmware on selected device** | MessageBox "No firmware ID was detected... re-seat and try Discover again." Connect aborted. |
| **SDK init failure** | `_lastSdkError` stored, MessageBox with detail. Discovery/connect paths check and throw or show. |
| **ReloadForFirmware failure** | "No library found for firmware '{id}'. Ensure matching .library in Assets\\SoundDesigner\\products\\." |
| **Connect failure (one or both sides)** | Per-side errors in `result.Errors`. `E_SEND_FAILURE` / "sending data" → user message "Communication error. Check cable, seating, and contacts." |
| **Partial success (e.g. Left OK, Right fail)** | Success path taken; MessageBox shows success + error lines. `DeviceConnectionService.DisconnectAsync` for failed side. |
| **Connection cancelled** | `ConnectionState = NotConnected`, MessageBox "Connection was cancelled." |

---

## Important Classes and Services

| Class/Service | Responsibility |
|---------------|----------------|
| `ConnectDevicesView` | UI, bindings, event handlers, orchestrates all flows. Acts as its own ViewModel. |
| `ProgrammerViewModel` | Programmer card: Name, SerialNumber, Firmware, Port, Backend, IsSelected, ProgrammerInfo. |
| `HearingAidViewModel` | Device card: Side, Model, SerialNumber, Firmware, ProductId, ChipId, BatteryLevel, ParameterLockState, Status, IsSelected. |
| `HiProService` | D2XX: ListDevicesAsync, DisconnectAsync. No SDK dependency for enumeration. |
| `ProgrammerScanner` | Wireless scan via SDK. `ScanWirelessOnlySync`. |
| `HiProWiredDiscoveryService` | Wired device discovery: DetectBothAsync, returns Left/Right DeviceInfo. |
| `DeviceDiscoveryService` | Wireless device discovery: DiscoverBothDevicesAsync. |
| `DeviceConnectionService` | ConnectAsync per side, GetConnection, DisconnectAsync. |
| `DeviceSessionService` | Holds session (SdkManager, ConnectionService, programmer). SetSession, SetDeviceIdentity, ClearSession. |
| `AppSessionState` | Connection status for header. SetConnected, IsNavigationEnabled. |
| `FittingSessionManager` | Offline session, CreateOfflineSessionAsync, ApplyParamFileAsync, SyncDeviceStateFromConnectionAsync. |
| `DeviceInitializationService` | EnsureInitializedAndConfiguredAsync per side. |
| `SdkManager` | SDK lifecycle. ReloadForFirmware, GetProduct. |
| `SdkGate` | Serializes SDK access. InvokeAsync for ReloadForFirmware. |
| `LibraryService` | EnumerateLibraries. |
| `ParamFileService` | FindParamForLibrary. |

---

## Notes for Developers

### Architecture

- Connect Devices uses **code-behind as ViewModel**. All state and commands live in `ConnectDevicesView.xaml.cs`. Consider extracting a `ConnectDevicesViewModel` if the screen grows or needs unit testing.
- SDK initialization is **lazy**: deferred until first scan (wired does not need it; wireless does) or discovery/connect.

### State dependencies

- `DeviceSessionService.HasActiveSession` — when true, `InitializeSdkServicesAsync` is skipped on Loaded.
- `SdkLifecycle.IsDisposingOrDisposed` — when true, init is skipped.
- `DeviceSessionService.IsConfigureRunning` — disables Connect Devices navigation and End Session.

### Coupling points

- **MainView** creates and caches `ConnectDevicesView`, wires `OnConnectionSucceeded` to navigate to Fitting.
- **Session end** uses `NavigateToConnectAndRestartDiscovery` which calls `ResetForInactiveConnection` and `StartWiredDiscoveryAfterDebounce`.

### Edge cases

- **Unprogrammed device (Serial -1 or 0):** Normal for dev/test. SDK may return `E_UNCONFIGURED_DEVICE` on Read/Write. Save to device is blocked; UI reflects "Not Configured."
- **Parameter lock:** `ParameterLockState` is shown on the card. Some devices allow write despite lock; Save will attempt and verify.
- **Battery:** `BatteryLevel` (percentage) from discovery when available. `BatteryVoltageV` from `Product.BatteryAverageVoltage` after connect (reflection; may be null if not supported).

### Recommendations

- Align SDK usage with vendor docs in `SoundDesignerSDK\documentation` (see `.cursor/rules/rules.mdc` §0).
- When adding new programmer types or discovery paths, ensure `ProgrammerInfo` and `DeviceInfo` are populated consistently for Connect and session setup.
