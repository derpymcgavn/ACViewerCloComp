using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using System;
using System.Reflection;
using System.Threading;

namespace ACViewer.CustomTextures
{
    public static class CustomTextureStore
    {
        private const string FileName = "CustomTextures.json";
        private static List<CustomTextureDefinition> _cache;

        // Added watcher for real-time updates of last imported JSON file
        private static FileSystemWatcher _activeWatcher;
        private static string _watchedClothingJsonPath;
        private static DateTime _lastWatcherRead = DateTime.MinValue;
        private static readonly object _watcherLock = new();

        /// <summary>
        /// Raised when a watched clothing JSON file is modified on disk and successfully re-imported.
        /// Provides the updated ClothingTable instance.
        /// </summary>
        public static event Action<ClothingTable> ClothingJsonUpdated;

        public static IEnumerable<CustomTextureDefinition> LoadAll()
        {
            if (_cache != null) return _cache;
            if (!File.Exists(FileName)) { _cache = new List<CustomTextureDefinition>(); return _cache; }
            try { _cache = JsonConvert.DeserializeObject<List<CustomTextureDefinition>>(File.ReadAllText(FileName)) ?? new List<CustomTextureDefinition>(); }
            catch { _cache = new List<CustomTextureDefinition>(); }
            return _cache;
        }

        public static void SaveDefinition(CustomTextureDefinition def)
        {
            var all = LoadAll().ToList();
            var existing = all.FirstOrDefault(d => d.Name == def.Name);
            if (existing != null) all.Remove(existing);
            all.Add(def);
            _cache = all;
            File.WriteAllText(FileName, JsonConvert.SerializeObject(all, Formatting.Indented));
        }

        // New: export clothing table (with optional overrides) to requested JSON format
        public static void ExportClothingTable(ClothingTable table, string path, CustomTextureDefinition overrides = null)
        {
            var export = new ClothingExport { Id = $"0x{table.Id:X8}" };
            foreach (var kvp in table.ClothingBaseEffects)
            {
                var baseOut = new ClothingBaseEffectExport();
                foreach (var objEff in kvp.Value.CloObjectEffects)
                {
                    var objOut = new CloObjectEffectExport { Index = $"0x{objEff.Index:X8}", ModelId = $"0x{objEff.ModelId:X8}" };
                    foreach (var tex in objEff.CloTextureEffects)
                    {
                        objOut.CloTextureEffects.Add(new CloTextureEffectExport { OldTexture = $"0x{tex.OldTexture:X8}", NewTexture = $"0x{tex.NewTexture:X8}" });
                    }
                    baseOut.CloObjectEffects.Add(objOut);
                }
                export.ClothingBaseEffects[$"0x{kvp.Key:X6}"] = baseOut; // key formatting similar to sample
            }
            foreach (var kvp in table.ClothingSubPalEffects)
            {
                var subOut = new ClothingSubPalExport { Icon = $"0x{kvp.Value.Icon:X8}" };
                foreach (var sp in kvp.Value.CloSubPalettes)
                {
                    var spOut = new CloSubPaletteExport { PaletteSet = $"0x{sp.PaletteSet:X8}" };
                    foreach (var r in sp.Ranges)
                    {
                        spOut.Ranges.Add(new CloSubPaletteRangeExport { Offset = $"0x{r.Offset:X8}", NumColors = $"0x{r.NumColors:X8}" });
                    }
                    subOut.CloSubPalettes.Add(spOut);
                }
                export.ClothingSubPalEffects[$"{kvp.Key}"] = subOut; // palette template numeric key (decimal)
            }

            // add custom texture overrides if provided
            if (overrides != null)
            {
                foreach (var entry in overrides.Entries)
                {
                    export.CustomTextureOverrides.Add(new CustomTextureOverrideExport
                    {
                        PartIndex = $"0x{entry.PartIndex:X8}",
                        OldTexture = $"0x{entry.OldId:X8}",
                        NewTexture = $"0x{entry.NewId:X8}"
                    });
                }
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(export, Formatting.Indented));
        }

        // New: Import clothing table JSON into ClothingTable instance
        public static ClothingTable ImportClothingTable(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Clothing JSON not found", path);
            ClothingExport export;
            try { export = JsonConvert.DeserializeObject<ClothingExport>(File.ReadAllText(path)); }
            catch (Exception ex) { throw new InvalidDataException("Failed to parse JSON", ex); }
            if (export == null) throw new InvalidDataException("Empty JSON");

            var table = new ClothingTable();
            if (!string.IsNullOrWhiteSpace(export.Id))
            {
                try
                {
                    uint idVal = ParseUInt(export.Id);
                    var prop = typeof(ACE.DatLoader.FileTypes.FileType).GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    prop?.SetValue(table, idVal, null);
                }
                catch { }
            }

            // Reflection helpers for private set properties
            var cloObjIndexProp = typeof(CloObjectEffect).GetProperty("Index", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var cloObjModelProp = typeof(CloObjectEffect).GetProperty("ModelId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var cloTexOldProp = typeof(CloTextureEffect).GetProperty("OldTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var cloTexNewProp = typeof(CloTextureEffect).GetProperty("NewTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var subIconProp = typeof(CloSubPalEffect).GetProperty("Icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Base effects
            foreach (var kvp in export.ClothingBaseEffects)
            {
                uint setupId; try { setupId = ParseUInt(kvp.Key); } catch { continue; }
                var baseEffect = new ClothingBaseEffect();
                foreach (var obj in kvp.Value.CloObjectEffects)
                {
                    var objEff = new CloObjectEffect();
                    try { cloObjIndexProp?.SetValue(objEff, ParseUInt(obj.Index)); } catch { }
                    try { cloObjModelProp?.SetValue(objEff, ParseUInt(obj.ModelId)); } catch { }
                    foreach (var tex in obj.CloTextureEffects)
                    {
                        var texEff = new CloTextureEffect();
                        try { cloTexOldProp?.SetValue(texEff, ParseUInt(tex.OldTexture)); } catch { }
                        try { cloTexNewProp?.SetValue(texEff, ParseUInt(tex.NewTexture)); } catch { }
                        objEff.CloTextureEffects.Add(texEff);
                    }
                    baseEffect.CloObjectEffects.Add(objEff);
                }
                if (!table.ClothingBaseEffects.ContainsKey(setupId))
                    table.ClothingBaseEffects.Add(setupId, baseEffect);
            }

            // Sub palette effects
            foreach (var kvp in export.ClothingSubPalEffects)
            {
                uint palTemplate; try { palTemplate = ParseUInt(kvp.Key); } catch { continue; }
                var subEffect = new CloSubPalEffect();
                try { subIconProp?.SetValue(subEffect, ParseUInt(kvp.Value.Icon)); } catch { }
                foreach (var sp in kvp.Value.CloSubPalettes)
                {
                    var spDef = new CloSubPalette();
                    try { spDef.PaletteSet = ParseUInt(sp.PaletteSet); } catch { continue; }
                    foreach (var r in sp.Ranges)
                    {
                        try
                        {
                            var offRaw = ParseUInt(r.Offset);
                            var lenRaw = ParseUInt(r.NumColors);
                            if (offRaw % 8 != 0) offRaw -= offRaw % 8;
                            if (lenRaw % 8 != 0) lenRaw -= lenRaw % 8;
                            if (lenRaw == 0) continue;
                            spDef.Ranges.Add(new CloSubPaletteRange
                            {
                                Offset = offRaw,
                                NumColors = lenRaw
                            });
                        }
                        catch { }
                    }
                    if (spDef.Ranges.Count > 0)
                        subEffect.CloSubPalettes.Add(spDef);
                }
                if (subEffect.CloSubPalettes.Count > 0 || (uint)subIconProp?.GetValue(subEffect) != 0)
                {
                    if (!table.ClothingSubPalEffects.ContainsKey(palTemplate))
                        table.ClothingSubPalEffects.Add(palTemplate, subEffect);
                }
            }

            return table;
        }

        /// <summary>
        /// Begin watching a specific clothing JSON file for modifications. Each change triggers a safe re-import and UI refresh callback.
        /// </summary>
        public static void WatchClothingJson(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                lock (_watcherLock)
                {
                    var full = Path.GetFullPath(path);
                    if (string.Equals(full, _watchedClothingJsonPath, StringComparison.OrdinalIgnoreCase))
                        return; // already watching

                    DisposeWatcher_NoLock();

                    _watchedClothingJsonPath = full;
                    _activeWatcher = new FileSystemWatcher(Path.GetDirectoryName(full)!, Path.GetFileName(full))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                    };
                    _activeWatcher.Changed += WatcherOnChanged;
                    _activeWatcher.Renamed += WatcherOnChanged;
                    _activeWatcher.EnableRaisingEvents = true;
                }
            }
            catch { /* swallow watcher issues silently */ }
        }

        private static void WatcherOnChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce rapid successive events
            lock (_watcherLock)
            {
                if ((DateTime.UtcNow - _lastWatcherRead).TotalMilliseconds < 150)
                    return;
                _lastWatcherRead = DateTime.UtcNow;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                // Allow file write to complete
                Thread.Sleep(120);
                try
                {
                    ClothingTable updated = ImportClothingTable(_watchedClothingJsonPath);
                    ClothingJsonUpdated?.Invoke(updated);
                }
                catch
                {
                    // ignore parse errors during live edit; UI keeps last good state
                }
            });
        }

        public static void StopWatchingClothingJson()
        {
            lock (_watcherLock)
            {
                DisposeWatcher_NoLock();
                _watchedClothingJsonPath = null;
            }
        }

        private static void DisposeWatcher_NoLock()
        {
            if (_activeWatcher != null)
            {
                try
                {
                    _activeWatcher.EnableRaisingEvents = false;
                    _activeWatcher.Changed -= WatcherOnChanged;
                    _activeWatcher.Renamed -= WatcherOnChanged;
                    _activeWatcher.Dispose();
                }
                catch { }
                _activeWatcher = null;
            }
        }

        private static uint ParseUInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s.Substring(2), 16);
            return Convert.ToUInt32(s, 10);
        }
    }
}
