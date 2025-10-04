using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ACViewer.CustomTextures
{
    internal static class TextureOverrideLocalStore
    {
        private const string LocalFile = "TextureOverrides.local.json";

        internal class Row
        {
            public int PartIndex { get; set; }
            public uint OldId { get; set; }
            public uint NewId { get; set; }
            public bool IsLocked { get; set; }
        }

        public static void Save(IEnumerable<Row> rows)
        {
            try
            {
                var list = rows.ToList();
                File.WriteAllText(LocalFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static List<Row> Load()
        {
            try
            {
                if (!File.Exists(LocalFile)) return new();
                var text = File.ReadAllText(LocalFile);
                return JsonSerializer.Deserialize<List<Row>>(text) ?? new();
            }
            catch { return new(); }
        }
    }
}
