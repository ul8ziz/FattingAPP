---
name: Fix NVM persistence on reconnect
overview: Saved settings do not persist after reconnect because the app currently writes only to device RAM (kActiveMemory / kSystemActiveMemory). The SDK sample shows that persisting to NVM requires WriteParameters(memoryIndex), not WriteParameters(kActiveMemory). The plan is to discover and use the .NET NVM write path, then keep a post-write delay so the device can flush before disconnect.
todos: []
isProject: false
---

# Fix: Saved changes not appearing after reconnect (write to NVM)

## Root cause

Vendor sample [presuite_memory_switch.py](C:\Users\haifa\Downloads\Fatting_App\Ezairo Pre Suite Firmware, Sound Designer and SDK\SoundDesignerSDK\SoundDesignerSDK\samples\win\python\presuite_memory_switch.py) is explicit:

- **WriteParameters(memoryNumber)** (e.g. `WriteParameters(0)`) → "Write all the memories **to NVM**" (lines 146–147).
- **WriteParameters(sd.kActiveMemory)** → "Write Active Memory. ... is now 3 dB **in RAM**" (line 184); later "we only wrote to RAM ... the value **in NVM was still 0**" (lines 190–191).

The app currently calls only `BeginWriteParameters(ParameterSpace.kActiveMemory)` and `BeginWriteParameters(ParameterSpace.kSystemActiveMemory)` in [SoundDesignerService.WriteMemorySnapshotAsync](src/Device/DeviceCommunication/SoundDesignerService.cs) (lines 223–260). So we are writing **RAM** only; we never write to **NVM**. After disconnect, the device reverts to NVM content, so the user sees old values.

## Approach

1. **Discover .NET NVM write API**
  The .NET SDK (SDLib / sdnet) may expose NVM write as:
  - an overload **BeginWriteParameters(int memoryIndex)** or **WriteParameters(int)**, or  
  - a **ParameterSpace** such as `kNvmMemory0` … `kNvmMemory7` (or similar names).
   Steps:
  - In the vendor **documentation** folder (`...\SoundDesignerSDK\SoundDesignerSDK\documentation`), look for Programmer's Guide / API reference sections on WriteParameters, ParameterSpace, NVM, or memory index. Per project rule (§0), base behavior on doc filename + section.
  - **Reflection**: at runtime (or a one-off probe), enumerate `ParameterSpace` enum values and `IProduct` methods named like `BeginWriteParameters` / `WriteParameters` (signatures and parameter types). Log results to Debug/ScanDiagnostics so we know what the .NET SDK offers.
2. **Implement NVM write in the save path**
  In [SoundDesignerService](src/Device/DeviceCommunication/SoundDesignerService.cs), inside **WriteMemorySnapshotAsync** (used by **BurnMemoryToNvmAsync**):
  - Keep current steps: **TrySelectMemoryContext(product, memoryIndex)**, **BuildWritableSnapshotForMemory**, **ApplySnapshotValuesToSdkParameters**.
  - **If** an NVM write is available (e.g. `BeginWriteParameters(memoryIndex)` or `ParameterSpace.kNvmMemoryN`):
    - Call the NVM write for the given `memoryIndex` (and if needed for system, e.g. `kSystemNvmMemory` per Python line 134). Prefer doc-referenced API; add a short comment with doc filename + section.
    - Keep existing **kActiveMemory** and **kSystemActiveMemory** write if the docs say both are required (e.g. RAM first then NVM), or replace with NVM-only if the docs say NVM write is sufficient.
  - **If** no NVM write is found in docs or via reflection:
    - Log once: `[NVM] Not supported by docs: no NVM write API found; persistence may fail after disconnect.`
    - Do **not** invent an API. Optionally try a single reflection call to `BeginWriteParameters(int)` if the product type exposes it (and log success/failure).
3. **Post-write delay**
  Keep the existing 250 ms delay after in-session save and 500 ms after Save and End in [SessionEndService](src/App/Services/SessionEndService.cs). If vendor docs specify a minimum delay after NVM write, use that (or document that we use 500 ms and suggest re-testing).
4. **Verification and diagnostics**
  - Existing **ReloadFromNvm** + verify after save is correct; keep it.
  - **On load after connect**: keep or extend the existing "Load fingerprint" log (e.g. first parameter Id + value) so we can confirm whether reconnected values match what we saved. No new UI.
5. **Docs and README**
  - In [README](README.md) and [FITTING_DEVELOPER_NOTE](docs/FITTING_DEVELOPER_NOTE.md): state that persistence requires writing to **NVM** (e.g. WriteParameters(memoryIndex) or kNvmMemoryN), not only to RAM (kActiveMemory). Document the discovery step (reflection/docs) and that if the .NET SDK does not expose NVM write, the app logs "Not supported by docs" and saving may not persist across disconnect.

## Files to touch


| File                                                                              | Change                                                                                                                                                                         |
| --------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| [SoundDesignerService.cs](src/Device/DeviceCommunication/SoundDesignerService.cs) | Discover NVM write (reflection / ParameterSpace); in WriteMemorySnapshotAsync, call NVM write when available; add doc comment; log "Not supported by docs" when not available. |
| [SessionEndService.cs](src/App/Services/SessionEndService.cs)                     | No logic change; keep 500 ms delay after save (optionally reference doc if delay is specified).                                                                                |
| [README.md](README.md)                                                            | Short note: persistence requires NVM write; discovery and fallback behavior.                                                                                                   |
| [docs/FITTING_DEVELOPER_NOTE.md](docs/FITTING_DEVELOPER_NOTE.md)                  | Same persistence/NVM note; mention reflection discovery and "Not supported by docs" log.                                                                                       |


## Order of work

1. Check vendor documentation folder for WriteParameters / NVM / ParameterSpace (list filenames and relevant sections).
2. Add reflection-based discovery (ParameterSpace values, BeginWriteParameters overloads) and log results.
3. Implement NVM write in `WriteMemorySnapshotAsync` when API is present; otherwise log and leave current (RAM-only) behavior.
4. Update README and FITTING_DEVELOPER_NOTE.
5. Build and verify (no new errors); suggest user test: save, end session, reconnect and confirm values.

## Risk / fallback

If the .NET SDK does not expose any NVM write (no memory-index overload, no kNvmMemoryN), the app will continue to write only RAM and will log that NVM write is not supported. The user would need a firmware/SDK update or vendor guidance for persistent save across disconnect.