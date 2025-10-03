using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACViewer.CustomPalettes;

namespace ACViewer.View;

/// <summary>
/// Lightweight palette preview / apply control for docking.
/// </summary>
public partial class CustomPalettePreviewControl : UserControl
{
    private List<uint> _paletteIds = new();

    public CustomPalettePreviewControl()
    {
        InitializeComponent();
    }

    public void LoadPalettes(IEnumerable<uint> paletteIds)
    {
        _paletteIds = paletteIds?.Distinct().OrderBy(i => i).ToList() ?? new List<uint>();
        PaletteList.Items.Clear();
        foreach (var id in _paletteIds)
            PaletteList.Items.Add(BuildPaletteListItem(id));
    }

    private ListBoxItem BuildPaletteListItem(uint id)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        var img = new System.Windows.Controls.Image { Width = 220, Height = 14, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        img.Source = BuildPaletteBitmap(id, 220, 14);
        sp.Children.Add(img);
        sp.Children.Add(new TextBlock { Text = $" 0x{id:X8}", VerticalAlignment = VerticalAlignment.Center });
        return new ListBoxItem { Content = sp, Tag = id, ToolTip = $"0x{id:X8}" };
    }

    private ImageSource BuildPaletteBitmap(uint id, int width, int height)
    {
        try
        {
            uint paletteId = id;
            if ((id >> 24) == 0xF)
            {
                var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(id);
                if (set?.PaletteList?.Count > 0) paletteId = set.PaletteList[0];
            }
            var palette = DatManager.PortalDat.ReadFromDat<Palette>(paletteId);
            if (palette == null || palette.Colors.Count == 0) return null;
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[width * height * 4];
            for (int x = 0; x < width; x++)
            {
                int palIdx = (int)((double)x / width * (palette.Colors.Count - 1));
                uint col = palette.Colors[palIdx];
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
            wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return wb;
        }
        catch { return null; }
    }

    private void PaletteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not ListBoxItem li) { SelectedPreview.Source = null; return; }
        uint id = (uint)li.Tag;
        SelectedPreview.Source = BuildPaletteBitmap(id, 512, 40);
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (PaletteList.SelectedItem is not ListBoxItem li) return;
        uint id = (uint)li.Tag;
        ApplySinglePalette(id);
    }

    private void PaletteList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PaletteList.SelectedItem is not ListBoxItem li) return;
        uint id = (uint)li.Tag;
        ApplySinglePalette(id);
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        LoadPalettes(_paletteIds);
    }

    private void ApplySinglePalette(uint paletteOrSetId)
    {
        // Build a minimal CustomPaletteDefinition with a single entry spanning entire palette.
        try
        {
            var def = new CustomPaletteDefinition { Shade = 0, Multi = false, Entries = new List<CustomPaletteEntry>() };
            var entry = new CustomPaletteEntry { PaletteSetId = paletteOrSetId };
            uint actualPaletteId = paletteOrSetId;
            int colorCount = 0;
            if ((paletteOrSetId >> 24) == 0x0F)
            {
                var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(paletteOrSetId);
                if (set?.PaletteList?.Count > 0) actualPaletteId = set.PaletteList[0];
            }
            var palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId);
            colorCount = palette?.Colors?.Count ?? 0;
            if (colorCount <= 0) return;
            // Express in groups of 8 colors -> one range covering everything.
            uint groups = (uint)Math.Max(1, (colorCount + 7) / 8);
            entry.Ranges.Add(new RangeDef { Offset = 0, Length = groups });
            def.Entries.Add(entry);

            // Hand off via ClothingTableList path (simulate custom live update)
            ClothingTableList.Instance?.ApplyPalettePreviewDefinition(def);
        }
        catch { }
    }
}
