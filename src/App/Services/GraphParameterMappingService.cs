using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>Loads GraphParameterMap.json and resolves mappings by library/product key.</summary>
    public sealed class GraphParameterMappingService : IGraphParameterMappingService
    {
        private const string MapFileName = "GraphParameterMap.json";
        private readonly Dictionary<string, GraphParameterMapProduct> _mapsByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _basePath;

        public GraphParameterMappingService()
        {
            _basePath = AppDomain.CurrentDomain.BaseDirectory;
            LoadMap();
        }

        private void LoadMap()
        {
            var path = Path.Combine(_basePath, "Assets", MapFileName);
            if (!File.Exists(path))
                path = Path.Combine(_basePath, MapFileName);
            if (!File.Exists(path))
            {
                Debug.WriteLine("[GraphParameterMapping] GraphParameterMap.json not found; graphs will show 'mapping not configured'.");
                return;
            }
            try
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = JsonSerializer.Deserialize<Dictionary<string, GraphParameterMapProduct>>(json, options);
                if (root != null)
                {
                    foreach (var kv in root)
                        _mapsByKey[kv.Key] = kv.Value;
                    Debug.WriteLine($"[GraphParameterMapping] Loaded mapping keys: {string.Join(", ", _mapsByKey.Keys)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GraphParameterMapping] Failed to load map: {ex.Message}");
            }
        }

        private GraphParameterMapProduct? GetMap(string? libraryOrProductKey)
        {
            if (string.IsNullOrEmpty(libraryOrProductKey)) return _mapsByKey.GetValueOrDefault("default");
            if (_mapsByKey.TryGetValue(libraryOrProductKey, out var map)) return map;
            var fileName = Path.GetFileName(libraryOrProductKey);
            if (!string.IsNullOrEmpty(fileName) && _mapsByKey.TryGetValue(fileName, out map)) return map;
            return _mapsByKey.GetValueOrDefault("default");
        }

        public bool HasMappingFor(string? libraryOrProductKey)
        {
            var map = GetMap(libraryOrProductKey);
            return map?.FreqGain != null || map?.Io != null;
        }

        public IReadOnlyDictionary<int, string> GetFreqGainParameterIdsByLevel(string? libraryOrProductKey)
        {
            var map = GetMap(libraryOrProductKey)?.FreqGain;
            if (map?.GainParamIdByLevel == null) return new Dictionary<int, string>();

            var result = new Dictionary<int, string>();
            foreach (var kv in map.GainParamIdByLevel)
                if (int.TryParse(kv.Key, out var level) && !string.IsNullOrEmpty(kv.Value))
                    result[level] = kv.Value;
            return result;
        }

        public IReadOnlyList<string> GetFreqGainCenterFrequencyParamIds(string? libraryOrProductKey)
        {
            var map = GetMap(libraryOrProductKey)?.FreqGain;
            return map?.CenterFreqParamIds ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        public IReadOnlyDictionary<int, IReadOnlyList<string>> GetInputOutputParamIdsByFrequencyHz(string? libraryOrProductKey)
        {
            var map = GetMap(libraryOrProductKey)?.Io;
            if (map?.InputOutputParamIdsByFrequencyHz == null) return new Dictionary<int, IReadOnlyList<string>>();

            var result = new Dictionary<int, IReadOnlyList<string>>();
            foreach (var kv in map.InputOutputParamIdsByFrequencyHz)
                if (int.TryParse(kv.Key, out var hz))
                    result[hz] = kv.Value ?? (IReadOnlyList<string>)Array.Empty<string>();
            return result;
        }

    }

    public class GraphParameterMapProduct
    {
        public FreqGainMap? FreqGain { get; set; }
        public IoMap? Io { get; set; }
    }

    public class FreqGainMap
    {
        public List<string>? CenterFreqParamIds { get; set; }
        public Dictionary<string, string>? GainParamIdByLevel { get; set; }
    }

    public class IoMap
    {
        public Dictionary<string, List<string>>? InputOutputParamIdsByFrequencyHz { get; set; }
    }
}
