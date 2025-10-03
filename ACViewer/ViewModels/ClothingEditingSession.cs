using System;
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

        private void NewClothing()
        {
            var id = NextClothingId();
            var vm = new ClothingTableVM { Id = id, IsModified = true };
            ClothingItems.Add(vm);
            SelectedClothing = vm;
            IsDirty = true;
        }

        private void CloneSelectedClothing()
        {
            if (SelectedClothing == null) return;
            var newId = NextClothingId();
            var clone = new ClothingTableVM { Id = newId, IsModified = true };
            foreach (var be in SelectedClothing.BaseEffects)
            {
                var beClone = new BaseEffectVM { BaseId = be.BaseId };
                foreach (var spe in be.SubPaletteEffects)
                {
                    var speClone = new SubPaletteEffectVM { EffectId = spe.EffectId };
                    foreach (var csp in spe.CloSubPalettes)
                    {
                        var cspClone = new CloSubPaletteVM { PaletteSetId = csp.PaletteSetId, Shade = csp.Shade };
                        foreach (var r in csp.Ranges)
                            cspClone.Ranges.Add(new RangeVM { OffsetGroups = r.OffsetGroups, LengthGroups = r.LengthGroups });
                        speClone.CloSubPalettes.Add(cspClone);
                    }
                    beClone.SubPaletteEffects.Add(speClone);
                }
                clone.BaseEffects.Add(beClone);
            }
            ClothingItems.Add(clone);
            SelectedClothing = clone;
            IsDirty = true;
        }

        private void DeleteSelectedClothing()
        {
            if (SelectedClothing == null) return;
            var idx = ClothingItems.IndexOf(SelectedClothing);
            ClothingItems.Remove(SelectedClothing);
            SelectedClothing = ClothingItems.Count > 0 ? ClothingItems[Math.Max(0, idx - 1)] : null;
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
        { if (Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(m)); return true; }
    }

    public class RangeVM : VMBase
    {
        private uint _offsetGroups; // groups of 8 colors
        private uint _lengthGroups; // groups of 8 colors
        public uint OffsetGroups { get => _offsetGroups; set => SetField(ref _offsetGroups, value); }
        public uint LengthGroups { get => _lengthGroups; set => SetField(ref _lengthGroups, value); }
    }

    public class CloSubPaletteVM : VMBase
    {
        private uint _paletteSetId;
        private float _shade; // resolved shade (0-1) optional per-entry override in future
        public uint PaletteSetId { get => _paletteSetId; set => SetField(ref _paletteSetId, value); }
        public float Shade { get => _shade; set => SetField(ref _shade, value); }
        public ObservableCollection<RangeVM> Ranges { get; } = new();
    }

    public class SubPaletteEffectVM : VMBase
    {
        private uint _effectId; // key from ClothingSubPalEffects dictionary
        public uint EffectId { get => _effectId; set => SetField(ref _effectId, value); }
        public ObservableCollection<CloSubPaletteVM> CloSubPalettes { get; } = new();
    }

    public class BaseEffectVM : VMBase
    {
        private uint _baseId; // key from ClothingBaseEffects
        public uint BaseId { get => _baseId; set => SetField(ref _baseId, value); }
        public ObservableCollection<SubPaletteEffectVM> SubPaletteEffects { get; } = new();
    }

    public class ClothingTableVM : VMBase
    {
        private uint _id;
        public uint Id { get => _id; set => SetField(ref _id, value); }
        public string DisplayName => $"0x{Id:X8}";
        public ObservableCollection<BaseEffectVM> BaseEffects { get; } = new();
    }

    public class SetupVM : VMBase
    {
        private uint _id;
        public uint Id { get => _id; set => SetField(ref _id, value); }
        public string DisplayName => $"0x{Id:X8}";
    }

    #endregion
}
