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
}

public class FbxScene
{
    public required List<FbxSceneNode> Nodes = new();
    public required List<FbxMeshData> Meshes = new();
}
