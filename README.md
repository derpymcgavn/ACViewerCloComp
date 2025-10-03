# ACViewer CloComp (Enhanced ACViewer)

Enhanced fork of ACViewer focused on clothing coloration, palette composition, texture swapping and unapologetically obsessive visualization of Asheron's Call data files.

## Branding
"CloComp" (Clothing / Color Composer) – forge chromatic destiny, one 8?color chunk at a time.

## New / Recently Added Goodies
- Interactive Range Editor Bar
  - Drag to paint ranges (group = 8 colors). Shift+Drag to erase. Hover readouts. Undo / Redo (buttons or Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z).
  - Non?overlapping enforcement: overlapping groups across rows are auto de?duped & compacted.
  - Right?click side list range ? "Split range to new row" for fast palette surgery.
- Multi?Palette Table (revamped Custom Palette dialog)
  - Per?row lock, global lock toggle, live palette assignment by clicking source list.
  - Supports PaletteSets (0x0Fxxxxxx) AND raw Palettes (0x04xxxxxx) transparently.
  - Shade slider (0–1) resolves correct member palette inside PaletteSet; direct palettes treated as single shade.
  - Auto highlight preview & large bar preview with active ranges luminance pop.
- Smart Palette Listing
  - Filters to only palette sets used by the current clothing + any raw palettes large enough to cover required color spans.
  - Fallback to full list if constraints fail.
- Clothing JSON Import / Export
  - Import custom clothing color + texture definitions (offset / length values auto rounded down to multiples of 8, zero length skipped).
  - Export current clothing table (including palette & texture data) for sharing / versioning.
- Texture Remapping (Early Stage)
  - Texture tab scaffolding with part extraction and candidate lists (0x05 SurfaceTexture / 0x06 Texture). Thumbnail previews & per?row locking included; presets planned.
- Custom Palette Presets
  - Save / Load via `CustomPalettes.json`. Preset picker dialog with live apply.
- Virindi Color Tool Helper
  - Mid?range color extraction for each applied sub?palette + shade aware palette resolution.
- Synthetic PaletteSet Fallback
  - If something foolish asks for a PaletteSet but only a raw Palette is cached, we conjure a one?entry synthetic set instead of detonating.
- Robust Range Parser
  - Comma/space tolerant, tolerant mode for in?progress edits, strict mode for final parse.
- Non?Overlapping Guarantee
  - Rows sharing a palette automatically lose overlapping groups (first claim wins). Empty rows vanish like broken fashion dreams.
- Live Model Rebuilds
  - Palette, shade, texture tweaks instantly regenerate the Setup preview. Zero manual refresh incantations.
- Defensive DAT Cache Type Validation
  - Prevents mis?cast face?plants (Palette vs PaletteSet) with humane warnings.
- Offset / Length Validation (Import)
  - All imported offsets & lengths snapped to 8?color boundaries; invalid fragments politely ignored.

## Usage TL;DR
1. Pick clothing (File Type 0x10). Click "Custom...".
2. Add / remove rows, click palettes on the left to bind them to selected row.
3. Paint or erase range blocks in the interactive bar. Split ranges if you crave modularity.
4. Adjust Shade. Bask in chromatic glory.
5. Save a preset; export clothing to JSON if you want to brag or version control your drip.

## Range Semantics
- Unit = group of 8 actual colors.
- Syntax: `offset:length` (group indices, not raw color indices).
- Example: `0:8` = first 64 colors; `0:4,8:4` = two distinct 32?color zones.

## JSON Clothing Import Notes
- Offsets / NumColors auto floored to nearest multiple of 8.
- Zero or empty results culled silently.
- Mixed palette sets (0x0F…) & raw palettes (0x04…) allowed in `PaletteSet` field.

## Preset Format (Excerpt)
```json
[
  {
    "Name": "SteelMist",
    "Multi": true,
    "Shade": 0.375,
    "Entries": [
      { "PaletteSetId": 4026531840, "Ranges": [ { "Offset": 0, "Length": 4 }, { "Offset": 8, "Length": 2 } ] }
    ]
  }
]
```

## Texture Tab (Preview State)
- Enumerates part surfaces, captures original texture IDs, allows prospective new texture selection.
- Future roadmap: external PNG import, diff overlays, batch apply, shareable texture remap presets.

## Quality of Life
- Undo / Redo for range painting.
- Auto shade preview & first member palette extraction for sets.
- Split context action for surgical palette decomposition.
- Generated read?only textual descriptor (good for git diffs / PR review of color intent).

## Build
- .NET 8 SDK
- Windows (WPF + MonoGame interop)
- `dotnet restore`, add dat files, launch.

## Projects
- ACViewer (WPF + MonoGame host)
- ACE.DatLoader / ACE.Entity / ACE.Common / ACE.Database (parsing, entities, utilities)

## Contributing Wishlist
- Texture remap UX (drag reorder, multi?select, external atlas import)
- Palette contrast / delta analytics & auto suggestions
- CLI batch preset apply & image export
- Better diff views (old vs new palette heatmaps)
- Multi?pal shade blending experiments
- Export to Virindi / plugin?friendly schemas

## Roadmap (Loose)
- Full texture remap pipeline
- PaletteSet comparison matrix & nearest color search
- Command line export & headless batch scripting
- Visual merge conflict helper for overlapping presets

## Philosophy
Make color iteration fast, visible, reversible, and mildly entertaining.

## Disclaimer
Not affiliated with Turbine or WB. For educational and archival purposes only.
Side effects may include: excessive chroma coordination, involuntary nodding at well?blended gradients, or forming opinions about shade granularity that normal people simply do not have.

## License
(Insert chosen license – MIT / GPL / etc.)

---
Original ACViewer credit to its authors; this fork fixates on advanced clothing & color composition workflows with a sprinkle of absurd practicality.
