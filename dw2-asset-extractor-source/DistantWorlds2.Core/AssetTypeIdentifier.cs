using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;

namespace DistantWorlds2.Core;

public enum DW2AssetKind
{
    RawOrUnknown, // not chunk-formatted (e.g. raw .sdsl source), or a chunk-formatted type we don't specially handle
    Texture,
    Sound,
    Model,
    Skeleton,
}

public static class AssetTypeIdentifier
{
    // Reads (and rewinds) just enough of the stream to classify it. Returns null if the content
    // isn't chunk-formatted at all (raw text/binary content, e.g. shader source).
    public static string? TryGetChunkType(Stream stream)
    {
        var startPos = stream.Position;
        try
        {
            var bsr = new BinarySerializationReader(stream);
            var chunkHeader = ChunkHeader.Read(bsr);
            return chunkHeader?.Type;
        }
        finally
        {
            stream.Position = startPos;
        }
    }

    public static DW2AssetKind Classify(string? chunkType)
    {
        if (chunkType == null)
            return DW2AssetKind.RawOrUnknown;
        if (chunkType.Contains(".Graphics.Texture"))
            return DW2AssetKind.Texture;
        if (chunkType.Contains(".Audio.Sound"))
            return DW2AssetKind.Sound;
        if (chunkType.Contains(".Rendering.Model"))
            return DW2AssetKind.Model;
        if (chunkType.Contains(".Rendering.Skeleton"))
            return DW2AssetKind.Skeleton;
        return DW2AssetKind.RawOrUnknown;
    }
}
