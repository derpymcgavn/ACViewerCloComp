using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ACViewer.CustomPalettes;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACViewer.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Animation;
using ACViewer.CustomTextures;
using ACViewer.Render;
using ACE.DatLoader.Entity;
using ACViewer.Model;
using ACE.Entity.Enum;
using Microsoft.Win32;
using ACViewer.View.Controls;
using System.IO;
using System.Windows.Threading;

namespace ACViewer.View
{
    // Converted to UserControl (formerly Window) for tabbed / docked usage only
    public partial class CustomPaletteDialog : UserControl
    {
        public static CustomPaletteDialog ActiveInstance { get; private set; }

        public UIElement PaletteDockContent { get; private set; }
        public UIElement TextureDockContent { get; private set; }

        private class DockHost : ContentControl, ICustomPaletteHost, ITextureOverrideHost
        {
            private readonly CustomPaletteDialog _owner; private readonly bool _isPaletteHost;
            public DockHost(CustomPaletteDialog owner, bool isPaletteHost) { _owner = owner; _isPaletteHost = isPaletteHost; }
            public void LoadDefinition(CustomPaletteDefinition definition, bool isLive) { if (definition == null || _owner == null) return; _owner.ExternalLoadDefinition(definition, isLive); }
            public void UpdateFromPalette(CustomPaletteDefinition definition) { if (!_isPaletteHost) return; if (definition == null) return; _owner.ExternalLoadDefinition(definition, true); }
        }

        private ListBox _lstSetupIds; private bool _setupPanelInitialized;

        public CustomPaletteDefinition ResultDefinition { get; private set; }
        public CustomPaletteDefinition StartingDefinition { get; set; }
        public List<uint> AvailablePaletteIDs { get; set; }
        public Action<CustomPaletteDefinition> OnLiveUpdate { get; set; }

        private const int PreviewWidth = 512; private const int PreviewHeight = 48;
        private PaletteSet _currentSet; private int _currentSetIndex;

        private CheckBox chkMulti; private TextBox txtPaletteId; private TextBox txtSearch; private ListBox lstPalettes; private System.Windows.Controls.Image imgBigPreview; private StackPanel panelSetBrowse; private TextBlock lblSetIndex; private Slider sldSetIndex; private Button btnUseSetPalette; private StackPanel panelSingle; private TextBox txtRanges; private StackPanel panelMulti; private TextBox txtMulti; private TextBox txtShade; private Slider sldShade; private Button btnOk; private Button btnSave; private Button btnLoad; private Button btnExport; private System.Windows.Controls.Image imgRangePreview; private TextBox txtColorSearch; private Button btnColorFind; private TextBlock lblColorResult; private ListView _lstRanges;
        private RangeEditorControl _rangeEditor; private Border _rangeEditorHost; private Button _btnRangeUndo; private Button _btnRangeRedo;

        private class RangeDisplay { public uint PaletteId { get; set; } public uint Offset { get; set; } public uint Length { get; set; } public string PaletteHex => $"0x{PaletteId:X8}"; public string OffsetLength => $"{Offset}:{Length}"; }
        private class PaletteEntryRow : INotifyPropertyChanged
        { private uint _paletteSetId; private string _rangesText; private bool _isLocked; public uint PaletteSetId { get => _paletteSetId; set { if (_paletteSetId != value) { _paletteSetId = value; OnPropertyChanged(nameof(PaletteSetId)); OnPropertyChanged(nameof(PaletteHex)); } } } public string RangesText { get => _rangesText; set { if (_rangesText != value) { _rangesText = value; OnPropertyChanged(nameof(RangesText)); } } } public bool IsLocked { get => _isLocked; set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(nameof(IsLocked)); } } } public string PaletteHex => $"0x{PaletteSetId:X8}"; public event PropertyChangedEventHandler PropertyChanged; private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

        private ObservableCollection<PaletteEntryRow> _rows = new(); private DataGrid _gridEntries; private Button _btnAddRow; private Button _btnRemoveRow; private bool _freezeLines; private CheckBox _chkLockAll;

        private class TextureRow { public int PartIndex { get; set; } public uint OldId { get; set; } public uint NewId { get; set; } public bool IsLocked { get; set; } public string OldHex => $"0x{OldId:X8}"; public string NewHex => $"0x{NewId:X8}"; }
        private readonly List<TextureRow> _textureRows = new(); private List<uint> _availableTextureIds = new(); private ListBox lstTextures; private DataGrid gridTextures; private CheckBox chkTexLockAll; private Button btnTexAdd; private Button btnTexRemove; private Button btnTexSave; private Button btnTexLoad; private Button btnTexOk; private TextBox txtTexSearch; private System.Windows.Controls.Image imgTexPreviewOld; private System.Windows.Controls.Image imgTexPreviewNew; private TabControl _tabControl; private TabControl _rootTabs; private DateTime _lastAutosave = DateTime.MinValue; private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(2); private const string AutosaveFile = "CustomPalette.autosave.json";

        // Disco
        private string _discoBuffer = string.Empty; private DispatcherTimer _discoTimer; private bool _discoMode; private readonly Random _discoRand = new();

        private bool _hasClothing; // gate enabling of UI until clothing item active

        public CustomPaletteDialog()
        {
            Width = 860; Height = 700; Content = BuildUI(); Loaded += CustomPaletteDialog_Loaded; KeyDown += CustomPaletteDialog_KeyDown; CustomPaletteStore.LoadAll(); ActiveInstance = this; // register
            // initial disabled state (grayed out) until clothing item present
            SetHasClothing(ClothingTableList.CurrentClothingItem != null);
        }

        private void CustomPaletteDialog_Loaded(object sender, RoutedEventArgs e) => InitializeCore();

        private void CustomPaletteDialog_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        { if (e.Key == System.Windows.Input.Key.Escape && _discoMode) { ToggleDisco(false); return; } var k = e.Key.ToString(); if (k.Length == 1 || (k.StartsWith("D") && k.Length == 2)) { char c = k[^1]; if (char.IsLetter(c)) c = char.ToLowerInvariant(c); _discoBuffer += c; if (_discoBuffer.Length > 16) _discoBuffer = _discoBuffer[^16..]; if (_discoBuffer.EndsWith("atoyot")) ToggleDisco(!_discoMode); } }
        private void ToggleDisco(bool enable) { if (enable == _discoMode) return; _discoMode = enable; if (enable) { _discoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; _discoTimer.Tick -= DiscoTick; _discoTimer.Tick += DiscoTick; _discoTimer.Start(); } else { _discoTimer?.Stop(); Background = Brushes.Transparent; if (imgBigPreview != null) imgBigPreview.Opacity = 1.0; } }
        private void DiscoTick(object sender, EventArgs e) { byte r = (byte)_discoRand.Next(64, 256); byte g = (byte)_discoRand.Next(64, 256); byte b = (byte)_discoRand.Next(64, 256); Background = new SolidColorBrush(Color.FromRgb(r, g, b)); if (imgBigPreview != null) imgBigPreview.Opacity = (_discoRand.NextDouble() * 0.3) + 0.7; }

        private void InitializeCore()
        { PopulateList(); if (StartingDefinition != null) { ApplyLoadedPreset(StartingDefinition); if (StartingDefinition.Multi) chkMulti.IsChecked = true; } else { SyncRowsFromText(); SyncTextLinesFromRows(); } _freezeLines = false; HookLiveEvents(); UpdateBigPreviewImage(); UpdateRangeHighlight(); RefreshRangeList(); InitTextureTabData(); PopulateSetupIds(); SyncRangeEditorFromRow(); }

        public void SetHasClothing(bool hasClothing)
        {
            _hasClothing = hasClothing;
            if (_tabControl != null)
            {
                _tabControl.IsEnabled = hasClothing; // gray out everything when false
                _tabControl.Opacity = hasClothing ? 1.0 : 0.55;
            }
        }

        private static void DetachFromParent(UIElement element)
        {
            if (element == null) return;
            if (element is FrameworkElement fe)
            {
                switch (fe.Parent)
                {
                    case ContentControl cc:
                        cc.Content = null; break;
                    case Panel pn:
                        pn.Children.Remove(fe); break;
                    case Decorator deco:
                        deco.Child = null; break;
                }
            }
        }

        private UIElement BuildUI()
        { var ui = BuildOriginalUI(); _rootTabs = ui as TabControl; _tabControl = _rootTabs; if (_rootTabs != null && _rootTabs.Items.Count >= 2) { if (_rootTabs.Items[0] is TabItem palTab && palTab.Content is UIElement dp) { var host = new DockHost(this, true) { Content = dp }; palTab.Content = host; PaletteDockContent = host; } if (_rootTabs.Items.Count > 1 && _rootTabs.Items[1] is TabItem texTab && texTab.Content is UIElement tex) { var host = new DockHost(this, false) { Content = tex }; texTab.Content = host; TextureDockContent = host; } }
            // ensure disabled if no clothing context at creation
            if (!_hasClothing && _tabControl != null) { _tabControl.IsEnabled = false; _tabControl.Opacity = 0.55; }
            return ui; }
        public (UIElement palette, UIElement textures) GetDockParts() { DetachFromParent(PaletteDockContent); DetachFromParent(TextureDockContent); return (PaletteDockContent, TextureDockContent); }
        public void InitializeForDock() => InitializeCore();

        private void HookLiveEvents() { if (txtPaletteId != null) txtPaletteId.TextChanged += (_, __) => DoLiveUpdate(); if (txtRanges != null) txtRanges.TextChanged += (_, __) => DoLiveUpdate(); if (sldShade != null) sldShade.ValueChanged += (_, __) => DoLiveUpdate(); }
        private void Autosave(CustomPaletteDefinition def) { if (def == null) return; if (DateTime.UtcNow - _lastAutosave < AutosaveInterval) return; _lastAutosave = DateTime.UtcNow; try { var json = System.Text.Json.JsonSerializer.Serialize(def, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(AutosaveFile, json); } catch { } }
        private void DoLiveUpdate() { if (TryBuildDefinition(out var def)) { OnLiveUpdate?.Invoke(def); Autosave(def); ResultDefinition = def; } }

        private bool TryBuildDefinition(out CustomPaletteDefinition def) { def = null; try { var tmp = new CustomPaletteDefinition { Multi = true, Shade = ParseFloat(txtShade.Text.Trim(), 0) }; foreach (var row in _rows) { var rangeSpec = row.RangesText.Replace(',', ' '); var entry = new CustomPaletteEntry { PaletteSetId = row.PaletteSetId, Ranges = RangeParser.ParseRanges(rangeSpec) }; if (entry.Ranges.Count == 0) return false; tmp.Entries.Add(entry); } if (tmp.Entries.Count == 0) return false; def = tmp; return true; } catch { return false; } }

        private void PopulateList() { if (AvailablePaletteIDs == null || lstPalettes == null) return; lstPalettes.Items.Clear(); foreach (var id in AvailablePaletteIDs) lstPalettes.Items.Add(BuildPaletteListItem(id)); }
        private ListBoxItem BuildPaletteListItem(uint id) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) }; var img = new System.Windows.Controls.Image { Width = 220, Height = 14, Stretch = Stretch.Fill, SnapsToDevicePixels = true }; img.Source = BuildPaletteBitmap(id, 220, 14); sp.Children.Add(img); sp.Children.Add(new TextBlock { Text = $" 0x{id:X8}", VerticalAlignment = VerticalAlignment.Center }); return new ListBoxItem { Content = sp, Tag = id, ToolTip = $"0x{id:X8}" }; }
        private ImageSource BuildPaletteBitmap(uint id, int width, int height) { try { uint paletteId = id; if ((id >> 24) == 0xF) { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(id); if (set?.PaletteList?.Count > 0) paletteId = set.PaletteList[0]; } var palette = DatManager.PortalDat.ReadFromDat<Palette>(paletteId); if (palette == null || palette.Colors.Count == 0) return null; var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); var pixels = new byte[width * height * 4]; for (int x = 0; x < width; x++) { int palIdx = (int)((double)x / width * (palette.Colors.Count - 1)); uint col = palette.Colors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF); for (int y = 0; y < height; y++) { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; } } wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0); return wb; } catch { return null; } }
        private void UpdateBigPreviewImage() { if (imgBigPreview == null) return; if (_currentSet != null) { if (_currentSet.PaletteList == null || _currentSet.PaletteList.Count == 0) { imgBigPreview.Source = null; return; } var safeIndex = Math.Clamp(_currentSetIndex, 0, _currentSet.PaletteList.Count - 1); var palId = _currentSet.PaletteList[safeIndex]; imgBigPreview.Source = BuildPaletteBitmap(palId, PreviewWidth, PreviewHeight); return; } else if (lstPalettes?.SelectedItem is ListBoxItem li) { imgBigPreview.Source = BuildPaletteBitmap((uint)li.Tag, PreviewWidth, PreviewHeight); } }

        private void UpdateRangeHighlight() { if (imgRangePreview == null) return; Palette palette = null; uint actualPaletteId = 0; string rangeSpec = null; if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow row) { uint palId = row.PaletteSetId; actualPaletteId = palId; if ((palId >> 24) == 0xF) { try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0]; } catch { imgRangePreview.Source = null; return; } } try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId); } catch { palette = null; } rangeSpec = row.RangesText.Replace(',', ' '); } else { if (txtMulti == null) return; var text = txtMulti.Text; if (string.IsNullOrWhiteSpace(text)) { imgRangePreview.Source = null; return; } int caret = txtMulti.CaretIndex; int lineStart = text.LastIndexOf('\n', Math.Clamp(caret - 1, 0, text.Length - 1)); if (lineStart == -1) lineStart = 0; else lineStart += 1; int lineEnd = text.IndexOf('\n', caret); if (lineEnd == -1) lineEnd = text.Length; var line = text.Substring(lineStart, lineEnd - lineStart).Trim(); if (string.IsNullOrWhiteSpace(line)) { imgRangePreview.Source = null; return; } var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 2) { imgRangePreview.Source = null; return; } uint palId; try { palId = ParseUInt(parts[0]); } catch { imgRangePreview.Source = null; return; } actualPaletteId = palId; if ((palId >> 24) == 0xF) { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0]; } palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId); rangeSpec = string.Join(" ", parts.Skip(1)); }
            if (palette == null || palette.Colors.Count == 0 || rangeSpec == null) { imgRangePreview.Source = null; return; } var ranges = RangeParser.ParseRanges(rangeSpec, true, out _); int colors = palette.Colors.Count; int width = 512; int height = 64; var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); var pixels = new byte[width * height * 4]; for (int x = 0; x < width; x++) { int palIdx = (int)((double)x / width * (colors - 1)); uint col = palette.Colors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF); bool highlighted = false; foreach (var rg in ranges) { var start = (int)rg.Offset * 8; var count = (int)rg.Length * 8; if (palIdx >= start && palIdx < start + count) { highlighted = true; break; } } if (highlighted) { r = (byte)Math.Min(255, r + 60); g = (byte)Math.Min(255, g + 60); b = (byte)Math.Min(255, b + 60); } for (int y = 0; y < height; y++) { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; } } wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0); imgRangePreview.Source = wb; }
        private void RefreshRangeList() { if (_lstRanges == null) return; var items = new List<RangeDisplay>(); foreach (var row in _rows) { var ranges = RangeParser.ParseRanges(row.RangesText.Replace(',', ' '), true, out _); foreach (var r in ranges) items.Add(new RangeDisplay { PaletteId = row.PaletteSetId, Offset = r.Offset, Length = r.Length }); } _lstRanges.ItemsSource = items; }

        private void SyncRangeEditorFromRow() { if (_rangeEditor == null) return; if (_gridEntries?.SelectedItem is not PaletteEntryRow row) { _rangeEditor.SetPalette(null); _rangeEditor.SetRanges(Array.Empty<RangeDef>()); UpdateRangeUndoRedoButtons(); return; } uint palId = row.PaletteSetId; uint actual = palId; if ((palId >> 24) == 0xF) { try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set?.PaletteList?.Count > 0) actual = set.PaletteList[0]; } catch { } } Palette palette = null; try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actual); } catch { } _rangeEditor.SetPalette(palette); var parsed = RangeParser.ParseRanges(row.RangesText.Replace(',', ' '), true, out _); _rangeEditor.SetRanges(parsed); UpdateRangeUndoRedoButtons(); }
        private void ApplyRangeEditorToSelectedRow(IReadOnlyList<RangeDef> list) { if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return; if (row.IsLocked) return; row.RangesText = string.Join(",", list.Select(r => $"{r.Offset}:{r.Length}")); EnforceNonOverlappingRanges(row.PaletteSetId); RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); if (!_freezeLines) SyncTextLinesFromRows(); _gridEntries.Items.Refresh(); UpdateRangeUndoRedoButtons(); }

        private void lstPalettes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPalettes.SelectedItem is not ListBoxItem li) return;
            uint id = (uint)li.Tag;

            if ((id >> 24) == 0xF)
            {
                _currentSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(id);
                _currentSetIndex = 0;
                var hasList = _currentSet?.PaletteList?.Count > 0;
                // Guard against optional UI (panelSetBrowse, sldSetIndex, lblSetIndex may not exist in minimal build)
                if (panelSetBrowse != null && sldSetIndex != null && lblSetIndex != null)
                {
                    panelSetBrowse.Visibility = hasList ? Visibility.Visible : Visibility.Collapsed;
                    if (hasList)
                    {
                        sldSetIndex.Maximum = _currentSet.PaletteList.Count - 1;
                        sldSetIndex.Value = 0;
                        lblSetIndex.Text = "Set Index: 0";
                    }
                }
            }
            else
            {
                _currentSet = null;
                if (panelSetBrowse != null) panelSetBrowse.Visibility = Visibility.Collapsed;
            }

            if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow row && !row.IsLocked)
            {
                row.PaletteSetId = id;
                RefreshRangeList();
                UpdateRangeHighlight();
                DoLiveUpdate();
                _gridEntries.Items.Refresh();
            }

            UpdateBigPreviewImage();
            UpdateRangeHighlight();
            RefreshRangeList();
            HighlightRangesForPalette(id);
            SyncRangeEditorFromRow();
        }
        private void HighlightRangesForPalette(uint palId) { if (_lstRanges?.ItemsSource is System.Collections.IEnumerable en) { foreach (var item in en) if (item is RangeDisplay rd && rd.PaletteId == palId) { _lstRanges.SelectedItem = rd; _lstRanges.ScrollIntoView(rd); break; } } }
        private void sldSetIndex_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_currentSet == null) return; _currentSetIndex = (int)sldSetIndex.Value; lblSetIndex.Text = $"Set Index: {_currentSetIndex}"; UpdateBigPreviewImage(); }
        private void btnUseSetPalette_Click(object sender, RoutedEventArgs e) { if (_currentSet == null) return; if (_currentSetIndex < 0 || _currentSetIndex >= _currentSet.PaletteList.Count) return; HighlightRangesForPalette(_currentSet.PaletteList[_currentSetIndex]); }
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) { var filter = txtSearch.Text.Trim().ToLowerInvariant(); lstPalettes.BeginInit(); lstPalettes.Items.Clear(); foreach (var id in AvailablePaletteIDs) { var label = $"0x{id:X8}".ToLowerInvariant(); if (string.IsNullOrEmpty(filter) || label.Contains(filter)) lstPalettes.Items.Add(BuildPaletteListItem(id)); } lstPalettes.EndInit(); }
        private void chkMulti_Checked(object sender, RoutedEventArgs e) => DoLiveUpdate();
        private void sldShade_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { txtShade.Text = sldShade.Value.ToString("0.###", CultureInfo.InvariantCulture); }
        private void btnOk_Click(object sender, RoutedEventArgs e) { if (TryBuildDefinition(out var def)) { ResultDefinition = def; OnLiveUpdate?.Invoke(def); } else { MessageBox.Show("Invalid definition", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void btnSave_Click(object sender, RoutedEventArgs e) { if (ResultDefinition == null && TryBuildDefinition(out var temp)) ResultDefinition = temp; if (ResultDefinition == null) return; var name = Microsoft.VisualBasic.Interaction.InputBox("Preset Name:", "Save Custom Palette", ResultDefinition.Name ?? "MyPreset"); if (string.IsNullOrWhiteSpace(name)) return; ResultDefinition.Name = name.Trim(); CustomPaletteStore.SaveDefinition(ResultDefinition); MessageBox.Show("Saved.", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Information); }
        private void btnLoad_Click(object sender, RoutedEventArgs e) { var defs = CustomPaletteStore.LoadAll().ToList(); if (defs.Count == 0) { MessageBox.Show("No saved presets.", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Information); return; } var picker = new PresetPickerWindow(defs); if (picker.ShowDialog() == true && picker.Selected != null) { ApplyLoadedPreset(picker.Selected); DoLiveUpdate(); } SyncRangeEditorFromRow(); }
        public void ApplyLoadedPreset(CustomPaletteDefinition def) { if (def == null) return; if (chkMulti != null) chkMulti.IsChecked = true; _rows.Clear(); foreach (var e in def.Entries) { var rtxt = string.Join(",", e.Ranges.Select(r => $"{r.Offset}:{r.Length}")); _rows.Add(new PaletteEntryRow { PaletteSetId = e.PaletteSetId, RangesText = rtxt, IsLocked = _chkLockAll?.IsChecked == true }); } EnforceNonOverlappingRanges(); if (!_freezeLines) SyncTextLinesFromRows(); txtShade.Text = def.Shade.ToString("0.###", CultureInfo.InvariantCulture); sldShade.Value = def.Shade; ResultDefinition = def; UpdateBigPreviewImage(); UpdateRangeHighlight(); RefreshRangeList(); SyncRangeEditorFromRow(); UpdateRangeUndoRedoButtons(); }
        internal void ExternalLoadDefinition(CustomPaletteDefinition def, bool isLive) { ApplyLoadedPreset(def); if (isLive) DoLiveUpdate(); }
        private void GridEntries_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { if (e.Row?.Item is not PaletteEntryRow row) return; if (row.IsLocked) { e.Cancel = true; return; } if (e.EditingElement is TextBox tb) { row.RangesText = tb.Text.Trim(); EnforceNonOverlappingRanges(row.PaletteSetId); SyncTextLinesFromRows(); RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); SyncRangeEditorFromRow(); } }
        private void GridEntries_BeginningEdit(object sender, DataGridBeginningEditEventArgs e) { if (e.Row?.Item is PaletteEntryRow row && row.IsLocked) e.Cancel = true; }
        private void AddRowFromSelection() { uint palId = 0; if (lstPalettes?.SelectedItem is ListBoxItem li) palId = (uint)li.Tag; else if (_currentSet != null && _currentSet.PaletteList != null && _currentSet.PaletteList.Count > 0) palId = _currentSet.PaletteList[0]; else if (AvailablePaletteIDs?.Count > 0) palId = AvailablePaletteIDs[0]; if (palId == 0) return; var newRow = new PaletteEntryRow { PaletteSetId = palId, RangesText = "0:1", IsLocked = _chkLockAll?.IsChecked == true }; _rows.Add(newRow); EnforceNonOverlappingRanges(palId); SyncTextLinesFromRows(); RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); _gridEntries.SelectedItem = newRow; SyncRangeEditorFromRow(); }
        private void RemoveSelectedRow() { if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return; if (row.IsLocked) return; var palId = row.PaletteSetId; _rows.Remove(row); EnforceNonOverlappingRanges(palId); SyncTextLinesFromRows(); RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); SyncRangeEditorFromRow(); }
        private void UpdateRangeUndoRedoButtons() { if (_btnRangeUndo == null || _btnRangeRedo == null || _rangeEditor == null) return; _btnRangeUndo.IsEnabled = _rangeEditor.CanUndo; _btnRangeRedo.IsEnabled = _rangeEditor.CanRedo; }

        private void PopulateSetupIds() { if (_lstSetupIds == null) return; _lstSetupIds.Items.Clear(); var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) return; foreach (var id in clothing.ClothingBaseEffects.Keys.OrderBy(k => k)) _lstSetupIds.Items.Add(new ListBoxItem { Content = $"0x{id:X8}", Tag = id }); var ext = ClothingTableList.Instance?.SetupIds; uint sel = 0; if (ext?.SelectedItem is ListBoxItem li) sel = (uint)li.DataContext; if (sel != 0) { for (int i = 0; i < _lstSetupIds.Items.Count; i++) if (_lstSetupIds.Items[i] is ListBoxItem item && (uint)item.Tag == sel) { _lstSetupIds.SelectedIndex = i; return; } } if (_lstSetupIds.Items.Count > 0) _lstSetupIds.SelectedIndex = 0; }
        private void LstSetupIds_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_lstSetupIds?.SelectedItem is not ListBoxItem li) return; uint setupId = (uint)li.Tag; var ext = ClothingTableList.Instance?.SetupIds; if (ext != null) { for (int i = 0; i < ext.Items.Count; i++) { if (ext.Items[i] is ListBoxItem other && (uint)other.DataContext == setupId) { ext.SelectedIndex = i; break; } } } DoLiveUpdate(); DoLiveTextureUpdateOnly(); }

        private void BtnExport_Click(object sender, RoutedEventArgs e) { try { var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) { MessageBox.Show("No clothing item loaded."); return; } var dlg = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json", FileName = $"Clothing_{clothing.Id:X8}.json" }; if (dlg.ShowDialog() != true) return; CustomTextureStore.ExportClothingTable(clothing, dlg.FileName); MessageBox.Show("Export complete."); } catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}"); } }

        private static uint ParseUInt(string s) { if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToUInt32(s[2..], 16); if (uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) return hex; return uint.Parse(s, CultureInfo.InvariantCulture); }
        private static float ParseFloat(string s, float defVal) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0f, 1f) : defVal;
        private void SyncRowsFromText() { _rows.Clear(); var text = txtMulti?.Text; if (string.IsNullOrWhiteSpace(text)) return; var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); foreach (var line in lines) { var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 2) continue; try { uint pal = ParseUInt(parts[0]); var ranges = string.Join(" ", parts.Skip(1)).Replace(' ', ','); _rows.Add(new PaletteEntryRow { PaletteSetId = pal, RangesText = ranges }); } catch { } } }
        private void SyncTextLinesFromRows() { if (txtMulti == null) return; var caret = txtMulti.CaretIndex; var newText = string.Join(System.Environment.NewLine, _rows.Select(r => $"0x{r.PaletteSetId:X8} {r.RangesText.Replace(' ', ',')}``")); if (txtMulti.Text != newText) { txtMulti.Text = newText; if (caret <= txtMulti.Text.Length) txtMulti.CaretIndex = caret; } }

        private void SplitSelectedRangeToNewRow() { if (_lstRanges?.SelectedItem is not RangeDisplay rd) return; PaletteEntryRow sourceRow = null; List<RangeDef> sourceRanges = null; foreach (var row in _rows) { if (row.PaletteSetId != rd.PaletteId) continue; var parsed = RangeParser.ParseRanges(row.RangesText.Replace(',', ' '), true, out _); var match = parsed.FirstOrDefault(r => r.Offset == rd.Offset && r.Length == rd.Length); if (match != null) { sourceRow = row; sourceRanges = parsed; break; } } if (sourceRow == null || sourceRanges == null) return; var removed = sourceRanges.First(r => r.Offset == rd.Offset && r.Length == rd.Length); sourceRanges.Remove(removed); if (sourceRanges.Count == 0) _rows.Remove(sourceRow); else sourceRow.RangesText = string.Join(",", sourceRanges.Select(r => $"{r.Offset}:{r.Length}")); var newRow = new PaletteEntryRow { PaletteSetId = rd.PaletteId, RangesText = $"{rd.Offset}:{rd.Length}", IsLocked = _chkLockAll?.IsChecked == true }; _rows.Add(newRow); EnforceNonOverlappingRanges(rd.PaletteId); if (!_freezeLines) SyncTextLinesFromRows(); RefreshRangeList(); _gridEntries.Items.Refresh(); _gridEntries.SelectedItem = newRow; UpdateRangeHighlight(); SyncRangeEditorFromRow(); DoLiveUpdate(); }
        private void EnforceNonOverlappingRanges(uint? paletteFilter = null) { var groups = _rows.Where(r => !paletteFilter.HasValue || r.PaletteSetId == paletteFilter.Value).GroupBy(r => r.PaletteSetId); foreach (var g in groups) { var used = new HashSet<uint>(); var orderedRows = g.ToList(); foreach (var row in orderedRows) { var parsed = RangeParser.ParseRanges(row.RangesText.Replace(',', ' '), true, out _).OrderBy(r => r.Offset).ToList(); var rebuilt = new List<(uint off, uint len)>(); foreach (var r in parsed) { for (uint i = r.Offset; i < r.Offset + r.Length; i++) { if (!used.Add(i)) continue; if (rebuilt.Count == 0 || rebuilt[^1].off + rebuilt[^1].len != i) rebuilt.Add((i, 1)); else rebuilt[^1] = (rebuilt[^1].off, rebuilt[^1].len + 1); } } var newText = string.Join(",", rebuilt.Select(t => $"{t.off}:{t.len}")); if (string.IsNullOrEmpty(newText)) row.RangesText = string.Empty; else row.RangesText = newText; } var empty = orderedRows.Where(r => string.IsNullOrWhiteSpace(r.RangesText)).ToList(); foreach (var r in empty) _rows.Remove(r); } if (!_freezeLines) SyncTextLinesFromRows(); }

        // Texture tab helpers (simplified subset)
        private void InitTextureTabData() { _textureRows.Clear(); _availableTextureIds.Clear(); try { var setupInst = ModelViewer.Instance?.Setup; var setup = setupInst?.Setup; if (setup == null) return; for (int partIndex = 0; partIndex < setup.Parts.Count; partIndex++) { var gfxObj = setup.Parts[partIndex]; if (gfxObj?._gfxObj == null) continue; foreach (var surfaceId in gfxObj._gfxObj.Surfaces) { var surface = DatManager.PortalDat.ReadFromDat<Surface>(surfaceId); if (surface.OrigTextureId != 0) { var oldTex = surface.OrigTextureId; if (!_textureRows.Exists(r => r.PartIndex == partIndex && r.OldId == oldTex)) _textureRows.Add(new TextureRow { PartIndex = partIndex, OldId = oldTex, NewId = oldTex }); if (!_availableTextureIds.Contains(oldTex)) _availableTextureIds.Add(oldTex); } } } foreach (var id in DatManager.PortalDat.AllFiles.Keys.Where(i => (i >> 24) == 0x05).Take(200)) if (!_availableTextureIds.Contains(id)) _availableTextureIds.Add(id); } catch { } _availableTextureIds = _availableTextureIds.Distinct().OrderBy(i => i).ToList(); }
        private void DoLiveTextureUpdateOnly() { /* noop in simplified embedding; texture live update handled elsewhere */ }

        private UIElement BuildOriginalUI() { // Minimal wrapper using existing design (subset) for embedding
            var tab = new TabControl();
            var palTab = new TabItem { Header = "Palettes" };
            var texTab = new TabItem { Header = "Textures" };
            tab.Items.Add(palTab); tab.Items.Add(texTab);
            // Palette tab content root
            var palDock = new DockPanel(); palTab.Content = palDock;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            btnLoad = new Button { Content = "Load", Width = 70, Margin = new Thickness(0,0,6,0) }; btnLoad.Click += btnLoad_Click;
            btnSave = new Button { Content = "Save", Width = 70, Margin = new Thickness(0,0,6,0) }; btnSave.Click += btnSave_Click;
            btnOk = new Button { Content = "Apply", Width = 80, Margin = new Thickness(0,0,6,0) }; btnOk.Click += btnOk_Click;
            btnExport = new Button { Content = "Export", Width = 80, Margin = new Thickness(0,0,6,0) }; btnExport.Click += BtnExport_Click;
            buttons.Children.Add(btnLoad); buttons.Children.Add(btnSave); buttons.Children.Add(btnOk); buttons.Children.Add(btnExport);
            DockPanel.SetDock(buttons, Dock.Bottom); palDock.Children.Add(buttons);
            var shadeGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) }; shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); shadeGrid.ColumnDefinitions.Add(new ColumnDefinition()); shadeGrid.Children.Add(new TextBlock { Text = "Shade:" }); txtShade = new TextBox { Text = "0", Height = 22 }; Grid.SetColumn(txtShade,1); shadeGrid.Children.Add(txtShade); sldShade = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01 }; Grid.SetColumn(sldShade,2); sldShade.ValueChanged += sldShade_ValueChanged; shadeGrid.Margin = new Thickness(0,4,0,4); DockPanel.SetDock(shadeGrid, Dock.Bottom); palDock.Children.Add(shadeGrid);
            var mainGrid = new Grid(); mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); mainGrid.ColumnDefinitions.Add(new ColumnDefinition()); palDock.Children.Add(mainGrid);
            // Left
            var leftStack = new StackPanel(); mainGrid.Children.Add(leftStack);
            // NEW: multi mode checkbox (was missing causing NRE)
            chkMulti = new CheckBox { Content = "Multi", IsChecked = true, Margin = new Thickness(0,0,0,4) }; chkMulti.Checked += chkMulti_Checked; chkMulti.Unchecked += chkMulti_Checked; leftStack.Children.Add(chkMulti);
            leftStack.Children.Add(new TextBlock { Text = "Palettes", FontWeight = FontWeights.Bold });
            txtSearch = new TextBox { Height = 22, Margin = new Thickness(0,0,0,4) }; txtSearch.TextChanged += txtSearch_TextChanged; leftStack.Children.Add(txtSearch);
            lstPalettes = new ListBox { Height = 260 }; lstPalettes.SelectionChanged += lstPalettes_SelectionChanged; leftStack.Children.Add(lstPalettes);
            _gridEntries = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = _rows, Height = 140, Margin = new Thickness(0,4,0,0) }; _gridEntries.SelectionChanged += (_, __) => { UpdateRangeHighlight(); SyncRangeEditorFromRow(); }; _gridEntries.CellEditEnding += GridEntries_CellEditEnding; _gridEntries.BeginningEdit += GridEntries_BeginningEdit; _gridEntries.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new System.Windows.Data.Binding("IsLocked") }); _gridEntries.Columns.Add(new DataGridTextColumn { Header = "Palette", Binding = new System.Windows.Data.Binding("PaletteHex") }); _gridEntries.Columns.Add(new DataGridTextColumn { Header = "Ranges", Binding = new System.Windows.Data.Binding("RangesText") }); leftStack.Children.Add(_gridEntries);
            var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,4,0,4) }; _btnAddRow = new Button { Content = "+", Width = 30, Margin = new Thickness(0,0,4,0)}; _btnAddRow.Click += (_, __)=> AddRowFromSelection(); _btnRemoveRow = new Button { Content = "-", Width = 30 }; _btnRemoveRow.Click += (_, __)=> RemoveSelectedRow(); rowBtns.Children.Add(_btnAddRow); rowBtns.Children.Add(_btnRemoveRow); leftStack.Children.Add(rowBtns);
            // Right details
            var rightStack = new StackPanel { Margin = new Thickness(6,0,0,0) }; Grid.SetColumn(rightStack,1); mainGrid.Children.Add(rightStack);
            rightStack.Children.Add(new TextBlock { Text = "Preview", FontWeight = FontWeights.Bold }); imgBigPreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill }; rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0,2,0,6), Child = imgBigPreview });
            rightStack.Children.Add(new TextBlock { Text = "Range Editor", FontWeight = FontWeights.Bold }); _btnRangeUndo = new Button { Content = "Undo", Width = 50, Margin = new Thickness(0,0,4,0) }; _btnRangeRedo = new Button { Content = "Redo", Width = 50 }; var undoPanel = new StackPanel { Orientation = Orientation.Horizontal }; undoPanel.Children.Add(_btnRangeUndo); undoPanel.Children.Add(_btnRangeRedo); rightStack.Children.Add(undoPanel); _btnRangeUndo.Click += (_, __)=> { _rangeEditor?.Undo(); UpdateRangeUndoRedoButtons(); }; _btnRangeRedo.Click += (_, __)=> { _rangeEditor?.Redo(); UpdateRangeUndoRedoButtons(); };
            _rangeEditor = new RangeEditorControl { Height = 44, Margin = new Thickness(0,2,0,4) }; _rangeEditor.RangesChanged += (_, list)=> ApplyRangeEditorToSelectedRow(list); _rangeEditor.HistoryChanged += (_, __)=> UpdateRangeUndoRedoButtons(); rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _rangeEditor });
            rightStack.Children.Add(new TextBlock { Text = "All Ranges", FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0)}); _lstRanges = new ListView { Height = 120 }; var gv = new GridView(); gv.Columns.Add(new GridViewColumn { Header = "Palette", DisplayMemberBinding = new System.Windows.Data.Binding("PaletteHex") }); gv.Columns.Add(new GridViewColumn { Header = "Off:Len", DisplayMemberBinding = new System.Windows.Data.Binding("OffsetLength") }); _lstRanges.View = gv; rightStack.Children.Add(_lstRanges);
            rightStack.Children.Add(new TextBlock { Text = "Generated", FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0)}); txtMulti = new TextBox { AcceptsReturn = true, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsReadOnly = true }; rightStack.Children.Add(txtMulti);
            rightStack.Children.Add(new TextBlock { Text = "Highlight", FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0)}); imgRangePreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill }; rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = imgRangePreview });
            return tab; }
    }
}
