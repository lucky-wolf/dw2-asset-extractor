# Distant Worlds 2 Asset Extractor (dw2extract)

A modding tool for [Distant Worlds 2](https://www.matrixgames.com/game/distant-worlds-2)
(a Slitherine/Matrix Games 4X built on the Stride/Xenko engine). Point it at your
DW2 install, pick which bundles and asset types you want, and it walks the
game's `.bundle` files and converts everything to standard formats on your
local disk: textures → `.dds`/`.png`, sounds → `.wav`, meshes → `.fbx`, shader
source and anything else copied as-is.

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
