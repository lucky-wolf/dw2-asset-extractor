# Distant Worlds 2 Bundle Tool (DW2BT) — project context

Modding tool suite for Distant Worlds 2 (a Slitherine/Matrix Games 4X built on the
Stride/Xenko engine). Extracts assets from the game's `.bundle` files and converts
them to standard formats. **Extraction/conversion only** — no repackaging, no
inserting modified assets back into the game. That's a deliberate scope boundary,
not a gap.

## Directory layout

```
dw2-asset-extractor/                  <- primary working directory
├── DW2BT Source/                     <- all source code, see README.md there for build/usage instructions
│   ├── DistantWorlds2.BundleManager/ <- dw2bm: precise per-asset CLI (original tool, heavily fixed)
│   ├── DistantWorlds2.Core/          <- shared conversion logic both exes use
│   ├── DistantWorlds2.AssetExtractor/<- dw2extract: bulk "extract everything" tool (new this project)
│   ├── DW2BT.Common.props            <- shared engine-linking MSBuild setup, imported by both exe projects
│   ├── DW2BT.local.props             <- gitignored, machine-specific: <DW2GameDir>C:\Steam\steamapps\common\Distant Worlds 2</DW2GameDir>
│   └── ref/                          <- OLD stub assemblies, superseded, no longer referenced by anything
├── dw2bm/                            <- published Release build of dw2bm.exe, ready to run
├── dw2extract/                       <- published Release build of dw2extract.exe, ready to run
└── test-workspace/                   <- local test setup with a junction to real bundles (gitignored)
```

**Read `DW2BT Source/README.md` first** — it has full build/setup/usage instructions for both tools and is kept up to date. This file is about *development* context that isn't there.

## Critical technical findings (don't re-derive these — they took significant investigation)

- **The game's engine is a Xenko/Stride hybrid.** DW2 ships both `Xenko.*.dll` (old branding) and `Stride.*.dll` (current) side by side. The low-level bundle/container format (`ObjectDatabase`, `ChunkHeader`, `BinarySerializationReader`) is Xenko-namespaced and byte-compatible with real data. But the actual content *types* — `Model`, `Sound`, `Texture`, `Skeleton` — are Stride-namespaced. The original DW2BT source (pre-this-project) only knew about Xenko types, which is why sound conversion silently no-op'd and mesh conversion didn't exist at all.
- **The tools target `net8.0-windows`**, not the original `net472` — the real engine DLLs are compiled for .NET 8 (confirmed via the game's own `runtimeconfig.json`) with strong-name signing incompatible with .NET Framework's binder. This isn't optional; net472 cannot load these DLLs.
- **Engine DLLs can't be committed to the repo** (Slitherine/Matrix IP, not a redistributable SDK). Every project imports `DW2BT.Common.props`, which pulls `Reference`s from `$(DW2GameDir)` (set in the gitignored `DW2BT.local.props`). Native libs (`x64\*.dll`, `win-x64\*.dll` in the game folder — Xenko's and Stride's native libs live in different folders but both get flattened into one `x64\` next to the built exe) get copied automatically via an MSBuild target in that same props file.
- **`dw2bm` resolves bundles relative to its own exe folder** (`<exe dir>\data\db\bundles\`) — that's baked into Xenko's `VirtualFileSystem` design, not something we control. Testing requires a directory *junction* there pointing at real bundle data.
- **`dw2extract` avoids that constraint** — it mounts whatever install folder the user picks at runtime via `VirtualFileSystem.MountFileSystem` (both Xenko's *and* Stride's VFS — they're independent static registries, mesh export goes through Stride's own `ContentManager` which needs its own mount).
- **Mesh export**: `Model`/`Skeleton` must be deserialized via Stride's own `ContentManager` (not Xenko's), with a `ContentFilter` restricted to `Buffer` types only (loading Texture/Material references would need a live GraphicsDevice we don't have). Raw vertex/index bytes come from `Buffer.GetSerializationData().Content` — no GPU device needed. FBX output is a **from-scratch binary FBX 7.4 writer** (`FbxBinaryWriter.cs`) — Blender's importer rejects ASCII FBX outright (dropped support years ago), so this had to be binary, hand-rolled (no FBX SDK dependency). Verified extensively against a live Blender instance via MCP (`mcp__blender__*` tools) — vertex data, node hierarchy, and rendered shape all confirmed correct for both a skinned character and a rigid-hardpoint ship hull. No bone-weight/skinning (deformer) export — not needed for ship hardpoint structure, which was the actual goal; a skinned model exports its shape correctly but won't be posable.
- **Texture → PNG must go through FFmpeg, not Xenko's own `Image.Save(..., Png)`.** That native path has two real bugs: it crashes the whole process (unmanaged, uncatchable) on floating-point/HDR pixel formats (e.g. BRDF LUTs), and it silently mis-decodes at least some block-compressed textures into a valid-looking but visually wrong PNG. FFmpeg's DDS decoder handles both correctly.
- **`Process` stdout/stderr redirection gotcha**: if you redirect both without draining them, it deadlocks the instant the child process's output fills the OS pipe buffer. Bit us once on the FFmpeg PNG conversion (hung for 25 minutes before caught). Don't redirect unless you're actually reading the streams.
- **`dw2extract`'s per-bundle behavior is deliberate, not a bug**: each bundle's output subfolder contains exactly that bundle's *own* declared assets (`BundleDescription.Assets`, read via `BundleOdbBackend.ReadBundleHeader` — no dependency-merging). DW2 routinely has later bundles override/re-declare a path an earlier bundle also declares (e.g. both `CoreContent` and `Human` can have their own version of the same path). This tool doesn't try to resolve which one the game actually uses — it surfaces all of them, once per declaring bundle, so the user can compare. Do not "fix" this into a deduplicating/merging design; that was explicitly rejected.
- **FFmpeg discovery checks the exe's own directory first** (`AppContext.BaseDirectory`), not just CWD/PATH — a CWD-only check is fragile against shortcuts/scripts that launch from a different working directory. Both `SoundConverter` and `TextureConverter` in `DistantWorlds2.Core` do this now.

## Working conventions established this session

- **Verify against real game data, not assumptions.** The user's game install is at `C:\Steam\steamapps\common\Distant Worlds 2`. Most debugging in this project involved building small scratch test harnesses, hardlinking specific bundles into a `mini-install` test folder (see `test-workspace/`), and running the actual tools against real data rather than reasoning abstractly.
- **When something looks visually wrong in an extracted asset, verify with a hash/byte comparison before concluding it's a bug** — one investigation this session chased a false alarm because a stylized, painterly game texture was misjudged as "corrupted" from visual memory alone.
- Both `dw2bm` and `dw2extract` published Release folders (`dw2bm/`, `dw2extract/` at the project root) are kept up to date and ready to run — re-publish after Core changes with:
  ```
  dotnet publish DistantWorlds2.AssetExtractor/DistantWorlds2.AssetExtractor.csproj -c Release -o "../dw2extract"
  dotnet publish DistantWorlds2.BundleManager/DistantWorlds2.BundleManager.csproj -c Release -o "../dw2bm"
  ```
  (native libs and FFmpeg need to already exist there or be re-copied — the MSBuild target handles native libs automatically on publish now; FFmpeg was copied in manually, see README.)

## Known gaps / possible follow-ups (not started)

- No tooling yet to distinguish DW2's own custom shaders from Stride's bundled engine shader library in bulk (manual: check the `namespace` line in extracted `.sdsl`, or the companion `<name>.sdsl/path` file's dev-machine path).
- `dw2extract` has no per-bundle progress/ETA reporting for the full ~30-40GB catalog.
- A `cubemap_01`-style asset (cross-bundle dependency) fails in both tools with "Unable to find the specified file" — known, low priority, not investigated further.
- No skinning/deformer export in the FBX writer (see above — deliberate for now, not forgotten).
