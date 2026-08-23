# DerpACE Clothing Studio / Mob Builder

CloComp is a Windows desktop editor for Asheron's Call DAT-driven visual modding and DerpACE admin workflows. It started as ACViewer, but this fork is now focused on clothing mods, palette/range work, texture swaps, model/part replacement, and mob visual SQL authoring.

The app still keeps the ACViewer internals where that makes compatibility easier, but the user-facing goal is simple: make DAT visual editing fast, visible, reversible, and sane enough for admins to use without spelunking raw IDs all day.

## Current highlights

- Dark WPF workspace with docked Explorer, 3D Preview, Clothing Studio, Mob Builder, Output, and JSON tools.
- Live MonoGame 3D preview for clothing and mob visuals.
- Camera-preserving preview refreshes, so texture/model clicks no longer kick the view back to the default zoom.
- DAT asset indexing and precache support with a visible progress bar.
- Lazy/infinite scrolling browsers for Mob Builder texture and model candidates.
- Searchable `0x05` SurfaceTexture browser with thumbnails.
- Searchable model/world-object browser for part replacement.
- Palette suggestion lookup from selected textures.
- ClothingMod JSON import/export and round-trip validation.
- Mob Builder SQL import/export for DerpACE-style `weenie_properties_texture_map` and `weenie_properties_anim_part` rows.
- DerpACE admin tooling hooks for local/admin-PC workflows.

## Requirements

- Windows 10/11
- .NET 8 SDK
- AC client DAT files available locally
- Git submodules initialized

```powershell
git submodule update --init --recursive
dotnet restore .\ACViewer.sln
dotnet build .\ACViewer\ACViewer.csproj --configuration Release
```

Run the release build from:

```text
ACViewer\bin\Release\net8.0-windows7.0\DerpAceClothingStudio.exe
```

Optional DAT startup argument:

```powershell
DerpAceClothingStudio.exe --dat-dir "C:\Turbine\Asheron's Call"
```

## First-load performance

DAT decoding is expensive the first time a session touches clothing tables, setup models, palettes, and surface textures. Use:

```text
Tools > Precache Clothing DAT Assets
```

The app shows a top progress strip while precache is running. It warms the common DAT paths and builds the ID indexes used by palette, texture, and model browsers.

## Clothing Studio workflow

1. Load the client DATs.
2. Select file type `0x10 - Clothing` in Explorer.
3. Pick a ClothingTable.
4. Use **Clone** or **Assign ID** when creating a custom ClothingMod entry.
5. Choose a setup/body preview target.
6. Edit parts:
   - Replace armor/world-object model IDs.
   - Replace per-part SurfaceTexture IDs.
   - Add/remove part index rows.
7. Edit palettes:
   - Pick raw `0x04` palettes or `0x0F` PaletteSets.
   - Paint one or more 8-color ranges.
   - Assign custom palette numbers for ClothingMod JSON.
8. Preview the full suit live in 3D.
9. Export ClothingMod JSON or publish through the DerpACE admin connection.

### Palette range semantics

Palette ranges are authored in 8-color groups.

- `0:8` means 8 groups, or 64 actual colors.
- `0:4,8:4` means two separate 32-color regions.
- Imports snap offsets and lengths to valid 8-color boundaries.
- Overlapping palette claims are treated defensively so one range does not accidentally stomp another.

Example ClothingMod palette entry:

```json
"13": {
  "Icon": "0x060017E4",
  "CloSubPalettes": [
    {
      "PaletteSet": "0x0F00001D",
      "Ranges": [
        {
          "Offset": "0x00000780",
          "NumColors": "0x00000050"
        }
      ]
    }
  ]
}
```

## Mob Builder workflow

Mob Builder is docked into the main workspace and shares the same 3D preview idea as Clothing Studio, but it targets SQL-shaped mob data instead of ClothingMod JSON.

Use it for mobs like custom bosses or admin-spawned creatures where the visual definition comes from SQL rows:

- `weenie_properties_texture_map`
  - `Index`
  - `OldId`
  - `NewId`
  - optional SQL comment/part label
- `weenie_properties_anim_part`
  - `Index`
  - `AnimationId`
  - optional SQL comment/part label

The **Textures / Palettes** tab previews old and replacement SurfaceTextures side by side and shows palette suggestions for the selected texture. The **Models / Parts** tab lets you browse model/world-object candidates and apply them to selected part rows.

Mob Builder can import SQL/JSON drafts, preview the mob in 3D, copy SQL to the clipboard, and export SQL for DerpACE database import.

## DerpACE admin connection

The admin connection is intended for trusted local admin PCs. Clothing Studio can connect to DerpACE admin tooling to pull, edit, publish, and reload clothing data without manually copying files around.

Suggested setup:

1. Enable DerpACE's admin map/API service on the server.
2. Bind it to a private LAN/VPN address reachable by admin PCs.
3. Use a long random admin token.
4. Open **Tools > DerpACE Clothing Admin** in the app.
5. Enter the server URL and token, connect, pull data, edit, then publish.

The app stores the server URL under `%LocalAppData%\DerpACE Clothing Studio`. Admin tokens are treated as session secrets and should not be written to disk.

## Validation and safety

The editor is intentionally defensive:

- Keeps DAT IDs normalized as hex strings.
- Validates common asset types before export.
- Round-trips ClothingMod JSON exports where supported.
- Preserves camera position for iterative texture/model edits.
- Uses reset actions and undo/redo where available so edits stay reversible.

For a quick serializer/export sanity check:

```powershell
dotnet run --project .\ACViewer\ACViewer.csproj --configuration Release -- --self-test-clothingmod
```

## Project layout

- `ACViewer/` - WPF app, MonoGame preview host, Clothing Studio, Mob Builder, DAT services.
- `ACViewer/ClothingStudio/` - clothing workflow models/services.
- `ACViewer/MobBuilder/` - mob builder models and import/export helpers.
- `ACViewer/CustomPalettes/` - palette presets, range editing, palette suggestions.
- `ACViewer/CustomTextures/` - texture override and ClothingMod serialization.
- `ACViewer/Services/` - DAT indexing, precache, admin clients, and shared services.
- `ACE/` - ACE parsing/entities/database projects used by the viewer/editor.

## Build notes

The project targets .NET 8 and Windows desktop APIs. CI should restore submodules, restore NuGet packages, and build the WPF project in Release mode.

Known warning: current ACE dependencies reference `log4net` 2.0.17, which NuGet reports with advisory `NU1902`. That warning is inherited from the ACE dependency tree and does not block the editor build.

## Roadmap

- Convert the older Clothing Studio paged galleries to the same lazy scrolling behavior used by Mob Builder.
- Tighten DerpACE SQL enum mappings against the live server schema.
- Add richer mob particle/effect authoring.
- Add more visual part highlighting for selected indices.
- Continue reducing first-load and model-switching jank.
- Explore safe DAT write/export integration with DatReaderWriter.

## Disclaimer

Not affiliated with Turbine or WB. For educational, archival, and private-server tooling purposes only.

Original ACViewer credit to its authors. This fork is focused on DerpACE clothing, mob visual editing, and admin-friendly workflows.
