using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;

namespace ACViewer.Model
{
    public class PaletteChanges: IEquatable<PaletteChanges>
    {
        public List<CloSubPalette> CloSubPalettes { get; set; }   // from ClothingTable.ClothingSubPalEffects[PaletteTemplate]

        public List<uint> PaletteIds { get; set; }   // for each CloSubPalette.PaletteSet, from PaletteSet.GetPaletteID(shade)

        private float _shadeUsed; // shade value used to resolve PaletteIds (for possible recompute)

        public PaletteChanges(List<CloSubPalette> subPalettes, float shade = 0.0f)
        {
            _shadeUsed = shade;
            CloSubPalettes = MergeSubPalettes(subPalettes);
            PaletteIds = GetPaletteIDs(CloSubPalettes, shade);
            Validate();
        }

        public void Add(List<CloSubPalette> subPalettes, float shade = 0.0f)
        {
            // Merge new sub palettes with existing, then rebuild palette ids in one pass
            var combined = new List<CloSubPalette>(CloSubPalettes);
            combined.AddRange(subPalettes);
            CloSubPalettes = MergeSubPalettes(combined);
            _shadeUsed = shade; // treat most recent shade as authoritative
            PaletteIds = GetPaletteIDs(CloSubPalettes, shade);
            Validate();
        }

        /// <summary>
        /// Re-resolve palette IDs for current Shade (e.g. if hue / shade slider changes post construction).
        /// </summary>
        public void RecomputePaletteIds(float newShade)
        {
            _shadeUsed = newShade;
            PaletteIds = GetPaletteIDs(CloSubPalettes, newShade);
            Validate();
        }

        /// <summary>
        /// Merge CloSubPalette entries sharing the same PaletteSet by coalescing overlapping / contiguous ranges.
        /// </summary>
        private static List<CloSubPalette> MergeSubPalettes(IEnumerable<CloSubPalette> subPalettes)
        {
            if (subPalettes == null)
                return new List<CloSubPalette>();

            var result = new List<CloSubPalette>();

            foreach (var grp in subPalettes.GroupBy(p => p.PaletteSet))
            {
                // Flatten all ranges for this palette set
                var ranges = grp.SelectMany(p => p.Ranges)
                                 .Select(r => new { r.Offset, r.NumColors })
                                 .OrderBy(r => r.Offset)
                                 .ToList();

                var merged = new List<(uint off, uint len)>();
                foreach (var r in ranges)
                {
                    if (r.NumColors == 0) continue;
                    if (merged.Count == 0)
                    {
                        merged.Add((r.Offset, r.NumColors));
                        continue;
                    }
                    var last = merged[merged.Count - 1];
                    var lastEnd = last.off + last.len;
                    var curEnd = r.Offset + r.NumColors;

                    // Overlap or contiguous: merge
                    if (r.Offset <= lastEnd)
                    {
                        if (curEnd > lastEnd)
                            merged[merged.Count - 1] = (last.off, curEnd - last.off);
                    }
                    else if (r.Offset == lastEnd) // explicit contiguous (already covered by <=, but kept for clarity)
                    {
                        merged[merged.Count - 1] = (last.off, last.len + r.NumColors);
                    }
                    else
                    {
                        merged.Add((r.Offset, r.NumColors));
                    }
                }

                var newSub = new CloSubPalette { PaletteSet = grp.Key };
                foreach (var (off, len) in merged)
                {
                    var nr = new CloSubPaletteRange { Offset = off, NumColors = len };
                    newSub.Ranges.Add(nr);
                }
                result.Add(newSub);
            }
            return result;
        }

        public List<uint> GetPaletteIDs(List<CloSubPalette> subPalettes, float shade = 0.0f)
        {
            var paletteIDs = new List<uint>();
            if (subPalettes == null) return paletteIDs;

            foreach (var subpalette in subPalettes)
            {
                if (subpalette.PaletteSet >> 24 == 0xF)
                {
                    try
                    {
                        var paletteSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(subpalette.PaletteSet);
                        var paletteId = paletteSet.GetPaletteID(shade);
                        paletteIDs.Add(paletteId);
                    }
                    catch
                    {
                        paletteIDs.Add(0); // fallback placeholder
                    }
                }
                else
                {
                    paletteIDs.Add(subpalette.PaletteSet);
                }
            }
            return paletteIDs;
        }

        /// <summary>
        /// Debug validation – only active in DEBUG builds.
        /// </summary>
        [Conditional("DEBUG")]
        private void Validate()
        {
            if (CloSubPalettes == null) return;

            if (PaletteIds == null || CloSubPalettes.Count != PaletteIds.Count)
            {
                Debug.WriteLine($"[PaletteChanges] Mismatch: CloSubPalettes={CloSubPalettes?.Count} PaletteIds={PaletteIds?.Count}");
            }

            var count = Math.Min(CloSubPalettes.Count, PaletteIds.Count);
            for (int i = 0; i < count; i++)
            {
                var palId = PaletteIds[i];
                if (palId == 0) continue;
                try
                {
                    var palette = DatManager.PortalDat.ReadFromDat<Palette>(palId);
                    if (palette?.Colors == null) continue;
                    var colorCount = palette.Colors.Count;
                    foreach (var range in CloSubPalettes[i].Ranges)
                    {
                        var end = range.Offset + range.NumColors;
                        if (end > colorCount)
                        {
                            Debug.WriteLine($"[PaletteChanges] Range exceeds palette size: PaletteId=0x{palId:X8} RangeOffset={range.Offset} RangeLen={range.NumColors} PaletteColors={colorCount}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PaletteChanges] Failed to read palette 0x{palId:X8}: {ex.Message}");
                }
            }
        }

        public bool Equals(PaletteChanges paletteChanges)
        {
            if (paletteChanges == null) return false;

            if (CloSubPalettes.Count != paletteChanges.CloSubPalettes.Count)
                return false;

            for (var i = 0; i < CloSubPalettes.Count; i++)
            {
                var a = CloSubPalettes[i];
                var b = paletteChanges.CloSubPalettes[i];

                if (a.PaletteSet != b.PaletteSet)
                    return false;

                if (a.Ranges.Count != b.Ranges.Count)
                    return false;

                for (var j = 0; j < a.Ranges.Count; j++)
                {
                    var ar = a.Ranges[j];
                    var br = b.Ranges[j];

                    if (ar.NumColors != br.NumColors || ar.Offset != br.Offset)
                        return false;
                }
            }

            if (PaletteIds.Count != paletteChanges.PaletteIds.Count)
                return false;

            for (var i = 0; i < PaletteIds.Count; i++)
            {
                if (PaletteIds[i] != paletteChanges.PaletteIds[i])
                    return false;
            }
            
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 0;

            foreach (var cloSubPalette in CloSubPalettes)
            {
                hash = (hash * 397) ^ cloSubPalette.PaletteSet.GetHashCode();

                foreach (var range in cloSubPalette.Ranges)
                {
                    hash = (hash * 397) ^ range.Offset.GetHashCode();
                    hash = (hash * 397) ^ range.NumColors.GetHashCode();
                }
            }

            foreach (var paletteID in PaletteIds)
                hash = (hash * 397) ^ paletteID.GetHashCode();

            return hash;
        }
    }
}
