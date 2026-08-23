using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACViewer.CustomPalettes;
using ACViewer.ClothingStudio;
using ACViewer.CustomTextures;
using ACViewer.Services;
using ACViewer.Utilities;
using AvalonDock.Layout;
using ACViewer.Model; // for CloSubPalette definitions
using ACViewer.ViewModels;
using System.Reflection; // added for reflection on texture overrides

namespace ACViewer.View
{
    public partial class ClothingTableList : UserControl
    {
        public static ClothingTableList Instance { get; set; }
        public static MainWindow MainWindow => MainWindow.Instance;
        public static ModelViewer ModelViewer => ModelViewer.Instance;
        public static ClothingTable CurrentClothingItem { get; private set; }
        public static uint PaletteTemplate { get; private set; }
        public static float Shade { get; private set; }
        public static uint Icon { get; private set; }

        private const uint CustomPaletteKey = 0xFFFFFFFF;
        private static bool _customActive;
        private static List<CloSubPalette> _customCloSubPalettes;
        private static float _customShade;
        private static uint? _lastActualPaletteTemplate;

        // cache last pushed non-custom resolved palettes (for editors / inspection)
        private static List<CloSubPalette> _resolvedStandardPalettes;

        // remember last JSON file path loaded (for toggling watch etc.)
        private static string _lastImportedJsonPath;

        // Data model used by VirindiColorTool
        public class VirindiColorInfo
        {
            public uint PalId { get; set; }
            public uint Color { get; set; }
        }

        public ClothingTableList()
        {
            InitializeComponent();
            Instance = this;
            DataContext = ViewModels.ClothingEditingSession.Instance;
            ClothingEditingSession.Instance.OpenClothingRequested += Session_OpenClothingRequested;

            ClothingEditingSession.Instance.ClothingIdChanged += Session_ClothingIdChanged;

            // Subscribe to live-reload events so session/UI update when watched file changes
            CustomTextureStore.ClothingJsonUpdated += OnWatchedClothingJsonUpdated;
        }

        private void OnWatchedClothingJsonUpdated(ACE.DatLoader.FileTypes.ClothingTable updated)
        {
            // Called from threadpool; marshal to UI thread
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var session = ClothingEditingSession.Instance;
                        if (updated == null) return;

                        // Preserve currently active texture overrides (if any) before replacing clothing table
                        var activeTexOverrides = session.ActiveTextureOverrides; // retain reference

                        session.CurrentClothingRaw = updated;

                        OnClickClothingBase(updated, updated.Id, null, null);
                        session.ActivePaletteDefinition = BuildSeedDefinition();
                        session.IsDirty = false;

                        // Re-apply previously active texture overrides (if definition still present)
                        if (activeTexOverrides != null)
                        {
                            session.ActiveTextureOverrides = activeTexOverrides; // restore
                            ApplyTextureOverridesToClothing(updated, activeTexOverrides);
                            LoadModelWithClothingBase(); // refresh viewer
                        }

                        MainWindow?.AddStatusText("Clothing JSON reloaded from watched file.");
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void Session_OpenClothingRequested(ClothingTable clothing)
        {
            if (clothing != null)
            {
                OnClickClothingBase(clothing, clothing.Id, null, null);
                OpenCustomDialog();
                return;
            }

            CurrentClothingItem = null;
            SetupIds.Items.Clear();
            PaletteTemplates.Items.Clear();
            ResetShadesSlider();
            _customActive = false;
            _customCloSubPalettes = null;
            CustomPaletteDialog.ActiveInstance?.RefreshForCurrentClothing(null);
            ClothingStudioWorkflowService.Instance.SelectClothing(null);
        }

        private void ClothingItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ClothingTableVM selected) return;
            if (CurrentClothingItem?.Id == selected.Id) return;
            var session = ClothingEditingSession.Instance;
            if (session.TryGetModel(selected.Id, out var model))
                OnClickClothingBase(model, model.Id, null, null);
        }

        /// <summary>
        /// Applies a CustomTextureDefinition (part/old->new) to a clothing table in-place using reflection on CloTextureEffect.NewTexture.
        /// </summary>
        private void AssignClothingId_Click(object sender, RoutedEventArgs e)
        {
            var session = ClothingEditingSession.Instance;
            if (session.SelectedClothing == null || CurrentClothingItem == null)
            {
                MessageBox.Show("Select or clone a working clothing mod first.", "Assign Clothing ID", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new ClothingIdPickerWindow(session, session.SelectedClothing.Id)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;
            if (!session.TryAssignClothingId(picker.SelectedId, picker.AllowPortalOverride, out var error))
            {
                MessageBox.Show(error, "Assign Clothing ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MainWindow?.AddStatusText($"Clothing mod reassigned to 0x{picker.SelectedId:X8}.");
        }

        private void Session_ClothingIdChanged(uint oldId, uint newId)
        {
            CustomPaletteDialog.ActiveInstance?.HandleClothingIdChanged(oldId, newId);
            MainWindow?.RealtimeJsonSync();
        }

        internal static void ApplyTextureOverridesToClothing(ClothingTable clothing, CustomTextureDefinition overridesDef)
        {
            if (clothing == null || overridesDef == null || overridesDef.Entries == null) return;

            // Build lookup: (partIndex, oldId) -> newId
            var map = overridesDef.Entries
                .GroupBy(e => (e.PartIndex, e.OldId))
                .ToDictionary(g => g.Key, g => g.Last().NewId); // last wins if duplicates

            // Reflection property caches
            var cloTexType = typeof(CloTextureEffect);
            var newTexProp = cloTexType.GetProperty("NewTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var oldTexProp = cloTexType.GetProperty("OldTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var kvp in clothing.ClothingBaseEffects)
            {
                foreach (var obj in kvp.Value.CloObjectEffects)
                {
                    var partIndex = (int)obj.Index;
                    foreach (var tex in obj.CloTextureEffects)
                    {
                        try
                        {
                            // Reader old texture id (respecting backing field accessibility)
                            uint oldId = tex.OldTexture;
                            if (oldTexProp != null)
                            {
                                var val = oldTexProp.GetValue(tex);
                                if (val is uint ov) oldId = ov;
                            }
                            if (map.TryGetValue((partIndex, oldId), out var newId))
                            {
                                if (tex.NewTexture != newId)
                                {
                                    if (newTexProp != null)
                                        newTexProp.SetValue(tex, newId);
                                    else
                                        // fallback if property not found (should not happen in current model)
                                        tex.GetType().GetField("NewTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(tex, newId);
                                }
                            }
                        }
                        catch { /* ignore problematic entry */ }
                    }
                }
            }
        }

        public void OnClickClothingBase(ClothingTable clothing, uint fileID, uint? paletteTemplate = null, float? shade = null)
        {
            CurrentClothingItem = clothing;
            ClothingStudioWorkflowService.Instance.SelectClothing(clothing);
            // Populate / refresh editing session view model (Phase 2 mapping)
            try
            {
                var session = ViewModels.ClothingEditingSession.Instance;
                if (clothing != null)
                {
                    ViewModels.ClothingMapping.AddOrUpdate(session, clothing);
                    // set session raw clothing reference
                    session.CurrentClothingRaw = clothing;
                    // seed active palette for editors
                    session.ActivePaletteDefinition = BuildSeedDefinition();
                    session.ActiveTextureOverrides = null;
                    session.IsDirty = false;
                }
                // Refresh docked palette/texture editor after the selection event unwinds so the
                // clothing list and 3D preview stay responsive while heavy DAT-derived rows rebuild.
                var activePaletteDefinition = session.ActivePaletteDefinition;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(CurrentClothingItem, clothing))
                        CustomPaletteDialog.ActiveInstance?.RefreshForCurrentClothing(activePaletteDefinition);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
            SetupIds.Items.Clear();
            PaletteTemplates.Items.Clear();
            ResetShadesSlider();
            _customActive = false;
            _lastActualPaletteTemplate = null;

            if (CurrentClothingItem?.ClothingBaseEffects == null || CurrentClothingItem.ClothingBaseEffects.Count == 0) return;

            foreach (var cbe in CurrentClothingItem.ClothingBaseEffects.Keys.OrderBy(i => i))
                SetupIds.Items.Add(new ListBoxItem { Content = cbe.ToString("X8"), DataContext = cbe });

            if (CurrentClothingItem.ClothingSubPalEffects.Count == 0) return;

            PaletteTemplates.Items.Add(new ListBoxItem { Content = "None", DataContext = (uint)0 });

            foreach (var subPal in CurrentClothingItem.ClothingSubPalEffects.Keys.OrderBy(i => i))
                PaletteTemplates.Items.Add(new ListBoxItem { Content = (PaletteTemplate)subPal + " - " + subPal, DataContext = subPal });

            PaletteTemplates.Items.Add(new ListBoxItem { Content = "Custom...", DataContext = CustomPaletteKey });
            SetupIds.SelectedIndex = 0;

            if (paletteTemplate == null) PaletteTemplates.SelectedIndex = 0;
            else
            {
                string pal = (PaletteTemplate)paletteTemplate + " - " + paletteTemplate;
                for (var i = 0; i < PaletteTemplates.Items.Count; i++)
                {
                    if (PaletteTemplates.Items[i] is ListBoxItem palItem && palItem.Content.ToString() == pal)
                    {
                        PaletteTemplates.SelectedItem = PaletteTemplates.Items[i];
                        PaletteTemplates.ScrollIntoView(PaletteTemplates.SelectedItem);
                        break;
                    }
                }
            }

            if (shade.HasValue && shade > 0 && Shades.Visibility == Visibility.Visible)
            {
                int palIndex = (int)((Shades.Maximum - 0.000001) * shade);
                Shades.Value = palIndex;
            }
        }

        private void SetupIDs_OnClick(object sender, SelectionChangedEventArgs e)
        {
            if (CurrentClothingItem == null) return;
            LoadModelWithClothingBase();
        }

        private void PaletteTemplates_OnClick(object sender, SelectionChangedEventArgs e)
        {
            ResetShadesSlider();
            if (CurrentClothingItem == null) return;
            if (PaletteTemplates.SelectedItem is not ListBoxItem selectedItem) return;

            uint palTemp = (uint)selectedItem.DataContext;

            // Always update (avoid stale static)
            PaletteTemplate = palTemp;

            if (palTemp == CustomPaletteKey)
            {
                OpenCustomDialog();
                Dispatcher.BeginInvoke(new Action(PromptAndAddCustomPaletteEntry));
                return;
            }

            if (palTemp > 0)
            {
                if (!CurrentClothingItem.ClothingSubPalEffects.ContainsKey(palTemp)) return;
                _lastActualPaletteTemplate = palTemp;

                int maxPals = 0;
                foreach (var sp in CurrentClothingItem.ClothingSubPalEffects[palTemp].CloSubPalettes)
                {
                    var palSetID = sp.PaletteSet;
                    int count = 1;
                    if ((palSetID >> 24) == 0x0F)
                    {
                        try
                        {
                            var palSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(palSetID);
                            count = (palSet.PaletteList?.Count ?? 0) > 0 ? palSet.PaletteList.Count : 1;
                        }
                        catch { count = 1; }
                    }
                    if (count > maxPals) maxPals = count;
                }
                if (maxPals > 1)
                {
                    Shades.Maximum = maxPals - 1;
                    Shades.Visibility = Visibility.Visible;
                    Shades.IsEnabled = true;
                }
            }
            else
            {
                _lastActualPaletteTemplate = null;
            }

            _customActive = false;
            ClothingStudioWorkflowService.Instance.SelectPaletteTemplate(palTemp);
            LoadModelWithClothingBase();
            RefreshDockEditorsIfPresent();
        }

        private List<CloSubPalette> BuildResolvedPalettes(uint paletteTemplate, float shade)
        {
            var list = new List<CloSubPalette>();
            if (paletteTemplate == 0 || CurrentClothingItem == null) return list;
            if (!CurrentClothingItem.ClothingSubPalEffects.TryGetValue(paletteTemplate, out var effect)) return list;

            foreach (var sp in effect.CloSubPalettes)
            {
                uint resolvedPaletteId = sp.PaletteSet;
                // If palette set (0x0Fxxxxxx) pick shade-specific palette
                if ((sp.PaletteSet >> 24) == 0x0F)
                {
                    try
                    {
                        var palSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(sp.PaletteSet);
                        resolvedPaletteId = palSet.GetPaletteID(shade);
                    }
                    catch { /* ignore; retain original id if failure */ }
                }

                // Clone CloSubPalette with resolved palette ID while keeping ranges
                var clone = new CloSubPalette
                {
                    PaletteSet = resolvedPaletteId
                };
                foreach (var r in sp.Ranges)
                    clone.Ranges.Add(new CloSubPaletteRange
                    {
                        Offset = r.Offset,
                        NumColors = r.NumColors
                    });
                list.Add(clone);
            }
            return list;
        }

        private CustomPaletteDefinition BuildSeedDefinition()
        {
            if (!_lastActualPaletteTemplate.HasValue || CurrentClothingItem == null) return null;
            if (!CurrentClothingItem.ClothingSubPalEffects.TryGetValue(_lastActualPaletteTemplate.Value, out var palEffect)) return null;
            var def = new CustomPaletteDefinition { Multi = palEffect.CloSubPalettes.Count > 1, Shade = Shade };
            foreach (var sp in palEffect.CloSubPalettes)
            {
                var entry = new CustomPaletteEntry { PaletteSetId = sp.PaletteSet };
                foreach (var r in sp.Ranges)
                {
                    var groupOffset = r.Offset / 8;
                    var groupLen = r.NumColors / 8;
                    entry.Ranges.Add(new RangeDef { Offset = groupOffset, Length = groupLen });
                }
                def.Entries.Add(entry);
            }
            if (def.Entries.Count == 1) def.Multi = false;
            return def;
        }

        private void PromptAndAddCustomPaletteEntry()
        {
            var seed = _customActive ? BuildSeedDefinitionFromCustom() : BuildSeedDefinition();
            var firstEntry = seed?.Entries?.FirstOrDefault();

            uint defaultPaletteId = firstEntry?.PaletteSetId ?? GetDefaultCustomPaletteId();
            string defaultRanges = FormatRanges(firstEntry?.Ranges);
            float defaultShade = seed?.Shade ?? Shade;

            var window = new CustomPaletteEntryWindow(GetDefaultCustomPaletteTemplate().ToString(CultureInfo.InvariantCulture), HexId.Format(GetDefaultCustomPaletteIcon()), HexId.Format(defaultPaletteId), defaultRanges, defaultShade)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true)
                return;

            var editor = CustomPaletteDialog.ActiveInstance;
            if (editor == null)
            {
                MainWindow?.AddStatusText("Clothing Studio panel was not ready for the new custom palette.");
                return;
            }

            editor.AddPaletteEntryFromMenu(window.PaletteTemplate, window.IconId, window.PaletteId, window.Ranges, window.Shade);
            var paletteDefinition = BuildSinglePaletteDefinition(window.PaletteId, window.Ranges, window.Shade);
            ApplyPaletteTemplateDefinition(window.PaletteTemplate, window.IconId, paletteDefinition);
            PaletteTemplate = window.PaletteTemplate;
            ClothingStudioWorkflowService.Instance.SelectPaletteTemplate(window.PaletteTemplate, paletteDefinition);
            MainWindow?.AddStatusText($"Pal #{window.PaletteTemplate} added with palette 0x{window.PaletteId:X8}.");
        }

        private uint GetDefaultCustomPaletteId()
        {
            if (_customCloSubPalettes?.Count > 0)
            {
                var existing = _customCloSubPalettes.FirstOrDefault(sp => sp.PaletteSet != 0)?.PaletteSet ?? 0;
                if (existing != 0)
                    return existing;
            }

            var seed = BuildSeedDefinition();
            var seedPalette = seed?.Entries?.FirstOrDefault(e => e.PaletteSetId != 0)?.PaletteSetId ?? 0;
            if (seedPalette != 0)
                return seedPalette;

            if (CurrentClothingItem != null)
            {
                foreach (var sp in CurrentClothingItem.ClothingSubPalEffects.Values.SelectMany(e => e.CloSubPalettes))
                {
                    if (sp.PaletteSet != 0)
                        return sp.PaletteSet;
                }
            }

            return 0x0400007E;
        }

        private static string FormatRanges(IEnumerable<RangeDef> ranges)
        {
            if (ranges == null)
                return "0:1";

            var text = string.Join(",", ranges.Select(r => $"{r.Offset}:{r.Length}"));
            return string.IsNullOrWhiteSpace(text) ? "0:1" : text;
        }
        private uint GetDefaultCustomPaletteTemplate()
        {
            if (CurrentClothingItem == null)
                return 1;

            var preferred = GetPreferredPaletteTemplate();
            var start = preferred == 0 || preferred == CustomPaletteKey ? 1 : preferred + 1;
            if (start == 0) start = 1;
            for (var id = start; id < uint.MaxValue; id++)
            {
                if (id != 0 && !CurrentClothingItem.ClothingSubPalEffects.ContainsKey(id))
                    return id;
            }
            return start;
        }

        private uint GetDefaultCustomPaletteIcon()
        {
            var preferred = GetPreferredPaletteTemplate();
            return preferred == 0 || preferred == CustomPaletteKey ? 0 : GetPaletteIcon(preferred);
        }

        private static CustomPaletteDefinition BuildSinglePaletteDefinition(uint paletteId, IReadOnlyList<RangeDef> ranges, float shade)
        {
            return new CustomPaletteDefinition
            {
                Multi = false,
                Shade = shade,
                Entries = new List<CustomPaletteEntry>
                {
                    new() { PaletteSetId = paletteId, Ranges = ranges?.Select(r => new RangeDef { Offset = r.Offset, Length = r.Length }).ToList() ?? new List<RangeDef>() }
                }
            };
        }

        private void ApplyPaletteTemplateDefinition(uint paletteTemplate, uint iconId, CustomPaletteDefinition definition)
        {
            if (CurrentClothingItem == null || definition == null || paletteTemplate == 0)
                return;

            var effect = new CloSubPalEffect();
            var iconProp = typeof(CloSubPalEffect).GetProperty(nameof(CloSubPalEffect.Icon), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            try { iconProp?.SetValue(effect, iconId); } catch { }

            foreach (var subPalette in CustomPaletteFactory.Build(definition))
            {
                var clone = new CloSubPalette { PaletteSet = subPalette.PaletteSet };
                foreach (var range in subPalette.Ranges)
                    clone.Ranges.Add(new CloSubPaletteRange { Offset = range.Offset, NumColors = range.NumColors });
                effect.CloSubPalettes.Add(clone);
            }

            if (effect.CloSubPalettes.Count == 0)
                return;

            CurrentClothingItem.ClothingSubPalEffects[paletteTemplate] = effect;
            _lastActualPaletteTemplate = paletteTemplate;
            _customActive = false;
            _customCloSubPalettes = null;
            ClothingEditingSession.Instance.ActivePaletteDefinition = definition;
            ClothingEditingSession.Instance.IsDirty = true;
            ClothingStudioWorkflowService.Instance.SelectPaletteTemplate(paletteTemplate, definition);
            RefreshPaletteTemplateListSelection(paletteTemplate);
            LoadModelWithClothingBase();
            RefreshDockEditorsIfPresent();
            MainWindow.Instance?.RealtimeJsonSync();
        }

        private void RefreshPaletteTemplateListSelection(uint paletteTemplate)
        {
            if (PaletteTemplates == null)
                return;

            ListBoxItem target = null;
            foreach (var item in PaletteTemplates.Items.OfType<ListBoxItem>())
            {
                if (item.DataContext is uint id && id == paletteTemplate)
                {
                    target = item;
                    break;
                }
            }

            if (target == null)
            {
                target = new ListBoxItem { Content = $"{(PaletteTemplate)paletteTemplate} - {paletteTemplate}", DataContext = paletteTemplate };
                var insertIndex = Math.Max(1, PaletteTemplates.Items.Count - 1);
                PaletteTemplates.Items.Insert(insertIndex, target);
            }

            PaletteTemplates.SelectedItem = target;
            PaletteTemplates.ScrollIntoView(target);
        }
        public void LoadModelWithClothingBase(bool resetCamera = true)
        {
            if (CurrentClothingItem == null || SetupIds.SelectedIndex == -1 || PaletteTemplates.SelectedIndex == -1)
                return;

            GameView.ViewMode = ACViewer.Enum.ViewMode.Model;

            // DAT-backed browser events can run before GameView.PostInit creates the
            // model renderer. Keep the editor/session selection and render it when
            // PostInit calls this method again.
            if (ModelViewer == null)
            {
                MainWindow?.AddStatusText("Model renderer is still initializing; clothing preview is queued.");
                return;
            }

            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            ClothingStudioWorkflowService.Instance.SelectSetup(setupId);
            float shade = 0;

            if (Shades.Visibility == Visibility.Visible)
            {
                shade = (float)(Shades.Value / Shades.Maximum);
                if (float.IsNaN(shade)) shade = 0;
            }

            Shade = shade;
            lblShade.Visibility = Shades.Visibility;
            lblShade.Content = "Shade: " + shade;

            try
            {
                if (_customActive && _customCloSubPalettes != null)
                {
                    ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _customCloSubPalettes, _customShade, resetCamera);
                    MainWindow?.AddStatusText($"Previewing custom clothing 0x{CurrentClothingItem.Id:X8} on setup 0x{setupId:X8}.");
                    return;
                }

                if (PaletteTemplate > 0)
                {
                    _resolvedStandardPalettes = BuildResolvedPalettes(PaletteTemplate, shade);
                    ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _resolvedStandardPalettes, shade, resetCamera);
                }
                else
                {
                    ModelViewer.LoadModel(setupId, CurrentClothingItem, (PaletteTemplate)0, shade, resetCamera);
                    _resolvedStandardPalettes = null;
                }

                ClothingStudioWorkflowService.Instance.Validate(CurrentClothingItem);
                MainWindow?.AddStatusText($"Previewing clothing 0x{CurrentClothingItem.Id:X8} on setup 0x{setupId:X8}.");
            }
            catch (Exception ex)
            {
                MainWindow?.AddStatusText($"Failed to render clothing 0x{CurrentClothingItem.Id:X8} on setup 0x{setupId:X8}: {ex.Message}");
            }
        }
        private void Shades_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Shades.Visibility == Visibility.Hidden) return;
            if (_customActive) return;
            LoadModelWithClothingBase();
            RefreshDockEditorsIfPresent();
        }

        private void RefreshDockEditorsIfPresent()
        {
            var mw = View.MainWindow.Instance;
            if (mw?.DockManager == null) return;

            var layout = mw.DockManager.Layout;
            var paletteDock = layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorable>()
                .FirstOrDefault(a => a.ContentId == "CustomPaletteDock");
            var textureDock = layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorable>()
                .FirstOrDefault(a => a.ContentId == "TextureOverridesDock");

            if (paletteDock == null && textureDock == null) return;

            // Build fresh definition (non-destructive). For standard selection we seed from original template.
            CustomPaletteDefinition def = _customActive
                ? BuildSeedDefinitionFromCustom()
                : BuildSeedDefinition();

            if (def == null) return;

            if (paletteDock?.Content is ICustomPaletteHost host)
            {
                host.LoadDefinition(def, isLive: false);
            }
            // Push highlighted palette/ranges directly to JSON editor when a definition is loaded
            try { MainWindow?.AddStatusText("Synced palette selection to JSON editor (preview)"); } catch { }

            if (textureDock?.Content is ITextureOverrideHost texHost)
            {
                texHost.UpdateFromPalette(def);
            }
        }

        private CustomPaletteDefinition BuildSeedDefinitionFromCustom()
        {
            if (!_customActive || _customCloSubPalettes == null) return null;
            var def = new CustomPaletteDefinition
            {
                Multi = _customCloSubPalettes.Count > 1,
                Shade = _customShade
            };
            foreach (var sp in _customCloSubPalettes)
            {
                var entry = new CustomPaletteEntry { PaletteSetId = sp.PaletteSet };
                foreach (var r in sp.Ranges)
                {
                    var groupOffset = r.Offset / 8;
                    var groupLen = r.NumColors / 8;
                    entry.Ranges.Add(new RangeDef { Offset = groupOffset, Length = groupLen });
                }
                def.Entries.Add(entry);
            }
            if (def.Entries.Count == 1) def.Multi = false;
            return def;
        }

        internal static List<uint> BuildAvailablePaletteIdList()
        {
            if (DatManager.PortalDat == null)
                return new List<uint>();

            var result = new HashSet<uint>();
            uint requiredMaxColorIndex = 0; // exclusive upper bound in raw palette colors

            if (CurrentClothingItem != null && CurrentClothingItem.ClothingSubPalEffects.Count > 0)
            {
                foreach (var sp in CurrentClothingItem.ClothingSubPalEffects.SelectMany(kvp => kvp.Value.CloSubPalettes))
                {
                    if (sp.PaletteSet != 0)
                        result.Add(sp.PaletteSet);

                    foreach (var range in sp.Ranges)
                    {
                        var end = range.Offset + range.NumColors;
                        if (end > requiredMaxColorIndex)
                            requiredMaxColorIndex = end;
                    }
                }
            }

            if (requiredMaxColorIndex == 0)
            {
                return DatIdIndex.PaletteAndPaletteSetIds().ToList();
            }

            foreach (var id in DatIdIndex.PaletteAndPaletteSetIds())
            {
                if (DatIdIndex.PaletteOrSetCovers(id, requiredMaxColorIndex))
                    result.Add(id);
            }

            return result.OrderBy(id => id).ToList();
        }
        public void OpenCustomDialog()
        {
            var mw = View.MainWindow.Instance;
            if (mw?.DockManager == null)
            {
                MainWindow?.AddStatusText("DockManager not ready - cannot open Custom Palette panel.");
                return;
            }

            try
            {
                var layout = mw.DockManager.Layout;
                var existing = layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorable>()
                    .FirstOrDefault(a => a.ContentId == "CustomPaletteDock");

                // Build definition now (maybe null)
                CustomPaletteDefinition currentDef = _customActive ? BuildSeedDefinitionFromCustom() : BuildSeedDefinition();

                if (existing != null)
                {
                    existing.IsActive = true;
                    existing.IsVisible = true;
                    if (currentDef != null && existing.Content is ICustomPaletteHost host1)
                        host1.LoadDefinition(currentDef, isLive: false);
                    return;
                }

                var panes = layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorablePane>().ToList();
                var targetPane = panes
                    .FirstOrDefault(p => !p.Children.Any(c => c.ContentId == "Output" || c.ContentId == "JsonEditor" || c.ContentId == "Explorer"))
                    ?? panes.FirstOrDefault(p => !p.Children.Any(c => c.ContentId == "Output" || c.ContentId == "JsonEditor"))
                    ?? panes.FirstOrDefault();

                if (targetPane == null)
                {
                    MainWindow?.AddStatusText("No suitable pane found for Custom Palette panel.");
                    return; // No popup fallback
                }

                // Create a fresh dialog instance each open to avoid re-parenting issues
                var dlg = new CustomPaletteDialog();

                var anchor = new AvalonDock.Layout.LayoutAnchorable
                {
                    Title = "Clothing Studio",
                    ContentId = "CustomPaletteDock",
                    Content = dlg,
                    CanClose = true,
                    CanHide = true
                };
                targetPane.Children.Add(anchor);
                anchor.IsActive = true;

                if (currentDef != null && dlg is ICustomPaletteHost host)
                    host.LoadDefinition(currentDef, isLive: false);
                else if (currentDef == null && CurrentClothingItem != null)
                {
                    // Defer definition if not yet available
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var defLater = _customActive ? BuildSeedDefinitionFromCustom() : BuildSeedDefinition();
                        if (defLater != null && anchor.Content is ICustomPaletteHost hostLater)
                            hostLater.LoadDefinition(defLater, isLive: false);
                    }));
                }
            }
            catch (Exception ex)
            {
                MainWindow?.AddStatusText($"Failed to open Custom Palette panel: {ex.Message}");
            }
        }

        public void OpenPaletteAndTextureEditors() => OpenCustomDialog();

        // ---------------- Added helper methods ----------------

        private void ResetShadesSlider()
        {
            Shades.Visibility = Visibility.Hidden;
            Shades.IsEnabled = false;
            Shades.Value = 0;
            lblShade.Visibility = Visibility.Hidden;
        }

        private void LiveUpdateCustom(CustomPaletteDefinition def)
        {
            if (def == null || CurrentClothingItem == null || SetupIds.SelectedIndex < 0) return;
            var list = new List<CloSubPalette>();
            foreach (var entry in def.Entries)
            {
                var sp = new CloSubPalette { PaletteSet = entry.PaletteSetId };
                foreach (var r in entry.Ranges)
                {
                    sp.Ranges.Add(new CloSubPaletteRange { Offset = r.Offset * 8, NumColors = r.Length * 8 });
                }
                list.Add(sp);
            }
            _customCloSubPalettes = list;
            _customShade = def.Shade;
            _customActive = true;
            GameView.ViewMode = ACViewer.Enum.ViewMode.Model;
            if (ModelViewer == null) return;
            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            ClothingStudioWorkflowService.Instance.SelectSetup(setupId);
            ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _customCloSubPalettes, _customShade, resetCamera: false);
            MainWindow.Instance?.RealtimeJsonSync();
        }

        public void ApplyPalettePreviewDefinition(CustomPaletteDefinition def)
        {
            // Apply without toggling permanent custom state (preview only)
            if (def == null || CurrentClothingItem == null || SetupIds.SelectedIndex < 0) return;
            var list = new List<CloSubPalette>();
            foreach (var entry in def.Entries)
            {
                var sp = new CloSubPalette { PaletteSet = entry.PaletteSetId };
                foreach (var r in entry.Ranges)
                    sp.Ranges.Add(new CloSubPaletteRange { Offset = r.Offset * 8, NumColors = r.Length * 8 });
                list.Add(sp);
            }
            GameView.ViewMode = ACViewer.Enum.ViewMode.Model;
            if (ModelViewer == null) return;
            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            ClothingStudioWorkflowService.Instance.SelectSetup(setupId);
            ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, list, def.Shade);
            MainWindow.Instance?.RealtimeJsonSync();
        }

        public void ForceOpenPaletteEditorAfterImport()
        {
            // Open editor window to allow immediate editing of imported item
            OpenPaletteAndTextureEditors();
        }

        public static List<VirindiColorInfo> GetVirindiColorToolInfo()
        {
            // Minimal placeholder: return empty list if no clothing loaded.
            // Extend later with actual palette slot extraction logic as needed.
            var list = new List<VirindiColorInfo>();
            if (CurrentClothingItem == null) return list;
            // Attempt to build simple color info from first palette definition if available
            try
            {
                var palTemplate = CurrentClothingItem.ClothingSubPalEffects.Keys.FirstOrDefault();
                if (palTemplate != 0 && CurrentClothingItem.ClothingSubPalEffects.TryGetValue(palTemplate, out var effect))
                {
                    foreach (var sp in effect.CloSubPalettes)
                    {
                        // For a palette set, fetch first palette; for raw palette use directly
                        uint palId = sp.PaletteSet;
                        if ((palId >> 24) == 0x0F)
                        {
                            var set = DatManager.PortalDat.ReadFromDat<PaletteSet>(palId);
                            if (set?.PaletteList?.Count > 0) palId = set.PaletteList[0];
                        }
                        var pal = DatManager.PortalDat.ReadFromDat<Palette>(palId);
                        if (pal?.Colors?.Count > 0)
                        {
                            // Use first color as representative
                            list.Add(new VirindiColorInfo { PalId = palId, Color = pal.Colors[0] & 0xFFFFFF });
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static uint GetIcon() => Icon;

        private void BtnImportJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Clothing JSON (*.json)|*.json", Title = "Import Clothing JSON" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var imported = CustomTextureStore.ImportClothingTable(dlg.FileName);
                if (imported == null)
                {
                    MainWindow.Instance.AddStatusText("Import failed: empty file");
                    return;
                }
                OnClickClothingBase(imported, imported.Id, null, null);
                ForceOpenPaletteEditorAfterImport();
                MainWindow.Instance.AddStatusText($"Imported clothing JSON: {System.IO.Path.GetFileName(dlg.FileName)}");
                _lastImportedJsonPath = dlg.FileName;
                CustomTextureStore.WatchClothingJson(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var session = ClothingEditingSession.Instance;
            switch (e.NewValue)
            {
                case SubPaletteEffectVM spe:
                    session.SelectedSubPaletteEffect = spe;
                    session.SelectedCloSubPalette = null;
                    break;
                case CloSubPaletteVM csp:
                    session.SelectedCloSubPalette = csp;
                    // also set parent effect for context operations
                    var root = session.SelectedClothing?.BaseEffects.FirstOrDefault(b => b.BaseId == 0xFFFFFFFF);
                    if (root != null)
                    {
                        foreach (var spe2 in root.SubPaletteEffects)
                            if (spe2.CloSubPalettes.Contains(csp)) { session.SelectedSubPaletteEffect = spe2; break; }
                    }
                    break;
                default:
                    session.SelectedSubPaletteEffect = null;
                    session.SelectedCloSubPalette = null;
                    break;
            }
        }

        internal static uint GetPreferredPaletteTemplate()
        {
            if (_lastActualPaletteTemplate.HasValue && _lastActualPaletteTemplate.Value != 0)
                return _lastActualPaletteTemplate.Value;
            if (PaletteTemplate != 0 && PaletteTemplate != CustomPaletteKey)
                return PaletteTemplate;
            return CurrentClothingItem?.ClothingSubPalEffects.Keys.OrderBy(key => key).FirstOrDefault() ?? 1u;
        }

        internal static uint GetPaletteIcon(uint paletteTemplate)
        {
            if (CurrentClothingItem?.ClothingSubPalEffects.TryGetValue(paletteTemplate, out var effect) == true)
                return effect.Icon;
            return 0;
        }

        internal void ApplyLivePaletteDefinition(CustomPaletteDefinition def)
        {
            if (def == null || CurrentClothingItem == null || SetupIds.SelectedIndex < 0) return;
            // Convert definition to CloSubPalettes
            try
            {
                var list = new List<CloSubPalette>();
                foreach (var entry in def.Entries)
                {
                    var sp = new CloSubPalette { PaletteSet = entry.PaletteSetId };
                    foreach (var r in entry.Ranges)
                        sp.Ranges.Add(new CloSubPaletteRange { Offset = r.Offset * 8, NumColors = r.Length * 8 });
                    if (sp.Ranges.Count > 0) list.Add(sp);
                }
                if (list.Count == 0) return;
                _customCloSubPalettes = list;
                _customShade = def.Shade;
                _customActive = true; // ensure model uses custom path
                ClothingStudioWorkflowService.Instance.SelectPaletteTemplate(CustomPaletteKey, def);
                GameView.ViewMode = ACViewer.Enum.ViewMode.Model;
                if (ModelViewer == null) return;
                var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            ClothingStudioWorkflowService.Instance.SelectSetup(setupId);
                ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _customCloSubPalettes, _customShade, resetCamera: false);
                MainWindow.Instance?.RealtimeJsonSync();
                ClothingEditingSession.Instance.ActivePaletteDefinition = def;
            }
            catch { }
        }

        internal CustomPaletteDefinition GetCurrentSeedDefinition()
        {
            try { return BuildSeedDefinition(); } catch { return null; }
        }
    }
}
