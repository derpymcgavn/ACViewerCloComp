using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACViewer.Services;

namespace ACViewer.CustomPalettes
{
    public sealed class TexturePaletteAnalysis
    {
        public uint SurfaceTextureId { get; init; }
        public bool IsIndexed { get; init; }
        public IReadOnlyList<uint> TextureIds { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<uint> DefaultPaletteIds { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<RangeDef> UsedRanges { get; init; } = Array.Empty<RangeDef>();
        public IReadOnlyList<TexturePaletteSuggestion> Suggestions { get; init; } = Array.Empty<TexturePaletteSuggestion>();
        public string Summary { get; init; }
    }

    public sealed class TexturePaletteSuggestion
    {
        public uint PaletteId { get; init; }
        public float Shade { get; init; }
        public IReadOnlyList<RangeDef> Ranges { get; init; } = Array.Empty<RangeDef>();
        public string Reason { get; init; }
        public string DisplayName => $"0x{PaletteId:X8}  {Reason}";

        public CustomPaletteDefinition ToDefinition(uint textureId)
        {
            return new CustomPaletteDefinition
            {
                Name = $"Texture_{textureId:X8}",
                Shade = Shade,
                Entries = new List<CustomPaletteEntry>
                {
                    new()
                    {
                        PaletteSetId = PaletteId,
                        Ranges = Ranges.Select(range => new RangeDef { Offset = range.Offset, Length = range.Length }).ToList()
                    }
                }
            };
        }
    }

    public static class TexturePaletteSuggestionService
    {
        private static readonly object IndexLock = new();
        private static Dictionary<uint, List<(uint SetId, int Index, int Count)>> _setsByPalette;

        public static TexturePaletteAnalysis Analyze(uint id, ClothingTable clothing)
        {
            if (DatManager.PortalDat == null)
                return new TexturePaletteAnalysis { SurfaceTextureId = id, Summary = "DAT files are not loaded." };

            var textureIds = ResolveTextureIds(id);
            var textures = new List<Texture>();
            foreach (var textureId in textureIds)
            {
                try
                {
                    var texture = DatManager.PortalDat.ReadFromDat<Texture>(textureId);
                    if (texture != null) textures.Add(texture);
                }
                catch { }
            }

            var indexed = textures
                .Where(texture => texture.DefaultPaletteId.HasValue &&
                                  (texture.Format == SurfacePixelFormat.PFID_INDEX16 || texture.Format == SurfacePixelFormat.PFID_P8))
                .ToList();
            if (indexed.Count == 0)
            {
                return new TexturePaletteAnalysis
                {
                    SurfaceTextureId = id,
                    TextureIds = textureIds,
                    Summary = "This texture is true-color; it has no linked DAT palette to recolor."
                };
            }

            var defaultPalettes = indexed.Select(texture => texture.DefaultPaletteId.Value).Distinct().ToList();
            var usedGroups = new SortedSet<uint>();
            foreach (var texture in indexed)
                AddUsedGroups(texture, usedGroups);
            var ranges = CoalesceGroups(usedGroups);
            if (ranges.Count == 0)
                ranges.Add(new RangeDef { Offset = 0, Length = 1 });

            EnsurePaletteSetIndex();
            var suggestions = new List<TexturePaletteSuggestion>();
            foreach (var paletteId in defaultPalettes)
            {
                suggestions.Add(new TexturePaletteSuggestion
                {
                    PaletteId = paletteId,
                    Ranges = CloneRanges(ranges),
                    Reason = "texture default palette"
                });

                if (_setsByPalette.TryGetValue(paletteId, out var sets))
                    foreach (var set in sets.Take(12))
                    {
                        var shade = set.Count <= 1 ? 0f : (float)set.Index / (set.Count - 1);
                        suggestions.Add(new TexturePaletteSuggestion
                        {
                            PaletteId = set.SetId,
                            Shade = shade,
                            Ranges = CloneRanges(ranges),
                            Reason = $"linked shade set ({set.Index + 1}/{set.Count})"
                        });
                    }
            }

            if (clothing?.ClothingSubPalEffects != null)
            {
                foreach (var effect in clothing.ClothingSubPalEffects.OrderBy(pair => pair.Key))
                {
                    var templateName = System.Enum.IsDefined(typeof(PaletteTemplate), (int)effect.Key)
                        ? ((PaletteTemplate)effect.Key).ToString()
                        : $"template {effect.Key}";
                    foreach (var subPalette in effect.Value.CloSubPalettes)
                    {
                        var clothingRanges = subPalette.Ranges.Count == 0
                            ? CloneRanges(ranges)
                            : subPalette.Ranges
                                .Where(range => range.NumColors > 0)
                                .Select(range => new RangeDef
                                {
                                    Offset = range.Offset / 8,
                                    Length = Math.Max(1, range.NumColors / 8)
                                })
                                .ToList();
                        suggestions.Add(new TexturePaletteSuggestion
                        {
                            PaletteId = subPalette.PaletteSet,
                            Ranges = clothingRanges,
                            Reason = $"used by this clothing: {templateName}"
                        });
                    }
                }
            }

            suggestions = suggestions
                .Where(suggestion => suggestion.PaletteId != 0 && suggestion.Ranges.Count > 0)
                .GroupBy(suggestion => (suggestion.PaletteId, RangeKey(suggestion.Ranges)))
                .Select(group => group.First())
                .Take(32)
                .ToList();

            var defaults = string.Join(", ", defaultPalettes.Select(palette => $"0x{palette:X8}"));
            var used = string.Join(", ", ranges.Select(range => $"{range.Offset}:{range.Length}"));
            return new TexturePaletteAnalysis
            {
                SurfaceTextureId = id,
                IsIndexed = true,
                TextureIds = textureIds,
                DefaultPaletteIds = defaultPalettes,
                UsedRanges = ranges,
                Suggestions = suggestions,
                Summary = $"Indexed | default {defaults} | used groups {used}"
            };
        }

        private static List<uint> ResolveTextureIds(uint id)
        {
            try
            {
                switch (id >> 24)
                {
                    case 0x05:
                        return DatManager.PortalDat.ReadFromDat<SurfaceTexture>(id)?.Textures?.Distinct().ToList() ?? new();
                    case 0x06:
                        return new List<uint> { id };
                    case 0x08:
                        var surface = DatManager.PortalDat.ReadFromDat<Surface>(id);
                        if (surface?.OrigTextureId == 0) return new();
                        return ResolveTextureIds(surface.OrigTextureId);
                    default:
                        return new();
                }
            }
            catch { return new(); }
        }

        private static void AddUsedGroups(Texture texture, ISet<uint> groups)
        {
            if (texture?.SourceData == null) return;
            if (texture.Format == SurfacePixelFormat.PFID_P8)
            {
                foreach (var index in texture.SourceData)
                    groups.Add((uint)index / 8);
                return;
            }
            if (texture.Format != SurfacePixelFormat.PFID_INDEX16) return;
            using var reader = new BinaryReader(new MemoryStream(texture.SourceData));
            while (reader.BaseStream.Position + sizeof(ushort) <= reader.BaseStream.Length)
                groups.Add(reader.ReadUInt16() / 8u);
        }

        private static List<RangeDef> CoalesceGroups(IEnumerable<uint> groups)
        {
            var result = new List<RangeDef>();
            foreach (var group in groups)
            {
                if (result.Count > 0)
                {
                    var previous = result[^1];
                    if (previous.Offset + previous.Length == group)
                    {
                        previous.Length++;
                        continue;
                    }
                }
                result.Add(new RangeDef { Offset = group, Length = 1 });
            }
            return result;
        }

        private static List<RangeDef> CloneRanges(IEnumerable<RangeDef> ranges) =>
            ranges.Select(range => new RangeDef { Offset = range.Offset, Length = range.Length }).ToList();

        private static string RangeKey(IEnumerable<RangeDef> ranges) =>
            string.Join(",", ranges.Select(range => $"{range.Offset}:{range.Length}"));

        private static void EnsurePaletteSetIndex()
        {
            if (_setsByPalette != null) return;
            lock (IndexLock)
            {
                if (_setsByPalette != null) return;
                _setsByPalette = DatIdIndex.PaletteSetsByPalette()
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }
    }
}
