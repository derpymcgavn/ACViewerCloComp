using System;
using System.Threading;
using System.Threading.Tasks;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACViewer.Enum;

namespace ACViewer.Services
{
    public interface IAssetRepository
    {
        T Get<T>(uint id, DatType datType = DatType.Portal) where T : FileType, new();
        bool TryGet<T>(uint id, out T asset, DatType datType = DatType.Portal) where T : FileType, new();
        Task<T> GetAsync<T>(uint id, DatType datType = DatType.Portal, CancellationToken ct = default) where T : FileType, new();
    }
}
