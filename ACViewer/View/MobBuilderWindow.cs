using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACViewer.CustomPalettes;
using ACViewer.Enum;
using ACViewer.Model;
using ACViewer.MobBuilder;
using ACViewer.Render;
using ACViewer.Services;
using ACViewer.Utilities;

namespace ACViewer.View
{
    public sealed class MobBuilderWindow : Window
    {
        private const int BrowserPageSize = 100;
        private readonly TextBox _name = Text("New Mob");
        private readonly TextBox _wcid = Text("0x00000000");
        private readonly TextBox _cloneFrom = Text("0x00000000");
        private readonly ComboBox _weenieType = Combo("Creature", "Creature", "Vendor", "Pet", "CombatPet", "NPC", "Container", "Portal");
        private readonly ComboBox _creatureType = Combo("Unknown", "Unknown", "Human", "Drudge", "Banderling", "Tumerok", "Lugian", "Undead", "Olthoi", "Virindi", "Shadow", "Elemental", "Custom");
        private readonly TextBox _setup = Text("0x02000001");
        private readonly TextBox _motion = Text("0x09000001");
        private readonly TextBox _sound = Text("0x20000000");
        private readonly TextBox _palette = Text("0x0400007E");
        private readonly TextBox _shade = Text("0.0");
        private readonly TextBox _scale = Text("1.0");
        private readonly TextBox _level = Text("1");
        private readonly TextBox _health = Text("100");
        private readonly TextBox _stamina = Text("100");
        private readonly TextBox _mana = Text("0");
        private readonly TextBox _armor = Text("0");
        private readonly TextBox _damage = Text("0");
        private readonly TextBox _attack = Text("100");
        private readonly TextBox _defense = Text("100");
        private readonly TextBox _magicDefense = Text("100");
        private readonly ComboBox _aiProfile = Combo("Melee", "Melee", "Missile", "Caster", "Hybrid", "Passive", "Vendor", "Custom");
        private readonly ComboBox _tolerance = Combo("Aggressive", "Aggressive", "Defensive", "Passive", "Neutral", "Friendly");
        private readonly TextBox _faction = Text(string.Empty);
        private readonly DataGrid _textureMaps = DataGridControl();
        private readonly DataGrid _animParts = DataGridControl();
        private readonly DataGrid _skills = DataGridControl();
        private readonly DataGrid _spells = DataGridControl();
        private readonly DataGrid _loot = DataGridControl();
        private readonly DataGrid _spawns = DataGridControl();
        private readonly TextBox _notes = Text(string.Empty, acceptsReturn: true);

        private readonly List<MobBuilderTextureMap> _textureRows = new();
        private readonly List<MobBuilderAnimPart> _animRows = new();
        private readonly List<MobBuilderSkill> _skillRows = new();
        private readonly List<MobBuilderSpell> _spellRows = new();
        private readonly List<MobBuilderLootEntry> _lootRows = new();
        private readonly List<MobBuilderSpawnEntry> _spawnRows = new();
        private readonly bool _isDocked;
        private readonly Dictionary<uint, ImageSource> _texturePreviewCache = new();
        private ListBox _textureBrowser;
        private ListBox _modelBrowser;
        private TextBox _textureFilter;
        private TextBox _modelFilter;
        private TextBlock _textureStatus;
        private TextBlock _modelStatus;
        private TextBlock _paletteSummary;
        private ListBox _paletteSuggestions;
        private System.Windows.Controls.Image _oldTexturePreview;
        private System.Windows.Controls.Image _newTexturePreview;
        private int _textureLoadedCount;
        private int _modelLoadedCount;
        private IReadOnlyList<uint> _textureIds = Array.Empty<uint>();
        private IReadOnlyList<uint> _modelIds = Array.Empty<uint>();
        private IReadOnlyList<uint> _filteredTextureIds = Array.Empty<uint>();
        private IReadOnlyList<uint> _filteredModelIds = Array.Empty<uint>();

        public MobBuilderWindow(bool isDocked = false)
        {
            _isDocked = isDocked;
            Title = "DerpACE Mob Builder";
            Width = 1180;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#2D2D30");
            Foreground = Brush("#F1F1F1");
            Content = BuildUi();
            SeedRows();
            RefreshVisualTools();
        }

        public UIElement DetachContentForDock()
        {
            var content = Content as UIElement;
            Content = null;
            return content;
        }
        private UIElement BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(10) };
            var header = new TextBlock
            {
                Text = "Mob Builder",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(Button("Import SQL/JSON", Import_Click));
            footer.Children.Add(Button("Export SQL", Export_Click));
            footer.Children.Add(Button("Copy SQL", Copy_Click));
            footer.Children.Add(Button("Preview Model", Preview_Click));
            if (!_isDocked)
                footer.Children.Add(Button("Close", (_, __) => Close()));
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var tabs = new TabControl();
            tabs.Items.Add(Tab("Basics", BuildBasics()));
            tabs.Items.Add(Tab("Textures / Palettes", BuildTextureSqlTools()));
            tabs.Items.Add(Tab("Models / Parts", BuildModelSqlTools()));
            tabs.Items.Add(Tab("Combat", BuildCombat()));
            tabs.Items.Add(Tab("Skills", BuildCollection(_skills, _skillRows, () => new MobBuilderSkill())));
            tabs.Items.Add(Tab("Spells", BuildCollection(_spells, _spellRows, () => new MobBuilderSpell())));
            tabs.Items.Add(Tab("Loot", BuildCollection(_loot, _lootRows, () => new MobBuilderLootEntry())));
            tabs.Items.Add(Tab("Spawns", BuildCollection(_spawns, _spawnRows, () => new MobBuilderSpawnEntry())));
            tabs.Items.Add(Tab("DerpACE / Notes", BuildNotes()));
            root.Children.Add(tabs);
            return root;
        }

        private UIElement BuildBasics()
        {
            var grid = FormGrid();
            AddRow(grid, "Name", _name, "Display name DerpACE/admins will see.");
            AddRow(grid, "New WCID", _wcid, "Target weenie class id for the mob override/new mob.");
            AddRow(grid, "Clone From WCID", _cloneFrom, "Optional source mob to clone before applying overrides.");
            AddRow(grid, "Weenie Type", _weenieType, "Usually Creature for mobs.");
            AddRow(grid, "Creature Type", _creatureType, "Used for grouping, damage/AI assumptions, and DerpACE metadata.");
            AddRow(grid, "Display Setup", _setup, "0x02 setup/model id used for 3D preview and exported visual metadata.");
            AddRow(grid, "Motion Table", _motion, "Animation/motion table id.");
            AddRow(grid, "Sound Table", _sound, "Creature sound table id.");
            AddRow(grid, "Default Palette", _palette, "Palette template/base id to apply by default.");
            AddRow(grid, "Shade", _shade, "ACE shade value from weenie_properties_float type 12.");
            AddRow(grid, "Scale", _scale, "Visual scale multiplier.");
            return Wrap(grid);
        }

        private UIElement BuildTextureSqlTools()
        {
            _textureMaps.ItemsSource = _textureRows;
            _textureMaps.AutoGenerateColumns = false;
            _textureMaps.CanUserAddRows = false;
            _textureMaps.CanUserDeleteRows = true;
            _textureMaps.SelectionMode = DataGridSelectionMode.Extended;
            _textureMaps.Columns.Clear();
            _textureMaps.Columns.Add(new DataGridTextColumn { Header = "Index", Binding = new Binding("Index") { Mode = BindingMode.TwoWay }, Width = 60 });
            _textureMaps.Columns.Add(new DataGridTextColumn { Header = "Old Texture", Binding = new Binding("OldId") { Mode = BindingMode.TwoWay }, Width = 110 });
            _textureMaps.Columns.Add(new DataGridTextColumn { Header = "New Texture", Binding = new Binding("NewId") { Mode = BindingMode.TwoWay }, Width = 110 });
            _textureMaps.Columns.Add(new DataGridTextColumn { Header = "SQL Comment / Part", Binding = new Binding("Part") { Mode = BindingMode.TwoWay }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _textureMaps.SelectionChanged += (_, __) => UpdateTextureSqlPreview();
            _textureMaps.CellEditEnding += (_, __) => Dispatcher.BeginInvoke(new Action(() => { UpdateTextureSqlPreview(); PreviewMobVisual(resetCamera: false); }));

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };
            Grid.SetColumn(left, 0);
            root.Children.Add(left);
            var rowButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            rowButtons.Children.Add(Button("+ SQL Row", (_, __) => AddTextureSqlRow()));
            rowButtons.Children.Add(Button("Remove Selected", (_, __) => RemoveSelected(_textureMaps, _textureRows)));
            rowButtons.Children.Add(Button("Preview", (_, __) => PreviewMobVisual(resetCamera: false)));
            DockPanel.SetDock(rowButtons, Dock.Bottom);
            left.Children.Add(rowButtons);
            var help = new TextBlock
            {
                Text = "weenie_properties_texture_map rows (Index, OldId, NewId). Select row(s), then choose a texture from the browser to replace NewId.",
                Foreground = Brush("#B8B8B8"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel.SetDock(help, Dock.Top);
            left.Children.Add(help);
            left.Children.Add(_textureMaps);

            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Brush("#555555") };
            Grid.SetColumn(splitter, 1);
            root.Children.Add(splitter);

            var right = new DockPanel { Margin = new Thickness(8, 0, 0, 0), LastChildFill = true };
            Grid.SetColumn(right, 2);
            root.Children.Add(right);

            var previews = new Grid { Height = 126, Margin = new Thickness(0, 0, 0, 8) };
            previews.ColumnDefinitions.Add(new ColumnDefinition());
            previews.ColumnDefinitions.Add(new ColumnDefinition());
            previews.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previews.RowDefinitions.Add(new RowDefinition());
            previews.Children.Add(new TextBlock { Text = "Old", FontWeight = FontWeights.Bold });
            var replacementLabel = new TextBlock { Text = "New / Replacement", FontWeight = FontWeights.Bold };
            Grid.SetColumn(replacementLabel, 1); previews.Children.Add(replacementLabel);
            _oldTexturePreview = new System.Windows.Controls.Image { Stretch = Stretch.Uniform };
            _newTexturePreview = new System.Windows.Controls.Image { Stretch = Stretch.Uniform };
            var oldBorder = PreviewBorder(_oldTexturePreview); Grid.SetRow(oldBorder, 1); previews.Children.Add(oldBorder);
            var newBorder = PreviewBorder(_newTexturePreview); Grid.SetColumn(newBorder, 1); Grid.SetRow(newBorder, 1); previews.Children.Add(newBorder);
            DockPanel.SetDock(previews, Dock.Top); right.Children.Add(previews);

            var suggestionPanel = new DockPanel { LastChildFill = true, Height = 100, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(suggestionPanel, Dock.Top); right.Children.Add(suggestionPanel);
            var suggestionTitle = new TextBlock { Text = "Palette suggestions from selected texture", FontWeight = FontWeights.Bold };
            DockPanel.SetDock(suggestionTitle, Dock.Top); suggestionPanel.Children.Add(suggestionTitle);
            _paletteSummary = new TextBlock { Text = "Select an indexed SurfaceTexture to inspect palette/range data.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B8B8B8"), Margin = new Thickness(0, 2, 0, 3) };
            DockPanel.SetDock(_paletteSummary, Dock.Top); suggestionPanel.Children.Add(_paletteSummary);
            _paletteSuggestions = new ListBox { DisplayMemberPath = "DisplayName" };
            suggestionPanel.Children.Add(_paletteSuggestions);

            var browserHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(browserHeader, Dock.Top); right.Children.Add(browserHeader);
            var browserLabel = new TextBlock { Text = "SurfaceTexture Browser", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(browserLabel, Dock.Left); browserHeader.Children.Add(browserLabel);
            _textureFilter = new TextBox { Height = 24, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Filter by hex id" };
            _textureFilter.TextChanged += (_, __) => RefreshTextureBrowser();
            browserHeader.Children.Add(_textureFilter);

            _textureStatus = BrowserStatusText();
            DockPanel.SetDock(_textureStatus, Dock.Bottom); right.Children.Add(_textureStatus);
            _textureBrowser = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetCanContentScroll(_textureBrowser, true);
            _textureBrowser.SelectionChanged += TextureBrowser_SelectionChanged;
            _textureBrowser.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => LoadMoreTextureBrowserItemsIfNeeded(e)));
            right.Children.Add(_textureBrowser);
            return root;
        }

        private UIElement BuildModelSqlTools()
        {
            _animParts.ItemsSource = _animRows;
            _animParts.AutoGenerateColumns = false;
            _animParts.CanUserAddRows = false;
            _animParts.CanUserDeleteRows = true;
            _animParts.SelectionMode = DataGridSelectionMode.Extended;
            _animParts.Columns.Clear();
            _animParts.Columns.Add(new DataGridTextColumn { Header = "Index", Binding = new Binding("Index") { Mode = BindingMode.TwoWay }, Width = 60 });
            _animParts.Columns.Add(new DataGridTextColumn { Header = "AnimationId / Model", Binding = new Binding("AnimationId") { Mode = BindingMode.TwoWay }, Width = 130 });
            _animParts.Columns.Add(new DataGridTextColumn { Header = "SQL Comment / Part", Binding = new Binding("Part") { Mode = BindingMode.TwoWay }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _animParts.SelectionChanged += (_, __) => PreviewMobVisual(resetCamera: false);
            _animParts.CellEditEnding += (_, __) => Dispatcher.BeginInvoke(new Action(() => PreviewMobVisual(resetCamera: false)));

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };
            Grid.SetColumn(left, 0); root.Children.Add(left);
            var rowButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            rowButtons.Children.Add(Button("+ Part Row", (_, __) => AddAnimSqlRow()));
            rowButtons.Children.Add(Button("Remove Selected", (_, __) => RemoveSelected(_animParts, _animRows)));
            rowButtons.Children.Add(Button("Preview", (_, __) => PreviewMobVisual(resetCamera: false)));
            DockPanel.SetDock(rowButtons, Dock.Bottom); left.Children.Add(rowButtons);
            var help = new TextBlock
            {
                Text = "weenie_properties_anim_part rows (Index, AnimationId). Select row(s), then choose a model/world object from the browser to replace the part.",
                Foreground = Brush("#B8B8B8"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel.SetDock(help, Dock.Top); left.Children.Add(help);
            left.Children.Add(_animParts);

            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Brush("#555555") };
            Grid.SetColumn(splitter, 1); root.Children.Add(splitter);

            var right = new DockPanel { Margin = new Thickness(8, 0, 0, 0), LastChildFill = true };
            Grid.SetColumn(right, 2); root.Children.Add(right);
            var browserHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(browserHeader, Dock.Top); right.Children.Add(browserHeader);
            var browserLabel = new TextBlock { Text = "World Object / Model Browser", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(browserLabel, Dock.Left); browserHeader.Children.Add(browserLabel);
            _modelFilter = new TextBox { Height = 24, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Filter by hex id" };
            _modelFilter.TextChanged += (_, __) => RefreshModelBrowser();
            browserHeader.Children.Add(_modelFilter);

            _modelStatus = BrowserStatusText();
            DockPanel.SetDock(_modelStatus, Dock.Bottom); right.Children.Add(_modelStatus);
            _modelBrowser = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetCanContentScroll(_modelBrowser, true);
            _modelBrowser.SelectionChanged += ModelBrowser_SelectionChanged;
            _modelBrowser.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => LoadMoreModelBrowserItemsIfNeeded(e)));
            right.Children.Add(_modelBrowser);
            return root;
        }
        private UIElement BuildCombat()
        {
            var grid = FormGrid();
            AddRow(grid, "Level", _level, "Displayed creature level.");
            AddRow(grid, "Health", _health, "Max health.");
            AddRow(grid, "Stamina", _stamina, "Max stamina.");
            AddRow(grid, "Mana", _mana, "Max mana.");
            AddRow(grid, "Armor Level", _armor, "Base armor level before equipment/protections.");
            AddRow(grid, "Damage Rating", _damage, "Simple DerpACE tuning metadata for outbound damage.");
            AddRow(grid, "Attack Skill", _attack, "Melee/missile/magic attack target value.");
            AddRow(grid, "Defense Skill", _defense, "Primary defense target value.");
            AddRow(grid, "Magic Defense", _magicDefense, "Magic defense target value.");
            AddRow(grid, "AI Profile", _aiProfile, "Starting behavior profile for DerpACE import.");
            AddRow(grid, "Tolerance", _tolerance, "Aggression/friendliness hint.");
            AddRow(grid, "Faction", _faction, "Optional DerpACE faction/key.");
            return Wrap(grid);
        }

        private UIElement BuildNotes()
        {
            var panel = new DockPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Use this tab for DerpACE-only metadata until we wire direct server/database writes. Export keeps it in the JSON so admins can share and import mob drafts.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = Brush("#CFCFCF")
            });
            DockPanel.SetDock(panel.Children[0], Dock.Top);
            _notes.MinHeight = 420;
            _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            panel.Children.Add(_notes);
            return panel;
        }

        private UIElement BuildCollection<T>(DataGrid grid, List<T> rows, Func<T> create)
        {
            grid.ItemsSource = rows;
            grid.AutoGenerateColumns = true;
            grid.CanUserAddRows = false;
            grid.CanUserDeleteRows = true;
            grid.Margin = new Thickness(0, 0, 0, 8);
            var root = new DockPanel();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bar.Children.Add(Button("+ Row", (_, __) => { rows.Add(create()); grid.Items.Refresh(); }));
            bar.Children.Add(Button("Remove Selected", (_, __) => { while (grid.SelectedItems.Count > 0 && grid.SelectedItems[0] is T row) rows.Remove(row); grid.Items.Refresh(); }));
            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);
            root.Children.Add(grid);
            return root;
        }

        private void SeedRows()
        {
            _skillRows.Add(new MobBuilderSkill { Name = "MeleeDefense", Value = 100 });
            _skillRows.Add(new MobBuilderSkill { Name = "MagicDefense", Value = 100 });
            _lootRows.Add(new MobBuilderLootEntry());
            _spawnRows.Add(new MobBuilderSpawnEntry());
            _skills.Items.Refresh();
            _loot.Items.Refresh();
            _spawns.Items.Refresh();
        }

        private MobBuilderDefinition BuildDefinition()
        {
            ValidateIds();
            return new MobBuilderDefinition
            {
                Name = _name.Text.Trim(),
                WeenieClassId = NormalizeHex(_wcid.Text),
                CloneFromWcid = NormalizeHex(_cloneFrom.Text),
                WeenieType = _weenieType.Text,
                CreatureType = _creatureType.Text,
                DisplaySetupId = NormalizeHex(_setup.Text),
                MotionTableId = NormalizeHex(_motion.Text),
                SoundTableId = NormalizeHex(_sound.Text),
                PaletteBaseId = NormalizeHex(_palette.Text),
                Shade = Float(_shade.Text, 0.0f),
                Scale = Double(_scale.Text, 1.0),
                Level = Int(_level.Text, 1),
                MaxHealth = Int(_health.Text, 100),
                MaxStamina = Int(_stamina.Text, 100),
                MaxMana = Int(_mana.Text, 0),
                ArmorLevel = Int(_armor.Text, 0),
                DamageRating = Int(_damage.Text, 0),
                AttackSkill = Int(_attack.Text, 100),
                DefenseSkill = Int(_defense.Text, 100),
                MagicDefenseSkill = Int(_magicDefense.Text, 100),
                AiProfile = _aiProfile.Text,
                Tolerance = _tolerance.Text,
                Faction = _faction.Text.Trim(),
                TextureMaps = new List<MobBuilderTextureMap>(_textureRows),
                AnimParts = new List<MobBuilderAnimPart>(_animRows),
                Skills = new List<MobBuilderSkill>(_skillRows),
                Spells = new List<MobBuilderSpell>(_spellRows),
                Loot = new List<MobBuilderLootEntry>(_lootRows),
                Spawns = new List<MobBuilderSpawnEntry>(_spawnRows),
                Notes = _notes.Text,
                UpdatedUtc = DateTime.UtcNow,
                DerpAceProperties = new Dictionary<string, string>
                {
                    ["workflow"] = "mob-builder",
                    ["visualSource"] = "portal-dat",
                    ["exportTarget"] = "DerpACE admin import"
                }
            };
        }

        private void LoadDefinition(MobBuilderDefinition def)
        {
            if (def == null) return;
            _name.Text = def.Name;
            _wcid.Text = def.WeenieClassId;
            _cloneFrom.Text = def.CloneFromWcid;
            _weenieType.Text = def.WeenieType;
            _creatureType.Text = def.CreatureType;
            _setup.Text = def.DisplaySetupId;
            _motion.Text = def.MotionTableId;
            _sound.Text = def.SoundTableId;
            _palette.Text = def.PaletteBaseId;
            _shade.Text = def.Shade.ToString("0.###");
            _scale.Text = def.Scale.ToString("0.###");
            _level.Text = def.Level.ToString();
            _health.Text = def.MaxHealth.ToString();
            _stamina.Text = def.MaxStamina.ToString();
            _mana.Text = def.MaxMana.ToString();
            _armor.Text = def.ArmorLevel.ToString();
            _damage.Text = def.DamageRating.ToString();
            _attack.Text = def.AttackSkill.ToString();
            _defense.Text = def.DefenseSkill.ToString();
            _magicDefense.Text = def.MagicDefenseSkill.ToString();
            _aiProfile.Text = def.AiProfile;
            _tolerance.Text = def.Tolerance;
            _faction.Text = def.Faction;
            _notes.Text = def.Notes;
            _textureRows.Clear(); _textureRows.AddRange(def.TextureMaps ?? new());
            _animRows.Clear(); _animRows.AddRange(def.AnimParts ?? new());
            _skillRows.Clear(); _skillRows.AddRange(def.Skills ?? new());
            _spellRows.Clear(); _spellRows.AddRange(def.Spells ?? new());
            _lootRows.Clear(); _lootRows.AddRange(def.Loot ?? new());
            _spawnRows.Clear(); _spawnRows.AddRange(def.Spawns ?? new());
            _textureMaps.Items.Refresh(); _animParts.Items.Refresh(); _skills.Items.Refresh(); _spells.Items.Refresh(); _loot.Items.Refresh(); _spawns.Items.Refresh();
            RefreshVisualTools();
            PreviewMobVisual(resetCamera: true);
        }

        private void RefreshVisualTools()
        {
            _textureIds = DatManager.PortalDat == null ? Array.Empty<uint>() : DatIdIndex.SurfaceTextureIds();
            _modelIds = DatManager.PortalDat == null ? Array.Empty<uint>() : DatIdIndex.ModelAndSetupIds();
            RefreshTextureBrowser();
            RefreshModelBrowser();
            UpdateTextureSqlPreview();
        }

        private ListBoxItem BuildTextureBrowserItem(uint id)
        {
            var row = new DockPanel { Margin = new Thickness(3) };
            var image = new System.Windows.Controls.Image
            {
                Source = BuildTexturePreview(id),
                Width = 48,
                Height = 48,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0),
                SnapsToDevicePixels = true
            };
            DockPanel.SetDock(image, Dock.Left);
            row.Children.Add(image);

            var label = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = $"0x{id:X8}", FontWeight = FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = "SurfaceTexture", Foreground = Brush("#B8B8B8") });
            row.Children.Add(label);

            return new ListBoxItem
            {
                Content = row,
                Tag = id,
                MinHeight = 56,
                ToolTip = "Select to write this id into NewId for selected SQL texture row(s)."
            };
        }
        private void RefreshTextureBrowser()
        {
            if (_textureBrowser == null) return;
            _filteredTextureIds = FilterIds(_textureIds, _textureFilter?.Text);
            _textureLoadedCount = 0;
            _textureBrowser.Items.Clear();
            LoadMoreTextureBrowserItems();
        }

        private void RefreshModelBrowser()
        {
            if (_modelBrowser == null) return;
            _filteredModelIds = FilterIds(_modelIds, _modelFilter?.Text);
            _modelLoadedCount = 0;
            _modelBrowser.Items.Clear();
            LoadMoreModelBrowserItems();
        }

        private void LoadMoreTextureBrowserItemsIfNeeded(ScrollChangedEventArgs e)
        {
            if (e.ExtentHeight <= 0 || e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 8)
                return;
            LoadMoreTextureBrowserItems();
        }

        private void LoadMoreModelBrowserItemsIfNeeded(ScrollChangedEventArgs e)
        {
            if (e.ExtentHeight <= 0 || e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 8)
                return;
            LoadMoreModelBrowserItems();
        }

        private void LoadMoreTextureBrowserItems()
        {
            if (_textureBrowser == null) return;
            var total = _filteredTextureIds?.Count ?? 0;
            var next = Math.Min(_textureLoadedCount + BrowserPageSize, total);
            for (var i = _textureLoadedCount; i < next; i++)
                _textureBrowser.Items.Add(BuildTextureBrowserItem(_filteredTextureIds[i]));
            _textureLoadedCount = next;
            UpdateBrowserStatus(_textureStatus, _textureLoadedCount, total);
        }

        private void LoadMoreModelBrowserItems()
        {
            if (_modelBrowser == null) return;
            var total = _filteredModelIds?.Count ?? 0;
            var next = Math.Min(_modelLoadedCount + BrowserPageSize, total);
            for (var i = _modelLoadedCount; i < next; i++)
            {
                var id = _filteredModelIds[i];
                var label = (id >> 24) == 0x02 ? "Setup" : "GfxObj";
                _modelBrowser.Items.Add(new ListBoxItem { Content = $"0x{id:X8}  {label}", Tag = id, ToolTip = "Select to write this id into AnimationId for selected SQL anim_part row(s)." });
            }
            _modelLoadedCount = next;
            UpdateBrowserStatus(_modelStatus, _modelLoadedCount, total);
        }

        private static IReadOnlyList<uint> FilterIds(IReadOnlyList<uint> ids, string filter)
        {
            if (ids == null || ids.Count == 0)
                return Array.Empty<uint>();
            if (string.IsNullOrWhiteSpace(filter))
                return ids;

            var needle = filter.Trim();
            if (needle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                needle = needle[2..];
            return ids.Where(id => id.ToString("X8").Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void UpdateBrowserStatus(TextBlock status, int loaded, int total)
        {
            if (status == null) return;
            status.Text = total == 0
                ? "No matches"
                : loaded >= total
                    ? $"Showing all {total:N0}"
                    : $"Showing {loaded:N0} of {total:N0} — scroll for more";
        }
        private void TextureBrowser_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_textureBrowser?.SelectedItem is not ListBoxItem item || item.Tag is not uint id) return;
            var targets = _textureMaps.SelectedItems.Cast<MobBuilderTextureMap>().ToList();
            if (targets.Count == 0)
            {
                var row = new MobBuilderTextureMap { Index = DefaultSelectedIndex(), OldId = HexId.Format(id), NewId = HexId.Format(id), Part = DefaultSelectedPartName() };
                _textureRows.Add(row);
                _textureMaps.Items.Refresh();
                _textureMaps.SelectedItem = row;
                targets.Add(row);
            }
            foreach (var row in targets)
                row.NewId = HexId.Format(id);
            _textureMaps.Items.Refresh();
            UpdateTextureSqlPreview();
            PreviewMobVisual(resetCamera: false);
            MainWindow.Instance?.AddStatusText($"Mob Builder wrote texture 0x{id:X8} into {targets.Count} SQL texture_map row(s).");
        }

        private void ModelBrowser_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_modelBrowser?.SelectedItem is not ListBoxItem item || item.Tag is not uint id) return;
            var targets = _animParts.SelectedItems.Cast<MobBuilderAnimPart>().ToList();
            if (targets.Count == 0)
            {
                var row = new MobBuilderAnimPart { Index = DefaultSelectedIndex(), AnimationId = HexId.Format(id), Part = DefaultSelectedPartName() };
                _animRows.Add(row);
                _animParts.Items.Refresh();
                _animParts.SelectedItem = row;
                targets.Add(row);
            }
            foreach (var row in targets)
                row.AnimationId = HexId.Format(id);
            _animParts.Items.Refresh();
            PreviewMobVisual(resetCamera: false);
            MainWindow.Instance?.AddStatusText($"Mob Builder wrote model 0x{id:X8} into {targets.Count} SQL anim_part row(s).");
        }

        private void AddTextureSqlRow()
        {
            var index = DefaultSelectedIndex();
            var row = new MobBuilderTextureMap { Index = index, OldId = "0x00000000", NewId = "0x00000000", Part = DefaultSelectedPartName() };
            _textureRows.Add(row);
            _textureMaps.Items.Refresh();
            _textureMaps.SelectedItem = row;
        }

        private void AddAnimSqlRow()
        {
            var index = NextUnusedAnimIndex();
            var row = new MobBuilderAnimPart { Index = index, AnimationId = "0x00000000", Part = DefaultPartName(index) };
            _animRows.Add(row);
            _animParts.Items.Refresh();
            _animParts.SelectedItem = row;
        }

        private int DefaultSelectedIndex()
        {
            if (_animParts?.SelectedItem is MobBuilderAnimPart anim) return anim.Index;
            if (_textureMaps?.SelectedItem is MobBuilderTextureMap tex) return tex.Index;
            return NextUnusedAnimIndex();
        }

        private string DefaultSelectedPartName()
        {
            if (_animParts?.SelectedItem is MobBuilderAnimPart anim && !string.IsNullOrWhiteSpace(anim.Part)) return anim.Part;
            if (_textureMaps?.SelectedItem is MobBuilderTextureMap tex && !string.IsNullOrWhiteSpace(tex.Part)) return tex.Part;
            return DefaultPartName(DefaultSelectedIndex());
        }

        private int NextUnusedAnimIndex()
        {
            for (var i = 0; i < 64; i++)
                if (!_animRows.Any(row => row.Index == i)) return i;
            return _animRows.Count;
        }

        private static string DefaultPartName(int index) => index switch
        {
            0 => "Base",
            1 => "Pelvis",
            2 => "Torso",
            3 => "Head",
            4 => "Left Upper Arm",
            5 => "Right Upper Arm",
            6 => "Left Forearm",
            7 => "Right Forearm",
            8 => "Left Hand",
            9 => "Right Hand",
            10 => "Left Thigh",
            11 => "Right Thigh",
            12 => "Left Shin",
            13 => "Right Shin",
            14 => "Left Foot",
            15 => "Right Foot",
            16 => "Head/Face",
            _ => $"Part {index}"
        };

        private void UpdateTextureSqlPreview()
        {
            var row = _textureMaps?.SelectedItem as MobBuilderTextureMap;
            if (row == null)
            {
                if (_oldTexturePreview != null) _oldTexturePreview.Source = null;
                if (_newTexturePreview != null) _newTexturePreview.Source = null;
                if (_paletteSummary != null) _paletteSummary.Text = "Select an indexed SurfaceTexture to inspect palette/range data.";
                if (_paletteSuggestions != null) _paletteSuggestions.ItemsSource = null;
                return;
            }

            var oldId = TryParseHex(row.OldId, out var parsedOld) ? parsedOld : 0;
            var newId = TryParseHex(row.NewId, out var parsedNew) ? parsedNew : 0;
            if (_oldTexturePreview != null) _oldTexturePreview.Source = BuildTexturePreview(oldId);
            if (_newTexturePreview != null) _newTexturePreview.Source = BuildTexturePreview(newId);
            UpdatePaletteSuggestions(newId);
        }

        private void UpdatePaletteSuggestions(uint textureId)
        {
            if (_paletteSummary == null || _paletteSuggestions == null) return;
            if (textureId == 0)
            {
                _paletteSummary.Text = "Select an indexed SurfaceTexture to inspect palette/range data.";
                _paletteSuggestions.ItemsSource = null;
                return;
            }

            var analysis = TexturePaletteSuggestionService.Analyze(textureId, null);
            _paletteSummary.Text = analysis.Summary ?? "No palette information found.";
            _paletteSuggestions.ItemsSource = analysis.Suggestions;
            _paletteSuggestions.SelectedItem = analysis.Suggestions?.FirstOrDefault();
        }

        private ImageSource BuildTexturePreview(uint id)
        {
            if (id == 0) return null;
            if (_texturePreviewCache.TryGetValue(id, out var cached)) return cached;
            try
            {
                if (DatManager.PortalDat == null) return null;
                uint type = id >> 24;
                ACE.DatLoader.FileTypes.Texture texFile = null;
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
                    var surface = DatManager.PortalDat.ReadFromDat<Surface>(id);
                    if (surface?.OrigTextureId != 0)
                        return BuildTexturePreview(surface.OrigTextureId);
                }

                Microsoft.Xna.Framework.Graphics.Texture2D decoded = null;
                try { decoded = TextureCache.Get(id, null, null, useCache: false); } catch { }
                if (decoded != null)
                {
                    var colors = new Microsoft.Xna.Framework.Color[decoded.Width * decoded.Height];
                    decoded.GetData(colors);
                    var image = BuildWriteableBitmapFromColors(colors, decoded.Width, decoded.Height);
                    if (image != null) return _texturePreviewCache[id] = image;
                }

                using var bmp = texFile?.GetBitmap();
                if (bmp == null) return null;
                var image2 = BuildWriteableBitmapFromDrawingBitmap(bmp);
                if (image2 != null) _texturePreviewCache[id] = image2;
                return image2;
            }
            catch { return null; }
        }

        private static WriteableBitmap BuildWriteableBitmapFromColors(Microsoft.Xna.Framework.Color[] colors, int width, int height)
        {
            try
            {
                var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                var data = new byte[width * height * 4];
                var p = 0;
                foreach (var c in colors)
                {
                    data[p++] = c.B;
                    data[p++] = c.G;
                    data[p++] = c.R;
                    data[p++] = c.A == 0 ? (byte)255 : c.A;
                }
                wb.WritePixels(new Int32Rect(0, 0, width, height), data, width * 4, 0);
                wb.Freeze();
                return wb;
            }
            catch { return null; }
        }

        private static WriteableBitmap BuildWriteableBitmapFromDrawingBitmap(System.Drawing.Bitmap bitmap)
        {
            const int MaxDim = 256;
            var w = bitmap.Width;
            var h = bitmap.Height;
            var scale = 1.0;
            if (w > MaxDim || h > MaxDim)
            {
                scale = Math.Min((double)MaxDim / w, (double)MaxDim / h);
                w = Math.Max(1, (int)(w * scale));
                h = Math.Max(1, (int)(h * scale));
            }

            System.Drawing.Bitmap working = bitmap;
            if (scale != 1.0)
            {
                working = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = System.Drawing.Graphics.FromImage(working);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(bitmap, new System.Drawing.Rectangle(0, 0, w, h));
            }

            try
            {
                var wb = new WriteableBitmap(working.Width, working.Height, 96, 96, PixelFormats.Bgra32, null);
                var data = new byte[working.Width * working.Height * 4];
                var p = 0;
                for (var y = 0; y < working.Height; y++)
                    for (var x = 0; x < working.Width; x++)
                    {
                        var c = working.GetPixel(x, y);
                        data[p++] = c.B;
                        data[p++] = c.G;
                        data[p++] = c.R;
                        data[p++] = c.A == 0 ? (byte)255 : c.A;
                    }
                wb.WritePixels(new Int32Rect(0, 0, working.Width, working.Height), data, working.Width * 4, 0);
                wb.Freeze();
                return wb;
            }
            finally
            {
                if (!ReferenceEquals(working, bitmap)) working.Dispose();
            }
        }

        private void PreviewMobVisual(bool resetCamera)
        {
            try
            {
                if (DatManager.PortalDat == null || ModelViewer.Instance == null) return;
                var id = HexId.Parse(_setup.Text);
                if (GameView.ViewMode != ViewMode.Model)
                    GameView.ViewMode = ViewMode.Model;
                ModelViewer.Instance.LoadModel(id, BuildVisualObjDesc(id), resetCamera);
            }
            catch { }
        }
        private ACViewer.Model.ObjDesc BuildVisualObjDesc(uint setupId)
        {
            var objDesc = new ACViewer.Model.ObjDesc(setupId, (ClothingTable)null)
            {
                PartChanges = new Dictionary<uint, PartChange>()
            };

            SetupModel setup = null;
            try { setup = DatManager.PortalDat?.ReadFromDat<SetupModel>(setupId); } catch { }

            PartChange GetPart(int index)
            {
                var partIndex = (uint)Math.Max(0, index);
                if (objDesc.PartChanges.TryGetValue(partIndex, out var existing))
                    return existing;

                uint modelId = 0;
                if (setup?.Parts != null && index >= 0 && index < setup.Parts.Count)
                    modelId = setup.Parts[index];
                else if (setupId >> 24 == 0x01)
                    modelId = setupId;

                var change = new PartChange(modelId);
                objDesc.PartChanges[partIndex] = change;
                return change;
            }

            foreach (var row in _animRows)
            {
                if (!HexId.TryParse(row.AnimationId, out var modelId))
                    continue;
                GetPart(row.Index).NewGfxObjId = modelId;
            }

            foreach (var row in _textureRows)
            {
                if (!HexId.TryParse(row.OldId, out var oldId) || !HexId.TryParse(row.NewId, out var newId))
                    continue;
                GetPart(row.Index).AddTexture(oldId, newId);
            }

            return objDesc;
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DatManager.PortalDat == null)
                    throw new InvalidOperationException("Load your DATs before previewing a mob.");

                var id = HexId.Parse(_setup.Text);
                PreviewMobVisual(resetCamera: false);
                CustomPaletteDialog.ActiveInstance?.SetMobBuilderMode(_name.Text);
                MainWindow.Instance?.AddStatusText($"Mob Builder previewing {HexId.Format(id)} with {_animRows.Count} model rows and {_textureRows.Count} texture rows.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mob Builder Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "ACE Weenie SQL or Mob JSON (*.sql;*.json)|*.sql;*.json|SQL files (*.sql)|*.sql|JSON files (*.json)|*.json|All files (*.*)|*.*", Title = "Import Mob Builder SQL/JSON" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var raw = File.ReadAllText(dlg.FileName);
                var trimmed = raw.TrimStart();
                var isSql = Path.GetExtension(dlg.FileName).Equals(".sql", StringComparison.OrdinalIgnoreCase)
                            || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                            || trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                            || trimmed.Contains("weenie_properties_texture_map", StringComparison.OrdinalIgnoreCase)
                            || trimmed.Contains("weenie_properties_anim_part", StringComparison.OrdinalIgnoreCase);
                var def = isSql ? MobBuilderSqlParser.Parse(raw) : JsonSerializer.Deserialize<MobBuilderDefinition>(raw);
                LoadDefinition(def);
                CustomPaletteDialog.ActiveInstance?.SetMobBuilderMode(_name.Text);
                MainWindow.Instance?.AddStatusText($"Mob Builder imported {Path.GetFileName(dlg.FileName)}.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "Mob Builder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "ACE/DerpACE Mob SQL (*.sql)|*.sql|DerpACE Mob Builder JSON (*.json)|*.json", FileName = $"{SafeFileName(_name.Text)}.sql", Title = "Export Mob Builder SQL" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var definition = BuildDefinition();
                var output = Path.GetExtension(dlg.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase)
                    ? ToJson(definition)
                    : ToSql(definition);
                File.WriteAllText(dlg.FileName, output);
                MainWindow.Instance?.AddStatusText($"Mob Builder exported SQL: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Mob Builder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ToSql(BuildDefinition()));
                MainWindow.Instance?.AddStatusText("Mob Builder SQL copied to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mob Builder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ValidateIds()
        {
            NormalizeHex(_wcid.Text);
            NormalizeHex(_cloneFrom.Text);
            NormalizeHex(_setup.Text);
            NormalizeHex(_motion.Text);
            NormalizeHex(_sound.Text);
            NormalizeHex(_palette.Text);
        }

        private static void RemoveSelected<T>(DataGrid grid, List<T> rows)
        {
            var selected = grid?.SelectedItems?.Cast<T>().ToList();
            if (selected == null || selected.Count == 0) return;
            foreach (var row in selected)
                rows.Remove(row);
            grid.Items.Refresh();
        }

        private static TextBlock BrowserStatusText() => new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = Brush("#B8B8B8"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        private static Border PreviewBorder(UIElement child) => new()
        {
            BorderBrush = Brush("#666666"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 6, 0),
            Background = Brush("#252526"),
            Child = child
        };

        private static bool TryParseHex(string text, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { value = HexId.Parse(text); return true; }
            catch { return false; }
        }
        private static string ToSql(MobBuilderDefinition def)
        {
            var wcid = HexId.Parse(def.WeenieClassId);
            var className = SafeSqlName(def.Name, wcid);
            var sb = new StringBuilder();
            sb.AppendLine($"-- DerpACE Mob Builder export: {SqlEscape(def.Name)}");
            sb.AppendLine($"-- Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
            sb.AppendLine($"-- Visual SQL rows are sourced from Mob Builder texture/model tabs.");
            sb.AppendLine();
            sb.AppendLine($"DELETE FROM `weenie` WHERE `class_Id` = {wcid};");
            sb.AppendLine();
            sb.AppendLine("INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES");
            sb.AppendLine($"({wcid}, '{SqlEscape(className)}', {WeenieTypeId(def.WeenieType)}, NOW()) /* {SqlEscape(def.WeenieType)} */;");
            sb.AppendLine();

            AppendValues(sb, "weenie_properties_string", "`object_Id`, `type`, `value`", new[]
            {
                $"({wcid}, 1, '{SqlEscape(def.Name)}') /* Name */"
            });

            AppendValues(sb, "weenie_properties_d_i_d", "`object_Id`, `type`, `value`", new[]
            {
                $"({wcid}, 1, {SqlDid(def.DisplaySetupId)}) /* Setup */",
                $"({wcid}, 2, {SqlDid(def.MotionTableId)}) /* MotionTable */",
                $"({wcid}, 3, {SqlDid(def.SoundTableId)}) /* SoundTable */"
            });

            var intRows = new List<string>
            {
                $"({wcid}, 2, {CreatureTypeId(def.CreatureType)}) /* CreatureType - {SqlEscape(def.CreatureType)} */",
                $"({wcid}, 3, {SqlIntValue(def.PaletteBaseId)}) /* PaletteTemplate */",
                $"({wcid}, 25, {def.Level}) /* Level */"
            };
            if (def.DamageRating != 0)
                intRows.Add($"({wcid}, 307, {def.DamageRating}) /* DamageRating */");
            AppendValues(sb, "weenie_properties_int", "`object_Id`, `type`, `value`", intRows);

            AppendValues(sb, "weenie_properties_float", "`object_Id`, `type`, `value`", new[]
            {
                $"({wcid}, 12, {SqlFloat(def.Shade)}) /* Shade */",
                $"({wcid}, 39, {SqlFloat(def.Scale)}) /* DefaultScale */"
            });

            var attributeRows = new List<string>
            {
                $"({wcid}, 1, {def.MaxHealth}, 0, 0) /* MaxHealth */",
                $"({wcid}, 3, {def.MaxStamina}, 0, 0) /* MaxStamina */",
                $"({wcid}, 5, {def.MaxMana}, 0, 0) /* MaxMana */"
            };
            AppendValues(sb, "weenie_properties_attribute_2nd", "`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`", attributeRows);

            var textureRows = (def.TextureMaps ?? new List<MobBuilderTextureMap>())
                .Where(row => !string.IsNullOrWhiteSpace(row.OldId) && !string.IsNullOrWhiteSpace(row.NewId))
                .Select(row => $"({wcid}, {row.Index}, {SqlDid(row.OldId)}, {SqlDid(row.NewId)}) /* {SqlEscape(row.Part)} */")
                .ToList();
            AppendValues(sb, "weenie_properties_texture_map", "`object_Id`, `index`, `old_Id`, `new_Id`", textureRows);

            var animRows = (def.AnimParts ?? new List<MobBuilderAnimPart>())
                .Where(row => !string.IsNullOrWhiteSpace(row.AnimationId))
                .Select(row => $"({wcid}, {row.Index}, {SqlDid(row.AnimationId)}) /* {SqlEscape(row.Part)} */")
                .ToList();
            AppendValues(sb, "weenie_properties_anim_part", "`object_Id`, `index`, `animation_Id`", animRows);

            var skillRows = (def.Skills ?? new List<MobBuilderSkill>())
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .Select(row => $"({wcid}, {SkillTypeId(row.Name)}, 0, 0, 0, {row.Value}, 0, 0, 0, 0) /* {SqlEscape(row.Name)} */")
                .ToList();
            AppendValues(sb, "weenie_properties_skill", "`object_Id`, `type`, `level_From_P_P`, `S_A_C`, `P_P`, `init_Level`, `resistance_At_Last_Check`, `last_Used_Time`, `c_P_Spent`, `starting_Value`", skillRows);

            var spellRows = (def.Spells ?? new List<MobBuilderSpell>())
                .Where(row => !string.IsNullOrWhiteSpace(row.SpellId))
                .Select(row => $"({wcid}, {SqlIntValue(row.SpellId)}, {SqlFloat(row.Chance)})")
                .ToList();
            AppendValues(sb, "weenie_properties_spell_book", "`object_Id`, `spell`, `probability`", spellRows);

            if (!string.IsNullOrWhiteSpace(def.Notes) || (def.DerpAceProperties?.Count ?? 0) > 0)
            {
                sb.AppendLine("-- DerpACE notes / metadata");
                if (!string.IsNullOrWhiteSpace(def.Notes))
                    sb.AppendLine($"-- Notes: {SqlEscape(def.Notes).Replace("\r", " ").Replace("\n", " ")}");
                foreach (var pair in def.DerpAceProperties ?? new Dictionary<string, string>())
                    sb.AppendLine($"-- {SqlEscape(pair.Key)}: {SqlEscape(pair.Value)}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendValues(StringBuilder sb, string table, string columns, IReadOnlyList<string> rows)
        {
            if (rows == null || rows.Count == 0) return;
            sb.AppendLine($"INSERT INTO `{table}` ({columns}) VALUES");
            for (var i = 0; i < rows.Count; i++)
                sb.AppendLine(i + 1 == rows.Count ? rows[i] + ";" : rows[i] + ",");
            sb.AppendLine();
        }

        private static string SqlDid(string value) => $"0x{HexId.Parse(value):X8}";
        private static string SqlFloat(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        private static string SqlEscape(string value) => (value ?? string.Empty).Replace("'", "''");
        private static int SqlIntValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var trimmed = value.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return unchecked((int)HexId.Parse(trimmed));
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }

        private static string SafeSqlName(string name, uint wcid)
        {
            var cleaned = new string((name ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? $"mob_{wcid}" : cleaned;
        }

        private static int WeenieTypeId(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "creature" => 10,
            "vendor" => 12,
            "container" => 20,
            "portal" => 7,
            _ => 10
        };

        private static int CreatureTypeId(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "human" => 31,
            "drudge" => 1,
            "banderling" => 3,
            "tumerok" => 6,
            "lugian" => 9,
            "undead" => 14,
            "olthoi" => 13,
            "virindi" => 16,
            "shadow" => 28,
            "elemental" => 18,
            _ => 0
        };

        private static int SkillTypeId(string value) => (value ?? string.Empty).Replace(" ", string.Empty).Trim().ToLowerInvariant() switch
        {
            "meleeattack" => 1,
            "missileattack" => 2,
            "magicattack" => 3,
            "meleedefense" => 6,
            "missiledefense" => 7,
            "magicdefense" => 15,
            _ => 6
        };
        private static string ToJson(MobBuilderDefinition def) => JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        private static string NormalizeHex(string text) => HexId.Format(HexId.Parse(text));
        private static int Int(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;
        private static float Float(string text, float fallback) => float.TryParse(text, out var value) ? value : fallback;
        private static double Double(string text, double fallback) => double.TryParse(text, out var value) ? value : fallback;
        private static string SafeFileName(string text)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
            return string.IsNullOrWhiteSpace(text) ? "mob" : text.Trim();
        }

        private static TabItem Tab(string header, UIElement content) => new() { Header = header, Content = content };
        private static TextBox Text(string value, bool acceptsReturn = false) => new()
        {
            Text = value,
            MinWidth = 220,
            AcceptsReturn = acceptsReturn,
            TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Background = Brush("#3A3A3D"),
            Foreground = Brush("#F1F1F1"),
            BorderBrush = Brush("#555555")
        };

        private static ComboBox Combo(string selected, params string[] values)
        {
            var combo = new ComboBox { MinWidth = 220, IsEditable = true, Background = Brush("#3A3A3D"), Foreground = Brush("#F1F1F1") };
            foreach (var value in values) combo.Items.Add(value);
            combo.Text = selected;
            return combo;
        }

        private static DataGrid DataGridControl() => new()
        {
            Background = Brush("#303030"),
            Foreground = Brush("#F1F1F1"),
            RowBackground = Brush("#333333"),
            AlternatingRowBackground = Brush("#383838"),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        private static Button Button(string text, RoutedEventHandler handler)
        {
            var button = new Button { Content = text, MinWidth = 88, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            button.Click += handler;
            return button;
        }

        private static Grid FormGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            return grid;
        }

        private static void AddRow(Grid grid, string label, Control control, string help)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = label, Margin = new Thickness(0, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#F1F1F1") };
            var hint = new TextBlock { Text = help, Margin = new Thickness(12, 6, 0, 6), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B8B8B8") };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            Grid.SetRow(control, row); Grid.SetColumn(control, 1);
            Grid.SetRow(hint, row); Grid.SetColumn(hint, 2);
            control.Margin = new Thickness(0, 3, 0, 3);
            grid.Children.Add(lbl); grid.Children.Add(control); grid.Children.Add(hint);
        }

        private static ScrollViewer Wrap(UIElement element) => new() { Content = element, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    }
}



















