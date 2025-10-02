# ACViewer CloComp (Enhanced ACViewer)

Enhanced fork of ACViewer focused on clothing coloration, palette composition and advanced visualization of Asheron's Call data files.

## Branding
"CloComp" (Clothing / Color Composer) – tools for inspecting, composing and previewing palette / color ranges for AC assets.

## Key Enhancements Over Upstream ACViewer
- Custom Palette Dialog (table driven multi–palette mode):
  - DataGrid with per–row lock, live palette assignment by clicking source list
  - PaletteSet (0x0Fxxxxxx) & raw Palette (0x04xxxxxx) support with compatibility filtering for current clothing item
  - Live shade handling and preview bars (big + per?range highlight)
  - Row add / remove, global lock/unlock, generated read?only definition snapshot
- Savable / loadable Custom Palette presets (JSON) with picker dialog
- Robust range parser (comma / whitespace tolerant) producing logical groups (8 colors per group)
- Virindi Color Tool integration / mid?color extraction helper
- Resizable split UI (nested GridSplitters) for palettes / ranges / details
- Defensive DAT cache type checking (prevents invalid casts between Palette & PaletteSet)
- Live model refresh when editing palette rows or clicking palette sources
- (WIP) Texture Remapping Tab: upcoming second tab in Custom dialog for SurfaceTexture (0x05) / Texture (0x06/0x08) substitution with thumbnails & presets

## Usage Overview
1. Open the Custom Palette dialog from the clothing view (select a clothing item then choose "Custom...").
2. Click a palette or palette set on the left – if a row is selected and unlocked its Palette/Set ID updates immediately.
3. Edit ranges (e.g. `0:4,8:2`) in the table. Each offset/length unit = 8 actual colors.
4. Lock rows you want frozen; use "Lock All Rows" for bulk locking.
5. Adjust Shade (0–1). Model & previews update live.
6. Save or load presets for reuse.

## Range Syntax
Token form: `offset:length`
- offset = group index (group = 8 colors)
- length = number of consecutive groups
Multiple tokens separated by comma or whitespace.
Examples:
- `0:8`  ? first 64 colors
- `0:4,8:4` ? two 32?color blocks

## Compatibility Filtering
When opened from a clothing item the palette list shows:
- Referenced PaletteSets used by that clothing definition (0x0F…)
- Any raw Palettes (0x04…) large enough to satisfy all required color spans of the ranges
Fallback: full palette / set list if no constraints resolved.

## Preset Storage
`CustomPalettes.json` (created on first save) in app directory:
```json
[{"Name":"Example","Multi":true,"Shade":0.25,"Entries":[{"PaletteSetId":4026531840,"Ranges":[{"Offset":0,"Length":8}]}]}]
```
Structure may evolve; backward compatibility best effort.

## Build Requirements
- .NET 8 SDK
- Windows (WPF + MonoGame WpfInterop)
- Run `dotnet restore` at repo root

## Running
1. Supply AC DAT files (portal.dat, cell.dat, etc.).
2. Launch application.
3. Configure data path if not auto?detected.

## Projects
- ACViewer (WPF + MonoGame host/UI)
- ACE.DatLoader / ACE.Entity / ACE.Common / ACE.Database (DAT parsing & entities)

## Contributing
PRs welcome for:
- Additional visualization (alpha, diff, heatmaps)
- Export / import formats (Virindi, CSV, CLI tooling)
- Undo/redo and validation UX
- Performance profiling for large palette scans
- (Future) Texture remap workflow (thumbnail grid, diff view, external PNG import)

## License
(Insert chosen license – MIT / GPL / etc.)

## Disclaimer
Not affiliated with Turbine or WB. For educational and archival purposes only.
Absolutely no guarantee of fitness for any purpose, including—but not limited to—being "f***in' fabulous" in game. If you become dramatically stylish, radiant, or otherwise socially overpowered as a result of using this tool, that's on you.

## Roadmap Ideas
- Batch comparison across PaletteSets
- Color nearest?match search in main list
- CLI export of composed palettes / preset batch processing
- Undo/redo stack for table edits
- Palette diff and contrast analytics
- Texture remapping presets & live surface substitution (0x05/0x06) (in progress)

---
Original ACViewer credit to its authors; this fork emphasizes advanced clothing & color composition workflows.
