using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ACViewer.CustomPalettes;
using ACE.DatLoader.FileTypes;

namespace ACViewer.View.Controls
{
    /// <summary>
    /// Interactive palette range editor.
    /// Logical grouping: 1 group = 8 colors. Ranges work in groups (Offset, Length).
    /// Shift + drag erases ranges covered by the drag selection (red overlay for existing ranges).
    /// Shows hover indicator and status badges. Supports Undo/Redo (Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z).
    /// </summary>
    public class RangeEditorControl : FrameworkElement
    {
        private List<uint> _paletteColors = new();
        private readonly List<RangeDef> _ranges = new();
        public event EventHandler<IReadOnlyList<RangeDef>> RangesChanged;

        private WriteableBitmap _paletteBitmap;
        private bool _needsPaletteRedraw = true;

        private const int TargetWidth = 512;
        private const int TargetHeight = 40;
        private const int MaxHistory = 50; // added constant for undo history limit
        // Undo/Redo stacks (each snapshot is a list clone of ranges)
        private readonly Stack<List<RangeDef>> _undoStack = new();
        private readonly Stack<List<RangeDef>> _redoStack = new();
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public event EventHandler HistoryChanged;
        // Drag / interaction state
        private bool _dragging;
        private bool _eraseMode; // true if shift held at drag start
        private int _dragStartGroup;
        private RangeDef _previewRange; // transient during drag
        private int? _hoverGroup;       // current hover group (null if none)

        private int TotalGroups => _paletteColors.Count == 0 ? 0 : (int)Math.Ceiling(_paletteColors.Count / 8.0);

        public RangeEditorControl()
        {
            Focusable = true;
            SnapsToDevicePixels = true;
            ClipToBounds = true;
        }

        #region Public API
        public void SetPalette(Palette palette)
        {
            _paletteColors = (palette?.Colors?.Count ?? 0) == 0 ? new List<uint>() : palette.Colors.ToList();
            _needsPaletteRedraw = true;
            InvalidateVisual();
        }

        public void SetRanges(IEnumerable<RangeDef> ranges)
        {
            _ranges.Clear();
            if (ranges != null)
            {
                foreach (var r in ranges)
                {
                    if (r.Length == 0) continue; _ranges.Add(new RangeDef { Offset = r.Offset, Length = r.Length });
                }
            }
            ClearHistory(); // external set resets history context
            NormalizeRanges();
            InvalidateVisual();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(CloneRanges(_ranges));
            var prev = _undoStack.Pop();
            _ranges.Clear(); _ranges.AddRange(prev.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length }));
            InvalidateVisual(); RaiseRangesChanged(); OnHistoryChanged();
        }
        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(CloneRanges(_ranges));
            var next = _redoStack.Pop();
            _ranges.Clear(); _ranges.AddRange(next.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length }));
            InvalidateVisual(); RaiseRangesChanged(); OnHistoryChanged();
        }
        #endregion

        #region History
        private void SnapshotForUndo()
        {
            // push current before mutating
            _undoStack.Push(CloneRanges(_ranges));
            if (_undoStack.Count > MaxHistory)
            {
                // trim oldest (convert to list then back skipping bottom) - simplest rebuild
                var temp = _undoStack.ToArray(); // top-first order
                Array.Reverse(temp); // oldest first
                var trimmed = temp.Skip(1).ToList();
                _undoStack.Clear();
                foreach (var s in trimmed) _undoStack.Push(s);
            }
            _redoStack.Clear();
            OnHistoryChanged();
        }
        private static List<RangeDef> CloneRanges(IEnumerable<RangeDef> src) => src.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length }).ToList();
        private void ClearHistory() { _undoStack.Clear(); _redoStack.Clear(); OnHistoryChanged(); }
        private void OnHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);
        #endregion

        #region Rendering
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Brushes.Black, null, rect);

            if (_paletteColors.Count == 0)
            {
                var ftEmpty = new FormattedText("(no palette)", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ftEmpty, new Point(6, 6));
                return;
            }

            if (_paletteBitmap == null || _needsPaletteRedraw)
                BuildPaletteBitmap();
            if (_paletteBitmap != null)
                dc.DrawImage(_paletteBitmap, rect);

            // Grid lines
            var totalGroups = TotalGroups;
            if (totalGroups > 0)
            {
                double groupWidth = rect.Width / totalGroups;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
                for (int g = 1; g < totalGroups; g++)
                    dc.DrawLine(pen, new Point(g * groupWidth, 0), new Point(g * groupWidth, rect.Height));
            }

            foreach (var r in _ranges)
                DrawRange(dc, r, Colors.Red, 0.40);

            if (_dragging && _previewRange != null)
            {
                var color = _eraseMode ? Color.FromRgb(200, 40, 40) : Colors.Gold;
                DrawRange(dc, _previewRange, color, _eraseMode ? 0.30 : 0.45, dashed: true);
            }

            if (!_dragging && _hoverGroup.HasValue && totalGroups > 0)
            {
                double groupWidth = rect.Width / totalGroups; double x = _hoverGroup.Value * groupWidth;
                var hoverPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1.0);
                dc.DrawLine(hoverPen, new Point(x + 0.5, 0), new Point(x + 0.5, rect.Height));
                string hoverText = $"Grp {_hoverGroup.Value} (off={_hoverGroup.Value}, colors {Math.Min(8 * (_hoverGroup.Value + 1), _paletteColors.Count)} / {_paletteColors.Count})";
                DrawStatusBadge(dc, hoverText, new Point(Math.Min(x + 4, rect.Width - 140), 4));
            }

            if (_dragging && _previewRange != null)
            {
                var off = _previewRange.Offset; var len = _previewRange.Length; var colors = len * 8;
                string status = _eraseMode ? $"Erase: off {off} len {len} ({colors})" : $"Add: off {off} len {len} ({colors})";
                DrawStatusBadge(dc, status, new Point(6, 4));
            }
        }

        private void DrawStatusBadge(DrawingContext dc, string text, Point origin)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var padding = new Size(6, 3);
            var rect = new Rect(origin, new Size(ft.Width + padding.Width * 2, ft.Height + padding.Height * 2));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1), rect, 4, 4);
            dc.DrawText(ft, new Point(rect.X + padding.Width, rect.Y + padding.Height - 1));
        }

        private void DrawRange(DrawingContext dc, RangeDef r, Color color, double fillAlpha, bool dashed = false)
        {
            int totalGroups = TotalGroups; if (totalGroups == 0) return;
            double groupWidth = ActualWidth / totalGroups; double x = r.Offset * groupWidth; double w = r.Length * groupWidth; if (w <= 0) return;
            var fill = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(fillAlpha, 0, 1) * 255), color.R, color.G, color.B));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)), 1.5); if (dashed) pen.DashStyle = DashStyles.Dash;
            dc.DrawRectangle(fill, pen, new Rect(x, 0, w, ActualHeight));
        }

        private void BuildPaletteBitmap()
        {
            try
            {
                _paletteBitmap = new WriteableBitmap(TargetWidth, TargetHeight, 96, 96, PixelFormats.Bgra32, null);
                int width = TargetWidth; int height = TargetHeight; int colors = _paletteColors.Count; byte[] pixels = new byte[width * height * 4];
                for (int x = 0; x < width; x++)
                {
                    int palIdx = (int)((double)x / width * (colors - 1)); if (palIdx < 0) palIdx = 0; if (palIdx >= colors) palIdx = colors - 1;
                    uint col = _paletteColors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF);
                    for (int y = 0; y < height; y++) { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; }
                }
                _paletteBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            }
            catch { _paletteBitmap = null; }
            _needsPaletteRedraw = false;
        }
        #endregion

        #region Interaction
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (_paletteColors.Count == 0) return;
            Focus(); CaptureMouse();
            _dragging = true; _eraseMode = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift; _dragStartGroup = GetGroupFromPoint(e.GetPosition(this).X); _previewRange = new RangeDef { Offset = (uint)_dragStartGroup, Length = 1 }; InvalidateVisual();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_paletteColors.Count > 0) _hoverGroup = GetGroupFromPoint(e.GetPosition(this).X); else _hoverGroup = null;
            if (_dragging && _previewRange != null)
            { int current = GetGroupFromPoint(e.GetPosition(this).X); int min = Math.Min(_dragStartGroup, current); int max = Math.Max(_dragStartGroup, current); _previewRange.Offset = (uint)min; _previewRange.Length = (uint)(max - min + 1); }
            InvalidateVisual();
        }
        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if (!_dragging) { _hoverGroup = null; InvalidateVisual(); } }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return; ReleaseMouseCapture(); _dragging = false;
            if (_previewRange != null && _previewRange.Length > 0)
            {
                SnapshotForUndo();
                if (_eraseMode) EraseRange(_previewRange.Offset, _previewRange.Length); else { _ranges.Add(new RangeDef { Offset = _previewRange.Offset, Length = _previewRange.Length }); NormalizeRanges(); RaiseRangesChanged(); }
                OnHistoryChanged();
            }
            _previewRange = null; InvalidateVisual();
        }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_ranges.Count == 0) return; int g = GetGroupFromPoint(e.GetPosition(this).X); var toRemove = _ranges.FirstOrDefault(r => g >= r.Offset && g < r.Offset + r.Length); if (toRemove != null) { SnapshotForUndo(); _ranges.Remove(toRemove); RaiseRangesChanged(); InvalidateVisual(); OnHistoryChanged(); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                { Redo(); e.Handled = true; }
                else if (e.Key == Key.Z)
                { Undo(); e.Handled = true; }
                else if (e.Key == Key.Y)
                { Redo(); e.Handled = true; }
            }
        }
        #endregion

        #region Logic
        private void EraseRange(uint selOffset, uint selLength)
        {
            if (selLength == 0 || _ranges.Count == 0) { RaiseRangesChanged(); return; }
            uint selEnd = selOffset + selLength; var updated = new List<RangeDef>();
            foreach (var r in _ranges)
            {
                uint rStart = r.Offset; uint rEnd = r.Offset + r.Length;
                if (selEnd <= rStart || selOffset >= rEnd) { updated.Add(r); continue; }
                if (selOffset <= rStart && selEnd >= rEnd) { continue; }
                if (selOffset <= rStart && selEnd < rEnd) { uint newOffset = selEnd; updated.Add(new RangeDef { Offset = newOffset, Length = rEnd - newOffset }); continue; }
                if (selOffset > rStart && selEnd >= rEnd) { updated.Add(new RangeDef { Offset = rStart, Length = selOffset - rStart }); continue; }
                if (selOffset > rStart && selEnd < rEnd) { uint leftLen = selOffset - rStart; uint rightOffset = selEnd; uint rightLen = rEnd - selEnd; if (leftLen > 0) updated.Add(new RangeDef { Offset = rStart, Length = leftLen }); if (rightLen > 0) updated.Add(new RangeDef { Offset = rightOffset, Length = rightLen }); }
            }
            _ranges.Clear(); _ranges.AddRange(updated.OrderBy(r => r.Offset)); NormalizeRanges(); RaiseRangesChanged();
        }

        private int GetGroupFromPoint(double x)
        { int total = TotalGroups; if (total <= 0) return 0; double clamped = Math.Max(0, Math.Min(ActualWidth - 1, x)); int g = (int)(clamped / ActualWidth * total); if (g < 0) g = 0; if (g >= total) g = total - 1; return g; }

        private void NormalizeRanges()
        { if (_ranges.Count <= 1) return; _ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset)); for (int i = 0; i < _ranges.Count - 1;) { var cur = _ranges[i]; var next = _ranges[i + 1]; if (next.Offset <= cur.Offset + cur.Length) { uint end = Math.Max(cur.Offset + cur.Length, next.Offset + next.Length); cur.Length = end - cur.Offset; _ranges.RemoveAt(i + 1); } else i++; } }

        private void RaiseRangesChanged() => RangesChanged?.Invoke(this, _ranges.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length }).ToList());
        #endregion

        protected override Size MeasureOverride(Size availableSize) => new Size(double.IsInfinity(availableSize.Width) ? 520 : availableSize.Width, 48);
    }
}
