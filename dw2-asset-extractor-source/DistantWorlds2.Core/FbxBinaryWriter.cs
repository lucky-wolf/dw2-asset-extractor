using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DistantWorlds2.Core;

// Minimal binary FBX 7.4 writer. No FBX SDK / native dependency. Blender's importer only
// accepts binary FBX (its ASCII support was dropped years ago), so this is the format that
// actually matters for verification, despite being more work than ASCII.
//
// Binary FBX node record layout (FBX < 7500, 32-bit offsets):
//   uint32 EndOffset        - absolute file offset just past this record (including its NULL terminator, if any)
//   uint32 NumProperties
//   uint32 PropertyListLen  - byte length of the encoded property list that follows the name
//   byte   NameLen
//   byte[NameLen] Name
//   <properties>
//   <nested child records, each a full node record, present only if there are children>
//   <13 zero bytes NULL-record terminator, present only if there are children>
public static class FbxBinaryWriter
{
    private sealed class BNode
    {
        public required string Name;
        public List<object> Properties { get; } = new();
        public List<BNode> Children { get; } = new();

        public BNode Add(string name, params object[] props)
        {
            var n = new BNode { Name = name };
            n.Properties.AddRange(props);
            Children.Add(n);
            return n;
        }
    }

    // FBX's binary object-name encoding is "<Name>\0\x01<Class>" (the ASCII/text format displays this
    // reversed, as "Class::Name", but the raw property string stored in the binary file is name-first).
    private static string ObjName(string name, string fbxClass) => name + "\0" + fbxClass;

    public static void Write(FbxScene scene, string destPath)
    {
        var root = new BNode { Name = "" }; // synthetic container for the top-level node list; never itself serialized

        var header = root.Add("FBXHeaderExtension");
        header.Add("FBXHeaderVersion", 1003);
        header.Add("FBXVersion", 7400);
        header.Add("Creator", "DW2BT dw2bm xm");

        var globalSettings = root.Add("GlobalSettings");
        globalSettings.Add("Version", 1000);
        var gsProps = globalSettings.Add("Properties70");
        AddP(gsProps, "UpAxis", "int", "Integer", 1);
        AddP(gsProps, "UpAxisSign", "int", "Integer", 1);
        AddP(gsProps, "FrontAxis", "int", "Integer", 2);
        AddP(gsProps, "FrontAxisSign", "int", "Integer", 1);
        AddP(gsProps, "CoordAxis", "int", "Integer", 0);
        AddP(gsProps, "CoordAxisSign", "int", "Integer", 1);
        AddP(gsProps, "UnitScaleFactor", "double", "Number", 100.0);

        var definitions = root.Add("Definitions");
        definitions.Add("Version", 100);
        definitions.Add("Count", scene.Nodes.Count + scene.Meshes.Count * 2);

        var objects = root.Add("Objects");

        var nodeModelIds = new long[scene.Nodes.Count];
        for (int i = 0; i < scene.Nodes.Count; i++)
            nodeModelIds[i] = 1000 + i;

        var meshModelIds = new long[scene.Meshes.Count];
        var meshGeomIds = new long[scene.Meshes.Count];
        long nextId = 1000 + scene.Nodes.Count;
        for (int i = 0; i < scene.Meshes.Count; i++)
        {
            meshModelIds[i] = nextId++;
            meshGeomIds[i] = nextId++;
        }

        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            var n = scene.Nodes[i];
            var (ex, ey, ez) = QuaternionToEulerXYZDegrees(n.LocalRotation);
            var model = objects.Add("Model", nodeModelIds[i], ObjName(n.Name, "Model"), "Null");
            model.Add("Version", 232);
            var props = model.Add("Properties70");
            AddP(props, "Lcl Translation", "Lcl Translation", "", (double)n.LocalPosition.X, (double)n.LocalPosition.Y, (double)n.LocalPosition.Z);
            AddP(props, "Lcl Rotation", "Lcl Rotation", "", (double)ex, (double)ey, (double)ez);
            AddP(props, "Lcl Scaling", "Lcl Scaling", "", (double)n.LocalScale.X, (double)n.LocalScale.Y, (double)n.LocalScale.Z);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
        }

        for (int i = 0; i < scene.Meshes.Count; i++)
        {
            var m = scene.Meshes[i];

            var geom = objects.Add("Geometry", meshGeomIds[i], ObjName(m.Name, "Geometry"), "Mesh");

            var vertexArray = new double[m.Positions.Length * 3];
            for (int v = 0; v < m.Positions.Length; v++)
            {
                vertexArray[v * 3 + 0] = m.Positions[v].X;
                vertexArray[v * 3 + 1] = m.Positions[v].Y;
                vertexArray[v * 3 + 2] = m.Positions[v].Z;
            }
            geom.Add("Vertices", vertexArray);

            var triCount = m.TriangleIndices.Length / 3;
            var polyIndex = new int[m.TriangleIndices.Length];
            for (int t = 0; t < triCount; t++)
            {
                polyIndex[t * 3 + 0] = m.TriangleIndices[t * 3 + 0];
                polyIndex[t * 3 + 1] = m.TriangleIndices[t * 3 + 1];
                polyIndex[t * 3 + 2] = ~m.TriangleIndices[t * 3 + 2];
            }
            geom.Add("PolygonVertexIndex", polyIndex);
            geom.Add("GeometryVersion", 124);

            var normalLayer = geom.Add("LayerElementNormal", 0);
            normalLayer.Add("Version", 101);
            normalLayer.Add("Name", "");
            normalLayer.Add("MappingInformationType", "ByPolygonVertex");
            normalLayer.Add("ReferenceInformationType", "Direct");
            var normalArray = new double[m.TriangleIndices.Length * 3];
            for (int t = 0; t < m.TriangleIndices.Length; t++)
            {
                var nrm = m.Normals[m.TriangleIndices[t]];
                normalArray[t * 3 + 0] = nrm.X;
                normalArray[t * 3 + 1] = nrm.Y;
                normalArray[t * 3 + 2] = nrm.Z;
            }
            normalLayer.Add("Normals", normalArray);

            if (m.UVs != null)
            {
                var uvLayer = geom.Add("LayerElementUV", 0);
                uvLayer.Add("Version", 101);
                uvLayer.Add("Name", "UVMap");
                uvLayer.Add("MappingInformationType", "ByPolygonVertex");
                uvLayer.Add("ReferenceInformationType", "Direct");
                var uvArray = new double[m.TriangleIndices.Length * 2];
                for (int t = 0; t < m.TriangleIndices.Length; t++)
                {
                    var uv = m.UVs[m.TriangleIndices[t]];
                    // FBX/OpenGL-style UV origin is bottom-left; DW2's is top-left (D3D-style), so flip V.
                    uvArray[t * 2 + 0] = uv.U;
                    uvArray[t * 2 + 1] = 1.0 - uv.V;
                }
                uvLayer.Add("UV", uvArray);
            }

            var layer = geom.Add("Layer", 0);
            layer.Add("Version", 100);
            var leNormal = layer.Add("LayerElement");
            leNormal.Add("Type", "LayerElementNormal");
            leNormal.Add("TypedIndex", 0);
            if (m.UVs != null)
            {
                var leUv = layer.Add("LayerElement");
                leUv.Add("Type", "LayerElementUV");
                leUv.Add("TypedIndex", 0);
            }

            var meshModel = objects.Add("Model", meshModelIds[i], ObjName(m.Name, "Model"), "Mesh");
            meshModel.Add("Version", 232);
            meshModel.Add("Shading", true);
            meshModel.Add("Culling", "CullingOff");
        }

        var connections = root.Add("Connections");
        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            var parent = scene.Nodes[i].ParentIndex;
            // Object ID 0 is FBX's reserved id for the implicit document root; every node without an
            // explicit parent must connect to it, or readers (Blender included) treat it as an orphan
            // and silently skip it rather than surfacing an error.
            connections.Add("C", "OO", nodeModelIds[i], parent >= 0 ? nodeModelIds[parent] : 0L);
        }
        for (int i = 0; i < scene.Meshes.Count; i++)
        {
            connections.Add("C", "OO", meshGeomIds[i], meshModelIds[i]);
            connections.Add("C", "OO", meshModelIds[i], nodeModelIds[scene.Meshes[i].NodeIndex]);
        }

        using var fs = File.Create(destPath);
        using var bw = new BinaryWriter(fs, Encoding.UTF8);

        bw.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  "));
        bw.Write((byte)0x00);
        bw.Write((byte)0x1A);
        bw.Write((byte)0x00);
        bw.Write((uint)7400);

        foreach (var topLevel in root.Children)
            WriteNode(bw, topLevel);

        bw.Write(new byte[13]); // top-level NULL record terminator

        WriteFooter(bw, fs);
    }

    private static void AddP(BNode props70, string name, string type, string subType, params object[] values)
    {
        var p = new object[] { name, type, subType, "A" }.Concat(values).ToArray();
        props70.Add("P", p);
    }

    private static void WriteNode(BinaryWriter bw, BNode node)
    {
        var stream = bw.BaseStream;
        var endOffsetPos = stream.Position;
        bw.Write((uint)0); // EndOffset placeholder
        bw.Write((uint)node.Properties.Count);
        var propListLenPos = stream.Position;
        bw.Write((uint)0); // PropertyListLen placeholder

        var nameBytes = Encoding.ASCII.GetBytes(node.Name);
        bw.Write((byte)nameBytes.Length);
        bw.Write(nameBytes);

        var propsStart = stream.Position;
        foreach (var prop in node.Properties)
            WriteProperty(bw, prop);
        var propsLen = (uint)(stream.Position - propsStart);

        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
                WriteNode(bw, child);
            bw.Write(new byte[13]); // NULL record terminator marks end of this node's child list
        }

        var endPos = stream.Position;

        stream.Position = endOffsetPos;
        bw.Write((uint)endPos);
        stream.Position = propListLenPos;
        bw.Write(propsLen);
        stream.Position = endPos;
    }

    private static void WriteProperty(BinaryWriter bw, object value)
    {
        switch (value)
        {
            case bool b:
                bw.Write((byte)'C');
                bw.Write(b);
                break;
            case int i:
                bw.Write((byte)'I');
                bw.Write(i);
                break;
            case long l:
                bw.Write((byte)'L');
                bw.Write(l);
                break;
            case float f:
                bw.Write((byte)'F');
                bw.Write(f);
                break;
            case double d:
                bw.Write((byte)'D');
                bw.Write(d);
                break;
            case string s:
                bw.Write((byte)'S');
                var strBytes = Encoding.UTF8.GetBytes(s);
                bw.Write((uint)strBytes.Length);
                bw.Write(strBytes);
                break;
            case int[] ia:
                WriteArray(bw, (byte)'i', ia.Length, sizeof(int), buf => { for (int k = 0; k < ia.Length; k++) BitConverter.GetBytes(ia[k]).CopyTo(buf, k * 4); });
                break;
            case double[] da:
                WriteArray(bw, (byte)'d', da.Length, sizeof(double), buf => { for (int k = 0; k < da.Length; k++) BitConverter.GetBytes(da[k]).CopyTo(buf, k * 8); });
                break;
            case float[] fa:
                WriteArray(bw, (byte)'f', fa.Length, sizeof(float), buf => { for (int k = 0; k < fa.Length; k++) BitConverter.GetBytes(fa[k]).CopyTo(buf, k * 4); });
                break;
            default:
                throw new NotSupportedException($"Unsupported FBX property value type: {value.GetType()}");
        }
    }

    private static void WriteArray(BinaryWriter bw, byte typeCode, int count, int elemSize, Action<byte[]> fill)
    {
        var raw = new byte[count * elemSize];
        fill(raw);

        using var compressedStream = new MemoryStream();
        using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        var compressed = compressedStream.ToArray();

        bw.Write(typeCode);
        bw.Write((uint)count);
        bw.Write((uint)1); // Encoding: 1 = zlib-compressed
        bw.Write((uint)compressed.Length);
        bw.Write(compressed);
    }

    private static void WriteFooter(BinaryWriter bw, Stream stream)
    {
        // This footer's exact bytes aren't strictly validated by most readers (including Blender's),
        // but every real FBX binary file carries this same well-known sequence, so we match it for
        // maximum compatibility rather than relying on readers tolerating a shorter/absent footer.
        var footerId = new byte[]
        {
            0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66,
            0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e
        };
        bw.Write(footerId);

        var padTo16 = (int)((16 - (stream.Position % 16)) % 16);
        bw.Write(new byte[padTo16]);

        bw.Write(new byte[4]); // reserved
        bw.Write((uint)7400); // FBX version again

        bw.Write(new byte[120]); // padding

        var footer2 = new byte[]
        {
            0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e,
            0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b
        };
        bw.Write(footer2);
    }

    // Converts a quaternion to XYZ-order Euler angles in degrees, matching FBX's default
    // "eEulerXYZ" rotation order (R = Rz * Ry * Rx applied to a column vector).
    private static (float X, float Y, float Z) QuaternionToEulerXYZDegrees((float X, float Y, float Z, float W) q)
    {
        var (x, y, z, w) = q;

        float m02 = 2f * (x * z + y * w);
        float m12 = 2f * (y * z - x * w);
        float m22 = 1f - 2f * (x * x + y * y);
        float m01 = 2f * (x * y - z * w);
        float m00 = 1f - 2f * (y * y + z * z);

        float sy = Math.Clamp(m02, -1f, 1f);
        float ry = MathF.Asin(sy);
        float rx, rz;
        if (MathF.Abs(sy) < 0.9999f)
        {
            rx = MathF.Atan2(-m12, m22);
            rz = MathF.Atan2(-m01, m00);
        }
        else
        {
            rx = 0f;
            rz = MathF.Atan2(2f * (x * y + z * w), 1f - 2f * (y * y + z * z));
        }

        const float rad2deg = 180f / MathF.PI;
        return (rx * rad2deg, ry * rad2deg, rz * rad2deg);
    }
}
