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
using ACViewer.ClothingStudio;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACViewer.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Animation;
using ACViewer.CustomTextures;
using ACViewer.Services;
using ACViewer.Render;
using ACE.DatLoader.Entity;
using ACViewer.Model;
using ACE.Entity.Enum;
using Microsoft.Win32;
using ACViewer.View.Controls;
using ACViewer.ViewModels;
using System.IO;
using System.Windows.Threading;
using System.Windows.Data;
using ACViewer.Converters;
using System.Threading;
using System.Collections.Specialized;
using System.Text.Json;
using System.Collections.Concurrent;
using AvalonDock.Layout;
using Microsoft.Xna.Framework.Graphics; // Texture2D only, avoid XNA Color ambiguity

#pragma warning disable 0169, 0649, 0414
namespace ACViewer.View
{
    public partial class CustomPaletteDialog : UserControl, ICustomPaletteHost
    {
        // ===== New caching fields =====
        private static readonly ConcurrentDictionary<(uint id, int w, int h), ImageSource> _paletteBitmapCache = new();
        private static bool _paletteCacheWarmed; private static readonly object _paletteWarmLock = new();

        // Loading overlay elements
        private Grid _rootGrid;
        private Border _loadingOverlay;
        private TextBlock _loadingText;
        private TabItem _palettesTab; private TabItem _texturesTab; private TabItem _modelsTab; private TabItem _attachmentsTab; private TabItem _jsonTab;
        private TextBlock _studioModeBanner; private TextBlock _palettesHeader; private TextBlock _textureOverridesHeader; private TextBlock _modelBrowserHeader; private TextBlock _modelHelpText; private TextBlock _modelOverridesHeader; private TextBlock _attachmentHelpText;
        private bool _isMobBuilderMode; private string _mobBuilderName;
        private DataGridColumn _partColumn;

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

        private TextBox txtPaletteId; private TextBox txtSearch; private ListBox lstPalettes; private System.Windows.Controls.Image imgBigPreview; private StackPanel panelSetBrowse; private TextBlock lblSetIndex; private Slider sldSetIndex; private Button btnUseSetPalette; private StackPanel panelSingle; private TextBox txtRanges; private StackPanel panelMulti; private TextBox txtMulti; private TextBox txtShade; private TextBox txtExportPaletteTemplate; private TextBox txtExportIcon; private Slider sldShade; private Button btnOk; private Button btnSave; private Button btnLoad; private Button btnExport; private System.Windows.Controls.Image imgRangePreview; private TextBox txtColorSearch; private Button btnColorFind; private TextBlock lblColorResult; private ListView _lstRanges;
        private TextBox txtAddPaletteIds; private Button btnAddPaletteIds; private RangeEditorControl _rangeEditor; private Border _rangeEditorHost; private Button _btnRangeUndo; private Button _btnRangeRedo;

        private class RangeDisplay { public uint PaletteId { get; set; } public uint Offset { get; set; } public uint Length { get; set; } public string OffsetLength => $"{Offset}:{Length}"; }
        private class PaletteEntryRow : INotifyPropertyChanged
        { private uint _paletteSetId; private string _rangesText = string.Empty; private bool _isLocked; public uint PaletteSetId { get => _paletteSetId; set { if (_paletteSetId != value) { _paletteSetId = value; OnPropertyChanged(nameof(PaletteSetId)); } } } public string RangesText { get => _rangesText; set { var v = value ?? string.Empty; if (_rangesText != v) { _rangesText = v; OnPropertyChanged(nameof(RangesText)); } } } public bool IsLocked { get => _isLocked; set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(nameof(IsLocked)); } } } public event PropertyChangedEventHandler PropertyChanged; private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

        private ObservableCollection<PaletteEntryRow> _rows = new(); private DataGrid _gridEntries; private Button _btnAddRow; private Button _btnRemoveRow; private bool _freezeLines; private CheckBox _chkLockAll;
        private sealed class TextureRow : OverrideRowBase { }
        private sealed class ModelRow : OverrideRowBase { }
        private sealed class AttachmentRow : INotifyPropertyChanged
        {
            private bool _isLocked; private int _partIndex; private string _kind = "Particle"; private uint _effectId; private uint _emitterInfoId; private int _emitterId; private string _attachTo = "Part"; private float _offsetX; private float _offsetY; private float _offsetZ; private float _rotationX; private float _rotationY; private float _rotationZ; private float _scale = 1.0f; private string _note = string.Empty;
            public bool IsLocked { get => _isLocked; set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(nameof(IsLocked)); } } }
            public int PartIndex { get => _partIndex; set { if (_partIndex != value) { _partIndex = value; OnPropertyChanged(nameof(PartIndex)); } } }
            public string Kind { get => _kind; set { var v = string.IsNullOrWhiteSpace(value) ? "Particle" : value.Trim(); if (_kind != v) { _kind = v; OnPropertyChanged(nameof(Kind)); } } }
            public uint EffectId { get => _effectId; set { if (_effectId != value) { _effectId = value; OnPropertyChanged(nameof(EffectId)); OnPropertyChanged(nameof(EffectHex)); } } }
            public string EffectHex { get => $"0x{EffectId:X8}"; set { if (TryParseUInt(value, out var parsed)) EffectId = parsed; } }
            public uint EmitterInfoId { get => _emitterInfoId; set { if (_emitterInfoId != value) { _emitterInfoId = value; OnPropertyChanged(nameof(EmitterInfoId)); OnPropertyChanged(nameof(EmitterHex)); } } }
            public string EmitterHex { get => $"0x{EmitterInfoId:X8}"; set { if (TryParseUInt(value, out var parsed)) EmitterInfoId = parsed; } }
            public int EmitterId { get => _emitterId; set { if (_emitterId != value) { _emitterId = value; OnPropertyChanged(nameof(EmitterId)); } } }
            public string AttachTo { get => _attachTo; set { var v = string.IsNullOrWhiteSpace(value) ? "Part" : value.Trim(); if (_attachTo != v) { _attachTo = v; OnPropertyChanged(nameof(AttachTo)); } } }
            public float OffsetX { get => _offsetX; set { if (Math.Abs(_offsetX - value) > 0.0001f) { _offsetX = value; OnPropertyChanged(nameof(OffsetX)); } } }
            public float OffsetY { get => _offsetY; set { if (Math.Abs(_offsetY - value) > 0.0001f) { _offsetY = value; OnPropertyChanged(nameof(OffsetY)); } } }
            public float OffsetZ { get => _offsetZ; set { if (Math.Abs(_offsetZ - value) > 0.0001f) { _offsetZ = value; OnPropertyChanged(nameof(OffsetZ)); } } }
            public float RotationX { get => _rotationX; set { if (Math.Abs(_rotationX - value) > 0.0001f) { _rotationX = value; OnPropertyChanged(nameof(RotationX)); } } }
            public float RotationY { get => _rotationY; set { if (Math.Abs(_rotationY - value) > 0.0001f) { _rotationY = value; OnPropertyChanged(nameof(RotationY)); } } }
            public float RotationZ { get => _rotationZ; set { if (Math.Abs(_rotationZ - value) > 0.0001f) { _rotationZ = value; OnPropertyChanged(nameof(RotationZ)); } } }
            public float Scale { get => _scale; set { if (Math.Abs(_scale - value) > 0.0001f) { _scale = value; OnPropertyChanged(nameof(Scale)); } } }
            public string Note { get => _note; set { var v = value ?? string.Empty; if (_note != v) { _note = v; OnPropertyChanged(nameof(Note)); } } }
            private static bool TryParseUInt(string value, out uint parsed)
            {
                parsed = 0;
                if (string.IsNullOrWhiteSpace(value)) return false;
                var s = value.Trim();
                var style = NumberStyles.Integer;
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { s = s.Substring(2); style = NumberStyles.HexNumber; }
                return uint.TryParse(s, style, CultureInfo.InvariantCulture, out parsed);
            }
            public event PropertyChangedEventHandler PropertyChanged; private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
        private readonly ObservableCollection<AttachmentRow> _attachmentRows = new(); private DataGrid gridAttachments; private readonly List<TextureRow> _textureRows = new(); private List<uint> _availableTextureIds = new(); private ListBox lstTextures; private DataGrid gridTextures; private DataGrid gridModels; private TextBox txtModelFilter; private ListBox lstModelGallery; private TextBlock lblModelGalleryStatus; private Button btnModelPreviousPage; private Button btnModelNextPage; private CheckBox chkModelPartFocus; private readonly ObservableCollection<ModelRow> _modelRowsObs = new(); private readonly List<ModelRow> _modelRows = new(); private List<uint> _availableModelIds = new(); private int _modelPage; private const int ModelPageSize = 100; private DispatcherTimer _modelPreviewTimer; private uint? _pendingModelPreviewId; private DispatcherTimer _modelApplyTimer; private CheckBox chkTexLockAll; private Button btnTexAdd; private Button btnTexRemove; private Button btnTexSave; private Button btnTexLoad; private Button btnTexOk; private TextBox txtTexSearch; private System.Windows.Controls.Image imgTexPreviewOld; private System.Windows.Controls.Image imgTexPreviewNew; private TabControl _tabControl; private TabControl _rootTabs; private DateTime _lastAutosave = DateTime.MinValue; private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(2); private const string AutosaveFile = "CustomPalette.autosave.json"; private const string LiveSnapshotFile = "CustomPalette.current.json";
        private TextBlock lblTexturePaletteInfo; private ListBox lstTexturePaletteSuggestions; private Button btnLoadTexturePalette; private CheckBox chkAutoLoadTexturePalette; private TexturePaletteAnalysis _texturePaletteAnalysis; private string _lastAutoTexturePaletteKey;

        // New gallery UI fields
        private TextBox txtTexFilter; private ListBox lstTexGallery;
        private readonly ConcurrentDictionary<uint, ImageSource> _thumbCache = new();
        private TextBlock lblTexGalleryStatus;
        private Button btnTexPreviousPage;
        private Button btnTexNextPage;
        private int _texturePage;
        private const int TexturePageSize = 100;
        private readonly HashSet<uint> _thumbInFlight = new();
        private readonly object _thumbSync = new();
        private ImageSource _thumbPlaceholder;

        // Disco
        private string _discoBuffer = string.Empty; private DispatcherTimer _discoTimer; private bool _discoMode; private readonly Random _discoRand = new();

        private bool _hasClothing; // gate enabling of UI until clothing item active

        // Live save debounce
        private CancellationTokenSource _liveSaveCts; private static readonly TimeSpan LiveSaveDelay = TimeSpan.FromMilliseconds(600);

        // Texture old/new override
        private ObservableCollection<TextureRow> _textureRowsObs = new(); private bool _suppressTextureEvents; private bool _suppressModelEvents; private DateTime _lastTextureApply = DateTime.MinValue; private static readonly TimeSpan TextureApplyDebounce = TimeSpan.FromMilliseconds(180); private static readonly System.Reflection.PropertyInfo _cloTexNewProp = typeof(CloTextureEffect).GetProperty("NewTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); private static readonly System.Reflection.PropertyInfo _cloTexOldProp = typeof(CloTextureEffect).GetProperty("OldTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); private static readonly System.Reflection.PropertyInfo _cloObjIndexProp = typeof(CloObjectEffect).GetProperty("Index", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); private static readonly System.Reflection.PropertyInfo _cloObjModelProp = typeof(CloObjectEffect).GetProperty("ModelId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        // Flashing range highlight
        private DispatcherTimer _rangeFlashTimer; private bool _flashOn; private RangeDef _flashRange;

        // Add JSON viewer components
        private TextBox _txtJson; private bool _suppressJson;

        // NEW: defer initialization until DATs loaded
        private bool _initialized; // whether InitializeCore ran
        private DispatcherTimer _datPollTimer; // polls for DatManager readiness

        public CustomPaletteDialog()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            Content = BuildUI(); Loaded += CustomPaletteDialog_Loaded; KeyDown += CustomPaletteDialog_KeyDown; CustomPaletteStore.LoadAll(); ActiveInstance = this; // register
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

        public void SelectTextureTab()
        {
            if (_rootTabs != null && _texturesTab != null)
                _rootTabs.SelectedItem = _texturesTab;
        }

        public void SelectPaletteTab()
        {
            if (_rootTabs != null && _rootTabs.Items.Count > 0)
                _rootTabs.SelectedIndex = 0;
        }

        public void SetMobBuilderMode(string mobName = null)
        {
            _isMobBuilderMode = true;
            _mobBuilderName = string.IsNullOrWhiteSpace(mobName) ? "Mob Builder" : mobName.Trim();
            ApplyStudioModeLabels();
            SelectModelTab();
            MainWindow.Instance?.AddStatusText($"Mob Builder visual editor active{(string.IsNullOrWhiteSpace(_mobBuilderName) ? string.Empty : $": {_mobBuilderName}")}.");
        }

        public void SetClothingStudioMode()
        {
            _isMobBuilderMode = false;
            _mobBuilderName = null;
            ApplyStudioModeLabels();
        }

        public void SelectModelTab()
        {
            if (_rootTabs != null && _modelsTab != null)
                _rootTabs.SelectedItem = _modelsTab;
        }

        private void ApplyStudioModeLabels()
        {
            var mobTitle = string.IsNullOrWhiteSpace(_mobBuilderName) ? "Mob Builder" : $"Mob Builder - {_mobBuilderName}";
            var dockTitle = _isMobBuilderMode ? "Mob Builder" : "Clothing Studio";

            if (_studioModeBanner != null)
            {
                _studioModeBanner.Text = _isMobBuilderMode
                    ? $"Mob Builder Visual Editor{(string.IsNullOrWhiteSpace(_mobBuilderName) ? string.Empty : $" - {_mobBuilderName}")}"
                    : "Clothing Studio";
            }
            if (_palettesTab != null) _palettesTab.Header = _isMobBuilderMode ? "Mob Palettes" : "Palettes";
            if (_texturesTab != null) _texturesTab.Header = _isMobBuilderMode ? "Mob Textures" : "Textures";
            if (_modelsTab != null) _modelsTab.Header = _isMobBuilderMode ? "Mob Parts" : "Models";
            if (_attachmentsTab != null) _attachmentsTab.Header = _isMobBuilderMode ? "Mob Effects" : "DerpACE Effects";
            if (_jsonTab != null) _jsonTab.Header = _isMobBuilderMode ? "Mob JSON" : "Session JSON";

            if (_palettesHeader != null)
            {
                _palettesHeader.Text = _isMobBuilderMode ? "Mob Compatible Palettes" : "Compatible Palettes";
                _palettesHeader.ToolTip = _isMobBuilderMode
                    ? "Shows palettes and palette sets that can be applied to this mob visual setup."
                    : "Shows current clothing palettes plus compatible raw palettes and palette sets that cover this clothing's color ranges.";
            }
            if (_textureOverridesHeader != null) _textureOverridesHeader.Text = _isMobBuilderMode ? "Mob Texture Replacements" : "Texture Overrides";
            if (_modelBrowserHeader != null)
            {
                _modelBrowserHeader.Text = _isMobBuilderMode ? "Mob Part / World Object Browser" : "World Object / Model Browser";
                _modelBrowserHeader.ToolTip = _isMobBuilderMode
                    ? "Click a world object/model to preview it on the mob. Select mob part rows first to assign it."
                    : "Click an entry to preview it in the main 3D Preview. Select one or more override rows first to assign it.";
            }
            if (_modelHelpText != null)
                _modelHelpText.Text = _isMobBuilderMode
                    ? "Click a candidate to preview it on the mob. Select one or more mob part rows to replace that part/model."
                    : "Click a candidate to preview it in 3D. Select armor rows first to assign it as an override.";
            if (_modelOverridesHeader != null)
            {
                _modelOverridesHeader.Text = _isMobBuilderMode ? "Mob Visual Part Replacements" : "Armor Piece Model Overrides";
                _modelOverridesHeader.ToolTip = _isMobBuilderMode
                    ? "Each row is a mob visual part. Edit New Model or pick from the browser to replace that mob part."
                    : "Each row is a clothing-part model/world-object reference. Edit New Model or pick from the browser to override an armor piece.";
            }
            if (_attachmentHelpText != null)
                _attachmentHelpText.Text = _isMobBuilderMode
                    ? "DerpACE mob attachments export metadata only. Use this for particles, glows, trails, weapon-style visuals, or scripted effects attached to mob parts at runtime."
                    : "DerpACE runtime attachments export metadata only. Use this for particles, glows, trails, or weapon-style visuals that DerpACE/CloComp should attach to clothing parts at runtime.";

            try
            {
                var main = MainWindow.Instance;
                if (main != null)
                {
                    main.Title = _isMobBuilderMode ? "DerpACE Mob Builder" : "DerpACE Clothing Studio - CloComp";
                    var dock = main.DockManager?.Layout?.Descendents().OfType<LayoutAnchorable>().FirstOrDefault(a => a.ContentId == "CustomPaletteDock");
                    if (dock != null)
                    {
                        dock.Title = dockTitle;
                        if (dock.IsHidden) dock.Show();
                        dock.IsActive = true;
                    }
                }
            }
            catch { }
        }
        public void AddPaletteEntryFromMenu(uint paletteTemplate, uint iconId, uint paletteId, IReadOnlyList<RangeDef> ranges, float shade)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            if (!AvailablePaletteIDs.Contains(paletteId))
            {
                AvailablePaletteIDs.Add(paletteId);
                AvailablePaletteIDs = AvailablePaletteIDs.OrderBy(id => id).ToList();
                PopulateList();
            }

            SelectPaletteTab();
            if (txtExportPaletteTemplate != null) txtExportPaletteTemplate.Text = paletteTemplate.ToString(CultureInfo.InvariantCulture);
            if (txtExportIcon != null) txtExportIcon.Text = $"0x{iconId:X8}";
            txtShade.Text = shade.ToString("0.###", CultureInfo.InvariantCulture);
            if (sldShade != null) sldShade.Value = shade;

            var rangesText = string.Join(",", ranges.Select(r => $"{r.Offset}:{r.Length}"));
            var row = new PaletteEntryRow { PaletteSetId = paletteId, RangesText = rangesText };
            _rows.Add(row);
            EnforceNonOverlappingRanges(paletteId);
            RefreshRangeList();
            SetFlashRangeFromSelection();
            UpdateRangeHighlight();
            if (_gridEntries != null)
            {
                _gridEntries.SelectedItem = row;
                _gridEntries.ScrollIntoView(row);
            }
            SyncRangeEditorFromRow();
            DoLiveUpdate();
            UpdateJsonView();
            MainWindow.Instance?.AddStatusText($"Added pal #{paletteTemplate} using palette 0x{paletteId:X8} with ranges {rangesText}.");
        }

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
                RefreshAvailablePalettes();
                PopulateList();
                // Attempt to seed from current clothing if no explicit starting definition provided
                if (StartingDefinition == null && ClothingTableList.CurrentClothingItem != null)
                {
                    try
                    {
                        var seed = ClothingTableList.Instance?.GetCurrentSeedDefinition() ?? BuildDatRangeDefinition();
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
                SyncExportTargetFromCurrentClothing();
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

        public void LoadDefinition(CustomPaletteDefinition definition, bool isLive)
        {
            StartingDefinition = definition;
            if (!_initialized) return;
            RefreshForCurrentClothing(definition, isLive);
        }

        public void RefreshForCurrentClothing(CustomPaletteDefinition definition, bool applyLive = false)
        {
            SetHasClothing(ClothingTableList.CurrentClothingItem != null);
            if (!_initialized) return;

            RefreshAvailablePalettes();
            PopulateList();

            _textureUndo.Clear();
            _textureRedo.Clear();
            _thumbCache.Clear();
            lock (_thumbSync) _thumbInFlight.Clear();

            var datDefinition = definition ?? BuildDatRangeDefinition();
            if (datDefinition != null)
                ApplyLoadedPreset(datDefinition);
            else
            {
                _rows.Clear();
                ResultDefinition = null;
                SyncTextLinesFromRows();
                RefreshRangeList();
                SyncRangeEditorFromRow();
            }

            InitTextureTabData();
            LoadLocalTextureOverrides();
            UpdateJsonView();
            SyncExportTargetFromCurrentClothing();
            if (applyLive && definition != null)
                DoLiveUpdate();
        }

        private CustomPaletteDefinition BuildDatRangeDefinition()
        {
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null || clothing.ClothingSubPalEffects == null || clothing.ClothingSubPalEffects.Count == 0)
                return null;

            var template = ClothingTableList.GetPreferredPaletteTemplate();
            if (template == 0 || !clothing.ClothingSubPalEffects.ContainsKey(template))
                template = clothing.ClothingSubPalEffects.Keys.OrderBy(key => key).FirstOrDefault();
            if (template == 0 || !clothing.ClothingSubPalEffects.TryGetValue(template, out var effect) || effect?.CloSubPalettes == null)
                return null;

            var definition = new CustomPaletteDefinition
            {
                Name = $"DAT_{clothing.Id:X8}_{template:X8}",
                Multi = effect.CloSubPalettes.Count > 1,
                Shade = ClothingTableList.Shade
            };

            foreach (var subPalette in effect.CloSubPalettes)
            {
                if (subPalette.PaletteSet == 0 || subPalette.Ranges == null || subPalette.Ranges.Count == 0)
                    continue;

                var entry = new CustomPaletteEntry { PaletteSetId = subPalette.PaletteSet };
                foreach (var range in subPalette.Ranges)
                {
                    var groupOffset = range.Offset / 8;
                    var groupLength = Math.Max(1u, (range.NumColors + 7u) / 8u);
                    entry.Ranges.Add(new RangeDef { Offset = groupOffset, Length = groupLength });
                }

                if (entry.Ranges.Count > 0)
                    definition.Entries.Add(entry);
            }

            if (definition.Entries.Count == 0)
                return null;
            definition.Multi = definition.Entries.Count > 1;
            return definition;
        }
        public void HandleClothingIdChanged(uint oldId, uint newId)
        {
            SaveLocalTextureOverrides();
            SyncExportTargetFromCurrentClothing();
            UpdateJsonView();
            MainWindow.Instance?.AddStatusText($"Texture and palette session now targets clothing 0x{newId:X8}.");
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
            _loadingText = new TextBlock { Text = "Loading...", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ring = new System.Windows.Shapes.Ellipse { Width = 42, Height = 42, Stroke = Brushes.DeepSkyBlue, StrokeThickness = 4, Opacity = 0.85 };
            var anim = new DoubleAnimation { From = 0, To = 360, RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(1.4) };
            var rt = new RotateTransform(); ring.RenderTransform = rt; ring.RenderTransformOrigin = new Point(0.5, 0.5);
            rt.BeginAnimation(RotateTransform.AngleProperty, anim);
            sp.Children.Add(ring); sp.Children.Add(_loadingText);
            _loadingOverlay = new Border { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), Child = sp, Visibility = Visibility.Collapsed };
            _rootGrid.Children.Add(_loadingOverlay);
            return _rootGrid;
        }

        private UIElement BuildFindReplaceToggle(bool isModelMode)
        {
            var panel = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Toggle the same find/replace workflow between armor part models and surface textures."
            };

            var label = new TextBlock
            {
                Text = "Find/Replace:",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(label, Dock.Left);
            panel.Children.Add(label);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Left);
            panel.Children.Add(buttons);

            var modelButton = new Button
            {
                Content = isModelMode ? "✓ Models / Parts" : "Models / Parts",
                MinWidth = 112,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Find and replace armor part world objects / GfxObj models."
            };
            modelButton.Click += (_, __) => SwitchFindReplaceMode(showModels: true);
            buttons.Children.Add(modelButton);

            var textureButton = new Button
            {
                Content = !isModelMode ? "✓ Textures" : "Textures",
                MinWidth = 92,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Find and replace SurfaceTexture IDs on the selected part."
            };
            textureButton.Click += (_, __) => SwitchFindReplaceMode(showModels: false);
            buttons.Children.Add(textureButton);

            panel.Children.Add(new TextBlock
            {
                Text = isModelMode
                    ? "Browse models, select one or more part rows, then apply a replacement."
                    : "Browse textures, select one or more texture rows, then apply a replacement.",
                Foreground = Brushes.DarkGray,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        private void SwitchFindReplaceMode(bool showModels)
        {
            var part = GetSelectedPartIndex();
            if (_rootTabs != null)
                _rootTabs.SelectedItem = showModels ? _modelsTab : _texturesTab;
            SelectPartInActiveFindReplaceGrid(part, showModels);
            MainWindow.Instance?.AddStatusText(showModels
                ? "Find/replace mode: models and armor parts."
                : "Find/replace mode: textures.");
        }

        private int? GetSelectedPartIndex()
        {
            if (gridModels?.SelectedItem is ModelRow selectedModel)
                return selectedModel.PartIndex;
            if (gridTextures?.SelectedItem is TextureRow selectedTexture)
                return selectedTexture.PartIndex;
            if (gridAttachments?.SelectedItem is AttachmentRow selectedAttachment)
                return selectedAttachment.PartIndex;
            return null;
        }

        private void SelectPartInActiveFindReplaceGrid(int? part, bool showModels)
        {
            if (part == null) return;
            try
            {
                if (showModels && gridModels != null)
                {
                    var row = _modelRowsObs.FirstOrDefault(r => r.PartIndex == part.Value);
                    if (row != null) { gridModels.SelectedItem = row; gridModels.ScrollIntoView(row); }
                }
                else if (!showModels && gridTextures != null)
                {
                    var row = _textureRowsObs.FirstOrDefault(r => r.PartIndex == part.Value);
                    if (row != null) { gridTextures.SelectedItem = row; gridTextures.ScrollIntoView(row); UpdateTexturePreviews(row); }
                }
            }
            catch { }
        }
        // Rewritten for clarity (fix potential brace mismatch earlier)
        private UIElement BuildOriginalUI()
        {
            var root = new DockPanel { LastChildFill = true };
            _studioModeBanner = new TextBlock { Text = "Clothing Studio", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 6, 8, 4), Foreground = Brushes.Gainsboro };
            DockPanel.SetDock(_studioModeBanner, Dock.Top);
            root.Children.Add(_studioModeBanner);
            var tab = new TabControl();
            _rootTabs = tab;
            root.Children.Add(tab);

            // Palette tab
            var palTab = new TabItem { Header = "Palettes" }; _palettesTab = palTab;
            var texTab = new TabItem { Header = "Textures" }; _texturesTab = texTab;
            var modelTab = new TabItem { Header = "Models" }; _modelsTab = modelTab;
            tab.Items.Add(palTab); tab.Items.Add(texTab); tab.Items.Add(modelTab);

            var palDock = new DockPanel();
            palTab.Content = palDock;
            PaletteDockContent = palDock;
            TextureDockContent = texTab; // expose logical part; we use tab itself for textures

            // Bottom buttons (palette tab)
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            btnLoad = new Button { Content = "Load", Width = 70, Margin = new Thickness(0, 0, 6, 0) }; btnLoad.Click += btnLoad_Click;
            btnSave = new Button { Content = "Save", Width = 70, Margin = new Thickness(0, 0, 6, 0) }; btnSave.Click += btnSave_Click;
            btnOk = new Button { Content = "Apply to JSON", Width = 110, Margin = new Thickness(0, 0, 6, 0) }; btnOk.Click += btnOk_Click;
            btnExport = new Button { Content = "Export Mod JSON", Width = 120, Margin = new Thickness(0, 0, 6, 0) }; btnExport.Click += BtnExport_Click;
            buttons.Children.Add(btnLoad); buttons.Children.Add(btnSave); buttons.Children.Add(btnOk); buttons.Children.Add(btnExport);
            DockPanel.SetDock(buttons, Dock.Bottom);
            palDock.Children.Add(buttons);

            // Shade controls
            var shadeGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            shadeGrid.ColumnDefinitions.Add(new ColumnDefinition());
            shadeGrid.Children.Add(new TextBlock { Text = "Shade:" });
            txtShade = new TextBox { Text = "0", Height = 22 }; Grid.SetColumn(txtShade, 1); shadeGrid.Children.Add(txtShade);
            sldShade = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01 }; Grid.SetColumn(sldShade, 2); sldShade.ValueChanged += sldShade_ValueChanged; shadeGrid.Children.Add(sldShade);
            DockPanel.SetDock(shadeGrid, Dock.Bottom);
            palDock.Children.Add(shadeGrid);

            // Main grid
            var mainGrid = new Grid(); mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); mainGrid.ColumnDefinitions.Add(new ColumnDefinition()); palDock.Children.Add(mainGrid);

            // Left stack (palette list + ranges)
            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var exportTargetGrid = new Grid { Margin = new Thickness(0, 4, 0, 0), ToolTip = "Target values written to the CustomClothingBase JSON file." };
            exportTargetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            exportTargetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            exportTargetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            exportTargetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            exportTargetGrid.Children.Add(new TextBlock { Text = "PaletteTemplate:", VerticalAlignment = VerticalAlignment.Center });
            txtExportPaletteTemplate = new TextBox { Margin = new Thickness(4, 0, 10, 0) }; Grid.SetColumn(txtExportPaletteTemplate, 1); exportTargetGrid.Children.Add(txtExportPaletteTemplate);
            var iconLabel = new TextBlock { Text = "Icon:", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(iconLabel, 2); exportTargetGrid.Children.Add(iconLabel);
            txtExportIcon = new TextBox { Margin = new Thickness(4, 0, 0, 0) }; Grid.SetColumn(txtExportIcon, 3); exportTargetGrid.Children.Add(txtExportIcon);
            DockPanel.SetDock(exportTargetGrid, Dock.Bottom);
            palDock.Children.Add(exportTargetGrid);

            var leftStack = new StackPanel();
            leftScroll.Content = leftStack;
            mainGrid.Children.Add(leftScroll);

            _palettesHeader = new TextBlock { Text = "Compatible Palettes", FontWeight = FontWeights.Bold, ToolTip = "Shows current clothing palettes plus compatible raw palettes and palette sets that cover this clothing's color ranges." }; leftStack.Children.Add(_palettesHeader);
            txtSearch = new TextBox { Height = 22, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Filter compatible palettes by hex ID. Example: 0F00059E or 0400007E" }; txtSearch.TextChanged += txtSearch_TextChanged; leftStack.Children.Add(txtSearch);
            // New: Add palette IDs input (supports comma/range syntax e.g. 420-421,0x04001234)
            var addPalPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            txtAddPaletteIds = new TextBox { Width = 150, Height = 22, ToolTip = "Add palette IDs (e.g. 420-421,0x04001234)" };
            btnAddPaletteIds = new Button { Content = "+Pal", Width = 54, Margin = new Thickness(4, 0, 0, 0) };
            btnAddPaletteIds.Click += BtnAddPaletteIds_Click;
            addPalPanel.Children.Add(txtAddPaletteIds); addPalPanel.Children.Add(btnAddPaletteIds); leftStack.Children.Add(addPalPanel);

            lstPalettes = new ListBox { MinHeight = 200 }; lstPalettes.SelectionChanged += lstPalettes_SelectionChanged; VirtualizingStackPanel.SetIsVirtualizing(lstPalettes, true); VirtualizingStackPanel.SetVirtualizationMode(lstPalettes, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(lstPalettes, true); leftStack.Children.Add(lstPalettes);
            _gridEntries = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = _rows, Height = 140, Margin = new Thickness(0, 4, 0, 0), EnableRowVirtualization = true }; _gridEntries.SelectionChanged += (_, __) => { SetFlashRangeFromSelection(); UpdateRangeHighlight(); SyncRangeEditorFromRow(); }; _gridEntries.CellEditEnding += GridEntries_CellEditEnding; _gridEntries.BeginningEdit += GridEntries_BeginningEdit; _gridEntries.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new Binding("IsLocked"), Width = 30 }); var paletteCol = new DataGridTemplateColumn { Header = "Palette", IsReadOnly = true }; var palFactory = new FrameworkElementFactory(typeof(TextBlock)); palFactory.SetBinding(TextBlock.TextProperty, new Binding("PaletteSetId") { Mode = BindingMode.OneWay, Converter = new UIntToHexConverter() }); paletteCol.CellTemplate = new DataTemplate { VisualTree = palFactory }; _gridEntries.Columns.Add(paletteCol); _gridEntries.Columns.Add(new DataGridTextColumn { Header = "Ranges", Binding = new Binding("RangesText") }); leftStack.Children.Add(_gridEntries);
            var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) }; _btnAddRow = new Button { Content = "+", Width = 30, Margin = new Thickness(0, 0, 4, 0) }; _btnAddRow.Click += (_, __) => AddRowFromSelection(); _btnRemoveRow = new Button { Content = "-", Width = 30 }; _btnRemoveRow.Click += (_, __) => RemoveSelectedRow(); rowBtns.Children.Add(_btnAddRow); rowBtns.Children.Add(_btnRemoveRow); leftStack.Children.Add(rowBtns);

            // Right stack (preview + range editor)
            var rightStack = new StackPanel { Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(rightStack, 1); mainGrid.Children.Add(rightStack);
            rightStack.Children.Add(new TextBlock { Text = "Preview", FontWeight = FontWeights.Bold });
            imgBigPreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill };
            rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 2, 0, 6), Child = imgBigPreview });
            rightStack.Children.Add(new TextBlock { Text = "Range Editor", FontWeight = FontWeights.Bold });
            _btnRangeUndo = new Button { Content = "Undo", Width = 50, Margin = new Thickness(0, 0, 4, 0) }; _btnRangeRedo = new Button { Content = "Redo", Width = 50 };
            var undoPanel = new StackPanel { Orientation = Orientation.Horizontal }; undoPanel.Children.Add(_btnRangeUndo); undoPanel.Children.Add(_btnRangeRedo); rightStack.Children.Add(undoPanel);
            _btnRangeUndo.Click += (_, __) => { _rangeEditor?.Undo(); UpdateRangeUndoRedoButtons(); }; _btnRangeRedo.Click += (_, __) => { _rangeEditor?.Redo(); UpdateRangeUndoRedoButtons(); };
            _rangeEditor = new RangeEditorControl { Height = 44, Margin = new Thickness(0, 2, 0, 4) };
            _rangeEditor.RangesChanged += (_, list) => ApplyRangeEditorToSelectedRow(list);
            _rangeEditor.HistoryChanged += (_, __) => UpdateRangeUndoRedoButtons();
            rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _rangeEditor });
            rightStack.Children.Add(new TextBlock { Text = "All Ranges", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) }); _lstRanges = new ListView { Height = 120 }; rightStack.Children.Add(_lstRanges);
            rightStack.Children.Add(new TextBlock { Text = "Generated", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) }); txtMulti = new TextBox { AcceptsReturn = true, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsReadOnly = true }; rightStack.Children.Add(txtMulti);
            rightStack.Children.Add(new TextBlock { Text = "Highlight", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) }); imgRangePreview = new System.Windows.Controls.Image { Height = 48, Stretch = Stretch.Fill }; rightStack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = imgRangePreview });

            // Texture tab content
            var texRoot = new DockPanel { Margin = new Thickness(8) }; texTab.Content = texRoot;
            var texModeToggle = BuildFindReplaceToggle(isModelMode: false); DockPanel.SetDock(texModeToggle, Dock.Top); texRoot.Children.Add(texModeToggle);
            var texButtons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            btnTexLoad = new Button { Content = "Load Overrides", Margin = new Thickness(0, 0, 6, 0) }; btnTexLoad.Click += BtnTexLoadOverrides_Click;
            btnTexSave = new Button { Content = "Save Overrides", Margin = new Thickness(0, 0, 6, 0) }; btnTexSave.Click += BtnTexSaveOverrides_Click;
            var btnTexApply = new Button { Content = "Apply", Margin = new Thickness(0, 0, 6, 0) }; btnTexApply.Click += BtnTexApply_Click;
            var btnTexResetSel = new Button { Content = "Reset Selected", Margin = new Thickness(0, 0, 6, 0) }; btnTexResetSel.Click += BtnTexResetSelected_Click;
            var btnTexResetAll = new Button { Content = "Reset All", Margin = new Thickness(0, 0, 6, 0) }; btnTexResetAll.Click += BtnTexResetAll_Click;
            var btnTexUndo = new Button { Content = "Undo", Margin = new Thickness(0, 0, 6, 0) }; btnTexUndo.Click += BtnTexUndo_Click;
            var btnTexRedo = new Button { Content = "Redo", Margin = new Thickness(0, 0, 6, 0) }; btnTexRedo.Click += BtnTexRedo_Click;
            texButtons.Children.Add(btnTexLoad); texButtons.Children.Add(btnTexSave); texButtons.Children.Add(btnTexApply); texButtons.Children.Add(btnTexResetSel); texButtons.Children.Add(btnTexResetAll); texButtons.Children.Add(btnTexUndo); texButtons.Children.Add(btnTexRedo);
            DockPanel.SetDock(texButtons, Dock.Bottom); texRoot.Children.Add(texButtons);

            var texGrid = new Grid(); texGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 260 }); texGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 280 }); texRoot.Children.Add(texGrid);
            var leftEditors = new DockPanel { Margin = new Thickness(0, 0, 6, 0), LastChildFill = true };
            Grid.SetColumn(leftEditors, 0); texGrid.Children.Add(leftEditors);
            _textureOverridesHeader = new TextBlock { Text = "Texture Overrides", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3) }; leftEditors.Children.Add(_textureOverridesHeader);
            gridTextures = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false, ItemsSource = _textureRowsObs, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, EnableRowVirtualization = true, Height = 190 };
            gridTextures.SelectionChanged += GridTextures_SelectionChanged; gridTextures.CellEditEnding += GridTextures_CellEditEnding;
            gridTextures.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new Binding("IsLocked"), Width = 26 });
            _partColumn = new DataGridTextColumn { Header = "Part", Binding = new Binding("PartIndex"), IsReadOnly = true, Width = 50, Visibility = _hasClothing ? Visibility.Visible : Visibility.Collapsed }; gridTextures.Columns.Add(_partColumn);
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "Old", Binding = new Binding("OldHex"), IsReadOnly = true, Width = 110 });
            gridTextures.Columns.Add(new DataGridTextColumn { Header = "New", Binding = new Binding("NewId") { Converter = new UIntToHexConverter(), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 110 });
            leftEditors.Children.Add(gridTextures);



            var texBrowser = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texBrowser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(texBrowser, 1); texGrid.Children.Add(texBrowser);

            var previewLabels = new Grid(); previewLabels.ColumnDefinitions.Add(new ColumnDefinition()); previewLabels.ColumnDefinitions.Add(new ColumnDefinition());
            previewLabels.Children.Add(new TextBlock { Text = "Original", FontWeight = FontWeights.Bold });
            var newLabel = new TextBlock { Text = "Replacement", FontWeight = FontWeights.Bold }; Grid.SetColumn(newLabel, 1); previewLabels.Children.Add(newLabel);
            Grid.SetRow(previewLabels, 0); texBrowser.Children.Add(previewLabels);
            var previewGrid = new Grid { Margin = new Thickness(0, 3, 0, 8) }; previewGrid.ColumnDefinitions.Add(new ColumnDefinition()); previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
            imgTexPreviewOld = new System.Windows.Controls.Image { Height = 104, Stretch = Stretch.Uniform }; previewGrid.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Child = imgTexPreviewOld });
            imgTexPreviewNew = new System.Windows.Controls.Image { Height = 104, Stretch = Stretch.Uniform }; var newPreviewBorder = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(4, 0, 0, 0), Child = imgTexPreviewNew }; Grid.SetColumn(newPreviewBorder, 1); previewGrid.Children.Add(newPreviewBorder);
            Grid.SetRow(previewGrid, 1); texBrowser.Children.Add(previewGrid);
            var linkedPalettePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            linkedPalettePanel.Children.Add(new TextBlock { Text = "Palettes linked to selected texture", FontWeight = FontWeights.Bold });
            lblTexturePaletteInfo = new TextBlock { Text = "Select an indexed texture to inspect its palette.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 3), Foreground = Brushes.DarkGray };
            linkedPalettePanel.Children.Add(lblTexturePaletteInfo);
            lstTexturePaletteSuggestions = new ListBox { Height = 72, DisplayMemberPath = "DisplayName", ToolTip = "Suggestions are derived from the texture's default palette, its shade sets, and palettes already used by this clothing table." };
            linkedPalettePanel.Children.Add(lstTexturePaletteSuggestions);
            chkAutoLoadTexturePalette = new CheckBox { Content = "Auto-load suggested palette ranges", IsChecked = true, Margin = new Thickness(0, 3, 0, 0), ToolTip = "When an indexed texture exposes palette/range data, load the best matching palette suggestion automatically." };
            linkedPalettePanel.Children.Add(chkAutoLoadTexturePalette);
            btnLoadTexturePalette = new Button { Content = "Load suggestion into Palettes", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(7, 2, 7, 2) };
            btnLoadTexturePalette.Click += BtnLoadTexturePalette_Click;
            linkedPalettePanel.Children.Add(btnLoadTexturePalette);
            Grid.SetRow(linkedPalettePanel, 2); texBrowser.Children.Add(linkedPalettePanel);

            var galleryLabel = new TextBlock { Text = "SurfaceTexture Browser", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 3), ToolTip = "Select a texture to apply it to the checked clothing-part rows." }; Grid.SetRow(galleryLabel, 3); texBrowser.Children.Add(galleryLabel);
            txtTexFilter = new TextBox { Height = 24, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Filter by hexadecimal SurfaceTexture ID" }; txtTexFilter.TextChanged += TexFilter_TextChanged; Grid.SetRow(txtTexFilter, 4); texBrowser.Children.Add(txtTexFilter);
            var pager = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            btnTexPreviousPage = new Button { Content = "Previous", MinWidth = 72 }; btnTexPreviousPage.Click += (_, __) => { if (_texturePage > 0) { _texturePage--; RefreshTextureGallery(); } }; DockPanel.SetDock(btnTexPreviousPage, Dock.Left); pager.Children.Add(btnTexPreviousPage);
            btnTexNextPage = new Button { Content = "Next", MinWidth = 72 }; btnTexNextPage.Click += (_, __) => { _texturePage++; RefreshTextureGallery(); }; DockPanel.SetDock(btnTexNextPage, Dock.Right); pager.Children.Add(btnTexNextPage);
            lblTexGalleryStatus = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray }; pager.Children.Add(lblTexGalleryStatus);
            Grid.SetRow(pager, 5); texBrowser.Children.Add(pager);
            lstTexGallery = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Single, Background = Brushes.Transparent, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) }; lstTexGallery.SelectionChanged += TexGallery_SelectionChanged; VirtualizingStackPanel.SetIsVirtualizing(lstTexGallery, true); VirtualizingStackPanel.SetVirtualizationMode(lstTexGallery, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(lstTexGallery, true); ScrollViewer.SetIsDeferredScrollingEnabled(lstTexGallery, true); Grid.SetRow(lstTexGallery, 6); texBrowser.Children.Add(lstTexGallery);

            // Models tab content
            var modelRoot = new DockPanel { Margin = new Thickness(8), LastChildFill = true }; modelTab.Content = modelRoot;
            var modelModeToggle = BuildFindReplaceToggle(isModelMode: true); DockPanel.SetDock(modelModeToggle, Dock.Top); modelRoot.Children.Add(modelModeToggle);
            var modelButtons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var btnModelAddPart = new Button { Content = "+ Part", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Add a clothing part index and assign it a model/world-object id." }; btnModelAddPart.Click += (_, __) => AddModelPartRow();
            var btnModelRemovePart = new Button { Content = "Remove Part", MinWidth = 96, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Delete the selected unlocked clothing part rows from the working clothing table. Use Reset Selected to restore a part instead." }; btnModelRemovePart.Click += (_, __) => RemoveSelectedModelPartRows();
            var btnModelApply = new Button { Content = "Apply Models", MinWidth = 96, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Apply the selected model/world-object overrides to the 3D clothing preview." }; btnModelApply.Click += (_, __) => ScheduleModelApply();
            var btnModelResetSel = new Button { Content = "Reset Selected", MinWidth = 100, Margin = new Thickness(0, 0, 6, 0) }; btnModelResetSel.Click += (_, __) => { var targets = gridModels?.SelectedItems?.Cast<ModelRow>().ToList(); if (targets == null || targets.Count == 0) return; MutateModelRows(() => { foreach (var target in targets) if (!target.IsLocked) target.NewId = target.OldId; }); ScheduleModelApply(); };
            var btnModelResetAll = new Button { Content = "Reset All", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) }; btnModelResetAll.Click += (_, __) => { MutateModelRows(() => { foreach (var row in _modelRowsObs) if (!row.IsLocked) row.NewId = row.OldId; }); ScheduleModelApply(); };
            modelButtons.Children.Add(btnModelAddPart); modelButtons.Children.Add(btnModelRemovePart); modelButtons.Children.Add(btnModelApply); modelButtons.Children.Add(btnModelResetSel); modelButtons.Children.Add(btnModelResetAll);
            DockPanel.SetDock(modelButtons, Dock.Bottom); modelRoot.Children.Add(modelButtons);

            var modelLayout = new Grid();
            modelLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230), MinHeight = 150 });
            modelLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modelLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 140 });
            modelRoot.Children.Add(modelLayout);

            var modelBrowser = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            modelBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modelBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modelBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modelBrowser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modelBrowser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(modelBrowser, 0); modelLayout.Children.Add(modelBrowser);

            _modelBrowserHeader = new TextBlock { Text = "World Object / Model Browser", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3), ToolTip = "Click an entry to preview it in the main 3D Preview. Select one or more override rows first to assign it." }; modelBrowser.Children.Add(_modelBrowserHeader);
            _modelHelpText = new TextBlock { Text = "Click a candidate to preview it in 3D. Select armor rows first to assign it as an override.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGray, Margin = new Thickness(0, 0, 0, 4) };
            var modelHelp = _modelHelpText;
            Grid.SetRow(modelHelp, 1); modelBrowser.Children.Add(modelHelp);
            chkModelPartFocus = new CheckBox { Content = "Show selected part only", IsChecked = true, Margin = new Thickness(0, 0, 0, 4), ToolTip = "When selecting an override row, isolate that part index in the 3D preview." }; chkModelPartFocus.Checked += (_, __) => FocusSelectedModelPart(); chkModelPartFocus.Unchecked += (_, __) => { ModelViewer.PartIdx = -1; MainWindow.Instance?.AddStatusText("Showing all model parts."); };
            Grid.SetRow(chkModelPartFocus, 2); modelBrowser.Children.Add(chkModelPartFocus);
            txtModelFilter = new TextBox { Height = 24, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Filter by hexadecimal model/setup/world-object ID" }; txtModelFilter.TextChanged += ModelFilter_TextChanged; Grid.SetRow(txtModelFilter, 3); modelBrowser.Children.Add(txtModelFilter);

            var modelGalleryShell = new DockPanel { LastChildFill = true };
            Grid.SetRow(modelGalleryShell, 4); modelBrowser.Children.Add(modelGalleryShell);
            var modelPager = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            btnModelPreviousPage = new Button { Content = "Previous", MinWidth = 72 }; btnModelPreviousPage.Click += (_, __) => { if (_modelPage > 0) { _modelPage--; RefreshModelGallery(); } }; DockPanel.SetDock(btnModelPreviousPage, Dock.Left); modelPager.Children.Add(btnModelPreviousPage);
            btnModelNextPage = new Button { Content = "Next", MinWidth = 72 }; btnModelNextPage.Click += (_, __) => { _modelPage++; RefreshModelGallery(); }; DockPanel.SetDock(btnModelNextPage, Dock.Right); modelPager.Children.Add(btnModelNextPage);
            lblModelGalleryStatus = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray }; modelPager.Children.Add(lblModelGalleryStatus);
            DockPanel.SetDock(modelPager, Dock.Bottom); modelGalleryShell.Children.Add(modelPager);
            lstModelGallery = new ListBox { MinHeight = 96, HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Single, Background = Brushes.Transparent, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) }; lstModelGallery.SelectionChanged += ModelGallery_SelectionChanged; VirtualizingStackPanel.SetIsVirtualizing(lstModelGallery, true); VirtualizingStackPanel.SetVirtualizationMode(lstModelGallery, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(lstModelGallery, true); ScrollViewer.SetIsDeferredScrollingEnabled(lstModelGallery, true); modelGalleryShell.Children.Add(lstModelGallery);

            var modelSplitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.DimGray, ShowsPreview = true };
            Grid.SetRow(modelSplitter, 1); modelLayout.Children.Add(modelSplitter);

            var modelOverrides = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetRow(modelOverrides, 2); modelLayout.Children.Add(modelOverrides);
            _modelOverridesHeader = new TextBlock { Text = "Armor Piece Model Overrides", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3), ToolTip = "Each row is a clothing-part model/world-object reference. Edit New Model or pick from the browser to override an armor piece." }; modelOverrides.Children.Add(_modelOverridesHeader);
            gridModels = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false, ItemsSource = _modelRowsObs, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, EnableRowVirtualization = true, MinHeight = 120, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            gridModels.CellEditEnding += GridModels_CellEditEnding;
            gridModels.SelectionChanged += (_, __) => FocusSelectedModelPart();
            gridModels.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new Binding("IsLocked"), Width = 28 });
            gridModels.Columns.Add(new DataGridTextColumn { Header = "Part", Binding = new Binding("PartIndex") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 55 });
            gridModels.Columns.Add(new DataGridTextColumn { Header = "Old Model", Binding = new Binding("OldHex"), IsReadOnly = true, Width = 120 });
            gridModels.Columns.Add(new DataGridTextColumn { Header = "New Model", Binding = new Binding("NewId") { Converter = new UIntToHexConverter(), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 120 });
            modelOverrides.Children.Add(gridModels);

            // DerpACE runtime metadata tab. These rows are intentionally exported as editor/session JSON;
            // DerpACE or CloComp can translate them into runtime particle hooks / weapon visuals.
            var attachmentTab = new TabItem { Header = "DerpACE Effects" }; _attachmentsTab = attachmentTab;
            attachmentTab.Content = BuildDerpAttachmentTab();
            tab.Items.Add(attachmentTab);

            // Add JSON tab
            var jsonTab = new TabItem { Header = "Session JSON" }; _jsonTab = jsonTab;
            var jsonGrid = new Grid();
            _txtJson = new TextBox { AcceptsReturn = true, IsReadOnly = true, ToolTip = "Read-only snapshot of the current palette and texture session", VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap };
            jsonGrid.Children.Add(_txtJson); jsonTab.Content = jsonGrid; tab.Items.Add(jsonTab);

            ApplyStudioModeLabels();
            return root;
        }
        private UIElement BuildDerpAttachmentTab()
        {
            var root = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var btnAdd = new Button { Content = "+ Attachment", MinWidth = 96, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Add DerpACE runtime metadata for a particle/visual attachment on a clothing part." };
            btnAdd.Click += (_, __) => AddDerpAttachmentRow();
            var btnDuplicate = new Button { Content = "Duplicate", MinWidth = 86, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Copy the selected attachment row." };
            btnDuplicate.Click += (_, __) => DuplicateSelectedDerpAttachmentRows();
            var btnRemove = new Button { Content = "Remove Selected", MinWidth = 112, Margin = new Thickness(0, 0, 6, 0) };
            btnRemove.Click += (_, __) => RemoveSelectedDerpAttachmentRows();
            var btnReset = new Button { Content = "Clear All", MinWidth = 76, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Clear all DerpACE-only attachment metadata from this editor session." };
            btnReset.Click += (_, __) => { _attachmentRows.Clear(); UpdateJsonView(); MainWindow.Instance?.RealtimeJsonSync(); };
            buttons.Children.Add(btnAdd); buttons.Children.Add(btnDuplicate); buttons.Children.Add(btnRemove); buttons.Children.Add(btnReset);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

            var stack = new DockPanel { LastChildFill = true };
            root.Children.Add(stack);
            var help = new TextBlock
            {
                Text = "DerpACE runtime attachments export metadata only. Use this for particles, glows, trails, or weapon-style visuals that DerpACE/CloComp should attach to clothing parts at runtime.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkGray,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _attachmentHelpText = help;
            DockPanel.SetDock(help, Dock.Top); stack.Children.Add(help);
            ApplyStudioModeLabels();

            gridAttachments = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false, ItemsSource = _attachmentRows, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, EnableRowVirtualization = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            gridAttachments.CellEditEnding += (_, __) => Dispatcher.BeginInvoke(new Action(() => { UpdateJsonView(); MainWindow.Instance?.RealtimeJsonSync(); }));
            gridAttachments.SelectionChanged += (_, __) => FocusSelectedAttachmentPart();
            gridAttachments.Columns.Add(new DataGridCheckBoxColumn { Header = "L", Binding = new Binding("IsLocked"), Width = 28 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Part", Binding = new Binding("PartIndex") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 55 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Binding("Kind") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 90 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Effect/PES", Binding = new Binding("EffectHex") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 115 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "EmitterInfo", Binding = new Binding("EmitterHex") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 115 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Emitter", Binding = new Binding("EmitterId") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 70 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Attach To", Binding = new Binding("AttachTo") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 95 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "X", Binding = new Binding("OffsetX") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 55 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Y", Binding = new Binding("OffsetY") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 55 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Z", Binding = new Binding("OffsetZ") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 55 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Rot X", Binding = new Binding("RotationX") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 60 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Rot Y", Binding = new Binding("RotationY") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 60 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Rot Z", Binding = new Binding("RotationZ") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 60 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Scale", Binding = new Binding("Scale") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 65 });
            gridAttachments.Columns.Add(new DataGridTextColumn { Header = "Note", Binding = new Binding("Note") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = 220 });
            stack.Children.Add(gridAttachments);
            _attachmentRows.CollectionChanged += (_, __) => UpdateJsonView();
            return root;
        }

        private int GetDefaultAttachmentPartIndex()
        {
            if (gridModels?.SelectedItem is ModelRow selectedModel) return selectedModel.PartIndex;
            if (gridTextures?.SelectedItem is TextureRow selectedTexture) return selectedTexture.PartIndex;
            return 0;
        }

        private void AddDerpAttachmentRow()
        {
            var row = new AttachmentRow { PartIndex = GetDefaultAttachmentPartIndex(), Kind = "Particle", AttachTo = "Part", Scale = 1.0f };
            _attachmentRows.Add(row);
            if (gridAttachments != null)
            {
                gridAttachments.SelectedItem = row;
                gridAttachments.ScrollIntoView(row);
            }
            UpdateJsonView();
            MainWindow.Instance?.RealtimeJsonSync();
        }

        private void DuplicateSelectedDerpAttachmentRows()
        {
            var selected = gridAttachments?.SelectedItems?.Cast<AttachmentRow>().ToList();
            if (selected == null || selected.Count == 0) return;
            foreach (var row in selected.Where(r => !r.IsLocked).ToList())
                _attachmentRows.Add(new AttachmentRow { PartIndex = row.PartIndex, Kind = row.Kind, EffectId = row.EffectId, EmitterInfoId = row.EmitterInfoId, EmitterId = row.EmitterId, AttachTo = row.AttachTo, OffsetX = row.OffsetX, OffsetY = row.OffsetY, OffsetZ = row.OffsetZ, RotationX = row.RotationX, RotationY = row.RotationY, RotationZ = row.RotationZ, Scale = row.Scale, Note = row.Note });
            UpdateJsonView();
            MainWindow.Instance?.RealtimeJsonSync();
        }

        private void RemoveSelectedDerpAttachmentRows()
        {
            var selected = gridAttachments?.SelectedItems?.Cast<AttachmentRow>().Where(r => !r.IsLocked).ToList();
            if (selected == null || selected.Count == 0) return;
            foreach (var row in selected) _attachmentRows.Remove(row);
            UpdateJsonView();
            MainWindow.Instance?.RealtimeJsonSync();
        }

        private void FocusSelectedAttachmentPart()
        {
            if (gridAttachments?.SelectedItem is not AttachmentRow row) return;
            ModelViewer.PartIdx = row.PartIndex;
            MainWindow.Instance?.AddStatusText($"Editing DerpACE attachment on clothing part {row.PartIndex}.");
        }
        private void RefreshAvailablePalettes()
        {
            try
            {
                AvailablePaletteIDs = ClothingTableList.BuildAvailablePaletteIdList();
                txtSearch_TextChanged(this, null);
                MainWindow.Instance?.AddStatusText($"Loaded {AvailablePaletteIDs.Count:N0} compatible palettes for the current clothing ranges.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.AddStatusText($"Palette discovery failed: {ex.Message}");
                AvailablePaletteIDs ??= new List<uint>();
            }
        }
        // === Existing palette code unchanged below (shortened for brevity) ===
        private void PopulateList() { if (AvailablePaletteIDs == null || lstPalettes == null) return; if (DatManager.PortalDat == null) return; lstPalettes.Items.Clear(); foreach (var id in AvailablePaletteIDs) lstPalettes.Items.Add(BuildPaletteListItem(id)); }
        private ListBoxItem BuildPaletteListItem(uint id) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) }; var img = new System.Windows.Controls.Image { Width = 220, Height = 14, Stretch = Stretch.Fill, SnapsToDevicePixels = true }; img.Source = BuildPaletteBitmap(id, 220, 14); sp.Children.Add(img); sp.Children.Add(new TextBlock { Text = $" 0x{id:X8}", VerticalAlignment = VerticalAlignment.Center }); return new ListBoxItem { Content = sp, Tag = id, ToolTip = $"0x{id:X8}" }; }
        private ImageSource BuildPaletteBitmap(uint id, int width, int height) { var key = (id, width, height); if (_paletteBitmapCache.TryGetValue(key, out var cached) && cached != null) return cached; try { if (DatManager.PortalDat == null) return null; uint paletteId = id; if ((id >> 24) == 0xF) { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(id); if (set?.PaletteList?.Count > 0) paletteId = set.PaletteList[0]; } var palette = DatManager.PortalDat.ReadFromDat<Palette>(paletteId); if (palette == null || palette.Colors.Count == 0) return null; var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); var pixels = new byte[width * height * 4]; for (int x = 0; x < width; x++) { int palIdx = (int)((double)x / width * (palette.Colors.Count - 1)); uint col = palette.Colors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF); for (int y = 0; y < height; y++) { int idx = (y * width + x) * 4; pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = a; } } wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0); wb.Freeze(); _paletteBitmapCache[key] = wb; return wb; } catch { return null; } }
        private void UpdateBigPreviewImage() { if (imgBigPreview == null) return; if (DatManager.PortalDat == null) { imgBigPreview.Source = null; return; } if (_currentSet != null) { if (_currentSet.PaletteList == null || _currentSet.PaletteList.Count == 0) { imgBigPreview.Source = null; return; } var safeIndex = Math.Clamp(_currentSetIndex, 0, _currentSet.PaletteList.Count - 1); var palId = _currentSet.PaletteList[safeIndex]; imgBigPreview.Source = BuildPaletteBitmap(palId, PreviewWidth, PreviewHeight); return; } else if (lstPalettes?.SelectedItem is ListBoxItem li) imgBigPreview.Source = BuildPaletteBitmap((uint)li.Tag, PreviewWidth, PreviewHeight); }
        private void UpdateRangeHighlight() { if (imgRangePreview == null) return; if (DatManager.PortalDat == null) { imgRangePreview.Source = null; return; } Palette palette = null; uint actualPaletteId = 0; string rangeSpec = null; if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow rowSel) { uint palId = rowSel.PaletteSetId; actualPaletteId = palId; if ((palId >> 24) == 0xF) { try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set != null && set.PaletteList.Count > 0) actualPaletteId = set.PaletteList[0]; } catch { imgRangePreview.Source = null; return; } } try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actualPaletteId); } catch { palette = null; } rangeSpec = rowSel.RangesText?.Replace(',', ' '); } else { imgRangePreview.Source = null; return; } if (palette == null || palette.Colors.Count == 0 || string.IsNullOrWhiteSpace(rangeSpec)) { imgRangePreview.Source = null; return; } var ranges = RangeParser.ParseRanges(rangeSpec, true, out _); int colors = palette.Colors.Count; int width = 512; int height = 64; var wb2 = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null); var pixels2 = new byte[width * height * 4]; for (int x = 0; x < width; x++) { int palIdx = (int)((double)x / width * (colors - 1)); uint col = palette.Colors[palIdx]; byte a = (byte)((col >> 24) & 0xFF); if (a == 0) a = 0xFF; byte r = (byte)((col >> 16) & 0xFF); byte g = (byte)((col >> 8) & 0xFF); byte b = (byte)(col & 0xFF); bool highlighted = false; foreach (var rg in ranges) { var start = (int)rg.Offset * 8; var count = (int)rg.Length * 8; if (palIdx >= start && palIdx < start + count) { highlighted = true; break; } } if (highlighted) { r = (byte)Math.Min(255, r + 80); g = (byte)Math.Min(255, g + 80); b = (byte)Math.Min(255, b + 80); } for (int y = 0; y < height; y++) { int idx = (y * width + x) * 4; pixels2[idx] = b; pixels2[idx + 1] = g; pixels2[idx + 2] = r; pixels2[idx + 3] = a; } } wb2.WritePixels(new Int32Rect(0, 0, width, height), pixels2, width * 4, 0); imgRangePreview.Source = wb2; }
        private void RefreshRangeList() { if (_lstRanges == null) return; var items = new List<RangeDisplay>(); foreach (var row in _rows) { var text = (row.RangesText ?? string.Empty).Replace(',', ' '); var ranges = RangeParser.ParseRanges(text, true, out _); foreach (var r in ranges) items.Add(new RangeDisplay { PaletteId = row.PaletteSetId, Offset = r.Offset, Length = r.Length }); } _lstRanges.ItemsSource = items; }
        private void SyncRangeEditorFromRow() { if (_rangeEditor == null) return; if (DatManager.PortalDat == null) { _rangeEditor.SetPalette(null); return; } if (_gridEntries?.SelectedItem is not PaletteEntryRow row) { _rangeEditor.SetPalette(null); _rangeEditor.SetRanges(Array.Empty<RangeDef>()); UpdateRangeUndoRedoButtons(); return; } uint palId = row.PaletteSetId; uint actual = palId; if ((palId >> 24) == 0xF) { try { var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId); if (set?.PaletteList?.Count > 0) actual = set.PaletteList[0]; } catch { } } Palette palette = null; try { palette = DatManager.PortalDat.ReadFromDat<Palette>(actual); } catch { } _rangeEditor.SetPalette(palette); var parsed = RangeParser.ParseRanges(((row.RangesText ?? string.Empty).Replace(',', ' ')), true, out _); _rangeEditor.SetRanges(parsed); UpdateRangeUndoRedoButtons(); }
        private void ApplyRangeEditorToSelectedRow(IReadOnlyList<RangeDef> list) { if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return; if (row.IsLocked) return; row.RangesText = string.Join(",", list.Select(r => $"{r.Offset}:{r.Length}")); EnforceNonOverlappingRanges(row.PaletteSetId); RefreshRangeList(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); DoLiveUpdate(); if (!_freezeLines) SyncTextLinesFromRows(); _gridEntries.Items.Refresh(); UpdateRangeUndoRedoButtons(); UpdateJsonView(); }
        private void lstPalettes_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (DatManager.PortalDat == null) return; if (lstPalettes?.SelectedItem is not ListBoxItem li) return; uint id = (uint)li.Tag; if ((id >> 24) == 0xF) { _currentSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(id); _currentSetIndex = 0; } else _currentSet = null; if (_gridEntries != null && _gridEntries.SelectedItem is PaletteEntryRow row && !row.IsLocked) { row.PaletteSetId = id; RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); _gridEntries.Items.Refresh(); } UpdateBigPreviewImage(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); RefreshRangeList(); SyncRangeEditorFromRow(); }
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
        private void btnOk_Click(object sender, RoutedEventArgs e) { if (TryBuildDefinition(out var def)) { ResultDefinition = def; OnLiveUpdate?.Invoke(def); EnsureDefinitionHasName(def); PersistImmediate(def); } else MessageBox.Show("Invalid definition", "Custom Palette", MessageBoxButton.OK, MessageBoxImage.Error); }
        private void btnSave_Click(object sender, RoutedEventArgs e) { if (ResultDefinition == null && TryBuildDefinition(out var temp)) ResultDefinition = temp; if (ResultDefinition == null) return; var name = Microsoft.VisualBasic.Interaction.InputBox("Preset Name:", "Save Custom Palette", ResultDefinition.Name ?? "MyPreset"); if (string.IsNullOrWhiteSpace(name)) return; ResultDefinition.Name = name.Trim(); CustomPaletteStore.SaveDefinition(ResultDefinition); MessageBox.Show("Saved."); }
        private void btnLoad_Click(object sender, RoutedEventArgs e) { var defs = CustomPaletteStore.LoadAll().ToList(); if (defs.Count == 0) { MessageBox.Show("No saved presets."); return; } var picker = new PresetPickerWindow(defs); if (picker.ShowDialog() == true && picker.Selected != null) { ApplyLoadedPreset(picker.Selected); DoLiveUpdate(); } SyncRangeEditorFromRow(); }
        public void ApplyLoadedPreset(CustomPaletteDefinition def)
        {
            if (def == null) return;
            _rows.Clear();
            foreach (var e in def.Entries)
            {
                var rtxt = (e.Ranges != null) ? string.Join(",", e.Ranges.Select(r => $"{r.Offset}:{r.Length}")) : string.Empty;
                _rows.Add(new PaletteEntryRow { PaletteSetId = e.PaletteSetId, RangesText = rtxt });
            }
            EnforceNonOverlappingRanges();
            if (!_freezeLines) SyncTextLinesFromRows();
            txtShade.Text = def.Shade.ToString("0.###", CultureInfo.InvariantCulture);
            sldShade.Value = def.Shade;
            ResultDefinition = def;
            if (_gridEntries != null && _rows.Count > 0 && _gridEntries.SelectedItem == null)
                _gridEntries.SelectedIndex = 0;
            UpdateBigPreviewImage();
            RefreshRangeList();
            SyncRangeEditorFromRow();
            SetFlashRangeFromSelection();
            UpdateRangeHighlight();
            UpdateRangeUndoRedoButtons();
        }
        private void GridEntries_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { if (e.Row?.Item is not PaletteEntryRow row) return; if (row.IsLocked) { e.Cancel = true; return; } if (e.EditingElement is TextBox tb) { row.RangesText = tb.Text?.Trim(); EnforceNonOverlappingRanges(row.PaletteSetId); SyncTextLinesFromRows(); RefreshRangeList(); UpdateRangeHighlight(); DoLiveUpdate(); SyncRangeEditorFromRow(); } }
        private void GridEntries_BeginningEdit(object sender, DataGridBeginningEditEventArgs e) { if (e.Row?.Item is PaletteEntryRow row && row.IsLocked) e.Cancel = true; }
        private void AddRowFromSelection() { uint palId = 0; if (lstPalettes?.SelectedItem is ListBoxItem li) palId = (uint)li.Tag; else if (_currentSet != null && _currentSet.PaletteList?.Count > 0) palId = _currentSet.PaletteList[0]; else if (AvailablePaletteIDs?.Count > 0) palId = AvailablePaletteIDs[0]; if (palId == 0) return; var newRow = new PaletteEntryRow { PaletteSetId = palId, RangesText = "0:1" }; _rows.Add(newRow); EnforceNonOverlappingRanges(palId); SyncTextLinesFromRows(); RefreshRangeList(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); DoLiveUpdate(); _gridEntries.SelectedItem = newRow; SyncRangeEditorFromRow(); }
        private void RemoveSelectedRow() { if (_gridEntries?.SelectedItem is not PaletteEntryRow row) return; if (row.IsLocked) return; var palId = row.PaletteSetId; _rows.Remove(row); EnforceNonOverlappingRanges(palId); SyncTextLinesFromRows(); RefreshRangeList(); SetFlashRangeFromSelection(); UpdateRangeHighlight(); DoLiveUpdate(); SyncRangeEditorFromRow(); }
        private void UpdateRangeUndoRedoButtons() { if (_btnRangeUndo == null || _btnRangeRedo == null || _rangeEditor == null) return; _btnRangeUndo.IsEnabled = _rangeEditor.CanUndo; _btnRangeRedo.IsEnabled = _rangeEditor.CanRedo; }
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null)
            {
                MessageBox.Show("No clothing item loaded.");
                return;
            }
            var dialog = new SaveFileDialog
            {
                Filter = "CustomClothingBase JSON (*.json)|*.json",
                FileName = $"{clothing.Id:X8}.json",
                Title = "Export CustomClothingBase Mod JSON"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                ExportClothingMod(dialog.FileName);
                MessageBox.Show(
                    "Exported and round-trip validated for CustomClothingBase.\n\n" + dialog.FileName,
                    "Clothing Mod Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Clothing Mod Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal void ExportClothingMod(string path)
        {
            var clothing = ClothingTableList.CurrentClothingItem
                ?? throw new InvalidOperationException("No clothing item is selected.");
            if (!TryGetClothingModEdits(out var palette, out var paletteTemplate, out var icon, out var overrides, out var error))
                throw new InvalidDataException(error);
            CustomTextureStore.ExportClothingTable(clothing, path, overrides, palette, paletteTemplate, icon);
        }

        private bool TryGetClothingModEdits(out CustomPaletteDefinition palette, out uint paletteTemplate, out uint icon,
            out CustomTextureDefinition overrides, out string error)
        {
            palette = null;
            paletteTemplate = 0;
            icon = 0;
            overrides = null;
            error = null;

            if (TryBuildDefinition(out var definition)) palette = definition;
            if (palette != null)
            {
                try { paletteTemplate = ParseUInt(txtExportPaletteTemplate?.Text ?? string.Empty); }
                catch { error = "PaletteTemplate must be a decimal or hexadecimal unsigned integer."; return false; }
                if (paletteTemplate == 0) { error = "PaletteTemplate 0 is reserved; choose the template number this mod should replace or add."; return false; }
                try { icon = ParseUInt(txtExportIcon?.Text ?? "0"); }
                catch { error = "Icon must be 0 or a decimal/hexadecimal 0x06 icon ID."; return false; }
            }

            var textureDefinition = new CustomTextureDefinition { Name = $"ClothingMod_{ClothingTableList.CurrentClothingItem?.Id:X8}" };
            foreach (var row in _textureRowsObs.Where(row => row.NewId != row.OldId))
                textureDefinition.Entries.Add(new CustomTextureEntry { PartIndex = row.PartIndex, OldId = row.OldId, NewId = row.NewId });
            if (textureDefinition.Entries.Count > 0) overrides = textureDefinition;
            return true;
        }

        private void SyncExportTargetFromCurrentClothing()
        {
            if (txtExportPaletteTemplate == null || txtExportIcon == null) return;
            var template = ClothingTableList.GetPreferredPaletteTemplate();
            txtExportPaletteTemplate.Text = template.ToString(CultureInfo.InvariantCulture);
            txtExportIcon.Text = $"0x{ClothingTableList.GetPaletteIcon(template):X8}";
        }
        private void EnforceNonOverlappingRanges(uint? paletteFilter = null) { var groups = _rows.Where(r => !paletteFilter.HasValue || r.PaletteSetId == paletteFilter.Value).GroupBy(r => r.PaletteSetId); foreach (var g in groups) { var used = new HashSet<uint>(); foreach (var row in g) { var text = (row.RangesText ?? string.Empty).Replace(',', ' '); var parsed = RangeParser.ParseRanges(text, true, out _).OrderBy(r => r.Offset).ToList(); var rebuilt = new List<(uint off, uint len)>(); foreach (var r in parsed) { if (r.Length == 0) continue; checked { var end = r.Offset + r.Length; for (uint i = r.Offset; i < end; i++) { if (!used.Add(i)) continue; if (rebuilt.Count == 0) { rebuilt.Add((i, 1)); } else { var last = rebuilt[^1]; if (last.off + last.len == i) rebuilt[^1] = (last.off, last.len + 1); else rebuilt.Add((i, 1)); } } } } var newText = string.Join(",", rebuilt.Select(t => $"{t.off}:{t.len}")); row.RangesText = newText; } } if (!_freezeLines) SyncTextLinesFromRows(); }
        private void HookLiveEvents() { if (txtPaletteId != null) txtPaletteId.TextChanged += (_, __) => DoLiveUpdate(); if (txtRanges != null) txtRanges.TextChanged += (_, __) => DoLiveUpdate(); if (sldShade != null) sldShade.ValueChanged += (_, __) => DoLiveUpdate(); }
        private void SyncRowsFromText() { }
        private void SyncTextLinesFromRows() { }
        private bool TryBuildDefinition(out CustomPaletteDefinition def) { def = null; try { var tmp = new CustomPaletteDefinition { Shade = (float)(double.TryParse(txtShade?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0, 1) : 0) }; foreach (var r in _rows) { if (string.IsNullOrWhiteSpace(r.RangesText)) continue; var entry = new CustomPaletteEntry { PaletteSetId = r.PaletteSetId, Ranges = RangeParser.ParseRanges((r.RangesText ?? string.Empty).Replace(',', ' ')) }; if (entry.Ranges.Count == 0) continue; tmp.Entries.Add(entry); } if (tmp.Entries.Count == 0) return false; tmp.Multi = tmp.Entries.Count > 1; def = tmp; return true; } catch { return false; } }
        private void EnsureDefinitionHasName(CustomPaletteDefinition def) { if (def == null) return; if (string.IsNullOrWhiteSpace(def.Name)) def.Name = $"Live_{DateTime.UtcNow:yyyyMMdd_HHmmss}"; }
        private void PersistImmediate(CustomPaletteDefinition def) { try { if (def != null) CustomPaletteStore.SaveDefinition(def); } catch { } }
        private void DoLiveUpdate() { if (TryBuildDefinition(out var d)) ResultDefinition = d; UpdateJsonView(); if (ResultDefinition != null) { ClothingStudioWorkflowService.Instance.SelectPaletteTemplate(0xFFFFFFFF, ResultDefinition); try { ClothingTableList.Instance?.ApplyLivePaletteDefinition(ResultDefinition); } catch { } } }

        // ===== Texture + Gallery Implementation =====
        private void AddTextureRowIfMissing(int partIndex, uint oldId, uint newId, List<uint> referenced, bool isUserAdded = false)
        {
            if ((oldId >> 24) != 0x05)
                return;

            if (!_textureRows.Any(row => row.PartIndex == partIndex && row.OldId == oldId))
                _textureRows.Add(new TextureRow { PartIndex = partIndex, OldId = oldId, NewId = newId, IsLocked = false, IsUserAdded = isUserAdded });
            if (!referenced.Contains(oldId)) referenced.Add(oldId);
            if ((newId >> 24) == 0x05 && !referenced.Contains(newId)) referenced.Add(newId);
        }

        private void AddFallbackTextureRowsFromModel(int partIndex, uint modelId, List<uint> referenced)
        {
            if (DatManager.PortalDat == null || modelId == 0 || (modelId >> 24) != 0x01)
                return;

            try
            {
                var gfxObj = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.GfxObj>(modelId);
                if (gfxObj?.Surfaces == null)
                    return;

                foreach (var surfaceId in gfxObj.Surfaces)
                {
                    uint textureId;
                    try { textureId = TextureCache.GetSurfaceTextureID(surfaceId); }
                    catch { textureId = surfaceId; }
                    AddTextureRowIfMissing(partIndex, textureId, textureId, referenced, isUserAdded: true);
                }
            }
            catch
            {
                // Some model IDs are placeholders or unavailable in partial DAT sets.
            }
        }
        private void InitTextureTabData()
        {
            _textureRows.Clear();
            _textureRowsObs.Clear();
            _modelRows.Clear();
            _modelRowsObs.Clear();
            _availableTextureIds.Clear();
            _availableModelIds.Clear();
            _texturePage = 0;
            _modelPage = 0;
            if (DatManager.PortalDat == null) return;

            try
            {
                var referenced = new List<uint>();
                var referencedModels = new List<uint>();
                if (ClothingTableList.CurrentClothingItem != null)
                {
                    foreach (var baseEffect in ClothingTableList.CurrentClothingItem.ClothingBaseEffects.Values)
                        foreach (var obj in baseEffect.CloObjectEffects)
                        {
                            var partIndex = (int)obj.Index;
                            var modelId = (uint)(_cloObjModelProp?.GetValue(obj) ?? obj.ModelId);
                            if (modelId != 0 && !_modelRows.Any(row => row.PartIndex == partIndex && row.OldId == modelId))
                                _modelRows.Add(new ModelRow { PartIndex = partIndex, OldId = modelId, NewId = modelId, IsLocked = false });
                            if (modelId != 0 && !referencedModels.Contains(modelId)) referencedModels.Add(modelId);

                            foreach (var tex in obj.CloTextureEffects)
                            {
                                var oldId = (uint)(_cloTexOldProp?.GetValue(tex) ?? tex.OldTexture);
                                var newId = (uint)(_cloTexNewProp?.GetValue(tex) ?? tex.NewTexture);
                                AddTextureRowIfMissing(partIndex, oldId, newId, referenced);
                            }
                            AddFallbackTextureRowsFromModel(partIndex, modelId, referenced);
                        }
                }

                var allSurfaceTextures = DatIdIndex.SurfaceTextureIds();
                _availableTextureIds = referenced
                    .Concat(allSurfaceTextures.Where(id => !referenced.Contains(id)))
                    .ToList();

                var allModels = DatIdIndex.ModelAndSetupIds();
                _availableModelIds = referencedModels
                    .Concat(allModels.Where(id => !referencedModels.Contains(id)))
                    .ToList();
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.AddStatusText($"Texture browser initialization failed: {ex.Message}");
            }

            foreach (var row in _textureRows.OrderBy(row => row.PartIndex).ThenBy(row => row.OldId))
                _textureRowsObs.Add(row);
            foreach (var row in _modelRows.OrderBy(row => row.PartIndex).ThenBy(row => row.OldId))
                _modelRowsObs.Add(row);
            if (gridTextures != null)
            {
                gridTextures.ItemsSource = _textureRowsObs;
                if (_textureRowsObs.Count > 0)
                {
                    gridTextures.SelectedIndex = 0;
                    UpdateTexturePreviews(gridTextures.SelectedItem as TextureRow);
                }
            }
            if (gridModels != null)
            {
                gridModels.ItemsSource = _modelRowsObs;
                if (_modelRowsObs.Count > 0)
                    gridModels.SelectedIndex = 0;
            }
            HookTextureRowEvents();
            HookModelRowEvents();
            RefreshTextureGallery();
            RefreshModelGallery();
        }
        private void HookTextureRowEvents() { foreach (var r in _textureRowsObs) r.PropertyChanged -= TextureRow_PropertyChanged; foreach (var r in _textureRowsObs) r.PropertyChanged += TextureRow_PropertyChanged; _textureRowsObs.CollectionChanged -= TextureRowsObs_CollectionChanged; _textureRowsObs.CollectionChanged += TextureRowsObs_CollectionChanged; }
        private void TextureRowsObs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) { if (e.NewItems != null) foreach (TextureRow r in e.NewItems) r.PropertyChanged += TextureRow_PropertyChanged; if (e.OldItems != null) foreach (TextureRow r in e.OldItems) r.PropertyChanged -= TextureRow_PropertyChanged; }
        private void TextureRow_PropertyChanged(object sender, PropertyChangedEventArgs e) { if (_suppressTextureEvents) return; if (e.PropertyName == nameof(TextureRow.NewId) && sender is TextureRow tr) { if (gridTextures?.SelectedItem == tr) UpdateTexturePreviews(tr); if (!tr.IsLocked) { ScheduleTextureApply(); SaveLocalTextureOverrides(); } } }
        private void HookModelRowEvents() { foreach (var r in _modelRowsObs) r.PropertyChanged -= ModelRow_PropertyChanged; foreach (var r in _modelRowsObs) r.PropertyChanged += ModelRow_PropertyChanged; _modelRowsObs.CollectionChanged -= ModelRowsObs_CollectionChanged; _modelRowsObs.CollectionChanged += ModelRowsObs_CollectionChanged; }
        private void ModelRowsObs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) { if (e.NewItems != null) foreach (ModelRow r in e.NewItems) r.PropertyChanged += ModelRow_PropertyChanged; if (e.OldItems != null) foreach (ModelRow r in e.OldItems) r.PropertyChanged -= ModelRow_PropertyChanged; }
        private void ModelRow_PropertyChanged(object sender, PropertyChangedEventArgs e) { if (_suppressModelEvents) return; if (e.PropertyName == nameof(ModelRow.NewId) && sender is ModelRow row && !row.IsLocked) ScheduleModelApply(); }
        private void GridModels_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { Dispatcher.BeginInvoke(new Action(ScheduleModelApply), DispatcherPriority.Background); }
        private void MutateModelRows(Action mutation)
        {
            _suppressModelEvents = true;
            try { mutation(); }
            finally { _suppressModelEvents = false; }
            gridModels?.Items.Refresh();
        }
        private ListBoxItem BuildModelGalleryItem(uint id)
        {
            var label = (id >> 24) == 0x02 ? "Setup" : "GfxObj";
            return new ListBoxItem
            {
                Content = new TextBlock { Text = $"0x{id:X8}  {label}", Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center },
                Tag = id,
                ToolTip = $"Apply {label} 0x{id:X8} to selected model rows"
            };
        }
        private void RefreshModelGallery()
        {
            if (lstModelGallery == null) return;
            if (DatManager.PortalDat == null) { lstModelGallery.Items.Clear(); return; }

            var page = PagedIdList.Build(_availableModelIds, txtModelFilter?.Text, _modelPage, ModelPageSize);
            _modelPage = page.Page;

            lstModelGallery.BeginInit();
            lstModelGallery.Items.Clear();
            foreach (var id in page.Items) lstModelGallery.Items.Add(BuildModelGalleryItem(id));
            lstModelGallery.EndInit();

            if (lblModelGalleryStatus != null)
                lblModelGalleryStatus.Text = page.Total == 0 ? "No matching models" : page.StatusText;
            if (btnModelPreviousPage != null) btnModelPreviousPage.IsEnabled = page.HasPrevious;
            if (btnModelNextPage != null) btnModelNextPage.IsEnabled = page.HasNext;
        }
        private void ModelFilter_TextChanged(object sender, TextChangedEventArgs e) { _modelPage = 0; RefreshModelGallery(); }
        private void ModelGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstModelGallery?.SelectedItem is not ListBoxItem item) return;
            var newId = (uint)item.Tag;

            var targets = gridModels?.SelectedItems?.Cast<ModelRow>().ToList();
            if (targets == null || targets.Count == 0)
            {
                QueueModelCandidatePreview(newId);
                return;
            }

            CancelQueuedModelCandidatePreview();
            MutateModelRows(() => { foreach (var target in targets) if (!target.IsLocked) target.NewId = newId; });
            ScheduleModelApply();
            FocusSelectedModelPart();
        }
        private void QueueModelCandidatePreview(uint id)
        {
            _pendingModelPreviewId = id;
            _modelPreviewTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _modelPreviewTimer.Tick -= ModelPreviewTimer_Tick;
            _modelPreviewTimer.Tick += ModelPreviewTimer_Tick;
            _modelPreviewTimer.Stop();
            _modelPreviewTimer.Start();
        }

        private void CancelQueuedModelCandidatePreview()
        {
            _pendingModelPreviewId = null;
            _modelPreviewTimer?.Stop();
        }

        private void ModelPreviewTimer_Tick(object sender, EventArgs e)
        {
            _modelPreviewTimer.Stop();
            if (_pendingModelPreviewId is not uint id)
                return;

            _pendingModelPreviewId = null;
            PreviewModelCandidateNow(id);
        }

        private void PreviewModelCandidateNow(uint id)
        {
            try
            {
                if (ModelViewer.Instance == null)
                {
                    MainWindow.Instance?.AddStatusText("Model preview is not ready yet.");
                    return;
                }

                GameView.ViewMode = ACViewer.Enum.ViewMode.Model;
                ModelViewer.Instance.LoadModel(id, resetCamera: false);
                MainWindow.Instance?.AddStatusText($"Previewing model/world object 0x{id:X8}.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.AddStatusText($"Failed to preview model/world object 0x{id:X8}: {ex.Message}");
            }
        }
        private void FocusSelectedModelPart()
        {
            if (chkModelPartFocus?.IsChecked != true)
            {
                ModelViewer.PartIdx = -1;
                return;
            }

            if (gridModels?.SelectedItem is not ModelRow row)
            {
                ModelViewer.PartIdx = -1;
                return;
            }

            ModelViewer.PartIdx = Math.Max(-1, row.PartIndex);
            MainWindow.Instance?.AddStatusText($"Editing clothing part index {row.PartIndex}; 3D preview is showing that part only.");
        }

        private uint GetSelectedOrDefaultModelId()
        {
            if (lstModelGallery?.SelectedItem is ListBoxItem item && item.Tag is uint selectedId)
                return selectedId;
            if (gridModels?.SelectedItem is ModelRow selectedRow && selectedRow.NewId != 0)
                return selectedRow.NewId;
            return _availableModelIds.FirstOrDefault(id => (id >> 24) == 0x01 || (id >> 24) == 0x02);
        }

        private static bool TryParseModelEditorUInt(string text, out uint value) => HexId.TryParse(text, out value);

        private void AddModelPartRow()
        {
            var defaultPart = gridModels?.SelectedItem is ModelRow selected ? selected.PartIndex : 0;
            var partText = Microsoft.VisualBasic.Interaction.InputBox("Clothing/setup part index to add or edit:", "Add Part Index", defaultPart.ToString(CultureInfo.InvariantCulture));
            if (!int.TryParse(partText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var partIndex) || partIndex < 0)
            {
                MainWindow.Instance?.AddStatusText("Add part canceled: invalid part index.");
                return;
            }

            var defaultModel = GetSelectedOrDefaultModelId();
            var modelText = Microsoft.VisualBasic.Interaction.InputBox("Model / world object id for this part:", "Add Part Model", $"0x{defaultModel:X8}");
            if (!TryParseModelEditorUInt(modelText, out var modelId) || modelId == 0)
            {
                MainWindow.Instance?.AddStatusText("Add part canceled: invalid model id.");
                return;
            }

            ModelRow row = null;
            MutateModelRows(() =>
            {
                row = _modelRowsObs.FirstOrDefault(r => r.PartIndex == partIndex);
                if (row == null)
                {
                    row = new ModelRow { PartIndex = partIndex, OldId = modelId, NewId = modelId, IsLocked = false, IsUserAdded = true };
                    _modelRowsObs.Add(row);
                }
                else if (!row.IsLocked)
                {
                    row.NewId = modelId;
                }
            });

            gridModels?.Items.Refresh();
            if (row != null && gridModels != null)
            {
                gridModels.SelectedItem = row;
                gridModels.ScrollIntoView(row);
            }
            ScheduleModelApply();
            FocusSelectedModelPart();
            MainWindow.Instance?.AddStatusText($"Added/updated clothing part index {partIndex} -> 0x{modelId:X8}.");
        }

        private void RemoveSelectedModelPartRows()
        {
            var selected = gridModels?.SelectedItems?.Cast<ModelRow>().Where(row => !row.IsLocked).ToList();
            if (selected == null || selected.Count == 0)
                return;

            var partIndexes = selected.Select(row => row.PartIndex).Distinct().ToList();
            var removedEffects = RemoveModelPartsFromWorkingClothing(partIndexes);
            var removedRows = 0;
            MutateModelRows(() =>
            {
                foreach (var row in _modelRowsObs.Where(row => partIndexes.Contains(row.PartIndex)).ToList())
                {
                    _modelRowsObs.Remove(row);
                    removedRows++;
                }
            });
            RemoveTextureRowsForDeletedParts(partIndexes);

            ModelViewer.PartIdx = -1;
            gridModels?.Items.Refresh();
            ScheduleModelApply();
            MainWindow.Instance?.AddStatusText($"Deleted {partIndexes.Count:N0} clothing part index(es), removed {removedEffects:N0} model effect row(s), and cleared {removedRows:N0} editor row(s).");
        }

        private int RemoveModelPartsFromWorkingClothing(IReadOnlyCollection<int> partIndexes)
        {
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null || partIndexes == null || partIndexes.Count == 0)
                return 0;

            var removed = 0;
            foreach (var baseEffect in clothing.ClothingBaseEffects.Values)
            {
                for (var i = baseEffect.CloObjectEffects.Count - 1; i >= 0; i--)
                {
                    var obj = baseEffect.CloObjectEffects[i];
                    if (!partIndexes.Contains((int)obj.Index))
                        continue;

                    baseEffect.CloObjectEffects.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        private void RemoveTextureRowsForDeletedParts(IReadOnlyCollection<int> partIndexes)
        {
            if (partIndexes == null || partIndexes.Count == 0)
                return;

            MutateTextureRows(() =>
            {
                foreach (var row in _textureRowsObs.Where(row => partIndexes.Contains(row.PartIndex)).ToList())
                    _textureRowsObs.Remove(row);
            });
        }

        private void ApplyModelOverridesToClothing()
        {
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null) return;
            var rows = _modelRowsObs.GroupBy(r => r.PartIndex).Select(g => g.Last()).ToList();
            foreach (var baseEffect in clothing.ClothingBaseEffects.Values)
            {
                foreach (var row in rows)
                {
                    var obj = baseEffect.CloObjectEffects.FirstOrDefault(o => (int)o.Index == row.PartIndex);
                    if (obj == null)
                    {
                        if (!row.IsUserAdded)
                            continue;

                        obj = new CloObjectEffect();
                        try { _cloObjIndexProp?.SetValue(obj, (uint)row.PartIndex); } catch { }
                        baseEffect.CloObjectEffects.Add(obj);
                    }

                    try { _cloObjModelProp?.SetValue(obj, row.NewId); } catch { }
                }
            }
        }
        private void ScheduleModelApply()
        {
            _modelApplyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _modelApplyTimer.Tick -= ModelApplyTimer_Tick;
            _modelApplyTimer.Tick += ModelApplyTimer_Tick;
            _modelApplyTimer.Stop();
            _modelApplyTimer.Start();
        }

        private void ModelApplyTimer_Tick(object sender, EventArgs e)
        {
            _modelApplyTimer.Stop();
            ApplyModelOverridesToClothing();
            ClothingStudioWorkflowService.Instance.MarkModelOverrides(_modelRowsObs.Any(r => r.NewId != r.OldId));
            ClothingTableList.Instance?.LoadModelWithClothingBase(resetCamera: false);
            FocusSelectedModelPart();
            MainWindow.Instance?.RealtimeJsonSync();
            UpdateJsonView();
        }
        private void MutateTextureRows(Action mutation)
        {
            _suppressTextureEvents = true;
            try
            {
                mutation();
            }
            finally
            {
                _suppressTextureEvents = false;
            }
        }
        private void UpdateTexturePreviews(TextureRow row)
        {
            if (row == null)
            {
                if (imgTexPreviewOld != null) imgTexPreviewOld.Source = null;
                if (imgTexPreviewNew != null) imgTexPreviewNew.Source = null;
                UpdateTexturePaletteSuggestions(0);
                return;
            }
            if (imgTexPreviewOld != null) imgTexPreviewOld.Source = BuildTexturePreview(row.OldId);
            if (imgTexPreviewNew != null) imgTexPreviewNew.Source = BuildTexturePreview(row.NewId);
            UpdateTexturePaletteSuggestions(row.NewId);
        }

        private void UpdateTexturePaletteSuggestions(uint textureId)
        {
            if (lblTexturePaletteInfo == null || lstTexturePaletteSuggestions == null) return;
            _texturePaletteAnalysis = textureId == 0 ? null : TexturePaletteSuggestionService.Analyze(textureId, ClothingTableList.CurrentClothingItem);
            lblTexturePaletteInfo.Text = _texturePaletteAnalysis?.Summary ?? "Select an indexed texture to inspect its palette.";
            lstTexturePaletteSuggestions.ItemsSource = _texturePaletteAnalysis?.Suggestions;
            var preferred = _texturePaletteAnalysis?.Suggestions?.FirstOrDefault(suggestion => (suggestion.PaletteId >> 24) == 0x0F)
                            ?? _texturePaletteAnalysis?.Suggestions?.FirstOrDefault();
            lstTexturePaletteSuggestions.SelectedItem = preferred;
            if (btnLoadTexturePalette != null) btnLoadTexturePalette.IsEnabled = preferred != null;
            if (preferred != null && chkAutoLoadTexturePalette?.IsChecked == true)
                ApplyTexturePaletteSuggestion(preferred, false);
        }

        private void BtnLoadTexturePalette_Click(object sender, RoutedEventArgs e)
        {
            if (lstTexturePaletteSuggestions?.SelectedItem is not TexturePaletteSuggestion suggestion) return;
            ApplyTexturePaletteSuggestion(suggestion, true);
        }

        private void ApplyTexturePaletteSuggestion(TexturePaletteSuggestion suggestion, bool switchToPaletteTab)
        {
            if (suggestion == null || _texturePaletteAnalysis == null) return;
            var key = $"{_texturePaletteAnalysis.SurfaceTextureId:X8}:{suggestion.PaletteId:X8}:{suggestion.Shade:0.###}:{string.Join(",", suggestion.Ranges.Select(range => $"{range.Offset}:{range.Length}"))}";
            if (!switchToPaletteTab && string.Equals(_lastAutoTexturePaletteKey, key, StringComparison.Ordinal)) return;
            _lastAutoTexturePaletteKey = key;

            var definition = suggestion.ToDefinition(_texturePaletteAnalysis.SurfaceTextureId);
            ApplyLoadedPreset(definition);
            ResultDefinition = definition;
            var template = ClothingTableList.GetPreferredPaletteTemplate();
            if (template == 0)
                template = ClothingTableList.CurrentClothingItem?.ClothingSubPalEffects.Keys.OrderBy(id => id).FirstOrDefault() ?? 1;
            if (template == 0) template = 1;
            if (txtExportPaletteTemplate != null) txtExportPaletteTemplate.Text = template.ToString(CultureInfo.InvariantCulture);
            DoLiveUpdate();
            if (switchToPaletteTab && _rootTabs != null) _rootTabs.SelectedIndex = 0;
            MainWindow.Instance?.AddStatusText($"Loaded palette 0x{suggestion.PaletteId:X8} with texture-derived ranges for 0x{_texturePaletteAnalysis.SurfaceTextureId:X8}.");
        }

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
                foreach (var c in colors) { data[p++] = c.B; data[p++] = c.G; data[p++] = c.R; data[p++] = c.A == 0 ? (byte)255 : c.A; }
                wb.WritePixels(new Int32Rect(0, 0, width, height), data, width * 4, 0);
                wb.Freeze();
                return wb;
            }
            catch { return null; }
        }
        private ImageSource BuildTexturePreview(uint id)
        {
            try
            {
                if (DatManager.PortalDat == null) return null;
                uint type = id >> 24; ACE.DatLoader.FileTypes.Texture texFile = null; // correct namespace
                if (type == 0x06)
                    texFile = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(id);
                else if (type == 0x05)
                {
                    var st = DatManager.PortalDat.ReadFromDat<SurfaceTexture>(id);
                    if (st?.Textures != null && st.Textures.Count > 0)
                        texFile = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(st.Textures[0]);
                }
                else if (type == 0x08)
                {
                    var surf = DatManager.PortalDat.ReadFromDat<Surface>(id);
                    if (surf != null)
                    {
                        if (surf.ColorValue != 0)
                        {
                            var wbColor = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
                            var dataColor = new byte[32 * 32 * 4];
                            byte a = (byte)(surf.ColorValue >> 24);
                            byte r = (byte)(surf.ColorValue >> 16);
                            byte g = (byte)(surf.ColorValue >> 8);
                            byte b = (byte)surf.ColorValue;
                            if (a == 0) a = 255;
                            for (int i = 0; i < 32 * 32; i++)
                            {
                                int idx = i * 4;
                                dataColor[idx] = b; dataColor[idx + 1] = g; dataColor[idx + 2] = r; dataColor[idx + 3] = a;
                            }
                            wbColor.WritePixels(new Int32Rect(0, 0, 32, 32), dataColor, 32 * 4, 0);
                            wbColor.Freeze();
                            return wbColor;
                        }
                        if (surf.OrigTextureId != 0)
                        {
                            var st2 = DatManager.PortalDat.ReadFromDat<SurfaceTexture>(surf.OrigTextureId);
                            if (st2?.Textures != null && st2.Textures.Count > 0)
                                texFile = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(st2.Textures[0]);
                        }
                    }
                }
                else if (type == 0x04)
                {
                    var palImg = BuildPaletteBitmap(id, 64, 16);
                    if (palImg != null) return palImg;
                }

                if (texFile == null && type != 0x06) return null;

                bool indexed = false;
                try { indexed = texFile != null && (texFile.Format == SurfacePixelFormat.PFID_INDEX16 || texFile.Format == SurfacePixelFormat.PFID_P8); }
                catch { }

                if (indexed || type == 0x05 || type == 0x08)
                {
                    Texture2D decoded = null;
                    try { decoded = TextureCache.Get(id, null, BuildLivePaletteChanges(), useCache: false); }
                    catch { }
                    if (decoded != null)
                    {
                        var cols = new Microsoft.Xna.Framework.Color[decoded.Width * decoded.Height];
                        try { decoded.GetData(cols); } catch { cols = null; }
                        if (cols != null)
                            return BuildWriteableBitmapFromColors(cols, decoded.Width, decoded.Height);
                    }
                }

                using var bmp = texFile?.GetBitmap();
                if (bmp == null) return null;
                int w = bmp.Width, h = bmp.Height;
                const int MaxDim = 256;
                double scale = 1.0;
                if (w > MaxDim || h > MaxDim)
                {
                    scale = Math.Min((double)MaxDim / w, (double)MaxDim / h);
                    w = (int)(w * scale);
                    h = (int)(h * scale);
                }
                System.Drawing.Bitmap working = bmp;
                if (scale != 1.0)
                {
                    working = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = System.Drawing.Graphics.FromImage(working);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.DrawImage(bmp, new System.Drawing.Rectangle(0, 0, w, h));
                }
                var wbOut = new WriteableBitmap(working.Width, working.Height, 96, 96, PixelFormats.Bgra32, null);
                var data = new byte[working.Width * working.Height * 4];
                int pOut = 0;
                for (int y = 0; y < working.Height; y++)
                    for (int x = 0; x < working.Width; x++)
                    {
                        var c = working.GetPixel(x, y);
                        data[pOut++] = c.B; data[pOut++] = c.G; data[pOut++] = c.R; data[pOut++] = c.A == 0 ? (byte)255 : c.A;
                    }
                wbOut.WritePixels(new Int32Rect(0, 0, working.Width, working.Height), data, working.Width * 4, 0);
                wbOut.Freeze();
                if (working != bmp) working.Dispose();
                return wbOut;
            }
            catch { return null; }
        }
        private ImageSource GetOrQueueThumbnail(uint id, System.Windows.Controls.Image target)
        {
            if (_thumbCache.TryGetValue(id, out var cached) && cached != null) return cached;
            _thumbPlaceholder ??= CreatePlaceholder();
            if (target != null)
            {
                RoutedEventHandler loaded = null;
                loaded = (_, __) =>
                {
                    target.Loaded -= loaded;
                    QueueThumbnail(id, target);
                };
                target.Loaded += loaded;
            }
            return _thumbPlaceholder;
        }

        private void QueueThumbnail(uint id, System.Windows.Controls.Image target)
        {
            if (_thumbCache.TryGetValue(id, out var cached) && cached != null) { target.Source = cached; return; }
            lock (_thumbSync)
            {
                if (!_thumbInFlight.Add(id)) return;
            }
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var source = BuildTexturePreview(id);
                    if (source != null) { _thumbCache[id] = source; target.Source = source; }
                }
                finally { lock (_thumbSync) _thumbInFlight.Remove(id); }
            }), DispatcherPriority.Background);
        }
        private ImageSource CreatePlaceholder() { int w = 64, h = 64; var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null); var data = new byte[w * h * 4]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { bool on = ((x / 8) + (y / 8)) % 2 == 0; byte g = (byte)(on ? 90 : 120); int i = (y * w + x) * 4; data[i] = g; data[i + 1] = g; data[i + 2] = g; data[i + 3] = 255; } wb.WritePixels(new Int32Rect(0, 0, w, h), data, w * 4, 0); wb.Freeze(); return wb; }
        private ListBoxItem BuildTextureGalleryItem(uint id)
        {
            var row = new DockPanel { Margin = new Thickness(3) };
            var image = new System.Windows.Controls.Image { Width = 58, Height = 58, Stretch = Stretch.Uniform, SnapsToDevicePixels = true, Margin = new Thickness(0, 0, 8, 0) };
            image.Source = GetOrQueueThumbnail(id, image);
            DockPanel.SetDock(image, Dock.Left); row.Children.Add(image);
            row.Children.Add(new TextBlock { Text = $"0x{id:X8}\nSurfaceTexture", VerticalAlignment = VerticalAlignment.Center });
            return new ListBoxItem { Content = row, Tag = id, ToolTip = $"Apply SurfaceTexture 0x{id:X8}" };
        }

        private void RefreshTextureGallery()
        {
            if (lstTexGallery == null) return;
            if (DatManager.PortalDat == null) { lstTexGallery.Items.Clear(); return; }

            var page = PagedIdList.Build(_availableTextureIds, txtTexFilter?.Text, _texturePage, TexturePageSize);
            _texturePage = page.Page;

            lstTexGallery.BeginInit();
            lstTexGallery.Items.Clear();
            foreach (var id in page.Items) lstTexGallery.Items.Add(BuildTextureGalleryItem(id));
            lstTexGallery.EndInit();

            if (lblTexGalleryStatus != null)
                lblTexGalleryStatus.Text = page.Total == 0 ? "No matching textures" : page.StatusText;
            if (btnTexPreviousPage != null) btnTexPreviousPage.IsEnabled = page.HasPrevious;
            if (btnTexNextPage != null) btnTexNextPage.IsEnabled = page.HasNext;
        }

        private void TexFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _texturePage = 0;
            RefreshTextureGallery();
        }

        private void TexGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTexGallery?.SelectedItem is not ListBoxItem item) return;
            var targets = gridTextures?.SelectedItems?.Cast<TextureRow>().ToList();
            if (targets == null || targets.Count == 0) return;
            var newId = (uint)item.Tag;
            PushTextureSnapshot();
            MutateTextureRows(() => { foreach (var target in targets) if (!target.IsLocked) target.NewId = newId; });
            gridTextures?.Items.Refresh();
            ScheduleTextureApply();
            SaveLocalTextureOverrides();
        }
        private void ClearInjectedFallbackTextureOverrides(IEnumerable<TextureRow> rows)
        {
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null || rows == null)
                return;

            var injectedTargets = rows
                .Where(row => row.IsUserAdded && !row.IsLocked && row.NewId != row.OldId)
                .Select(row => (row.PartIndex, row.OldId, row.NewId))
                .ToHashSet();
            if (injectedTargets.Count == 0)
                return;

            foreach (var baseEffect in clothing.ClothingBaseEffects.Values)
            {
                foreach (var obj in baseEffect.CloObjectEffects)
                {
                    var partIndex = (int)obj.Index;
                    for (var i = obj.CloTextureEffects.Count - 1; i >= 0; i--)
                    {
                        var tex = obj.CloTextureEffects[i];
                        var oldId = (uint)(_cloTexOldProp?.GetValue(tex) ?? tex.OldTexture);
                        var newId = (uint)(_cloTexNewProp?.GetValue(tex) ?? tex.NewTexture);
                        if (injectedTargets.Contains((partIndex, oldId, newId)))
                            obj.CloTextureEffects.RemoveAt(i);
                    }
                }
            }
        }
        private void RestoreExplicitTextureOverridesToBase(IEnumerable<TextureRow> rows)
        {
            var clothing = ClothingTableList.CurrentClothingItem;
            if (clothing == null || rows == null)
                return;

            var targets = rows
                .Where(row => !row.IsUserAdded && !row.IsLocked && row.NewId != row.OldId)
                .Select(row => (row.PartIndex, row.OldId))
                .ToHashSet();
            if (targets.Count == 0)
                return;

            foreach (var baseEffect in clothing.ClothingBaseEffects.Values)
            {
                foreach (var obj in baseEffect.CloObjectEffects)
                {
                    var partIndex = (int)obj.Index;
                    foreach (var tex in obj.CloTextureEffects)
                    {
                        var oldId = (uint)(_cloTexOldProp?.GetValue(tex) ?? tex.OldTexture);
                        if (targets.Contains((partIndex, oldId)))
                        {
                            try { _cloTexNewProp?.SetValue(tex, oldId); } catch { }
                        }
                    }
                }
            }
        }
        private void ApplyTextureOverridesToClothing()
        {
            if (ClothingTableList.CurrentClothingItem == null) return;
            var map = _textureRowsObs
                .Where(row => row.NewId != row.OldId)
                .GroupBy(row => (row.PartIndex, row.OldId))
                .ToDictionary(g => g.Key, g => g.Last().NewId);
            if (map.Count == 0) return;

            foreach (var kvp in ClothingTableList.CurrentClothingItem.ClothingBaseEffects)
            {
                foreach (var obj in kvp.Value.CloObjectEffects)
                {
                    var partIndex = (int)obj.Index;
                    var matchedOldIds = new HashSet<uint>();
                    foreach (var tex in obj.CloTextureEffects)
                    {
                        uint oldT = (uint)(_cloTexOldProp?.GetValue(tex) ?? tex.OldTexture);
                        uint currentNew = (uint)(_cloTexNewProp?.GetValue(tex) ?? tex.NewTexture);
                        if (map.TryGetValue((partIndex, oldT), out var desiredNew))
                        {
                            matchedOldIds.Add(oldT);
                            if (desiredNew != currentNew)
                            {
                                try { _cloTexNewProp?.SetValue(tex, desiredNew); } catch { }
                            }
                        }
                    }

                    foreach (var pending in map.Where(pair => pair.Key.PartIndex == partIndex && !matchedOldIds.Contains(pair.Key.OldId)))
                    {
                        var tex = new CloTextureEffect();
                        try { _cloTexOldProp?.SetValue(tex, pending.Key.OldId); } catch { }
                        try { _cloTexNewProp?.SetValue(tex, pending.Value); } catch { }
                        obj.CloTextureEffects.Add(tex);
                    }
                }
            }
        }
        private void ScheduleTextureApply() { ApplyTextureOverridesToClothing(); ClothingStudioWorkflowService.Instance.MarkTextureOverrides(_textureRowsObs.Any(r => r.NewId != r.OldId)); ClothingTableList.Instance?.LoadModelWithClothingBase(resetCamera: false); MainWindow.Instance?.RealtimeJsonSync(); if (gridTextures?.SelectedItem is TextureRow tr) UpdateTexturePreviews(tr); UpdateJsonView(); }
        private void SaveLocalTextureOverrides() { try { var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) return; TextureOverrideLocalStore.Save(clothing.Id, _textureRowsObs.Select(r => new TextureOverrideLocalStore.Row { PartIndex = r.PartIndex, OldId = r.OldId, NewId = r.NewId, IsLocked = r.IsLocked })); } catch { } }
        private void LoadLocalTextureOverrides() { try { var clothing = ClothingTableList.CurrentClothingItem; if (clothing == null) return; var rows = TextureOverrideLocalStore.Load(clothing.Id); if (rows.Count == 0) return; MutateTextureRows(() => { foreach (var row in rows) { var match = _textureRowsObs.FirstOrDefault(r => r.PartIndex == row.PartIndex && r.OldId == row.OldId); if (match != null) { match.NewId = row.NewId; match.IsLocked = row.IsLocked; } } }); ScheduleTextureApply(); } catch { } }
        private Stack<Dictionary<(int part, uint oldId), uint>> _textureUndo = new(); private Stack<Dictionary<(int part, uint oldId), uint>> _textureRedo = new(); private bool _suppressTextureUndo; private Dictionary<(int part, uint oldId), uint> SnapshotTextureMapping() => _textureRowsObs.GroupBy(r => (r.PartIndex, r.OldId)).ToDictionary(g => g.Key, g => g.Last().NewId); private void PushTextureSnapshot() { if (_suppressTextureUndo) return; _textureUndo.Push(SnapshotTextureMapping()); _textureRedo.Clear(); }
        private void ApplyTextureMapping(Dictionary<(int part, uint oldId), uint> map) { if (map == null) return; _suppressTextureUndo = true; MutateTextureRows(() => { foreach (var row in _textureRowsObs) if (map.TryGetValue((row.PartIndex, row.OldId), out var newId) && !row.IsLocked) row.NewId = newId; }); _suppressTextureUndo = false; ScheduleTextureApply(); SaveLocalTextureOverrides(); }
        private void BtnTexUndo_Click(object sender, RoutedEventArgs e) => BtnTexUndo(); private void BtnTexRedo_Click(object sender, RoutedEventArgs e) => BtnTexRedo(); private void BtnTexUndo() { if (_textureUndo.Count == 0) return; var current = SnapshotTextureMapping(); var prev = _textureUndo.Pop(); _textureRedo.Push(current); ApplyTextureMapping(prev); }
        private void BtnTexRedo() { if (_textureRedo.Count == 0) return; var current = SnapshotTextureMapping(); var next = _textureRedo.Pop(); _textureUndo.Push(current); ApplyTextureMapping(next); }
        private void BtnTexApply_Click(object sender, RoutedEventArgs e) => ScheduleTextureApply();
        private void BtnTexResetSelected_Click(object sender, RoutedEventArgs e)
        {
            var sel = gridTextures?.SelectedItems?.Cast<TextureRow>().ToList();
            if (sel == null || sel.Count == 0) return;
            PushTextureSnapshot();
            ClearInjectedFallbackTextureOverrides(sel);
            RestoreExplicitTextureOverridesToBase(sel);
            MutateTextureRows(() => { foreach (var r in sel) if (!r.IsLocked) r.NewId = r.OldId; });
            ScheduleTextureApply();
            SaveLocalTextureOverrides();
        }
        private void BtnTexResetAll_Click(object sender, RoutedEventArgs e)
        {
            PushTextureSnapshot();
            ClearInjectedFallbackTextureOverrides(_textureRowsObs.ToList());
            RestoreExplicitTextureOverridesToBase(_textureRowsObs.ToList());
            MutateTextureRows(() => { foreach (var r in _textureRowsObs) if (!r.IsLocked) r.NewId = r.OldId; });
            ScheduleTextureApply();
            SaveLocalTextureOverrides();
        }
        private void BtnTexLoadOverrides_Click(object sender, RoutedEventArgs e) { var dlg = new OpenFileDialog { Filter = "Texture Override or ClothingMod JSON (*.json)|*.json" }; if (dlg.ShowDialog() != true) return; try { var list = TextureOverrideLocalStore.Deserialize(File.ReadAllText(dlg.FileName)); PushTextureSnapshot(); MutateTextureRows(() => { foreach (var row in list) { var match = _textureRowsObs.FirstOrDefault(r => r.PartIndex == row.PartIndex && r.OldId == row.OldId); if (match != null && !match.IsLocked) { match.NewId = row.NewId; match.IsLocked = row.IsLocked; } } }); ScheduleTextureApply(); SaveLocalTextureOverrides(); } catch (Exception ex) { MessageBox.Show($"Failed to load overrides: {ex.Message}", "Texture Override Import", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void BtnTexSaveOverrides_Click(object sender, RoutedEventArgs e) { var dlg = new SaveFileDialog { Filter = "Texture Override (*.json)|*.json", FileName = "TextureOverrides.json" }; if (dlg.ShowDialog() != true) return; try { var list = _textureRowsObs.Select(r => new TextureOverrideLocalStore.Row { PartIndex = r.PartIndex, OldId = r.OldId, NewId = r.NewId, IsLocked = r.IsLocked }).ToList(); File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception ex) { MessageBox.Show($"Failed to save overrides: {ex.Message}"); } }
        private void GridTextures_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (gridTextures?.SelectedItem is TextureRow tr) UpdateTexturePreviews(tr); else UpdateTexturePreviews(null); }
        private void GridTextures_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { PushTextureSnapshot(); }

        private void UpdateJsonView()
        {
            if (_txtJson == null) return;
            try
            {
                var palOk = TryBuildDefinition(out var palDef) ? palDef : ResultDefinition;
                var overrides = _textureRowsObs.Where(r => r.NewId != r.OldId).Select(r => new { r.PartIndex, r.OldId, r.NewId }).ToList();
                var modelOverrides = _modelRowsObs.Where(r => r.NewId != r.OldId).Select(r => new { r.PartIndex, OldModelId = r.OldId, NewModelId = r.NewId }).ToList();
                var derpAceAttachments = _attachmentRows
                    .Where(r => r.EffectId != 0 || r.EmitterInfoId != 0 || !string.IsNullOrWhiteSpace(r.Note))
                    .Select(r => new
                    {
                        r.PartIndex,
                        Type = r.Kind,
                        EffectId = $"0x{r.EffectId:X8}",
                        EmitterInfoId = $"0x{r.EmitterInfoId:X8}",
                        r.EmitterId,
                        r.AttachTo,
                        Offset = new[] { r.OffsetX, r.OffsetY, r.OffsetZ },
                        Rotation = new[] { r.RotationX, r.RotationY, r.RotationZ },
                        r.Scale,
                        r.Note
                    }).ToList();
                var payload = new
                {
                    palette = palOk,
                    textures = overrides,
                    models = modelOverrides,
                    derpAce = new
                    {
                        schema = "DerpACE.ClothingStudio.Attachments.v1",
                        attachments = derpAceAttachments
                    },
                    updatedUtc = DateTime.UtcNow
                };
                _suppressJson = true;
                _txtJson.Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { }
            finally { _suppressJson = false; }
        }
        private void BtnAddPaletteIds_Click(object sender, RoutedEventArgs e)
        {
            var text = txtAddPaletteIds.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var added = false;
            try
            {
                foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!token.Contains('-'))
                    {
                        var id = ParseUInt(token);
                        if (!AvailablePaletteIDs.Contains(id)) { AvailablePaletteIDs.Add(id); added = true; }
                        continue;
                    }

                    var parts = token.Split('-');
                    if (parts.Length != 2) throw new FormatException($"Invalid palette range '{token}'.");
                    var first = ParseUInt(parts[0]);
                    var last = ParseUInt(parts[1]);
                    if (first > last) (first, last) = (last, first);
                    if ((ulong)last - first + 1 > 4096)
                        throw new FormatException("A palette range may contain at most 4096 IDs.");
                    for (var id = first; ; id++)
                    {
                        if (!AvailablePaletteIDs.Contains(id)) { AvailablePaletteIDs.Add(id); added = true; }
                        if (id == last) break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invalid palette IDs", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!added) return;
            AvailablePaletteIDs = AvailablePaletteIDs.OrderBy(i => i).ToList();
            PopulateList();
            _ = PrewarmPaletteCacheAndRefreshAsync();
        }

        private static uint ParseUInt(string s) => HexId.Parse(s);
        private void SetFlashRangeFromSelection() { _flashRange = null; }

        private async Task PrewarmPaletteCacheAndRefreshAsync()
        {
            if (DatManager.PortalDat == null || AvailablePaletteIDs == null || AvailablePaletteIDs.Count == 0)
                return;
            if (_paletteCacheWarmed) return;
            lock (_paletteWarmLock)
            {
                if (_paletteCacheWarmed) return;
                _paletteCacheWarmed = true;
            }
            try
            {
                await Task.Run(() =>
                {
                    foreach (var id in AvailablePaletteIDs)
                    {
                        // Warm both small list thumbnail and large preview sizes
                        _ = BuildPaletteBitmap(id, 220, 14);
                        _ = BuildPaletteBitmap(id, PreviewWidth, PreviewHeight);
                    }
                }).ConfigureAwait(false);
            }
            catch { }
            try
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (lstPalettes == null) return;
                    var sel = lstPalettes.SelectedItem;
                    lstPalettes.BeginInit();
                    lstPalettes.Items.Clear();
                    foreach (var id in AvailablePaletteIDs)
                        lstPalettes.Items.Add(BuildPaletteListItem(id));
                    lstPalettes.EndInit();
                    if (sel != null) lstPalettes.SelectedItem = sel;
                    UpdateBigPreviewImage();
                }), DispatcherPriority.Background);
            }
            catch { }
        }

    }
}
#pragma warning restore 0169, 0649, 0414
