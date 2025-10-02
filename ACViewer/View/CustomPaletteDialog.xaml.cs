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

namespace ACViewer.View
{
    public partial class CustomPaletteDialog : Window
    {
        // Model
        public CustomPaletteDefinition ResultDefinition { get; private set; }
        public CustomPaletteDefinition StartingDefinition { get; set; }
        public List<uint> AvailablePaletteIDs { get; set; }
        public Action<CustomPaletteDefinition> OnLiveUpdate { get; set; }

        // UI constants
        private const int PreviewWidth = 512;
        private const int PreviewHeight = 48;

        // runtime state
        private PaletteSet _currentSet;
        private int _currentSetIndex;

        // UI references
        private CheckBox chkMulti;
        private TextBox txtPaletteId; // legacy hidden single mode
        private TextBox txtSearch;
        private ListBox lstPalettes;
        private System.Windows.Controls.Image imgBigPreview;
        private StackPanel panelSetBrowse;
        private TextBlock lblSetIndex;
        private Slider sldSetIndex;
        private Button btnUseSetPalette;
        private StackPanel panelSingle; // hidden
        private TextBox txtRanges;      // hidden
        private StackPanel panelMulti;
        private TextBox txtMulti;
        private TextBox txtShade;
        private Slider sldShade;
        private Button btnOk;
        private Button btnSave;
        private Button btnLoad;
        private System.Windows.Controls.Image imgRangePreview;
        private TextBox txtColorSearch;
        private Button btnColorFind;
        private TextBlock lblColorResult;
        private ListView _lstRanges; // side list of parsed ranges

        // Display model for ranges
        private class RangeDisplay
        {
            public uint PaletteId { get; set; }
            public uint Offset { get; set; }
            public uint Length { get; set; }
            public string PaletteHex => $"0x{PaletteId:X8}";
            public string OffsetLength => $"{Offset}:{Length}";
        }

        // New table row model
        private class PaletteEntryRow : INotifyPropertyChanged
        {
            private uint _paletteSetId;
            private string _rangesText;
            private bool _isLocked;
            public uint PaletteSetId { get => _paletteSetId; set { if (_paletteSetId != value) { _paletteSetId = value; OnPropertyChanged(nameof(PaletteSetId)); OnPropertyChanged(nameof(PaletteHex)); } } }
            public string RangesText { get => _rangesText; set { if (_rangesText != value) { _rangesText = value; OnPropertyChanged(nameof(RangesText)); } } }
            public bool IsLocked { get => _isLocked; set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(nameof(IsLocked)); } } }
            public string PaletteHex => $"0x{PaletteSetId:X8}";
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private ObservableCollection<PaletteEntryRow> _rows = new ObservableCollection<PaletteEntryRow>();
        private DataGrid _gridEntries;  // new table UI
        private Button _btnAddRow;
        private Button _btnRemoveRow;
        private bool _freezeLines; // prevents further changes to generated lines textbox after initial snapshot
        private CheckBox _chkLockAll; // global lock toggle

        public CustomPaletteDialog()
        {
            Title = "Custom Palette";
            Width = 860;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Content = BuildUI();
            Loaded += OnLoaded;
        }

        #region UI Build
        private UIElement BuildUI()
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(8) };

            // Buttons
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            btnLoad = new Button { Content = "Load", Width = 80, Margin = new Thickness(0, 0, 8, 0) }; btnLoad.Click += btnLoad_Click;
            btnSave = new Button { Content = "Save", Width = 80, Margin = new Thickness(0, 0, 8, 0) }; btnSave.Click += btnSave_Click;
            btnOk = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true }; btnOk.Click += btnOk_Click;
            var btnCancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            buttons.Children.Add(btnLoad); buttons.Children.Add(btnSave); buttons.Children.Add(btnOk); buttons.Children.Add(btnCancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Shade controls
            var shadeGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shadeGrid.Children.Add(new TextBlock { Text = "Shade (0-1):", VerticalAlignment = VerticalAlignment.Center });
            txtShade = new TextBox { Height = 24, Text = "0" }; Grid.SetColumn(txtShade, 1); shadeGrid.Children.Add(txtShade);
            sldShade = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            sldShade.ValueChanged += sldShade_ValueChanged; Grid.SetColumn(sldShade, 3); shadeGrid.Children.Add(sldShade);
            DockPanel.SetDock(shadeGrid, Dock.Bottom);
            root.Children.Add(shadeGrid);

            // Top controls stack
            var topStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 8) };
            chkMulti = new CheckBox { Content = "Multi-Palette Mode", Margin = new Thickness(0, 0, 0, 8), IsChecked = true, Visibility = Visibility.Collapsed };
            chkMulti.Checked += chkMulti_Checked; chkMulti.Unchecked += chkMulti_Checked; topStack.Children.Add(chkMulti);
            var idGrid = new Grid();
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            idGrid.Children.Add(new TextBlock { Text = "Palette/Set ID", Visibility = Visibility.Collapsed });
            txtPaletteId = new TextBox { Height = 24, Visibility = Visibility.Collapsed }; Grid.SetColumn(txtPaletteId, 1); idGrid.Children.Add(txtPaletteId);
            idGrid.Children.Add(new TextBlock { Text = "Search:", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(idGrid.Children[^1], 3);
            txtSearch = new TextBox { Height = 24 }; txtSearch.TextChanged += txtSearch_TextChanged; Grid.SetColumn(txtSearch, 4); idGrid.Children.Add(txtSearch);
            topStack.Children.Add(idGrid);

            // Color search row
            var colorGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colorGrid.Children.Add(new TextBlock { Text = "Color Name / Hex:", VerticalAlignment = VerticalAlignment.Center });
            txtColorSearch = new TextBox { Height = 24, Margin = new Thickness(6, 0, 6, 0), ToolTip = "Enter CSS color name (e.g. 'crimson') or hex (#RRGGBB / 0xRRGGBB)." }; Grid.SetColumn(txtColorSearch, 1); colorGrid.Children.Add(txtColorSearch);
            btnColorFind = new Button { Content = "Find Closest", Width = 110, Height = 24 }; btnColorFind.Click += BtnColorFind_Click; Grid.SetColumn(btnColorFind, 2); colorGrid.Children.Add(btnColorFind);
            lblColorResult = new TextBlock { Text = "", Margin = new Thickness(8, 4, 0, 0), TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(lblColorResult, 3); colorGrid.Children.Add(lblColorResult);
            topStack.Children.Add(colorGrid);
            DockPanel.SetDock(topStack, Dock.Top); root.Children.Add(topStack);

            // Main content grid
            var mainGrid = new Grid();
            // Replace fixed widths with resizable star columns + splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });   // palettes + ranges container
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });  // details

            // Palettes + ranges side-by-side (own grid with splitter)
            var palRangeGrid = new Grid();
            palRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 }); // palettes
            palRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // inner splitter
            palRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 }); // ranges

            lstPalettes = new ListBox { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            lstPalettes.SelectionChanged += lstPalettes_SelectionChanged;
            var palBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 0), Child = lstPalettes };
            palRangeGrid.Children.Add(palBorder); // column 0 default

            // Inner splitter between palette list and ranges
            var innerSplitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60))
            };
            Grid.SetColumn(innerSplitter, 1);
            palRangeGrid.Children.Add(innerSplitter);

            _lstRanges = new ListView { Margin = new Thickness(0, 0, 0, 0) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Palette", DisplayMemberBinding = new System.Windows.Data.Binding("PaletteHex") });
            gv.Columns.Add(new GridViewColumn { Header = "Offset:Len", DisplayMemberBinding = new System.Windows.Data.Binding("OffsetLength") });
            _lstRanges.View = gv; _lstRanges.SelectionChanged += Ranges_SelectionChanged;
            var rngBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136)), BorderThickness = new Thickness(1), Child = _lstRanges }; Grid.SetColumn(rngBorder, 2); palRangeGrid.Children.Add(rngBorder);
            mainGrid.Children.Add(palRangeGrid); // stays in column 0

            // Outer splitter between left (pal/range) and right (details)
            var outerSplitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50))
            };
            Grid.SetColumn(outerSplitter, 1);
            mainGrid.Children.Add(outerSplitter);

            // Details panel
            var detailsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(detailsScroll, 2); mainGrid.Children.Add(detailsScroll);
            var detailsStack = new StackPanel(); detailsScroll.Content = detailsStack;
            detailsStack.Children.Add(new TextBlock { Text = "Preview:", FontWeight = FontWeights.Bold });
            imgBigPreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            detailsStack.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 8), BorderBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), Height = 48, Child = imgBigPreview });

            panelSetBrowse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
            lblSetIndex = new TextBlock { Text = "Set Index: 0", Width = 110, VerticalAlignment = VerticalAlignment.Center };
            sldSetIndex = new Slider { Width = 260, Minimum = 0, Maximum = 0, IsSnapToTickEnabled = true, TickFrequency = 1 }; sldSetIndex.ValueChanged += sldSetIndex_ValueChanged;
            btnUseSetPalette = new Button { Content = "Use", Width = 60 }; btnUseSetPalette.Click += btnUseSetPalette_Click;
            panelSetBrowse.Children.Add(lblSetIndex); panelSetBrowse.Children.Add(sldSetIndex); panelSetBrowse.Children.Add(btnUseSetPalette); detailsStack.Children.Add(panelSetBrowse);

            panelSingle = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
            panelSingle.Children.Add(new TextBlock { Text = "Ranges (off:len,...):" });
            txtRanges = new TextBox { Height = 24 }; panelSingle.Children.Add(txtRanges); detailsStack.Children.Add(panelSingle);

            panelMulti = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Visible };
            panelMulti.Children.Add(new TextBlock { Text = "Palette / Ranges Table:" });

            // Global lock toggle
            _chkLockAll = new CheckBox { Content = "Lock All Rows", Margin = new Thickness(0, 2, 0, 2) };
            _chkLockAll.Checked += (_, __) => { foreach (var r in _rows) r.IsLocked = true; _gridEntries.Items.Refresh(); };
            _chkLockAll.Unchecked += (_, __) => { foreach (var r in _rows) r.IsLocked = false; _gridEntries.Items.Refresh(); };
            panelMulti.Children.Add(_chkLockAll);

            _gridEntries = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                ItemsSource = _rows,
                Height = 180,
                Margin = new Thickness(0, 2, 0, 4),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                IsReadOnly = false
            };
            _gridEntries.SelectionChanged += (_, __) => { UpdateRangeHighlight(); };
            _gridEntries.CellEditEnding += GridEntries_CellEditEnding;
            _gridEntries.BeginningEdit += GridEntries_BeginningEdit;

            // Columns: Lock, Palette, Ranges
            var colLock = new DataGridCheckBoxColumn { Header = "Lock", Binding = new System.Windows.Data.Binding("IsLocked") { Mode = System.Windows.Data.BindingMode.TwoWay } };
            var colPal = new DataGridTextColumn { Header = "Palette/Set ID", Binding = new System.Windows.Data.Binding("PaletteHex") { Mode = System.Windows.Data.BindingMode.OneWay } };
            var colRanges = new DataGridTextColumn { Header = "Ranges (off:len,...)" , Binding = new System.Windows.Data.Binding("RangesText") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.Explicit } };
            _gridEntries.Columns.Add(colLock);
            _gridEntries.Columns.Add(colPal);
            _gridEntries.Columns.Add(colRanges);
            panelMulti.Children.Add(_gridEntries);

            var rowBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _btnAddRow = new Button { Content = "Add", Width = 60, Margin = new Thickness(0, 0, 6, 0) }; _btnAddRow.Click += (_, __) => AddRowFromSelection();
            _btnRemoveRow = new Button { Content = "Remove", Width = 70 }; _btnRemoveRow.Click += (_, __) => RemoveSelectedRow();
            rowBtnPanel.Children.Add(_btnAddRow); rowBtnPanel.Children.Add(_btnRemoveRow);
            panelMulti.Children.Add(rowBtnPanel);

            panelMulti.Children.Add(new TextBlock { Text = "Generated Lines (read-only):" });
            txtMulti = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinLines = 4, IsReadOnly = true };
            panelMulti.Children.Add(txtMulti);

            panelMulti.Children.Add(new TextBlock { Text = "Line Highlight Preview:" });
            imgRangePreview = new System.Windows.Controls.Image { Height = 64, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            panelMulti.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)), Height = 64, Child = imgRangePreview });
            detailsStack.Children.Add(panelMulti);

            root.Children.Add(mainGrid);
            return root;
        }

        private void GridEntries_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Commit ranges edits explicitly
            if (e.Column.DisplayIndex == 1)
            {
                if (e.Row.Item is PaletteEntryRow row)
                {
                    if (e.EditingElement is TextBox tb)
                    {
                        row.RangesText = tb.Text.Trim();
                        SyncTextLinesFromRows();
                        RefreshRangeList();
                        UpdateRangeHighlight();
                        DoLiveUpdate();
                    }
                }
            }
        }

        private void GridEntries_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is PaletteEntryRow row && row.IsLocked)
            {
                e.Cancel = true; // prevent editing locked row
            }
        }

        private void AddRowFromSelection()
        {
            uint palId = 0;
            if (lstPalettes.SelectedItem is ListBoxItem li)
                palId = (uint)li.Tag;
            else if (_currentSet != null && _currentSet.PaletteList.Count > 0)
                palId = _currentSet.PaletteList[0];
            else if (AvailablePaletteIDs?.Count > 0)
                palId = AvailablePaletteIDs[0];
            if (palId == 0) return;
            var newRow = new PaletteEntryRow { PaletteSetId = palId, RangesText = "0:1", IsLocked = _chkLockAll.IsChecked == true };
            _rows.Add(newRow);
            SyncTextLinesFromRows();
            RefreshRangeList();
            UpdateRangeHighlight();
            DoLiveUpdate();
            _gridEntries.SelectedItem = newRow;
        }

        private void RemoveSelectedRow()
        {
            if (_gridEntries.SelectedItem is PaletteEntryRow row)
            {
                if (row.IsLocked) return; // do not remove locked row
                _rows.Remove(row);
                SyncTextLinesFromRows();
                RefreshRangeList();
                UpdateRangeHighlight();
                DoLiveUpdate();
            }
        }

        private void SyncRowsFromText()
        {
            _rows.Clear();
            var text = txtMulti.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                try
                {
                    uint pal = ParseUInt(parts[0]);
                    var ranges = string.Join(" ", parts.Skip(1)).Replace(' ', ',');
                    _rows.Add(new PaletteEntryRow { PaletteSetId = pal, RangesText = ranges });
                }
                catch { }
            }
        }

        private void SyncTextLinesFromRows()
        {
            if (_freezeLines) return; // keep original snapshot
            txtMulti.Text = string.Join(System.Environment.NewLine, _rows.Select(r => $"0x{r.PaletteSetId:X8} {r.RangesText.Replace(' ', ',')}"));
        }

        #endregion

        #region Lifecycle / Live update
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateList();
            if (StartingDefinition != null)
            {
                ApplyLoadedPreset(StartingDefinition);
                if (StartingDefinition.Multi) chkMulti.IsChecked = true;
            }
            else
            {
                // initialize rows from any existing text (empty initially)
                SyncRowsFromText();
                SyncTextLinesFromRows(); // initial snapshot (will be empty)
            }
            // Freeze the generated lines so they remain constant for the lifetime of the dialog
            _freezeLines = true;
            HookLiveEvents();
            UpdateBigPreviewImage();
            UpdateRangeHighlight();
            RefreshRangeList();
        }

        private void HookLiveEvents()
        {
            txtPaletteId.TextChanged += (_, __) => DoLiveUpdate();
            txtRanges.TextChanged += (_, __) => DoLiveUpdate();
            sldShade.ValueChanged += (_, __) => DoLiveUpdate();
        }

        private void DoLiveUpdate()
        {
            if (OnLiveUpdate == null) return;
            if (TryBuildDefinition(out var def)) OnLiveUpdate(def);
        }
        #endregion

        #region Definition building
        private bool TryBuildDefinition(out CustomPaletteDefinition def)
        {
            def = null;
            try
            {
                var tmp = new CustomPaletteDefinition { Multi = true, Shade = ParseFloat(txtShade.Text.Trim(), 0) };
                foreach (var row in _rows)
                {
                    var rangeSpec = row.RangesText.Replace(',', ' ');
                    var entry = new CustomPaletteEntry { PaletteSetId = row.PaletteSetId, Ranges = RangeParser.ParseRanges(rangeSpec) };
                    if (entry.Ranges.Count == 0) return false; tmp.Entries.Add(entry);
                }
                if (tmp.Entries.Count == 0) return false; def = tmp; return true;
            }
            catch { return false; }
        }
        #endregion

        #region Palette list & preview
        private void PopulateList()
        {
            if (AvailablePaletteIDs == null) return;
            lstPalettes.Items.Clear();
            foreach (var id in AvailablePaletteIDs)
                lstPalettes.Items.Add(BuildPaletteListItem(id));
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
                    { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; }
                }
                wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                return wb;
            }
            catch { return null; }
        }

        private void UpdateBigPreviewImage()
        {
            if (_currentSet != null)
            {
                var palId = _currentSet.PaletteList[Math.Clamp(_currentSetIndex, 0, _currentSet.PaletteList.Count - 1)];
                imgBigPreview.Source = BuildPaletteBitmap(palId, PreviewWidth, PreviewHeight);
            }
            else if (lstPalettes.SelectedItem is ListBoxItem li)
                imgBigPreview.Source = BuildPaletteBitmap((uint)li.Tag, PreviewWidth, PreviewHeight);
        }
        #endregion

        #region Range highlight & side list
        private void UpdateRangeHighlight()
        {
            Palette palette = null;
            uint actualPaletteId = 0;
            string rangeSpec = null;
            if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow row)
            {
                uint palId = row.PaletteSetId;
                actualPaletteId = palId;
                if ((palId >> 24) == 0xF)
                {
                    try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0]; }
                    catch { imgRangePreview.Source = null; return; }
                }
                try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId); } catch { palette = null; }
                rangeSpec = row.RangesText.Replace(',', ' ');
            }
            else
            {
                // fallback to original logic using txtMulti caret if table empty
                var text = txtMulti.Text; if (string.IsNullOrWhiteSpace(text)) { imgRangePreview.Source = null; return; }
                int caret = txtMulti.CaretIndex;
                int lineStart = text.LastIndexOf('\n', Math.Clamp(caret - 1, 0, text.Length - 1));
                if (lineStart == -1) lineStart = 0; else lineStart += 1;
                int lineEnd = text.IndexOf('\n', caret); if (lineEnd == -1) lineEnd = text.Length;
                var line = text.Substring(lineStart, lineEnd - lineStart).Trim();
                if (string.IsNullOrWhiteSpace(line)) { imgRangePreview.Source = null; return; }
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) { imgRangePreview.Source = null; return; }
                uint palId; try { palId = ParseUInt(parts[0]); } catch { imgRangePreview.Source = null; return; }
                actualPaletteId = palId;
                if ((palId >> 24) == 0xF)
                {
                    var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId);
                    if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0];
                }
                palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId);
                rangeSpec = string.Join(" ", parts.Skip(1));
            }

            if (palette == null || palette.Colors.Count == 0 || rangeSpec == null) { imgRangePreview.Source = null; return; }
            var ranges = RangeParser.ParseRanges(rangeSpec, tolerant: true, out _);
            int colors = palette.Colors.Count;
            int width = 512; int height = 64;
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[width * height * 4];
            for (int x = 0; x < width; x++)
            {
                int palIdx = (int)((double)x / width * (colors - 1));
                uint col = palette.Colors[palIdx];
                byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF;
                byte r = (byte)((col >> 16) & 0xFF);
                byte g = (byte)((col >> 8) & 0xFF);
                byte b = (byte)(col & 0xFF);
                bool highlighted = false;
                foreach (var rg in ranges)
                {
                    var start = (int)rg.Offset * 8;
                    var count = (int)rg.Length * 8;
                    if (palIdx >= start && palIdx < start + count) { highlighted = true; break; }
                }
                if (highlighted) { r = (byte)Math.Min(255, r + 60); g = (byte)Math.Min(255, g + 60); b = (byte)Math.Min(255, b + 60); }
                for (int y = 0; y < height; y++)
                { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; }
            }
            wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            imgRangePreview.Source = wb;
        }

        private void RefreshRangeList()
        {
            var items = new List<RangeDisplay>();
            foreach (var row in _rows)
            {
                var ranges = RangeParser.ParseRanges(row.RangesText.Replace(',', ' '), tolerant: true, out _);
                foreach (var r in ranges)
                    items.Add(new RangeDisplay { PaletteId = row.PaletteSetId, Offset = r.Offset, Length = r.Length });
            }
            _lstRanges.ItemsSource = items;
        }

        // Added missing event handlers
        private void BtnColorFind_Click(object sender, RoutedEventArgs e)
        {
            lblColorResult.Text = "";
            if (lstPalettes.SelectedItem is not ListBoxItem li) { lblColorResult.Text = "Select a palette first."; return; }
            uint id = (uint)li.Tag; uint actualPaletteId = id;
            if ((id >> 24) == 0xF)
            {
                try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(id); if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0]; }
                catch { lblColorResult.Text = "Failed reading palette set."; return; }
            }
            Palette palette; try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId); } catch { lblColorResult.Text = "Palette load failed."; return; }
            if (palette == null || palette.Colors.Count == 0) { lblColorResult.Text = "No colors."; return; }
            var query = txtColorSearch.Text.Trim(); if (string.IsNullOrEmpty(query)) { lblColorResult.Text = "Enter color name or hex."; return; }
            if (!TryParseColorQuery(query, out var targetR, out var targetG, out var targetB)) { lblColorResult.Text = "Unrecognized color."; return; }
            int bestIdx = -1; double bestDist = double.MaxValue; uint bestColor = 0;
            for (int i = 0; i < palette.Colors.Count; i++)
            {
                uint col = palette.Colors[i]; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF);
                double dr = r - targetR; double dg = g - targetG; double db = b - targetB; double dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist) { bestDist = dist; bestIdx = i; bestColor = col; }
            }
            if (bestIdx < 0) { lblColorResult.Text = "No match."; return; }
            var (nearestName, nearestHex, _) = ColorNameCatalog.GetNearestName((byte)((bestColor >> 16) & 0xFF), (byte)((bestColor >> 8) & 0xFF), (byte)(bestColor & 0xFF));
            lblColorResult.Text = $"Idx {bestIdx} -> #{(bestColor & 0xFFFFFF):X6} ~ {nearestName} (?={Math.Sqrt(bestDist):0.0})";
        }

        private void Ranges_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No-op; selection already reflected elsewhere.
        }
        #endregion

        #region Event Handlers
        private void lstPalettes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPalettes.SelectedItem is not ListBoxItem li) return;
            uint id = (uint)li.Tag;
            if ((id >> 24) == 0xF)
            {
                _currentSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(id);
                _currentSetIndex = 0;
                panelSetBrowse.Visibility = (_currentSet != null && _currentSet.PaletteList.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
                sldSetIndex.Maximum = (_currentSet?.PaletteList.Count ?? 1) - 1; sldSetIndex.Value = 0; lblSetIndex.Text = "Set Index: 0";
            }
            else { _currentSet = null; panelSetBrowse.Visibility = Visibility.Collapsed; }

            // If a row in the table is selected and not locked, update its palette ID to the clicked one
            if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow row && !row.IsLocked)
            {
                // Assign the raw id (palette set or single palette). Live update will resolve sets as needed.
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
        }

        private void HighlightRangesForPalette(uint palId)
        {
            if (_lstRanges?.ItemsSource is System.Collections.IEnumerable en)
            {
                foreach (var item in en)
                {
                    if (item is RangeDisplay rd && rd.PaletteId == palId)
                    { _lstRanges.SelectedItem = rd; _lstRanges.ScrollIntoView(rd); break; }
                }
            }
        }

        private void sldSetIndex_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (_currentSet == null) return; _currentSetIndex = (int)sldSetIndex.Value; lblSetIndex.Text = $"Set Index: {_currentSetIndex}"; UpdateBigPreviewImage(); }

        private void btnUseSetPalette_Click(object sender, RoutedEventArgs e)
        { if (_currentSet == null) return; if (_currentSetIndex < 0 || _currentSetIndex >= _currentSet.PaletteList.Count) return; HighlightRangesForPalette(_currentSet.PaletteList[_currentSetIndex]); }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = txtSearch.Text.Trim().ToLowerInvariant();
            lstPalettes.BeginInit(); lstPalettes.Items.Clear();
            foreach (var id in AvailablePaletteIDs)
            { var label = $"0x{id:X8}".ToLowerInvariant(); if (string.IsNullOrEmpty(filter) || label.Contains(filter)) lstPalettes.Items.Add(BuildPaletteListItem(id)); }
            lstPalettes.EndInit();
        }

        private void chkMulti_Checked(object sender, RoutedEventArgs e) => DoLiveUpdate();

        private void sldShade_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { txtShade.Text = sldShade.Value.ToString("0.###", CultureInfo.InvariantCulture); }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            try { if (!TryBuildDefinition(out var def)) throw new Exception("Invalid definition"); ResultDefinition = def; DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (ResultDefinition == null && TryBuildDefinition(out var temp)) ResultDefinition = temp; if (ResultDefinition == null) return;
            var name = Microsoft.VisualBasic.Interaction.InputBox("Preset Name:", "Save Custom Palette", ResultDefinition.Name ?? "MyPreset"); if (string.IsNullOrWhiteSpace(name)) return;
            ResultDefinition.Name = name.Trim(); CustomPaletteStore.SaveDefinition(ResultDefinition); MessageBox.Show(this, "Saved.", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var defs = CustomPaletteStore.LoadAll().ToList(); if (defs.Count == 0) { MessageBox.Show(this, "No saved presets.", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var picker = new PresetPickerWindow(defs) { Owner = this }; if (picker.ShowDialog() == true && picker.Selected != null) { ApplyLoadedPreset(picker.Selected); DoLiveUpdate(); }
        }

        private void ApplyLoadedPreset(CustomPaletteDefinition def)
        {
            chkMulti.IsChecked = true; // force multi
            _rows.Clear();
            foreach (var e in def.Entries)
            {
                var rtxt = string.Join(",", e.Ranges.Select(r => $"{r.Offset}:{r.Length}"));
                _rows.Add(new PaletteEntryRow { PaletteSetId = e.PaletteSetId, RangesText = rtxt, IsLocked = _chkLockAll.IsChecked == true });
            }
            if (!_freezeLines)
                SyncTextLinesFromRows();
            txtShade.Text = def.Shade.ToString("0.###", CultureInfo.InvariantCulture); sldShade.Value = def.Shade; ResultDefinition = def; UpdateBigPreviewImage(); UpdateRangeHighlight(); RefreshRangeList();
        }
        #endregion

        #region Helpers
        private static uint ParseUInt(string s)
        { if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToUInt32(s[2..], 16); if (uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return h; return uint.Parse(s, CultureInfo.InvariantCulture); }
        private static float ParseFloat(string s, float defVal) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0f, 1f) : defVal;

        private bool TryParseColorQuery(string query, out int r, out int g, out int b)
        {
            r = g = b = 0; string hex = null;
            if (query.StartsWith('#')) hex = query[1..];
            else if (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = query[2..];
            else if (query.Length == 6 && query.All(Uri.IsHexDigit)) hex = query;
            if (hex != null && hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hv))
            { r = (hv >> 16) & 0xFF; g = (hv >> 8) & 0xFF; b = hv & 0xFF; return true; }
            if (ColorNameCatalog.TryGet(query, out var val)) { r = (val >> 16) & 0xFF; g = (val >> 8) & 0xFF; b = val & 0xFF; return true; }
            var nearest = ColorNameCatalog.GetNearestNameBySpelling(query); if (nearest != null && ColorNameCatalog.TryGet(nearest, out var fval)) { r = (fval >> 16) & 0xFF; g = (fval >> 8) & 0xFF; b = fval & 0xFF; return true; }
            return false;
        }
        #endregion
    }
}
