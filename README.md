# Distant Worlds 2 Asset Extractor (dw2extract)

A modding tool for [Distant Worlds 2](https://www.matrixgames.com/game/distant-worlds-2)
(a Slitherine/Matrix Games 4X built on the Stride/Xenko engine). Point it at your
DW2 install, and it walks the game's `.bundle` files and converts everything to
standard formats on your local disk.

## The Easy Way

If you just want to build it and run it, use the repo-root helper scripts:

1. Run `build.bat`
2. Run `run.bat`

`build.bat` checks prerequisites, helps find your DW2 install, writes the local
build props file, restores NuGet packages, and builds the tool. `run.bat` then
launches the built extractor and captures a copy of the run output to
`LastRun.log`.

If your machine is already set up to run PowerShell scripts directly, you can use
the equivalent `.ps1` entrypoints instead:

1. Run `./build.ps1`
2. Run `./run.ps1`

The detailed/manual setup notes are still below if you want the full breakdown.

## What it does

Run `dw2extract.exe` and it walks you through two checklists, then asks where
to save the output:

- **Which bundles to pull from** — `CoreContent`, `Human`, `Abandoned`,
  `Creatures`, one per race/content pack, however many DW2 ships with. All
  selected by default; pick just one or two if you don't want the whole ~30-40
  GB catalog.
- **Which asset types to extract:**
  - Textures → `.dds` and/or `.png`, independently — pick one, the other, or
    both.
  - Sounds → `.wav`.
  - Meshes → `.fbx` — geometry, skeleton/hardpoint hierarchy, **and** its
    materials and textures, already linked. Diffuse, normal, and emissive maps
    are wired in automatically (this pulls in PNG export too, whether or not
    you checked it, since the mesh needs it), so a model opens in Blender
    already textured — not just bare gray geometry. See
    [`dw2-asset-extractor-source/README.md`](dw2-asset-extractor-source/README.md#running-it)
    for the details on what does and doesn't come through (e.g. Stride's
    packed PBR maps end up as custom material properties rather than a wrong
    texture slot, and skinned characters export their shape but aren't
    posable — no bone-weight data).
  - Everything else (shader source, XML/data files, ...) copied as-is, so you
    can still see what's in a bundle even for types this tool doesn't convert.

Output is organized one subfolder per bundle, mirroring the game's own
folder structure — and deliberately **not** deduplicated across bundles: DW2
routinely has more than one bundle declare its own version of the same asset
path, and this tool surfaces every one of them rather than guessing which the
game actually uses at runtime.

Your install folder and output folder are remembered between runs, so you're
not re-browsing for them every single time.

**Extraction/conversion only** — this tool does not repackage bundles or insert
anything back into the game.

There's no downloadable build here — `dw2extract.exe` links directly against
the real Xenko/Stride/DistantWorlds2 engine DLLs from your own DW2 install,
which are Slitherine/Matrix Games' property and can't be redistributed. Every
user builds their own copy (a couple of `dotnet` commands, no Visual Studio
required) against their own install — that's what the steps below walk you
through.

## Getting started

1. **Check the prerequisites below** — you'll need Distant Worlds 2 installed
   before step 3 (building) will work. Downloading in step 2 has no
   prerequisites.
2. **Download this repository.** Either:
   - On GitHub, click **Code → Download ZIP**, then extract it anywhere, or
   - Clone it: `git clone https://github.com/salemonz/dw2-asset-extractor.git`

   Everything here is source and docs (no game data, no built binaries), so
   this is a small download. It includes the
   [`dw2-asset-extractor-source/`](dw2-asset-extractor-source/) folder, which
   is what you'll build from.
3. **Open the `dw2-asset-extractor-source/` folder and follow its README**,
   starting at "One-time setup":
   [`dw2-asset-extractor-source/README.md`](dw2-asset-extractor-source/README.md).
   It walks you through pointing the build at your DW2 install, building with
   `dotnet`, and running the tool.

## Prerequisites

You'll need, before step 3 above will work:

- **Windows 10/11.** This tool doesn't run cross-platform — it targets
  `net8.0-windows` and links against Windows-only native libraries (Direct3D,
  the engine's own native DLLs, etc.).
- **Distant Worlds 2 installed** (Steam or GOG), and legitimately owned. This
  is required twice over: the *build* links against the engine DLLs sitting in
  your install folder, and the *tool itself* reads bundles from an install
  folder you point it at when it runs. You do **not** need to separately
  install Stride or Xenko (the engine DW2 is built on) — those DLLs already
  ship inside your DW2 install; nothing else to fetch there.
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** — needed
  to build. The `dotnet` command-line tool that ships with it is all the
  walkthrough uses; an IDE (Visual Studio, Rider, ...) is optional.
- **[FFmpeg](https://ffmpeg.org/download.html)** (`ffmpeg.exe` + `ffprobe.exe`)
  — needed for sound conversion and DDS→PNG conversion specifically. Without
  it, texture extraction still works (you just won't get `.png`, only `.dds`),
  but sound extraction fails outright. Put both on your `PATH`, or copy them
  next to the built `dw2extract.exe`.

## Origin

This project started as an expansion of **Distant Worlds 2 Bundle Manager
(`dw2bm`)**, a precise per-asset extraction CLI originally created by
**Tyler Young** ([Tyler-IN](https://github.com/Tyler-IN)) for the
[Distant Worlds 2 Mod Community (DW2MC)](https://github.com/DW2MC/DistantWorlds2.BundleManager).
That tool's low-level bundle-reading groundwork made this project possible.

What began as fixes and additions to `dw2bm` grew into a separate bulk
"extract everything" tool with broader format support (including mesh export
to FBX, which `dw2bm` never had), and this repo now focuses solely on that
tool — the original `dw2bm` per-asset CLI lives on in its own project and
isn't included here.

## Development

Built with substantial assistance from [Claude Code](https://claude.com/claude-code)
(Anthropic), used throughout for implementation, debugging (including
verification against real game data and a live Blender instance for the mesh
exporter), and documentation.
