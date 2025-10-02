# ACViewer CloComp (Enhanced ACViewer)

Enhanced fork of ACViewer focused on clothing coloration, palette composition and advanced visualization of Asheron's Call data files.

## Branding
"CloComp" (Clothing / Color Composer) – tools for inspecting, composing and previewing palette / color ranges for AC assets.

## Key Enhancements Over Upstream ACViewer
- Custom Palette Dialog (multi?palette mode only) with:
  - Line based syntax: `0x<PaletteOrSetID> off:len[,off:len...]`
  - Live preview + per?line range highlight visualization
  - Shade factor (0–1) applied across composed palettes
  - Automatic line insertion when selecting palettes / palette sets
  - Support for PaletteSets (0xFxxxxxxx) with in?set browsing slider
- Savable / loadable Custom Palette presets (JSON) with picker dialog
- Robust range parser (comma + whitespace, validation) producing `Offset/Length` groups (logical groups of 8 colors for highlighting)
- Virindi Color Tool integration / export visualization
- Improved WPF UI layout (resizable dialog, dark friendly previews)
- Safer parsing (graceful failures instead of silent crashes in most UI paths)

## Usage Overview
1. Open the Custom Palette dialog from the clothing / model context (menu or button depending on integration).
2. Select a palette or palette set in the left list – a default full?range line is auto added.
3. Edit the range tokens (e.g. `0:4,6:2`) to highlight specific 8?color groups.
4. Adjust Shade slider for global dim/bright effect.
5. Save preset (stored in `CustomPalettes.json` alongside the exe) or Load existing.

## Range Syntax
Token: `offset:length`
- `offset` = group index (each group = 8 colors)
- `length` = number of consecutive groups
Multiple tokens separated by space or comma.
Examples:
- `0:8` first 64 colors
- `0:4,8:4` two blocks of 32 colors each

## Build Requirements
- .NET 8 SDK
- Windows (WPF + MonoGame WpfInterop)
- NuGet restore (run `dotnet restore` at repo root)

## Running
1. Copy required AC data files (portal.dat, cell.dat, etc.) into a folder.
2. Launch application.
3. Set the AC data folder in configuration if not auto detected.

## Projects
- ACViewer (WPF + MonoGame host/UI)
- ACE.DatLoader / ACE.Entity / ACE.Common / ACE.Database (data access for DAT content)

## Preset Storage
`CustomPalettes.json` (created on first save) – simple array of objects:
```json
[{"Name":"Example","Multi":true,"Shade":0.25,"Entries":[{"PaletteSetId":4026531840,"Ranges":[{"Offset":0,"Length":8}]}]}]
```
Fields may evolve; backward compatibility kept best?effort.

## Contributing
PRs welcome for:
- Additional visualization (alpha channel, diff tools)
- Export / import formats (Virindi, JSON schema evolution)
- Validation & error messaging improvements

## License
(Insert your chosen license here – MIT / GPL / etc.)

## Disclaimer
Not affiliated with Turbine or WB. For educational and archival purposes.

## Roadmap Ideas
- Batch compare palettes across sets
- Color search (nearest palette entry)
- CLI export of composed palettes
- Undo/redo in Custom Palette dialog

---
Original ACViewer credit to its authors; this fork emphasizes advanced clothing & color composition workflows.
