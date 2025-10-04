using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text; // added for Encoding
using ACE.DatLoader;
using ACViewer.Enum;

namespace ACViewer.Services
{
    public sealed class DatLoadProgress
    {
        public string Stage { get; init; }
        public double Percent { get; init; }
        public string Message { get; init; }
    }

    /// <summary>
    /// Provides staged, cancellable asynchronous initialization of DAT files with progress callbacks.
    /// Wraps existing DatManager.Initialize while emitting synthetic stage progress events so UI can react.
    /// </summary>
    public sealed class DatInitializationService
    {
        private bool _initializing;
        public bool IsInitializing => _initializing;
        public event Action<DatLoadProgress> Progress;

        private void Report(string stage, double pct, string msg = null)
            => Progress?.Invoke(new DatLoadProgress { Stage = stage, Percent = pct, Message = msg ?? stage });

        public async Task<bool> InitializeAsync(string path, bool loadCellDat, CancellationToken ct = default)
        {
            if (_initializing) return false;
            _initializing = true;
            try
            {
                Report("Validate Path", 0);
                await Task.Delay(25, ct).ConfigureAwait(false);
                if (!Directory.Exists(path))
                {
                    Report("Error", 0, $"Directory not found: {path}");
                    return false;
                }

                // Stage: scan directory (lightweight)
                Report("Scan Directory", 5);
                var files = Directory.GetFiles(path, "*.dat", SearchOption.TopDirectoryOnly);
                int found = files.Length;
                Report("Scan Directory", 10, $"Found {found} dat files");

                // We emit synthetic staged progress (Portal/Cell/HighRes/Language) even though the loader runs monolithically.
                Report("Open Portal", 15);
                await Task.Delay(10, ct).ConfigureAwait(false);
                Report("Open Cell", 25);
                await Task.Delay(10, ct).ConfigureAwait(false);
                Report("Open HighRes", 35);
                await Task.Delay(5, ct).ConfigureAwait(false);
                Report("Open Language", 45);
                await Task.Delay(5, ct).ConfigureAwait(false);

                // Real initialize (single call) - may be lengthy
                Report("DatManager.Initialize", 50, "Loading DAT indices...");
                await Task.Run(() =>
                {
                    // Ensure legacy code pages (cp1252 etc.) are available
                    try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
                    DatManager.Initialize(path, true, loadCellDat);
                }, ct).ConfigureAwait(false);

                // Post-initial analysis / counts
                int portalCount = DatManager.PortalDat?.AllFiles?.Count ?? 0;
                int cellCount = DatManager.CellDat?.AllFiles?.Count ?? 0;
                int highResCount = DatManager.HighResDat?.AllFiles?.Count ?? 0;
                int languageCount = DatManager.LanguageDat?.AllFiles?.Count ?? 0;
                Report("Index Summary", 80, $"Portal:{portalCount:n0} Cell:{cellCount:n0} HighRes:{highResCount:n0} Lang:{languageCount:n0}");

                // Optional DID tables / auxiliary loads
                Report("Aux Tables", 85, "Loading DID tables...");
                try { Data.DIDTables.Load(); } catch { }
                Report("Finalize", 95);
                await Task.Delay(25, ct).ConfigureAwait(false);
                Report("Complete", 100, "DATs loaded");
                return true;
            }
            catch (OperationCanceledException)
            {
                Report("Canceled", 0, "Initialization canceled");
                return false;
            }
            catch (Exception ex)
            {
                Report("Error", 0, ex.Message);
                return false;
            }
            finally
            {
                _initializing = false;
            }
        }
    }
}
