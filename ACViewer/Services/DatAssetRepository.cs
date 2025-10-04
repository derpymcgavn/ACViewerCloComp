using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACViewer.Enum;

namespace ACViewer.Services
{
    /// <summary>
    /// Default implementation backed directly by DatManager.*Dat databases.
    /// Provides a small in-memory cache for already decoded assets.
    /// </summary>
    public sealed class DatAssetRepository : IAssetRepository
    {
        private readonly ConcurrentDictionary<(uint id, DatType type, System.Type t), object> _cache = new();
        private readonly int _maxEntries;

        public DatAssetRepository(int maxEntries = 4096)
        {
            _maxEntries = maxEntries;
        }

        public T Get<T>(uint id, DatType datType = DatType.Portal) where T : FileType, new()
        {
            if (TryGet(id, out T existing, datType)) return existing;
            var db = ResolveDatabase(datType);
            if (db == null) return null;
            var asset = db.ReadFromDat<T>(id);
            AddToCache(id, datType, asset);
            return asset;
        }

        public bool TryGet<T>(uint id, out T asset, DatType datType = DatType.Portal) where T : FileType, new()
        {
            if (_cache.TryGetValue((id, datType, typeof(T)), out var obj) && obj is T typed)
            {
                asset = typed; return true;
            }
            asset = null; return false;
        }

        public Task<T> GetAsync<T>(uint id, DatType datType = DatType.Portal, CancellationToken ct = default) where T : FileType, new()
        {
            return Task.Run(() => Get<T>(id, datType), ct);
        }

        private DatDatabase ResolveDatabase(DatType type) => type switch
        {
            DatType.Cell => DatManager.CellDat,
            DatType.Portal => DatManager.PortalDat,
            DatType.HighRes => DatManager.HighResDat,
            DatType.Language => DatManager.LanguageDat,
            _ => DatManager.PortalDat
        };

        private void AddToCache(uint id, DatType datType, FileType asset)
        {
            if (asset == null) return;
            if (_cache.Count > _maxEntries)
            {
                // naive trimming (remove 10%)
                int remove = _maxEntries / 10;
                foreach (var key in _cache.Keys)
                {
                    if (remove-- <= 0) break;
                    _cache.TryRemove(key, out _);
                }
            }
            _cache[(id, datType, asset.GetType())] = asset;
        }
    }
}
