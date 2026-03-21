# Audiogram Screen

## Overview

The Audiogram screen is a clinical input layer for entering hearing thresholds (dB HL) at standard frequencies. It feeds into the same device session and save path as the Fitting page: Generate WDRC produces prescription targets, maps them to device parameters, and applies updates to the session snapshot. The user then opens Fitting to review and save to the device.

The screen uses a proper MVVM architecture: `AudiogramView` binds to `AudiogramViewModel`, which holds per-memory audiogram data, chart points, numeric rows, and commands. Memory selection is synchronized with `DeviceSessionService` (single source of truth shared with Fitting).

---

## Screen Responsibilities

| Responsibility | Description |
|----------------|-------------|
| **Clinical data entry** | Enter AC/BC/UCL thresholds at 250–8000 Hz via chart clicks or numeric table. |
| **Per-memory storage** | Store audiogram data per memory index (1–8). Switch memory saves current data and loads saved data for the new memory. |
| **Validation** | Validate frequency set, range, and duplicate frequencies before Save and Generate WDRC. |
| **Persistence** | Save to JSON file (user-selected path) and auto-save to app-data (`%AppData%\Ul8ziz\FittingApp\audiogram_sessions.json`) so data survives app restarts. |
| **Generate WDRC** | Run prescription engine (stub), map targets to device parameters, apply to session snapshot for connected ear(s) only. |
| **Integration with Fitting** | Apply WDRC updates to `DeviceSessionService` snapshot cache; user opens Fitting to review and save to NVM. |

---

## UI Structure

The screen is laid out in a vertical `StackPanel` inside a `ScrollViewer`:

### Header Row

- **Title:** "Audiogram"
- **Memory selector:** ComboBox bound to `MemoryDisplayNames` (Memory 1–8), `SelectedIndex` two-way bound to `SelectedMemoryIndex` (0-based). Shared with Fitting via `DeviceSessionService.SelectedMemoryIndex`.

### No-Connection Banner

- **Visibility:** `ShowNoConnectionMessage` (true when neither ear is connected).
- **Content:** "Connect a hearing aid to enable Audiogram." Amber background.

### Chart Area (three columns)

| Column | Content |
|--------|---------|
| **Right** | Right Ear chart (`AudiogramChartControl`), Clear Right, Copy >>. Visible when `IsRightEarEnabled`. |
| **Center** | Tool panel: AC/BC/UCL symbol rows (toggle input mode), Masked, No Response, PTA display (Right \| Left). |
| **Left** | Left Ear chart, << Copy, Clear Left. Visible when `IsLeftEarEnabled`. |

### Numeric View Area (two columns)

- **Right Ear — Numeric View:** DataGrid with AC/BC/UCL rows, columns 250–8k Hz. Visible when `IsRightEarEnabled`.
- **Left Ear — Numeric View:** Same structure. Visible when `IsLeftEarEnabled`.

### Bottom Action Bar

- **Save Audiogram:** Saves to user-selected JSON file; blocks when validation fails (e.g. duplicate frequencies). Disabled while `IsGeneratingWdrc`.
- **Generate WDRC:** Requires connected device. Disabled when no device or generating. Hint text: "Requires connected hearing aid" or "Generates fitting parameters for selected memory".
- **Open Fitting:** Navigates to Fitting (always enabled).

### Validation / Status Messages

- **ValidationMessage:** Red, validation errors (e.g. duplicate frequencies). Visibility when non-empty.
- **StatusMessage:** Gray, informational (e.g. "Audiogram data loaded for Memory 1.", "WDRC parameters generated..."). Visibility when non-empty.

---

## Enable/Disable Rules

| Condition | Effect |
|-----------|--------|
| `IsAudiogramEnabled` | `AppSessionState.ConnectedLeft \|\| ConnectedRight`. Gates Generate WDRC; when false, action bar shows "Requires connected hearing aid". |
| `IsRightEarEnabled` | `AppSessionState.ConnectedRight`. Right chart and numeric view visibility. |
| `IsLeftEarEnabled` | `AppSessionState.ConnectedLeft`. Left chart and numeric view visibility. |
| `ShowNoConnectionMessage` | `!ConnectedLeft && !ConnectedRight`. Shows amber banner. |
| `SaveAudiogramCommand.CanExecute` | `!_isGeneratingWdrc`. |
| `GenerateWdrcCommand.CanExecute` | `!_isGeneratingWdrc && IsAudiogramEnabled`. |
| `OpenFittingCommand` | Always executable. |

**Sidebar navigation:** Audiogram is enabled in the sidebar only when `AppSessionState.IsNavigationEnabled` (i.e. a device is connected). Same as Fitting.

---

## Memory Workflow

### Memory selector

- **Source of truth:** `DeviceSessionService.SelectedMemoryIndex` (0-based). UI label "Memory 1" = index 0 = SDK `kNvmMemory1`.
- **Binding:** ComboBox `SelectedIndex` two-way bound to `AudiogramViewModel.SelectedMemoryIndex`.

### Synchronization with Fitting

- **Audiogram → Session:** When user changes memory in Audiogram, `SelectedMemoryIndex` setter calls `_session.SetSelectedMemoryIndex(value)`, which raises `PropertyChanged`. FittingViewModel subscribes and updates its `SelectedMemoryIndex`.
- **Fitting → Audiogram:** When user changes memory in Fitting, same flow: `DeviceSessionService.SetSelectedMemoryIndex` → `PropertyChanged` → AudiogramViewModel `OnSessionPropertyChanged` updates `_selectedMemoryIndex` and loads data for the new memory.

### Memory switch sequence

1. **Save current:** `SaveCurrentMemoryAudiogram()` — builds session from rows, stores in `_audioSessionsByMemory[oldIndex]`, triggers `AutoSaveToAppDataAsync()` (fire-and-forget).
2. **Update shared state:** `_session.SetSelectedMemoryIndex(value)`.
3. **Load new:** `LoadAudiogramForMemory(value)` — populates chart and rows from `_audioSessionsByMemory[value]` or clears if no data.

### Internal mapping

- `MemoryNames[i]` = "Memory {i+1}" for display.
- SDK `kNvmMemoryN` where N = `memoryIndex + 1`.

---

## Data Loading Workflow

### When screen opens

1. **Constructor:** `LoadFromAppData()` — synchronously reads `%AppData%\Ul8ziz\FittingApp\audiogram_sessions.json` into `_audioSessionsByMemory`. If file missing or empty, starts clean.
2. **If device already connected:** `LoadAudiogramForMemory(_selectedMemoryIndex)` — populates UI from stored data for current memory.
3. **Loaded event:** `AudiogramView.OnLoaded` calls `PopulateFromSession()` — re-populates from `_audiogramSession` (redundant if constructor already loaded; ensures consistency).

### Data sources

| Source | When | Content |
|--------|------|---------|
| **Empty state** | No data for memory in `_audioSessionsByMemory` | Clears chart and rows, status "No audiogram data for Memory N (left/right/both ears active). Enter thresholds above." |
| **Persisted (app-data)** | `LoadFromAppData()` on startup | `Dictionary<int, AudiogramSession>` keyed by memory index. Survives app restarts. |
| **Persisted (file)** | Load Audiogram (OpenFileDialog) | User-selected JSON. Loaded into current memory, then persisted to app-data. |
| **Session store** | Memory switch, device connect | `_audioSessionsByMemory[memIdx]` — in-memory per-memory store. |

### Left/right data

- **Right:** `_rightEarChartPoints`, `_rightEarRows`, `AudiogramSession.RightEarAudiogram`.
- **Left:** `_leftEarChartPoints`, `_leftEarRows`, `AudiogramSession.LeftEarAudiogram`.
- Each ear is independent; data is built from `BuildEarAudiogram(side, rows)` which iterates rows (AC/BC/UCL) and frequency columns.

---

## Chart Interaction Workflow

### Chart click handling

- **Control:** `AudiogramChartControl` has a transparent `ClickOverlay` over the canvas. `MouseLeftButtonDown` converts pixel position to (freqHz, dbHL).
- **Coordinate conversion:** `XToFreq` (log scale 250–8000 Hz), `YToDb` (linear −10 to 120 dB, inverted). Frequency snapped to nearest standard (250, 500, 1k, 2k, 3k, 4k, 6k, 8k). dB snapped to 5 dB steps.
- **Command:** `ChartClickCommand` receives `(double freqHz, int dbHL)`. Bound per ear: `RightChartClickCommand`, `LeftChartClickCommand`.

### Selected ear

- **Determined by which chart was clicked.** Right chart → `OnChartClick(DeviceSide.Right, ...)`. Left chart → `OnChartClick(DeviceSide.Left, ...)`.

### Selected input mode

- **ActiveInputMode:** AC, BC, UCL, Masked, NoResponse, PTA. Toggle buttons in center panel.
- **Effect on plotted point:**
  - AC → `AudiogramPointType.AC`
  - BC → `AudiogramPointType.BC`
  - UCL → `AudiogramPointType.UCL`
  - Masked → `IsMasked = true` (AC/BC symbol drawn filled)
  - NoResponse → `IsNoResponse = true` (AC/BC symbol drawn with arrow down)
  - PTA → no action (click ignored)

### Add/update/remove logic

- **Same (freq, type) exists:** Remove (toggle off).
- **Otherwise:** Add new point with current mode's type, IsMasked, IsNoResponse.
- **After change:** `SyncChartToRows(side)` — updates numeric table from chart.

### Chart symbols

- **AC:** Circle (open = unmasked, filled = masked, open + arrow = no response).
- **BC:** Bracket shape (left ear `[`, right ear `]`).
- **UCL:** Downward triangle.
- **Connection line:** Dashed line between AC points (excluding no-response) for each ear.

---

## Numeric View Workflow

### Structure

- **Rows:** AC, BC, UCL (`AudiogramTypeRowViewModel` per type).
- **Columns:** 250, 500, 1k, 2k, 3k, 4k, 6k, 8k Hz. Each cell bound to `Val250`, `Val500`, etc. with `UpdateSourceTrigger=LostFocus`.

### Chart → Numeric

- **SyncChartToRows(side):** Called after every chart click. For each row (AC/BC/UCL) and frequency index, finds matching chart point (same freq, same type, not no-response) and sets row cell to `DbHL.ToString()` or null.

### Numeric → Chart

- **SyncRowsToChart(side):** Defined but **not invoked** when user edits numeric cells. The VM has no subscription to row `PropertyChanged`. Therefore:
  - **Chart edits** update rows immediately.
  - **Numeric edits** update rows only; chart display is not refreshed until next chart click or memory switch.
  - **BuildSessionFromRows** (used for Save, Generate) reads from rows, so persisted and generated data reflect numeric edits correctly.

### Per-ear separation

- Right and left have separate `_rightEarRows` / `_leftEarRows` and `_rightEarChartPoints` / `_leftEarChartPoints`. No cross-ear sync except Copy commands.

---

## Validation Rules

### When validation runs

- **Save Audiogram:** Before opening SaveFileDialog. Blocks save if invalid.
- **Generate WDRC:** Before prescription. Blocks generation if invalid.

### What is validated

- **Per ear (only connected ears for Generate WDRC):**
  - Frequency in range (0, 20000] Hz.
  - No duplicate frequency (same freq + type in one ear).
  - Threshold in range −10 to 120 dB HL.
  - UCL in range −10 to 130 dB HL.
- **Empty data:** Warning "No threshold points" for validated ear.

### Duplicate frequency

- **Rule:** `seenFreq.Contains(p.FrequencyHz)` within one ear → error "Duplicate frequency: X Hz."
- **Blocking:** Yes. Save and Generate WDRC are blocked until resolved.

### Invalid values

- Out-of-range frequency or dB → error. Blocking.

### Validation result

- `AudiogramValidationResult`: `IsValid`, `Errors`, `Warnings`.
- `ValidationMessage` shows errors (and warnings if any). `StatusMessage` shows user-friendly summary (e.g. "Cannot save audiogram until duplicate frequencies are resolved.").

---

## Save and Persistence Workflow

### Save Audiogram (button)

1. `SyncRowsToSession()` — build session from rows, store in `_audiogramSession` and `_audioSessionsByMemory[_selectedMemoryIndex]`.
2. If empty → status "Nothing to save — enter audiogram data first.", return.
3. **Validation:** If invalid (e.g. duplicates) → block, show validation message.
4. SaveFileDialog → user selects path.
5. `_persistenceService.SaveAsync(session, path)` — JSON serialization.
6. `SaveToAppDataAsync()` — write `_audioSessionsByMemory` to app-data path (awaited).

### App-data persistence

- **Path:** `%AppData%\Ul8ziz\FittingApp\audiogram_sessions.json`
- **Content:** `Dictionary<int, AudiogramSession>` — only memories with at least one point.
- **When written:**
  - `AutoSaveToAppDataAsync()` (fire-and-forget) on memory switch (`SaveCurrentMemoryAudiogram`).
  - `SaveToAppDataAsync()` (awaited) after Save Audiogram and Load Audiogram.
  - `FlushAsync()` before session end (called by MainView's `FlushAudiogramAsync`).

### Load Audiogram (button)

1. OpenFileDialog → user selects JSON file.
2. `_persistenceService.LoadAsync(path)` → `AudiogramSession`.
3. Store in `_audioSessionsByMemory[_selectedMemoryIndex]`, `PopulateFromSession()`.
4. `SaveToAppDataAsync()` — persist to app-data so data survives restart.

### Persistence keys

- Memory index (0–7) as dictionary key. No device/serial in key; data is app-wide, not per-device.

---

## Generate WDRC Workflow

### Sequence

1. **Resolve connected ears:** `connLeft`, `connRight` from `AppSessionState`. If neither → status "No hearing aid connected.", return.
2. **Save UI to session:** `SyncRowsToSession()` — build `_audiogramSession` from rows.
3. **Validate** (only connected ears): `_validationService.Validate(_audiogramSession, connLeft, connRight)`. If invalid → block, show errors.
4. **Prescription:** `_prescriptionEngine.ComputeTargets(_audiogramSession, null)` — stub returns flat 0 dB targets.
5. **Resolve snapshots:** `_session.GetSnapshotsForMemory(memIdx)` for left/right. Use only sides that are connected. If both null for connected sides → "No device snapshot available... Load fitting parameters first on the Fitting screen.", return.
6. **Parameter mapping:** `_parameterMappingService.MapTargetsToDevice(targets, libraryKey, refSnap)` — E7111V2 uses `GraphParameterMap.json` to produce parameter Id → value updates.
7. **Apply to Fitting:** For each connected side with snapshot, `_integrationService.ApplyToFitting(mappingResult, side, memIdx)` — updates snapshot in place, `SetMemorySnapshot`, `MarkMemoryDirty`.
8. **Status:** "WDRC parameters generated for Memory N (left/right/both ears). Open Fitting to review and save to device."

### Memory resolution

- Uses `_selectedMemoryIndex` (0-based). SDK space = `kNvmMemory{memIdx + 1}`.

### Validation gate

- Generation is blocked if validation fails (e.g. duplicate frequencies). No partial generation.

### What happens to Fitting

- `AudiogramIntegrationService.ApplyToFitting` updates `DeviceSessionService` snapshot cache and marks memory dirty. No direct SDK write. User must open Fitting and click Save to Device (or Session End → Save & End) to commit to NVM.

---

## Navigation and Integration with Fitting

### Open Fitting

- `OpenFitting()` calls `_requestNavigate?.Invoke("Fitting")` — callback from MainView. Navigates to Fitting view.

### Memory context preservation

- Both Audiogram and Fitting use `DeviceSessionService.SelectedMemoryIndex`. Changing memory in one updates the other via `PropertyChanged`. No explicit handoff.

### Generated results in Fitting

- `ApplyToFitting` writes parameter updates into the snapshot. Fitting displays the snapshot; when user opens Fitting after Generate WDRC, the updated values appear in the parameter grid. User saves to device from Fitting.

---

## Error Handling

| Scenario | Handling |
|----------|----------|
| **Duplicate frequency** | Validation error. Save and Generate WDRC blocked. Message: "Cannot save/generate until duplicate frequencies are resolved." |
| **Invalid chart/numeric value** | Validation error (frequency or dB out of range). Blocking. |
| **Missing data** | Warning "No threshold points" for ear. Generate may proceed (stub returns 0 dB); Save blocks if completely empty. |
| **Persistence failure** | Save/Load catch exception, StatusMessage = "Save error: ..." / "Load error: ...". |
| **Generation blocked (no device)** | StatusMessage = "No hearing aid connected. Connect a device to generate WDRC." |
| **Generation blocked (no snapshot)** | StatusMessage = "No device snapshot available for Memory N. Load fitting parameters first on the Fitting screen." |
| **Generation blocked (no mapping)** | StatusMessage = "No parameter mapping available for '{libraryKey}'. WDRC not updated. Ensure GraphParameterMap.json is loaded." |
| **Device disconnected** | `OnAppSessionPropertyChanged` detects disconnect. StatusMessage = "Device disconnected. Connect a hearing aid to re-enable Audiogram." Audiogram data retained in memory store. |
| **LoadFromAppData failure** | Caught, no throw. Debug output only. |

---

## Important Classes and Services

| Class/Service | Responsibility |
|---------------|----------------|
| `AudiogramView` | XAML, binds to AudiogramViewModel. OnLoaded → PopulateFromSession. |
| `AudiogramViewModel` | Per-memory storage, chart/row sync, commands, validation, persistence, Generate WDRC, Open Fitting. |
| `AudiogramChartControl` | Canvas-based chart, frequency/dB conversion, click → command with (freqHz, dbHL). |
| `AudiogramChartPoint` | Display model: FrequencyHz, DbHL, PointType, IsNoResponse, IsMasked. |
| `AudiogramTypeRowViewModel` | One row (AC/BC/UCL), Val250–Val8k, GetValueForIndex/SetValueForIndex. |
| `AudiogramSession` | LeftEarAudiogram, RightEarAudiogram. |
| `EarAudiogram` | Side, Points (List&lt;AudiogramPoint&gt;). |
| `AudiogramPoint` | FrequencyHz, ThresholdDbHL, PointType. |
| `AudiogramValidationService` | Validate(session, validateLeft, validateRight). Duplicate freq, range, empty. |
| `AudiogramPersistenceService` | SaveAsync, LoadAsync (single session), SaveSessionsAsync, LoadSessionsAsync (dict by memory). |
| `PrescriptionEngineStub` | ComputeTargets → flat 0 dB. Replace with NAL-NL2/DSL-v5 when implemented. |
| `E7111V2ParameterMappingService` | MapTargetsToDevice using GraphParameterMap.json. |
| `AudiogramIntegrationService` | ApplyToFitting — update snapshot, SetMemorySnapshot, MarkMemoryDirty. |
| `DeviceSessionService` | SelectedMemoryIndex, GetSnapshotsForMemory, SetMemorySnapshot, MarkMemoryDirty. |
| `AppSessionState` | ConnectedLeft, ConnectedRight. |

---

## Notes for Developers

### Architecture

- Audiogram uses a proper ViewModel. Chart and numeric views are separate; chart is primary for click input; numeric is primary for BuildSessionFromRows (Save/Generate). Numeric edits do not currently trigger chart redraw.

### State dependencies

- `DeviceSessionService.SelectedMemoryIndex` — shared with Fitting. Audiogram subscribes to `PropertyChanged`.
- `AppSessionState.ConnectedLeft/Right` — gates ear visibility and Generate WDRC. Audiogram subscribes for edge detection (connect/disconnect).

### Coupling

- **MainView:** Injects `requestNavigate` callback. Calls `FlushAudiogramAsync` before session end.
- **DeviceSessionService:** Snapshot cache, memory dirty, selected index.
- **FittingSessionManager:** Library key for parameter mapping.

### Edge cases

- **No Response / Masked:** Stored in `AudiogramChartPoint` but `BuildEarAudiogram` does not persist IsNoResponse/IsMasked into `AudiogramPoint` (model has no such fields). No-response points are excluded from SyncChartToRows numeric display and from connection line.
- **PTA:** Display-only (500/1000/2000 Hz AC average). PTA mode prevents chart clicks.
- **Stub prescription:** PrescriptionEngineStub returns 0 dB. Real NAL-NL2/DSL-v5 to be implemented.

### Recommendations

- Consider calling `SyncRowsToChart` when numeric cells change (e.g. via row PropertyChanged subscription) so chart stays in sync.
- Ensure `GraphParameterMap.json` exists and has mappings for the loaded library (e.g. E7111V2) for Generate WDRC to produce updates.
- Fitting must have loaded the memory at least once (from device or offline) so `GetSnapshotsForMemory` returns a non-null snapshot; otherwise Generate WDRC aborts.
