using System.Collections.Generic;
using System.Globalization;
using ACE.DatLoader.Entity;

namespace ACViewer.CustomPalettes
{
    public class CustomPaletteDefinition
    {
        public string Name { get; set; }
        public bool Multi { get; set; }
        public float Shade { get; set; }
        public List<CustomPaletteEntry> Entries { get; set; } = new();
    }

    public class CustomPaletteEntry
    {
        public uint PaletteSetId { get; set; }
        public List<RangeDef> Ranges { get; set; } = new();
    }

    public class RangeDef
    {
        public uint Offset { get; set; }   // logical group index (group = 8 colors)
        public uint Length { get; set; }   // number of groups
    }

    public static class RangeParser
    {
        // Existing API (strict)
        public static List<RangeDef> ParseRanges(string input) => ParseRanges(input, tolerant: false, out _);

        // New overload: tolerant parsing for live editing scenarios
        public static List<RangeDef> ParseRanges(string input, bool tolerant, out List<string> errors)
        {
            errors = new List<string>();
            var result = new List<RangeDef>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            var tokens = input.Replace("\r", "").Replace("\n", " ")
                .Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                var t = raw.Trim(); if (t.Length == 0) continue;
                if (!TryParseRangeToken(t, out var rd, out var err))
                {
                    if (tolerant)
                    {
                        if (!string.IsNullOrEmpty(err)) errors.Add(err);
                        continue; // skip malformed token in tolerant mode
                    }
                    // strict: throw immediately
                    throw new System.FormatException(err ?? ($"Bad range token '{t}'"));
                }
                result.Add(rd);
            }
            return result;
        }

        private static bool TryParseRangeToken(string token, out RangeDef range, out string error)
        {
            range = null; error = null;
            var parts = token.Split(':');
            if (parts.Length != 2)
            { error = $"Bad range token '{token}' (expected off:len)"; return false; }
            var offPart = parts[0].Trim(); var lenPart = parts[1].Trim();
            if (offPart.Length == 0 || lenPart.Length == 0)
            { error = $"Incomplete range token '{token}'"; return false; }
            if (!uint.TryParse(offPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var off))
            { error = $"Invalid offset in token '{token}'"; return false; }
            if (!uint.TryParse(lenPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var len))
            { error = $"Invalid length in token '{token}'"; return false; }
            range = new RangeDef { Offset = off, Length = len }; return true;
        }
    }

    public static class CustomPaletteFactory
    {
        // Each logical group = 8 colors (matches existing highlighting logic & engine expectations)
        private const int GroupSize = 8;
        public static List<CloSubPalette> Build(CustomPaletteDefinition def)
        {
            var list = new List<CloSubPalette>();
            foreach (var entry in def.Entries)
            {
                var sub = new CloSubPalette { PaletteSet = entry.PaletteSetId };
                foreach (var r in entry.Ranges)
                {
                    // Convert logical groups to actual color offsets / lengths
                    var cr = new CloSubPaletteRange { Offset = r.Offset * GroupSize, NumColors = r.Length * GroupSize };
                    sub.Ranges.Add(cr);
                }
                if (sub.Ranges.Count > 0)
                    list.Add(sub);
            }
            return list;
        }
    }
}
