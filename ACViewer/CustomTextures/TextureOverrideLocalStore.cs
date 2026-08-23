using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ACViewer.Config;


namespace ACViewer.CustomTextures
{
    internal static class TextureOverrideLocalStore
    {
        private static string GetLocalFile(uint clothingId)
        {
            var directory = Path.Combine(ConfigManager.AppDataDirectory, "TextureOverrides");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"{clothingId:X8}.json");
        }

        internal class Row
        {
            public int PartIndex { get; set; }
            public uint OldId { get; set; }
            public uint NewId { get; set; }
            public bool IsLocked { get; set; }
        }

        public static void Save(uint clothingId, IEnumerable<Row> rows)
        {
            try
            {
                var list = rows.ToList();
                File.WriteAllText(GetLocalFile(clothingId), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static List<Row> Load(uint clothingId)
        {
            try
            {
                var localFile = GetLocalFile(clothingId);
                if (!File.Exists(localFile)) return new();
                return Deserialize(File.ReadAllText(localFile));
            }
            catch { return new(); }
        }

        public static List<Row> Deserialize(string json)
        {
            using var document = JsonDocument.Parse(json);
            var rows = new List<Row>();
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                AddRows(root, rows);
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(root, "CustomTextureOverrides", out var overrides))
                    AddRows(overrides, rows);
                if (TryGetProperty(root, "Entries", out var entries))
                    AddRows(entries, rows);
                if (TryGetProperty(root, "ClothingBaseEffects", out var baseEffects))
                    AddClothingRows(baseEffects, rows);
                if (rows.Count == 0 && TryParseRow(root, out var row))
                    rows.Add(row);
            }

            if (rows.Count == 0)
                throw new InvalidDataException("No texture overrides were found. Choose a saved override list, a legacy CustomTextureOverrides file, or a ClothingMod JSON export.");

            return rows
                .GroupBy(row => (row.PartIndex, row.OldId))
                .Select(group => group.Last())
                .ToList();
        }

        private static void AddRows(JsonElement element, ICollection<Row> rows)
        {
            if (element.ValueKind != JsonValueKind.Array) return;
            foreach (var item in element.EnumerateArray())
                if (TryParseRow(item, out var row))
                    rows.Add(row);
        }

        private static void AddClothingRows(JsonElement baseEffects, ICollection<Row> rows)
        {
            if (baseEffects.ValueKind != JsonValueKind.Object) return;
            foreach (var baseEffect in baseEffects.EnumerateObject())
            {
                if (!TryGetProperty(baseEffect.Value, "CloObjectEffects", out var objectEffects) || objectEffects.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var objectEffect in objectEffects.EnumerateArray())
                {
                    if (!TryGetUInt(objectEffect, "Index", out var partIndex) ||
                        !TryGetProperty(objectEffect, "CloTextureEffects", out var textureEffects) ||
                        textureEffects.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var textureEffect in textureEffects.EnumerateArray())
                    {
                        if (TryGetUInt(textureEffect, "OldTexture", out var oldId) &&
                            TryGetUInt(textureEffect, "NewTexture", out var newId) &&
                            partIndex <= int.MaxValue)
                            rows.Add(new Row { PartIndex = (int)partIndex, OldId = oldId, NewId = newId });
                    }
                }
            }
        }

        private static bool TryParseRow(JsonElement element, out Row row)
        {
            row = null;
            if (element.ValueKind != JsonValueKind.Object ||
                !(TryGetUInt(element, "PartIndex", out var partIndex) || TryGetUInt(element, "Index", out partIndex)) ||
                !(TryGetUInt(element, "OldId", out var oldId) || TryGetUInt(element, "OldTexture", out oldId)) ||
                !(TryGetUInt(element, "NewId", out var newId) || TryGetUInt(element, "NewTexture", out newId)) ||
                partIndex > int.MaxValue)
                return false;

            var isLocked = TryGetProperty(element, "IsLocked", out var locked) &&
                           (locked.ValueKind == JsonValueKind.True || locked.ValueKind == JsonValueKind.String && bool.TryParse(locked.GetString(), out var parsed) && parsed);
            row = new Row { PartIndex = (int)partIndex, OldId = oldId, NewId = newId, IsLocked = isLocked };
            return true;
        }

        private static bool TryGetUInt(JsonElement element, string name, out uint value)
        {
            value = 0;
            if (!TryGetProperty(element, name, out var property)) return false;
            if (property.ValueKind == JsonValueKind.Number) return property.TryGetUInt32(out value);
            if (property.ValueKind != JsonValueKind.String) return false;
            var text = property.GetString()?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToUInt32(text.Substring(2), 16)
                    : Convert.ToUInt32(text, 10);
                return true;
            }
            catch { return false; }
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
            value = default;
            return false;
        }
    }
}
