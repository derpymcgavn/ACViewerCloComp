using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using ACViewer.View;

namespace ACViewer.Services
{
    /// <summary>
    /// Thread-safe status sink decoupling non-UI code from MainWindow.
    /// Batches messages and forwards to MainWindow on dispatcher.
    /// </summary>
    public sealed class UiStatusSink : IStatusSink, IDisposable
    {
        private readonly ConcurrentQueue<(DateTime ts, string msg, StatusSeverity sev)> _queue = new();
        private readonly Dispatcher _dispatcher;
        private readonly Timer _flushTimer;
        private volatile bool _flushing;
        private bool _disposed;
        private const int FlushIntervalMs = 250; // small cadence for batching
        private const int MaxBatch = 25;

        public UiStatusSink(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _flushTimer = new Timer(_ => TryFlush(), null, FlushIntervalMs, FlushIntervalMs);
        }

        public void Post(string message, StatusSeverity severity = StatusSeverity.Info)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _queue.Enqueue((DateTime.UtcNow, Format(message, severity), severity));
        }

        private static string Format(string msg, StatusSeverity sev)
        {
            return sev switch
            {
                StatusSeverity.Warning => "[WARN] " + msg,
                StatusSeverity.Error => "[ERROR] " + msg,
                StatusSeverity.Success => "[OK] " + msg,
                StatusSeverity.Debug => "[DBG] " + msg,
                _ => msg
            };
        }

        private void TryFlush()
        {
            if (_flushing || _queue.IsEmpty) return;
            _flushing = true;
            try
            {
                var batch = new System.Collections.Generic.List<(DateTime ts, string msg, StatusSeverity sev)>(MaxBatch);
                while (batch.Count < MaxBatch && _queue.TryDequeue(out var item))
                    batch.Add(item);
                if (batch.Count == 0) return;

                _dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (MainWindow.Instance == null) return;
                    foreach (var b in batch.OrderBy(b=>b.ts))
                        MainWindow.Instance.AddStatusText(b.msg);
                }), DispatcherPriority.Background);
            }
            finally
            {
                _flushing = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer?.Dispose();
        }
    }
}
