using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SharpDX.D3DCompiler;
using DistantWorlds2.Core;
using Xenko.Core.IO;
using Xenko.Core.Reflection;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Core.Storage;
using Xenko.Graphics;
using Buffer = System.Buffer;
using Xenko.Core.Streaming;
using Xenko.Core;

namespace DistantWorlds2.BundleManager;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Some Stride assemblies (e.g. ServiceWire, pulled in by Stride.Core's static init) aren't part of
        // this project's compiled dependency closure, so .NET's default deps.json-driven resolution won't
        // find them even though they sit right next to the exe. Fall back to loading by simple name from
        // our own directory, matching how a normal DW2 install resolves its own plugin-style dependencies.
        AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, new AssemblyName(resolveArgs.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        if (args.Length == 4 && args[0] == "xm")
            return await MeshExporter.ExportToFbx(args[1], args[2], args[3]);

        var odb = ObjectDatabase.CreateDefaultDatabase();

        switch (args.Length)
        {
            default: {
                Console.WriteLine("Usage:");
                Console.WriteLine("dw2bm lb");
                Console.WriteLine("    Lists the bundles available.");
                Console.WriteLine();
                Console.WriteLine("dw2bm ls [bundle name] [glob pattern]");
                Console.WriteLine("    Lists the files within a bundle.");
                Console.WriteLine();
                Console.WriteLine("dw2bm ex [bundle name] <source file> <destination file>");
                Console.WriteLine("    Extracts a file from within a bundle.");
                Console.WriteLine("dw2bm ex [bundle name] [glob pattern] <destination folder>");
                Console.WriteLine("    Extracts the files matching the pattern from within a bundle.");
                Console.WriteLine();
                Console.WriteLine("dw2bm id <source file>");
                Console.WriteLine("    Gets the type identifier for a loose file.");
                Console.WriteLine();
                Console.WriteLine("dw2bm id [bundle name] <source file>");
                Console.WriteLine("    Gets the type identifier for a bundled file.");
                Console.WriteLine();
                Console.WriteLine("dw2bm xt <source file> <destination file>");
                Console.WriteLine("    Converts a texture from Xenko format to DDS.");
                Console.WriteLine();
                Console.WriteLine("dw2bm tx <source file> <destination file>");
                Console.WriteLine("    Converts a texture from DDS to Xenko format.");
                Console.WriteLine();
                Console.WriteLine("dw2bm txs <source file> <destination root folder> <relative asset path>");
                Console.WriteLine("    Converts a texture from DDS to Xenko's streaming texture format.");
                Console.WriteLine();
                Console.WriteLine("dw2bm xs <source file> <destination file>");
                Console.WriteLine("dw2bm xs <glob pattern> <destination folder> <destination extension>");
                Console.WriteLine("    Converts sound files from Xenko format to an output format.");
                Console.WriteLine("    [REQUIRES FFmpeg binaries in PATH]");
                Console.WriteLine();
                Console.WriteLine("dw2bm sx <source file> <destination root folder> <relative asset path>");
                Console.WriteLine("dw2bm sx <glob pattern> <destination root folder> <relative asset root>");
                Console.WriteLine("    Converts sound files to Xenko format.");
                Console.WriteLine("    [REQUIRES FFmpeg binaries in PATH]");
                Console.WriteLine();
                Console.WriteLine("dw2bm xm <bundle name> <model asset path> <destination .fbx file>");
                Console.WriteLine("    Converts a mesh (and its skeleton, if any) from a bundle to FBX.");
                Console.WriteLine();
                Console.WriteLine("dw2bm xu <source file> <destination file>");
                Console.WriteLine("    Converts an unpacked Xenko asset to a packable asset.");
                Console.WriteLine("    Yeah it doesn't make sense to me either.");
                Console.WriteLine();
                Console.WriteLine("dw2bm mb <bundle name> <source root dir> <glob pattern>");
                Console.WriteLine("    Creates a bundle from a selection of files.");
                Console.WriteLine("    INCOMPLETE - BUNDLES ARE INCOMPATIBLE");
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("dw2bm lb");
                Console.WriteLine("dw2bm ls Abandoned **");
                Console.WriteLine("dw2bm ex Abandoned \"Ships/Abandoned/Images/Abandoned_Colony\" Abandoned_Colony");
                Console.WriteLine("dw2bm ex CoreContent Sounds/** \"./mods/MyMod/extracted/\"");
                Console.WriteLine("dw2bm id Abandoned \"Ships/Abandoned/Images/Abandoned_Colony\"");
                Console.WriteLine("dw2bm id Abandoned_Colony");
                Console.WriteLine("dw2bm xt Abandoned_Colony Abandoned_Colony.dds");
                Console.WriteLine("dw2bm tx Abandoned_Colony.dds Abandoned_Colony");
                Console.WriteLine("dw2bm txs Abandoned_Colony.dds \"./mods/MyMod/assets\" \"Ships/Abandoned/Images/Abandoned_Colony\"");
                Console.WriteLine("dw2bm xs HeavyRailGun HeavyRailGun.wav");
                Console.WriteLine("dw2bm xs \"./mods/MyMod/extracted/Sounds/**\" \"./mods/MyMod/assets/Sounds/\" wav");                    
                Console.WriteLine("dw2bm sx HeavyRailGun.wav HeavyRailGun");
                Console.WriteLine("dw2bm sx \"./mods/MyMod/source/Sounds/**\" \"./mods/MyMod/assets/\" \"Sounds/\"");
                Console.WriteLine("dw2bm xm CoreContent \"Creatures/knight\" knight.fbx");
                Console.WriteLine("dw2bm xu Abandoned_Colony AbandonedColonyForBundle");
                Console.WriteLine("dw2bm mb MyBundle path/to/files/**");
                Console.WriteLine();

                if (args.Length > 0)
                    return 1;

                return 0;
            }
            case 1 when args[0] == "lb": {
                // Bundle resolution elsewhere (ls/ex/id, via Xenko's VirtualFileSystem) is always relative to
                // the executable's own directory, not the current working directory. Match that here so `lb`
                // agrees with every other command about where "data/db/bundles" lives.
                var bundlesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "db", "bundles");
                foreach (var bundleName in BundleCatalog.ListBundleNames(bundlesDir))
                    Console.WriteLine(bundleName);

                return 0;
            }
            case 2 when args[0] == "ls": {
                var bundleName = args[1];
                await odb.LoadBundle(bundleName);
                foreach (var kv in odb.ContentIndexMap.GetMergedIdMap())
                    Console.WriteLine(kv.Key);
                return 0;
            }
            case 3 when args[0] == "ls": {
                var bundleName = args[1];
                await odb.LoadBundle(bundleName);
                var globExpr = args[2];
                var matcher = new Matcher(StringComparison.Ordinal);
                matcher.AddInclude(globExpr);
                var map = odb.ContentIndexMap;
                var r = matcher.Match(map.GetMergedIdMap().Select(kv => kv.Key));

                if (!r.HasMatches)
                    return 1;

                foreach (var file in r.Files)
                    Console.WriteLine(file.Path);

                return 0;
            }
            case 4 when args[0] == "ex": {
                var bundleName = args[1];
                await odb.LoadBundle(bundleName);
                var fileProvider = new DatabaseFileProvider(odb);
                async Task Extract(string src, string dst)
                {
                    Console.WriteLine($"{bundleName}:{src} -> {dst}");
                    if (!await BundleExtractor.ExtractRaw(odb, fileProvider, src, dst))
                    {
                        await Console.Error.WriteLineAsync("Failed.");
                        await Console.Error.FlushAsync();
                    }
                }

                if (!args[2].Contains('*')) //Single file
                {
                    var src = args[2];
                    var dst = args[3];
                    await Extract(src, dst);
                    
                    Console.WriteLine("Done.");
                    return 0;
                }
                else //Glob
                {
                    if (!args[3].EndsWith('/') && !args[3].EndsWith('\\'))
                            args[3] += "/";
                    var globExpr = args[2];
                    var matcher = new Matcher(StringComparison.Ordinal);
                    matcher.AddInclude(globExpr);
                    var map = odb.ContentIndexMap;
                    var r = matcher.Match(map.GetMergedIdMap().Select(kv => kv.Key));

                    if (!r.HasMatches)
                        return 1;

                    foreach (var file in r.Files)
                    {
                        var src = file.Path;
                        var dst = Path.Combine(Path.GetDirectoryName(args[3]), file.Path);
                        await Extract(src, dst);
                    }
                    Console.WriteLine("Done.");
                    return 0;
                }
            }
            case 2 when args[0] == "id": {
                var src = args[1];
                Console.WriteLine($"Identifying {src}");
                using var srcFs = File.OpenRead(src);
                var bsr = new BinarySerializationReader(srcFs);
                var chunkHeader = ChunkHeader.Read(bsr);
                if (chunkHeader == null)
                {
                    Console.WriteLine("Not chunk-formatted (raw content, e.g. shader source or plain text).");
                    return 0;
                }
                Console.WriteLine($"Version: {chunkHeader.Version}");
                Console.WriteLine(chunkHeader.Type);
                return 0;
            }
            case 3 when args[0] == "id": {
                var bundleName = args[1];
                await odb.LoadBundle(bundleName);
                var src = args[2];
                Console.WriteLine($"Identifying {bundleName}:{src}");
                if (odb.ContentIndexMap.TryGetValue(src, out var id))
                {
                    var fileProvider = new DatabaseFileProvider(odb);
                    using var s = fileProvider.OpenStream(src, VirtualFileMode.Open, VirtualFileAccess.Read, VirtualFileShare.Read, StreamFlags.Seekable);
                    s.Seek(0, SeekOrigin.Begin);
                    var bsr = new BinarySerializationReader(s);
                    var chunkHeader = ChunkHeader.Read(bsr);
                    if (chunkHeader == null)
                    {
                        Console.WriteLine("Not chunk-formatted (raw content, e.g. shader source or plain text).");
                        return 0;
                    }
                    Console.WriteLine($"Version: {chunkHeader.Version}");
                    Console.WriteLine(chunkHeader.Type);
                }
                else
                {
                    await Console.Error.WriteLineAsync("Failed.");
                    await Console.Error.FlushAsync();
                    return 1;
                }

                return 0;
            }
            case 3 when args[0] == "xt": {
                    var src = args[1];
                    var dst = args[2];
                    var ext = Path.GetExtension(dst);
                    var fmt = ext switch
                    {
                        ".dds" => ImageFileType.Dds,
                        _ => throw new NotImplementedException()
                    };
                    TextureConverter.XenkoToDds(src, dst);
                    return 0;
            }            
            case 3 when args[0] == "tx": {
                var src = args[1];
                var dst = args[2];

                using var srcFs = File.OpenRead(src);

                using var img = Image.Load(srcFs);

                using var dstFs = File.OpenWrite(dst);

                var bsw = new BinarySerializationWriter(dstFs);
                var header = new ChunkHeader { Type = typeof(Texture).AssemblyQualifiedName };
                header.Write(bsw);
                header.OffsetToReferences = (int)bsw.NativeStream.Position;

                var refsPath = src + ".refs";
                if (!File.Exists(refsPath))
                    bsw.Write(0);
                else
                {
                    var refBytes = File.ReadAllBytes(refsPath);
                    await bsw.NativeStream.WriteAsync(refBytes, 0, refBytes.Length);
                }
                header.OffsetToObject = (int)bsw.NativeStream.Position;
                dstFs.WriteByte(0); // bool false
                img.Save(dstFs, ImageFileType.Xenko);
                bsw.Write(0); // padding?
                bsw.NativeStream.Position = 0;
                header.Write(bsw);

                return 0;
            }
            case 4 when args[0] == "txs": {
                var src = args[1];
                var dstRoot = args[2];
                var assetPath = args[3];

                using var srcFs = File.OpenRead(src);
                using var img = Image.Load(srcFs);

                StreamingImage.Write(dstRoot, assetPath, img);

                return 0;
            }
            case 3 when args[0] == "xs":
            {

                var src = args[1];
                var dst = args[2];
                SoundConverter.XenkoToSoundfile(src, dst);
                return 0;

            }
            case 4 when args[0] == "xs":
            {
                if (!args[3].StartsWith("."))
                    args[3] = "." + args[3];
                var globExpr = args[1];
                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddInclude(globExpr);
                var r = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(".")));

                if (!r.HasMatches)
                    return 1;

                foreach (var file in r.Files)
                {
                    var src = file.Path;
                    var dst = Path.Combine(Path.GetDirectoryName(args[2]), file.Stem) + $"{args[3]}";
                    Console.WriteLine($"{src} -> {dst}");
                    SoundConverter.XenkoToSoundfile(src, dst);
                }
                Console.WriteLine("Done.");
                return 0;
            }

            case 4 when args[0] == "sx":
            {                
                if (!args[1].Contains('*')) //Single file
                {
                    var src = args[1];
                    var dstRoot = args[2];
                    var assetPath = args[3];
                    SoundConverter.SoundToXenko(src, dstRoot, assetPath);
                    return 0;
                }
                else //Glob
                {
                    var globExpr = args[1];
                    var matcher = new Matcher(StringComparison.Ordinal);
                    matcher.AddInclude(globExpr);
                    var r = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(".")));

                    if (!r.HasMatches)
                        return 1;

                    foreach (var file in r.Files)
                    {
                        var dstRoot = args[2];
                        var src = file.Path;
                        var assetPath = Path.Combine(Path.GetDirectoryName(args[3]), Path.ChangeExtension(file.Stem, null)).Replace('\\','/');
                        Console.WriteLine($"{src} -> {Path.Combine(dstRoot, assetPath)}");
                        SoundConverter.SoundToXenko(src, dstRoot, assetPath);
                    }
                    Console.WriteLine("Done.");
                    return 0;
                }
            }
            case 3 when args[0] == "xu": {
                var src = args[1];
                var dst = args[2];
                
                Console.WriteLine($"{src} -> {dst}");

                using var srcFs = File.OpenRead(src);
                using var dstFs = File.OpenWrite(dst);
                
                var bsr = new BinarySerializationReader(srcFs);
                var bsw = new BinarySerializationWriter(dstFs);
                
                var chunkHeader = ChunkHeader.Read(bsr);
                if (chunkHeader == null) throw new NotSupportedException("Invalid chunk header.");
                var objOffset = chunkHeader.OffsetToObject;
                srcFs.Seek(objOffset, SeekOrigin.Begin);


                var refsOffset = chunkHeader.OffsetToReferences;
                if (refsOffset < objOffset)
                {
                    var contentLength = (int)(srcFs.Length - objOffset);
                    var refsLength = objOffset - refsOffset;
                    chunkHeader.OffsetToObject = refsOffset;
                    chunkHeader.OffsetToReferences = objOffset + contentLength;
                    chunkHeader.Write(bsw);
                    srcFs.Seek(objOffset, SeekOrigin.Begin);
                    var buf = new byte[contentLength];
                    await srcFs.ReadAsync(buf, 0, contentLength);
                    await dstFs.WriteAsync(buf, 0, contentLength);
                    if (buf.Length < refsLength)
                        buf = new byte[refsLength];
                    srcFs.Seek(refsOffset, SeekOrigin.Begin);
                    await srcFs.ReadAsync(buf, 0, refsLength);
                    await dstFs.WriteAsync(buf, 0, refsLength);
                }
                else
                    await srcFs.CopyToAsync(dstFs);

                Console.WriteLine("Done.");
                return 0;
            }
            case 4 when args[0] == "mb": {
                var name = args[1];
                var srcUri = new Uri(args[2], UriKind.RelativeOrAbsolute);
                var srcRoot = (srcUri.IsAbsoluteUri
                    ? srcUri.MakeRelativeUri(new(Environment.CurrentDirectory))
                    : srcUri).ToString();
                var glob = args[3];
                Console.WriteLine($"Making {name}.bundle out of {glob} in {srcRoot}");

                var vfsRoot = "/create-bundle/" + name;
                var newBundleVfsRoot = "/created-bundle/";

                VirtualFileSystem.MountFileSystem(vfsRoot, srcRoot);

                IOdbBackend fbe = new FileOdbBackend(vfsRoot, null, true);

                var ci = (ContentIndexMap)fbe.ContentIndexMap;

                ci.AutoLoadNewValues = true;
                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddInclude(glob);
                var results = matcher.Execute(new DirectoryInfoWrapper(new(srcRoot)));
                var map = results.Files.Select(f => new KeyValuePair<string, ObjectId>(f.Path, ObjectId.New()));
                ci.AddValues(map);
                ci.Save();

                var ciKvs = ci.GetValues();
                //var filePaths = ciKvs.Select(kv => kv.Key).ToArray();
                var objectIds = ciKvs.Select(kv => kv.Value).ToArray();

                Directory.CreateDirectory("tmp/created-bundle");
                var bundlePath = $"tmp/created-bundle/{name}.bundle";
                if (File.Exists(bundlePath))
                    File.Delete(bundlePath);
                VirtualFileSystem.MountFileSystem(newBundleVfsRoot, "tmp/created-bundle");

                var fbe2 = new SimpleFileOdbBackend(srcRoot, ciKvs);
                
                BundleOdbBackend.CreateBundle(
                    $"/created-bundle/{name}.bundle",
                    fbe2,
                    objectIds,
                    new HashSet<ObjectId>(),
                    new(new LinearSearchReadOnlyDictionary<string, ObjectId>(ciKvs)),
                    Array.Empty<string>(),
                    false
                );

                return 0;
            }
        }
    }

}