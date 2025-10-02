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
    /// </summary>
    public class RangeEditorControl : FrameworkElement
    {
        // Backing palette data (raw ARGB colors)
        private List<uint> _paletteColors = new();
        public IReadOnlyList<uint> PaletteColors => _paletteColors;

        // Ranges (logical groups)
        private readonly List<RangeDef> _ranges = new();
        public IEnumerable<RangeDef> Ranges => _ranges.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length });

        public event EventHandler<IReadOnlyList<RangeDef>> RangesChanged;

        private System.Windows.Media.Imaging.WriteableBitmap _paletteBitmap; // explicit namespace
        private bool _needsPaletteRedraw = true;

        private const int TargetWidth = 512;   // internal render surface; scaled to ActualWidth
        private const int TargetHeight = 40;

        // Drag state
        private bool _dragging;
        private int _dragStartGroup;
        private RangeDef _previewRange; // not yet committed

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
                    if (r.Length == 0) continue;
                    _ranges.Add(new RangeDef { Offset = r.Offset, Length = r.Length });
                }
            }
            NormalizeRanges();
            InvalidateVisual();
        }
        #endregion

        #region Rendering
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Brushes.Black, null, rect);

            if (_paletteColors.Count == 0)
            {
                var ft = new FormattedText(
                    "(no palette)",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    12,
                    Brushes.Gray,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(6, 6));
                return;
            }

            if (_paletteBitmap == null || _needsPaletteRedraw)
                BuildPaletteBitmap();

            if (_paletteBitmap != null)
                dc.DrawImage(_paletteBitmap, rect);

            // Draw group separators lightly
            var totalGroups = TotalGroups;
            if (totalGroups > 0)
            {
                double groupWidth = rect.Width / totalGroups;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
                for (int g = 1; g < totalGroups; g++)
                {
                    double x = g * groupWidth;
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, rect.Height));
                }
            }

            // Draw existing ranges
            foreach (var r in _ranges)
                DrawRange(dc, r, Colors.DeepSkyBlue, 0.35);

            // Draw preview (while dragging)
            if (_dragging && _previewRange != null)
                DrawRange(dc, _previewRange, Colors.Gold, 0.45, dashed: true);
        }

        private void DrawRange(DrawingContext dc, RangeDef r, Color color, double fillAlpha, bool dashed = false)
        {
            int totalGroups = TotalGroups; if (totalGroups == 0) return;
            double groupWidth = ActualWidth / totalGroups;
            double x = r.Offset * groupWidth;
            double w = r.Length * groupWidth;
            var fill = new SolidColorBrush(Color.FromArgb((byte)(fillAlpha * 255), color.R, color.G, color.B));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)), 1.5);
            if (dashed) pen.DashStyle = DashStyles.Dash;
            dc.DrawRectangle(fill, pen, new Rect(x, 0, w, ActualHeight));
        }

        private void BuildPaletteBitmap()
        {
            try
            {
                _paletteBitmap = new System.Windows.Media.Imaging.WriteableBitmap(TargetWidth, TargetHeight, 96, 96, PixelFormats.Bgra32, null);
                int width = TargetWidth; int height = TargetHeight; int colors = _paletteColors.Count;
                byte[] pixels = new byte[width * height * 4];
                for (int x = 0; x < width; x++)
                {
                    int palIdx = (int)((double)x / width * (colors - 1));
                    if (palIdx < 0) palIdx = 0; if (palIdx >= colors) palIdx = colors - 1;
                    uint col = _paletteColors[palIdx];
                    byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF;
                    byte r = (byte)((col >> 16) & 0xFF);
                    byte g = (byte)((col >> 8) & 0xFF);
                    byte b = (byte)(col & 0xFF);
                    for (int y = 0; y < height; y++)
                    {
                        int idx = (y * width + x) * 4;
                        pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a;
                    }
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
            Focus(); CaptureMouse(); _dragging = true;
            _dragStartGroup = GetGroupFromPoint(e.GetPosition(this).X);
            _previewRange = new RangeDef { Offset = (uint)_dragStartGroup, Length = 1 };
            InvalidateVisual();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _previewRange == null) return;
            int current = GetGroupFromPoint(e.GetPosition(this).X);
            int min = Math.Min(_dragStartGroup, current);
            int max = Math.Max(_dragStartGroup, current);
            _previewRange.Offset = (uint)min;
            _previewRange.Length = (uint)(max - min + 1);
            InvalidateVisual();
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            ReleaseMouseCapture(); _dragging = false;
            if (_previewRange != null && _previewRange.Length > 0)
            { _ranges.Add(new RangeDef { Offset = _previewRange.Offset, Length = _previewRange.Length }); NormalizeRanges(); RaiseRangesChanged(); }
            _previewRange = null; InvalidateVisual();
        }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_ranges.Count == 0) return;
            int g = GetGroupFromPoint(e.GetPosition(this).X);
            var toRemove = _ranges.FirstOrDefault(r => g >= r.Offset && g < r.Offset + r.Length);
            if (toRemove != null) { _ranges.Remove(toRemove); RaiseRangesChanged(); InvalidateVisual(); }
        }
        private int GetGroupFromPoint(double x)
        {
            int total = TotalGroups; if (total <= 0) return 0;
            double clamped = Math.Max(0, Math.Min(ActualWidth - 1, x));
            int g = (int)(clamped / ActualWidth * total);
            if (g < 0) g = 0; if (g >= total) g = total - 1; return g;
        }
        private void NormalizeRanges()
        {
            if (_ranges.Count <= 1) return;
            _ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            for (int i = 0; i < _ranges.Count - 1; )
            {
                var cur = _ranges[i]; var next = _ranges[i + 1];
                if (next.Offset <= cur.Offset + cur.Length)
                { uint end = Math.Max(cur.Offset + cur.Length, next.Offset + next.Length); cur.Length = end - cur.Offset; _ranges.RemoveAt(i + 1); }
                else i++;
            }
        }
        private void RaiseRangesChanged()
        { RangesChanged?.Invoke(this, _ranges.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length }).ToList()); }
        #endregion

        protected override Size MeasureOverride(Size availableSize) => new Size(double.IsInfinity(availableSize.Width) ? 520 : availableSize.Width, 48);
    }
}
