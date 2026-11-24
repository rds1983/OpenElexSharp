using System.Buffers.Binary;
using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>One vertex attribute declaration from a GMSH v2 mesh.</summary>
public sealed record GmshVertexAttribute(
    int Usage,
    int UsageIndex,
    uint Format,
    uint Stream,
    uint Unknown0,
    uint Unknown1,
    uint Unknown2)
{
    /// <summary>Human-readable usage name for the common ELEX semantics.</summary>
    public string UsageName => Usage switch
    {
        0 => "position",
        1 => "normal",
        2 => "texcoord",
        3 => "tangent",
        4 => "color",
        _ => $"usage{Usage}",
    };
}

/// <summary>One vertex-stream descriptor from a GMSH v2 mesh.</summary>
public sealed record GmshVertexStream(int Usage, uint Unknown, uint Offset, uint Stride)
{
    /// <summary>Human-readable usage name for the common ELEX semantics.</summary>
    public string UsageName => Usage switch
    {
        0 => "position",
        1 => "normal",
        2 => "texcoord",
        3 => "tangent",
        4 => "color",
        _ => $"usage{Usage}",
    };
}

/// <summary>A raw material/texture reference record from the GMSH v2 material section.</summary>
public sealed record GmshMaterialReference(IReadOnlyList<uint> Raw)
{
    /// <summary>Returns the first value of the record.</summary>
    public uint Value0 => Raw.Count > 0 ? Raw[0] : 0;

    /// <summary>Returns the record hash field when present.</summary>
    public uint Hash => Raw.Count > 6 ? Raw[6] : 0;
}

/// <summary>
/// A parsed ELEX GMSH v2 static mesh. ELEX <c>msh</c> resources are a GAR5 wrapper followed by
/// a GMSH header, indices, a vertex position buffer and declaration/stream metadata.
/// </summary>
public sealed class GmshMesh
{
    private const uint GmshMagic = 0x48534D47; // "GMSH"
    private const int Gar5WrapperSize = 44;
    private const int HeaderValueCount = 14;
    private const int VertexAttributeSize = 28;
    private const int VertexStreamSize = 16;
    private const int MaxAttributeCount = 64;
    private const int MaxStreamCount = 64;
    private const int MaxMaterialCount = 4096;
    private const uint MaxIndexCount = 64u * 1024u * 1024u;
    private const uint MaxVertexCount = 16u * 1024u * 1024u;
    private const float MaxCoordinateMagnitude = 10_000_000f;

    private GmshMesh(
        string name,
        string sourcePath,
        int gmshOffset,
        uint version,
        uint[] headerValues,
        uint indexOffset,
        uint[] indices,
        Vector3[] positions,
        GmshVertexAttribute[] vertexAttributes,
        GmshVertexStream[] vertexStreams,
        IReadOnlyList<GmshMaterialReference> materialReferences,
        int materialSectionOffset,
        int materialSectionLength)
    {
        Name = name;
        SourcePath = sourcePath;
        GmshOffset = gmshOffset;
        Version = version;
        HeaderValues = headerValues;
        IndexOffset = indexOffset;
        Indices = indices;
        Positions = positions;
        VertexAttributes = vertexAttributes;
        VertexStreams = vertexStreams;
        MaterialReferences = materialReferences;
        MaterialSectionOffset = materialSectionOffset;
        MaterialSectionLength = materialSectionLength;
    }

    /// <summary>Logical resource name.</summary>
    public string Name { get; }

    /// <summary>Virtual path of the owning resource.</summary>
    public string SourcePath { get; }

    /// <summary>Offset of the GMSH magic in the resource.</summary>
    public int GmshOffset { get; }

    /// <summary>GMSH version; currently 2.</summary>
    public uint Version { get; }

    /// <summary>The 14 raw header values following the version.</summary>
    public IReadOnlyList<uint> HeaderValues { get; }

    /// <summary>Offset of the index block relative to the GMSH magic.</summary>
    public uint IndexOffset { get; }

    /// <summary>Triangle indices.</summary>
    public IReadOnlyList<uint> Indices { get; }

    /// <summary>Per-vertex positions.</summary>
    public IReadOnlyList<Vector3> Positions { get; }

    /// <summary>Vertex attribute declarations.</summary>
    public IReadOnlyList<GmshVertexAttribute> VertexAttributes { get; }

    /// <summary>Vertex stream descriptors.</summary>
    public IReadOnlyList<GmshVertexStream> VertexStreams { get; }

    /// <summary>Raw material/texture reference records.</summary>
    public IReadOnlyList<GmshMaterialReference> MaterialReferences { get; }

    /// <summary>Offset of the material section relative to the GMSH magic.</summary>
    public int MaterialSectionOffset { get; }

    /// <summary>Length in bytes of the material section.</summary>
    public int MaterialSectionLength { get; }

    /// <summary>Number of triangles when the index count is a multiple of three.</summary>
    public int TriangleCount => Indices.Count / 3;

    /// <summary>Whether the index buffer represents a triangle list.</summary>
    public bool IsTriangleList => Indices.Count % 3 == 0;

    /// <summary>Whether the mesh declares a normal attribute or stream.</summary>
    public bool HasNormals =>
        VertexAttributes.Any(attribute => attribute.Usage == 1) ||
        VertexStreams.Any(stream => stream.Usage == 1);

    /// <summary>Whether the mesh declares a texture-coordinate attribute or stream.</summary>
    public bool HasTextureCoordinates =>
        VertexAttributes.Any(attribute => attribute.Usage == 2) ||
        VertexStreams.Any(stream => stream.Usage == 2);

    /// <summary>Returns the position attribute when available.</summary>
    public GmshVertexAttribute? PositionAttribute =>
        VertexAttributes.FirstOrDefault(attribute => attribute.Usage == 0);

    /// <summary>Parses a GMSH v2 resource, optionally wrapped in a GAR5 resource header.</summary>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="GmshFormatException">The mesh is malformed or unsupported.</exception>
    public static GmshMesh Parse(string name, string sourcePath, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(data);

        var gmshOffset = FindGmshOffset(data);
        if (gmshOffset + 8 + HeaderValueCount * 4 > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has a truncated GMSH header.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(gmshOffset + 4));
        if (version != 2)
        {
            throw new GmshFormatException($"Mesh '{name}' uses unsupported GMSH version {version}.");
        }

        var headerValues = new uint[HeaderValueCount];
        for (var index = 0; index < headerValues.Length; index++)
        {
            headerValues[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(gmshOffset + 8 + index * 4));
        }

        var indexOffset = headerValues[0];
        if (indexOffset >= data.Length - gmshOffset)
        {
            throw new GmshFormatException($"Mesh '{name}' has an invalid index offset.");
        }

        var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(gmshOffset + (int)indexOffset));
        if (indexCount == 0 || indexCount > MaxIndexCount)
        {
            throw new GmshFormatException($"Mesh '{name}' has an invalid index count {indexCount}.");
        }

        var indexDataOffset = gmshOffset + (int)indexOffset + 4;
        var indexByteCount = checked((int)indexCount * 4);
        if (indexDataOffset + indexByteCount + 4 > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated index data.");
        }

        var indices = new uint[indexCount];
        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(indexDataOffset + index * 4));
        }

        var vertexCountOffset = indexDataOffset + indexByteCount;
        var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(vertexCountOffset));
        if (vertexCount == 0 || vertexCount > MaxVertexCount)
        {
            throw new GmshFormatException($"Mesh '{name}' has an invalid vertex count {vertexCount}.");
        }

        var positionDataOffset = vertexCountOffset + 4;
        var positionByteCount = checked((int)vertexCount * 12);
        if (positionDataOffset + positionByteCount > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated position data.");
        }

        var positions = new Vector3[vertexCount];
        for (var index = 0; index < positions.Length; index++)
        {
            var offset = positionDataOffset + index * 12;
            var x = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
            var y = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4));
            var z = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8));
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                MathF.Abs(x) > MaxCoordinateMagnitude ||
                MathF.Abs(y) > MaxCoordinateMagnitude ||
                MathF.Abs(z) > MaxCoordinateMagnitude)
            {
                throw new GmshFormatException($"Mesh '{name}' has a non-finite or unreasonable position.");
            }

            positions[index] = new Vector3(x, y, z);
        }

        for (var index = 0; index < indices.Length; index++)
        {
            if (indices[index] >= vertexCount)
            {
                throw new GmshFormatException(
                    $"Mesh '{name}' index {index} references vertex {indices[index]} of {vertexCount}.");
            }
        }

        var vertexAttributes = ReadVertexAttributes(name, data, gmshOffset, headerValues[4], headerValues[5]);
        var vertexStreams = ReadVertexStreams(name, data, gmshOffset, headerValues[8], headerValues[9]);

        var materialOffset = headerValues[11];
        var meshHeaderOffset = headerValues[3];
        var materialCount = headerValues[10];
        var materialSectionLength = 0;
        var materialReferences = new List<GmshMaterialReference>();
        if (materialCount > 0 && materialCount <= MaxMaterialCount &&
            meshHeaderOffset > materialOffset && meshHeaderOffset <= data.Length - gmshOffset)
        {
            materialSectionLength = (int)(meshHeaderOffset - materialOffset);
            if (materialSectionLength == materialCount * 10 * 4)
            {
                for (var record = 0; record < materialCount; record++)
                {
                    var values = new uint[10];
                    for (var value = 0; value < values.Length; value++)
                    {
                        values[value] = BinaryPrimitives.ReadUInt32LittleEndian(
                            data.AsSpan(gmshOffset + (int)materialOffset + record * 40 + value * 4));
                    }

                    materialReferences.Add(new GmshMaterialReference(values));
                }
            }
        }

        return new GmshMesh(
            name,
            sourcePath,
            gmshOffset,
            version,
            headerValues,
            indexOffset,
            indices,
            positions,
            vertexAttributes,
            vertexStreams,
            materialReferences,
            (int)materialOffset,
            materialSectionLength);
    }

    private static GmshVertexAttribute[] ReadVertexAttributes(
        string name,
        byte[] data,
        int gmshOffset,
        uint count,
        uint offset)
    {
        if (count == 0)
        {
            return [];
        }

        if (count > MaxAttributeCount)
        {
            throw new GmshFormatException($"Mesh '{name}' declares too many vertex attributes ({count}).");
        }

        var byteCount = checked((int)count * VertexAttributeSize);
        if (offset + byteCount > data.Length - gmshOffset)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated vertex attributes.");
        }

        var attributes = new GmshVertexAttribute[count];
        for (var index = 0; index < attributes.Length; index++)
        {
            var record = gmshOffset + (int)offset + index * VertexAttributeSize;
            attributes[index] = new GmshVertexAttribute(
                (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record)),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 12)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 16)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 20)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 24)));
        }

        return attributes;
    }

    private static GmshVertexStream[] ReadVertexStreams(
        string name,
        byte[] data,
        int gmshOffset,
        uint count,
        uint offset)
    {
        if (count == 0)
        {
            return [];
        }

        if (count > MaxStreamCount)
        {
            throw new GmshFormatException($"Mesh '{name}' declares too many vertex streams ({count}).");
        }

        var byteCount = checked((int)count * VertexStreamSize);
        if (offset + byteCount > data.Length - gmshOffset)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated vertex streams.");
        }

        var streams = new GmshVertexStream[count];
        for (var index = 0; index < streams.Length; index++)
        {
            var record = gmshOffset + (int)offset + index * VertexStreamSize;
            streams[index] = new GmshVertexStream(
                (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 12)));
        }

        return streams;
    }

    private static int FindGmshOffset(byte[] data)
    {
        if (data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == GmshMagic)
        {
            return 0;
        }

        if (data.Length >= Gar5WrapperSize + 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(data) == Gar5Header.Magic &&
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize)) == GmshMagic)
        {
            return Gar5WrapperSize;
        }

        throw new GmshFormatException("The data does not start with a GMSH file or a GAR5 mesh wrapper.");
    }
}
