using System;
using System.Collections.Generic;
using System.IO;

namespace ACViewer.Data
{
    // Simplified in-memory store for clothing JSON blobs and metadata (removed SQLite dependency)
    public static class ClothingRepository
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<uint, (string Name,string Json, DateTime UpdatedUtc)> _items = new();
        private static string _persistPath;

        public static void Initialize(string dbPath = null)
        {
            // optional JSON persistence (single file) if path provided
            lock (_lock)
            {
                _persistPath = string.IsNullOrEmpty(dbPath) ? null : dbPath;
                if (!string.IsNullOrEmpty(_persistPath))
                {
                    var dir = Path.GetDirectoryName(_persistPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    LoadFromDisk();
                }
            }
        }

        private static void LoadFromDisk()
        {
            if (string.IsNullOrEmpty(_persistPath) || !File.Exists(_persistPath)) return;
            try
            {
                var lines = File.ReadAllLines(_persistPath);
                _items.Clear();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 4) continue;
                    if (!uint.TryParse(parts[0], out var id)) continue;
                    var name = parts[1];
                    var updated = DateTime.TryParse(parts[2], out var dt) ? dt : DateTime.UtcNow;
                    var json = parts[3];
                    _items[id] = (name, json, updated);
                }
            }
            catch { /* ignore */ }
        }

        private static void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_persistPath)) return;
            try
            {
                var lines = new List<string>();
                foreach (var kvp in _items)
                    lines.Add(string.Join('\t', kvp.Key, kvp.Value.Name, kvp.Value.UpdatedUtc.ToString("o"), kvp.Value.Json.Replace('\n',' ')));
                File.WriteAllLines(_persistPath, lines);
            }
            catch { /* ignore */ }
        }

        public static void Upsert(uint id, string name, string json)
        {
            lock (_lock)
            {
                _items[id] = (name ?? string.Empty, json ?? string.Empty, DateTime.UtcNow);
                SaveToDisk();
            }
        }

        public static string GetJson(uint id)
        {
            lock (_lock)
            {
                return _items.TryGetValue(id, out var entry) ? entry.Json : null;
            }
        }

        public static IEnumerable<uint> ListIds()
        {
            lock (_lock)
            {
                return new List<uint>(_items.Keys);
            }
        }

        public static void ExportJsonToFile(uint id, string path)
        {
            var json = GetJson(id);
            if (json == null) throw new FileNotFoundException("Clothing JSON not found in repo for id", id.ToString());
            File.WriteAllText(path, json);
        }
    }
}
