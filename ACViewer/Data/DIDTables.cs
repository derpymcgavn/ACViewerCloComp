using System;
using System.Collections.Generic;
using System.IO;

namespace ACViewer.Data
{
    public static class DIDTables
    {
        public static Dictionary<uint, DIDTable> Setups { get; set; }
        private static readonly object LoadLock = new();
        private static bool Loaded;

        static DIDTables()
        {
            Setups = new Dictionary<uint, DIDTable>();
        }

        public static void Load()
        {
            if (Loaded) return;

            lock (LoadLock)
            {
                if (Loaded) return;

                var filename = Path.Combine(AppContext.BaseDirectory, "Data", "DIDTables.txt");
                var loadedSetups = new Dictionary<uint, DIDTable>();
                var lineNumber = 0;

                foreach (var line in File.ReadLines(filename))
                {
                    lineNumber++;

                    // comment
                    if (line.StartsWith("#"))
                        continue;

                    var pieces = line.Split(',');

                    if (pieces.Length != 4)
                    {
                        Console.WriteLine($"DIDTables.Load({filename}): line {lineNumber} length {pieces.Length}");
                        continue;
                    }

                    var setupID = pieces[0].Length > 0 ? Convert.ToUInt32(pieces[0], 16) : 0;
                    var mtableID = pieces[1].Length > 0 ? Convert.ToUInt32(pieces[1], 16) : 0;
                    var stableID = pieces[2].Length > 0 ? Convert.ToUInt32(pieces[2], 16) : 0;
                    var ctableID = pieces[3].Length > 0 ? Convert.ToUInt32(pieces[3], 16) : 0;

                    var table = new DIDTable(setupID)
                    {
                        MotionTableID = mtableID,
                        SoundTableID = stableID,
                        CombatTableID = ctableID
                    };

                    loadedSetups[setupID] = table;
                }

                Setups = loadedSetups;
                Loaded = true;
            }
        }

        public static DIDTable Get(uint setupID)
        {
            Setups.TryGetValue(setupID, out var setup);
            return setup;
        }
    }
}
