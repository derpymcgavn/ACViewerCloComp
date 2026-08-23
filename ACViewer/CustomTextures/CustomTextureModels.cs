using System.Collections.Generic;
using Newtonsoft.Json;

namespace ACViewer.CustomTextures
{
    public class CustomTextureDefinition
    {
        public string Name { get; set; }
        public List<CustomTextureEntry> Entries { get; set; } = new List<CustomTextureEntry>();
    }

    public class CustomTextureEntry
    {
        public int PartIndex { get; set; }
        public uint OldId { get; set; }
        public uint NewId { get; set; }
    }

    // Export container matching requested JSON structure
    public class ClothingExport
    {
        public Dictionary<string, ClothingBaseEffectExport> ClothingBaseEffects { get; set; } = new();
        public Dictionary<string, ClothingSubPalExport> ClothingSubPalEffects { get; set; } = new();
        // Legacy ACViewer files may contain this section. CustomClothingBase ignores it;
        // new exports bake replacements into ClothingBaseEffects and omit this property.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<CustomTextureOverrideExport> CustomTextureOverrides { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool AllowBaseOverride { get; set; }

        public string Id { get; set; }
    }

    public class ClothingBaseEffectExport
    {
        public List<CloObjectEffectExport> CloObjectEffects { get; set; } = new();
    }

    public class CloObjectEffectExport
    {
        public string Index { get; set; }
        public string ModelId { get; set; }
        public List<CloTextureEffectExport> CloTextureEffects { get; set; } = new();
    }

    public class CloTextureEffectExport
    {
        public string OldTexture { get; set; }
        public string NewTexture { get; set; }
    }

    public class ClothingSubPalExport
    {
        public string Icon { get; set; }
        public List<CloSubPaletteExport> CloSubPalettes { get; set; } = new();
    }

    public class CloSubPaletteExport
    {
        public string PaletteSet { get; set; }
        public List<CloSubPaletteRangeExport> Ranges { get; set; } = new();
    }

    public class CloSubPaletteRangeExport
    {
        public string Offset { get; set; }
        public string NumColors { get; set; }
    }

    public class CustomTextureOverrideExport
    {
        public string PartIndex { get; set; }
        public string OldTexture { get; set; }
        public string NewTexture { get; set; }
    }
}
