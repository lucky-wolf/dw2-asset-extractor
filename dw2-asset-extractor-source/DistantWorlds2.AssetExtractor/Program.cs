using System.Reflection;
using System.Windows.Forms;
using DistantWorlds2.Core;
using Xenko.Core.IO;
using Xenko.Core.Serialization.Contents;
using Xenko.Core.Storage;

namespace DistantWorlds2.AssetExtractor;

public static class Program
{
    private const string VfsRoot = "/dw2install";

    [Flags]
    private enum AssetKindFlags
    {
        None = 0,
        Dds = 1 << 0,
        Png = 1 << 1,
        Wav = 1 << 2,
        Fbx = 1 << 3,
        Misc = 1 << 4,
        All = Dds | Png | Wav | Fbx | Misc,
    }

    private static readonly (string Label, AssetKindFlags Flag)[] AssetTypeOptions =
    [
        ("Textures - DDS (.dds)", AssetKindFlags.Dds),
        ("Textures - PNG (.png, converted from DDS)", AssetKindFlags.Png),
        ("Sounds (.wav)", AssetKindFlags.Wav),
        ("Meshes (.fbx)", AssetKindFlags.Fbx),
        ("Everything else / unsupported (copied as-is)", AssetKindFlags.Misc),
    ];

    public static async Task<int> Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, new AssemblyName(resolveArgs.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        Console.WriteLine("Distant Worlds 2 Asset Extractor");
        Console.WriteLine("Extracts and converts every asset from every bundle: textures -> .dds, sounds -> .wav,");
        Console.WriteLine("meshes -> .fbx, everything else copied as-is (shader source, data files, ...).");
        Console.WriteLine();

        // Optional non-interactive form (dw2extract.exe <installDir> <outputDir> [bundleNameFilter]) for
        // scripting/testing; with no arguments it falls back to the folder-picker dialogs for normal
        // interactive use. The optional 3rd argument limits the run to bundles whose name contains it
        // (case-insensitive) — handy for re-running just one bundle instead of the whole catalog.
        string? installDir = args.Length is 2 or 3 ? args[0] : null;
        string? outputDir = args.Length is 2 or 3 ? args[1] : null;
        string? bundleFilter = args.Length == 3 ? args[2] : null;

        bool interactive = installDir == null;
        var settings = interactive ? UserSettings.Load() : null;

        if (installDir == null)
        {
            var savedInstallDir = settings!.InstallDir;
            if (savedInstallDir != null && !File.Exists(Path.Combine(savedInstallDir, "DistantWorlds2.exe")))
                savedInstallDir = null; // no longer a valid DW2 install, don't offer to reuse it

            installDir = ConfirmOrPickFolder(savedInstallDir,
                "Use your previously selected Distant Worlds 2 install folder?",
                "Select your Distant Worlds 2 install folder (contains DistantWorlds2.exe)");
        }
        if (installDir == null)
        {
            Console.WriteLine("Cancelled.");
            return 1;
        }
        if (!File.Exists(Path.Combine(installDir, "DistantWorlds2.exe")))
        {
            Console.Error.WriteLine($"'{installDir}' doesn't look like a Distant Worlds 2 install (DistantWorlds2.exe not found there).");
            return 1;
        }

        var bundlesDir = Path.Combine(installDir, "data", "db", "bundles");
        if (!Directory.Exists(bundlesDir))
        {
            Console.Error.WriteLine($"No bundles found at '{bundlesDir}'.");
            return 1;
        }

        var allBundles = BundleCatalog.ListBundleNames(bundlesDir).OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
        if (allBundles.Count == 0)
        {
            Console.Error.WriteLine($"No bundles found at '{bundlesDir}'.");
            return 1;
        }

        List<string> selectedBundles;
        if (bundleFilter != null)
        {
            // Scripted form: substring filter, no dialog.
            selectedBundles = allBundles.Where(b => b.Contains(bundleFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (selectedBundles.Count == 0)
            {
                Console.Error.WriteLine($"No bundles match filter '{bundleFilter}'.");
                return 1;
            }
        }
        else if (!interactive)
        {
            // Scripted form with no filter: extract everything, no dialog.
            selectedBundles = allBundles;
        }
        else
        {
            var picked = PickBundles(allBundles);
            if (picked == null || picked.Count == 0)
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
            selectedBundles = picked;
        }

        AssetKindFlags assetTypes;
        if (!interactive)
        {
            // Scripted form: extract every supported asset type, no dialog.
            assetTypes = AssetKindFlags.All;
        }
        else
        {
            var pickedTypes = PickAssetTypes();
            if (pickedTypes is null or AssetKindFlags.None)
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
            assetTypes = pickedTypes.Value;
        }

        if (outputDir == null)
        {
            outputDir = ConfirmOrPickFolder(settings!.OutputDir,
                "Use your previously selected output folder?",
                "Select where extracted assets should be saved");
        }
        if (outputDir == null)
        {
            Console.WriteLine("Cancelled.");
            return 1;
        }

        if (interactive)
        {
            settings!.InstallDir = installDir;
            settings.OutputDir = outputDir;
            settings.Save();
        }

        Console.WriteLine();
        Console.WriteLine($"Install:  {installDir}");
        Console.WriteLine($"Output:   {outputDir}");
        Console.WriteLine($"Bundles:  {selectedBundles.Count} of {allBundles.Count} selected");
        Console.WriteLine($"Types:    {string.Join(", ", AssetTypeOptions.Where(o => assetTypes.HasFlag(o.Flag)).Select(o => o.Label))}");
        Console.WriteLine();

        return await Extract(installDir, outputDir, selectedBundles, assetTypes);
    }

    private static string? PickFolderCore(string description)
    {
        using var dialog = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    // If savedPath is set, asks the user (Yes/No/Cancel) whether to reuse it before falling back to the
    // normal folder-browser dialog on "No". Returns null on Cancel or if the browser dialog is dismissed.
    private static string? ConfirmOrPickFolder(string? savedPath, string confirmMessage, string pickerDescription)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            if (savedPath != null)
            {
                var choice = MessageBox.Show(
                    $"{confirmMessage}\n\n{savedPath}",
                    "Distant Worlds 2 Asset Extractor",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Yes)
                {
                    result = savedPath;
                    return;
                }
                if (choice == DialogResult.Cancel)
                    return;
                // No -> fall through to the folder browser below.
            }

            result = PickFolderCore(pickerDescription);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static List<string>? PickBundles(List<string> allBundles)
    {
        List<string>? result = null;
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                Text = "Select bundles to extract",
                StartPosition = FormStartPosition.CenterScreen,
                Width = 420,
                Height = 520,
                MinimizeBox = false,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
            };

            var listBox = new CheckedListBox
            {
                Left = 10,
                Top = 10,
                Width = 380,
                Height = 400,
                CheckOnClick = true,
                IntegralHeight = false,
            };
            foreach (var bundleName in allBundles)
                listBox.Items.Add(bundleName, true);

            var selectAllButton = new Button { Text = "Select All", Left = 10, Top = 420, Width = 90 };
            selectAllButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, true);
            };

            var selectNoneButton = new Button { Text = "Select None", Left = 105, Top = 420, Width = 90 };
            selectNoneButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, false);
            };

            var okButton = new Button { Text = "OK", Left = 220, Top = 420, Width = 80, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 305, Top = 420, Width = 85, DialogResult = DialogResult.Cancel };

            form.Controls.Add(listBox);
            form.Controls.Add(selectAllButton);
            form.Controls.Add(selectNoneButton);
            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() == DialogResult.OK)
                result = listBox.CheckedItems.Cast<string>().ToList();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static AssetKindFlags? PickAssetTypes()
    {
        AssetKindFlags? result = null;
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                Text = "Select asset types to extract",
                StartPosition = FormStartPosition.CenterScreen,
                Width = 420,
                Height = 300,
                MinimizeBox = false,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
            };

            var listBox = new CheckedListBox
            {
                Left = 10,
                Top = 10,
                Width = 380,
                Height = 180,
                CheckOnClick = true,
                IntegralHeight = false,
            };
            foreach (var (label, _) in AssetTypeOptions)
                listBox.Items.Add(label, true);

            var selectAllButton = new Button { Text = "Select All", Left = 10, Top = 200, Width = 90 };
            selectAllButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, true);
            };

            var selectNoneButton = new Button { Text = "Select None", Left = 105, Top = 200, Width = 90 };
            selectNoneButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, false);
            };

            var okButton = new Button { Text = "OK", Left = 220, Top = 200, Width = 80, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 305, Top = 200, Width = 85, DialogResult = DialogResult.Cancel };

            form.Controls.Add(listBox);
            form.Controls.Add(selectAllButton);
            form.Controls.Add(selectNoneButton);
            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() == DialogResult.OK)
            {
                var flags = AssetKindFlags.None;
                for (int i = 0; i < AssetTypeOptions.Length; i++)
                    if (listBox.GetItemChecked(i))
                        flags |= AssetTypeOptions[i].Flag;
                result = flags;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static async Task<int> Extract(string installDir, string outputDir, List<string> selectedBundles, AssetKindFlags assetTypes)
    {
        VirtualFileSystem.MountFileSystem(VfsRoot, installDir); // Xenko-side (bundle container plumbing)
        Stride.Core.IO.VirtualFileSystem.MountFileSystem(VfsRoot, installDir); // Stride-side (MeshExporter's ContentManager)

        // DW2 routinely lets a later-loaded bundle re-declare (override) an asset path that an earlier
        // one also declares — e.g. CoreContent and Human can both have their own "Ships/Human/..." entry,
        // and which one the game actually uses at runtime depends on load order we're not trying to
        // reconstruct here. This tool's job is just to get everything out where you can see it, so each
        // bundle's own declared asset list (its BundleDescription.Assets — literally what that bundle's
        // own file says it contains, with no dependency-merging) goes into its own subfolder, full stop.
        // No cross-bundle deduplication: the same path can legitimately appear once per bundle that
        // declares it.
        var stats = new Stats();

        foreach (var bundleName in selectedBundles)
        {
            Console.WriteLine($"=== {bundleName} ===");

            List<string> ownAssetUrls;
            try
            {
                var bundleFileUrl = $"{VfsRoot}/data/db/bundles/{bundleName}.bundle";
                var description = Stride.Core.Storage.BundleOdbBackend.ReadBundleHeader(bundleFileUrl, out _);
                ownAssetUrls = description.Assets.Select(kv => kv.Key).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Failed to read bundle header for '{bundleName}': {ex.Message}");
                continue;
            }

            // Still loaded the normal (dependency-resolving) way — we need that to actually read asset
            // bytes, since e.g. a Model's Buffer references are resolved through the loaded object graph.
            // Only *which* asset URLs we process for this bundle comes from its own declared list above.
            ObjectDatabase odb;
            try
            {
                odb = new ObjectDatabase($"{VfsRoot}/data/db", "index", $"{VfsRoot}/data/db");
                await odb.LoadBundle(bundleName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Failed to load bundle '{bundleName}': {ex.Message}");
                continue;
            }

            using (odb)
            {
                var fileProvider = new DatabaseFileProvider(odb);

                foreach (var url in ownAssetUrls)
                {
                    if (url.EndsWith("_Data", StringComparison.Ordinal) || url.EndsWith("/path", StringComparison.Ordinal))
                        continue; // companions, handled by their primary asset

                    await ExtractOne(odb, fileProvider, bundleName, url, outputDir, assetTypes, stats);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {stats.Textures} textures, {stats.Sounds} sounds, {stats.Meshes} meshes, {stats.Raw} other files copied, {stats.Failures} failed.");
        return 0;
    }

    private static async Task ExtractOne(ObjectDatabase odb, DatabaseFileProvider fileProvider, string bundleName, string url, string outputDir, AssetKindFlags assetTypes, Stats stats)
    {
        var destBase = Path.Combine(outputDir, bundleName, SanitizePath(url));

        try
        {
            using var probeStream = fileProvider.OpenStream(url, VirtualFileMode.Open, VirtualFileAccess.Read, VirtualFileShare.Read, StreamFlags.Seekable);
            var chunkType = AssetTypeIdentifier.TryGetChunkType(probeStream);
            var kind = AssetTypeIdentifier.Classify(chunkType);

            switch (kind)
            {
                case DW2AssetKind.Skeleton:
                    // Pulled in automatically by whichever Model references it — not a useful standalone export.
                    return;

                case DW2AssetKind.Texture:
                {
                    var wantDds = assetTypes.HasFlag(AssetKindFlags.Dds);
                    var wantPng = assetTypes.HasFlag(AssetKindFlags.Png);
                    if (!wantDds && !wantPng)
                        return;

                    var rawPath = destBase + ".raw";
                    if (!await BundleExtractor.ExtractRaw(odb, fileProvider, url, rawPath))
                        throw new IOException("extract failed");
                    TextureConverter.XenkoToDds(rawPath, destBase + ".dds");
                    CleanupRaw(rawPath);
                    File.Delete(destBase + ".dds.refs");

                    var pngOk = false;
                    if (wantPng)
                    {
                        try
                        {
                            TextureConverter.DdsToPng(destBase + ".dds", destBase + ".png");
                            pngOk = true;
                        }
                        catch (Exception ex)
                        {
                            // Some DDS variants (e.g. certain cubemap/array layouts) may not have a PNG-savable
                            // path in Xenko's Image.Save — the .dds itself already succeeded, so don't fail the
                            // whole asset over a missing PNG. Since PNG conversion failed, keep the .dds around
                            // regardless of whether the user asked for it, so the asset isn't lost entirely.
                            Console.Error.WriteLine($"  (no PNG for {url}: {ex.Message})");
                        }
                    }

                    if (!wantDds && pngOk)
                        File.Delete(destBase + ".dds");

                    stats.Textures++;
                    break;
                }

                case DW2AssetKind.Sound:
                {
                    if (!assetTypes.HasFlag(AssetKindFlags.Wav))
                        return;

                    var rawPath = destBase + ".raw";
                    if (!await BundleExtractor.ExtractRaw(odb, fileProvider, url, rawPath))
                        throw new IOException("extract failed");
                    SoundConverter.XenkoToSoundfile(rawPath, destBase + ".wav");
                    CleanupRaw(rawPath);
                    stats.Sounds++;
                    break;
                }

                case DW2AssetKind.Model:
                    if (!assetTypes.HasFlag(AssetKindFlags.Fbx))
                        return;
                    await MeshExporter.ExportToFbx(bundleName, url, destBase + ".fbx", VfsRoot);
                    stats.Meshes++;
                    break;

                case DW2AssetKind.RawOrUnknown:
                default:
                    if (!assetTypes.HasFlag(AssetKindFlags.Misc))
                        return;
                    if (!await BundleExtractor.ExtractRaw(odb, fileProvider, url, destBase))
                        throw new IOException("extract failed");
                    stats.Raw++;
                    break;
            }
        }
        catch (Exception ex)
        {
            stats.Failures++;
            Console.Error.WriteLine($"  FAILED {url}: {ex.Message}");
        }
    }

    private static void CleanupRaw(string rawPath)
    {
        File.Delete(rawPath);
        if (File.Exists(rawPath + "_Data"))
            File.Delete(rawPath + "_Data");
    }

    private static string SanitizePath(string url)
    {
        var parts = url.Split('/');
        var invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < parts.Length; i++)
            foreach (var c in invalid)
                parts[i] = parts[i].Replace(c, '_');
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private class Stats
    {
        public int Textures, Sounds, Meshes, Raw, Failures;
    }
}
