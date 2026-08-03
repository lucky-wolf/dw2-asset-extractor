using Xenko.Core.IO;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Core.Storage;

namespace DistantWorlds2.Core;

public static class BundleExtractor
{
    // Raw-copies one bundle entry (and its "_Data" companion, if it has one) to disk, no conversion.
    // Returns false if the entry isn't present in the bundle.
    public static async Task<bool> ExtractRaw(ObjectDatabase odb, DatabaseFileProvider fileProvider, string url, string destPath)
    {
        if (!odb.ContentIndexMap.TryGetValue(url, out _))
            return false;

        if (Path.GetDirectoryName(destPath) is { Length: > 0 } destDir)
            FsSafety.EnsureDirectory(destDir);
        // The reverse collision: some other asset already needed destPath itself to be a directory
        // (e.g. "Skybox/gen/Texture_1" exists alongside a bare "Skybox" asset).
        if (Directory.Exists(destPath))
            destPath += " (asset)";
        using (var s = fileProvider.OpenStream(url, VirtualFileMode.Open, VirtualFileAccess.Read, VirtualFileShare.Read, StreamFlags.Seekable))
        using (var fs = File.Create(destPath))
        {
            fs.SetLength(s.Length);
            await s.CopyToAsync(fs);
        }

        if (url.EndsWith("_Data"))
            return true;

        // Only chunk-formatted assets can have a "_Data" companion, and only if one is actually indexed.
        using (var s = fileProvider.OpenStream(url, VirtualFileMode.Open, VirtualFileAccess.Read, VirtualFileShare.Read, StreamFlags.Seekable))
        {
            var chunkType = AssetTypeIdentifier.TryGetChunkType(s);
            var dataUrl = url + "_Data";
            if (chunkType != null && odb.ContentIndexMap.TryGetValue(dataUrl, out _))
            {
                using var contentSrc = fileProvider.OpenStream(dataUrl, VirtualFileMode.Open, VirtualFileAccess.Read, VirtualFileShare.Read);
                using var contentDst = File.Create(destPath + "_Data");
                await contentSrc.CopyToAsync(contentDst);
            }
        }

        return true;
    }
}
