using System;
using System.Linq;
using ACE.DatLoader.FileTypes; // ClothingTable file type
using ACE.DatLoader.Entity; // CloSubPalEffect etc.

namespace ACViewer.ViewModels
{
    /// <summary>
    /// Static helpers to map between raw ClothingTable model objects and the editing view models.
    /// Phase 2: one-way population (model -> VM). Reverse mapping added for export/save.
    /// </summary>
    public static class ClothingMapping
    {
        /// <summary>
        /// Create a new ClothingTableVM populated from an ACE.DatLoader.FileTypes.ClothingTable.
        /// If an existing VM is supplied it is cleared & repopulated.
        /// </summary>
        public static ClothingTableVM MapToViewModel(ClothingTable model, ClothingTableVM existing = null)
        {
            if (model == null) return null;
            var vm = existing ?? new ClothingTableVM { Id = model.Id };
            vm.Id = model.Id;
            vm.BaseEffects.Clear();

            // Base Effects (textures / objects). For now we only capture the base IDs themselves; deeper editing later.
            foreach (var kvp in model.ClothingBaseEffects.OrderBy(k => k.Key))
            {
                var baseVm = new BaseEffectVM { BaseId = kvp.Key };
                vm.BaseEffects.Add(baseVm);
            }

            // Insert a synthetic grouping node for SubPalette effects as a BaseEffectVM with special BaseId (optional).
            // Instead we will later bind directly to a parallel collection. For now we attach SubPaletteEffects to each BaseEffectVM if IDs correspond (future logic).

            // Add a pseudo BaseEffect for aggregated SubPalette effects so the TreeView has a single root collection now.
            var subPalRoot = new BaseEffectVM { BaseId = 0xFFFFFFFF }; // sentinel ID representing "SubPalettes"
            foreach (var kvp in model.ClothingSubPalEffects.OrderBy(k => k.Key))
            {
                var subVm = new SubPaletteEffectVM { EffectId = kvp.Key };
                foreach (var cloSub in kvp.Value.CloSubPalettes)
                {
                    var csp = new CloSubPaletteVM { PaletteSetId = cloSub.PaletteSet, Shade = 0f };
                    foreach (var r in cloSub.Ranges)
                    {
                        // Convert raw color offsets / lengths to group units (8 colors per group)
                        uint groupOffset = r.Offset / 8;
                        uint groupLen = r.NumColors / 8;
                        if (groupLen == 0) groupLen = 1; // guard – keep at least 1 group visible
                        csp.Ranges.Add(new RangeVM { OffsetGroups = groupOffset, LengthGroups = groupLen });
                    }
                    subVm.CloSubPalettes.Add(csp);
                }
                subPalRoot.SubPaletteEffects.Add(subVm);
            }
            if (subPalRoot.SubPaletteEffects.Count > 0)
                vm.BaseEffects.Add(subPalRoot);

            return vm;
        }

        /// <summary>
        /// Ensure a ClothingTableVM exists in the session for the provided model and synchronize its contents.
        /// </summary>
        public static ClothingTableVM AddOrUpdate(ClothingEditingSession session, ClothingTable model)
        {
            if (session == null || model == null) return null;
            var vm = session.ClothingItems.FirstOrDefault(c => c.Id == model.Id);
            vm = MapToViewModel(model, vm);
            if (!session.ClothingItems.Contains(vm))
                session.ClothingItems.Add(vm);
            session.SelectedClothing = vm;
            return vm;
        }

        /// <summary>
        /// Reverse map a ClothingTableVM to a new ClothingTable model instance.
        /// Note: Base effect object/texture details (CloObjectEffects) are not captured yet and will be empty.
        /// </summary>
        public static ClothingTable MapToModel(ClothingTableVM vm)
        {
            if (vm == null) return null;
            var model = new ClothingTable();
            // model.Id has no public setter; cannot mutate here (Id originates from original table or allocator)

            // Base effects
            foreach (var be in vm.BaseEffects.Where(b => b.BaseId != 0xFFFFFFFF))
            {
                if (!model.ClothingBaseEffects.ContainsKey(be.BaseId))
                    model.ClothingBaseEffects.Add(be.BaseId, new ClothingBaseEffect());
            }

            var subRoot = vm.BaseEffects.FirstOrDefault(b => b.BaseId == 0xFFFFFFFF);
            if (subRoot != null)
            {
                foreach (var effectVm in subRoot.SubPaletteEffects)
                {
                    var subEffect = new CloSubPalEffect();
                    foreach (var cloVm in effectVm.CloSubPalettes)
                    {
                        var palette = new CloSubPalette { PaletteSet = cloVm.PaletteSetId };
                        foreach (var r in cloVm.Ranges)
                        {
                            var off = r.OffsetGroups * 8;
                            var len = r.LengthGroups * 8;
                            if (len == 0) continue;
                            palette.Ranges.Add(new CloSubPaletteRange { Offset = off, NumColors = len });
                        }
                        if (palette.Ranges.Count > 0)
                            subEffect.CloSubPalettes.Add(palette);
                    }
                    if (subEffect.CloSubPalettes.Count > 0 && !model.ClothingSubPalEffects.ContainsKey(effectVm.EffectId))
                        model.ClothingSubPalEffects.Add(effectVm.EffectId, subEffect);
                }
            }
            return model;
        }
    }
}
