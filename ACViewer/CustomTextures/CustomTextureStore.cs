using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using System;
using ACViewer.CustomPalettes;
using ACViewer.Config;
using System.Reflection;
using System.Threading;

namespace ACViewer.CustomTextures
{
    public static class CustomTextureStore
    {
        private const string LegacyFileName = "CustomTextures.json";
        private static string FileName
        {
            get
            {
                var directory = ConfigManager.AppDataDirectory;
                Directory.CreateDirectory(directory);
                var fileName = Path.Combine(directory, LegacyFileName);
                if (!File.Exists(fileName) && File.Exists(LegacyFileName))
                    File.Copy(LegacyFileName, fileName);
                return fileName;
            }
        }
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

        /// <summary>
        /// Exports an OptimShi/CustomClothingBase-compatible JSON file. Palette and texture edits
        /// are baked into a cloned ClothingTable, validated, written, and imported again to prove
        /// that no supported data was lost in serialization.
        /// </summary>
        public static void ExportClothingTable(ClothingTable table, string path, CustomTextureDefinition overrides = null,
            CustomPaletteDefinition palette = null, uint? paletteTemplate = null, uint? icon = null)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An export path is required.", nameof(path));

            var composed = ClothingModService.Compose(table, overrides, palette, paletteTemplate, icon);
            var validationErrors = ClothingModService.Validate(composed);
            if (validationErrors.Count > 0)
                throw new InvalidDataException("ClothingMod validation failed:\n- " + string.Join("\n- ", validationErrors));
            var export = new ClothingExport
            {
                Id = $"0x{composed.Id:X8}",
                AllowBaseOverride = DatManager.PortalDat?.AllFiles?.ContainsKey(composed.Id) == true
            };
            foreach (var kvp in composed.ClothingBaseEffects)
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
                export.ClothingBaseEffects[$"0x{kvp.Key:X8}"] = baseOut;
            }
            foreach (var kvp in composed.ClothingSubPalEffects)
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
            File.WriteAllText(path, JsonConvert.SerializeObject(export, Formatting.Indented));

            var roundTrip = ImportClothingTable(path);
            var roundTripErrors = ClothingModService.Validate(roundTrip)
                .Concat(ClothingModService.Compare(composed, roundTrip))
                .Distinct()
                .ToList();
            if (roundTripErrors.Count > 0)
                throw new InvalidDataException("Export round-trip failed:\n- " + string.Join("\n- ", roundTripErrors));
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
                // Preserve the explicit palette-template key even when the DAT entry is an empty
                // placeholder. Some clothing tables contain zero-range/zero-icon palette templates;
                // dropping them here makes export round-trip validation report that the template
                // disappeared, even though the JSON faithfully represented it.
                if (!table.ClothingSubPalEffects.ContainsKey(palTemplate))
                    table.ClothingSubPalEffects.Add(palTemplate, subEffect);
            }

            // Apply the explicit override section after rebuilding the base table so export/import is symmetric.
            if (export.CustomTextureOverrides != null)
            {
                foreach (var item in export.CustomTextureOverrides)
                {
                    try
                    {
                        var partIndex = ParseUInt(item.PartIndex);
                        var oldTexture = ParseUInt(item.OldTexture);
                        var newTexture = ParseUInt(item.NewTexture);
                        foreach (var baseEffect in table.ClothingBaseEffects.Values)
                        {
                            foreach (var objectEffect in baseEffect.CloObjectEffects.Where(o => o.Index == partIndex))
                            {
                                foreach (var textureEffect in objectEffect.CloTextureEffects.Where(t => t.OldTexture == oldTexture))
                                {
                                    if (cloTexNewProp != null)
                                        cloTexNewProp.SetValue(textureEffect, newTexture);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore only the malformed override; the remainder of the clothing table is still usable.
                    }
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
