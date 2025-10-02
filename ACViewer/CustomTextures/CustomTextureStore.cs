using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;

namespace ACViewer.CustomTextures
{
    public static class CustomTextureStore
    {
        private const string FileName = "CustomTextures.json";
        private static List<CustomTextureDefinition> _cache;

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
                export.ClothingBaseEffects[$"0x{kvp.Key:X6}"] = baseOut; // key formatting similar to sample (variable widths retained)
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
                export.ClothingSubPalEffects[$"{kvp.Key}"] = subOut; // palette template numeric key
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
    }
}
