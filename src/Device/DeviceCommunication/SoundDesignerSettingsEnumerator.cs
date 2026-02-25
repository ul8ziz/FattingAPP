using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Enumerates device settings from IProduct using the SDK's proper parameter API:
    ///   Product.Memories → ParameterMemoryList → ParameterMemory → Parameters → ParameterList → Parameter
    /// Groups parameters by Parameter.LongModuleName to produce dynamic tabs.
    /// No hardcoded tabs—tab list is derived entirely from what the connected device exposes.
    ///
    /// Preferred entry point: BuildSnapshotForMemory (enumerates ONE memory = ~595 params).
    /// Legacy entry point: BuildFullSnapshot (enumerates ALL 8 memories = ~4760 params).
    /// </summary>
    public static class SoundDesignerSettingsEnumerator
    {
        /// <summary>
        /// Builds a snapshot for a SINGLE memory index (0-7).
        /// This is the preferred method — shows ~595 parameters grouped by LongModuleName,
        /// instead of all 4760 (8 memories). Each module becomes one tab with one section.
        /// </summary>
        /// <param name="product">The IProduct instance (offline or device-connected).</param>
        /// <param name="memoryIndex">Memory index 0-7 to enumerate.</param>
        /// <param name="side">Left or Right ear designation.</param>
        /// <returns>Snapshot with categories grouped by LongModuleName, one section per category.</returns>
        public static DeviceSettingsSnapshot BuildSnapshotForMemory(IProduct product, int memoryIndex, DeviceSide side)
        {
            var snapshot = new DeviceSettingsSnapshot { Side = side };
            if (product == null) return snapshot;

            List<SettingItem> memParams;
            try
            {
                memParams = EnumerateSingleMemory(product, memoryIndex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsEnumerator] BuildSnapshotForMemory({memoryIndex}) error: {ex.Message}");
                memParams = new List<SettingItem>();
            }

            if (memParams.Count == 0)
            {
                Debug.WriteLine($"[SettingsEnumerator] WARNING: Memory {memoryIndex} returned 0 parameters");
                var empty = new SettingCategory { Id = "NoParams", Title = "No Parameters Found" };
                var sec = new SettingSection { Id = "info", Title = "Information" };
                sec.Items.Add(new SettingItem
                {
                    Id = "_info", Name = "No parameters available",
                    DisplayName = $"Memory {memoryIndex + 1} has no parameters.",
                    Description = "Ensure the library is loaded correctly.",
                    SettingDataType = SettingItem.DataType.String, ReadOnly = true, Value = "—"
                });
                empty.Sections.Add(sec);
                snapshot.Categories.Add(empty);
                return snapshot;
            }

            // Group by LongModuleName → one SettingCategory per module
            var grouped = memParams
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ModuleName) ? "General" : p.ModuleName)
                .OrderBy(g => g.Key);

            foreach (var moduleGroup in grouped)
            {
                var cat = new SettingCategory
                {
                    Id = SanitizeId(moduleGroup.Key),
                    Title = moduleGroup.Key
                };

                // Single section per module (no Memory 1..8 stacking)
                var section = new SettingSection
                {
                    Id = $"mem{memoryIndex}",
                    Title = $"Memory {memoryIndex + 1}"
                };

                foreach (var item in moduleGroup)
                    section.Items.Add(item);

                if (section.Items.Count > 0)
                    cat.Sections.Add(section);

                if (cat.Sections.Count > 0)
                    snapshot.Categories.Add(cat);
            }

            Debug.WriteLine($"[SettingsEnumerator] BuildSnapshotForMemory({memoryIndex}): " +
                            $"{snapshot.Categories.Count} modules, {memParams.Count} params");
            return snapshot;
        }

        /// <summary>
        /// Builds a snapshot for the SystemMemory parameters (global, not per-memory).
        /// These are shown in a separate "System" tab.
        /// </summary>
        public static DeviceSettingsSnapshot BuildSystemSnapshot(IProduct product, DeviceSide side)
        {
            var snapshot = new DeviceSettingsSnapshot { Side = side };
            if (product == null) return snapshot;

            List<SettingItem> sysParams;
            try
            {
                sysParams = EnumerateSystemMemory(product);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsEnumerator] BuildSystemSnapshot error: {ex.Message}");
                sysParams = new List<SettingItem>();
            }

            if (sysParams.Count == 0)
            {
                Debug.WriteLine("[SettingsEnumerator] SystemMemory returned 0 parameters");
                return snapshot;
            }

            var cat = new SettingCategory { Id = "System", Title = "System" };
            var section = new SettingSection { Id = "system", Title = "System Parameters" };
            foreach (var item in sysParams)
                section.Items.Add(item);
            cat.Sections.Add(section);
            snapshot.Categories.Add(cat);

            Debug.WriteLine($"[SettingsEnumerator] BuildSystemSnapshot: {sysParams.Count} system params");
            return snapshot;
        }
        /// <summary>
        /// Builds a dynamic snapshot grouped by module name (one category per module).
        /// Only modules that have parameters appear as tabs. No hardcoded tab list.
        /// </summary>
        public static DeviceSettingsSnapshot BuildFullSnapshot(IProduct product, DeviceSide side)
        {
            var snapshot = new DeviceSettingsSnapshot { Side = side };
            if (product == null) return snapshot;

            // Try proper SDK Memories API first; fallback to reflection enumeration
            List<SettingItem> allParams;
            bool usedProperApi = false;

            try
            {
                allParams = EnumerateViaMemoriesApi(product);
                if (allParams.Count > 0)
                {
                    usedProperApi = true;
                    Debug.WriteLine($"[SettingsEnumerator] SDK Memories API: {allParams.Count} parameters discovered");
                }
                else
                {
                    Debug.WriteLine("[SettingsEnumerator] SDK Memories API returned 0 parameters, falling back to reflection");
                    allParams = EnumerateViaReflection(product);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsEnumerator] Memories API error: {ex.Message}. Falling back to reflection.");
                allParams = EnumerateViaReflection(product);
            }

            if (allParams.Count == 0)
            {
                Debug.WriteLine("[SettingsEnumerator] WARNING: No parameters found from any method");
                var empty = new SettingCategory { Id = "NoParams", Title = "No Parameters Found" };
                var sec = new SettingSection { Id = "info", Title = "Information" };
                sec.Items.Add(new SettingItem
                {
                    Id = "_info", Name = "No parameters available",
                    DisplayName = "No parameters could be read from the connected device.",
                    Description = "Ensure the device is connected and initialized, then try Refresh.",
                    SettingDataType = SettingItem.DataType.String, ReadOnly = true, Value = "—"
                });
                empty.Sections.Add(sec);
                snapshot.Categories.Add(empty);
                return snapshot;
            }

            // Group by ModuleName → SettingCategory (dynamic tabs)
            var grouped = allParams
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ModuleName) ? "General" : p.ModuleName)
                .OrderBy(g => g.Key);

            foreach (var moduleGroup in grouped)
            {
                var cat = new SettingCategory
                {
                    Id = SanitizeId(moduleGroup.Key),
                    Title = moduleGroup.Key
                };

                // Within each module, group by MemoryName as sections
                var memGroups = moduleGroup
                    .GroupBy(p => string.IsNullOrWhiteSpace(p.MemoryName) ? "Parameters" : p.MemoryName)
                    .OrderBy(g => g.Key);

                foreach (var memGroup in memGroups)
                {
                    var section = new SettingSection
                    {
                        Id = SanitizeId(memGroup.Key),
                        Title = memGroup.Key
                    };
                    foreach (var item in memGroup)
                        section.Items.Add(item);
                    if (section.Items.Count > 0)
                        cat.Sections.Add(section);
                }

                if (cat.Sections.Count > 0)
                    snapshot.Categories.Add(cat);
            }

            Debug.WriteLine($"[SettingsEnumerator] Snapshot: {snapshot.Categories.Count} modules, " +
                            $"{snapshot.Categories.Sum(c => c.Sections.Sum(s => s.Items.Count))} total params " +
                            $"(usedProperApi={usedProperApi})");
            return snapshot;
        }

        #region SDK Memories API (proper path)

        /// <summary>
        /// Enumerates parameters from a SINGLE memory by index (0-7).
        /// Accesses Product.Memories[memoryIndex].Parameters directly.
        /// </summary>
        private static List<SettingItem> EnumerateSingleMemory(IProduct product, int memoryIndex)
        {
            var items = new List<SettingItem>();
            var productType = product.GetType();

            var memoriesProp = productType.GetProperty("Memories", BindingFlags.Public | BindingFlags.Instance);
            if (memoriesProp == null)
            {
                Debug.WriteLine("[SettingsEnumerator] Product.Memories property not found");
                return items;
            }

            var memories = memoriesProp.GetValue(product);
            if (memories == null)
            {
                Debug.WriteLine("[SettingsEnumerator] Product.Memories returned null");
                return items;
            }

            // Get the specific memory by index using Item[int] indexer or IEnumerable skip
            object? targetMemory = null;
            string memoryName = $"Memory {memoryIndex + 1}";

            // Try Item[int] indexer first (ParameterMemoryList pattern)
            var itemProp = memories.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
            if (itemProp != null)
            {
                try
                {
                    targetMemory = itemProp.GetValue(memories, new object[] { memoryIndex });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsEnumerator] Indexer[{memoryIndex}] error: {ex.Message}");
                }
            }

            // Fallback: iterate IEnumerable and skip to index
            if (targetMemory == null && memories is IEnumerable memoriesEnum)
            {
                int idx = 0;
                foreach (var mem in memoriesEnum)
                {
                    if (idx == memoryIndex) { targetMemory = mem; break; }
                    idx++;
                }
            }

            if (targetMemory == null)
            {
                Debug.WriteLine($"[SettingsEnumerator] Memory index {memoryIndex} not found");
                return items;
            }

            memoryName = TryGetStringProperty(targetMemory, "Name")
                      ?? TryGetStringProperty(targetMemory, "Description")
                      ?? memoryName;

            // Enumerate Parameters from this single memory
            var paramsProp = targetMemory.GetType().GetProperty("Parameters", BindingFlags.Public | BindingFlags.Instance);
            if (paramsProp == null)
            {
                Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}': no Parameters property");
                return items;
            }

            var parameters = paramsProp.GetValue(targetMemory);
            if (parameters == null || parameters is not IEnumerable paramsEnum)
            {
                Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}': Parameters is null or not enumerable");
                return items;
            }

            foreach (var param in paramsEnum)
            {
                if (param == null) continue;
                try
                {
                    var item = BuildSettingItemFromSdkParameter(param, memoryName);
                    if (item != null) items.Add(item);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}' param error: {ex.Message}");
                }
            }

            Debug.WriteLine($"[SettingsEnumerator] EnumerateSingleMemory({memoryIndex}, '{memoryName}'): {items.Count} parameters");
            return items;
        }

        /// <summary>
        /// Enumerates parameters from Product.SystemMemory (global parameters).
        /// </summary>
        private static List<SettingItem> EnumerateSystemMemory(IProduct product)
        {
            var items = new List<SettingItem>();
            var productType = product.GetType();

            var sysProp = productType.GetProperty("SystemMemory", BindingFlags.Public | BindingFlags.Instance);
            if (sysProp == null)
            {
                Debug.WriteLine("[SettingsEnumerator] Product.SystemMemory property not found");
                return items;
            }

            var systemMemory = sysProp.GetValue(product);
            if (systemMemory == null)
            {
                Debug.WriteLine("[SettingsEnumerator] Product.SystemMemory returned null");
                return items;
            }

            var paramsProp = systemMemory.GetType().GetProperty("Parameters", BindingFlags.Public | BindingFlags.Instance);
            if (paramsProp == null)
            {
                Debug.WriteLine("[SettingsEnumerator] SystemMemory has no Parameters property");
                return items;
            }

            var parameters = paramsProp.GetValue(systemMemory);
            if (parameters == null || parameters is not IEnumerable paramsEnum)
            {
                Debug.WriteLine("[SettingsEnumerator] SystemMemory.Parameters is null or not enumerable");
                return items;
            }

            foreach (var param in paramsEnum)
            {
                if (param == null) continue;
                try
                {
                    var item = BuildSettingItemFromSdkParameter(param, "System");
                    if (item != null) items.Add(item);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsEnumerator] SystemMemory param error: {ex.Message}");
                }
            }

            Debug.WriteLine($"[SettingsEnumerator] EnumerateSystemMemory: {items.Count} parameters");
            return items;
        }

        /// <summary>
        /// Enumerates parameters via: Product.Memories → foreach ParameterMemory → .Parameters → foreach Parameter.
        /// LEGACY: enumerates ALL 8 memories (4760 params). Prefer BuildSnapshotForMemory instead.
        /// </summary>
        private static List<SettingItem> EnumerateViaMemoriesApi(IProduct product)
        {
            var items = new List<SettingItem>();
            var productType = product.GetType();

            // Device data is already loaded by SoundDesignerService (ConfigureDevice + ReadParameters or ReadFromDevice).
            // The enumerator only reads in-memory parameter objects—no device I/O here.

            // Access Product.Memories
            var memoriesProp = productType.GetProperty("Memories", BindingFlags.Public | BindingFlags.Instance);
            if (memoriesProp == null)
            {
                Debug.WriteLine("[SettingsEnumerator] Product.Memories property not found");
                return items;
            }

            var memories = memoriesProp.GetValue(product);
            if (memories == null)
            {
                Debug.WriteLine("[SettingsEnumerator] Product.Memories returned null");
                return items;
            }

            // Iterate each ParameterMemory
            if (memories is IEnumerable memoriesEnum)
            {
                int memIndex = 0;
                foreach (var memory in memoriesEnum)
                {
                    if (memory == null) continue;
                    memIndex++;
                    string memoryName = TryGetStringProperty(memory, "Name")
                                     ?? TryGetStringProperty(memory, "Description")
                                     ?? $"Memory {memIndex}";

                    // Get Parameters property from ParameterMemory
                    var paramsProp = memory.GetType().GetProperty("Parameters", BindingFlags.Public | BindingFlags.Instance);
                    if (paramsProp == null)
                    {
                        Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}': no Parameters property");
                        continue;
                    }

                    var parameters = paramsProp.GetValue(memory);
                    if (parameters == null || parameters is not IEnumerable paramsEnum)
                    {
                        Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}': Parameters is null or not enumerable");
                        continue;
                    }

                    int paramCount = 0;
                    foreach (var param in paramsEnum)
                    {
                        if (param == null) continue;
                        try
                        {
                            var item = BuildSettingItemFromSdkParameter(param, memoryName);
                            if (item != null)
                            {
                                items.Add(item);
                                paramCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}' param error: {ex.Message}");
                        }
                    }
                    Debug.WriteLine($"[SettingsEnumerator] Memory '{memoryName}': {paramCount} parameters");
                }
            }
            else
            {
                // Try Count/Item indexer pattern (ParameterMemoryList may not implement IEnumerable)
                items.AddRange(EnumerateFromIndexer(memories, "Memories"));
            }

            return items;
        }

        /// <summary>
        /// Creates a SettingItem from an SDK Parameter object using reflection to read:
        /// Name, Description, Units, LongModuleName, ShortModuleName, Type,
        /// BooleanValue, DoubleValue, DoubleMin, DoubleMax, Value, Min, Max,
        /// ListValues, TextListValues, StepSize.
        ///
        /// IMPORTANT: Value-specific properties (BooleanValue, DoubleValue, Value, Min, Max, etc.)
        /// use TryGetValueDirect() which bypasses _failedGetters. This prevents cross-contamination
        /// where a BooleanValue failure on a Double parameter blocks all subsequent Boolean parameters.
        /// Only metadata properties (Name, Description, etc.) use the cached _failedGetters.
        /// </summary>
        [DebuggerNonUserCode]
        private static SettingItem? BuildSettingItemFromSdkParameter(object param, string memoryName)
        {
            var type = param.GetType();
            var name = TryGetStringProperty(param, "Name");
            var id = TryGetStringProperty(param, "Id") ?? name;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id)) return null;

            var item = new SettingItem
            {
                Id = id ?? "",
                Name = name ?? id ?? "",
                ParameterId = id ?? "",
                Description = TryGetStringProperty(param, "Description") ?? "",
                Unit = TryGetStringProperty(param, "Units") ?? TryGetStringProperty(param, "Unit") ?? "",
                ModuleName = TryGetStringProperty(param, "LongModuleName")
                          ?? TryGetStringProperty(param, "ShortModuleName")
                          ?? TryGetStringProperty(param, "ModuleName")
                          ?? "",
                MemoryName = memoryName,
                SdkParameterRef = param,
                ReadOnly = false
            };
            item.DisplayName = item.Name;

            // Read ReadOnly flag if available
            var roVal = TryGetValueDirect(param, "ReadOnly");
            if (roVal is bool ro) item.ReadOnly = ro;

            // Determine type and read value.
            // Use TryGetValueDirect (not TryGetPropertyValue) for value-access properties
            // to avoid _failedGetters cross-contamination between different parameter data types.
            var paramTypeStr = TryGetStringProperty(param, "Type")?.ToLowerInvariant() ?? "";
            var paramTypeObj = TryGetPropertyValue(param, "Type");

            // Also try numeric enum value for SDK ParameterType (0=Bool,1=Double,2=Integer,3=IndexedList,4=TextList)
            int paramTypeInt = -1;
            if (paramTypeObj is int pti) paramTypeInt = pti;
            else if (paramTypeObj is Enum) { try { paramTypeInt = Convert.ToInt32(paramTypeObj); } catch { } }

            if (paramTypeStr.Contains("bool") || IsSdkParamType(paramTypeObj, "Boolean", "kBoolean") || paramTypeInt == 0)
            {
                item.SettingDataType = SettingItem.DataType.Bool;
                item.Value = TryGetValueDirect(param, "BooleanValue")
                          ?? TryGetValueDirect(param, "Value");
            }
            else if (paramTypeStr.Contains("double") || paramTypeStr.Contains("float") ||
                     IsSdkParamType(paramTypeObj, "Double", "kDouble") || paramTypeInt == 1)
            {
                item.SettingDataType = SettingItem.DataType.Double;
                item.Value = TryGetValueDirect(param, "DoubleValue")
                          ?? TryGetValueDirect(param, "Value");
                var dMin = TryGetValueDirect(param, "DoubleMin") ?? TryGetValueDirect(param, "Min");
                var dMax = TryGetValueDirect(param, "DoubleMax") ?? TryGetValueDirect(param, "Max");
                if (dMin != null && double.TryParse(dMin.ToString(), out var minVal)) item.Min = minVal;
                if (dMax != null && double.TryParse(dMax.ToString(), out var maxVal)) item.Max = maxVal;
                // Read step size for slider tick frequency
                var dStep = TryGetValueDirect(param, "StepSize") ?? TryGetValueDirect(param, "Step");
                if (dStep != null && double.TryParse(dStep.ToString(), out var stepVal) && stepVal > 0) item.Step = stepVal;
            }
            else if (paramTypeStr.Contains("int") || IsSdkParamType(paramTypeObj, "Integer", "kInteger") || paramTypeInt == 2)
            {
                item.SettingDataType = SettingItem.DataType.Int;
                item.Value = TryGetValueDirect(param, "Value");
                var iMin = TryGetValueDirect(param, "Min");
                var iMax = TryGetValueDirect(param, "Max");
                if (iMin != null && double.TryParse(iMin.ToString(), out var minVal)) item.Min = minVal;
                if (iMax != null && double.TryParse(iMax.ToString(), out var maxVal)) item.Max = maxVal;
                // Read step size for slider tick frequency
                var iStep = TryGetValueDirect(param, "StepSize") ?? TryGetValueDirect(param, "Step");
                if (iStep != null && double.TryParse(iStep.ToString(), out var stepVal) && stepVal > 0) item.Step = stepVal;
            }
            else if (paramTypeStr.Contains("list") || paramTypeStr.Contains("index") ||
                     IsSdkParamType(paramTypeObj, "IndexedList", "kIndexedList", "TextList", "kTextList") ||
                     paramTypeInt == 3 || paramTypeInt == 4)
            {
                item.SettingDataType = SettingItem.DataType.Enum;
                item.Value = TryGetValueDirect(param, "Value");

                // Try multiple property names for list values (SDK uses different names for IndexedList vs TextList)
                var listVals = TryGetEnumerableStrings(param, "TextListValues")
                            ?? TryGetEnumerableStrings(param, "ListValues")
                            ?? TryGetEnumerableStrings(param, "IndexedListValues")
                            ?? TryGetEnumerableStrings(param, "Values");

                if (listVals != null && listVals.Length > 0)
                {
                    item.EnumValues = listVals;
                }
                else
                {
                    // Fallback for IndexedList: generate index-based labels from Min/Max range
                    // E.g., parameter with range 0-5 → ["0", "1", "2", "3", "4", "5"]
                    var eMin = TryGetValueDirect(param, "Min") ?? TryGetValueDirect(param, "DoubleMin");
                    var eMax = TryGetValueDirect(param, "Max") ?? TryGetValueDirect(param, "DoubleMax");
                    if (eMin != null && eMax != null &&
                        double.TryParse(eMin.ToString(), out var lo) &&
                        double.TryParse(eMax.ToString(), out var hi) &&
                        hi > lo && (hi - lo) <= 256) // Safety cap: max 256 items
                    {
                        item.Min = lo;
                        item.Max = hi;
                        var count = (int)(hi - lo) + 1;
                        var generated = new string[count];
                        for (int gi = 0; gi < count; gi++)
                            generated[gi] = ((int)(lo + gi)).ToString();
                        item.EnumValues = generated;
                        Debug.WriteLine($"[SettingsEnumerator] Enum '{item.Name}': generated {count} index labels ({lo}-{hi}) — SDK list properties returned null");
                    }
                }
            }
            else
            {
                // Unknown type: read Value only (universal accessor).
                // Avoid trying DoubleValue/BooleanValue which throw based on runtime type
                // and cause TargetInvocationException flood — the #1 cause of tab-switch delay.
                item.Value = TryGetValueDirect(param, "Value");
                item.SettingDataType = InferDataType(item.Value);
                if (item.SettingDataType == SettingItem.DataType.Double || item.SettingDataType == SettingItem.DataType.Int)
                {
                    var mn = TryGetValueDirect(param, "Min");
                    var mx = TryGetValueDirect(param, "Max");
                    if (mn != null && double.TryParse(mn.ToString(), out var minV)) item.Min = minV;
                    if (mx != null && double.TryParse(mx.ToString(), out var maxV)) item.Max = maxV;
                }
            }

            return item;
        }

        /// <summary>
        /// Applies snapshot values back to the SDK Parameter objects (SdkParameterRef).
        /// Must be called before BeginWriteParameters so the product's in-memory state
        /// matches the edited snapshot; otherwise the device receives stale data and
        /// "Save to device" appears not to persist when returning to settings.
        /// Per Programmer's Guide: set parameter values on the product, then call WriteParameters.
        /// </summary>
        [DebuggerNonUserCode]
        public static void ApplySnapshotValuesToSdkParameters(DeviceSettingsSnapshot snapshot)
        {
            if (snapshot == null) return;
            int applied = 0;
            int skipped = 0;
            foreach (var cat in snapshot.Categories)
            {
                foreach (var sec in cat.Sections)
                {
                    foreach (var item in sec.Items)
                    {
                        if (item.SdkParameterRef == null || item.ReadOnly) { skipped++; continue; }
                        if (TrySetParameterValue(item.SdkParameterRef, item))
                            applied++;
                        else
                            skipped++;
                    }
                }
            }
            Debug.WriteLine($"[SettingsEnumerator] ApplySnapshotValuesToSdkParameters: applied={applied}, skipped={skipped}");
        }

        /// <summary>
        /// Sets the SDK Parameter value from a SettingItem (Value, BooleanValue, DoubleValue per type).
        /// </summary>
        [DebuggerNonUserCode]
        private static bool TrySetParameterValue(object sdkParam, SettingItem item)
        {
            var v = item.Value;
            var paramType = sdkParam.GetType();

            // Try Value (generic setter) first
            var valueProp = GetCachedProperty(paramType, "Value");
            if (valueProp != null && valueProp.CanWrite && v != null)
            {
                try
                {
                    var propType = valueProp.PropertyType;
                    if (propType == typeof(double)) { valueProp.SetValue(sdkParam, Convert.ToDouble(v)); return true; }
                    if (propType == typeof(int)) { valueProp.SetValue(sdkParam, Convert.ToInt32(v)); return true; }
                    if (propType == typeof(bool)) { valueProp.SetValue(sdkParam, Convert.ToBoolean(v)); return true; }
                    valueProp.SetValue(sdkParam, v);
                    return true;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie ? tie.InnerException : null;
                    Debug.WriteLine($"[SettingsEnumerator] TrySetParameterValue(Value) failed: {(inner?.Message ?? ex.Message)}");
                }
            }

            // Try DoubleValue for Double/Int
            if (item.SettingDataType == SettingItem.DataType.Double || item.SettingDataType == SettingItem.DataType.Int)
            {
                var dblProp = GetCachedProperty(paramType, "DoubleValue");
                if (dblProp != null && dblProp.CanWrite)
                {
                    try
                    {
                        dblProp.SetValue(sdkParam, Convert.ToDouble(v));
                        return true;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException tie ? tie.InnerException : null;
                        Debug.WriteLine($"[SettingsEnumerator] TrySetParameterValue(DoubleValue) failed: {(inner?.Message ?? ex.Message)}");
                    }
                }
            }

            // Try BooleanValue for Bool
            if (item.SettingDataType == SettingItem.DataType.Bool)
            {
                var boolProp = GetCachedProperty(paramType, "BooleanValue");
                if (boolProp != null && boolProp.CanWrite)
                {
                    try
                    {
                        boolProp.SetValue(sdkParam, Convert.ToBoolean(v));
                        return true;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException tie ? tie.InnerException : null;
                        Debug.WriteLine($"[SettingsEnumerator] TrySetParameterValue(BooleanValue) failed: {(inner?.Message ?? ex.Message)}");
                    }
                }
            }

            return false;
        }

        #endregion

        #region Reflection fallback (legacy)

        /// <summary>Method/property names we must NOT invoke—they have side effects or can crash.</summary>
        private static readonly HashSet<string> BlacklistedMemberNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "ConfigureDevice", "ResetDevice", "ReadFromDevice", "WriteToDevice",
            "BeginInitializeDevice", "EndInitializeDevice", "BeginDetectDevice", "EndDetectDevice",
            "CloseDevice", "OpenDevice", "Connect", "Disconnect", "CheckDevice",
            "Initialize", "Dispose", "GetResult", "GetProgressValue",
            "ReadParameters", "WriteParameters", "SwitchToMemory"
        };

        /// <summary>Fallback: enumerate via reflection on IProduct members (original approach).</summary>
        private static List<SettingItem> EnumerateViaReflection(IProduct product)
        {
            var items = new List<SettingItem>();
            if (product == null) return items;
            var t = product.GetType();

            // Try accessing Memories property even in fallback
            try
            {
                var memoriesProp = t.GetProperty("Memories", BindingFlags.Public | BindingFlags.Instance);
                if (memoriesProp != null)
                {
                    var memories = memoriesProp.GetValue(product);
                    if (memories != null)
                    {
                        var fromMemories = EnumerateFromIndexer(memories, "Memories");
                        if (fromMemories.Count > 0)
                        {
                            items.AddRange(fromMemories);
                            return items;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsEnumerator] Fallback Memories access: {ex.Message}");
            }

            // Original reflection on methods/properties
            foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetParameters().Length != 0) continue;
                if (method.ReturnType == typeof(void)) continue;
                if (BlacklistedMemberNames.Contains(method.Name)) continue;
                if (!LooksLikeParameterGetter(method.Name)) continue;
                try
                {
                    var result = method.Invoke(product, null);
                    if (result == null) continue;
                    AddItemsFromCollection(result, method.Name, items);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsEnumerator] Reflect method {method.Name}: {ex.Message}");
                }
            }

            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                if (BlacklistedMemberNames.Contains(prop.Name)) continue;
                if (!LooksLikeParameterGetter(prop.Name)) continue;
                try
                {
                    var result = prop.GetValue(product);
                    if (result == null) continue;
                    AddItemsFromCollection(result, prop.Name, items);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsEnumerator] Reflect prop {prop.Name}: {ex.Message}");
                }
            }

            return items;
        }

        private static bool LooksLikeParameterGetter(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            if (n.Contains("parameter") || n.Contains("block") || n.Contains("category") ||
                n.Contains("setting") || (n.Contains("config") && !n.Contains("configure")) ||
                n.Contains("memories") || n.Contains("memory"))
                return true;
            if (n.Contains("device") || n.Contains("detect") || n.Contains("init") ||
                n.Contains("reset") || n.Contains("close") || n.Contains("write") || n.Contains("read"))
                return false;
            return false;
        }

        private static void AddItemsFromCollection(object collection, string sourceName, List<SettingItem> items)
        {
            if (collection is IEnumerable enumerable)
            {
                foreach (var element in enumerable)
                {
                    if (element == null) continue;
                    var item = TryCreateSettingItemFromReflection(element, sourceName);
                    if (item != null)
                        items.Add(item);
                }
            }
        }

        private static SettingItem? TryCreateSettingItemFromReflection(object obj, string sourceName)
        {
            var type = obj.GetType();
            string? id = null, name = null, displayName = null, parameterId = null;
            string? description = null, moduleName = null, unit = null;
            object? value = null;
            double min = double.NaN, max = double.NaN, step = double.NaN;
            bool readOnly = false;
            var dataType = SettingItem.DataType.Unknown;
            var enumValues = new List<string>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                var pname = prop.Name;

                // Skip if we already know this getter throws for this type
                var failKey = (type, pname);
                if (_failedGetters.Contains(failKey)) continue;

                try
                {
                    var v = prop.GetValue(obj);
                    if (pname.Equals("Id", StringComparison.OrdinalIgnoreCase)) { id = v?.ToString(); parameterId = id; }
                    else if (pname.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = v?.ToString();
                    else if (pname.Equals("DisplayName", StringComparison.OrdinalIgnoreCase)) displayName = v?.ToString();
                    else if (pname.Equals("ParameterId", StringComparison.OrdinalIgnoreCase)) parameterId = v?.ToString();
                    else if (pname.Equals("Description", StringComparison.OrdinalIgnoreCase)) description = v?.ToString();
                    else if (pname.Equals("LongModuleName", StringComparison.OrdinalIgnoreCase) || pname.Equals("ModuleName", StringComparison.OrdinalIgnoreCase)) moduleName = v?.ToString();
                    else if (pname.Equals("ShortModuleName", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(moduleName)) moduleName = v?.ToString();
                    else if (pname.Equals("Value", StringComparison.OrdinalIgnoreCase) || pname.Equals("CurrentValue", StringComparison.OrdinalIgnoreCase)) value = v;
                    else if (pname.Equals("Unit", StringComparison.OrdinalIgnoreCase) || pname.Equals("Units", StringComparison.OrdinalIgnoreCase)) unit = v?.ToString();
                    else if (pname.Equals("Min", StringComparison.OrdinalIgnoreCase) && v != null && double.TryParse(v.ToString(), out var dmin)) min = dmin;
                    else if (pname.Equals("Max", StringComparison.OrdinalIgnoreCase) && v != null && double.TryParse(v.ToString(), out var dmax)) max = dmax;
                    else if (pname.Equals("DoubleMin", StringComparison.OrdinalIgnoreCase) && v != null && double.TryParse(v.ToString(), out var ddmin)) min = ddmin;
                    else if (pname.Equals("DoubleMax", StringComparison.OrdinalIgnoreCase) && v != null && double.TryParse(v.ToString(), out var ddmax)) max = ddmax;
                    else if (pname.Equals("Step", StringComparison.OrdinalIgnoreCase) && v != null && double.TryParse(v.ToString(), out var dstep)) step = dstep;
                    else if (pname.Equals("ReadOnly", StringComparison.OrdinalIgnoreCase) && v is bool ro) readOnly = ro;
                    else if (pname.Equals("DataType", StringComparison.OrdinalIgnoreCase) || pname.Equals("Type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (v != null) dataType = MapDataType(v);
                    }
                    else if ((pname.Equals("EnumValues", StringComparison.OrdinalIgnoreCase) ||
                              pname.Equals("ListValues", StringComparison.OrdinalIgnoreCase) ||
                              pname.Equals("TextListValues", StringComparison.OrdinalIgnoreCase)) && v is IEnumerable en)
                    {
                        enumValues = en.Cast<object>().Select(x => x?.ToString() ?? "").ToList();
                    }
                }
                catch
                {
                    _failedGetters.Add(failKey);
                }
            }

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(id)) return null;

            var item = new SettingItem
            {
                Id = id ?? name ?? "p",
                Name = name ?? id ?? "",
                DisplayName = displayName ?? name ?? id ?? "",
                ParameterId = parameterId ?? id ?? "",
                Description = description ?? "",
                ModuleName = moduleName ?? sourceName,
                MemoryName = "",
                Value = value,
                Unit = unit ?? "",
                Min = min, Max = max, Step = step,
                ReadOnly = readOnly,
                SdkParameterRef = obj
            };
            if (dataType != SettingItem.DataType.Unknown) item.SettingDataType = dataType;
            else item.SettingDataType = InferDataType(value);
            if (enumValues.Count > 0) item.EnumValues = enumValues.ToArray();
            return item;
        }

        #endregion

        #region Helpers

        /// <summary>Enumerate items from an object that has Count + Item indexer (SDK collection pattern).</summary>
        private static List<SettingItem> EnumerateFromIndexer(object collection, string contextName)
        {
            var items = new List<SettingItem>();
            var type = collection.GetType();

            // Try IEnumerable first
            if (collection is IEnumerable enumerable)
            {
                foreach (var element in enumerable)
                {
                    if (element == null) continue;
                    // Check if element is a ParameterMemory (has Parameters property)
                    var paramsProp = element.GetType().GetProperty("Parameters", BindingFlags.Public | BindingFlags.Instance);
                    if (paramsProp != null)
                    {
                        var parameters = paramsProp.GetValue(element);
                        string memName = TryGetStringProperty(element, "Name") ?? contextName;
                        if (parameters is IEnumerable paramsEnum)
                        {
                            foreach (var param in paramsEnum)
                            {
                                if (param == null) continue;
                                var item = BuildSettingItemFromSdkParameter(param, memName);
                                if (item != null) items.Add(item);
                            }
                        }
                        else if (parameters != null)
                        {
                            items.AddRange(EnumerateFromIndexer(parameters, memName));
                        }
                    }
                    else
                    {
                        // Might be a Parameter object directly
                        var item = BuildSettingItemFromSdkParameter(element, contextName);
                        if (item != null) items.Add(item);
                    }
                }
                return items;
            }

            // Try Count + Item[int] indexer
            var countProp = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            var itemProp = type.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
            if (countProp != null && itemProp != null)
            {
                var count = (int)(countProp.GetValue(collection) ?? 0);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var element = itemProp.GetValue(collection, new object[] { i });
                        if (element == null) continue;
                        var paramsProp = element.GetType().GetProperty("Parameters", BindingFlags.Public | BindingFlags.Instance);
                        if (paramsProp != null)
                        {
                            var parameters = paramsProp.GetValue(element);
                            string memName = TryGetStringProperty(element, "Name") ?? $"Memory {i}";
                            if (parameters != null)
                                items.AddRange(EnumerateFromIndexer(parameters, memName));
                        }
                        else
                        {
                            var item = BuildSettingItemFromSdkParameter(element, contextName);
                            if (item != null) items.Add(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SettingsEnumerator] Indexer[{i}]: {ex.Message}");
                    }
                }
            }

            return items;
        }

        // Cache PropertyInfo per (Type, PropertyName) to avoid repeated reflection lookups
        // and eliminate thousands of TargetInvocationException from missing properties.
        private static readonly Dictionary<(Type, string), PropertyInfo?> _propertyCache = new();

        // Track properties whose getters throw, so we don't keep invoking them.
        private static readonly HashSet<(Type, string)> _failedGetters = new();

        [DebuggerNonUserCode]
        private static PropertyInfo? GetCachedProperty(Type type, string propName)
        {
            var key = (type, propName);
            if (!_propertyCache.TryGetValue(key, out var prop))
            {
                prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                _propertyCache[key] = prop;
            }
            return prop;
        }

        [DebuggerNonUserCode]
        private static string? TryGetStringProperty(object obj, string propName)
        {
            var type = obj.GetType();
            var key = (type, propName);

            // Skip if we already know this getter throws
            if (_failedGetters.Contains(key)) return null;

            var prop = GetCachedProperty(type, propName);
            if (prop == null) return null;
            try
            {
                return prop.GetValue(obj)?.ToString();
            }
            catch
            {
                _failedGetters.Add(key);
                return null;
            }
        }

        [DebuggerNonUserCode]
        private static object? TryGetPropertyValue(object obj, string propName)
        {
            var type = obj.GetType();
            var key = (type, propName);

            // Skip if we already know this getter throws
            if (_failedGetters.Contains(key)) return null;

            var prop = GetCachedProperty(type, propName);
            if (prop == null) return null;
            try
            {
                return prop.GetValue(obj);
            }
            catch
            {
                _failedGetters.Add(key);
                return null;
            }
        }

        /// <summary>
        /// Gets the current value from an SDK Parameter object (for verification after read-back).
        /// Tries Value, DoubleValue, BooleanValue. Returns null if ref is null or read fails.
        /// </summary>
        [DebuggerNonUserCode]
        internal static object? GetCurrentParameterValue(object? sdkParam)
        {
            if (sdkParam == null) return null;
            var v = TryGetValueDirect(sdkParam, "Value");
            if (v != null) return v;
            var d = TryGetValueDirect(sdkParam, "DoubleValue");
            if (d != null) return d;
            var b = TryGetValueDirect(sdkParam, "BooleanValue");
            return b ?? null;
        }

        /// <summary>
        /// Verifies that a subset of snapshot parameters match the current SDK state (after read-back).
        /// Samples up to maxItemsToCheck items that have SdkParameterRef. Returns (true, null) if all match.
        /// </summary>
        [DebuggerNonUserCode]
        internal static (bool Verified, string? FailureMessage) VerifySnapshotAfterReadBack(DeviceSettingsSnapshot snapshot, int maxItemsToCheck = 50)
        {
            if (snapshot == null) return (true, null);
            const double doubleTolerance = 1e-9;
            int checkedCount = 0;
            foreach (var cat in snapshot.Categories)
            {
                foreach (var sec in cat.Sections)
                {
                    foreach (var item in sec.Items)
                    {
                        if (item.SdkParameterRef == null || item.ReadOnly) continue;
                        if (checkedCount >= maxItemsToCheck) return (true, null);
                        var current = GetCurrentParameterValue(item.SdkParameterRef);
                        var expected = item.Value;
                        bool match = false;
                        if (expected == null && current == null) match = true;
                        else if (expected != null && current != null)
                        {
                            if (expected is double ed && current is double cd)
                                match = Math.Abs(ed - cd) <= doubleTolerance;
                            else
                                match = Equals(expected, current);
                        }
                        if (!match)
                        {
                            Debug.WriteLine($"[SettingsEnumerator] VerifySnapshotAfterReadBack: mismatch Id={item.Id} expected={expected} current={current}");
                            return (false, $"Verification failed: parameter '{item.Id}' did not match after read-back. Re-read or reconnect.");
                        }
                        checkedCount++;
                    }
                }
            }
            return (true, null);
        }

        /// <summary>
        /// Reads a property value WITHOUT using _failedGetters cache.
        /// Used for value-specific properties (BooleanValue, DoubleValue, Value, Min, Max, etc.)
        /// that exist on the .NET Type but throw dynamically based on the SDK parameter's runtime data type.
        ///
        /// Using _failedGetters for these causes cross-contamination: marking BooleanValue as "failed"
        /// after a Double parameter incorrectly blocks it for all subsequent Boolean parameters
        /// (since all SDK Parameters share the same .NET Type / RCW).
        /// </summary>
        [DebuggerNonUserCode]
        private static object? TryGetValueDirect(object obj, string propName)
        {
            var prop = GetCachedProperty(obj.GetType(), propName);
            if (prop == null) return null;
            try { return prop.GetValue(obj); }
            catch { return null; }
        }

        /// <summary>
        /// Reads a list/enum property using TryGetValueDirect (bypasses _failedGetters).
        /// TextListValues/ListValues exist on the SDK Parameter Type but throw dynamically
        /// for non-list parameters. Using _failedGetters would block all subsequent list reads.
        /// </summary>
        [DebuggerNonUserCode]
        private static string[]? TryGetEnumerableStrings(object obj, string propName)
        {
            try
            {
                var val = TryGetValueDirect(obj, propName);
                if (val == null) return null;
                if (val is IEnumerable<string> strEnum)
                    return strEnum.ToArray();
                if (val is IEnumerable en)
                    return en.Cast<object>().Select(x => x?.ToString() ?? "").ToArray();
                return null;
            }
            catch { return null; }
        }

        private static bool IsSdkParamType(object? typeObj, params string[] names)
        {
            if (typeObj == null) return false;
            var s = typeObj.ToString() ?? "";
            return names.Any(n => s.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string SanitizeId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "General";
            return new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        }

        private static SettingItem.DataType MapDataType(object v)
        {
            var s = v.ToString()?.ToLowerInvariant() ?? "";
            if (s.Contains("bool")) return SettingItem.DataType.Bool;
            if (s.Contains("int") || s.Contains("integer")) return SettingItem.DataType.Int;
            if (s.Contains("double") || s.Contains("float")) return SettingItem.DataType.Double;
            if (s.Contains("enum") || s.Contains("list") || s.Contains("index")) return SettingItem.DataType.Enum;
            if (s.Contains("string")) return SettingItem.DataType.String;
            return SettingItem.DataType.Unknown;
        }

        private static SettingItem.DataType InferDataType(object? value)
        {
            if (value == null) return SettingItem.DataType.String;
            if (value is bool) return SettingItem.DataType.Bool;
            if (value is int or long or short or byte) return SettingItem.DataType.Int;
            if (value is double or float or decimal) return SettingItem.DataType.Double;
            if (value is string) return SettingItem.DataType.String;
            return SettingItem.DataType.Unknown;
        }

        #endregion
    }
}
