namespace DistantWorlds2.Core;

public static class BundleCatalog
{
    // Bundle descriptor files are named "<name>.bundle"; the actual content-shard files sitting
    // alongside them are named "<name>.<32-hex-char-hash>.bundle" and shouldn't be listed as bundles
    // in their own right.
    public static IEnumerable<string> ListBundleNames(string bundlesDir)
    {
        foreach (var bundlePath in Directory.EnumerateFiles(bundlesDir, "*.bundle"))
        {
            var bundleFileName = Path.GetFileName(bundlePath);
            var bundleName = bundleFileName[..^7]; // strip ".bundle"

            if (bundleFileName.Length > 40)
            {
                var lastDot = bundleFileName.LastIndexOf('.');
                if (lastDot == -1)
                    continue;
                var prevLastDot = bundleFileName.LastIndexOf('.', lastDot - 1);
                if (prevLastDot != -1 && lastDot - prevLastDot == 33)
                    continue;
            }

            yield return bundleName;
        }
    }
}
