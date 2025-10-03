namespace ACViewer.CustomPalettes
{
    // Minimal interfaces so ClothingTableList can notify docked editors without hard dependencies
    public interface ICustomPaletteHost
    {
        void LoadDefinition(CustomPaletteDefinition definition, bool isLive);
    }

    public interface ITextureOverrideHost
    {
        void UpdateFromPalette(CustomPaletteDefinition definition);
    }
}
