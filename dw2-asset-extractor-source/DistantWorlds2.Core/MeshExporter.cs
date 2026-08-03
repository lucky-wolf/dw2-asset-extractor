using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;
using Stride.Core.Storage;
using Stride.Core.IO;
using Stride.Graphics;
using Stride.Graphics.Data;
using Stride.Rendering;
using Stride.Rendering.Materials;
using StrideBuffer = Stride.Graphics.Buffer;

namespace DistantWorlds2.Core;

public static class MeshExporter
{
    // Only Buffer references are followed when loading a Model — we need the raw vertex/index bytes,
    // but not the referenced Textures/Materials (which would require a live GraphicsDevice to deserialize).
    private static readonly ContentManagerLoaderSettings BufferOnlySettings = new()
    {
        ContentFilter = ContentManagerLoaderSettings.NewContentFilterByType(typeof(StrideBuffer))
    };

    // Same idea, one level further down the reference graph: loading a Material this way resolves its
    // parameter data (which textures are bound to which shader keys) without trying to fully deserialize
    // those Textures themselves — which, like above, would need a live GraphicsDevice and just crashes
    // (NullReferenceException) without one. The Texture objects that come back are unresolved proxies;
    // AttachedReferenceManager.GetAttachedReference(...) still gives their bundle URL, which is all we need.
    private static readonly ContentManagerLoaderSettings MaterialOnlySettings = new()
    {
        ContentFilter = ContentManagerLoaderSettings.NewContentFilterByType(typeof(Material))
    };

    // vfsRoot lets callers point this at a bundle set outside the executable's own directory (which is
    // where Stride's own VirtualFileSystem — a separate static registry from Xenko's — resolves relative
    // to by default). Pass a root you've already mounted via Stride.Core.IO.VirtualFileSystem.MountFileSystem;
    // null keeps the default exe-relative behavior dw2bm's "xm" command relies on.
    // outputDir + bundleName let material texture references be resolved into paths relative to destPath
    // — see BuildMaterialData — matching exactly where the main extraction loop puts a bundle's textures.
    public static async Task<int> ExportToFbx(string bundleName, string modelUrl, string destPath, string outputDir, string? vfsRoot = null)
    {
        var odb = vfsRoot == null
            ? ObjectDatabase.CreateDefaultDatabase()
            : new ObjectDatabase($"{vfsRoot}/data/db", "index", $"{vfsRoot}/data/db");
        await odb.LoadBundle(bundleName);
        var fileProvider = new DatabaseFileProvider(odb);
        var fileProviderService = new DatabaseFileProviderService(fileProvider);
        var contentManager = new ContentManager(fileProviderService);

        Model model;
        try
        {
            model = contentManager.Load<Model>(modelUrl, BufferOnlySettings);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to load model '{modelUrl}' from bundle '{bundleName}': {ex.Message}");
            return 1;
        }

        Skeleton? skeleton = null;
        var skeletonUrl = AttachedReferenceManager.GetAttachedReference(model.Skeleton)?.Url;
        if (skeletonUrl != null)
        {
            try
            {
                skeleton = contentManager.Load<Skeleton>(skeletonUrl);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Warning: failed to load skeleton '{skeletonUrl}': {ex.Message}");
            }
        }

        var scene = new FbxScene { Nodes = new List<FbxSceneNode>(), Meshes = new List<FbxMeshData>() };

        if (skeleton?.Nodes != null)
        {
            foreach (var node in skeleton.Nodes)
            {
                scene.Nodes.Add(new FbxSceneNode
                {
                    Name = string.IsNullOrEmpty(node.Name) ? $"Node{scene.Nodes.Count}" : node.Name,
                    ParentIndex = node.ParentIndex,
                    LocalPosition = (node.Transform.Position.X, node.Transform.Position.Y, node.Transform.Position.Z),
                    LocalRotation = (node.Transform.Rotation.X, node.Transform.Rotation.Y, node.Transform.Rotation.Z, node.Transform.Rotation.W),
                    LocalScale = (node.Transform.Scale.X, node.Transform.Scale.Y, node.Transform.Scale.Z),
                });
            }
        }
        else
        {
            scene.Nodes.Add(new FbxSceneNode
            {
                Name = "RootNode",
                ParentIndex = -1,
                LocalPosition = (0, 0, 0),
                LocalRotation = (0, 0, 0, 1),
                LocalScale = (1, 1, 1),
            });
        }

        if (model.Materials != null)
        {
            foreach (var materialInstance in model.Materials)
                scene.Materials.Add(BuildMaterialData(contentManager, odb, materialInstance, destPath, outputDir, bundleName));
        }

        if (model.Meshes != null)
        {
            var nameCounts = new Dictionary<string, int>();
            foreach (var mesh in model.Meshes)
            {
                var meshData = ExtractMesh(mesh, scene.Nodes.Count);
                if (meshData == null)
                    continue;

                meshData.MaterialIndex = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.Materials.Count
                    ? mesh.MaterialIndex
                    : -1;

                var baseName = string.IsNullOrEmpty(mesh.Name) ? "Mesh" : mesh.Name;
                var count = nameCounts.TryGetValue(baseName, out var c) ? c : 0;
                nameCounts[baseName] = count + 1;
                meshData.Name = count == 0 ? baseName : $"{baseName}_{count}";

                scene.Meshes.Add(meshData);
            }
        }

        if (Path.GetDirectoryName(destPath) is { Length: > 0 } destDir)
            FsSafety.EnsureDirectory(destDir);
        FbxBinaryWriter.Write(scene, destPath);

        Console.WriteLine($"{bundleName}:{modelUrl} -> {destPath}");
        Console.WriteLine($"  {scene.Nodes.Count} nodes, {scene.Meshes.Count} meshes" + (skeleton == null ? " (no skeleton)" : ""));
        return 0;
    }

    private static FbxMaterialData BuildMaterialData(ContentManager contentManager, ObjectDatabase odb, MaterialInstance materialInstance, string destPath, string outputDir, string bundleName)
    {
        var materialUrl = AttachedReferenceManager.GetAttachedReference(materialInstance.Material)?.Url;
        // "NoMaterial" rather than something generic like "Material" — Blender treats "Material" as its
        // own default name and silently renames ours to "Material.001" on collision, which is confusing
        // in the outliner even though it's functionally harmless.
        var data = new FbxMaterialData { Name = materialUrl != null ? materialUrl.Split('/')[^1] : "NoMaterial" };
        if (materialUrl == null)
            return data; // this material slot exists but has nothing assigned — leave it textureless, not an error

        Material material;
        try
        {
            material = contentManager.Load<Material>(materialUrl, MaterialOnlySettings);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: failed to load material '{materialUrl}': {ex.Message}");
            return data;
        }

        var parameters = material.Passes?.FirstOrDefault()?.Parameters;
        if (parameters == null)
            return data;

        var destDir = Path.GetDirectoryName(destPath)!;

        string? ResolveTexturePath(ObjectParameterKey<Texture> key)
        {
            if (!parameters.ContainsKey(key))
                return null;
            var textureUrl = AttachedReferenceManager.GetAttachedReference(parameters.Get(key, false))?.Url;
            // Only link to textures this bundle actually declares an entry for — excludes Stride's own
            // built-in resources (e.g. the PBR environment lighting LUT, which isn't game content and
            // was never going to be extracted) and cross-bundle references this pass can't resolve a
            // predictable output path for.
            if (textureUrl == null || !odb.ContentIndexMap.TryGetValue(textureUrl, out _))
                return null;
            var textureDestPath = Path.Combine(outputDir, bundleName, AssetUrlPaths.Sanitize(textureUrl) + ".png");
            return Path.GetRelativePath(destDir, textureDestPath);
        }

        data.DiffuseTexturePath = ResolveTexturePath(MaterialKeys.DiffuseMap);
        data.NormalTexturePath = ResolveTexturePath(MaterialKeys.NormalMap);
        data.EmissiveTexturePath = ResolveTexturePath(MaterialKeys.EmissiveMap);

        // Deliberately NOT wired into any FBX material slot — see FbxMaterialData's doc comment.
        foreach (var (label, key) in new (string, ObjectParameterKey<Texture>)[]
                 {
                     ("GlossinessMap", MaterialKeys.GlossinessMap),
                     ("MetalnessMap", MaterialKeys.MetalnessMap),
                     ("AmbientOcclusionMap", MaterialKeys.AmbientOcclusionMap),
                 })
        {
            if (ResolveTexturePath(key) is { } path)
                data.ExtraTextureProperties[label] = path;
        }

        return data;
    }

    private static FbxMeshData? ExtractMesh(Mesh mesh, int nodeCount)
    {
        var draw = mesh.Draw;
        if (draw?.VertexBuffers == null || draw.VertexBuffers.Length == 0 || draw.IndexBuffer == null)
            return null;

        var nodeIndex = mesh.NodeIndex;
        if (nodeIndex < 0 || nodeIndex >= nodeCount)
            nodeIndex = 0;

        var vb = draw.VertexBuffers[0];
        var vbContent = vb.Buffer?.GetSerializationData()?.Content;
        var ibContent = draw.IndexBuffer.Buffer?.GetSerializationData()?.Content;
        if (vbContent == null || ibContent == null || vb.Declaration == null)
            return null;

        var elements = vb.Declaration.VertexElements;
        var posEl = FindElement(elements, "POSITION");
        var normEl = FindElement(elements, "NORMAL");
        var uvEl = FindElement(elements, "TEXCOORD");
        if (posEl == null)
            return null;

        var stride = vb.Declaration.VertexStride;
        var vertexCount = vb.Count;
        var positions = new (float X, float Y, float Z)[vertexCount];
        var normals = new (float X, float Y, float Z)[vertexCount];
        (float U, float V)[]? uvs = uvEl != null ? new (float, float)[vertexCount] : null;

        for (int i = 0; i < vertexCount; i++)
        {
            var baseOffset = vb.Offset + i * stride;
            positions[i] = ReadFloat3(vbContent, baseOffset + posEl.Value.AlignedByteOffset);
            normals[i] = normEl != null ? ReadFloat3(vbContent, baseOffset + normEl.Value.AlignedByteOffset) : (0, 0, 1);
            if (uvEl != null && uvs != null)
                uvs[i] = ReadFloat2(vbContent, baseOffset + uvEl.Value.AlignedByteOffset);
        }

        var indexCount = draw.IndexBuffer.Count;
        var indices = new int[indexCount];
        var is32Bit = draw.IndexBuffer.Is32Bit;
        var ibBaseOffset = draw.IndexBuffer.Offset;
        for (int i = 0; i < indexCount; i++)
        {
            indices[i] = is32Bit
                ? BitConverter.ToInt32(ibContent, ibBaseOffset + i * 4)
                : BitConverter.ToUInt16(ibContent, ibBaseOffset + i * 2);
        }

        return new FbxMeshData
        {
            Name = mesh.Name ?? "Mesh",
            NodeIndex = nodeIndex,
            Positions = positions,
            Normals = normals,
            UVs = uvs,
            TriangleIndices = indices,
        };
    }

    private static VertexElement? FindElement(VertexElement[] elements, string semanticName)
    {
        foreach (var el in elements)
            if (el.SemanticName == semanticName && el.AlignedByteOffset >= 0)
                return el;
        return null;
    }

    private static (float, float, float) ReadFloat3(byte[] data, int offset)
        => (BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));

    private static (float, float) ReadFloat2(byte[] data, int offset)
        => (BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4));
}
