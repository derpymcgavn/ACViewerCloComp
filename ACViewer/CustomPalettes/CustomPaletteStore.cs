using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ACViewer.Config;


namespace ACViewer.CustomPalettes
{
    public static class CustomPaletteStore
    {
        private const string LegacyFileName = "CustomPalettes.json";
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
        private static List<CustomPaletteDefinition> _cache;

        public static IEnumerable<CustomPaletteDefinition> LoadAll()
        {
            if (_cache != null) return _cache;
            if (!File.Exists(FileName))
            {
                _cache = new List<CustomPaletteDefinition>();
                return _cache;
            }
            try
            {
                _cache = JsonConvert.DeserializeObject<List<CustomPaletteDefinition>>(File.ReadAllText(FileName))
                         ?? new List<CustomPaletteDefinition>();
            }
            catch
            {
                _cache = new List<CustomPaletteDefinition>();
            }
            return _cache;
        }

        public static void SaveDefinition(CustomPaletteDefinition def)
        {
            var all = LoadAll().ToList();
            var existing = all.FirstOrDefault(d => d.Name == def.Name);
            if (existing != null)
                all.Remove(existing);
            all.Add(def);
            _cache = all;
            File.WriteAllText(FileName, JsonConvert.SerializeObject(all, Formatting.Indented));
        }
    }
}
