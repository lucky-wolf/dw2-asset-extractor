using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Graphics;
using FFMpegCore;
using FFMpegCore.Helpers;

namespace DistantWorlds2.Core;

public static class TextureConverter
{
    private static int ffmpegTimeoutMs = 5 * 60 * 1000;

    public static void SetFfmpegTimeoutSeconds(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
            return;
        ffmpegTimeoutMs = timeoutSeconds * 1000;
    }

    public static void XenkoToDds(string src, string dst)
    {
        using var srcFs = File.OpenRead(src);
        var bsr = new BinarySerializationReader(srcFs);
        var chunkHeader = ChunkHeader.Read(bsr);
        if (chunkHeader == null) throw new NotSupportedException("Invalid chunk header.");
        srcFs.Seek(chunkHeader.OffsetToObject, SeekOrigin.Begin);
        using var img = bsr.ReadBoolean() ? StreamingImage.Load(bsr, src) : Image.Load(srcFs)!;
        using var dstFs = File.OpenWrite(dst);
        img.Save(dstFs, ImageFileType.Dds);
        srcFs.Seek(chunkHeader.OffsetToReferences, SeekOrigin.Begin);
        var refsSize = chunkHeader.OffsetToObject > chunkHeader.OffsetToReferences
            ? chunkHeader.OffsetToObject - chunkHeader.OffsetToReferences
            : srcFs.Length - chunkHeader.OffsetToReferences;
        var refs = new byte[refsSize];
        srcFs.Read(refs, 0, (int)refsSize);

        var refsPath = dst + ".refs";
        File.WriteAllBytes(refsPath, refs);
    }

    private static string ffmpegPath = "";

    private static bool FindFFmpeg()
    {
        // Check the executable's own directory first — reliable no matter how the tool was launched
        // (double-click, a shortcut with a different "Start in" folder, a script run from elsewhere).
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")))
        {
            ffmpegPath = AppContext.BaseDirectory;
            return true;
        }
        if (File.Exists("ffmpeg.exe"))
        {
            ffmpegPath = ".";
            return true;
        }
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var pathVar = Environment.GetEnvironmentVariable("Path", target);
            foreach (var path in (pathVar ?? "").Split(';'))
            {
                if (File.Exists(Path.Combine(path, "ffmpeg.exe")))
                {
                    ffmpegPath = path;
                    return true;
                }
            }
        }
        return false;
    }

    // Png is a viewing/reference convenience, not a round-trippable asset format, so this reads back
    // the already-converted .dds rather than re-parsing the original Xenko chunk file.
    //
    // This shells out to FFmpeg rather than using Xenko's own Image.Save(..., Png): that path decodes
    // at least some block-compressed textures incorrectly (produces a valid-looking but visually garbled
    // PNG — a real, silent correctness bug, not just a missing-format case) and separately crashes the
    // whole process outright (an unmanaged access violation, uncatchable) on floating-point/HDR pixel
    // formats. FFmpeg's DDS decoder handles both cases correctly and is already a dependency here for
    // sound conversion.
    public static void DdsToPng(string ddsPath, string pngPath)
    {
        if (ffmpegPath == "" && !FindFFmpeg())
            throw new NotSupportedException("Failed to find FFmpeg.");

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo.FileName = Path.Combine(ffmpegPath, "ffmpeg.exe");
        proc.StartInfo.ArgumentList.Add("-y");
        proc.StartInfo.ArgumentList.Add("-nostdin");
        proc.StartInfo.ArgumentList.Add("-loglevel");
        proc.StartInfo.ArgumentList.Add("error");
        // Force full-range RGB interpretation for DDS
        proc.StartInfo.ArgumentList.Add("-color_range");
        proc.StartInfo.ArgumentList.Add("pc");
        proc.StartInfo.ArgumentList.Add("-i");
        proc.StartInfo.ArgumentList.Add(ddsPath);
        // Swap R and B channels via colorchannelmixer (ffmpeg's DDS decoder comes out BGR-ordered).
        // This maps: R(out)=B(in), G(out)=G(in), B(out)=R(in); alpha (aa) is left at its default of 1,
        // i.e. passed through unchanged. Output format is rgba, not rgb24 — rgb24 has no alpha plane at
        // all, which was flattening every transparent pixel to opaque black.
        proc.StartInfo.ArgumentList.Add("-vf");
        proc.StartInfo.ArgumentList.Add("colorchannelmixer=rr=0:rb=1:br=1:bb=0,format=rgba");
        // Ensure output is full range
        proc.StartInfo.ArgumentList.Add("-color_range");
        proc.StartInfo.ArgumentList.Add("pc");
        proc.StartInfo.ArgumentList.Add("-frames:v");
        proc.StartInfo.ArgumentList.Add("1");
        proc.StartInfo.ArgumentList.Add(pngPath);
        proc.StartInfo.UseShellExecute = false;
        // Deliberately NOT redirecting stdout/stderr: redirecting without a reader draining the pipe
        // deadlocks as soon as ffmpeg's output fills the OS pipe buffer (it blocks writing, we block in
        // WaitForExit) — that's exactly what hung the extractor on its first run with this enabled.
        // -loglevel error keeps ffmpeg quiet enough that this isn't needed for diagnostics anyway; exit
        // code + output file checks below are how we detect failure.
        proc.StartInfo.CreateNoWindow = true;
        proc.Start();

        if (!proc.WaitForExit(ffmpegTimeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"ffmpeg timed out after {ffmpegTimeoutMs / 1000}s while converting DDS to PNG");
        }

        if (proc.ExitCode != 0 || !File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
        {
            if (File.Exists(pngPath))
                File.Delete(pngPath);
            throw new InvalidOperationException($"ffmpeg failed to convert to PNG (exit code {proc.ExitCode})");
        }
    }
}
