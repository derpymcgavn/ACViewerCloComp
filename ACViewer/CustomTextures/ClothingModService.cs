using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACViewer.CustomPalettes;

namespace ACViewer.CustomTextures
{
    /// <summary>
    /// Builds and validates the server-side ClothingTable payload consumed by
    /// OptimShi/CustomClothingBase. The source DAT object is never mutated.
    /// </summary>
    public static class ClothingModService
    {
        private const uint MinimumClothingId = 0x10000000;
        private const uint MaximumClothingId = 0x10FFFFFF;
        private const uint MaximumPaletteColors = 2048;

        private static readonly PropertyInfo FileIdProperty = typeof(FileType).GetProperty(
            nameof(FileType.Id), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo ObjectIndexProperty = typeof(CloObjectEffect).GetProperty(
            nameof(CloObjectEffect.Index), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo ObjectModelProperty = typeof(CloObjectEffect).GetProperty(
            nameof(CloObjectEffect.ModelId), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo OldTextureProperty = typeof(CloTextureEffect).GetProperty(
            nameof(CloTextureEffect.OldTexture), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo NewTextureProperty = typeof(CloTextureEffect).GetProperty(
            nameof(CloTextureEffect.NewTexture), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo IconProperty = typeof(CloSubPalEffect).GetProperty(
            nameof(CloSubPalEffect.Icon), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static ClothingTable CreateEmpty(uint id)
        {
            var table = new ClothingTable();
            SetId(table, id);
            return table;
        }

        public static ClothingTable Clone(ClothingTable source, uint? newId = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var clone = CreateEmpty(newId ?? source.Id);

            foreach (var pair in source.ClothingBaseEffects)
            {
                var baseEffect = new ClothingBaseEffect();
                foreach (var sourceObject in pair.Value.CloObjectEffects)
                {
                    var objectEffect = new CloObjectEffect();
                    ObjectIndexProperty?.SetValue(objectEffect, sourceObject.Index);
                    ObjectModelProperty?.SetValue(objectEffect, sourceObject.ModelId);
                    foreach (var sourceTexture in sourceObject.CloTextureEffects)
                    {
                        var textureEffect = new CloTextureEffect();
                        OldTextureProperty?.SetValue(textureEffect, sourceTexture.OldTexture);
                        NewTextureProperty?.SetValue(textureEffect, sourceTexture.NewTexture);
                        objectEffect.CloTextureEffects.Add(textureEffect);
                    }
                    baseEffect.CloObjectEffects.Add(objectEffect);
                }
                clone.ClothingBaseEffects.Add(pair.Key, baseEffect);
            }

            foreach (var pair in source.ClothingSubPalEffects)
                clone.ClothingSubPalEffects.Add(pair.Key, ClonePaletteEffect(pair.Value));

            return clone;
        }

        public static ClothingTable Compose(
            ClothingTable source,
            CustomTextureDefinition textureOverrides = null,
            CustomPaletteDefinition paletteDefinition = null,
            uint? paletteTemplate = null,
            uint? icon = null)
        {
            var result = Clone(source);

            if (paletteDefinition != null)
            {
                if (!paletteTemplate.HasValue || paletteTemplate.Value == 0)
                    throw new InvalidOperationException("A non-zero PaletteTemplate is required when exporting palette edits.");

                var effect = new CloSubPalEffect();
                var resolvedIcon = icon ?? (source.ClothingSubPalEffects.TryGetValue(paletteTemplate.Value, out var prior)
                    ? prior.Icon
                    : 0u);
                IconProperty?.SetValue(effect, resolvedIcon);

                foreach (var subPalette in CustomPaletteFactory.Build(paletteDefinition))
                    effect.CloSubPalettes.Add(CloneSubPalette(subPalette));

                if (effect.CloSubPalettes.Count == 0)
                    throw new InvalidOperationException("The palette edit contains no usable ranges.");
                result.ClothingSubPalEffects[paletteTemplate.Value] = effect;
            }

            ApplyTextureOverrides(result, textureOverrides);
            return result;
        }

        public static IReadOnlyList<string> Validate(ClothingTable table)
        {
            var errors = new List<string>();
            if (table == null)
            {
                errors.Add("Clothing table is missing.");
                return errors;
            }
            if (table.Id < MinimumClothingId || table.Id > MaximumClothingId)
                errors.Add($"Clothing ID 0x{table.Id:X8} must be in 0x10000000-0x10FFFFFF.");
            if (table.ClothingBaseEffects.Count == 0 && table.ClothingSubPalEffects.Count == 0)
                errors.Add("The mod contains neither model/texture effects nor palette effects.");

            foreach (var basePair in table.ClothingBaseEffects)
            {
                if ((basePair.Key >> 24) != 0x02)
                    errors.Add($"Base-effect key 0x{basePair.Key:X8} is not a Setup (0x02) ID.");
                foreach (var objectEffect in basePair.Value.CloObjectEffects)
                {
                    if (objectEffect.Index > 33)
                        errors.Add($"Part index {objectEffect.Index} is outside the supported player-part range 0-33.");
                    if (objectEffect.ModelId != 0 && (objectEffect.ModelId >> 24) != 0x01)
                        errors.Add($"Model 0x{objectEffect.ModelId:X8} for part {objectEffect.Index} is not a GfxObj (0x01) ID.");
                    foreach (var texture in objectEffect.CloTextureEffects)
                    {
                        if (texture.OldTexture != 0 && (texture.OldTexture >> 24) != 0x05)
                            errors.Add($"Old texture 0x{texture.OldTexture:X8} for part {objectEffect.Index} is not a SurfaceTexture (0x05) ID.");
                        if (texture.NewTexture != 0 && (texture.NewTexture >> 24) != 0x05)
                            errors.Add($"New texture 0x{texture.NewTexture:X8} for part {objectEffect.Index} is not a SurfaceTexture (0x05) ID.");
                    }
                }
            }

            foreach (var palettePair in table.ClothingSubPalEffects)
            {
                if (palettePair.Key == 0)
                    errors.Add("PaletteTemplate 0 is reserved and cannot contain a custom palette effect.");
                if (palettePair.Value.Icon != 0 && (palettePair.Value.Icon >> 24) != 0x06)
                    errors.Add($"Icon 0x{palettePair.Value.Icon:X8} for template {palettePair.Key} is not a Texture (0x06) ID.");
                var occupied = new HashSet<uint>();
                foreach (var palette in palettePair.Value.CloSubPalettes)
                {
                    var type = palette.PaletteSet >> 24;
                    if (type != 0x04 && type != 0x0F)
                        errors.Add($"Palette 0x{palette.PaletteSet:X8} in template {palettePair.Key} must be a Palette (0x04) or PaletteSet (0x0F).");
                    foreach (var range in palette.Ranges)
                    {
                        if (range.NumColors == 0)
                            errors.Add($"Template {palettePair.Key} contains an empty palette range.");
                        if (range.Offset % 8 != 0 || range.NumColors % 8 != 0)
                            errors.Add($"Template {palettePair.Key} range {range.Offset}:{range.NumColors} is not aligned to 8 colors.");
                        if (range.Offset >= MaximumPaletteColors || range.NumColors > MaximumPaletteColors - Math.Min(range.Offset, MaximumPaletteColors))
                            errors.Add($"Template {palettePair.Key} range {range.Offset}:{range.NumColors} exceeds {MaximumPaletteColors} colors.");
                        for (var color = range.Offset; color < range.Offset + range.NumColors && color < MaximumPaletteColors; color++)
                            if (!occupied.Add(color))
                            {
                                errors.Add($"Template {palettePair.Key} has overlapping palette ranges at color {color}.");
                                break;
                            }
                    }
                }
            }
            var files = DatManager.PortalDat?.AllFiles;
            if (files != null)
            {
                foreach (var basePair in table.ClothingBaseEffects)
                {
                    if (!files.ContainsKey(basePair.Key)) errors.Add($"Setup 0x{basePair.Key:X8} does not exist in the loaded portal DAT.");
                    foreach (var objectEffect in basePair.Value.CloObjectEffects)
                    {
                        if (objectEffect.ModelId != 0 && !files.ContainsKey(objectEffect.ModelId))
                            errors.Add($"Model 0x{objectEffect.ModelId:X8} does not exist in the loaded portal DAT.");
                        foreach (var texture in objectEffect.CloTextureEffects)
                        {
                            if (texture.OldTexture != 0 && !files.ContainsKey(texture.OldTexture))
                                errors.Add($"Old texture 0x{texture.OldTexture:X8} does not exist in the loaded portal DAT.");
                            if (texture.NewTexture != 0 && !files.ContainsKey(texture.NewTexture))
                                errors.Add($"New texture 0x{texture.NewTexture:X8} does not exist in the loaded portal DAT.");
                        }
                    }
                }
                foreach (var palettePair in table.ClothingSubPalEffects)
                {
                    if (palettePair.Value.Icon != 0 && !files.ContainsKey(palettePair.Value.Icon))
                        errors.Add($"Icon 0x{palettePair.Value.Icon:X8} does not exist in the loaded portal DAT.");
                    foreach (var palette in palettePair.Value.CloSubPalettes)
                        if (palette.PaletteSet != 0 && !files.ContainsKey(palette.PaletteSet))
                            errors.Add($"Palette 0x{palette.PaletteSet:X8} does not exist in the loaded portal DAT.");
                }
            }
            return errors.Distinct().ToList();
        }

        public static IReadOnlyList<string> Compare(ClothingTable expected, ClothingTable actual)
        {
            var errors = new List<string>();
            if (expected == null || actual == null)
            {
                errors.Add("Round-trip produced a missing clothing table.");
                return errors;
            }
            if (expected.Id != actual.Id) errors.Add("Clothing ID changed during round-trip.");
            if (expected.ClothingBaseEffects.Count != actual.ClothingBaseEffects.Count)
                errors.Add("Base-effect count changed during round-trip.");
            if (expected.ClothingSubPalEffects.Count != actual.ClothingSubPalEffects.Count)
                errors.Add("Palette-effect count changed during round-trip.");

            foreach (var pair in expected.ClothingBaseEffects)
            {
                if (!actual.ClothingBaseEffects.TryGetValue(pair.Key, out var other))
                {
                    errors.Add($"Base effect 0x{pair.Key:X8} disappeared during round-trip.");
                    continue;
                }
                var left = pair.Value.CloObjectEffects.SelectMany(o => o.CloTextureEffects.Select(t => (o.Index, o.ModelId, t.OldTexture, t.NewTexture))).ToList();
                var right = other.CloObjectEffects.SelectMany(o => o.CloTextureEffects.Select(t => (o.Index, o.ModelId, t.OldTexture, t.NewTexture))).ToList();
                if (!left.SequenceEqual(right)) errors.Add($"Base effect 0x{pair.Key:X8} changed during round-trip.");
            }
            foreach (var pair in expected.ClothingSubPalEffects)
            {
                if (!actual.ClothingSubPalEffects.TryGetValue(pair.Key, out var other))
                {
                    errors.Add($"Palette template {pair.Key} disappeared during round-trip.");
                    continue;
                }
                var left = pair.Value.CloSubPalettes.SelectMany(p => p.Ranges.Select(r => (pair.Value.Icon, p.PaletteSet, r.Offset, r.NumColors))).ToList();
                var right = other.CloSubPalettes.SelectMany(p => p.Ranges.Select(r => (other.Icon, p.PaletteSet, r.Offset, r.NumColors))).ToList();
                if (!left.SequenceEqual(right)) errors.Add($"Palette template {pair.Key} changed during round-trip.");
            }
            return errors;
        }

        /// <summary>
        /// Dependency-free smoke test used by CI and local verification. It exercises composition,
        /// compatible JSON serialization, import, validation, and structural comparison.
        /// </summary>
        public static void RunExportSelfTest()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ACViewer-ClothingMod-{Guid.NewGuid():N}.json");
            try
            {
                var source = CreateEmpty(0x10FF0001);
                var baseEffect = new ClothingBaseEffect();
                var objectEffect = new CloObjectEffect();
                ObjectIndexProperty?.SetValue(objectEffect, 9u);
                ObjectModelProperty?.SetValue(objectEffect, 0x01000001u);
                var textureEffect = new CloTextureEffect();
                OldTextureProperty?.SetValue(textureEffect, 0x05000001u);
                NewTextureProperty?.SetValue(textureEffect, 0x05000001u);
                objectEffect.CloTextureEffects.Add(textureEffect);
                baseEffect.CloObjectEffects.Add(objectEffect);
                source.ClothingBaseEffects.Add(0x02000001u, baseEffect);

                // Some live DAT clothing tables carry empty palette-template placeholders.
                // Export/import must preserve the key even when it has no icon or ranges.
                source.ClothingSubPalEffects.Add(61u, new CloSubPalEffect());

                var palette = new CustomPaletteDefinition
                {
                    Name = "SelfTest",
                    Entries = new List<CustomPaletteEntry>
                    {
                        new() { PaletteSetId = 0x04000001u, Ranges = new List<RangeDef> { new() { Offset = 0, Length = 1 } } }
                    }
                };
                var textures = new CustomTextureDefinition
                {
                    Name = "SelfTest",
                    Entries = new List<CustomTextureEntry>
                    {
                        new() { PartIndex = 9, OldId = 0x05000001u, NewId = 0x05000002u }
                    }
                };

                CustomTextureStore.ExportClothingTable(source, path, textures, palette, 94, 0);
                var exportedJson = File.ReadAllText(path);
                if (exportedJson.Contains("CustomTextureOverrides", StringComparison.Ordinal))
                    throw new InvalidDataException("Nonstandard CustomTextureOverrides leaked into the mod payload.");
                var exportedRows = TextureOverrideLocalStore.Deserialize(exportedJson);
                if (!exportedRows.Any(row => row.PartIndex == 9 && row.OldId == 0x05000001u && row.NewId == 0x05000002u))
                    throw new InvalidDataException("Texture override import did not recognize the ClothingMod export format.");
                var pal13 = new CustomPaletteDefinition
                {
                    Name = "SelfTestPal13",
                    Entries = new List<CustomPaletteEntry>
                    {
                        new() { PaletteSetId = 0x0F00001Du, Ranges = new List<RangeDef> { new() { Offset = 240, Length = 10 } } }
                    }
                };
                CustomTextureStore.ExportClothingTable(source, path, null, pal13, 13, 0x060017E4u);
                var pal13Json = File.ReadAllText(path);
                if (!pal13Json.Contains("\"13\"", StringComparison.Ordinal) ||
                    !pal13Json.Contains("\"Icon\": \"0x060017E4\"", StringComparison.Ordinal) ||
                    !pal13Json.Contains("\"PaletteSet\": \"0x0F00001D\"", StringComparison.Ordinal) ||
                    !pal13Json.Contains("\"Offset\": \"0x00000780\"", StringComparison.Ordinal) ||
                    !pal13Json.Contains("\"NumColors\": \"0x00000050\"", StringComparison.Ordinal))
                    throw new InvalidDataException("Palette template 13 did not export with the expected icon, palette set, or raw range values.");

                const string legacyOverrides = "{\"CustomTextureOverrides\":[{\"PartIndex\":\"0x00000009\",\"OldTexture\":\"0x05000001\",\"NewTexture\":\"0x05000002\"}]}";
                var legacyRows = TextureOverrideLocalStore.Deserialize(legacyOverrides);
                if (legacyRows.Count != 1 || legacyRows[0].PartIndex != 9 || legacyRows[0].OldId != 0x05000001u || legacyRows[0].NewId != 0x05000002u)
                    throw new InvalidDataException("Texture override import did not recognize the legacy format.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void ApplyTextureOverrides(ClothingTable table, CustomTextureDefinition overrides)
        {
            if (overrides?.Entries == null) return;
            var map = overrides.Entries.GroupBy(e => (e.PartIndex, e.OldId)).ToDictionary(g => g.Key, g => g.Last().NewId);
            foreach (var baseEffect in table.ClothingBaseEffects.Values)
                foreach (var objectEffect in baseEffect.CloObjectEffects)
                    foreach (var texture in objectEffect.CloTextureEffects)
                        if (map.TryGetValue(((int)objectEffect.Index, texture.OldTexture), out var replacement))
                            NewTextureProperty?.SetValue(texture, replacement);
        }

        private static CloSubPalEffect ClonePaletteEffect(CloSubPalEffect source)
        {
            var clone = new CloSubPalEffect();
            IconProperty?.SetValue(clone, source.Icon);
            foreach (var palette in source.CloSubPalettes) clone.CloSubPalettes.Add(CloneSubPalette(palette));
            return clone;
        }

        private static CloSubPalette CloneSubPalette(CloSubPalette source)
        {
            var clone = new CloSubPalette { PaletteSet = source.PaletteSet };
            foreach (var range in source.Ranges)
                clone.Ranges.Add(new CloSubPaletteRange { Offset = range.Offset, NumColors = range.NumColors });
            return clone;
        }

        private static void SetId(ClothingTable table, uint id) => FileIdProperty?.SetValue(table, id);

        internal static void AssignId(ClothingTable table, uint id) => SetId(table, id);
    }
}
