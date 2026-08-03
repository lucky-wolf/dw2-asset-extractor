namespace DistantWorlds2.Core;

public static class AssetUrlPaths
{
    // Turns a bundle asset URL ("Ships/Human/human_battleship") into a filesystem-safe relative path
    // ("Ships\Human\human_battleship" on Windows). Shared by the main per-asset extraction loop and by
    // MeshExporter (which needs to predict where a material's referenced textures will land, to link
    // them into the exported FBX) so both stay in exact agreement about where a given asset ends up.
    public static string Sanitize(string url)
    {
        var parts = url.Split('/');
        var invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < parts.Length; i++)
            foreach (var c in invalid)
                parts[i] = parts[i].Replace(c, '_');
        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
