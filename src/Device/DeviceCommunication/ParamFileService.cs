using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    // =========================================================================
    // JSON model classes matching *.param file structure (E7111V2.param etc.)
    // Based on SDK sample Param.cs from sdmaui/helper/Param.cs
    // =========================================================================

    /// <summary>
    /// Root model for a .param file. Contains library metadata,
    /// 8 memory profiles, system parameters, transducer info, and voice alerts.
    /// </summary>
    public class ParamFile
    {
        [JsonPropertyName("library")]
        public string Library { get; set; } = "";

        [JsonPropertyName("libraryid")]
        public int LibraryId { get; set; }

        [JsonPropertyName("product")]
        public string Product { get; set; } = "";

        [JsonPropertyName("librarysignature")]
        public string LibrarySignature { get; set; } = "";

        [JsonPropertyName("memory")]
        public List<ParamMemory> Memory { get; set; } = new();

        [JsonPropertyName("system")]
        public ParamMemory? System { get; set; }

        [JsonPropertyName("transducer")]
        public List<ParamTransducer>? Transducer { get; set; }

        [JsonPropertyName("scratchmemory")]
        public ParamScratchMemory? ScratchMemory { get; set; }

        [JsonPropertyName("voicealerts")]
        public List<object>? VoiceAlerts { get; set; }
    }

    /// <summary>
    /// One memory profile (0-7) or system memory, containing named parameter values.
    /// </summary>
    public class ParamMemory
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("param")]
        public List<ParamNameValue> Param { get; set; } = new();
    }

    /// <summary>
    /// A single parameter name/value pair as stored in the .param JSON.
    /// Values are always strings (parsed by type when applied to the SDK product).
    /// </summary>
    public class ParamNameValue
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    /// <summary>Transducer definition in the .param file.</summary>
    public class ParamTransducer
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("port")]
        public string Port { get; set; } = "";
    }

    /// <summary>Scratch memory section in the .param file.</summary>
    public class ParamScratchMemory
    {
        [JsonPropertyName("csvalues")]
        public string CsValues { get; set; } = "";
    }

    // =========================================================================
    // ParamFileService: load, save, create .param files
    // =========================================================================

    /// <summary>
    /// Service for loading, saving, and creating .param files.
    /// .param files store parameter presets/snapshots for offline use.
    /// Based on the SDK sample pattern (sdmaui/helper/Param.cs + SDManager.cs).
    /// </summary>
    public static class ParamFileService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Loads a .param file from disk (synchronous). Use when calling from STA thread to avoid deadlock.
        /// Returns null if the file doesn't exist or can't be parsed.
        /// </summary>
        public static ParamFile? Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Debug.WriteLine($"[ParamFileService] File not found: {path}");
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var paramFile = JsonSerializer.Deserialize<ParamFile>(json, _jsonOptions);

                if (paramFile != null)
                {
                    Debug.WriteLine($"[ParamFileService] Loaded: {Path.GetFileName(path)} — " +
                                    $"library={paramFile.Library}, product={paramFile.Product}, " +
                                    $"memories={paramFile.Memory?.Count ?? 0}");
                }

                return paramFile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParamFileService] Error loading {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads a .param file from disk (async).
        /// Returns null if the file doesn't exist or can't be parsed.
        /// </summary>
        public static async Task<ParamFile?> LoadAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Debug.WriteLine($"[ParamFileService] File not found: {path}");
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var paramFile = JsonSerializer.Deserialize<ParamFile>(json, _jsonOptions);

                if (paramFile != null)
                {
                    Debug.WriteLine($"[ParamFileService] Loaded: {Path.GetFileName(path)} — " +
                                    $"library={paramFile.Library}, product={paramFile.Product}, " +
                                    $"memories={paramFile.Memory?.Count ?? 0}");
                }

                return paramFile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParamFileService] Error loading {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves a ParamFile to disk as JSON.
        /// </summary>
        public static async Task SaveAsync(string path, ParamFile paramFile, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty", nameof(path));
            if (paramFile == null)
                throw new ArgumentNullException(nameof(paramFile));

            var json = JsonSerializer.Serialize(paramFile, _jsonOptions);
            await File.WriteAllTextAsync(path, json, ct);

            Debug.WriteLine($"[ParamFileService] Saved: {Path.GetFileName(path)} — " +
                            $"library={paramFile.Library}, memories={paramFile.Memory?.Count ?? 0}");
        }

        /// <summary>
        /// Creates a new ParamFile from an IProduct's current parameter state.
        /// Reads all 8 memories + system memory and builds the JSON structure.
        /// Must be called on the STA thread where Product was created.
        /// </summary>
        public static ParamFile CreateFromProduct(SDLib.IProduct product, string libraryName)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            var paramFile = new ParamFile
            {
                Library = libraryName,
                Product = product.ToString() ?? libraryName,
                Memory = new List<ParamMemory>()
            };

            // Extract parameters from each memory (0-7)
            try
            {
                var memoriesProp = product.GetType().GetProperty("Memories",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (memoriesProp?.GetValue(product) is System.Collections.IEnumerable memories)
                {
                    int memIndex = 0;
                    foreach (var memory in memories)
                    {
                        if (memory == null) { memIndex++; continue; }

                        var pm = new ParamMemory { Id = memIndex, Param = new List<ParamNameValue>() };
                        ExtractParametersFromMemory(memory, pm.Param);
                        paramFile.Memory.Add(pm);
                        memIndex++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParamFileService] Error extracting memories: {ex.Message}");
            }

            // Extract system parameters
            try
            {
                var sysProp = product.GetType().GetProperty("SystemMemory",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (sysProp?.GetValue(product) is object systemMemory)
                {
                    paramFile.System = new ParamMemory { Id = -1, Param = new List<ParamNameValue>() };
                    ExtractParametersFromMemory(systemMemory, paramFile.System.Param);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParamFileService] Error extracting system memory: {ex.Message}");
            }

            Debug.WriteLine($"[ParamFileService] Created from product: {paramFile.Memory.Count} memories, " +
                            $"system={paramFile.System?.Param?.Count ?? 0} params");

            return paramFile;
        }

        /// <summary>
        /// Finds a .param file that matches the given library name in the products directory.
        /// E.g., for "E7111V2.library" looks for "E7111V2.param".
        /// </summary>
        public static string? FindParamForLibrary(string libraryFileName)
        {
            if (string.IsNullOrWhiteSpace(libraryFileName)) return null;

            // Strip extension from library name: "E7111V2.library" -> "E7111V2"
            var stem = Path.GetFileNameWithoutExtension(libraryFileName);
            return FindParamForLibraryStem(stem, searchEmbeddedProductsDirectory: true);
        }

        /// <summary>
        /// Resolves a .param for a library given its full path: checks the library's directory first (external folders),
        /// then embedded <c>Assets/SoundDesigner/products</c> (same stem rules as <see cref="FindParamForLibrary"/>).
        /// </summary>
        public static string? FindParamForLibraryPath(string libraryFullPath)
        {
            if (string.IsNullOrWhiteSpace(libraryFullPath) || !File.Exists(libraryFullPath))
                return null;

            var stem = Path.GetFileNameWithoutExtension(libraryFullPath);
            var libDir = Path.GetDirectoryName(libraryFullPath);
            if (!string.IsNullOrEmpty(libDir))
            {
                var sibling = Path.Combine(libDir, stem + ".param");
                if (File.Exists(sibling))
                {
                    Debug.WriteLine($"[ParamFileService] Found .param next to library: {sibling}");
                    return sibling;
                }

                try
                {
                    var candidates = Directory.GetFiles(libDir, "*.param")
                        .Where(f => Path.GetFileNameWithoutExtension(f)
                            .StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f.Length)
                        .ToArray();
                    if (candidates.Length > 0)
                    {
                        Debug.WriteLine($"[ParamFileService] Found .param (prefix) next to library: {candidates[0]}");
                        return candidates[0];
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ParamFileService] Error searching sibling .param: {ex.Message}");
                }
            }

            return FindParamForLibraryStem(stem, searchEmbeddedProductsDirectory: true);
        }

        private static string? FindParamForLibraryStem(string stem, bool searchEmbeddedProductsDirectory)
        {
            if (string.IsNullOrWhiteSpace(stem)) return null;

            if (searchEmbeddedProductsDirectory)
            {
                var productsPath = SdkConfiguration.GetProductsPath();
                if (Directory.Exists(productsPath))
                {
                    var paramPath = Path.Combine(productsPath, stem + ".param");
                    if (File.Exists(paramPath))
                    {
                        Debug.WriteLine($"[ParamFileService] Found matching .param: {paramPath}");
                        return paramPath;
                    }

                    try
                    {
                        var candidates = Directory.GetFiles(productsPath, "*.param")
                            .Where(f => Path.GetFileNameWithoutExtension(f)
                                .StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => f.Length)
                            .ToArray();

                        if (candidates.Length > 0)
                        {
                            Debug.WriteLine($"[ParamFileService] Found matching .param (prefix): {candidates[0]}");
                            return candidates[0];
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ParamFileService] Error searching for .param files: {ex.Message}");
                    }
                }
            }

            Debug.WriteLine($"[ParamFileService] No .param file found for stem: {stem}");
            return null;
        }

        /// <summary>
        /// Enumerates all .param files in the products directory.
        /// </summary>
        public static List<string> EnumerateParamFiles()
        {
            var productsPath = SdkConfiguration.GetProductsPath();
            if (!Directory.Exists(productsPath)) return new List<string>();

            try
            {
                return Directory.GetFiles(productsPath, "*.param")
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParamFileService] Error enumerating .param files: {ex.Message}");
                return new List<string>();
            }
        }

        // =========================================================================
        // Internal helpers
        // =========================================================================

        /// <summary>
        /// Extracts name/value pairs from a ParameterMemory's Parameters collection.
        /// Uses reflection to access the SDK's Parameter objects.
        /// </summary>
        private static void ExtractParametersFromMemory(object memory, List<ParamNameValue> target)
        {
            var paramsProp = memory.GetType().GetProperty("Parameters",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (paramsProp?.GetValue(memory) is not System.Collections.IEnumerable parameters) return;

            foreach (var param in parameters)
            {
                if (param == null) continue;
                try
                {
                    var name = GetStringProperty(param, "Name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var value = GetParameterValueAsString(param);
                    target.Add(new ParamNameValue { Name = name, Value = value });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ParamFileService] Error extracting param: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reads the current value of an SDK Parameter object as a string.
        /// Tries DoubleValue, BooleanValue, Value in order.
        /// </summary>
        private static string GetParameterValueAsString(object param)
        {
            var type = param.GetType();

            // Try Value property first (most universal)
            var valueProp = type.GetProperty("Value",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (valueProp != null)
            {
                try
                {
                    var val = valueProp.GetValue(param);
                    if (val != null) return val.ToString() ?? "0";
                }
                catch { /* fall through */ }
            }

            // Try DoubleValue
            var dblProp = type.GetProperty("DoubleValue",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (dblProp != null)
            {
                try
                {
                    var val = dblProp.GetValue(param);
                    if (val != null) return val.ToString() ?? "0";
                }
                catch { /* fall through */ }
            }

            // Try BooleanValue
            var boolProp = type.GetProperty("BooleanValue",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (boolProp != null)
            {
                try
                {
                    var val = boolProp.GetValue(param);
                    if (val is bool b) return b ? "1" : "0";
                }
                catch { /* fall through */ }
            }

            return "0";
        }

        private static string? GetStringProperty(object obj, string propName)
        {
            var prop = obj.GetType().GetProperty(propName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return prop?.GetValue(obj)?.ToString();
        }
    }
}
