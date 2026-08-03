# Distant Worlds 2 Asset Extractor (dw2extract)

A modding tool for [Distant Worlds 2](https://www.matrixgames.com/game/distant-worlds-2)
(a Slitherine/Matrix Games 4X built on the Stride/Xenko engine). Point it at your
DW2 install, pick which bundles and asset types you want, and it walks the
game's `.bundle` files and converts everything to standard formats on your
local disk: textures → `.dds`/`.png`, sounds → `.wav`, meshes → `.fbx`, shader
source and anything else copied as-is.

**Extraction/conversion only** — this tool does not repackage bundles or insert
anything back into the game.

See [`DW2BT Source/README.md`](DW2BT%20Source/README.md) for build setup and
full usage instructions. A ready-to-run published build lives in
[`dw2extract/`](dw2extract/).

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
