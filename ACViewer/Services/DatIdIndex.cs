using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;

namespace ACViewer.Services
{
    /// <summary>
    /// Lightweight, lazy indexes over Portal DAT file ids.
    /// Keep the expensive AllFiles/type scans centralized so editor panels do not
    /// rebuild the same lists every time a clothing entry or tab refreshes.
    /// </summary>
    public static class DatIdIndex
    {
        private static readonly object Sync = new();
        private static DatDatabase _portalDat;
        private static Dictionary<byte, List<uint>> _portalIdsByType = new();
        private static readonly ConcurrentDictionary<uint, int> PaletteColorCounts = new();
        private static Dictionary<uint, List<(uint SetId, int Index, int Count)>> _paletteSetsByPalette;

        public static IReadOnlyList<uint> PortalIdsByType(byte type)
        {
            EnsurePortalIndex();
            return _portalIdsByType.TryGetValue(type, out var ids) ? ids : Array.Empty<uint>();
        }

        public static IReadOnlyList<uint> SurfaceTextureIds() => PortalIdsByType(0x05);

        public static IReadOnlyList<uint> ModelAndSetupIds()
        {
            EnsurePortalIndex();
            return PortalIdsByType(0x01).Concat(PortalIdsByType(0x02)).OrderBy(id => id).ToList();
        }

        public static IReadOnlyList<uint> PaletteAndPaletteSetIds()
        {
            EnsurePortalIndex();
            return PortalIdsByType(0x04).Concat(PortalIdsByType(0x0F)).OrderBy(id => id).ToList();
        }

        public static bool PaletteOrSetCovers(uint id, uint requiredColorCount)
        {
            if (requiredColorCount == 0) return true;

            try
            {
                var type = id >> 24;
                if (type == 0x04)
                    return GetPaletteColorCount(id) >= requiredColorCount;

                if (type != 0x0F) return false;
                var set = DatManager.PortalDat?.ReadFromDat<PaletteSet>(id);
                if (set?.PaletteList == null || set.PaletteList.Count == 0) return false;
                return set.PaletteList.Any(paletteId => GetPaletteColorCount(paletteId) >= requiredColorCount);
            }
            catch
            {
                return false;
            }
        }

        public static IReadOnlyDictionary<uint, List<(uint SetId, int Index, int Count)>> PaletteSetsByPalette()
        {
            EnsurePortalIndex();
            if (_paletteSetsByPalette != null) return _paletteSetsByPalette;

            lock (Sync)
            {
                EnsurePortalIndex();
                if (_paletteSetsByPalette != null) return _paletteSetsByPalette;

                var index = new Dictionary<uint, List<(uint SetId, int Index, int Count)>>();
                foreach (var setId in PortalIdsByType(0x0F))
                {
                    try
                    {
                        var set = DatManager.PortalDat?.ReadFromDat<PaletteSet>(setId);
                        if (set?.PaletteList == null) continue;
                        for (var i = 0; i < set.PaletteList.Count; i++)
                        {
                            var paletteId = set.PaletteList[i];
                            if (!index.TryGetValue(paletteId, out var entries))
                                index[paletteId] = entries = new List<(uint, int, int)>();
                            entries.Add((setId, i, set.PaletteList.Count));
                        }
                    }
                    catch { }
                }

                _paletteSetsByPalette = index;
                return _paletteSetsByPalette;
            }
        }

        private static int GetPaletteColorCount(uint paletteId)
        {
            return PaletteColorCounts.GetOrAdd(paletteId, id =>
            {
                try
                {
                    return DatManager.PortalDat?.ReadFromDat<Palette>(id)?.Colors?.Count ?? 0;
                }
                catch
                {
                    return 0;
                }
            });
        }

        private static void EnsurePortalIndex()
        {
            var portal = DatManager.PortalDat;
            if (portal == null)
            {
                lock (Sync)
                {
                    _portalDat = null;
                    _portalIdsByType = new Dictionary<byte, List<uint>>();
                    PaletteColorCounts.Clear();
                    _paletteSetsByPalette = null;
                }
                return;
            }

            if (ReferenceEquals(_portalDat, portal) && _portalIdsByType.Count > 0) return;

            lock (Sync)
            {
                if (ReferenceEquals(_portalDat, portal) && _portalIdsByType.Count > 0) return;

                _portalDat = portal;
                _portalIdsByType = portal.AllFiles.Keys
                    .GroupBy(id => (byte)(id >> 24))
                    .ToDictionary(group => group.Key, group => group.OrderBy(id => id).ToList());
                PaletteColorCounts.Clear();
                _paletteSetsByPalette = null;
            }
        }
    }
}
