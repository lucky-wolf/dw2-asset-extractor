namespace DistantWorlds2.Core;

// Engine-agnostic intermediate representation, so FbxAsciiWriter has no dependency on Stride types.

public class FbxSceneNode
{
    public required string Name;
    public int ParentIndex; // -1 for a root node
    public (float X, float Y, float Z) LocalPosition;
    public (float X, float Y, float Z, float W) LocalRotation; // quaternion
    public (float X, float Y, float Z) LocalScale;
}

public class FbxMeshData
{
    public required string Name;
    public required int NodeIndex; // index into FbxScene.Nodes this mesh is parented to
    public required (float X, float Y, float Z)[] Positions;
    public required (float X, float Y, float Z)[] Normals;
    public (float U, float V)[]? UVs;
    public required int[] TriangleIndices; // flat, 3 per triangle, indexing into Positions/Normals/UVs
    public int MaterialIndex = -1; // index into FbxScene.Materials, or -1 for no material
}

// One entry per material slot the source Model declared (FbxScene.Materials[i] always corresponds to
// the source Model's Materials[i], same indexing FbxMeshData.MaterialIndex points into — kept 1:1 rather
// than deduplicated, since that's what the source data already gives us for free).
//
// Only DiffuseMap/NormalMap/EmissiveMap get wired up as real FBX texture connections (the "make it look
// textured in Blender" win) — see MeshExporter for why Glossiness/Metalness/AmbientOcclusion deliberately
// don't: they're usually packed into channels of one shared texture in Stride's PBR material model, and
// classic FBX materials have no slot that means "the red channel of this texture." Forcing them into a
// wrong-meaning slot (e.g. "Specular") would actively mislead anyone editing the result. Instead they're
// surfaced as plain custom properties on the FBX Material node — visible in Blender's material custom
// properties, informative for a technical artist, harmless to anything that ignores them, and crucially
// they still point at the exact same packed texture file the game itself uses, so round-tripping edits
// back into Stride (e.g. via Marmoset Toolbag's channel-packing workflow) isn't compromised by this tool
// having invented a new unpacked format along the way.
public class FbxMaterialData
{
    public required string Name;
    public string? DiffuseTexturePath; // relative to the .fbx file's own directory
    public string? NormalTexturePath;
    public string? EmissiveTexturePath;
    public Dictionary<string, string> ExtraTextureProperties = new(); // e.g. "GlossinessMap" -> relative path
}

public class FbxScene
{
    public required List<FbxSceneNode> Nodes = new();
    public required List<FbxMeshData> Meshes = new();
    public List<FbxMaterialData> Materials = new();
}
