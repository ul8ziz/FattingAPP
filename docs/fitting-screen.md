# Fitting Screen

## Overview

The Fitting screen is the main engineering/result screen for hearing aid fitting parameters. It displays device settings in dynamic tabs derived from each parameter's **LongModuleName** (one tab per module), with a Memory 1–8 selector. The screen supports two modes: **offline** (library-first, no device) and **online** (device attached, read/write). It serves as the primary interface for viewing and editing fitting parameters, saving to NVM, and integrating with Audiogram-generated WDRC values.

---

## Screen Responsibilities

| Responsibility | Description |
|----------------|-------------|
| **Parameter display** | Show device parameters grouped by module (dynamic tabs). Only the selected memory is displayed (~595 params per memory). |
| **Parameter editing** | Allow user to edit values via sliders, ComboBoxes, toggles. Changes update the in-memory snapshot and mark memory dirty. |
| **Memory management** | Memory 1–8 selector; shared with Audiogram via `DeviceSessionService.SelectedMemoryIndex`. Load on demand when switching. |
| **Save to device** | Save Current Memory / Save ALL Memories — Burn to NVM, verify read-back, clear dirty. NVM-only policy. |
| **Reload from NVM** | Restore from NVM + Read from RAM for current memory. |
| **Live Mode** | Debounced immediate write (400 ms) when enabled; otherwise changes stay local until Save. |
| **Configure Device** | Manufacturing step when device is connected but not configured (e.g. unprogrammed). |
| **Session integration** | Uses `DeviceSessionService` for snapshots, dirty tracking, connection state. |

---

## UI Structure

The screen uses a `Grid` layout with fixed rows (no outer ScrollViewer) so `VirtualizingStackPanel` in ListViews works correctly.

### Row 0: Header

- **Title:** "Fitting"
- **Caption:** Mode label (Offline / Connected) + visible parameter count
- **Param file:** `ParamFileName` (e.g. loaded .param preset) on the right

### Row 1: Toolbar

| Control | Description |
|---------|-------------|
| **Memory selector** | ComboBox bound to `MemoryNames` (Memory 1–8), `SelectedIndex` → `SelectedMemoryIndex` |
| **Live Mode** | Toggle button; highlighted when enabled |
| **Refresh** | Re-read all parameters from device |
| **Reload from NVM** | Restore + Read current memory from NVM |
| **Configure** | Visible when connected but not configured |
| **Save Current Memory** | Burn current memory to NVM |
| **Save ALL Memories** | Burn all dirty memories to NVM |
| **Status line** | `FittingStatusText` — Connected/Configured, dirty state, "Persisted to NVM (verified)" |

### Row 2: Error Banner

- **Visibility:** `HasError` (red background)
- **Content:** `ErrorMessage`
- **Retry button:** Visible when `ShowRetryInErrorBanner` (hidden for unprogrammed device — retry won't help)

### Row 3: Tab Strip + Search

- **Tab buttons:** `ItemsControl` bound to `Tabs`; each tab calls `SelectTabCommand` with tab `Id`
- **Search box:** `SearchText` bound; 200 ms debounce for `UpdateFilteredItems`

### Row 4: Content (Left/Right Ear Panels)

| Panel | Binding | Visibility |
|-------|---------|------------|
| **Left Ear** | `LeftGroups` | `ShowLeftCard` (Left connected) |
| **Right Ear** | `RightGroups` | `ShowRightCard` (Right connected) |

When only one side is connected, the visible panel spans full width (`Grid.ColumnSpan="3"`).

**Panel structure:**

- **Header:** "Left Ear" / "Right Ear", `SelectedMemoryName`, group count
- **Groups:** `ItemsControl` with `LeftGroupTemplate` / `RightGroupTemplate` — each group is an `Expander` with:
  - **Header:** Group title + param count badge
  - **Content:** `ListView` of `Rows` (lazy-loaded when expander opens)

**Parameter row:** `ParameterFieldCard` with `ParameterValueTemplate` (Slider, ComboBox, Toggle, or read-only TextBlock per data type).

### Loading Overlay

- **Visibility:** `IsLoading`
- **Content:** Semi-transparent overlay + centered card with indeterminate progress bar + `LoadingMessage`

---

## Enable/Disable Rules

| Condition | Effect |
|-----------|--------|
| **Navigation to Fitting** | Enabled when `IsNavigationEnabled` (device connected). Same as Audiogram. |
| **Save Current Memory** | Disabled when: `IsLoading`, `!HasActiveSession`, `IsOfflineMode`, `!IsDeviceConfigured`, `!IsCurrentMemoryDirty` |
| **Save ALL Memories** | Disabled when: `IsLoading`, `!HasActiveSession`, `IsOfflineMode`, `!IsDeviceConfigured`, `!HasAnyDirtyMemory` |
| **Configure Device** | Visible when: `HasActiveSession`, `!IsOfflineMode`, `!IsDeviceConfigured`, `!IsConfiguring` |
| **Reload from NVM** | Disabled when: `IsLoading`, `!HasActiveSession`, `IsOfflineMode`, `!IsDeviceConfigured` |
| **Refresh** | Disabled when: `IsLoading`, `!HasActiveSession` |
| **Live Mode** | Disabled when: `IsOfflineMode` |
| **ShowLeftCard** | `_session.LeftConnected` |
| **ShowRightCard** | `_session.RightConnected` |
| **IsDeviceConfigured** | `DeviceSessionService.IsConfigured` (both sides configured) |

---

## Memory Workflow

### Memory selector

- **Source of truth:** `DeviceSessionService.SelectedMemoryIndex` (0-based). UI label "Memory 1" = index 0 = SDK `kNvmMemory1`.
- **Binding:** ComboBox `SelectedIndex` bound to `FittingViewModel.SelectedMemoryIndex`.

### Synchronization with Audiogram

- **Fitting → Session:** When user changes memory in Fitting, `SelectedMemoryIndex` setter calls `_session.SetSelectedMemoryIndex(value)`, which raises `PropertyChanged`. AudiogramViewModel subscribes and updates its `SelectedMemoryIndex`.
- **Audiogram → Fitting:** When user changes memory in Audiogram, same flow: `SetSelectedMemoryIndex` → `PropertyChanged` → FittingViewModel `OnSessionPropertyChanged` updates `_selectedMemoryIndex` and calls `OnMemoryChangedAsync()`.

### Memory switch sequence (`OnMemoryChangedAsync`)

1. Set `IsMemoryLoadInProgress` and `IsLoading`.
2. For each connected side: `EnsureMemoryLoadedAsync(side, memoryIndex, ct)` — cache hit returns immediately; else load from device or build offline snapshot.
3. For offline session: `_sessionMgr.SwitchMemory(_selectedMemoryIndex)`; `LeftSnapshot` / `RightSnapshot` from session manager.
4. For active session: `TryGetMemorySnapshot` for each side → assign `LeftSnapshot` / `RightSnapshot`.
5. `BuildItemCaches()`, `EnsureTabLoadedAsync(SelectedTabId)`, `UpdateFilteredItems()`, `RefreshSaveState()`.

### Internal mapping

- `MemoryNames[i]` = "Memory {i+1}" for display.
- `SelectedMemoryName` = `MemoryNames[SelectedMemoryIndex]`.

---

## Parameter Loading Workflow

### Data sources

| Source | When used |
|--------|-----------|
| **Device** | Connected and configured: `ReloadFromNvmAsync` → `ReadMemorySnapshotAsync` → `BuildSnapshotForMemory` |
| **Library (offline)** | Not configured: `SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(product, memoryIndex, side)` with library defaults |
| **Session cache** | `_session.TryGetMemorySnapshot(side, memoryIndex, out snapshot)` — used when switching memory if already loaded |

### Load pipeline (`LoadSettingsAsync`)

1. Cancel any in-flight load; set `IsLoading`; clear `LeftSnapshot` / `RightSnapshot`; invalidate `_settingsCache`.
2. Sync `SelectedMemoryIndex` from `_session.SelectedMemoryIndex`.
3. If device known not configured: build library snapshots for both sides; skip `EnsureInitializedAndConfigured` and `ReadParameters`.
4. Else: call `DeviceInitializationService.EnsureInitializedAndConfiguredAsync` per side.
5. If still not configured: show `LastConfigError`; build library snapshots for display.
6. If configured: `_soundDesigner.ReloadFromNvmAsync` per side → `LeftSnapshot` / `RightSnapshot`; `_session.SetMemorySnapshot(…)`.
7. `BuildItemCaches()`, `RebuildDynamicTabs()`, `UpdateFilteredItems()`.

### Offline load (`LoadOfflineFromLibrary`)

- Called from `OnNavigatedTo` when `_sessionMgr.IsOffline && !_session.HasActiveSession`.
- Uses `_sessionMgr.LeftSnapshot`; `RightSnapshot = null`.
- Only left panel shown.

### ParameterSpace usage

- SDK uses `ParameterSpace.kActiveMemory` / `kSystemActiveMemory` for read.
- NVM write uses `ParameterSpace.kNvmMemoryN` (discovered via reflection).
- Memory switching: `TrySelectMemoryContext(product, memoryIndex)` before read/write.

---

## Tab and Group Generation Workflow

### Tab creation (`RebuildDynamicTabs`)

- Build union of categories from `LeftSnapshot` and `RightSnapshot`.
- Each `SettingCategory` has `Id` (from `LongModuleName`), `Title`, `Sections`.
- One `TabDescriptor` per category: `Id`, `Title`, `GroupsCount`, `IsLoaded`.
- Tabs are ordered; no hardcoded tab list.
- If current `SelectedTabId` is not in tabs, select first tab.

### Group creation (`BuildGroupDescriptorsForTab`)

- For each `SettingSection` in the category: `GroupDescriptor { Id, Title, ParamsCount, IsLoaded }`.
- `BuildSnapshotForMemory` produces one section per module (title "Memory N"); so typically one group per tab.

### Lazy loading

1. **Tab selection:** `SelectedTabId` setter → `EnsureTabLoadedAsync(tabId)`.
2. **EnsureTabLoadedAsync:** Build `LeftGroups` / `RightGroups` from `BuildGroupDescriptorsForTab`; for each group call `EnsureGroupLoadedAsync(side, groupId)`.
3. **EnsureGroupLoadedAsync:** Called when group Expander opens (or on tab load). Check `_leftGroupRowsCache` / `_rightGroupRowsCache` for `(tabId, groupId)`. If cached, assign `group.Rows`; else: `BuildRowsForGroup` → `SettingItemViewModel` per item → cache → assign.

### Item cache

- `BuildItemCaches()` builds `_leftItemCache` / `_rightItemCache`: `Dictionary<tabId, List<SettingItemViewModel>>`.
- `SettingItemViewModel` wraps `SettingItem`; created once per snapshot load; reused across tab switches.
- `FilteredLeftItems` / `FilteredRightItems` are built from cache + search filter; used for flat list binding (if applicable). The primary UI uses `LeftGroups` / `RightGroups` with `Rows` per group.

### Search / filter

- `SearchText` change → `DebouncedUpdateFilteredItems` (200 ms).
- `BuildRowsForGroup` and `BuildFlatItemsFromCache` filter by `MatchesSearch(item, searchLower)` — matches `Name`, `DisplayName`, `Description`, `Id`.

---

## Parameter Editing Workflow

### How the user edits

- **ValueTemplate** in `FittingView.xaml.cs` provides:
  - **Double/Int:** Slider + `TextBox` (read-only) with `FormattedValue`; `Value` two-way bound.
  - **Enum:** ComboBox with `SelectedEnumIndex` two-way.
  - **Bool:** CheckBox with `Value` two-way via `ObjectToBoolConverter`.
  - **String:** Read-only `TextBlock`.
- `ParameterFieldCard` receives `DataContext` = `SettingItemViewModel`; `ValueTemplate` renders the control.

### What happens when value changes

- `SettingItemViewModel.Value` setter → `_model.Value = value` (SettingItem).
- `SettingItem` raises `PropertyChanged` for `Value`.
- `SubscribeSnapshotDirty` has subscribed to each `SettingItem.PropertyChanged` on `Value`:
  - `_settingsCache.DirtyBuffer.Add(side, item.Id, item.Value)`
  - `_session.MarkMemoryDirty(side, SelectedMemoryIndex)`
  - `HasUnsavedChanges = true`
  - `OnPropertyChanged(IsCurrentMemoryDirty)`, `OnPropertyChanged(HasAnyDirtyMemory)`, `OnPropertyChanged(FittingStatusText)`

### Dirty state tracking

- **Per (side, memoryIndex):** `DeviceSessionService._dirtyMemories` (HashSet).
- **IsCurrentMemoryDirty:** True if either connected side has that memory dirty.
- **HasAnyDirtyMemory:** True if any memory is dirty.
- **HasUnsavedChanges:** Mirrors `_session.HasDirty`; `_session.SetDirty(value)`.

### Local vs device-bound

- Edits update the in-memory snapshot (`SettingItem.Value`) immediately.
- No SDK write until **Save Current Memory** / **Save ALL Memories** (or Live Mode debounce).
- Changes are persisted only after Burn to NVM + verify.

---

## Save / Write / Reload Workflow

### Save Current Memory (`SaveCurrentMemoryToNvmAsync`)

1. Guard: `HasActiveSession`, `!IsOfflineMode`, `IsCurrentMemoryDirty`, `IsConfigured`.
2. Unprogrammed check: if Serial=-1 or 0, show error and return.
3. `EnsureMemoryLoadedAsync` for each connected side (ensure snapshot in cache).
4. `GetSnapshotsForMemory(SelectedMemoryIndex)` → `SaveMemoryForSideToNvmAsync` per side.

### Save Memory For Side To NVM (`SaveMemoryForSideToNvmAsync`)

1. `EnsureInitializedAndConfiguredAsync(session, side, ct)`.
2. `_soundDesigner.BurnMemoryToNvmAsync(product, adaptor, snapshot, memoryIndex, …)` — logs `[NVM] Burn M#`.
3. `_soundDesigner.VerifyMemoryMatchesNvmAsync(…)` — read-back and compare; logs `[NVM] Verify M# OK/FAIL`.
4. On success: `_session.ClearMemoryDirty(side, memoryIndex)`, `_settingsCache.InvalidateCacheForSide(side)`, `_nvmSaveStatus = "Persisted to NVM (verified)"`.
5. `Task.Delay(250)` before any follow-up.

### Save ALL Memories (`SaveAllMemoriesToNvmAsync`)

- Iterates memoryIndex 0–7; if `onlyDirty` and neither side dirty, skip.
- For each memory: `EnsureMemoryLoadedAsync` → `GetSnapshotsForMemory` → `SaveMemoryForSideToNvmAsync` per dirty side.

### Reload from NVM (`ReloadFromNvmAsync`)

1. Guard: `HasActiveSession`, `!IsOfflineMode`, `IsConfigured`.
2. `EnsureInitializedAndConfiguredAsync` per side.
3. `_soundDesigner.ReloadFromNvmAsync` per side (Restore + Read from RAM).
4. `_session.SetMemorySnapshot`; assign `LeftSnapshot` / `RightSnapshot`.
5. `BuildItemCaches()`, `UpdateFilteredItems()`, `RefreshSaveState()`.

### Verification

- After Burn: `VerifyMemoryMatchesNvmAsync` compares key parameters (max 50) with read-back.
- Some parameters (e.g. `X_AuxiliaryAttenuation`) may be skipped via `KnownVerifySkipIds` for known device variance.

### Error handling

- SDK exceptions (e.g. `E_UNCONFIGURED_DEVICE`, `E_SEND_FAILURE`) are caught and converted to user-friendly messages via `GetUserFriendlyMessage`.
- `ErrorMessage` set; `ShowRetryInErrorBanner` false for unprogrammed (retry won't help).
- Partial failure: one side can fail while the other loads/saves.

---

## WDRC Workflow

### Where WDRC appears

- WDRC parameters are grouped under a module tab (e.g. "WDRC" or product-specific module name) from `LongModuleName`.
- No special WDRC tab in code; tabs are fully dynamic from SDK.

### WDRC parameter structure

- Parameters come from `Product.Memories[memoryIndex].Parameters`; grouped by `LongModuleName`.
- E7111V2: `GraphParameterMap.json` maps WDRC gain param IDs (`X_WDRC_LowLevelGain`, `X_WDRC_HighLevelGain`) for levels 40–100 dB and I/O frequencies.

### Audiogram-generated WDRC values

1. Audiogram: user enters thresholds → Generate WDRC → `IPrescriptionEngine.ComputeTargets` (stub) → `IParameterMappingService.MapTargetsToDevice` (E7111V2ParameterMappingService uses GraphParameterMap).
2. `IAudiogramIntegrationService.ApplyToFitting(mappingResult, side, memoryIndex)`:
   - Gets snapshot from `GetSnapshotsForMemory(memoryIndex)`.
   - For each `(paramId, value)` in `mappingResult.ParameterUpdates`: finds `SettingItem` by Id, sets `Value`.
   - `SetMemorySnapshot`, `MarkMemoryDirty`.
3. User opens Fitting → sees updated WDRC values in the snapshot; Save to Device persists.

### Auto-generated vs manual

- No separate "auto-generated" flag in Fitting. All edits (manual or from Audiogram) update the same snapshot; dirty tracking is per (side, memory).

---

## Other Module Workflows

### EQ, MPO, AGCo, etc.

- All modules are treated equally. Tabs are built from `LongModuleName`; no hardcoded module list.
- Each module tab has one or more sections (groups); parameters are `SettingItem` with `Value`, `Min`, `Max`, `DataType`, etc.
- Editing flow is identical for all modules.

### System parameters

- `BuildSystemSnapshot` builds a separate "System" tab for system memory parameters. Not currently used in the main Fitting flow; `BuildSnapshotForMemory` is the primary path.

---

## Session and Device State Integration

### Connection state

- `DeviceSessionService`: `LeftConnected`, `RightConnected`, `IsConfigured`, `LastConfigError`, `SdkManager`, `ConnectionService`.
- Fitting subscribes to `PropertyChanged` for `SelectedMemoryIndex`, `IsConfigured`, `LastConfigError`, `LeftConnected`, `RightConnected`, `HasDirty`.

### Offline vs connected session

- **Offline:** `FittingSessionManager.IsOffline`; no device; `LoadOfflineFromLibrary`; only left panel.
- **Connected:** `LoadSettingsAsync`; device read or library fallback if not configured.

### Refresh after connection

- `OnNavigatedTo` → `LoadSettingsAsync` when not offline and not loading.
- After Configure success: `LoadSettingsAsync` re-reads parameters.

### Status updates

- `FittingStatusText` reflects: connection, configured, dirty, "Persisted to NVM (verified)".
- `SaveToDeviceToolTip`, `SaveAllMemoriesToolTip`, `ConfigureDeviceToolTip` explain disabled state.

---

## Validation and Constraints

### Parameter validity

- `SettingItem` has `Min`, `Max`, `Step`, `ReadOnly`, `DataType`.
- Sliders use `Minimum`/`Maximum` from model; `ReadOnly` disables control.
- No explicit SDK validity check in Fitting; value is applied to snapshot and written on Save.

### Range checking

- Slider bindings enforce Min/Max. `DoubleNaNToFallbackConverter` handles NaN.
- Enum: `SelectedEnumIndex` ensures value is within `EnumValues`.

### Blocked edits

- `ReadOnly` parameters: control `IsEnabled` false via `InverseBoolConverter`.

### Dependency-aware validation

- Not implemented; parameters are independent.

---

## Navigation and Integration with Audiogram

### How Fitting is opened

- Sidebar: "Fitting" button → `NavigateCommand` with parameter "Fitting".
- After Connect: app auto-navigates to Fitting.

### Open Fitting from Audiogram

- Audiogram has "Open Fitting" button; navigates to Fitting with same `SelectedMemoryIndex`.
- Shared memory: `DeviceSessionService.SelectedMemoryIndex` is single source of truth.

### Memory preservation

- Navigation does not change `SelectedMemoryIndex`; both screens bind to session.

### Audiogram-driven changes in Fitting

- Generate WDRC → `ApplyToFitting` updates snapshot in `DeviceSessionService`; marks memory dirty.
- `SetMemorySnapshot` updates `_memorySnapshots[(side, memoryIndex)]`.
- Fitting's `LeftSnapshot` / `RightSnapshot` are set from `GetSnapshotsForMemory` when loading; on memory switch or when Fitting is already showing that memory, the snapshot may be updated by Audiogram before user navigates. FittingViewModel does not directly subscribe to snapshot updates from Audiogram; the snapshot is refreshed when user switches memory or when `LoadSettingsAsync` runs.

---

## Error Handling

| Scenario | Handling |
|----------|----------|
| **No connected device** | `IsNavigationEnabled` false; Fitting not reachable. Offline mode: `LoadOfflineFromLibrary`. |
| **No loaded session** | `LoadOfflineFromLibrary` or `LoadSettingsAsync`; error banner if connection service null. |
| **Memory mismatch** | N/A; memory index is shared. |
| **Read failures** | `ErrorMessage` set; `ShowRetryInErrorBanner` true; Retry calls `LoadSettingsAsync`. |
| **Write failures** | `ErrorMessage` from `BurnMemoryToNvmAsync` or `VerifyMemoryMatchesNvmAsync`; `SaveMemoryForSideToNvmAsync` returns early. |
| **Invalid parameter values** | SDK may throw on `SetValue`; caught and logged; user sees error. |
| **SDK exceptions** | `LogFittingException` writes to log.txt; `GetUserFriendlyMessage` converts to user-friendly text. |
| **Stale UI state** | `InvalidateCacheForSide` after save; `BuildItemCaches` on load/reload. |
| **Unprogrammed device** | Serial=-1 or 0; Save disabled; `ShowRetryInErrorBanner` false for unprogrammed. |
| **Partial device/session** | One side can fail; other side still loads; error message concatenated. |

---

## Important Classes and Services

| Class/Service | Location | Responsibility |
|---------------|----------|----------------|
| **FittingViewModel** | `App.ViewModels` | Main VM; state, commands, load/save, dirty tracking |
| **FittingView** | `App.Views` | XAML layout; bindings; Expander handlers |
| **ParameterFieldCard** | `App.Views.Controls` | Parameter row UI; value template |
| **SettingItemViewModel** | `FittingViewModel.cs` (nested) | Wraps SettingItem; Value, UI helpers |
| **GroupDescriptor** | `App.Models` | Group metadata; Rows (lazy) |
| **TabDescriptor** | `App.Models` | Tab metadata |
| **DeviceSessionService** | `App.Services` | Session state; snapshots; SelectedMemoryIndex; dirty |
| **FittingSessionManager** | `App.Services` | Session phase; offline mode; IsDeviceConfigured |
| **SoundDesignerSettingsEnumerator** | `Device.DeviceCommunication` | BuildSnapshotForMemory; tab/group structure |
| **SoundDesignerService** | `Device.DeviceCommunication` | ReloadFromNvmAsync; BurnMemoryToNvmAsync; VerifyMemoryMatchesNvmAsync |
| **DeviceInitializationService** | `App.Services` | EnsureInitializedAndConfiguredAsync; RunConfigureDeviceAsync |
| **SettingsCache** | `App.Services` | Snapshot cache; DirtyBuffer |

---

## Notes for Developers

### Graphs panel

- **GraphsPanelView** and **GraphsPanelViewModel** exist; they display Freq. Gain and I/O curves from snapshot via `GraphParameterMap.json`.
- `GraphsPanelViewModel.SetDataSource(snapshot, memoryIndex, libraryKey)` is called when snapshot/memory changes.
- The Graphs panel is **not** currently embedded in the Fitting view layout; it may be shown elsewhere or reserved for future integration.

### Performance

- Lazy tabs and groups: only headers first; rows when expander opens.
- Item cache: `SettingItemViewModel` created once per snapshot load; reused across tab switches.
- Search: 200 ms debounce.
- VirtualizingStackPanel in ListViews for group rows.

### Coupling points

- **Audiogram:** Shared `SelectedMemoryIndex`; `ApplyToFitting` updates session snapshot.
- **Connect Devices:** Session established after Connect; Fitting loads on navigate.
- **Session End:** `RunSaveAllOnDispatcherAsync` uses same Burn/Verify path.

### Risks / edge cases

- **Thread affinity:** SDK COM objects require STA; all SDK calls run on UI thread or via SdkGate (which preserves context).
- **Memory switch during save:** `_memoryIoLock` serializes memory load; save uses `GetSnapshotsForMemory` at save time.
- **Unprogrammed device:** Save disabled; Configure may be required for Read/Write.

### Related documentation

- **docs/FITTING_DEVELOPER_NOTE.md** — SDK behavior, NVM flow, Configure Device, session-end save, Audiogram flow.
- **docs/connect-devices-screen.md** — Connect flow, library, programmer, discovery.
- **docs/audiogram-screen.md** — Audiogram workflow, Generate WDRC, integration with Fitting.
