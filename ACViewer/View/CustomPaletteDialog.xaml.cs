using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
using System.Windows.Data;
using ACViewer.Converters;
using System.Threading;
using System.Collections.Specialized;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework.Graphics; // Texture2D only, avoid XNA Color ambiguity

#pragma warning disable 0169, 0649, 0414
namespace ACViewer.View
{
    // Converted to UserControl (formerly Window) for tabbed / docked usage only
    public partial class CustomPaletteDialog : UserControl
    {
        // Loading overlay elements
        private Grid _rootGrid;            // wraps entire control content
        private Border _loadingOverlay;    // semi-transparent overlay shown while initializing / caching
        private TextBlock _loadingText;
        private TabItem _texturesTab;      // reference to textures tab for visibility control
        private DataGridColumn _partColumn; // part index column (hidden when inappropriate)

        public static CustomPaletteDialog ActiveInstance { get; private set; }

        public UIElement PaletteDockContent { get; private set; }
        public UIElement TextureDockContent { get; private set; }

        private ListBox _lstSetupIds; private bool _setupPanelInitialized;

        public CustomPaletteDefinition ResultDefinition { get; private set; }
        public CustomPaletteDefinition StartingDefinition { get; set; }
        public List<uint> AvailablePaletteIDs { get; set; } = new();
        public Action<CustomPaletteDefinition> OnLiveUpdate { get; set; }

        private const int PreviewWidth = 512; private const int PreviewHeight = 48;
        private PaletteSet _currentSet; private int _currentSetIndex;

        private TextBox txtPaletteId; private TextBox txtSearch; private ListBox lstPalettes; private System.Windows.Controls.Image imgBigPreview; private StackPanel panelSetBrowse; private TextBlock lblSetIndex; private Slider sldSetIndex; private Button btnUseSetPalette; private StackPanel panelSingle; private TextBox txtRanges; private StackPanel panelMulti; private TextBox txtMulti; private TextBox txtShade; private Slider sldShade; private Button btnOk; private Button btnSave; private Button btnLoad; private Button btnExport; private System.Windows.Controls.Image imgRangePreview; private TextBox txtColorSearch; private Button btnColorFind; private TextBlock lblColorResult; private ListView _lstRanges;
        // Newly added palette id input controls
        private TextBox txtAddPaletteIds; private Button btnAddPaletteIds;
        private RangeEditorControl _rangeEditor; private Border _rangeEditorHost; private Button _btnRangeUndo; private Button _btnRangeRedo;

        private class RangeDisplay { public uint PaletteId { get; set; } public uint Offset { get; set; } public uint Length { get; set; } public string OffsetLength => $"{Offset}:{Length}"; }
        private class PaletteEntryRow : INotifyPropertyChanged
        {
            private uint _paletteSetId; private string _rangesText = string.Empty; private bool _isLocked;
            public uint PaletteSetId { get => _paletteSetId; set { if (_paletteSetId != value) { _paletteSetId = value; OnPropertyChanged(nameof(PaletteSetId)); } } }
            public string RangesText
            {
                get => _rangesText;
                set
                {
                    var v = value ?? string.Empty; // normalize null -> empty
                    if (_rangesText != v)
                    {
                        _rangesText = v;
                        OnPropertyChanged(nameof(RangesText));
                    }
                }
            }
            public bool IsLocked { get => _isLocked; set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(nameof(IsLocked)); } } }
            public event PropertyChangedEventHandler PropertyChanged; private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private ObservableCollection<PaletteEntryRow> _rows = new(); private DataGrid _gridEntries; private Button _btnAddRow; private Button _btnRemoveRow; private bool _freezeLines; private CheckBox _chkLockAll;

        // Updated TextureRow: implements INotifyPropertyChanged, exposes NewHex read-only, edit via NewId binding
        private class TextureRow : INotifyPropertyChanged
        {
            private uint _newId;
            public int PartIndex { get; set; }
            public uint OldId { get; set; }
            public uint NewId { get => _newId; set { if (_newId != value) { _newId = value; OnPropertyChanged(nameof(NewId)); OnPropertyChanged(nameof(NewHex)); } } }
            public bool IsLocked { get; set; }
            public string OldHex => $"0x{OldId:X8}";
            public string NewHex => $"0x{NewId:X8}"; // display only
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
        private readonly List<TextureRow> _textureRows = new(); private List<uint> _availableTextureIds = new(); private ListBox lstTextures; private DataGrid gridTextures; private CheckBox chkTexLockAll; private Button btnTexAdd; private Button btnTexRemove; private Button btnTexSave; private Button btnTexLoad; private Button btnTexOk; private TextBox txtTexSearch; private System.Windows.Controls.Image imgTexPreviewOld; private System.Windows.Controls.Image imgTexPreviewNew; private TabControl _tabControl; private TabControl _rootTabs; private DateTime _lastAutosave = DateTime.MinValue; private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(2); private const string AutosaveFile = "CustomPalette.autosave.json"; private const string LiveSnapshotFile = "CustomPalette.current.json"; // added snapshot for immediate export

        // New gallery UI fields
        private TextBox txtTexFilter; private ListBox lstTexGallery;
        // Thumbnail cache + async loader control (made thread-safe)
        private readonly ConcurrentDictionary<uint, ImageSource> _thumbCache = new();
        private readonly HashSet<uint> _thumbInFlight = new();
        private readonly object _thumbSync = new(); // NEW lock for _thumbInFlight access
        private readonly System.Threading.SemaphoreSlim _thumbSemaphore = new(4); // limit concurrent decodes
        private ImageSource _thumbPlaceholder; // lazy-created

        // Disco
        private string _discoBuffer = string.Empty; private DispatcherTimer _discoTimer; private bool _discoMode; private readonly Random _discoRand = new();

        private bool _hasClothing; // gate enabling of UI until clothing item active

        // Live save debounce
        private CancellationTokenSource _liveSaveCts; private static readonly TimeSpan LiveSaveDelay = TimeSpan.FromMilliseconds(600);

        // Texture old/new override
        private ObservableCollection<TextureRow> _textureRowsObs = new();
        private bool _suppressTextureEvents;
        private DateTime _lastTextureApply = DateTime.MinValue;
        private static readonly TimeSpan TextureApplyDebounce = TimeSpan.FromMilliseconds(180);
        private static readonly System.Reflection.PropertyInfo _cloTexNewProp = typeof(CloTextureEffect).GetProperty("NewTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.PropertyInfo _cloTexOldProp = typeof(CloTextureEffect).GetProperty("OldTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        // Flashing range highlight
        private DispatcherTimer _rangeFlashTimer; private bool _flashOn; private RangeDef _flashRange;

        // Add JSON viewer components
        private TextBox _txtJson; private bool _suppressJson; private DateTime _lastJsonEdit; private DispatcherTimer _jsonApplyTimer;

        // NEW: defer initialization until DATs loaded
        private bool _initialized; // whether InitializeCore ran
        private DispatcherTimer _datPollTimer; // polls for DatManager readiness

        public CustomPaletteDialog()
        {
            Width = 860; Height = 700; Content = BuildUI(); Loaded += CustomPaletteDialog_Loaded; KeyDown += CustomPaletteDialog_KeyDown; CustomPaletteStore.LoadAll(); ActiveInstance = this; // register
            SetHasClothing(ClothingTableList.CurrentClothingItem != null);

            // If DATs not yet loaded, show overlay and poll until they are.
            if (DatManager.PortalDat == null)
            {
                ShowLoadingOverlay("Waiting for DAT load...");
                _datPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _datPollTimer.Tick += (_, __) =>
                {
                    if (DatManager.PortalDat != null)
                    {
                        _datPollTimer.Stop();
                        SafeInitializeCore();
                    }
                };
                _datPollTimer.Start();
            }
        }

        private void SafeInitializeCore()
        {
            if (_initialized) return;
            if (DatManager.PortalDat == null) return; // still not ready
            InitializeCore();
            _initialized = true;
        }

        // Backwards compatibility helpers in case older callers still invoke them
        public void InitializeForDock()
        {
            if (Content == null) Content = BuildUI();
            SafeInitializeCore();
        }
        public (UIElement palette, UIElement textures) GetDockParts() => (PaletteDockContent, TextureDockContent);

        private void CustomPaletteDialog_Loaded(object sender, RoutedEventArgs e)
        {
            SafeInitializeCore();
        }

        private void CustomPaletteDialog_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        { if (e.Key == System.Windows.Input.Key.Escape && _discoMode) { ToggleDisco(false); return; } var k = e.Key.ToString(); if (k.Length == 1 || (k.StartsWith("D") && k.Length == 2)) { char c = k[^1]; if (char.IsLetter(c)) c = char.ToLowerInvariant(c); _discoBuffer += c; if (_discoBuffer.Length > 16) _discoBuffer = _discoBuffer[^16..]; if (_discoBuffer.EndsWith("atoyot")) ToggleDisco(!_discoMode); } }
        private void ToggleDisco(bool enable) { if (enable == _discoMode) return; _discoMode = enable; if (enable) { _discoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; _discoTimer.Tick -= DiscoTick; _discoTimer.Tick += DiscoTick; _discoTimer.Start(); } else { _discoTimer?.Stop(); Background = Brushes.Transparent; if (imgBigPreview != null) imgBigPreview.Opacity = 1.0; } }
        private void DiscoTick(object sender, EventArgs e) { byte r = (byte)_discoRand.Next(64, 256); byte g = (byte)_discoRand.Next(64, 256); byte b = (byte)_discoRand.Next(64, 256); Background = new SolidColorBrush(Color.FromRgb(r, g, b)); if (imgBigPreview != null) imgBigPreview.Opacity = (_discoRand.NextDouble() * 0.3) + 0.7; }

        private void InitializeCore()
        {
            ShowLoadingOverlay("Caching assets...");
            try
            {
                PopulateList();
                // Attempt to seed from current clothing if no explicit starting definition provided
                if (StartingDefinition == null && ClothingTableList.CurrentClothingItem != null)
                {
                    try
                    {
                        var seed = ClothingTableList.Instance?.GetCurrentSeedDefinition();
                        if (seed != null) StartingDefinition = seed;
                    }
                    catch { }
                }
                if (StartingDefinition != null) { ApplyLoadedPreset(StartingDefinition); }
                else { SyncRowsFromText(); SyncTextLinesFromRows(); }
                _freezeLines = false;
                HookLiveEvents();
                UpdateBigPreviewImage();
                UpdateRangeHighlight();
                RefreshRangeList();
                InitTextureTabData();
                PopulateSetupIds(); // now implemented below
                SyncRangeEditorFromRow();
                LoadLocalTextureOverrides();
                // Force initial live push so model reflects dialog immediately
                DoLiveUpdate();
            }
            finally
            {
                HideLoadingOverlay();
            }
        }

        // Added stub to satisfy legacy invocation
        private void PopulateSetupIds()
        {
            // Older versions populated a setup id list; current UI no longer needs it.
            // Keep method to avoid CS0103 from legacy calls.
        }

        public void SetHasClothing(bool hasClothing)
        {
            _hasClothing = hasClothing;
            if (_partColumn != null)
                _partColumn.Visibility = hasClothing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowLoadingOverlay(string message = null)
        {
            if (_loadingOverlay == null) return;
            if (!string.IsNullOrWhiteSpace(message) && _loadingText != null)
                _loadingText.Text = message;
            _loadingOverlay.Visibility = Visibility.Visible;
        }
        private void HideLoadingOverlay()
        {
            if (_loadingOverlay == null) return;
            _loadingOverlay.Visibility = Visibility.Collapsed;
        }

        // CHANGED: Made public so callers (e.g., ClothingTableList.OpenCustomDialog) can invoke without accessibility error.
        public UIElement BuildUI()
        {
            var tabs = BuildOriginalUI();
            _rootGrid = new Grid();
            _rootGrid.Children.Add(tabs);
            _loadingText = new TextBlock { Text = "Loading...", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,8,0,0) };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ring = new System.Windows.Shapes.Ellipse { Width = 42, Height = 42, Stroke = Brushes.DeepSkyBlue, StrokeThickness = 4, Opacity = 0.85 };
            var anim = new DoubleAnimation { From = 0, To = 360, RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(1.4) };
            var rt = new RotateTransform(); ring.RenderTransform = rt; ring.RenderTransformOrigin = new Point(0.5,0.5);
            rt.BeginAnimation(RotateTransform.AngleProperty, anim);
            sp.Children.Add(ring); sp.Children.Add(_loadingText);
            _loadingOverlay = new Border { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), Child = sp, Visibility = Visibility.Collapsed };
            _rootGrid.Children.Add(_loadingOverlay);
            return _rootGrid;
        }

        // Rewritten for clarity (fix potential brace mismatch earlier)
        private UIElement BuildOriginalUI()
        {
            var tab = new TabControl();
            _rootTabs = tab;

            // Palette tab
            var palTab = new TabItem { Header = "Palettes" };
            var texTab = new TabItem { Header = "Textures" }; _texturesTab = texTab;
            tab.Items.Add(palTab); tab.Items.Add(texTab);

            var palDock = new DockPanel();
            palTab.Content = palDock;
            PaletteDockContent = palDock;
            TextureDockContent = texTab; // expose logical part; we use tab itself for textures

            // Bottom buttons (palette tab)
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,8,0,0) };
            btnLoad = new Button { Content = "Load", Width = 70, Margin = new Thickness(0,0,6,0) }; btnLoad.Click += btnLoad_Click;
            btnSave = new Button { Content = "Save", Width = 70, Margin = new Thickness(0,0,6,0) }; btnSave.Click += btnSave_Click;
            btnOk = new Button { Content = "Apply to JSON", Width = 110, Margin = new Thickness(0,0,6,0) }; btnOk.Click += btnOk_Click;
            btnExport = new Button { Content = "Export", Width = 80, Margin = new Thickness(0,0,6,0) }; btnExport.Click += BtnExport_Click;
            buttons.Children.Add(btnLoad); buttons.Children.Add(btnSave); buttons.Children.Add(btnOk); buttons.Children.Add(btnExport);
            DockPanel.SetDock(buttons, Dock.Bottom);
            palDock.Children.Add(buttons);

            // Shade controls
            var shadeGrid = new Grid { Margin = new Thickness(0,4,0,0) };
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition());
            shadeGrid.Children.Add(new TextBlock { Text = "Shade:" });
            txtShade = new TextBox { Text = "0", Height = 22 }; Grid.SetColumn(txtShade,1); shadeGrid.Children.Add(txtShade);
            sldShade = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01 }; Grid.SetColumn(sldShade,2); sldShade.ValueChanged += sldShade_ValueChanged; shadeGrid.Children.Add(sldShade);
            DockPanel.SetDock(shadeGrid, Dock.Bottom);
            palDock.Children.Add(shadeGrid);

            // Main grid
            var mainGrid = new Grid(); mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); mainGrid.ColumnDefinitions.Add(new ColumnDefinition()); palDock.Children.Add(mainGrid);

            // Left stack (palette list + ranges)
            var leftStack = new StackPanel(); mainGrid.Children.Add(leftStack);
            leftStack.Children.Add(new TextBlock { Text = "Palettes", FontWeight = FontWeights.Bold });
            txtSearch = new TextBox { Height = 22, Margin = new Thickness(0,0,0,4) }; txtSearch.TextChanged += txtSearch_TextChanged; leftStack.Children.Add(txtSearch);
            // New: Add palette IDs input (supports comma/range syntax e.g. 420-421,0x04001234)
            var addPalPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,4) };
            txtAddPaletteIds = new TextBox { Width = 140, Height = 22, ToolTip = "Add palette IDs (e.g. 420-421,0x4001234)" };
            btnAddPaletteIds = new Button { Content = "+Pal", Width = 50, Margin = new Thickness(4,0,0,0) };
            btnAddPaletteIds.Click += BtnAddPaletteIds_Click;
            addPalPanel.Children.Add(txtAddPaletteIds); addPalPanel.Children.Add(btnAddPaletteIds); leftStack.Children.Add(addPalPanel);
            lstPalettes = new ListBox { Height = 260 }; lstPalettes.SelectionChanged += lstPalettes_SelectionChanged; leftStack.Children.Add(lstPalettes);
            _gridEntries = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = _rows, Height = 140, Margin = new Thickness(0,4,0,0) }; _gridEntries.SelectionChanged += (_, __) => { SetFlashRangeFromSelection(); UpdateRangeHighlight(); SyncRangeEditorFromRow(); }; _gridEntries.CellEditEnding += GridEntries_CellEditEnding; _gridEntries.BeginningEdit += GridEntries_BeginningEdit; _gridEntries.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new Binding("IsLocked") });
            var paletteCol = new DataGridTemplateColumn { Header = "Palette", IsReadOnly = true }; var palFactory = new FrameworkElementFactory(typeof(TextBlock)); palFactory.SetBinding(TextBlock.TextProperty, new Binding("PaletteSetId") { Mode = BindingMode.OneWay, Converter = new UIntToHexConverter() }); paletteCol.CellTemplate = new DataTemplate { VisualTree = palFactory }; _gridEntries.Columns.Add(paletteCol);
            _gridEntries.Columns.Add(new DataGridTextColumn { Header = "Ranges", Binding = new Binding("RangesText") }); leftStack.Children.Add(_gridEntries);
            var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,4,0,4) }; _btnAddRow = new Button { Content = "+", Width = 30, Margin = new Thickness(0,0,4,0)}; _btnAddRow.Click += (_, __)=> AddRowFromSelection(); _btnRemoveRow = new Button { Content = "-", Width = 30 }; _btnRemoveRow.Click += (_, __)=> RemoveSelectedRow(); rowBtns.Children.Add(_btnAddRow); rowBtns.Children.Add(_btnRemoveRow); leftStack.Children.Add(rowBtns);

            // Right stack (preview + range editor)
            var rightStack = new StackPanel { Margin = new Thickness(6,0,0,0) }; Grid.SetColumn(rightStack,1); mainGrid.Children.Add(rightStack);
            rightStack.Children.Add(new TextBlock { Text = "Preview", FontWeight = FontWeights.Bold });
            imgBigPreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill };
            rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0,2,0,6), Child = imgBigPreview });
            rightStack.Children.Add(new TextBlock { Text = "Range Editor", FontWeight = FontWeights.Bold });
            _btnRangeUndo = new Button { Content = "Undo", Width = 50, Margin = new Thickness(0,0,4,0) }; _btnRangeRedo = new Button { Content = "Redo", Width = 50 };
            var undoPanel = new StackPanel { Orientation = Orientation.Horizontal }; undoPanel.Children.Add(_btnRangeUndo); undoPanel.Children.Add(_btnRangeRedo); rightStack.Children.Add(undoPanel);
            _btnRangeUndo.Click += (_, __)=> { _rangeEditor?.Undo(); UpdateRangeUndoRedoButtons(); }; _btnRangeRedo.Click += (_, __)=> { _rangeEditor?.Redo(); UpdateRangeUndoRedoButtons(); };
            _rangeEditor = new RangeEditorControl { Height = 44, Margin = new Thickness(0,2,0,4) };
            _rangeEditor.RangesChanged += (_, list)=> ApplyRangeEditorToSelectedRow(list);
            _rangeEditor.HistoryChanged += (_, __)=> UpdateRangeUndoRedoButtons();
            rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _rangeEditor });
            rightStack.Children.Add(new TextBlock { Text = "All Ranges", FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0)});
            _lstRanges = new ListView { Height = 120 }; rightStack.Children.Add(_lstRanges);
            rightStack.Children.Add(new TextBlock { Text = "Generated", FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0)});
            txtMulti = new TextBox { AcceptsReturn = true, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsReadOnly = true }; rightStack.Children.Add(txtMulti);
            rightStack.Children.Add(new TextBlock { Text = "Highlight", FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0)});
            imgRangePreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill }; rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = imgRangePreview });

            // Texture tab content
            var texRoot = new DockPanel(); texTab.Content = texRoot;
            var texButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,6,0,0) };
            btnTexLoad = new Button { Content = "Load Overrides", Margin = new Thickness(0,0,6,0) }; btnTexLoad.Click += BtnTexLoadOverrides_Click;
            btnTexSave = new Button { Content = "Save Overrides", Margin = new Thickness(0,0,6,0) }; btnTexSave.Click += BtnTexSaveOverrides_Click;
            var btnTexApply = new Button { Content = "Apply", Margin = new Thickness(0,0,6,0) }; btnTexApply.Click += BtnTexApply_Click;
            var btnTexResetSel = new Button { Content = "Reset Selected", Margin = new Thickness(0,0,6,0) }; btnTexResetSel.Click += BtnTexResetSelected_Click;
            var btnTexResetAll = new Button { Content = "Reset All", Margin = new Thickness(0,0,6,0) }; btnTexResetAll.Click += BtnTexResetAll_Click;
            var btnTexUndo = new Button { Content = "Undo", Margin = new Thickness(0,0,6,0) }; btnTexUndo.Click += BtnTexUndo_Click;
            var btnTexRedo = new Button { Content = "Redo", Margin = new Thickness(0,0,6,0) }; btnTexRedo.Click += BtnTexRedo_Click;
            texButtons.Children.Add(btnTexLoad); texButtons.Children.Add(btnTexSave); texButtons.Children.Add(btnTexApply); texButtons.Children.Add(btnTexResetSel); texButtons.Children.Add(btnTexResetAll); texButtons.Children.Add(btnTexUndo); texButtons.Children.Add(btnTexRedo);
            DockPanel.SetDock(texButtons, Dock.Bottom); texRoot.Children.Add(texButtons);

            var texGrid = new Grid(); texGrid.ColumnDefinitions.Add(new ColumnDefinition()); texGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); texRoot.Children.Add(texGrid);
            gridTextures = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(0,0,6,0), IsReadOnly = false, ItemsSource = _textureRowsObs, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow };
            gridTextures.SelectionChanged += GridTextures_SelectionChanged; gridTextures.CellEditEnding += GridTextures_CellEditEnding;
            gridTextures.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new Binding("IsLocked"), Width = 26 });
            _partColumn = new DataGridTextColumn { Header = "Part", Binding = new Binding("PartIndex"), IsReadOnly = true, Width = 50, Visibility = _hasClothing ? Visibility.Visible : Visibility.Collapsed }; gridTextures.Columns.Add(_partColumn);
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "Old", Binding = new Binding("OldHex"), IsReadOnly = true, Width = 110 });
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "New", Binding = new Binding("NewId") { Converter = new UIntToHexConverter(), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 110 });
            Grid.SetColumn(gridTextures,0); texGrid.Children.Add(gridTextures);

            var texPreviewStack = new StackPanel { Orientation = Orientation.Vertical }; Grid.SetColumn(texPreviewStack,1); texGrid.Children.Add(texPreviewStack);
            texPreviewStack.Children.Add(new TextBlock { Text = "Old Texture", FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,2) });
            imgTexPreviewOld = new System.Windows.Controls.Image { Height = 128, Stretch = Stretch.Uniform, Margin = new Thickness(0,0,0,8) }; texPreviewStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = imgTexPreviewOld });
            texPreviewStack.Children.Add(new TextBlock { Text = "New Texture", FontWeight = FontWeights.Bold, Margin = new Thickness(0,4,0,2) });
            imgTexPreviewNew = new System.Windows.Controls.Image { Height = 128, Stretch = Stretch.Uniform }; texPreviewStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = imgTexPreviewNew });
            texPreviewStack.Children.Add(new TextBlock { Text = "All Textures (select to apply)", FontWeight = FontWeights.Bold, Margin = new Thickness(0,8,0,2) });
            txtTexFilter = new TextBox { Height = 22, Margin = new Thickness(0,0,0,4), ToolTip = "Filter (hex)" }; txtTexFilter.TextChanged += TexFilter_TextChanged; texPreviewStack.Children.Add(txtTexFilter);
            lstTexGallery = new ListBox { Height = 260, HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Single, Background = Brushes.Transparent, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) }; lstTexGallery.SelectionChanged += TexGallery_SelectionChanged; texPreviewStack.Children.Add(lstTexGallery);

            // Add JSON tab
            var jsonTab = new TabItem { Header = "JSON" };
            var jsonGrid = new Grid();
            _txtJson = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap };
            _txtJson.TextChanged += (s, e) => { if (_suppressJson) return; _lastJsonEdit = DateTime.UtcNow; InitJsonTimer(); _jsonApplyTimer.Stop(); _jsonApplyTimer.Start(); };
            jsonGrid.Children.Add(_txtJson); jsonTab.Content = jsonGrid; tab.Items.Add(jsonTab);

            return tab;
        }
        // === Existing palette code unchanged below (shortened for brevity) ===
        private void PopulateList(){ if (AvailablePaletteIDs == null || lstPalettes == null) return; if (DatManager.PortalDat == null) return; lstPalettes.Items.Clear(); foreach (var id in AvailablePaletteIDs) lstPalettes.Items.Add(BuildPaletteListItem(id)); }
        private ListBoxItem BuildPaletteListItem(uint id){ var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) }; var img = new System.Windows.Controls.Image { Width = 220, Height = 14, Stretch = Stretch.Fill, SnapsToDevicePixels = true }; img.Source = BuildPaletteBitmap(id, 220, 14); sp.Children.Add(img); sp.Children.Add(new TextBlock { Text = $" 0x{id:X8}", VerticalAlignment = VerticalAlignment.Center }); return new ListBoxItem { Content = sp, Tag = id, ToolTip = $"0x{id:X8}" }; }
        private ImageSource BuildPaletteBitmap(uint id, int width, int height){ try { if (DatManager.PortalDat == null) return null; uint paletteId = id; if ((id >> 24) == 0xF){ var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(id); if (set?.PaletteList?.Count > 0) paletteId = set.PaletteList[0]; } var palette = DatManager.PortalDat.ReadFromDat<Palette>(paletteId); if (palette == null || palette.Colors.Count == 0) return null; var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); var pixels = new byte[width * height * 4]; for (int x = 0; x < width; x++){ int palIdx = (int)((double)x / width * (palette.Colors.Count - 1)); uint col = palette.Colors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF); for (int y = 0; y < height; y++){ int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; } } wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0); return wb; } catch { return null; } }
        private void UpdateBigPreviewImage(){ if (imgBigPreview == null) return; if (DatManager.PortalDat == null) { imgBigPreview.Source = null; return; } if (_currentSet != null){ if (_currentSet.PaletteList == null || _currentSet.PaletteList.Count == 0){ imgBigPreview.Source = null; return; } var safeIndex = Math.Clamp(_currentSetIndex, 0, _currentSet.PaletteList.Count - 1); var palId = _currentSet.PaletteList[safeIndex]; imgBigPreview.Source = BuildPaletteBitmap(palId, PreviewWidth, PreviewHeight); return; } else if (lstPalettes?.SelectedItem is ListBoxItem li) imgBigPreview.Source = BuildPaletteBitmap((uint)li.Tag, PreviewWidth, PreviewHeight); }
        private void UpdateRangeHighlight(){ if (imgRangePreview == null) return; if (DatManager.PortalDat == null) { imgRangePreview.Source = null; return; } Palette palette = null; uint actualPaletteId = 0; string rangeSpec = null; if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow rowSel){ uint palId = rowSel.PaletteSetId; actualPaletteId = palId; if ((palId >> 24) == 0xF){ try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0]; } catch { imgRangePreview.Source = null; return; } } try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId); } catch { palette = null; } rangeSpec = rowSel.RangesText?.Replace(',', ' '); } else { imgRangePreview.Source = null; return; } if (palette == null || palette.Colors.Count == 0 || string.IsNullOrWhiteSpace(rangeSpec)){ imgRangePreview.Source = null; return; } var ranges = RangeParser.ParseRanges(rangeSpec, true, out _); int colors = palette.Colors.Count; int width = 512; int height = 64; var wb2 = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); var pixels2 = new byte[width * height * 4]; for (int x = 0; x < width; x++){ int palIdx = (int)((double)x / width * (colors - 1)); uint col = palette.Colors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF); bool highlighted = false; foreach (var rg in ranges){ var start = (int)rg.Offset * 8; var count = (int)rg.Length * 8; if (palIdx >= start && palIdx < start + count){ highlighted = true; break; } } if (highlighted){ r = (byte)Math.Min(255, r + 80); g = (byte)Math.Min(255, g + 80); b = (byte)Math.Min(255, b + 80); } for (int y = 0; y < height; y++){ int idx = (y * width + x) * 4; pixels2[idx] = b; pixels2[idx + 1] = g; pixels2[idx + 2] = r; pixels2[idx + 3] = a; } } wb2.WritePixels(new Int32Rect(0, 0, width, height), pixels2, width * 4, 0); imgRangePreview.Source = wb2; }
        private void RefreshRangeList(){ if (_lstRanges == null) return; var items = new List<RangeDisplay>(); foreach (var row in _rows){ var text = (row.RangesText ?? string.Empty).Replace(',', ' '); var ranges = RangeParser.ParseRanges(text, true, out _); foreach (var r in ranges) items.Add(new RangeDisplay { PaletteId = row.PaletteSetId, Offset = r.Offset, Length = r.Length }); } _lstRanges.ItemsSource = items; }
        private void SyncRangeEditorFromRow(){ if (_rangeEditor == null) return; if (DatManager.PortalDat == null) { _rangeEditor.SetPalette(null); return; } if (_gridEntries?.SelectedItem is not PaletteEntryRow row){ _rangeEditor.SetPalette(null); _rangeEditor.SetRanges(Array.Empty<RangeDef>()); UpdateRangeUndoRedoButtons(); return; } uint palId = row.PaletteSetId; uint actual = palId; if ((palId >> 24) == 0xF){ try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set?.PaletteList?.Count > 0) actual = set.PaletteList[0]; } catch { } } Palette palette = null; try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actual); } catch { } _rangeEditor.SetPalette(palette); var parsed = RangeParser.ParseRanges(((row.RangesText ?? string.Empty).Replace(',', ' ')), true, out _); _rangeEditor.SetRanges(parsed); UpdateRangeUndoRedoButtons(); }
        private void ApplyRangeEditorToSelectedRow(IReadOnlyList<RangeDef> list){ if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return; if (row.IsLocked) return; row.RangesText = string.Join(",", list.Select(r => $"{r.Offset}:{r.Length}")); EnforceNonOverlappingRanges(row.PaletteSetId); RefreshRangeList(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); DoLiveUpdate(); if (!_freezeLines) SyncTextLinesFromRows(); _gridEntries.Items.Refresh(); UpdateRangeUndoRedoButtons(); UpdateJsonView(); }
        private void lstPalettes_SelectionChanged(object sender, SelectionChangedEventArgs e){ if (DatManager.PortalDat == null) return; if (lstPalettes?.SelectedItem is not ListBoxItem li) return; uint id = (uint)li.Tag; if ((id >> 24) == 0xF) { _currentSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(id); _currentSetIndex = 0; } else _currentSet = null; if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow row && !row.IsLocked){ row.PaletteSetId = id; RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); _gridEntries.Items.Refresh(); } UpdateBigPreviewImage(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); RefreshRangeList(); SyncRangeEditorFromRow(); }
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lstPalettes == null || AvailablePaletteIDs == null) return;
            if (DatManager.PortalDat == null) return;
            var filter = (txtSearch?.Text ?? string.Empty).Trim().ToLowerInvariant();
            lstPalettes.BeginInit();
            lstPalettes.Items.Clear();
            foreach (var id in AvailablePaletteIDs)
            {
                var label = $"0x{id:X8}".ToLowerInvariant();
                if (filter.Length == 0 || label.Contains(filter))
                    lstPalettes.Items.Add(BuildPaletteListItem(id));
            }
            lstPalettes.EndInit();
        }
        private void sldShade_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => txtShade.Text = sldShade.Value.ToString("0.###", CultureInfo.InvariantCulture);
        private void btnOk_Click(object sender, RoutedEventArgs e){ if (TryBuildDefinition(out var def)) { ResultDefinition = def; OnLiveUpdate?.Invoke(def); EnsureDefinitionHasName(def); PersistImmediate(def); } else MessageBox.Show("Invalid definition", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Error); }
        private void btnSave_Click(object sender, RoutedEventArgs e){ if (ResultDefinition == null && TryBuildDefinition(out var temp)) ResultDefinition = temp; if (ResultDefinition == null) return; var name = Microsoft.VisualBasic.Interaction.InputBox("Preset Name:", "Save Custom Palette", ResultDefinition.Name ?? "MyPreset"); if (string.IsNullOrWhiteSpace(name)) return; ResultDefinition.Name = name.Trim(); CustomPaletteStore.SaveDefinition(ResultDefinition); MessageBox.Show("Saved."); }
        private void btnLoad_Click(object sender, RoutedEventArgs e){ var defs = CustomPaletteStore.LoadAll().ToList(); if (defs.Count == 0) { MessageBox.Show("No saved presets."); return; } var picker = new PresetPickerWindow(defs); if (picker.ShowDialog() == true && picker.Selected != null) { ApplyLoadedPreset(picker.Selected); DoLiveUpdate(); } SyncRangeEditorFromRow(); }
        public void ApplyLoadedPreset(CustomPaletteDefinition def){ if (def == null) return; _rows.Clear(); foreach (var e in def.Entries){ var rtxt = (e.Ranges != null) ? string.Join(",", e.Ranges.Select(r => $"{r.Offset}:{r.Length}")) : string.Empty; _rows.Add(new PaletteEntryRow { PaletteSetId = e.PaletteSetId, RangesText = rtxt }); } EnforceNonOverlappingRanges(); if (!_freezeLines) SyncTextLinesFromRows(); txtShade.Text = def.Shade.ToString("0.###", CultureInfo.InvariantCulture); sldShade.Value = def.Shade; ResultDefinition = def; UpdateBigPreviewImage(); UpdateRangeHighlight(); RefreshRangeList(); SyncRangeEditorFromRow(); UpdateRangeUndoRedoButtons(); }
        private void GridEntries_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e){ if (e.Row?.Item is not PaletteEntryRow row) return; if (row.IsLocked) { e.Cancel = true; return; } if (e.EditingElement is TextBox tb){ row.RangesText = tb.Text?.Trim(); EnforceNonOverlappingRanges(row.PaletteSetId); SyncTextLinesFromRows(); RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); SyncRangeEditorFromRow(); } }
        private void GridEntries_BeginningEdit(object sender, DataGridBeginningEditEventArgs e){ if (e.Row?.Item is PaletteEntryRow row && row.IsLocked) e.Cancel = true; }
        private void AddRowFromSelection(){ uint palId = 0; if (lstPalettes?.SelectedItem is ListBoxItem li) palId = (uint)li.Tag; else if (_currentSet != null && _currentSet.PaletteList?.Count > 0) palId = _currentSet.PaletteList[0]; else if (AvailablePaletteIDs?.Count > 0) palId = AvailablePaletteIDs[0]; if (palId == 0) return; var newRow = new PaletteEntryRow { PaletteSetId = palId, RangesText = "0:1" }; _rows.Add(newRow); EnforceNonOverlappingRanges(palId); SyncTextLinesFromRows(); RefreshRangeList(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); DoLiveUpdate(); _gridEntries.SelectedItem = newRow; SyncRangeEditorFromRow(); }
        private void RemoveSelectedRow(){ if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return; if (row.IsLocked) return; var palId = row.PaletteSetId; _rows.Remove(row); EnforceNonOverlappingRanges(palId); SyncTextLinesFromRows(); RefreshRangeList(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); DoLiveUpdate(); SyncRangeEditorFromRow(); }
        private void UpdateRangeUndoRedoButtons(){ if (_btnRangeUndo == null || _btnRangeRedo == null || _rangeEditor == null) return; _btnRangeUndo.IsEnabled = _rangeEditor.CanUndo; _btnRangeRedo.IsEnabled = _rangeEditor.CanRedo; }
        private void BtnExport_Click(object sender, RoutedEventArgs e){ try { var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) { MessageBox.Show("No clothing item loaded."); return; } var dlg = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json", FileName = $"Clothing_{clothing.Id:X8}.json" }; if (dlg.ShowDialog() != true) return; var overrides = new CustomTextureDefinition { Name = "ExportSession" }; foreach (var row in _textureRowsObs.Where(r => r.NewId != r.OldId)) overrides.Entries.Add(new CustomTextureEntry { PartIndex = row.PartIndex, OldId = row.OldId, NewId = row.NewId }); CustomTextureStore.ExportClothingTable(clothing, dlg.FileName, overrides.Entries.Count == 0 ? null : overrides); MessageBox.Show("Export complete."); } catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}"); }
        }
        private void EnforceNonOverlappingRanges(uint? paletteFilter = null)
        {
            var groups = _rows.Where(r => !paletteFilter.HasValue || r.PaletteSetId == paletteFilter.Value)
                              .GroupBy(r => r.PaletteSetId);

            foreach (var g in groups)
            {
                var used = new HashSet<uint>();
                foreach (var row in g)
                {
                    var text = (row.RangesText ?? string.Empty).Replace(',', ' ');
                    var parsed = RangeParser.ParseRanges(text, true, out _)
                                            .OrderBy(r => r.Offset)
                                            .ToList();

                    var rebuilt = new List<(uint off, uint len)>();

                    foreach (var r in parsed)
                    {
                        if (r.Length == 0) continue; // skip empty ranges defensively
                        // iterate each logical group index covered by this range
                        // (ranges are already expressed in group units)
                        checked
                        {
                            var end = r.Offset + r.Length; // exclusive
                            for (uint i = r.Offset; i < end; i++)
                            {
                                if (!used.Add(i))
                                    continue; // already claimed by earlier row

                                if (rebuilt.Count == 0)
                                {
                                    // start first segment
                                    rebuilt.Add((i, 1));
                                }
                                else
                                {
                                    var last = rebuilt[^1];
                                    // contiguous -> extend; else start a new segment at i
                                    if (last.off + last.len == i)
                                        rebuilt[^1] = (last.off, last.len + 1);
                                    else
                                        rebuilt.Add((i, 1));
                                }
                            }
                        }
                    }

                    var newText = string.Join(",", rebuilt.Select(t => $"{t.off}:{t.len}"));
                    row.RangesText = newText;
                }
            }
            if (!_freezeLines)
                SyncTextLinesFromRows();
        }
        private void HookLiveEvents(){ if (txtPaletteId != null) txtPaletteId.TextChanged += (_, __) => DoLiveUpdate(); if (txtRanges != null) txtRanges.TextChanged += (_, __) => DoLiveUpdate(); if (sldShade != null) sldShade.ValueChanged += (_, __) => DoLiveUpdate(); }
        private void SyncRowsFromText(){}
        private void SyncTextLinesFromRows(){}
        private bool TryBuildDefinition(out CustomPaletteDefinition def){ def = null; try { var tmp = new CustomPaletteDefinition { Shade = (float)(double.TryParse(txtShade?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0, 1) : 0) }; foreach (var r in _rows){ if (string.IsNullOrWhiteSpace(r.RangesText)) continue; var entry = new CustomPaletteEntry { PaletteSetId = r.PaletteSetId, Ranges = RangeParser.ParseRanges((r.RangesText ?? string.Empty).Replace(',', ' ')) }; if (entry.Ranges.Count == 0) continue; tmp.Entries.Add(entry); } if (tmp.Entries.Count == 0) return false; tmp.Multi = tmp.Entries.Count > 1; def = tmp; return true; } catch { return false; } }
        private void EnsureDefinitionHasName(CustomPaletteDefinition def){ if (def == null) return; if (string.IsNullOrWhiteSpace(def.Name)) def.Name = $"Live_{DateTime.UtcNow:yyyyMMdd_HHmmss}"; }
        private void PersistImmediate(CustomPaletteDefinition def){ try { if (def != null) CustomPaletteStore.SaveDefinition(def); } catch { } }
        private void DoLiveUpdate(){ if (TryBuildDefinition(out var d)) ResultDefinition = d; UpdateJsonView(); if (ResultDefinition != null) { try { ClothingTableList.Instance?.ApplyLivePaletteDefinition(ResultDefinition); } catch { } } }

        // ===== Texture + Gallery Implementation =====
        private void InitTextureTabData(){ _textureRows.Clear(); _textureRowsObs.Clear(); _availableTextureIds.Clear(); if (DatManager.PortalDat == null) return; try { if (ClothingTableList.CurrentClothingItem != null){ foreach (var kvp in ClothingTableList.CurrentClothingItem.ClothingBaseEffects){ foreach (var obj in kvp.Value.CloObjectEffects){ foreach (var tex in obj.CloTextureEffects){ uint oldT = (uint)(_cloTexOldProp?.GetValue(tex) ?? tex.OldTexture); uint newT = (uint)(_cloTexNewProp?.GetValue(tex) ?? tex.NewTexture); int partIndex = (int)obj.Index; if (!_textureRows.Any(r => r.PartIndex == partIndex && r.OldId == oldT)) _textureRows.Add(new TextureRow { PartIndex = partIndex, OldId = oldT, NewId = newT, IsLocked = false }); if (!_availableTextureIds.Contains(oldT)) _availableTextureIds.Add(oldT); if (newT != 0 && !_availableTextureIds.Contains(newT)) _availableTextureIds.Add(newT); } } } } foreach (var id in DatManager.PortalDat.AllFiles.Keys.Where(i => (i >> 24) == 0x05).Take(800)) if (!_availableTextureIds.Contains(id)) _availableTextureIds.Add(id); } catch { } foreach (var r in _textureRows.OrderBy(r => r.PartIndex).ThenBy(r => r.OldId)) _textureRowsObs.Add(r); _availableTextureIds = _availableTextureIds.Distinct().OrderBy(i => i).ToList(); if (gridTextures != null){ gridTextures.ItemsSource = _textureRowsObs; if (_textureRowsObs.Count > 0){ gridTextures.SelectedIndex = 0; UpdateTexturePreviews(gridTextures.SelectedItem as TextureRow); } } HookTextureRowEvents(); RefreshTextureGallery(); }
        private void HookTextureRowEvents(){ foreach (var r in _textureRowsObs) r.PropertyChanged -= TextureRow_PropertyChanged; foreach (var r in _textureRowsObs) r.PropertyChanged += TextureRow_PropertyChanged; _textureRowsObs.CollectionChanged -= TextureRowsObs_CollectionChanged; _textureRowsObs.CollectionChanged += TextureRowsObs_CollectionChanged; }
        private void TextureRowsObs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e){ if (e.NewItems != null) foreach (TextureRow r in e.NewItems) r.PropertyChanged += TextureRow_PropertyChanged; if (e.OldItems != null) foreach (TextureRow r in e.OldItems) r.PropertyChanged -= TextureRow_PropertyChanged; }
        private void TextureRow_PropertyChanged(object sender, PropertyChangedEventArgs e){ if (e.PropertyName == nameof(TextureRow.NewId) && sender is TextureRow tr){ if (gridTextures?.SelectedItem == tr) UpdateTexturePreviews(tr); if (!tr.IsLocked){ PushTextureSnapshot(); ScheduleTextureApply(); SaveLocalTextureOverrides(); } } }
        private void UpdateTexturePreviews(TextureRow row){ if (row == null){ if (imgTexPreviewOld != null) imgTexPreviewOld.Source = null; if (imgTexPreviewNew != null) imgTexPreviewNew.Source = null; return; } if (imgTexPreviewOld != null) imgTexPreviewOld.Source = BuildTexturePreview(row.OldId); if (imgTexPreviewNew != null) imgTexPreviewNew.Source = BuildTexturePreview(row.NewId); }

        // ---- LIVE PALETTE -> TEXTURE PREVIEW SUPPORT (single implementation) ----
        private PaletteChanges BuildLivePaletteChanges()
        {
            var def = ResultDefinition; if (def == null || def.Entries == null || def.Entries.Count == 0) return null;
            try
            {
                var subs = new List<CloSubPalette>();
                foreach (var e in def.Entries)
                {
                    var sp = new CloSubPalette { PaletteSet = e.PaletteSetId };
                    foreach (var r in e.Ranges)
                    {
                        var off = r.Offset * 8; var len = r.Length * 8; if (len == 0) continue;
                        sp.Ranges.Add(new CloSubPaletteRange { Offset = off, NumColors = len });
                    }
                    if (sp.Ranges.Count > 0) subs.Add(sp);
                }
                return subs.Count == 0 ? null : new PaletteChanges(subs, def.Shade);
            }
            catch { return null; }
        }
        private static WriteableBitmap BuildWriteableBitmapFromColors(Microsoft.Xna.Framework.Color[] colors, int width, int height)
        {
            try
            {
                var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                var data = new byte[width * height * 4]; int p = 0;
                foreach (var c in colors){ data[p++] = c.B; data[p++] = c.G; data[p++] = c.R; data[p++] = c.A == 0 ? (byte)255 : c.A; }
                wb.WritePixels(new Int32Rect(0, 0, width, height), data, width * 4, 0);
                return wb;
            }
            catch { return null; }
        }
        private ImageSource BuildTexturePreview(uint id)
        {
            try
            {
                if (DatManager.PortalDat == null) return null;
                uint type = id >> 24; ACE.DatLoader.FileTypes.Texture texFile = null; // fixed namespace
                if (type == 0x06) texFile = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(id); // fixed namespace
                else if (type == 0x05){ var st = DatManager.PortalDat.ReadFromDat<SurfaceTexture>(id); if (st?.Textures != null && st.Textures.Count > 0) texFile = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(st.Textures[0]); } // fixed namespace
                else if (type == 0x08){ var surf = DatManager.PortalDat.ReadFromDat<Surface>(id); if (surf != null){ if (surf.ColorValue != 0){ var wbColor = new WriteableBitmap(32,32,96,96,PixelFormats.Bgra32,null); var dataColor=new byte[32*32*4]; byte a=(byte)(surf.ColorValue>>24); byte r=(byte)(surf.ColorValue>>16); byte g=(byte)(surf.ColorValue>>8); byte b=(byte)surf.ColorValue; if(a==0)a=255; for(int i=0;i<32*32;i++){ int idx=i*4; dataColor[idx]=b; dataColor[idx+1]=g; dataColor[idx+2]=r; dataColor[idx+3]=a;} wbColor.WritePixels(new Int32Rect(0,0,32,32),dataColor,32*4,0); return wbColor;} if (surf.OrigTextureId!=0){ var st2=DatManager.PortalDat.ReadFromDat<SurfaceTexture>(surf.OrigTextureId); if(st2?.Textures!=null&&st2.Textures.Count>0) texFile=DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(st2.Textures[0]); } } }
                else if (type == 0x04){ var palImg = BuildPaletteBitmap(id,64,16); if (palImg != null) return palImg; }
                if (texFile == null && type != 0x06) return null;
                bool indexed = false; try { indexed = texFile != null && (texFile.Format == SurfacePixelFormat.PFID_INDEX16 || texFile.Format == SurfacePixelFormat.PFID_P8); } catch { }
                if (indexed)
                {
                    Texture2D decoded = null; try { decoded = TextureCache.Get(id, null, BuildLivePaletteChanges(), useCache:false); } catch { }
                    if (decoded != null){ var cols = new Microsoft.Xna.Framework.Color[decoded.Width * decoded.Height]; try { decoded.GetData(cols); } catch { } return BuildWriteableBitmapFromColors(cols, decoded.Width, decoded.Height); }
                }
                using var bmp = texFile?.GetBitmap(); if (bmp == null) return null;
                int w = bmp.Width, h = bmp.Height; const int MaxDim = 256; double scale = 1.0; if (w > MaxDim || h > MaxDim){ scale = Math.Min((double)MaxDim / w, (double)MaxDim / h); w = (int)(w * scale); h = (int)(h * scale); }
                System.Drawing.Bitmap working = bmp; if (scale != 1.0){ working = new System.Drawing.Bitmap(w,h,System.Drawing.Imaging.PixelFormat.Format32bppArgb); using var g = System.Drawing.Graphics.FromImage(working); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; g.DrawImage(bmp, new System.Drawing.Rectangle(0,0,w,h)); }
                var wbOut = new WriteableBitmap(working.Width, working.Height, 96, 96, PixelFormats.Bgra32, null); var data = new byte[working.Width * working.Height * 4]; int pOut = 0; for(int y=0;y<working.Height;y++) for(int x=0;x<working.Width;x++){ var c=working.GetPixel(x,y); data[pOut++]=c.B; data[pOut++]=c.G; data[pOut++]=c.R; data[pOut++]=c.A==0?(byte)255:c.A; } wbOut.WritePixels(new Int32Rect(0,0,working.Width,working.Height),data,working.Width*4,0); if (working!=bmp) working.Dispose(); return wbOut;
            }
            catch { return null; }
        }

        // ---- TEXTURE GALLERY / OVERRIDES (re-added) ----
        private ImageSource GetOrQueueThumbnail(uint id, System.Windows.Controls.Image target)
        {
            if (_thumbCache.TryGetValue(id, out var cached) && cached != null) return cached;
            _thumbPlaceholder ??= CreatePlaceholder();
            var placeholder = _thumbPlaceholder;
            lock (_thumbSync){ if (_thumbInFlight.Contains(id)) return placeholder; _thumbInFlight.Add(id); }
            _ = Task.Run(async () => { try { await _thumbSemaphore.WaitAsync().ConfigureAwait(false); ImageSource src=null; try { src=BuildTexturePreview(id);} catch {} if (src!=null){ _thumbCache[id]=src; try { _=Dispatcher.BeginInvoke(new Action(()=>{ if (target!=null) target.Source=src; }), DispatcherPriority.Background);} catch { } } } finally { _thumbSemaphore.Release(); lock(_thumbSync){ _thumbInFlight.Remove(id);} } });
            return placeholder;
        }
        private ImageSource CreatePlaceholder(){ int w=64,h=64; var wb=new WriteableBitmap(w,h,96,96,PixelFormats.Bgra32,null); var data=new byte[w*h*4]; for(int y=0;y<h;y++) for(int x=0;x<w;x++){ bool on=((x/8)+(y/8))%2==0; byte g=(byte)(on?90:120); int i=(y*w+x)*4; data[i]=g; data[i+1]=g; data[i+2]=g; data[i+3]=255; } wb.WritePixels(new Int32Rect(0,0,w,h),data,w*4,0); return wb; }
        private ListBoxItem BuildTextureGalleryItem(uint id){ var sp=new StackPanel{ Orientation=Orientation.Vertical, Margin=new Thickness(2), Width=72 }; var img=new System.Windows.Controls.Image{ Width=64, Height=64, Stretch=Stretch.Uniform, SnapsToDevicePixels=true }; img.Source=GetOrQueueThumbnail(id,img); sp.Children.Add(img); sp.Children.Add(new TextBlock{ Text=$"0x{id:X8}", FontSize=10, TextAlignment=TextAlignment.Center }); return new ListBoxItem{ Content=sp, Tag=id, ToolTip=$"0x{id:X8}" }; }
        private void RefreshTextureGallery(){ if (lstTexGallery==null) return; if (DatManager.PortalDat == null) { lstTexGallery.Items.Clear(); return; } lstTexGallery.BeginInit(); lstTexGallery.Items.Clear(); var filter=txtTexFilter?.Text?.Trim().ToLowerInvariant(); IEnumerable<uint> ids=_availableTextureIds; if(!string.IsNullOrEmpty(filter)) ids=ids.Where(i=>$"0x{i:X8}".ToLowerInvariant().Contains(filter)); int count=0; foreach(var id in ids){ lstTexGallery.Items.Add(BuildTextureGalleryItem(id)); if(++count>=800) break; } lstTexGallery.EndInit(); }
        private void TexFilter_TextChanged(object sender, TextChangedEventArgs e)=>RefreshTextureGallery();
        private void TexGallery_SelectionChanged(object sender, SelectionChangedEventArgs e){ if (lstTexGallery?.SelectedItem is not ListBoxItem li) return; uint newId=(uint)li.Tag; var targets=gridTextures?.SelectedItems?.Cast<TextureRow>().ToList(); if (targets==null||targets.Count==0) return; PushTextureSnapshot(); foreach(var t in targets) if(!t.IsLocked) t.NewId=newId; gridTextures?.Items.Refresh(); ScheduleTextureApply(); SaveLocalTextureOverrides(); }

        private void ApplyTextureOverridesToClothing(){ if (ClothingTableList.CurrentClothingItem==null) return; var map=_textureRowsObs.ToDictionary(k=>(k.PartIndex,k.OldId),v=>v.NewId); foreach(var kvp in ClothingTableList.CurrentClothingItem.ClothingBaseEffects) foreach(var obj in kvp.Value.CloObjectEffects) foreach(var tex in obj.CloTextureEffects){ uint oldT=(uint)(_cloTexOldProp?.GetValue(tex)??tex.OldTexture); uint currentNew=(uint)(_cloTexNewProp?.GetValue(tex)??tex.NewTexture); if(map.TryGetValue(((int)obj.Index,oldT), out var desiredNew) && desiredNew!=currentNew){ try { _cloTexNewProp?.SetValue(tex,desiredNew);} catch { } } } }
        private void ScheduleTextureApply(){ ApplyTextureOverridesToClothing(); try { TextureCache.Init(false);} catch { } ClothingTableList.Instance?.LoadModelWithClothingBase(); MainWindow.Instance?.RealtimeJsonSync(); if (gridTextures?.SelectedItem is TextureRow tr) UpdateTexturePreviews(tr); UpdateJsonView(); }
        private void SaveLocalTextureOverrides(){ try { TextureOverrideLocalStore.Save(_textureRowsObs.Select(r=>new TextureOverrideLocalStore.Row{ PartIndex=r.PartIndex, OldId=r.OldId, NewId=r.NewId, IsLocked=r.IsLocked })); } catch { } }
        private void LoadLocalTextureOverrides(){ try { var rows=TextureOverrideLocalStore.Load(); if(rows.Count==0) return; foreach(var row in rows){ var match=_textureRowsObs.FirstOrDefault(r=>r.PartIndex==row.PartIndex && r.OldId==row.OldId); if(match!=null){ match.NewId=row.NewId; match.IsLocked=row.IsLocked; } else { _textureRowsObs.Add(new TextureRow{ PartIndex=row.PartIndex, OldId=row.OldId, NewId=row.NewId, IsLocked=row.IsLocked }); } } ScheduleTextureApply(); } catch { } }

        private Stack<Dictionary<(int part,uint oldId),uint>> _textureUndo=new();
        private Stack<Dictionary<(int part,uint oldId),uint>> _textureRedo=new();
        private bool _suppressTextureUndo;
        private Dictionary<(int part,uint oldId),uint> SnapshotTextureMapping()=>_textureRowsObs.ToDictionary(r=>(r.PartIndex,r.OldId),r=>r.NewId);
        private void PushTextureSnapshot(){ if(_suppressTextureUndo) return; _textureUndo.Push(SnapshotTextureMapping()); _textureRedo.Clear(); }
        private void ApplyTextureMapping(Dictionary<(int part,uint oldId),uint> map){ if(map==null) return; _suppressTextureUndo=true; foreach(var row in _textureRowsObs) if(map.TryGetValue((row.PartIndex,row.OldId), out var newId) && !row.IsLocked) row.NewId=newId; _suppressTextureUndo=false; ScheduleTextureApply(); SaveLocalTextureOverrides(); }
        private void BtnTexUndo_Click(object sender, RoutedEventArgs e)=>BtnTexUndo(); private void BtnTexRedo_Click(object sender, RoutedEventArgs e)=>BtnTexRedo();
        private void BtnTexUndo(){ if(_textureUndo.Count==0) return; var current=SnapshotTextureMapping(); var prev=_textureUndo.Pop(); _textureRedo.Push(current); ApplyTextureMapping(prev); }
        private void BtnTexRedo(){ if(_textureRedo.Count==0) return; var current=SnapshotTextureMapping(); var next=_textureRedo.Pop(); _textureUndo.Push(current); ApplyTextureMapping(next); }
        private void BtnTexApply_Click(object sender,RoutedEventArgs e)=>ScheduleTextureApply();
        private void BtnTexResetSelected_Click(object sender,RoutedEventArgs e){ var sel=gridTextures?.SelectedItems?.Cast<TextureRow>().ToList(); if(sel==null||sel.Count==0) return; PushTextureSnapshot(); foreach(var r in sel) if(!r.IsLocked) r.NewId=r.OldId; ScheduleTextureApply(); SaveLocalTextureOverrides(); }
        private void BtnTexResetAll_Click(object sender,RoutedEventArgs e){ PushTextureSnapshot(); foreach(var r in _textureRowsObs) if(!r.IsLocked) r.NewId=r.OldId; ScheduleTextureApply(); SaveLocalTextureOverrides(); }
        private void BtnTexLoadOverrides_Click(object sender,RoutedEventArgs e){ var dlg=new OpenFileDialog{ Filter="Texture Override (*.json)|*.json" }; if(dlg.ShowDialog()!=true) return; try { var json=File.ReadAllText(dlg.FileName); var list=JsonSerializer.Deserialize<List<TextureOverrideLocalStore.Row>>(json); if(list==null) return; PushTextureSnapshot(); foreach(var row in list){ var match=_textureRowsObs.FirstOrDefault(r=>r.PartIndex==row.PartIndex && r.OldId==row.OldId); if(match!=null && !match.IsLocked) match.NewId=row.NewId; } ScheduleTextureApply(); SaveLocalTextureOverrides(); } catch(Exception ex){ MessageBox.Show($"Failed to load overrides: {ex.Message}"); } }
        private void BtnTexSaveOverrides_Click(object sender,RoutedEventArgs e){ var dlg=new SaveFileDialog{ Filter="Texture Override (*.json)|*.json", FileName="TextureOverrides.json" }; if(dlg.ShowDialog()!=true) return; try { var list=_textureRowsObs.Select(r=>new TextureOverrideLocalStore.Row{ PartIndex=r.PartIndex, OldId=r.OldId, NewId=r.NewId, IsLocked=r.IsLocked }).ToList(); File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(list,new JsonSerializerOptions{ WriteIndented=true })); } catch(Exception ex){ MessageBox.Show($"Failed to save overrides: {ex.Message}"); } }
        private void GridTextures_SelectionChanged(object sender, SelectionChangedEventArgs e){ if(gridTextures?.SelectedItem is TextureRow tr) UpdateTexturePreviews(tr); else UpdateTexturePreviews(null); }
        private void GridTextures_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e){ PushTextureSnapshot(); }

        // JSON helper
        private void InitJsonTimer(){ _jsonApplyTimer ??= new DispatcherTimer{ Interval=TimeSpan.FromMilliseconds(600)}; _jsonApplyTimer.Tick -= JsonApplyTick; _jsonApplyTimer.Tick += JsonApplyTick; }
        private void JsonApplyTick(object sender, EventArgs e){ if(_txtJson==null) return; if((DateTime.UtcNow - _lastJsonEdit).TotalMilliseconds < 600) return; _jsonApplyTimer.Stop(); }
        private void UpdateJsonView(){ if(_txtJson==null) return; try { var palOk = TryBuildDefinition(out var palDef) ? palDef : ResultDefinition; var overrides=_textureRowsObs.Where(r=>r.NewId!=r.OldId).Select(r=>new { r.PartIndex, r.OldId, r.NewId }).ToList(); var payload=new { palette=palOk, textures=overrides, updatedUtc=DateTime.UtcNow }; _suppressJson=true; _txtJson.Text=JsonSerializer.Serialize(payload,new JsonSerializerOptions{ WriteIndented=true }); } catch { } finally { _suppressJson=false; } }

        // Palette list add
        private void BtnAddPaletteIds_Click(object sender,RoutedEventArgs e){ var txt=txtAddPaletteIds.Text; if(string.IsNullOrWhiteSpace(txt)) return; var tokens=txt.Split(',',StringSplitOptions.RemoveEmptyEntries); var added=false; foreach(var t in tokens){ try { if(t.Contains('-')){ var parts=t.Split('-'); uint a=ParseUInt(parts[0]); uint b=ParseUInt(parts[1]); if(a>b) (a,b)=(b,a); for(uint i=a;i<=b;i++){ if(!AvailablePaletteIDs.Contains(i)){ AvailablePaletteIDs.Add(i); added=true; } } } else { uint id=ParseUInt(t); if(!AvailablePaletteIDs.Contains(id)){ AvailablePaletteIDs.Add(id); added=true; } } } catch { } } if(added){ AvailablePaletteIDs=AvailablePaletteIDs.OrderBy(i=>i).ToList(); PopulateList(); } }
        private static uint ParseUInt(string s){ s=s.Trim(); if(s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) return Convert.ToUInt32(s.Substring(2),16); return Convert.ToUInt32(s); }
        private void SetFlashRangeFromSelection(){ _flashRange=null; }
    }
}
#pragma warning restore 0169, 0649, 0414
