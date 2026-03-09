using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDLib;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Manages Sound Designer SDK product libraries.
    /// Enumerates available *.library files from embedded assets,
    /// loads them offline (no device required), and builds parameter
    /// metadata (tabs, controls, ranges) from the library schema.
    /// </summary>
    public sealed class LibraryService : IDisposable
    {
        private IProductManager? _productManager;
        private ILibrary? _loadedLibrary;
        private IProduct? _offlineProduct;
        private string? _loadedLibraryPath;

        /// <summary>True after a library has been loaded via LoadLibraryAsync.</summary>
        public bool IsLibraryLoaded => _loadedLibrary != null && _offlineProduct != null;

        /// <summary>File name (not full path) of the currently loaded library.</summary>
        public string? LoadedLibraryName => _loadedLibraryPath != null ? Path.GetFileName(_loadedLibraryPath) : null;

        /// <summary>Product description from the loaded library (e.g. "Ezairo 7160 SL 16 Channels").</summary>
        public string? ProductDescription { get; private set; }

        /// <summary>The offline IProduct instance (for parameter enumeration without device).</summary>
        public IProduct? OfflineProduct => _offlineProduct;

        /// <summary>The IProductManager instance (shared with DeviceService for device operations).</summary>
        public IProductManager? ProductManager => _productManager;

        // =========================================================================
        // Enumerate available libraries
        // =========================================================================

        /// <summary>
        /// Lists all available *.library files from the embedded assets (products folder).
        /// Returns (fileName, fullPath) pairs sorted by name.
        /// </summary>
        public static List<LibraryInfo> EnumerateLibraries()
        {
            var files = SdkConfiguration.EnumerateLibraryFiles();
            return files
                .Select(f => new LibraryInfo
                {
                    FileName = Path.GetFileNameWithoutExtension(f),
                    FullPath = f
                })
                .OrderBy(l => l.FileName)
                .ToList();
        }

        // =========================================================================
        // Load library (offline — no device required)
        // =========================================================================

        /// <summary>
        /// Loads a *.library file and creates an offline IProduct.
        /// After this, parameter metadata (structure, names, types, ranges, modules)
        /// is available via BuildOfflineSnapshot without any device connected.
        /// Threading: Must be called on the thread that owns the SDK objects (usually UI thread).
        /// </summary>
        public Task LoadLibraryAsync(string libraryPath, CancellationToken ct = default)
        {
            if (!File.Exists(libraryPath))
                throw new FileNotFoundException($"Library file not found: {libraryPath}");

            // Unload previous
            UnloadLibrary();

            // Execute directly on calling thread (caller must ensure correct thread/context)
            try
            {
                ct.ThrowIfCancellationRequested();

                // Ensure SDK environment is set up (config, PATH, DllDirectory)
                SdkConfiguration.SetupEnvironment();

                // Get or create ProductManager
                _productManager = SDLibMain.GetProductManagerInstance();
                if (_productManager == null)
                    throw new InvalidOperationException("Failed to get ProductManager instance");

                Debug.WriteLine($"[LibraryService] SDK Version: {_productManager.Version}");
                Debug.WriteLine($"[LibraryService] Loading library: {libraryPath}");

                _loadedLibrary = _productManager.LoadLibraryFromFile(libraryPath);
                if (_loadedLibrary == null)
                    throw new InvalidOperationException($"Failed to load library: {libraryPath}");

                _loadedLibraryPath = libraryPath;

                // Create product from first (or only) product definition
                if (_loadedLibrary.Products != null && _loadedLibrary.Products.Count > 0)
                {
                    foreach (IProductDefinition pd in _loadedLibrary.Products)
                    {
                        Debug.WriteLine($"[LibraryService] Creating offline product: {pd.Description}");
                        try
                        {
                            _offlineProduct = pd.CreateProduct();
                        }
                        catch (System.Reflection.TargetInvocationException tex)
                        {
                            var inner = tex.InnerException ?? tex;
                            Debug.WriteLine($"[LibraryService] CreateProduct TargetInvocationException inner: {inner.Message}");
                            throw new InvalidOperationException($"CreateProduct failed: {inner.Message}", inner);
                        }
                        ProductDescription = pd.Description;
                        break;
                    }
                }

                if (_offlineProduct == null)
                    throw new InvalidOperationException("No product definitions found in library");

                Debug.WriteLine($"[LibraryService] Library loaded: {Path.GetFileName(libraryPath)} — {ProductDescription}");
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Loads a library by file name (searched in products directory).
        /// Convenience overload for UI selection.
        /// </summary>
        public Task LoadLibraryByNameAsync(string fileName, CancellationToken ct = default)
        {
            var fullPath = SdkConfiguration.GetLibraryPath(fileName);
            return LoadLibraryAsync(fullPath, ct);
        }

        // =========================================================================
        // Build offline snapshot (parameter metadata from library, no device values)
        // =========================================================================

        /// <summary>
        /// Builds a DeviceSettingsSnapshot from the loaded library's parameter metadata.
        /// Parameters have structure (name, type, module, ranges) but NO device values.
        /// Values default to the library's initial/default values.
        /// Requires IsLibraryLoaded == true.
        /// </summary>
        public DeviceSettingsSnapshot BuildOfflineSnapshot(DeviceSide side)
        {
            if (_offlineProduct == null)
                throw new InvalidOperationException("No library loaded. Call LoadLibraryAsync first.");

            // Reuse the existing enumerator — it reads metadata from the Product object.
            // Without ReadParameters, values will be library defaults (not device-specific).
            return SoundDesignerSettingsEnumerator.BuildFullSnapshot(_offlineProduct, side);
        }

        /// <summary>
        /// Builds a snapshot for a SINGLE memory (0-7) — ~595 params instead of 4760.
        /// This is the preferred method for the E7111V2 fitting workflow.
        /// </summary>
        public DeviceSettingsSnapshot BuildSnapshotForMemory(int memoryIndex, DeviceSide side)
        {
            if (_offlineProduct == null)
                throw new InvalidOperationException("No library loaded. Call LoadLibraryAsync first.");

            return SoundDesignerSettingsEnumerator.BuildSnapshotForMemory(_offlineProduct, memoryIndex, side);
        }

        /// <summary>
        /// Builds a snapshot for system parameters (global, not per-memory).
        /// </summary>
        public DeviceSettingsSnapshot BuildSystemSnapshot(DeviceSide side)
        {
            if (_offlineProduct == null)
                throw new InvalidOperationException("No library loaded. Call LoadLibraryAsync first.");

            return SoundDesignerSettingsEnumerator.BuildSystemSnapshot(_offlineProduct, side);
        }

        // =========================================================================
        // Memory access (SDK ParameterMemory objects)
        // =========================================================================

        /// <summary>
        /// Gets a specific ParameterMemory by index (0-7) from the offline Product.
        /// Returns the raw SDK memory object for direct parameter manipulation.
        /// </summary>
        public object? GetMemory(int memoryIndex)
        {
            if (_offlineProduct == null) return null;

            try
            {
                var memoriesProp = _offlineProduct.GetType().GetProperty("Memories",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (memoriesProp == null) return null;

                var memories = memoriesProp.GetValue(_offlineProduct);
                if (memories == null) return null;

                // Try Item[int] indexer
                var itemProp = memories.GetType().GetProperty("Item",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (itemProp != null)
                {
                    return itemProp.GetValue(memories, new object[] { memoryIndex });
                }

                // Fallback: IEnumerable skip
                if (memories is System.Collections.IEnumerable enumerable)
                {
                    int idx = 0;
                    foreach (var mem in enumerable)
                    {
                        if (idx == memoryIndex) return mem;
                        idx++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LibraryService] GetMemory({memoryIndex}) error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the SystemMemory from the offline Product (global parameters).
        /// </summary>
        public object? GetSystemMemory()
        {
            if (_offlineProduct == null) return null;

            try
            {
                var sysProp = _offlineProduct.GetType().GetProperty("SystemMemory",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                return sysProp?.GetValue(_offlineProduct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LibraryService] GetSystemMemory() error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies parameter values from a ParamFile to the offline IProduct for a specific memory.
        /// Uses the SDK pattern from SDManager.SetValueFromFile():
        ///   memory.Parameters.GetById(name) → set value by type.
        /// Must be called on the STA thread.
        /// </summary>
        [DebuggerNonUserCode]
        public int ApplyParamToProduct(ParamFile paramFile, int memoryIndex)
        {
            if (_offlineProduct == null)
                throw new InvalidOperationException("No library loaded.");
            if (paramFile == null)
                throw new ArgumentNullException(nameof(paramFile));
            if (memoryIndex < 0 || memoryIndex >= (paramFile.Memory?.Count ?? 0))
            {
                Debug.WriteLine($"[LibraryService] ApplyParam: memoryIndex {memoryIndex} out of range (memories={paramFile.Memory?.Count ?? 0})");
                return 0;
            }

            var memory = GetMemory(memoryIndex);
            if (memory == null)
            {
                Debug.WriteLine($"[LibraryService] ApplyParam: could not get memory {memoryIndex} from product");
                return 0;
            }

            var paramData = paramFile.Memory[memoryIndex];
            int applied = 0;
            int failed = 0;

            // Get the Parameters collection from the memory
            var paramsProp = memory.GetType().GetProperty("Parameters",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (paramsProp == null)
            {
                Debug.WriteLine($"[LibraryService] ApplyParam: memory has no Parameters property");
                return 0;
            }

            var parameters = paramsProp.GetValue(memory);
            if (parameters == null)
            {
                Debug.WriteLine($"[LibraryService] ApplyParam: Parameters is null");
                return 0;
            }

            foreach (var nv in paramData.Param)
            {
                try
                {
                    // Try GetById method on ParameterList (SDK pattern)
                    var sdkParam = FindParameterByName(parameters, nv.Name);
                    if (sdkParam == null)
                    {
                        failed++;
                        continue;
                    }

                    SetParameterValue(sdkParam, nv.Value);
                    applied++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LibraryService] ApplyParam: error setting {nv.Name}={nv.Value}: {ex.Message}");
                    failed++;
                }
            }

            Debug.WriteLine($"[LibraryService] ApplyParam memory {memoryIndex}: applied={applied}, failed={failed}, total={paramData.Param.Count}");
            return applied;
        }

        /// <summary>
        /// Applies system parameters from a ParamFile to the offline IProduct.
        /// </summary>
        [DebuggerNonUserCode]
        public int ApplySystemParams(ParamFile paramFile)
        {
            if (_offlineProduct == null || paramFile?.System == null) return 0;

            var systemMemory = GetSystemMemory();
            if (systemMemory == null) return 0;

            var paramsProp = systemMemory.GetType().GetProperty("Parameters",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (paramsProp == null) return 0;

            var parameters = paramsProp.GetValue(systemMemory);
            if (parameters == null) return 0;

            int applied = 0;
            foreach (var nv in paramFile.System.Param)
            {
                try
                {
                    var sdkParam = FindParameterByName(parameters, nv.Name);
                    if (sdkParam != null)
                    {
                        SetParameterValue(sdkParam, nv.Value);
                        applied++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LibraryService] ApplySystemParam: error setting {nv.Name}={nv.Value}: {ex.Message}");
                }
            }

            Debug.WriteLine($"[LibraryService] ApplySystemParams: applied={applied}/{paramFile.System.Param.Count}");
            return applied;
        }

        // =========================================================================
        // Apply param to any IProduct (for Configure Device — session product, not offline)
        // =========================================================================

        /// <summary>
        /// Applies parameter values from a ParamFile to any IProduct (e.g. session product) for a specific memory.
        /// Used by Configure Device flow to load .param template before calling ConfigureDevice.
        /// Must be called on the STA thread. Does not dispose or modify ProductManager.
        /// </summary>
        [DebuggerNonUserCode]
        public static int ApplyParamToProduct(SDLib.IProduct product, ParamFile paramFile, int memoryIndex)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));
            if (paramFile == null)
                throw new ArgumentNullException(nameof(paramFile));
            if (memoryIndex < 0 || memoryIndex >= (paramFile.Memory?.Count ?? 0))
            {
                Debug.WriteLine($"[LibraryService] ApplyParamToProduct: memoryIndex {memoryIndex} out of range");
                return 0;
            }

            var memory = GetMemoryFromProduct(product, memoryIndex);
            if (memory == null)
            {
                Debug.WriteLine($"[LibraryService] ApplyParamToProduct: could not get memory {memoryIndex} from product");
                return 0;
            }

            var paramsProp = memory.GetType().GetProperty("Parameters",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (paramsProp == null) return 0;

            var parameters = paramsProp.GetValue(memory);
            if (parameters == null) return 0;

            var paramData = paramFile.Memory[memoryIndex];
            int applied = 0;
            foreach (var nv in paramData.Param)
            {
                try
                {
                    var sdkParam = FindParameterByName(parameters, nv.Name);
                    if (sdkParam != null)
                    {
                        SetParameterValue(sdkParam, nv.Value);
                        applied++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LibraryService] ApplyParamToProduct: {nv.Name}={nv.Value}: {ex.Message}");
                }
            }

            if (applied > 0)
                Debug.WriteLine($"[LibraryService] ApplyParamToProduct memory {memoryIndex}: applied={applied}/{paramData.Param.Count}");
            return applied;
        }

        /// <summary>Gets a specific ParameterMemory by index (0-7) from any IProduct.</summary>
        private static object? GetMemoryFromProduct(SDLib.IProduct product, int memoryIndex)
        {
            try
            {
                var memoriesProp = product.GetType().GetProperty("Memories",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (memoriesProp?.GetValue(product) is not System.Collections.IEnumerable memories)
                    return null;

                int idx = 0;
                foreach (var mem in memories)
                {
                    if (idx == memoryIndex) return mem;
                    idx++;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LibraryService] GetMemoryFromProduct({memoryIndex}): {ex.Message}");
            }

            return null;
        }

        // =========================================================================
        // Parameter lookup helpers (SDK pattern)
        // =========================================================================

        /// <summary>
        /// Finds a Parameter in a ParameterList by name.
        /// Tries GetById method first (SDK ParameterList.GetById(string)),
        /// then falls back to iterating and matching by Name.
        /// </summary>
        private static object? FindParameterByName(object parameterList, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Try GetById method (SDK ParameterList pattern)
            return FindParameterByNameImpl(parameterList, name);
        }

        [DebuggerNonUserCode]
        private static object? FindParameterByNameImpl(object parameterList, string name)
        {
            var getByIdMethod = parameterList.GetType().GetMethod("GetById",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(string) }, null);

            if (getByIdMethod != null)
            {
                try
                {
                    return getByIdMethod.Invoke(parameterList, new object[] { name });
                }
                catch { /* fall through to iteration */ }
            }

            // Fallback: iterate and match by Name property
            if (parameterList is System.Collections.IEnumerable enumerable)
            {
                foreach (var param in enumerable)
                {
                    if (param == null) continue;
                    var nameProp = param.GetType().GetProperty("Name",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (nameProp != null)
                    {
                        var paramName = nameProp.GetValue(param)?.ToString();
                        if (string.Equals(paramName, name, StringComparison.OrdinalIgnoreCase))
                            return param;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sets a Parameter's value from a string, matching the SDK sample SetValueFromFile pattern.
        /// Tries Value setter, then DoubleValue, then BooleanValue based on the parameter type.
        /// </summary>
        [DebuggerNonUserCode]
        private static void SetParameterValue(object sdkParam, string valueStr)
        {
            var paramType = sdkParam.GetType();

            // Try direct Value property (works for most types)
            var valueProp = paramType.GetProperty("Value",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (valueProp != null && valueProp.CanWrite)
            {
                try
                {
                    // Try setting as the property's type
                    var propType = valueProp.PropertyType;
                    if (propType == typeof(double) && double.TryParse(valueStr, out var dv))
                    {
                        valueProp.SetValue(sdkParam, dv);
                        return;
                    }
                    if (propType == typeof(int) && int.TryParse(valueStr, out var iv))
                    {
                        valueProp.SetValue(sdkParam, iv);
                        return;
                    }
                    if (propType == typeof(bool))
                    {
                        valueProp.SetValue(sdkParam, valueStr == "1" || valueStr.Equals("true", StringComparison.OrdinalIgnoreCase));
                        return;
                    }
                    // Generic: try as object
                    if (int.TryParse(valueStr, out var intVal))
                        valueProp.SetValue(sdkParam, intVal);
                    else if (double.TryParse(valueStr, out var dblVal))
                        valueProp.SetValue(sdkParam, dblVal);
                    else
                        valueProp.SetValue(sdkParam, valueStr);
                    return;
                }
                catch { /* try alternatives */ }
            }

            // Try DoubleValue
            var dblProp = paramType.GetProperty("DoubleValue",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (dblProp != null && dblProp.CanWrite && double.TryParse(valueStr, out var dVal))
            {
                try { dblProp.SetValue(sdkParam, dVal); return; }
                catch { /* fall through */ }
            }

            // Try BooleanValue
            var boolProp = paramType.GetProperty("BooleanValue",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (boolProp != null && boolProp.CanWrite)
            {
                try
                {
                    boolProp.SetValue(sdkParam, valueStr == "1" || valueStr.Equals("true", StringComparison.OrdinalIgnoreCase));
                    return;
                }
                catch { /* fall through */ }
            }

            Debug.WriteLine($"[LibraryService] SetParameterValue: could not set value for parameter (type mismatch)");
        }

        // =========================================================================
        // FirmwareId → library mapping
        // =========================================================================

        /// <summary>
        /// Finds the best matching library file for a given firmware ID.
        /// Returns null if no match found.
        /// Strategy:
        ///   1. Exact match: FirmwareId == library file name stem (e.g. "E7111V2" → "E7111V2.library")
        ///   2. Prefix match: FirmwareId starts with library stem (e.g. "E7111V2" matches "E7111*.library")
        ///   3. If only one library exists, return it as the default.
        /// </summary>
        public static LibraryInfo? FindLibraryForFirmware(string firmwareId)
        {
            if (string.IsNullOrWhiteSpace(firmwareId)) return null;
            var libraries = EnumerateLibraries();
            if (libraries.Count == 0) return null;

            // Exact match
            var exact = libraries.FirstOrDefault(l =>
                string.Equals(l.FileName, firmwareId, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Prefix match (firmware starts with library name root)
            var prefix = libraries.FirstOrDefault(l =>
                firmwareId.StartsWith(l.FileName, StringComparison.OrdinalIgnoreCase));
            if (prefix != null) return prefix;

            // Reverse prefix (library name starts with firmware root)
            var revPrefix = libraries.FirstOrDefault(l =>
                l.FileName.StartsWith(firmwareId.Substring(0, Math.Min(4, firmwareId.Length)), StringComparison.OrdinalIgnoreCase));
            if (revPrefix != null) return revPrefix;

            // Single library available — use as default
            if (libraries.Count == 1)
            {
                Debug.WriteLine($"[LibraryService] No match for firmware '{firmwareId}', using only library: {libraries[0].FileName}");
                return libraries[0];
            }

            return null;
        }

        // =========================================================================
        // Cleanup
        // =========================================================================

        public void UnloadLibrary()
        {
            try { if (_offlineProduct is IDisposable d) d.Dispose(); } catch { }
            try { if (_loadedLibrary is IDisposable d) d.Dispose(); } catch { }
            _offlineProduct = null;
            _loadedLibrary = null;
            _loadedLibraryPath = null;
            ProductDescription = null;
        }

        public void Dispose()
        {
            UnloadLibrary();
            _productManager = null;
        }
    }

    /// <summary>Describes an available *.library file.</summary>
    public class LibraryInfo
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public override string ToString() => FileName;
    }
}
