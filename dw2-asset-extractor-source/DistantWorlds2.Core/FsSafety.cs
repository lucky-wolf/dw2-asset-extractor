namespace DistantWorlds2.Core;

public static class FsSafety
{
    // Guards the check-then-act File.Exists/File.Move sequence below — with conversion work now running
    // on a shared worker pool, multiple threads can call this for colliding/overlapping path segments at
    // once (most notably MeshExporter.ExportToFbx, which has no earlier single-threaded prepare phase to
    // serialize it). Directory creation only happens once per unique output folder, not once per asset,
    // so a single global lock here is effectively free.
    private static readonly object dirLock = new();

    // DW2's asset URL scheme allows a "bare" name and paths nested under that same name to both be
    // valid, real assets — e.g. both "Skybox" and "Skybox/gen/Texture_1" exist in CoreContent. Mapped
    // onto a real filesystem that's a genuine collision (a path can't be both a file and a directory).
    // Ensures dirPath can be created by renaming any ancestor segment that's currently a plain file
    // out of the way first, rather than letting Directory.CreateDirectory throw.
    public static void EnsureDirectory(string dirPath)
    {
        lock (dirLock)
        {
            var full = Path.GetFullPath(dirPath);
            var root = Path.GetPathRoot(full)!;
            var current = root;
            foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.Exists(current))
                    File.Move(current, current + " (asset)", overwrite: true);
            }
            Directory.CreateDirectory(full);
        }
    }
}
