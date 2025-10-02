using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ACViewer.Utilities
{
    /// <summary>
    /// Small CSS color name catalog + nearest color name helpers for palette search.
    /// Stored as RGB (no alpha) 0xRRGGBB
    /// </summary>
    public static class ColorNameCatalog
    {
        private static readonly Dictionary<string, int> _nameToColor = new(StringComparer.OrdinalIgnoreCase)
        {
            // Core CSS colors (subset)
            {"black", 0x000000},{"white",0xFFFFFF},{"red",0xFF0000},{"lime",0x00FF00},{"blue",0x0000FF},
            {"yellow",0xFFFF00},{"cyan",0x00FFFF},{"magenta",0xFF00FF},{"gray",0x808080},{"silver",0xC0C0C0},
            {"maroon",0x800000},{"olive",0x808000},{"green",0x008000},{"purple",0x800080},{"teal",0x008080},{"navy",0x000080},
            {"orange",0xFFA500},{"gold",0xFFD700},{"brown",0xA52A2A},{"tan",0xD2B48C},{"khaki",0xF0E68C},
            {"crimson",0xDC143C},{"indigo",0x4B0082},{"violet",0xEE82EE},{"pink",0xFFC0CB},{"salmon",0xFA8072},
            {"coral",0xFF7F50},{"turquoise",0x40E0D0},{"aquamarine",0x7FFFD4},{"skyblue",0x87CEEB},{"royalblue",0x4169E1},
            {"dodgerblue",0x1E90FF},{"slategray",0x708090},{"firebrick",0xB22222},{"chocolate",0xD2691E},{"seagreen",0x2E8B57},
            {"forestgreen",0x228B22},{"springgreen",0x00FF7F},{"lavender",0xE6E6FA},{"beige",0xF5F5DC},{"mistyrose",0xFFE4E1},
            {"plum",0xDDA0DD},{"orchid",0xDA70D6},{"sienna",0xA0522D},{"peru",0xCD853F},{"lightgray",0xD3D3D3},
            {"darkgray",0xA9A9A9},{"lightgreen",0x90EE90},{"lightblue",0xADD8E6},{"darkred",0x8B0000},{"darkgreen",0x006400},
            {"darkblue",0x00008B},{"midnightblue",0x191970},{"darkorange",0xFF8C00},{"aliceblue",0xF0F8FF},{"linen",0xFAF0E6}
        };

        private static List<(string Name,int Color,int R,int G,int B)> _cache;
        private static List<(string Name,int Color,int[] Vector)> _spellCache;

        private static void EnsureCache()
        {
            if (_cache != null) return;
            _cache = _nameToColor.Select(kv => (kv.Key, kv.Value, (kv.Value>>16)&0xFF, (kv.Value>>8)&0xFF, kv.Value&0xFF)).ToList();
        }

        public static bool TryGet(string name, out int rgb) => _nameToColor.TryGetValue(name, out rgb);

        /// <summary>Find nearest named color for an RGB triple. Returns (name, hex, distanceSquared).</summary>
        public static (string Name, string Hex, int DistSq) GetNearestName(byte r, byte g, byte b)
        {
            EnsureCache();
            string bestName = null; int bestColor = 0; int bestDist = int.MaxValue;
            foreach (var (Name, Color, R, G, B) in _cache)
            {
                int dr = R - r; int dg = G - g; int db = B - b; int d = dr*dr + dg*dg + db*db;
                if (d < bestDist) { bestDist = d; bestName = Name; bestColor = Color; }
            }
            return (bestName, $"#{bestColor:X6}", bestDist);
        }

        /// <summary>Find nearest name by spelling distance (Levenshtein) for fuzzy queries.</summary>
        public static string GetNearestNameBySpelling(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            query = query.Trim();
            int best = int.MaxValue; string bestName = null;
            foreach (var name in _nameToColor.Keys)
            {
                int d = Levenshtein(query, name);
                if (d < best) { best = d; bestName = name; }
            }
            if (best <= Math.Max(2, query.Length/2)) return bestName; // threshold
            return null;
        }

        private static int Levenshtein(string a, string b)
        {
            int n = a.Length, m = b.Length;
            var dp = new int[n+1, m+1];
            for (int i=0;i<=n;i++) dp[i,0]=i;
            for (int j=0;j<=m;j++) dp[0,j]=j;
            for (int i=1;i<=n;i++)
            {
                for (int j=1;j<=m;j++)
                {
                    int cost = a[i-1]==b[j-1]?0:1;
                    dp[i,j] = Math.Min(Math.Min(dp[i-1,j]+1, dp[i,j-1]+1), dp[i-1,j-1]+cost);
                }
            }
            return dp[n,m];
        }

        /// <summary>Enumerate all names.</summary>
        public static IEnumerable<string> Names => _nameToColor.Keys;
    }
}
