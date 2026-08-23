using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ACE.DatLoader; // for DatManager
using ACE.DatLoader.FileTypes; // for future expansion
using ACViewer.CustomPalettes; // for RangeDef reuse if desired
using ACE.DatLoader.Entity; // clothing table types live here
using ACViewer.CustomTextures;
using ACViewer.ClothingStudio;

namespace ACViewer.ViewModels
{
    /// <summary>
    /// Root editing session for integrated clothing + palette editing.
    /// Step 1 scaffold: holds collections & ID allocation. Mapping + commands added later.
    /// </summary>
    public sealed class ClothingEditingSession : INotifyPropertyChanged
    {
        public static ClothingEditingSession Instance { get; } = new();

        private ClothingEditingSession() { }

        public ObservableCollection<ClothingTableVM> ClothingItems { get; } = new();
        public ObservableCollection<SetupVM> Setups { get; } = new();
        public ClothingStudioWorkflowService Workflow { get; } = ClothingStudioWorkflowService.Instance;
        private readonly Dictionary<uint, ClothingTable> _models = new();

        public event Action<ClothingTable> OpenClothingRequested;
        public event Action<uint, uint> ClothingIdChanged;


        // Single source-of-truth properties
        private ACE.DatLoader.FileTypes.ClothingTable _currentClothingRaw;
        public ACE.DatLoader.FileTypes.ClothingTable CurrentClothingRaw
        {
            get => _currentClothingRaw;
            set
            {
                if (!Equals(_currentClothingRaw, value))
                {
                    _currentClothingRaw = value;
                    OnPropertyChanged();
                    // Clear some dependent state when switching clothing
                    ActivePaletteDefinition = null;
                    ActiveTextureOverrides = null;
                    IsDirty = false;
                    Workflow.SelectClothing(_currentClothingRaw);
                }
            }
        }

        private CustomPaletteDefinition _activePaletteDefinition;
        public CustomPaletteDefinition ActivePaletteDefinition
        {
            get => _activePaletteDefinition;
            set { if (_activePaletteDefinition != value) { _activePaletteDefinition = value; OnPropertyChanged(); } }
        }

        private CustomTextureDefinition _activeTextureOverrides;
        public CustomTextureDefinition ActiveTextureOverrides
        {
            get => _activeTextureOverrides;
            set { if (_activeTextureOverrides != value) { _activeTextureOverrides = value; OnPropertyChanged(); } }
        }

        private bool _isDirty;
        public bool IsDirty { get => _isDirty; set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); } } }

        // Simple incremental allocators for new local IDs (kept outside real DAT range regions user cares about)
        private uint _nextClothingId = 0x10FF0000; // 0x10 = clothing file type
        private uint _nextSetupId = 0x02FF0000;    // 0x02 = setup file type

        public uint NextClothingId()
        {
            // Ensure non-collision with existing DAT files or session items
            while (IdExists(_nextClothingId)) _nextClothingId++;
            return _nextClothingId++;
        }

        public uint NextSetupId()
        {
            while (IdExists(_nextSetupId)) _nextSetupId++;
            return _nextSetupId++;
        }
        public IReadOnlyList<ClothingIdSuggestion> SuggestClothingIds(uint currentId, int count = 6)
        {
            var suggestions = new List<ClothingIdSuggestion>();
            AddSuggestion(FindFreeClothingId(0x10FF0000), "recommended custom range");
            if (currentId >= 0x10000000 && currentId < 0x10FFFFFF)
                AddSuggestion(FindFreeClothingId(currentId + 1), "next free after current");
            AddSuggestion(FindFreeClothingId(0x10FE0000), "alternate custom range");
            AddSuggestion(FindFreeClothingId(0x10FD0000), "alternate custom range");

            var cursor = 0x10FF0000u;
            while (suggestions.Count < count && cursor <= 0x10FFFFFF)
            {
                var candidate = FindFreeClothingId(cursor);
                if (candidate == 0) break;
                AddSuggestion(candidate, "available ClothingMod ID");
                cursor = candidate == 0x10FFFFFF ? 0 : candidate + 1;
                if (cursor == 0) break;
            }
            return suggestions.Take(count).ToList();

            void AddSuggestion(uint id, string reason)
            {
                if (id != 0 && id != currentId && suggestions.All(item => item.Id != id))
                    suggestions.Add(new ClothingIdSuggestion { Id = id, Reason = reason });
            }
        }

        public bool TryAssignClothingId(uint newId, bool allowPortalOverride, out string error)
        {
            error = null;
            if (SelectedClothing == null || !TryGetModel(SelectedClothing.Id, out var model))
            {
                error = "No working clothing mod is selected.";
                return false;
            }
            var oldId = SelectedClothing.Id;
            if ((newId >> 24) != 0x10)
            {
                error = "Clothing IDs must be in the 0x10000000-0x10FFFFFF range.";
                return false;
            }
            if (newId != oldId && ClothingItems.Any(item => item != SelectedClothing && item.Id == newId))
            {
                error = $"Working mod 0x{newId:X8} already exists.";
                return false;
            }
            var portalCollision = false;
            try { portalCollision = DatManager.PortalDat?.AllFiles?.ContainsKey(newId) == true; } catch { }
            if (newId != oldId && portalCollision && !allowPortalOverride)
            {
                error = $"0x{newId:X8} already exists in portal.dat. Enable existing-ID override only if replacing it is intentional.";
                return false;
            }
            if (newId == oldId) return true;

            _models.Remove(oldId);
            ClothingModService.AssignId(model, newId);
            _models[newId] = model;
            SelectedClothing.Id = newId;
            IsDirty = true;
            ClothingIdChanged?.Invoke(oldId, newId);
            return true;
        }

        public bool IsPortalClothingId(uint id)
        {
            try { return DatManager.PortalDat?.AllFiles?.ContainsKey(id) == true; }
            catch { return false; }
        }

        private uint FindFreeClothingId(uint start)
        {
            if ((start >> 24) != 0x10) return 0;
            for (var id = start; id <= 0x10FFFFFF; id++)
            {
                if (!IdExists(id)) return id;
                if (id == 0x10FFFFFF) break;
            }
            return 0;
        }

        private static bool IdExists(uint id)
        {
            try
            {
                if (DatManager.PortalDat?.AllFiles?.ContainsKey(id) == true) return true;
            }
            catch { }
            return Instance.ClothingItems.Any(c => c.Id == id) || Instance.Setups.Any(s => s.Id == id);
        }

        private ClothingTableVM _selectedClothing;
        public ClothingTableVM SelectedClothing { get => _selectedClothing; set { if (_selectedClothing != value) { _selectedClothing = value; OnPropertyChanged(); } } }

        private SetupVM _selectedSetup;
        public SetupVM SelectedSetup { get => _selectedSetup; set { if (_selectedSetup != value) { _selectedSetup = value; OnPropertyChanged(); } } }

        #region Commands
        public ICommand NewClothingCommand => new RelayCommand(_ => NewClothing());
        public ICommand CloneClothingCommand => new RelayCommand(_ => CloneSelectedClothing(), _ => SelectedClothing != null);
        public ICommand DeleteClothingCommand => new RelayCommand(_ => DeleteSelectedClothing(), _ => SelectedClothing != null);
        public ICommand AddSubPaletteEffectCommand => new RelayCommand(_ => AddSubPaletteEffect(), _ => SelectedClothing != null);
        public ICommand AddCloSubPaletteCommand => new RelayCommand(p => AddCloSubPalette(p as SubPaletteEffectVM), _ => SelectedSubPaletteEffect != null);
        public ICommand DeleteCloSubPaletteCommand => new RelayCommand(p => DeleteCloSubPalette(p as CloSubPaletteVM), p => p is CloSubPaletteVM);
        public ICommand IncreaseScaleCommand => new RelayCommand(_ => UiScale = Math.Min(2.0, UiScale + 0.1));
        public ICommand DecreaseScaleCommand => new RelayCommand(_ => UiScale = Math.Max(0.75, UiScale - 0.1));
        #endregion

        private double _uiScale = 1.0;
        public double UiScale { get => _uiScale; set { if (Math.Abs(_uiScale - value) > 0.0001) { _uiScale = value; OnPropertyChanged(); } } }

        private SubPaletteEffectVM _selectedSubPaletteEffect;
        public SubPaletteEffectVM SelectedSubPaletteEffect { get => _selectedSubPaletteEffect; set { if (_selectedSubPaletteEffect != value) { _selectedSubPaletteEffect = value; OnPropertyChanged(); } } }

        private CloSubPaletteVM _selectedCloSubPalette;
        public CloSubPaletteVM SelectedCloSubPalette { get => _selectedCloSubPalette; set { if (_selectedCloSubPalette != value) { _selectedCloSubPalette = value; OnPropertyChanged(); } } }

        public void RegisterModel(ClothingTable model)
        {
            if (model != null) _models[model.Id] = model;
        }

        public bool TryGetModel(uint id, out ClothingTable model) => _models.TryGetValue(id, out model);

        private void RegisterAndOpen(ClothingTable model)
        {
            RegisterModel(model);
            var vm = ClothingMapping.AddOrUpdate(this, model);
            vm.IsModified = true;
            SelectedClothing = vm;
            OpenClothingRequested?.Invoke(model);
        }

        private void NewClothing()
        {
            RegisterAndOpen(ClothingModService.CreateEmpty(NextClothingId()));
            IsDirty = true;
        }

        private void CloneSelectedClothing()
        {
            if (SelectedClothing == null) return;
            if (!TryGetModel(SelectedClothing.Id, out var source)) return;
            var newId = NextClothingId();
            RegisterAndOpen(ClothingModService.Clone(source, newId));
            IsDirty = true;
        }

        private void DeleteSelectedClothing()
        {
            if (SelectedClothing == null) return;
            if (System.Windows.MessageBox.Show(
                    $"Remove working clothing mod 0x{SelectedClothing.Id:X8}?\n\nExported files are not deleted.",
                    "Delete Working Mod",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                return;
            var idx = ClothingItems.IndexOf(SelectedClothing);
            var removedId = SelectedClothing.Id;
            ClothingItems.Remove(SelectedClothing);
            _models.Remove(removedId);
            SelectedClothing = ClothingItems.Count > 0 ? ClothingItems[Math.Max(0, idx - 1)] : null;
            CurrentClothingRaw = null;
            if (SelectedClothing != null && TryGetModel(SelectedClothing.Id, out var next))
                OpenClothingRequested?.Invoke(next);
            else
                OpenClothingRequested?.Invoke(null);
            IsDirty = true;
        }

        private void AddSubPaletteEffect()
        {
            if (SelectedClothing == null) return;
            var root = SelectedClothing.BaseEffects.FirstOrDefault(b => b.BaseId == 0xFFFFFFFF);
            if (root == null)
            {
                root = new BaseEffectVM { BaseId = 0xFFFFFFFF };
                SelectedClothing.BaseEffects.Add(root);
            }
            uint newId = 1;
            var existing = root.SubPaletteEffects.Select(s => s.EffectId).ToHashSet();
            while (existing.Contains(newId)) newId++;
            var spe = new SubPaletteEffectVM { EffectId = newId, IsModified = true };
            root.SubPaletteEffects.Add(spe);
            SelectedSubPaletteEffect = spe;
            IsDirty = true;
        }

        private void AddCloSubPalette(SubPaletteEffectVM effect)
        {
            effect ??= SelectedSubPaletteEffect;
            if (effect == null) return;
            var csp = new CloSubPaletteVM { PaletteSetId = 0, Shade = 0f };
            csp.Ranges.Add(new RangeVM { OffsetGroups = 0, LengthGroups = 1 });
            effect.CloSubPalettes.Add(csp);
            SelectedCloSubPalette = csp;
            IsDirty = true;
        }

        private void DeleteCloSubPalette(CloSubPaletteVM vm)
        {
            if (vm == null) return;
            var root = SelectedClothing?.BaseEffects.FirstOrDefault(b => b.BaseId == 0xFFFFFFFF);
            if (root == null) return;
            foreach (var spe in root.SubPaletteEffects)
            {
                if (spe.CloSubPalettes.Remove(vm))
                {
                    if (SelectedCloSubPalette == vm) SelectedCloSubPalette = null;
                    break;
                }
            }
            IsDirty = true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string m = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(m));
    }

    #region View Models (Step 1 minimal properties)

    public abstract class VMBase : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isModified;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public bool IsModified { get => _isModified; set => SetField(ref _isModified, value); }
        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string m = null)
        { if (Equals(field, value)) return false; field = value; OnPropertyChanged(m); return true; }
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RangeVM : VMBase
    {
        private uint _offsetGroups; // groups of 8 colors
        private uint _lengthGroups; // groups of 8 colors
        public uint OffsetGroups { get => _offsetGroups; set => SetField(ref _offsetGroups, value); }
        public uint LengthGroups { get => _lengthGroups; set => SetField(ref _lengthGroups, value); }
        public string DisplayName => $"Range {OffsetGroups}:{LengthGroups}";
    }

    public class CloSubPaletteVM : VMBase
    {
        private uint _paletteSetId;
        private float _shade; // resolved shade (0-1) optional per-entry override in future
        public uint PaletteSetId { get => _paletteSetId; set => SetField(ref _paletteSetId, value); }
        public float Shade { get => _shade; set => SetField(ref _shade, value); }
        public ObservableCollection<RangeVM> Ranges { get; } = new();
        public string DisplayName => $"Palette 0x{PaletteSetId:X8} (shade {Shade:0.###})";
    }

    public class SubPaletteEffectVM : VMBase
    {
        private uint _effectId; // key from ClothingSubPalEffects dictionary
        public uint EffectId { get => _effectId; set => SetField(ref _effectId, value); }
        public ObservableCollection<CloSubPaletteVM> CloSubPalettes { get; } = new();
        public string DisplayName => $"Sub-effect {EffectId}";
    }

    public class BaseEffectVM : VMBase
    {
        private uint _baseId; // key from ClothingBaseEffects
        public uint BaseId { get => _baseId; set => SetField(ref _baseId, value); }
        public ObservableCollection<SubPaletteEffectVM> SubPaletteEffects { get; } = new();
        public string DisplayName => BaseId == 0xFFFFFFFF ? "Sub-palette effects" : $"Base setup 0x{BaseId:X8}";
    }

    public class ClothingTableVM : VMBase
    {
        private uint _id;
        public uint Id { get => _id; set { if (SetField(ref _id, value)) OnPropertyChanged(nameof(DisplayName)); } }
        public string DisplayName => $"0x{Id:X8}";
        public ObservableCollection<BaseEffectVM> BaseEffects { get; } = new();
    }

    public class SetupVM : VMBase
    {
        private uint _id;
        public uint Id { get => _id; set => SetField(ref _id, value); }
        public string DisplayName => $"0x{Id:X8}";
    }

    public sealed class ClothingIdSuggestion
    {
        public uint Id { get; init; }
        public string Reason { get; init; }
        public string DisplayName => $"0x{Id:X8}  {Reason}";
    }

    #endregion
}
