using System.Diagnostics;
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
    private const int BundleProgressLogInterval = 250;

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
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        RunLogger.Initialize(ResolveLogPath());

        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, new AssemblyName(resolveArgs.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };

            RunLogger.Info("Distant Worlds 2 Asset Extractor");
            RunLogger.Info("Extracts and converts every asset from every bundle: textures -> .dds, sounds -> .wav,");
            RunLogger.Info("meshes -> .fbx, everything else copied as-is (shader source, data files, ...).");
            RunLogger.Info(string.Empty);

            var options = ParseArgs(args);
            string? installDir = options.InstallDir;
            string? outputDir = options.OutputDir;
            string? bundleFilter = options.BundleFilter;

            var settings = UserSettings.Load();
            TextureConverter.SetFfmpegTimeoutSeconds(settings.GetTextureFfmpegTimeoutSeconds());
            SoundConverter.SetFfmpegTimeoutSeconds(settings.GetSoundFfmpegTimeoutSeconds());
            var maxParallelBundles = options.MaxParallelBundles ?? settings.GetMaxParallelBundles();
            var verbose = options.Verbose;

            bool interactive = installDir == null;

            if (installDir == null)
            {
                var savedInstallDir = settings.InstallDir;
                if (savedInstallDir != null && !File.Exists(Path.Combine(savedInstallDir, "DistantWorlds2.exe")))
                    savedInstallDir = null; // no longer a valid DW2 install, don't offer to reuse it

                installDir = ConfirmOrPickFolder(savedInstallDir,
                    "Use your previously selected Distant Worlds 2 install folder?",
                    "Select your Distant Worlds 2 install folder (contains DistantWorlds2.exe)");
            }
            if (installDir == null)
            {
                RunLogger.Info("Cancelled.");
                return 1;
            }
            if (!File.Exists(Path.Combine(installDir, "DistantWorlds2.exe")))
            {
                RunLogger.Error($"'{installDir}' doesn't look like a Distant Worlds 2 install (DistantWorlds2.exe not found there).");
                return 1;
            }

            if (outputDir == null)
            {
                outputDir = ConfirmOrPickFolder(settings.OutputDir,
                    "Use your previously selected output folder?",
                    "Select where extracted assets should be saved");
            }
            if (outputDir == null)
            {
                RunLogger.Info("Cancelled.");
                return 1;
            }

            var bundlesDir = Path.Combine(installDir, "data", "db", "bundles");
            if (!Directory.Exists(bundlesDir))
            {
                RunLogger.Error($"No bundles found at '{bundlesDir}'.");
                return 1;
            }

            var allBundles = BundleCatalog.ListBundleNames(bundlesDir).OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
            if (allBundles.Count == 0)
            {
                RunLogger.Error($"No bundles found at '{bundlesDir}'.");
                return 1;
            }

            List<string> selectedBundles;
            if (bundleFilter != null)
            {
                selectedBundles = allBundles.Where(b => b.Contains(bundleFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                if (selectedBundles.Count == 0)
                {
                    RunLogger.Error($"No bundles match filter '{bundleFilter}'.");
                    return 1;
                }
            }
            else if (!interactive)
            {
                selectedBundles = allBundles;
            }
            else
            {
                var picked = PickBundles(allBundles);
                if (picked == null || picked.Count == 0)
                {
                    RunLogger.Info("Cancelled.");
                    return 1;
                }
                selectedBundles = picked;
            }

            AssetKindFlags assetTypes;
            if (!interactive)
            {
                assetTypes = AssetKindFlags.All;
            }
            else
            {
                var pickedTypes = PickAssetTypes();
                if (pickedTypes is null or AssetKindFlags.None)
                {
                    RunLogger.Info("Cancelled.");
                    return 1;
                }
                assetTypes = pickedTypes.Value;
            }

            if (assetTypes.HasFlag(AssetKindFlags.Fbx))
                assetTypes |= AssetKindFlags.Png;

            if (interactive)
            {
                settings.InstallDir = installDir;
                settings.OutputDir = outputDir;
                settings.Save();
            }

            RunLogger.Info(string.Empty);
            RunLogger.Info($"Install:   {installDir}");
            RunLogger.Info($"Output:    {outputDir}");
            RunLogger.Info($"Bundles:   {selectedBundles.Count} of {allBundles.Count} selected");
            RunLogger.Info($"Types:     {string.Join(", ", AssetTypeOptions.Where(o => assetTypes.HasFlag(o.Flag)).Select(o => o.Label))}");
            RunLogger.Info($"Parallel:  {maxParallelBundles} bundle worker(s)");
            if (verbose)
                RunLogger.Info("Verbose:   enabled");
            RunLogger.Info(string.Empty);

            return await Extract(installDir, outputDir, selectedBundles, assetTypes, maxParallelBundles, verbose);
        }
        finally
        {
            RunLogger.Dispose();
        }
    }

    private sealed class CommandLineOptions
    {
        public string? InstallDir { get; init; }
        public string? OutputDir { get; init; }
        public string? BundleFilter { get; init; }
        public int? MaxParallelBundles { get; init; }
        public bool Verbose { get; init; }
    }

    private static CommandLineOptions ParseArgs(string[] args)
    {
        var positional = new List<string>();
        int? maxParallelBundles = null;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-j" or "--jobs" or "--max-parallelism" or "--max-parallel-bundles")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsed) || parsed <= 0)
                    throw new ArgumentException($"{arg} requires a positive integer value.");
                maxParallelBundles = parsed;
                continue;
            }

            if (arg is "-v" or "--verbose")
            {
                verbose = true;
                continue;
            }

            positional.Add(arg);
        }

        if (positional.Count != 0 && positional.Count != 2 && positional.Count != 3)
            throw new ArgumentException("Usage: dw2extract.exe [<installDir> <outputDir> [bundleNameFilter]] [-j <count>] [-v]");

        return new CommandLineOptions
        {
            InstallDir = positional.Count >= 2 ? positional[0] : null,
            OutputDir = positional.Count >= 2 ? positional[1] : null,
            BundleFilter = positional.Count == 3 ? positional[2] : null,
            MaxParallelBundles = maxParallelBundles,
            Verbose = verbose,
        };
    }

    private static string ResolveLogPath()
    {
        var envPath = Environment.GetEnvironmentVariable("DW2EXTRACT_LOG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;
        return Path.Combine(AppContext.BaseDirectory, "dw2extractor.log");
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

            var selectAllButton = new Button { Text = "All", Left = 10, Top = 432, Width = 90, Height = 36 };
            selectAllButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, true);
            };

            var selectNoneButton = new Button { Text = "Clear", Left = 105, Top = 432, Width = 90, Height = 36 };
            selectNoneButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, false);
            };

            var okButton = new Button { Text = "OK", Left = 220, Top = 432, Width = 80, Height = 36, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 305, Top = 432, Width = 85, Height = 36, DialogResult = DialogResult.Cancel };

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

            // FBX export needs PNG textures to link materials to (see MeshExporter) — nudge the user
            // toward that up front rather than silently overriding their choice after the fact. This is
            // a one-way convenience, not a hard lock: PNG can still be unchecked afterward, but Main()
            // forces it back on before extraction if FBX ends up selected regardless.
            var fbxIndex = Array.FindIndex(AssetTypeOptions, o => o.Flag == AssetKindFlags.Fbx);
            var pngIndex = Array.FindIndex(AssetTypeOptions, o => o.Flag == AssetKindFlags.Png);
            listBox.ItemCheck += (_, e) =>
            {
                if (e.Index == fbxIndex && e.NewValue == CheckState.Checked)
                    listBox.SetItemChecked(pngIndex, true);
            };

            var selectAllButton = new Button { Text = "All", Left = 10, Top = 212, Width = 90, Height = 36 };
            selectAllButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, true);
            };

            var selectNoneButton = new Button { Text = "Clear", Left = 105, Top = 212, Width = 90, Height = 36 };
            selectNoneButton.Click += (_, _) =>
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                    listBox.SetItemChecked(i, false);
            };

            var okButton = new Button { Text = "OK", Left = 220, Top = 212, Width = 80, Height = 36, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 305, Top = 212, Width = 85, Height = 36, DialogResult = DialogResult.Cancel };

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

    private static async Task<int> Extract(string installDir, string outputDir, List<string> selectedBundles, AssetKindFlags assetTypes, int maxParallelBundles, bool verbose)
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
        var statsLock = new object();
        var activeBundles = 0;
        var maxObservedConcurrentBundles = 0;
        var totalStopwatch = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            selectedBundles,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelBundles },
            async (bundleName, _) =>
            {
                var bundleStopwatch = Stopwatch.StartNew();
                var currentActive = Interlocked.Increment(ref activeBundles);
                UpdateMaxObserved(ref maxObservedConcurrentBundles, currentActive);
                if (verbose)
                    RunLogger.Info($"[bundle-start] {bundleName} active={currentActive}/{maxParallelBundles}");

                var bundleStats = await ExtractBundle(bundleName, outputDir, assetTypes);
                lock (statsLock)
                {
                    stats.MergeFrom(bundleStats);
                }

                var remainingActive = Interlocked.Decrement(ref activeBundles);
                if (verbose)
                    RunLogger.Info($"[bundle-done]  {bundleName} elapsed={bundleStopwatch.Elapsed:hh\\:mm\\:ss} success={bundleStats.SuccessfulAssets} failed={bundleStats.Failures} active={remainingActive}/{maxParallelBundles}");
            });

        var successfulAssets = stats.Textures + stats.Sounds + stats.Meshes + stats.Raw;
        var attemptedAssets = successfulAssets + stats.Failures;

        RunLogger.Info(string.Empty);
        RunLogger.Info($"Done. {successfulAssets} assets succeeded, {stats.Failures} failed, {attemptedAssets} total attempted.");
        RunLogger.Info($"Successful assets by type: {stats.Textures} textures, {stats.Sounds} sounds, {stats.Meshes} meshes, {stats.Raw} other.");
        RunLogger.Info($"Output files created: {stats.DdsFiles} dds, {stats.PngFiles} png, {stats.WavFiles} wav, {stats.FbxFiles} fbx, {stats.OtherFiles} other.");
        RunLogger.Info($"Average time per successful asset: textures {stats.GetAverageMilliseconds(stats.TextureMilliseconds, stats.Textures):0.0} ms, sounds {stats.GetAverageMilliseconds(stats.SoundMilliseconds, stats.Sounds):0.0} ms, meshes {stats.GetAverageMilliseconds(stats.MeshMilliseconds, stats.Meshes):0.0} ms, other {stats.GetAverageMilliseconds(stats.RawMilliseconds, stats.Raw):0.0} ms.");
        if (verbose)
            RunLogger.Info($"Parallel efficacy: max concurrent bundles observed {maxObservedConcurrentBundles}/{maxParallelBundles}, total elapsed {totalStopwatch.Elapsed:hh\\:mm\\:ss}.");

        if (stats.Failures > 0)
        {
            RunLogger.Info(string.Empty);
            RunLogger.Yellow("Failures:");
            foreach (var failure in stats.FailureDetails)
                RunLogger.Yellow($"  - {failure}");
        }

        return 0;
    }

    private static void UpdateMaxObserved(ref int maxObserved, int candidate)
    {
        while (true)
        {
            var current = maxObserved;
            if (candidate <= current)
                return;
            if (Interlocked.CompareExchange(ref maxObserved, candidate, current) == current)
                return;
        }
    }

    private static async Task<Stats> ExtractBundle(string bundleName, string outputDir, AssetKindFlags assetTypes)
    {
        var stats = new Stats();

        List<string> ownAssetUrls;
        try
        {
            var bundleFileUrl = $"{VfsRoot}/data/db/bundles/{bundleName}.bundle";
            var description = Stride.Core.Storage.BundleOdbBackend.ReadBundleHeader(bundleFileUrl, out _);
            ownAssetUrls = description.Assets.Select(kv => kv.Key).ToList();
        }
        catch (Exception ex)
        {
            stats.Failures++;
            stats.FailureDetails.Add($"{bundleName}:(bundle header) -> {ex.Message}");
            RunLogger.Error($"  Failed to read bundle header for '{bundleName}': {ex.Message}");
            return stats;
        }

        ObjectDatabase odb;
        try
        {
            odb = new ObjectDatabase($"{VfsRoot}/data/db", "index", $"{VfsRoot}/data/db");
            await odb.LoadBundle(bundleName);
        }
        catch (Exception ex)
        {
            stats.Failures++;
            stats.FailureDetails.Add($"{bundleName}:(bundle load) -> {ex.Message}");
            RunLogger.Error($"  Failed to load bundle '{bundleName}': {ex.Message}");
            return stats;
        }

        var totalAssets = ownAssetUrls.Count(url => !url.EndsWith("_Data", StringComparison.Ordinal) && !url.EndsWith("/path", StringComparison.Ordinal));
        var processedAssets = 0;

        using (odb)
        {
            var fileProvider = new DatabaseFileProvider(odb);

            foreach (var url in ownAssetUrls)
            {
                if (url.EndsWith("_Data", StringComparison.Ordinal) || url.EndsWith("/path", StringComparison.Ordinal))
                    continue;

                await ExtractOne(odb, fileProvider, bundleName, url, outputDir, assetTypes, stats);
                processedAssets++;

                if (processedAssets == totalAssets || processedAssets % BundleProgressLogInterval == 0)
                    RunLogger.Info($"  [{bundleName}] {processedAssets}/{totalAssets} assets processed");
            }
        }

        return stats;
    }

    private static async Task ExtractOne(ObjectDatabase odb, DatabaseFileProvider fileProvider, string bundleName, string url, string outputDir, AssetKindFlags assetTypes, Stats stats)
    {
        var destBase = Path.Combine(outputDir, bundleName, AssetUrlPaths.Sanitize(url));
        var startedAt = Stopwatch.GetTimestamp();

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
                            RunLogger.Warn($"  (no PNG for {url}: {ex.Message})");
                        }
                    }

                    if (!wantDds && pngOk)
                        File.Delete(destBase + ".dds");

                    stats.Textures++;
                    if (wantDds || !pngOk)
                        stats.DdsFiles++;
                    if (pngOk)
                        stats.PngFiles++;
                    stats.TextureMilliseconds += Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
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
                    stats.WavFiles++;
                    stats.SoundMilliseconds += Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    break;
                }

                case DW2AssetKind.Model:
                    if (!assetTypes.HasFlag(AssetKindFlags.Fbx))
                        return;
                    await MeshExporter.ExportToFbx(bundleName, url, destBase + ".fbx", outputDir, VfsRoot);
                    stats.Meshes++;
                    stats.FbxFiles++;
                    stats.MeshMilliseconds += Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    break;

                case DW2AssetKind.RawOrUnknown:
                default:
                    if (!assetTypes.HasFlag(AssetKindFlags.Misc))
                        return;
                    if (!await BundleExtractor.ExtractRaw(odb, fileProvider, url, destBase))
                        throw new IOException("extract failed");
                    stats.Raw++;
                    stats.OtherFiles += CountRawOutputFiles(destBase);
                    stats.RawMilliseconds += Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    break;
            }
        }
        catch (Exception ex)
        {
            stats.Failures++;
            stats.FailureDetails.Add($"{bundleName}:{url} -> {ex.Message}");
            RunLogger.Error($"  FAILED {url}: {ex.Message}");
        }
    }

    private static void CleanupRaw(string rawPath)
    {
        File.Delete(rawPath);
        if (File.Exists(rawPath + "_Data"))
            File.Delete(rawPath + "_Data");
    }

    private static int CountRawOutputFiles(string destBase)
    {
        var count = 0;
        if (File.Exists(destBase))
            count++;
        if (File.Exists(destBase + "_Data"))
            count++;
        return count;
    }

    private class Stats
    {
        public int Textures, Sounds, Meshes, Raw, Failures;
        public int DdsFiles, PngFiles, WavFiles, FbxFiles, OtherFiles;
        public double TextureMilliseconds, SoundMilliseconds, MeshMilliseconds, RawMilliseconds;
        public List<string> FailureDetails { get; } = [];
        public int SuccessfulAssets => Textures + Sounds + Meshes + Raw;

        public void MergeFrom(Stats other)
        {
            Textures += other.Textures;
            Sounds += other.Sounds;
            Meshes += other.Meshes;
            Raw += other.Raw;
            Failures += other.Failures;
            DdsFiles += other.DdsFiles;
            PngFiles += other.PngFiles;
            WavFiles += other.WavFiles;
            FbxFiles += other.FbxFiles;
            OtherFiles += other.OtherFiles;
            TextureMilliseconds += other.TextureMilliseconds;
            SoundMilliseconds += other.SoundMilliseconds;
            MeshMilliseconds += other.MeshMilliseconds;
            RawMilliseconds += other.RawMilliseconds;
            FailureDetails.AddRange(other.FailureDetails);
        }

        public double GetAverageMilliseconds(double totalMilliseconds, int count)
            => count == 0 ? 0 : totalMilliseconds / count;
    }
}
