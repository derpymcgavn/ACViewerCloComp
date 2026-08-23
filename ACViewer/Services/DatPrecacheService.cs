using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;

namespace ACViewer.Services
{
    public sealed class DatPrecacheProgress
    {
        public DatPrecacheProgress(string message, int loaded, int total)
        {
            Message = message;
            Loaded = loaded;
            Total = total;
        }

        public string Message { get; }
        public int Loaded { get; }
        public int Total { get; }
        public int Percent => Total <= 0 ? 0 : Math.Clamp((int)Math.Round(Loaded * 100.0 / Total), 0, 100);
    }

    /// <summary>
    /// Warms the DAT objects that the clothing studio repeatedly touches while
    /// switching clothing IDs. This avoids rebuilding broad indexes or doing the
    /// first expensive DAT decode on the UI thread.
    /// </summary>
    public static class DatPrecacheService
    {
        public static bool IsRunning { get; private set; }

        public static Task WarmClothingStudioAsync(
            IProgress<DatPrecacheProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (IsRunning)
            {
                progress?.Report(new DatPrecacheProgress("DAT precache is already running.", 0, 0));
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                IsRunning = true;
                var sw = Stopwatch.StartNew();
                try
                {
                    var portal = DatManager.PortalDat;
                    if (portal == null)
                    {
                        progress?.Report(new DatPrecacheProgress("Portal DAT is not loaded yet.", 0, 0));
                        return;
                    }

                    progress?.Report(new DatPrecacheProgress("Building DAT id index...", 0, 0));
                    var clothingIds = DatIdIndex.PortalIdsByType(0x10).ToList();
                    var setupIds = DatIdIndex.PortalIdsByType(0x02).ToList();
                    var modelIds = DatIdIndex.PortalIdsByType(0x01).ToList();
                    var surfaceTextureIds = DatIdIndex.SurfaceTextureIds().ToList();

                    // Palette set index is used by palette suggestions; building it once prevents
                    // repeated full PaletteSet scans while clicking between clothing entries.
                    _ = DatIdIndex.PaletteSetsByPalette();

                    var clothingSetupIds = new HashSet<uint>();
                    var clothingModelIds = new HashSet<uint>();
                    var referencedSurfaceIds = new HashSet<uint>();
                    var referencedSurfaceTextureIds = new HashSet<uint>();
                    var loaded = 0;
                    var total = clothingIds.Count + setupIds.Count + Math.Min(surfaceTextureIds.Count, 2048);

                    foreach (var id in clothingIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryRead<ClothingTable>(portal, id, clothing =>
                        {
                            foreach (var setupId in clothing.ClothingBaseEffects.Keys)
                                clothingSetupIds.Add(setupId);

                            foreach (var baseEffect in clothing.ClothingBaseEffects.Values)
                                foreach (var obj in baseEffect.CloObjectEffects)
                                {
                                    if (obj.ModelId != 0)
                                        clothingModelIds.Add(obj.ModelId);
                                    foreach (var tex in obj.CloTextureEffects)
                                    {
                                        if (tex.OldTexture != 0)
                                            referencedSurfaceTextureIds.Add(tex.OldTexture);
                                        if (tex.NewTexture != 0)
                                            referencedSurfaceTextureIds.Add(tex.NewTexture);
                                    }
                                }
                        });

                        ReportEvery(progress, ++loaded, total, "Warming clothing tables...");
                    }

                    foreach (var setupId in clothingSetupIds.Concat(setupIds.Take(512)).Distinct())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryRead<SetupModel>(portal, setupId, setup =>
                        {
                            if (setup.Parts != null)
                                foreach (var modelId in setup.Parts)
                                    if (modelId != 0)
                                        clothingModelIds.Add(modelId);
                        });
                        ReportEvery(progress, ++loaded, total, "Warming setup/model references...");
                    }

                    foreach (var modelId in clothingModelIds.Concat(modelIds.Take(1024)).Distinct())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryRead<GfxObj>(portal, modelId, gfx =>
                        {
                            if (gfx.Surfaces != null)
                                foreach (var surfaceId in gfx.Surfaces)
                                    if (surfaceId != 0)
                                        referencedSurfaceIds.Add(surfaceId);
                        });
                    }

                    foreach (var surfaceId in referencedSurfaceIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryRead<Surface>(portal, surfaceId, surface =>
                        {
                            if (surface.OrigTextureId != 0)
                                referencedSurfaceTextureIds.Add(surface.OrigTextureId);
                        });
                    }

                    foreach (var stId in referencedSurfaceTextureIds.Concat(surfaceTextureIds.Take(2048)).Distinct())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryRead<SurfaceTexture>(portal, stId, _ => { });
                        ReportEvery(progress, ++loaded, total, "Warming surface texture records...");
                    }

                    progress?.Report(new DatPrecacheProgress(
                        $"DAT precache complete in {sw.Elapsed.TotalSeconds:0.0}s. Warmed {clothingIds.Count:N0} clothing tables, {clothingSetupIds.Count:N0} clothing setups, {clothingModelIds.Count:N0} models.",
                        total,
                        total));
                }
                catch (OperationCanceledException)
                {
                    progress?.Report(new DatPrecacheProgress("DAT precache canceled.", 0, 0));
                }
                finally
                {
                    IsRunning = false;
                }
            }, cancellationToken);
        }

        private static void TryRead<T>(DatDatabase portal, uint id, Action<T> onRead = null)
            where T : FileType, new()
        {
            try
            {
                var value = portal.ReadFromDat<T>(id);
                if (value != null)
                    onRead?.Invoke(value);
            }
            catch
            {
                // Bad ids are expected in modded / partial DAT environments; precache should never
                // block actual editing because one optional asset cannot be decoded.
            }
        }

        private static void ReportEvery(IProgress<DatPrecacheProgress> progress, int loaded, int total, string message)
        {
            if (progress == null) return;
            if (loaded == total || loaded % 250 == 0)
                progress.Report(new DatPrecacheProgress(message, loaded, total));
        }
    }
}
