using System;
using System.IO;
using System.Linq;

using Newtonsoft.Json;

namespace ACViewer.Config
{
    public static class ConfigManager
    {
        public const string ProductDirectoryName = "DerpACE Clothing Studio";
        private static readonly object MigrationLock = new();
        private static bool _migrationChecked;
        public static string LegacyAppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACViewer");

        public static string AppDataDirectory
        {
            get
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName);
                Directory.CreateDirectory(directory);
                EnsureLegacyDataMigrated(directory);
                return directory;
            }
        }

        private static string Filename
        {
            get
            {
                var destination = Path.Combine(AppDataDirectory, "DerpAceClothingStudio.json");
                if (File.Exists(destination)) return destination;

                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "ACViewer.json"),
                    Path.Combine(AppDataDirectory, "ACViewer.json"),
                    Path.Combine(LegacyAppDataDirectory, "ACViewer.json"),
                    Path.Combine(Environment.CurrentDirectory, "ACViewer.json")
                };
                foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(candidate) || string.Equals(candidate, destination, StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(candidate, destination, false);
                    break;
                }
                return destination;
            }
        }

        private static Config config { get; set; }
        private static void EnsureLegacyDataMigrated(string destination)
        {
            lock (MigrationLock)
            {
                if (_migrationChecked)
                    return;
                _migrationChecked = true;

                if (!Directory.Exists(LegacyAppDataDirectory))
                    return;

                foreach (var sourceFile in Directory.EnumerateFiles(LegacyAppDataDirectory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(LegacyAppDataDirectory, sourceFile);
                        var destinationFile = Path.Combine(destination, relativePath);
                        if (File.Exists(destinationFile))
                            continue;

                        var destinationDirectory = Path.GetDirectoryName(destinationFile);
                        if (!string.IsNullOrWhiteSpace(destinationDirectory))
                            Directory.CreateDirectory(destinationDirectory);
                        File.Copy(sourceFile, destinationFile, false);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }


        public static Config Config
        {
            get
            {
                if (config == null)
                    config = new Config();

                return config;
            }
        }

        public static Config Snapshot { get; set; }

        public static void SaveConfig()
        {
            var settings = new JsonSerializerSettings();
            settings.Formatting = Formatting.Indented;

            var json = JsonConvert.SerializeObject(config, settings);

            File.WriteAllText(Filename, json);
        }

        public static void LoadConfig()
        {
            config = ReadConfig();
        }

        public static Config ReadConfig()
        {
            if (!File.Exists(Filename)) return null;

            var json = File.ReadAllText(Filename);

            var _config = JsonConvert.DeserializeObject<Config>(json);

            if (_config == null)
            {
                Console.WriteLine($"ConfigManager.LoadConfig() - failed to parse {Filename}");
                return null;
            }
            return _config;
        }

        public static void TakeSnapshot()
        {
            Snapshot = ReadConfig();
        }

        public static void RestoreSnapshot()
        {
            config = Snapshot;
        }

        public static bool HasDBInfo
        {
            get
            {
                return config != null && config.Database != null && !string.IsNullOrWhiteSpace(config.Database.Host) &&
                    config.Database.Port > 0 &&
                    !string.IsNullOrWhiteSpace(config.Database.DatabaseName) &&
                    !string.IsNullOrWhiteSpace(config.Database.Username) &&
                    !string.IsNullOrWhiteSpace(config.Database.Password);
            }
        }
    }
}
