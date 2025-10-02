using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACViewer.CustomPalettes; // added

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

        public ClothingTableList()
        {
            InitializeComponent();
            Instance = this;
            DataContext = this;
        }

        public void OnClickClothingBase(ClothingTable clothing, uint fileID, uint? paletteTemplate = null, float? shade = null)
        {
            CurrentClothingItem = clothing;
            SetupIds.Items.Clear();
            PaletteTemplates.Items.Clear();
            ResetShadesSlider();
            _customActive = false;
            _lastActualPaletteTemplate = null;

            if (CurrentClothingItem.ClothingBaseEffects.Count == 0) return;

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

            if (palTemp == CustomPaletteKey) { OpenCustomDialog(); return; }

            if (palTemp > 0)
            {
                if (!CurrentClothingItem.ClothingSubPalEffects.ContainsKey(palTemp)) return;
                PaletteTemplate = palTemp; _lastActualPaletteTemplate = palTemp;
                int maxPals = 0;
                foreach (var sp in CurrentClothingItem.ClothingSubPalEffects[palTemp].CloSubPalettes)
                {
                    var palSetID = sp.PaletteSet;
                    var clothing = DatManager.PortalDat.ReadFromDat<PaletteSet>(palSetID);
                    if (clothing.PaletteList.Count > maxPals) maxPals = clothing.PaletteList.Count;
                }
                if (maxPals > 1)
                {
                    Shades.Maximum = maxPals - 1;
                    Shades.Visibility = Visibility.Visible;
                    Shades.IsEnabled = true;
                    MainWindow.Status.WriteLine($"Reading PaletteSets and found {maxPals} Shade options");
                }
            }
            else _lastActualPaletteTemplate = null;

            _customActive = false;
            LoadModelWithClothingBase();
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
                    // Source ranges are stored in raw color units (Offset / NumColors). Convert to logical groups (8 colors per group)
                    var groupOffset = r.Offset / 8; // int division ok; data should align to 8
                    var groupLen = r.NumColors / 8;
                    entry.Ranges.Add(new RangeDef { Offset = groupOffset, Length = groupLen });
                }
                def.Entries.Add(entry);
            }
            if (def.Entries.Count == 1) def.Multi = false; return def;
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

        private void OpenCustomDialog()
        {
            var dlg = new CustomPaletteDialog
            {
                Owner = MainWindow,
                StartingDefinition = BuildSeedDefinition(),
                AvailablePaletteIDs = BuildAvailablePaletteIdList(),
                OnLiveUpdate = LiveUpdateCustom
            };
            var result = dlg.ShowDialog();
            if (result == true && dlg.ResultDefinition != null)
            {
                try { ApplyCustomDefinition(dlg.ResultDefinition); }
                catch (Exception ex)
                { _customActive = false; MainWindow.Status.WriteLine($"Failed applying custom palette: {ex.Message}"); PaletteTemplates.SelectedIndex = 0; }
            }
            else PaletteTemplates.SelectedIndex = 0;
        }

        private void ApplyCustomDefinition(CustomPaletteDefinition def)
        {
            _customCloSubPalettes = CustomPaletteFactory.Build(def);
            _customShade = def.Shade; _customActive = true; Shade = _customShade;
            lblShade.Visibility = Visibility.Visible; lblShade.Content = $"Shade: {_customShade:0.###}";
            Shades.Visibility = Visibility.Hidden; Shades.IsEnabled = false;
            LoadModelWithClothingBase();
            MainWindow.Status.WriteLine("Applied custom palette definition");
        }

        private void LiveUpdateCustom(CustomPaletteDefinition def)
        {
            try
            {
                _customCloSubPalettes = CustomPaletteFactory.Build(def);
                _customShade = def.Shade; _customActive = true; Shade = _customShade;
                if (SetupIds.SelectedItem is ListBoxItem item)
                    ModelViewer.LoadModelCustom((uint)item.DataContext, CurrentClothingItem, _customCloSubPalettes, _customShade);
            }
            catch { }
        }

        private void ResetShadesSlider()
        {
            Shades.Visibility = Visibility.Hidden; Shades.IsEnabled = false; Shades.Value = 0; Shades.Maximum = 1; Shade = 0;
        }

        public void LoadModelWithClothingBase()
        {
            if (CurrentClothingItem == null || SetupIds.SelectedIndex == -1 || PaletteTemplates.SelectedIndex == -1) return;
            var setupId = (uint)((ListBoxItem)SetupIds.SelectedItem).DataContext;
            if (_customActive)
            { ModelViewer.LoadModelCustom(setupId, CurrentClothingItem, _customCloSubPalettes, _customShade); return; }
            float shade = 0;
            if (Shades.Visibility == Visibility.Visible)
            { shade = (float)(Shades.Value / Shades.Maximum); if (float.IsNaN(shade)) shade = 0; }
            Shade = shade; lblShade.Visibility = Shades.Visibility; lblShade.Content = "Shade: " + shade.ToString();
            var paletteTemplate = (PaletteTemplate)(uint)((ListBoxItem)PaletteTemplates.SelectedItem).DataContext;
            ModelViewer.LoadModel(setupId, CurrentClothingItem, paletteTemplate, shade);
        }

        private void Shades_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Shades.Visibility == Visibility.Hidden) return; if (_customActive) return; LoadModelWithClothingBase();
        }

        public class VctInfo { public uint PalId; public uint Color; }

        public static List<VctInfo> GetVirindiColorToolInfo()
        {
            var result = new List<VctInfo>(); if (CurrentClothingItem == null) return result;
            if (_customActive && _customCloSubPalettes != null)
            {
                foreach (var subPal in _customCloSubPalettes)
                {
                    uint paletteID = subPal.PaletteSet;
                    foreach (var r in subPal.Ranges)
                    {
                        uint mid = Convert.ToUInt32(r.NumColors / 2); uint colorIdx = r.Offset + mid; uint color = 0;
                        if (paletteID >> 24 == 0xF)
                        { var palSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(paletteID); var palId = palSet.GetPaletteID(_customShade); var palette = DatManager.PortalDat.ReadFromDat<Palette>(palId); if (palette.Colors.Count >= colorIdx) color = palette.Colors[(int)colorIdx] & 0xFFFFFF; result.Add(new VctInfo { PalId = palId & 0xFFFF, Color = color }); }
                        else { var palette = DatManager.PortalDat.ReadFromDat<Palette>(paletteID); if (palette.Colors.Count >= colorIdx) color = palette.Colors[(int)colorIdx] & 0xFFFFFF; result.Add(new VctInfo { PalId = paletteID & 0xFFFF, Color = color }); }
                    }
                }
                return result;
            }
            if (CurrentClothingItem.ClothingSubPalEffects.Count == 0) return result; if (!CurrentClothingItem.ClothingSubPalEffects.ContainsKey(PaletteTemplate)) return result; if (float.IsNaN(Shade)) Shade = 0;
            Icon = CurrentClothingItem.GetIcon(PaletteTemplate); var palEffects = CurrentClothingItem.ClothingSubPalEffects[PaletteTemplate];
            foreach (var subPal in palEffects.CloSubPalettes)
            { var palSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(subPal.PaletteSet); var paletteID = palSet.GetPaletteID(Shade); var palette = DatManager.PortalDat.ReadFromDat<Palette>(paletteID); foreach (var r in subPal.Ranges) { uint mid = Convert.ToUInt32(r.NumColors / 2); uint colorIdx = r.Offset + mid; uint color = 0; if (palette.Colors.Count >= colorIdx) color = palette.Colors[(int)colorIdx]; result.Add(new VctInfo { PalId = paletteID & 0xFFFF, Color = color & 0xFFFFFF }); } }
            return result;
        }

        public static uint GetIcon() => CurrentClothingItem.GetIcon(PaletteTemplate);
    }
}
