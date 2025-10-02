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
using ACViewer.View.Controls; // added for RangeEditorControl

namespace ACViewer.View
{
    public partial class CustomPaletteDialog : Window
    {
        // NEW: Setup IDs list inside dialog (0x02 files) to mirror ClothingTableList.SetupIds
        private ListBox _lstSetupIds;
        private bool _setupPanelInitialized;

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
        private Button btnExport; // new export button
        private System.Windows.Controls.Image imgRangePreview;
        private TextBox txtColorSearch;
        private Button btnColorFind;
        private TextBlock lblColorResult;
        private ListView _lstRanges; // side list of parsed ranges

        // Range editor (new feature)
        private RangeEditorControl _rangeEditor;
        private Border _rangeEditorHost;

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

        // Texture remap models
        private class TextureRow { public int PartIndex { get; set; } public uint OldId { get; set; } public uint NewId { get; set; } public bool IsLocked { get; set; } public string OldHex => $"0x{OldId:X8}"; public string NewHex => $"0x{NewId:X8}"; }
        private readonly List<TextureRow> _textureRows = new();
        private List<uint> _availableTextureIds = new();
        private ListBox lstTextures; private DataGrid gridTextures; private CheckBox chkTexLockAll; private Button btnTexAdd; private Button btnTexRemove; private Button btnTexSave; private Button btnTexLoad; private Button btnTexOk; private TextBox txtTexSearch; private System.Windows.Controls.Image imgTexPreviewOld; private System.Windows.Controls.Image imgTexPreviewNew; private TabControl _tabControl;

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
            var ui = BuildOriginalUI();
            _tabControl = ui as TabControl;
            if (_tabControl != null)
            {
                foreach (var item in _tabControl.Items)
                {
                    if (item is TabItem ti && ti.Header?.ToString() == "Textures")
                    {
                        var grid = new Grid { Margin = new Thickness(8) };
                        BuildTextureTab(grid);
                        ti.Content = grid; break;
                    }
                }
            }
            return ui;
        }

        private void BuildTextureTab(Grid texTabContent)
        {
            InitTextureTabData();
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var leftStack = new DockPanel();
            var searchPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            searchPanel.Children.Add(new TextBlock { Text = "Search:", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            txtTexSearch = new TextBox { Height = 22, Width = 160 }; txtTexSearch.TextChanged += (_, __) => FilterTextureList(); searchPanel.Children.Add(txtTexSearch);
            DockPanel.SetDock(searchPanel, Dock.Top); leftStack.Children.Add(searchPanel);
            lstTextures = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            lstTextures.SelectionChanged += (_, __) => ApplyTextureSelection(); leftStack.Children.Add(lstTextures);
            root.Children.Add(leftStack);
            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)) }; Grid.SetColumn(splitter, 1); root.Children.Add(splitter);
            var rightStack = new DockPanel(); Grid.SetColumn(rightStack, 2);
            var buttonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            btnTexLoad = new Button { Content = "Load", Width = 70, Margin = new Thickness(0, 0, 6, 0) }; btnTexLoad.Click += (_, __) => LoadTexturePreset();
            btnTexSave = new Button { Content = "Save", Width = 70, Margin = new Thickness(0, 0, 6, 0) }; btnTexSave.Click += (_, __) => SaveTexturePreset();
            btnTexOk = new Button { Content = "Apply", Width = 80 }; btnTexOk.Click += (_, __) => { ApplyTextureResult(); };
            buttonBar.Children.Add(btnTexLoad); buttonBar.Children.Add(btnTexSave); buttonBar.Children.Add(btnTexOk);
            DockPanel.SetDock(buttonBar, Dock.Bottom); rightStack.Children.Add(buttonBar);
            var tableStack = new StackPanel { Orientation = Orientation.Vertical };
            chkTexLockAll = new CheckBox { Content = "Lock All Rows", Margin = new Thickness(0, 0, 0, 4) }; chkTexLockAll.Checked += (_, __) => { foreach (var r in _textureRows) r.IsLocked = true; gridTextures.Items.Refresh(); }; chkTexLockAll.Unchecked += (_, __) => { foreach (var r in _textureRows) r.IsLocked = false; gridTextures.Items.Refresh(); };
            tableStack.Children.Add(chkTexLockAll);
            gridTextures = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, Height = 180, ItemsSource = _textureRows, Margin = new Thickness(0, 0, 0, 4) };
            gridTextures.SelectionChanged += (_, __) => UpdateTexturePreview();
            gridTextures.Columns.Add(new DataGridCheckBoxColumn { Header = "Lock", Binding = new System.Windows.Data.Binding("IsLocked") { Mode = System.Windows.Data.BindingMode.TwoWay } });
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "Part", Binding = new System.Windows.Data.Binding("PartIndex") { Mode = System.Windows.Data.BindingMode.OneWay }, IsReadOnly = true });
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "Old", Binding = new System.Windows.Data.Binding("OldHex") { Mode = System.Windows.Data.BindingMode.OneWay }, IsReadOnly = true });
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "New", Binding = new System.Windows.Data.Binding("NewHex") { Mode = System.Windows.Data.BindingMode.OneWay }, IsReadOnly = true });
            tableStack.Children.Add(gridTextures);
            var rowButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            btnTexAdd = new Button { Content = "Add", Width = 60, Margin = new Thickness(0, 0, 6, 0) }; btnTexAdd.Click += (_, __) => AddTextureRow();
            btnTexRemove = new Button { Content = "Remove", Width = 70 }; btnTexRemove.Click += (_, __) => RemoveTextureRow();
            rowButtons.Children.Add(btnTexAdd); rowButtons.Children.Add(btnTexRemove); tableStack.Children.Add(rowButtons);
            tableStack.Children.Add(new TextBlock { Text = "Preview (Old / New)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 2) });
            var prevGrid = new Grid(); prevGrid.ColumnDefinitions.Add(new ColumnDefinition()); prevGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); prevGrid.ColumnDefinitions.Add(new ColumnDefinition());
            imgTexPreviewOld = new System.Windows.Controls.Image { Height = 96, Stretch = Stretch.Uniform };
            imgTexPreviewNew = new System.Windows.Controls.Image { Height = 96, Stretch = Stretch.Uniform };
            prevGrid.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Child = imgTexPreviewOld });
            var arrow = new TextBlock { Text = "?", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 24 }; Grid.SetColumn(arrow, 1); prevGrid.Children.Add(arrow);
            Grid.SetColumn(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = imgTexPreviewNew }, 2);
            prevGrid.Children.Add(new Border { Visibility = Visibility.Collapsed }); // placeholder
            tableStack.Children.Add(prevGrid);
            rightStack.Children.Add(tableStack);
            root.Children.Add(rightStack);
            texTabContent.Children.Add(root);
            RebuildTextureList();
        }

        private void InitTextureTabData()
        {
            _textureRows.Clear(); _availableTextureIds.Clear();
            try
            {
                var setupInst = ModelViewer.Instance?.Setup; var setup = setupInst?.Setup; if (setup == null) return;
                for (int partIndex = 0; partIndex < setup.Parts.Count; partIndex++)
                {
                    var gfxObj = setup.Parts[partIndex]; if (gfxObj?._gfxObj == null) continue;
                    foreach (var surfaceId in gfxObj._gfxObj.Surfaces)
                    {
                        var surface = DatManager.PortalDat.ReadFromDat<Surface>(surfaceId);
                        if (surface.OrigTextureId != 0)
                        {
                            var oldTex = surface.OrigTextureId;
                            if (!_textureRows.Exists(r => r.PartIndex == partIndex && r.OldId == oldTex))
                                _textureRows.Add(new TextureRow { PartIndex = partIndex, OldId = oldTex, NewId = oldTex });
                            if (!_availableTextureIds.Contains(oldTex)) _availableTextureIds.Add(oldTex);
                        }
                    }
                }
                foreach (var id in DatManager.PortalDat.AllFiles.Keys.Where(i => (i >> 24) == 0x05).Take(300))
                    if (!_availableTextureIds.Contains(id)) _availableTextureIds.Add(id);
            }
            catch { }
            _availableTextureIds = _availableTextureIds.Distinct().OrderBy(i => i).ToList();
        }

        private void RebuildTextureList(string filter = null)
        {
            lstTextures.BeginInit(); lstTextures.Items.Clear();
            foreach (var id in _availableTextureIds)
            {
                var label = $"0x{id:X8}"; if (!string.IsNullOrEmpty(filter) && !label.ToLowerInvariant().Contains(filter)) continue;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var img = new System.Windows.Controls.Image { Width = 96, Height = 32, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 4, 0) };
                img.Source = BuildTextureBitmap(id);
                sp.Children.Add(img); sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                lstTextures.Items.Add(new ListBoxItem { Content = sp, Tag = id });
            }
            lstTextures.EndInit();
        }

        private ImageSource BuildTextureBitmap(uint id)
        {
            try
            {
                var tex = TextureCache.Get(id, null, null, useCache: false); if (tex == null) return null;
                var w = tex.Width; var h = tex.Height; var sampleH = Math.Min(h, 32);
                var data = new Microsoft.Xna.Framework.Color[w * h]; tex.GetData(data);
                var bmp = new WriteableBitmap(w, sampleH, 96, 96, PixelFormats.Bgra32, null);
                var pixels = new byte[w * sampleH * 4];
                for (int y = 0; y < sampleH; y++) for (int x = 0; x < w; x++) { var c = data[y * w + x]; var idx = (y * w + x) * 4; pixels[idx] = c.B; pixels[idx + 1] = c.G; pixels[idx + 2] = c.R; pixels[idx + 3] = c.A; }
                bmp.WritePixels(new Int32Rect(0, 0, w, sampleH), pixels, w * 4, 0);
                return bmp;
            }
            catch { return null; }
        }

        private void FilterTextureList() => RebuildTextureList(txtTexSearch.Text.Trim().ToLowerInvariant());
        private void ApplyTextureSelection() { if (lstTextures.SelectedItem is not ListBoxItem item) return; var id = (uint)item.Tag; if (gridTextures.SelectedItem is TextureRow row && !row.IsLocked) { row.NewId = id; gridTextures.Items.Refresh(); UpdateTexturePreview(); DoLiveTextureUpdateOnly(); } }
        private void UpdateTexturePreview() { if (gridTextures.SelectedItem is not TextureRow row) { imgTexPreviewOld.Source = null; imgTexPreviewNew.Source = null; return; } imgTexPreviewOld.Source = BuildTextureBitmap(row.OldId); imgTexPreviewNew.Source = BuildTextureBitmap(row.NewId); }
        private void AddTextureRow() { _textureRows.Add(new TextureRow { PartIndex = 0, OldId = 0, NewId = 0 }); gridTextures.Items.Refresh(); }
        private void RemoveTextureRow() { if (gridTextures.SelectedItem is TextureRow row) { _textureRows.Remove(row); gridTextures.Items.Refresh(); DoLiveTextureUpdateOnly(); } }
        private CustomTextureDefinition BuildTextureDefinition() { if (_textureRows.Count == 0) return null; var def = new CustomTextureDefinition(); foreach (var r in _textureRows) def.Entries.Add(new CustomTextureEntry { PartIndex = r.PartIndex, OldId = r.OldId, NewId = r.NewId }); return def; }
        private void ApplyTextureDefinition(CustomTextureDefinition def) { _textureRows.Clear(); foreach (var e in def.Entries) _textureRows.Add(new TextureRow { PartIndex = e.PartIndex, OldId = e.OldId, NewId = e.NewId }); gridTextures.Items.Refresh(); }
        private void SaveTexturePreset() { var def = BuildTextureDefinition(); if (def == null) return; var name = Microsoft.VisualBasic.Interaction.InputBox("Preset Name:", "Save Texture Preset", def.Name ?? "TexPreset"); if (string.IsNullOrWhiteSpace(name)) return; def.Name = name.Trim(); CustomTextureStore.SaveDefinition(def); MessageBox.Show(this, "Saved."); }
        private void LoadTexturePreset() { var defs = CustomTextureStore.LoadAll().ToList(); if (defs.Count == 0) { MessageBox.Show(this, "No presets."); return; } var list = string.Join(",", defs.Select(d => d.Name)); var chosen = Microsoft.VisualBasic.Interaction.InputBox($"Available: {list}", "Load Texture Preset", defs.First().Name); var def = defs.FirstOrDefault(d => d.Name == chosen); if (def == null) return; ApplyTextureDefinition(def); DoLiveTextureUpdateOnly(); }
        private void ApplyTextureResult() { DoLiveTextureUpdateOnly(); MessageBox.Show(this, "Applied (session only)"); }
        private void DoLiveTextureUpdateOnly()
        {
            try
            {
                var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) { MessageBox.Show(this, "No clothing item loaded."); return; }
                var setupList = ClothingTableList.Instance.SetupIds; if (setupList.SelectedItem is not ListBoxItem li) return; var setupId = (uint)li.DataContext;
                var obj = new ACViewer.Model.ObjDesc(setupId, clothing.Id, PaletteTemplate.Undef, 0f);
                if (ResultDefinition != null)
                {
                    var palSubs = new List<CloSubPalette>();
                    foreach (var entry in ResultDefinition.Entries)
                    {
                        var sub = new CloSubPalette { PaletteSet = entry.PaletteSetId };
                        foreach (var rg in entry.Ranges) sub.Ranges.Add(new CloSubPaletteRange { Offset = (uint)rg.Offset * 8, NumColors = (uint)rg.Length * 8 });
                        palSubs.Add(sub);
                    }
                    if (palSubs.Count > 0) obj.PaletteChanges = new PaletteChanges(palSubs, ResultDefinition.Shade);
                }
                if (obj.PartChanges == null) obj.PartChanges = new Dictionary<uint, PartChange>();
                foreach (var tr in _textureRows)
                {
                    if (tr.OldId == 0 || tr.NewId == 0 || tr.OldId == tr.NewId) continue;
                    if (!obj.PartChanges.TryGetValue((uint)tr.PartIndex, out var pc))
                    { var setup = DatManager.PortalDat.ReadFromDat<SetupModel>(setupId); if (tr.PartIndex < setup.Parts.Count) pc = new PartChange(setup.Parts[tr.PartIndex]); else continue; obj.PartChanges.Add((uint)tr.PartIndex, pc); }
                    pc.AddTexture(tr.OldId, tr.NewId);
                }
                ModelViewer.Instance.Setup = new SetupInstance(setupId, obj);
            }
            catch { }
        }

        private UIElement BuildOriginalUI()
        {
            // (original large UI) - truncated only area where range editor inserted
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            btnLoad = new Button { Content = "Load", Width = 80, Margin = new Thickness(0, 0, 8, 0) }; btnLoad.Click += btnLoad_Click;
            btnSave = new Button { Content = "Save", Width = 80, Margin = new Thickness(0, 0, 8, 0) }; btnSave.Click += btnSave_Click;
            btnOk = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true }; btnOk.Click += btnOk_Click;
            btnExport = new Button { Content = "Export", Width = 90, Margin = new Thickness(0, 0, 8, 0) }; btnExport.Click += BtnExport_Click;
            var btnCancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            buttons.Children.Add(btnLoad); buttons.Children.Add(btnSave); buttons.Children.Add(btnOk); buttons.Children.Add(btnExport); buttons.Children.Add(btnCancel);
            DockPanel.SetDock(buttons, Dock.Bottom);

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
            DockPanel.SetDock(topStack, Dock.Top);

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

            var palRangeGrid = new Grid();
            palRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
            palRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            palRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });

            var leftStack = new StackPanel { Orientation = Orientation.Vertical };
            leftStack.Children.Add(new TextBlock { Text = "Setups (0x02):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
            _lstSetupIds = new ListBox { Height = 110, Margin = new Thickness(0, 0, 0, 6) };
            _lstSetupIds.SelectionChanged += LstSetupIds_SelectionChanged;
            leftStack.Children.Add(_lstSetupIds);
            leftStack.Children.Add(new TextBlock { Text = "Palettes:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
            lstPalettes = new ListBox { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 320 };
            ScrollViewer.SetVerticalScrollBarVisibility(lstPalettes, ScrollBarVisibility.Auto);
            lstPalettes.SelectionChanged += lstPalettes_SelectionChanged;
            leftStack.Children.Add(lstPalettes);
            var palBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136)), BorderThickness = new Thickness(1), Margin = new Thickness(0), Child = leftStack };
            palRangeGrid.Children.Add(palBorder);

            var innerSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)) };
            Grid.SetColumn(innerSplitter, 1); palRangeGrid.Children.Add(innerSplitter);

            _lstRanges = new ListView { Margin = new Thickness(0, 0, 0, 0) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Palette", DisplayMemberBinding = new System.Windows.Data.Binding("PaletteHex") });
            gv.Columns.Add(new GridViewColumn { Header = "Offset:Len", DisplayMemberBinding = new System.Windows.Data.Binding("OffsetLength") });
            _lstRanges.View = gv;
            var rngBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136)), BorderThickness = new Thickness(1), Child = _lstRanges }; Grid.SetColumn(rngBorder, 2); palRangeGrid.Children.Add(rngBorder);
            mainGrid.Children.Add(palRangeGrid);

            var outerSplitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)) }; Grid.SetColumn(outerSplitter, 1); mainGrid.Children.Add(outerSplitter);

            var detailsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(detailsScroll, 2); mainGrid.Children.Add(detailsScroll);
            var detailsStack = new StackPanel(); detailsScroll.Content = detailsStack;
            detailsStack.Children.Add(new TextBlock { Text = "Preview:", FontWeight = FontWeights.Bold });
            imgBigPreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            detailsStack.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 8), BorderBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), Height = 48, Child = imgBigPreview });

            // Range editor integration
            detailsStack.Children.Add(new TextBlock { Text = "Interactive Range Editor:", FontWeight = FontWeights.Bold });
            _rangeEditor = new RangeEditorControl { Height = 44, Margin = new Thickness(0, 2, 0, 6) };
            _rangeEditor.RangesChanged += (_, list) => ApplyRangeEditorToSelectedRow(list);
            _rangeEditorHost = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), Child = _rangeEditor, Height = 44 };
            detailsStack.Children.Add(_rangeEditorHost);
            detailsStack.Children.Add(new TextBlock { Text = "Drag to add (groups of 8 colors). Right-click range to remove.", FontSize = 11, Foreground = Brushes.Gray });

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
            _chkLockAll = new CheckBox { Content = "Lock All Rows", Margin = new Thickness(0, 2, 0, 2) }; _chkLockAll.Checked += (_, __) => { foreach (var r in _rows) r.IsLocked = true; _gridEntries.Items.Refresh(); }; _chkLockAll.Unchecked += (_, __) => { foreach (var r in _rows) r.IsLocked = false; _gridEntries.Items.Refresh(); }; panelMulti.Children.Add(_chkLockAll);
            _gridEntries = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = _rows, Height = 180, Margin = new Thickness(0, 2, 0, 4), HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.All, IsReadOnly = false };
            _gridEntries.SelectionChanged += (_, __) => { UpdateRangeHighlight(); SyncRangeEditorFromRow(); };
            _gridEntries.CellEditEnding += GridEntries_CellEditEnding; _gridEntries.BeginningEdit += GridEntries_BeginningEdit;
            var colLock = new DataGridCheckBoxColumn { Header = "Lock", Binding = new System.Windows.Data.Binding("IsLocked") { Mode = System.Windows.Data.BindingMode.TwoWay } };
            var colPal = new DataGridTextColumn { Header = "Palette/Set ID", Binding = new System.Windows.Data.Binding("PaletteHex") { Mode = System.Windows.Data.BindingMode.OneWay } };
            var colRanges = new DataGridTextColumn { Header = "Ranges (off:len,...)" , Binding = new System.Windows.Data.Binding("RangesText") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.Explicit } };
            _gridEntries.Columns.Add(colLock); _gridEntries.Columns.Add(colPal); _gridEntries.Columns.Add(colRanges); panelMulti.Children.Add(_gridEntries);
            var rowBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) }; _btnAddRow = new Button { Content = "Add", Width = 60, Margin = new Thickness(0, 0, 6, 0) }; _btnAddRow.Click += (_, __) => AddRowFromSelection(); _btnRemoveRow = new Button { Content = "Remove", Width = 70 }; _btnRemoveRow.Click += (_, __) => RemoveSelectedRow(); rowBtnPanel.Children.Add(_btnAddRow); rowBtnPanel.Children.Add(_btnRemoveRow); panelMulti.Children.Add(rowBtnPanel);
            panelMulti.Children.Add(new TextBlock { Text = "Generated Lines (read-only):" }); txtMulti = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinLines = 4, IsReadOnly = true }; panelMulti.Children.Add(txtMulti);
            panelMulti.Children.Add(new TextBlock { Text = "Line Highlight Preview:" }); imgRangePreview = new System.Windows.Controls.Image { Height = 64, Stretch = Stretch.Fill, SnapsToDevicePixels = true }; panelMulti.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)), Height = 64, Child = imgRangePreview }); detailsStack.Children.Add(panelMulti);

            var tab = new TabControl();
            var palTab = new TabItem { Header = "Palettes", Content = (UIElement)null };
            var texTabContent = new Grid { Margin = new Thickness(8) };
            texTabContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texTabContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            texTabContent.Children.Add(new TextBlock { Text = "Textures (WIP) — future implementation will allow mapping surface / texture IDs.", Margin = new Thickness(0, 0, 0, 8) });
            var placeholder = new TextBlock { Text = "Coming soon: Texture remapping tab.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontStyle = FontStyles.Italic };
            Grid.SetRow(placeholder, 1); texTabContent.Children.Add(placeholder);
            var texTab = new TabItem { Header = "Textures", Content = texTabContent };
            tab.Items.Add(palTab); tab.Items.Add(texTab);

            var palDock = new DockPanel();
            palDock.Children.Add(buttons);
            palDock.Children.Add(shadeGrid);
            palDock.Children.Add(topStack);
            palDock.Children.Add(mainGrid);
            palTab.Content = palDock;

            return tab;
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
                SyncRowsFromText();
                SyncTextLinesFromRows();
            }
            _freezeLines = true;
            HookLiveEvents();
            UpdateBigPreviewImage();
            UpdateRangeHighlight();
            RefreshRangeList();
            InitTextureTabData();
            PopulateSetupIds();
            SyncRangeEditorFromRow();
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
                if (_currentSet.PaletteList == null || _currentSet.PaletteList.Count == 0)
                {
                    imgBigPreview.Source = null; // nothing to show
                    return;
                }
                var safeIndex = Math.Clamp(_currentSetIndex, 0, _currentSet.PaletteList.Count - 1);
                var palId = _currentSet.PaletteList[safeIndex];
                imgBigPreview.Source = BuildPaletteBitmap(palId, PreviewWidth, PreviewHeight);
                return;
            }
            else if (lstPalettes.SelectedItem is ListBoxItem li)
            {
                imgBigPreview.Source = BuildPaletteBitmap((uint)li.Tag, PreviewWidth, PreviewHeight);
            }
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
                byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF);
                bool highlighted = false;
                foreach (var rg in ranges)
                {
                    var start = (int)rg.Offset * 8;
                    var count = (int)rg.Length * 8;
                    if (palIdx >= start && palIdx < start + count) { highlighted = true; break; }
                }
                if (highlighted) { r = (byte)Math.Min(255, r + 60); g = (byte)Math.Min(255, g + 60); b = (byte)Math.Min(255, b + 60); }
                for (int y = 0; y < height; y++) { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; }
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
        #endregion

        #region Range Editor Sync
        private void SyncRangeEditorFromRow()
        {
            if (_rangeEditor == null) return;
            if (_gridEntries?.SelectedItem is not PaletteEntryRow row) { _rangeEditor.SetPalette(null); _rangeEditor.SetRanges(Array.Empty<RangeDef>()); return; }
            uint palId = row.PaletteSetId; uint actual = palId;
            if ((palId >> 24) == 0xF)
            {
                try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set?.PaletteList?.Count > 0) actual = set.PaletteList[0]; } catch { }
            }
            Palette palette = null; try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actual); } catch { }
            _rangeEditor.SetPalette(palette);
            var parsed = RangeParser.ParseRanges(row.RangesText.Replace(',', ' '), tolerant: true, out _);
            _rangeEditor.SetRanges(parsed);
        }

        private void ApplyRangeEditorToSelectedRow(IReadOnlyList<RangeDef> list)
        {
            if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return;
            if (row.IsLocked) return;
            row.RangesText = string.Join(",", list.Select(r => $"{r.Offset}:{r.Length}"));
            RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); if (!_freezeLines) SyncTextLinesFromRows(); _gridEntries.Items.Refresh();
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
                if (_currentSet?.PaletteList != null && _currentSet.PaletteList.Count > 0)
                {
                    panelSetBrowse.Visibility = Visibility.Visible;
                    sldSetIndex.Maximum = _currentSet.PaletteList.Count - 1;
                    sldSetIndex.Value = 0; lblSetIndex.Text = "Set Index: 0";
                }
                else panelSetBrowse.Visibility = Visibility.Collapsed;
            }
            else { _currentSet = null; panelSetBrowse.Visibility = Visibility.Collapsed; }

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

        private void sldSetIndex_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_currentSet == null) return; _currentSetIndex = (int)sldSetIndex.Value; lblSetIndex.Text = $"Set Index: {_currentSetIndex}"; UpdateBigPreviewImage(); }
        private void btnUseSetPalette_Click(object sender, RoutedEventArgs e) { if (_currentSet == null) return; if (_currentSetIndex < 0 || _currentSetIndex >= _currentSet.PaletteList.Count) return; HighlightRangesForPalette(_currentSet.PaletteList[_currentSetIndex]); }
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = txtSearch.Text.Trim().ToLowerInvariant(); lstPalettes.BeginInit(); lstPalettes.Items.Clear(); foreach (var id in AvailablePaletteIDs) { var label = $"0x{id:X8}".ToLowerInvariant(); if (string.IsNullOrEmpty(filter) || label.Contains(filter)) lstPalettes.Items.Add(BuildPaletteListItem(id)); } lstPalettes.EndInit();
        }
        private void chkMulti_Checked(object sender, RoutedEventArgs e) => DoLiveUpdate();
        private void sldShade_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { txtShade.Text = sldShade.Value.ToString("0.###", CultureInfo.InvariantCulture); }
        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            try { if (!TryBuildDefinition(out var def)) throw new Exception("Invalid definition"); ResultDefinition = def; DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (ResultDefinition == null && TryBuildDefinition(out var temp)) ResultDefinition = temp; if (ResultDefinition == null) return; var name = Microsoft.VisualBasic.Interaction.InputBox("Preset Name:", "Save Custom Palette", ResultDefinition.Name ?? "MyPreset"); if (string.IsNullOrWhiteSpace(name)) return; ResultDefinition.Name = name.Trim(); CustomPaletteStore.SaveDefinition(ResultDefinition); MessageBox.Show(this, "Saved.", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var defs = CustomPaletteStore.LoadAll().ToList(); if (defs.Count == 0) { MessageBox.Show(this, "No saved presets.", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var picker = new PresetPickerWindow(defs) { Owner = this }; if (picker.ShowDialog() == true && picker.Selected != null) { ApplyLoadedPreset(picker.Selected); DoLiveUpdate(); }
            SyncRangeEditorFromRow();
        }
        private void ApplyLoadedPreset(CustomPaletteDefinition def)
        {
            chkMulti.IsChecked = true; _rows.Clear(); foreach (var e in def.Entries) { var rtxt = string.Join(",", e.Ranges.Select(r => $"{r.Offset}:{r.Length}")); _rows.Add(new PaletteEntryRow { PaletteSetId = e.PaletteSetId, RangesText = rtxt, IsLocked = _chkLockAll.IsChecked == true }); }
            if (!_freezeLines) SyncTextLinesFromRows(); txtShade.Text = def.Shade.ToString("0.###", CultureInfo.InvariantCulture); sldShade.Value = def.Shade; ResultDefinition = def; UpdateBigPreviewImage(); UpdateRangeHighlight(); RefreshRangeList(); SyncRangeEditorFromRow();
        }

        private void GridEntries_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row?.Item is not PaletteEntryRow row) return;
            if (row.IsLocked) { e.Cancel = true; return; }
            if (e.EditingElement is TextBox tb)
            {
                row.RangesText = tb.Text.Trim();
                SyncTextLinesFromRows();
                RefreshRangeList();
                UpdateRangeHighlight();
                DoLiveUpdate();
                SyncRangeEditorFromRow();
            }
        }

        private void GridEntries_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row?.Item is PaletteEntryRow row && row.IsLocked)
                e.Cancel = true;
        }

        private void AddRowFromSelection()
        {
            uint palId = 0;
            if (lstPalettes?.SelectedItem is ListBoxItem li) palId = (uint)li.Tag;
            else if (_currentSet != null && _currentSet.PaletteList != null && _currentSet.PaletteList.Count > 0) palId = _currentSet.PaletteList[0];
            else if (AvailablePaletteIDs?.Count > 0) palId = AvailablePaletteIDs[0];
            if (palId == 0) return;
            var newRow = new PaletteEntryRow { PaletteSetId = palId, RangesText = "0:1", IsLocked = _chkLockAll?.IsChecked == true };
            _rows.Add(newRow);
            SyncTextLinesFromRows();
            RefreshRangeList();
            UpdateRangeHighlight();
            DoLiveUpdate();
            _gridEntries.SelectedItem = newRow;
            SyncRangeEditorFromRow();
        }

        private void RemoveSelectedRow()
        {
            if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return;
            if (row.IsLocked) return;
            _rows.Remove(row);
            SyncTextLinesFromRows();
            RefreshRangeList();
            UpdateRangeHighlight();
            DoLiveUpdate();
            SyncRangeEditorFromRow();
        }
        #endregion

        #region SetupIdPanel
        private void PopulateSetupIds()
        {
            if (_lstSetupIds == null) return;
            _lstSetupIds.Items.Clear();
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null) return;
            foreach (var id in clothing.ClothingBaseEffects.Keys.OrderBy(k => k))
                _lstSetupIds.Items.Add(new ListBoxItem { Content = $"0x{id:X8}", Tag = id });
            var ext = ClothingTableList.Instance?.SetupIds;
            uint sel = 0; if (ext?.SelectedItem is ListBoxItem li) sel = (uint)li.DataContext;
            if (sel != 0)
            {
                for (int i = 0; i < _lstSetupIds.Items.Count; i++)
                    if (_lstSetupIds.Items[i] is ListBoxItem item && (uint)item.Tag == sel) { _lstSetupIds.SelectedIndex = i; return; }
            }
            if (_lstSetupIds.Items.Count > 0) _lstSetupIds.SelectedIndex = 0;
        }

        private void LstSetupIds_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_lstSetupIds?.SelectedItem is not ListBoxItem li) return;
            uint setupId = (uint)li.Tag;
            var ext = ClothingTableList.Instance?.SetupIds;
            if (ext != null)
            {
                for (int i = 0; i < ext.Items.Count; i++)
                {
                    if (ext.Items[i] is ListBoxItem other && (uint)other.DataContext == setupId) { ext.SelectedIndex = i; break; }
                }
            }
            DoLiveUpdate();
            DoLiveTextureUpdateOnly();
        }
        #endregion

        #region Export
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) { MessageBox.Show(this, "No clothing item loaded."); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON Files (*.json)|*.json", FileName = $"Clothing_{clothing.Id:X8}.json" };
                if (dlg.ShowDialog(this) != true) return;
                CustomTextureStore.ExportClothingTable(clothing, dlg.FileName);
                MessageBox.Show(this, "Export complete.");
            }
            catch (Exception ex)
            { MessageBox.Show(this, $"Export failed: {ex.Message}"); }
        }
        #endregion

        #region Helpers
        private static uint ParseUInt(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s[2..], 16);
            if (uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return hex;
            return uint.Parse(s, CultureInfo.InvariantCulture);
        }

        private static float ParseFloat(string s, float defVal)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? Math.Clamp(f, 0f, 1f) : defVal;

        private void SyncRowsFromText()
        {
            _rows.Clear();
            var text = txtMulti?.Text; if (string.IsNullOrWhiteSpace(text)) return;
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
            if (_freezeLines) return;
            txtMulti.Text = string.Join(System.Environment.NewLine, _rows.Select(r => $"0x{r.PaletteSetId:X8} {r.RangesText.Replace(' ', ',')}"));
        }
        #endregion
    }
}
