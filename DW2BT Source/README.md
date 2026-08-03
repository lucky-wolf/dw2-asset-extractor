# Distant Worlds 2 Bundle Tool (DW2BT)

Extracts assets from DW2 `.bundle` files and converts them to standard formats:
textures → `.dds`, sounds → `.wav`/etc, meshes → `.fbx`, shaders → `.sdsl` source.
This tool only extracts/converts to your local disk — it does not repackage bundles
or insert anything back into the game.

Two executables, sharing the same conversion code:
- **`dw2bm`** (`DistantWorlds2.BundleManager`) — the original precise, per-asset CLI:
  list bundles, list/identify/extract individual files, convert one at a time.
- **`dw2extract`** (`DistantWorlds2.AssetExtractor`) — point it at your DW2 install
  and an output folder, and it walks *every* bundle, converting and recreating the
  full folder structure automatically. See [Asset Extractor](#asset-extractor) below.

Both build on **`DistantWorlds2.Core`**, a class library holding the actual
conversion logic (texture/sound/mesh conversion, bundle reading) — fix something
there and both tools pick it up.

## One-time setup

1. **Point the build at your DW2 install.** The real Xenko/Stride engine DLLs these
   tools link against ship with the game itself — they can't be bundled with this
   repo (they're Slitherine/Matrix Games' property, not a redistributable SDK).

   Copy `DW2BT.local.props.example` to `DW2BT.local.props` (same folder, next to
   this README) and edit the path:
   ```xml
   <DW2GameDir>C:\Steam\steamapps\common\Distant Worlds 2</DW2GameDir>
   ```
   `DW2BT.local.props` is gitignored — it's machine-specific. Both `dw2bm` and
   `dw2extract` import this same setup (`DW2BT.Common.props`), so it only needs
   doing once.

2. **Build:**
   ```
   dotnet build DistantWorlds2.BundleManager.sln
   ```
   This produces `DistantWorlds2.BundleManager\bin\Debug\net8.0-windows\dw2bm.exe`
   and `DistantWorlds2.AssetExtractor\bin\Debug\net8.0-windows\dw2extract.exe`,
   each with all the engine DLLs and native libraries (`x64\*.dll`) copied alongside
   automatically.

3. **Give `dw2bm` access to bundle data.** (`dw2extract` doesn't need this step —
   see below, it takes the install folder as an input instead.) `dw2bm` resolves
   bundles relative to its *own folder* (`<exe folder>\data\db\bundles\`), not your
   current directory. The cleanest way to satisfy that without duplicating
   gigabytes of game data is a directory junction pointing at the game's own
   bundles:
   ```powershell
   $out = "D:\path\to\DW2BT Source\DistantWorlds2.BundleManager\bin\Debug\net8.0-windows"
   New-Item -ItemType Directory -Path "$out\data\db" -Force
   New-Item -ItemType Junction -Path "$out\data\db\bundles" `
       -Target "C:\Steam\steamapps\common\Distant Worlds 2\data\db\bundles"
   ```
   (Simplest alternative: just copy `dw2bm.exe` + its output folder into the game
   install folder itself and run it from there — `data\db\bundles` is already
   sitting right next to it.)

4. **Sound conversion needs FFmpeg.** Put `ffmpeg.exe`/`ffprobe.exe` on your `PATH`,
   or copy them into the same output folder as `dw2bm.exe`/`dw2extract.exe`.

Run everything below from that output folder (or add it to your `PATH`).
Destination paths in the commands below are relative to wherever you run
`dw2bm.exe` *from* — bundle resolution is exe-relative, output paths are cwd-relative.

## Commands

**`dw2bm lb`** — list available bundles (by name, without the hash suffix).
```
dw2bm lb
```

**`dw2bm ls <bundle> [glob]`** — list files inside a bundle.
```
dw2bm ls CoreContent "Sounds/**"
dw2bm ls Human "Ships/Human/**"
```

**`dw2bm id <bundle> <asset path>`** (or `dw2bm id <loose file>`) — show an asset's
type, e.g. `Stride.Rendering.Model`, `Stride.Audio.Sound`, `Stride.Graphics.Texture`.
Useful for figuring out what something is before extracting it.
```
dw2bm id CoreContent "Creatures/knight"
```

**`dw2bm ex <bundle> <asset path> <dest file>`** — extract one file, raw.
**`dw2bm ex <bundle> <glob> <dest folder>`** — extract everything matching a glob.
```
dw2bm ex CoreContent "shaders/DWColor.sdsl" DWColor.sdsl
dw2bm ex CoreContent "Sounds/**" extracted/Sounds/
```

**`dw2bm xt <extracted texture file> <dest.dds>`** — convert an extracted texture
to `.dds`. Works for both small embedded textures and large streaming textures
(needs the matching `_Data` file alongside the source, which `ex` extracts for you
automatically when there is one).
```
dw2bm ex CoreContent "UserInterface/Textures/circle01" circle01
dw2bm xt circle01 circle01.dds
```

**`dw2bm xs <extracted sound file> <dest.wav>`** (or glob form) — convert an
extracted sound to `.wav`/etc via FFmpeg.
```
dw2bm ex CoreContent "Sounds/Components/TractorBeam" TractorBeam
dw2bm xs TractorBeam TractorBeam.wav
```

**`dw2bm xm <bundle> <model asset path> <dest.fbx>`** — export a mesh (and its
skeleton, if it has one) straight from the bundle to binary FBX. No separate `ex`
step needed — this one loads directly from the bundle.
```
dw2bm xm CoreContent "Creatures/knight" knight.fbx
dw2bm xm Human "Ships/Human/human_battleship" battleship.fbx
```
Exports geometry (positions/normals/UVs) and the full node hierarchy (skeleton
bones, or hardpoint markers like `#weaponVerticalLauncherLarge9` for ships) with
each mesh parented under its correct node. Does **not** export bone-weight/skinning
data (fine for ship hardpoint structure; a skinned character like the knight
exports its shape correctly but won't be posable) or materials/textures (pull
those separately with `xt`).

Shader source (`.sdsl` entries under `shaders/`) extracts via plain `ex` — it's
raw text in the bundle, not compiled bytecode, so no conversion step is needed.

## Asset Extractor

`dw2extract.exe` needs no setup beyond the build steps above — run it and it asks
(via folder-browse dialogs) for your DW2 install folder, then where to save
extracted assets. It doesn't need the `data\db\bundles` junction `dw2bm` does: it
mounts whatever install folder you pick at runtime.

It then walks every bundle and recreates the game's own folder structure under
your chosen output folder, one subfolder per bundle (`CoreContent\`, `Human\`,
`Abandoned\`, ...), converting as it goes:
- Textures → `.dds` **and** `.png` (side by side) — the PNG is via FFmpeg (needs
  `ffmpeg.exe` on `PATH` or next to the exe, same as sound conversion), not
  Xenko's own PNG encoder, which turned out to silently mis-decode at least some
  block-compressed textures and to crash outright (unmanaged, uncatchable) on
  floating-point/HDR textures like BRDF LUTs. A texture whose format has no sane
  PNG representation (or that FFmpeg's DDS decoder can't handle) still gets its
  `.dds`, just without a matching `.png`.
- Sounds → `.wav`
- Meshes → `.fbx` (same coverage/limitations as `dw2bm xm` — see above)
- Everything else (shader source, XML/data files, ...) copied as-is, unconverted

Each bundle's subfolder contains exactly that bundle's *own* declared assets — no
cross-bundle deduplication, and no attempt to resolve which version "wins" at
runtime. DW2 routinely has later bundles re-declare (override) an asset path
that an earlier one also declares (e.g. both `CoreContent` and `Human` can
legitimately have their own version of the same path) — this tool deliberately
doesn't try to figure out which one the game actually uses at a given point;
it just gets everything out where you can see and compare both versions
yourself. Same path appearing in multiple bundle subfolders is expected, not
a bug.

For scripting/testing, it also accepts the two paths as command-line arguments
to skip the dialogs: `dw2extract.exe "C:\...\Distant Worlds 2" "D:\output"`.

Heads up: this is a full-catalog extraction — the game's bundles total around
30-40 GB, and full conversion (especially mesh export) will take a while and use
significant disk space. There's no per-bundle filtering yet; it's all-or-nothing.

## Not yet covered

- `tx`, `txs`, `sx`, `mb`, `xu` are for the *reverse* direction (packing assets back
  into the game's format / building bundles) — out of scope for this tool and not
  something this pass touched.
- No tooling yet to distinguish DW2's own custom shaders from Stride's bundled
  engine shader library within a bulk extraction — `grep`/search the extracted
  `.sdsl` files' `namespace` line (DW2's own use `namespace DistantWorlds2`) or
  their companion `<name>.sdsl/path` file (Stride's point into a NuGet cache path,
  DW2's point into the original dev's project tree).
