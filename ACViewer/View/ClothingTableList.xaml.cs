using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACViewer.CustomPalettes;
using ACViewer.CustomTextures;
using AvalonDock.Layout;
using ACViewer.Model; // for CloSubPalette definitions
using ACViewer.ViewModels;

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
        }

        public void OnClickClothingBase(ClothingTable clothing, uint fileID, uint? paletteTemplate = null, float? shade = null)
        {
            CurrentClothingItem = clothing;
            // Populate / refresh editing session view model (Phase 2 mapping)
            try
            {
                var session = ViewModels.ClothingEditingSession.Instance;
                if (clothing != null)
                {
                    ViewModels.ClothingMapping.AddOrUpdate(session, clothing);
                }
                // enable palette dialog if open
                CustomPaletteDialog.ActiveInstance?.SetHasClothing(clothing != null);
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

            if (paletteTemplate == null) PaletteTemplates.SelectedIndex = 0; else
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

        public void LoadModelWithClothingBase()
        {
            if (CurrentClothingItem == null || SetupIds.SelectedIndex == -1 || PaletteTemplates.SelectedIndex == -1)
                return;

            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            float shade = 0;

            if (Shades.Visibility == Visibility.Visible)
            {
                shade = (float)(Shades.Value / Shades.Maximum);
                if (float.IsNaN(shade)) shade = 0;
            }

            Shade = shade;
            lblShade.Visibility = Shades.Visibility;
            lblShade.Content = "Shade: " + shade;

            // If custom active, use custom path
            if (_customActive && _customCloSubPalettes != null)
            {
                ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _customCloSubPalettes, _customShade);
                return;
            }

            // Standard path unified via resolved palettes (unless "None")
            if (PaletteTemplate > 0)
            {
                _resolvedStandardPalettes = BuildResolvedPalettes(PaletteTemplate, shade);
                ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _resolvedStandardPalettes, shade);
            }
            else
            {
                // No palette template selected ("None") -> fall back to vanilla
                ModelViewer.LoadModel(setupId, CurrentClothingItem, (PaletteTemplate)0, shade);
                _resolvedStandardPalettes = null;
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

        private List<uint> BuildAvailablePaletteIdList()
        {
            // Collect referenced palette sets from clothing item (0x0Fxxxxxx)
            List<uint> referencedSets = null;
            uint requiredMaxColorIndex = 0; // exclusive upper bound (Offset + NumColors)

            if (CurrentClothingItem != null && CurrentClothingItem.ClothingSubPalEffects.Count > 0)
            {
                referencedSets = CurrentClothingItem.ClothingSubPalEffects
                    .SelectMany(kvp => kvp.Value.CloSubPalettes)
                    .Select(sp => sp.PaletteSet)
                    .Where(id => (id >> 24) == 0x0F)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                // Determine maximum color span required by the clothing's ranges so we can validate raw palettes (0x04)
                foreach (var sp in CurrentClothingItem.ClothingSubPalEffects.SelectMany(k => k.Value.CloSubPalettes))
                {
                    foreach (var r in sp.Ranges)
                    {
                        var end = r.Offset + r.NumColors; // ranges are expressed in raw color units already
                        if (end > requiredMaxColorIndex)
                            requiredMaxColorIndex = end;
                    }
                }
            }

            // If we have required color span, attempt to add compatible standalone palettes (0x04xxxxxx)
            var result = new List<uint>();
            if (referencedSets != null && referencedSets.Count > 0)
                result.AddRange(referencedSets);

            // Add individual palettes that are large enough to cover the required ranges
            if (requiredMaxColorIndex > 0)
            {
                foreach (var id in DatManager.PortalDat.AllFiles.Keys)
                {
                    if ((id >> 24) != 0x04) continue; // only raw palettes
                    try
                    {
                        var pal = DatManager.PortalDat.ReadFromDat<Palette>(id);
                        if (pal?.Colors?.Count >= requiredMaxColorIndex)
                            result.Add(id);
                    }
                    catch { /* ignore unreadable palette */ }
                }
            }

            if (result.Count > 0)
                return result.Distinct().OrderBy(i => i).ToList();

            // Fallback: Full list of palette sets (0x0F) and palettes (0x04)
            return DatManager.PortalDat.AllFiles.Keys
                .Where(id => (id >> 24) == 0x04 || (id >> 24) == 0x0F)
                .OrderBy(id => id)
                .ToList();
        }

        public void OpenCustomDialog()
        {
            var mw = View.MainWindow.Instance;
            if (mw?.DockManager == null)
            {
                MainWindow?.AddStatusText("DockManager not ready - cannot open Custom Palette panel.");
                return;
            }

            var dlg = new CustomPaletteDialog
            {
                StartingDefinition = BuildSeedDefinition(),
                AvailablePaletteIDs = BuildAvailablePaletteIdList(),
                OnLiveUpdate = LiveUpdateCustom
            };

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
                    if (currentDef != null && existing.Content is ICustomPaletteHost host)
                        host.LoadDefinition(currentDef, isLive: false);

                    // Refresh texture dock if exists
                    var texDock2 = layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorable>()
                        .FirstOrDefault(a => a.ContentId == "TextureOverridesDock");
                    if (currentDef != null && texDock2?.Content is ITextureOverrideHost texHost2)
                        texHost2.UpdateFromPalette(currentDef);
                    return;
                }

                var targetPane = layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorablePane>()
                    .FirstOrDefault(p => p.Children.Any(c => c.ContentId == "JsonEditor"))
                    ?? layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorablePane>().FirstOrDefault();

                if (targetPane == null)
                {
                    MainWindow?.AddStatusText("No suitable pane found for Custom Palette panel.");
                    return; // No popup fallback
                }

                dlg.InitializeForDock();
                var parts = dlg.GetDockParts();
                if (parts.palette is UIElement palContent)
                {
                    var anchor = new AvalonDock.Layout.LayoutAnchorable
                    {
                        Title = "Custom Palette",
                        ContentId = "CustomPaletteDock",
                        Content = palContent,
                        CanClose = true,
                        CanHide = true
                    };
                    targetPane.Children.Add(anchor);
                    anchor.IsActive = true;
                    if (currentDef != null && palContent is ICustomPaletteHost host)
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
                if (parts.textures is UIElement texContent)
                {
                    var texAnchor = new AvalonDock.Layout.LayoutAnchorable
                    {
                        Title = "Texture Overrides",
                        ContentId = "TextureOverridesDock",
                        Content = texContent,
                        CanClose = true,
                        CanHide = true
                    };
                    targetPane.Children.Add(texAnchor);
                    if (currentDef != null && texContent is ITextureOverrideHost texHost)
                        texHost.UpdateFromPalette(currentDef);
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
            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _customCloSubPalettes, _customShade);
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
            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, list, def.Shade);
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
    }
}
