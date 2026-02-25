using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Caches device settings per side after a single batched read per ParameterSpace.
    /// UI reads only from cache; writes mark dirty and are committed only on Save to Device.
    /// Call InvalidateCacheForSide after writes, configure, reconnect, or library switch.
    /// </summary>
    public class SettingsCache
    {
        private readonly Dictionary<DeviceSide, DeviceSettingsSnapshot?> _snapshots = new();
        private readonly Dictionary<DeviceSide, bool> _valid = new();
        private readonly DirtyBuffer _dirtyBuffer = new DirtyBuffer();

        /// <summary>Dirty buffer for tracking unsaved changes; cleared on Save.</summary>
        public DirtyBuffer DirtyBuffer => _dirtyBuffer;

        /// <summary>True if the cache for the given side has been loaded and not invalidated.</summary>
        public bool IsValid(DeviceSide side) => _valid.TryGetValue(side, out var v) && v && _snapshots.TryGetValue(side, out var s) && s != null;

        /// <summary>Load cache for the given side by running the loader once. No-op if already valid.</summary>
        public async Task EnsureCacheLoadedAsync(DeviceSide side, Func<DeviceSide, Task<DeviceSettingsSnapshot?>> loadAsync)
        {
            if (IsValid(side)) return;
            var sw = Stopwatch.StartNew();
            var snapshot = await loadAsync(side).ConfigureAwait(false);
            _snapshots[side] = snapshot;
            _valid[side] = snapshot != null;
            sw.Stop();
            var paramCount = snapshot?.Categories.SelectMany(c => c.Sections.SelectMany(s => s.Items)).Count() ?? 0;
            System.Diagnostics.Debug.WriteLine($"[Perf] ReadSpace side={side} ms={sw.ElapsedMilliseconds} params={paramCount}");
        }

        /// <summary>Returns the cached snapshot for the side, or null if not loaded.</summary>
        public DeviceSettingsSnapshot? GetSnapshot(DeviceSide side) =>
            _snapshots.TryGetValue(side, out var s) ? s : null;

        /// <summary>Sets the snapshot for the side (e.g. after load). Marks cache valid.</summary>
        public void SetSnapshot(DeviceSide side, DeviceSettingsSnapshot? snapshot)
        {
            _snapshots[side] = snapshot;
            _valid[side] = snapshot != null;
        }

        /// <summary>Try get current value for a parameter (from cache + dirty overrides).</summary>
        public bool TryGetValue(DeviceSide side, string paramId, out object? value)
        {
            if (_dirtyBuffer.TryGet(side, paramId, out value)) return true;
            var snapshot = GetSnapshot(side);
            var item = FindItem(snapshot, paramId);
            if (item != null) { value = item.Value; return true; }
            value = null; return false;
        }

        /// <summary>Set value and mark parameter dirty. Does not call SDK write.</summary>
        public void SetValue(DeviceSide side, string paramId, object? value)
        {
            var snapshot = GetSnapshot(side);
            var item = FindItem(snapshot, paramId);
            if (item != null)
            {
                item.Value = value;
                _dirtyBuffer.Add(side, paramId, value);
            }
        }

        /// <summary>Invalidate cache for the given side; next EnsureCacheLoadedAsync will re-read.</summary>
        public void InvalidateCacheForSide(DeviceSide side)
        {
            _valid[side] = false;
            _snapshots[side] = null;
        }

        /// <summary>Invalidate both sides (e.g. reconnect or library switch).</summary>
        public void InvalidateAll()
        {
            InvalidateCacheForSide(DeviceSide.Left);
            InvalidateCacheForSide(DeviceSide.Right);
        }

        private static SettingItem? FindItem(DeviceSettingsSnapshot? snapshot, string paramId)
        {
            if (snapshot == null || string.IsNullOrEmpty(paramId)) return null;
            foreach (var cat in snapshot.Categories)
            foreach (var sec in cat.Sections)
            foreach (var item in sec.Items)
                if (string.Equals(item.Id, paramId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ParameterId, paramId, StringComparison.OrdinalIgnoreCase))
                    return item;
            return null;
        }
    }

    /// <summary>Tracks dirty parameters per side until Save; cleared on successful write.</summary>
    public class DirtyBuffer
    {
        private readonly Dictionary<(DeviceSide Side, string ParamId), object?> _dirty = new();

        public bool TryGet(DeviceSide side, string paramId, out object? value)
        {
            return _dirty.TryGetValue((side, paramId), out value);
        }

        public void Add(DeviceSide side, string paramId, object? value)
        {
            _dirty[(side, paramId)] = value;
        }

        public void Clear()
        {
            _dirty.Clear();
        }

        public bool Any => _dirty.Count > 0;
        public int Count => _dirty.Count;
    }
}
