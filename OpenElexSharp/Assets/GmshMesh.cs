using System.Buffers.Binary;
using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>Semantic usage of a GMSH vertex attribute, as stored in the attribute record.</summary>
public enum GmshVertexUsage
{
    /// <summary>Object-space position.</summary>
    Position = 0,

    /// <summary>Object-space normal.</summary>
    Normal = 1,

    /// <summary>Object-space tangent.</summary>
    Tangent = 2,

    /// <summary>Vertex color.</summary>
    Color = 3,

    /// <summary>Texture coordinate set.</summary>
    TextureCoordinate = 4,
}

/// <summary>
/// Known <c>eEGfxFormat2</c> format codes used by GMSH vertex attributes. The enum mirrors the
/// engine's DXGI-style format list: the observed codes map to R32G32B32_Float (4),
/// R16G16B16A16_Float (7), R10G10B10A2_UNorm (15), R16G16_Float (23) and B8G8R8A8_UNorm (58).
/// </summary>
public static class GmshVertexFormat
{
    /// <summary><c>R32G32B32_Float</c>: three little-endian floats.</summary>
    public const uint R32G32B32Float = 4;

    /// <summary><c>R16G16B16A16_Float</c>: four half floats; positions use the first three.</summary>
    public const uint R16G16B16A16Float = 7;

    /// <summary><c>R10G10B10A2_UNorm</c>: 10-bit signed-ish components biased at 511.5.</summary>
    public const uint R10G10B10A2UNorm = 15;

    /// <summary><c>R16G16_Float</c>: two half floats.</summary>
    public const uint R16G16Float = 23;

    /// <summary><c>B8G8R8A8_UNorm</c>: four bytes in blue, green, red, alpha order.</summary>
    public const uint B8G8R8A8UNorm = 58;

    /// <summary>Returns the byte size of one element, or 0 for an unknown format.</summary>
    public static int GetSize(uint format) => format switch
    {
        R32G32B32Float => 12,
        R16G16B16A16Float => 8,
        R10G10B10A2UNorm => 4,
        R16G16Float => 4,
        B8G8R8A8UNorm => 4,
        _ => 0,
    };

    /// <summary>Returns a human-readable name for the known formats.</summary>
    public static string GetName(uint format) => format switch
    {
        R32G32B32Float => "R32G32B32_Float",
        R16G16B16A16Float => "R16G16B16A16_Float",
        R10G10B10A2UNorm => "R10G10B10A2_UNorm",
        R16G16Float => "R16G16_Float",
        B8G8R8A8UNorm => "B8G8R8A8_UNorm",
        _ => $"format{format}",
    };
}

/// <summary>One vertex attribute declaration from a GMSH v2 mesh.</summary>
/// <param name="Usage">Semantic usage; see <see cref="GmshVertexUsage"/>.</param>
/// <param name="UsageOffset">
/// Byte offset of this attribute inside the vertex-stream record. Two attributes that share a stream
/// (for example two texture-coordinate sets) use different offsets.
/// </param>
/// <param name="Format">An <c>eEGfxFormat2</c> format code; see <see cref="GmshVertexFormat"/>.</param>
/// <param name="Slot">Observed shader-slot value. It is not the stream index; attributes bind to streams by usage.</param>
/// <param name="Unknown0">Unresolved field, observed zero.</param>
/// <param name="Unknown1">Unresolved field, observed zero.</param>
/// <param name="Unknown2">Unresolved field, observed zero.</param>
public sealed record GmshVertexAttribute(
    int Usage,
    int UsageOffset,
    uint Format,
    uint Slot,
    uint Unknown0,
    uint Unknown1,
    uint Unknown2)
{
    /// <summary>Strongly typed usage.</summary>
    public GmshVertexUsage UsageKind => (GmshVertexUsage)Usage;

    /// <summary>Human-readable usage name.</summary>
    public string UsageName => UsageKind switch
    {
        GmshVertexUsage.Position => "position",
        GmshVertexUsage.Normal => "normal",
        GmshVertexUsage.Tangent => "tangent",
        GmshVertexUsage.Color => "color",
        GmshVertexUsage.TextureCoordinate => "texcoord",
        _ => $"usage{Usage}",
    };

    /// <summary>Human-readable format name.</summary>
    public string FormatName => GmshVertexFormat.GetName(Format);
}

/// <summary>One tightly packed vertex stream from a GMSH v2 mesh.</summary>
/// <param name="Usage">Semantic usage; see <see cref="GmshVertexUsage"/>.</param>
/// <param name="Unknown">Unresolved field, observed as 1.</param>
/// <param name="Offset">Byte offset of the stream from the start of the vertex data.</param>
/// <param name="Stride">Size of one stream record in bytes.</param>
public sealed record GmshVertexStream(int Usage, uint Unknown, uint Offset, uint Stride)
{
    /// <summary>Strongly typed usage.</summary>
    public GmshVertexUsage UsageKind => (GmshVertexUsage)Usage;

    /// <summary>Human-readable usage name.</summary>
    public string UsageName => UsageKind switch
    {
        GmshVertexUsage.Position => "position",
        GmshVertexUsage.Normal => "normal",
        GmshVertexUsage.Tangent => "tangent",
        GmshVertexUsage.Color => "color",
        GmshVertexUsage.TextureCoordinate => "texcoord",
        _ => $"usage{Usage}",
    };
}

/// <summary>
/// One 40-byte buffer descriptor stored in the GMSH block before the mesh header. The two retail
/// records describe the embedded index buffer and the vertex buffer; the first record's element-size
/// field selects 16- or 32-bit indices.
/// </summary>
public sealed record GmshBufferDescriptor(IReadOnlyList<uint> Raw)
{
    /// <summary>Size of the described buffer in bytes.</summary>
    public uint Size => Raw.Count > 0 ? Raw[0] : 0;

    /// <summary>Index element size in bytes (2 or 4) for the index-buffer descriptor; otherwise 0.</summary>
    public uint ElementSize => Raw.Count > 3 ? Raw[3] : 0;

    /// <summary>Repeated class or format hash observed at field 6.</summary>
    public uint Hash => Raw.Count > 6 ? Raw[6] : 0;
}

/// <summary>
/// A parsed ELEX GMSH v2 static mesh. ELEX <c>msh</c> resources are a GAR5 wrapper followed by a
/// GMSH header, a 16- or 32-bit GPU index buffer, tightly packed vertex streams, two buffer
/// descriptors, and declaration tables. A second 32-bit index block and a float3 position copy are
/// stored after the declarations for tooling.
/// </summary>
public sealed class GmshMesh
{
    private const uint GmshMagic = 0x48534D47; // "GMSH"
    private const int Gar5WrapperSize = 44;
    private const int HeaderValueCount = 14;
    private const int HeaderSize = 8 + HeaderValueCount * 4;
    private const int VertexAttributeSize = 28;
    private const int VertexStreamSize = 16;
    private const int BufferDescriptorSize = 40;
    private const int MaxAttributeCount = 64;
    private const int MaxStreamCount = 64;
    private const int MaxBufferDescriptorCount = 64;
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
        int[] indices,
        uint[] sourceIndices,
        Vector3[] positions,
        Vector3[] sourcePositions,
        Vector3[] normals,
        Vector3[] tangents,
        Vector4[] colors,
        Vector2[] textureCoordinates,
        Vector2[] textureCoordinates1,
        GmshVertexAttribute[] vertexAttributes,
        GmshVertexStream[] vertexStreams,
        GmshBufferDescriptor[] bufferDescriptors,
        IReadOnlyList<string> diagnostics,
        int indexDataOffset,
        int vertexDataOffset,
        int vertexDataLength,
        int bufferDescriptorOffset,
        int bufferDescriptorLength)
    {
        Name = name;
        SourcePath = sourcePath;
        GmshOffset = gmshOffset;
        Version = version;
        HeaderValues = headerValues;
        IndexOffset = indexOffset;
        Indices = indices;
        SourceIndices = sourceIndices;
        Positions = positions;
        SourcePositions = sourcePositions;
        Normals = normals;
        Tangents = tangents;
        Colors = colors;
        TextureCoordinates = textureCoordinates;
        TextureCoordinates1 = textureCoordinates1;
        VertexAttributes = vertexAttributes;
        VertexStreams = vertexStreams;
        BufferDescriptors = bufferDescriptors;
        Diagnostics = diagnostics;
        IndexDataOffset = indexDataOffset;
        VertexDataOffset = vertexDataOffset;
        VertexDataLength = vertexDataLength;
        BufferDescriptorOffset = bufferDescriptorOffset;
        BufferDescriptorLength = bufferDescriptorLength;
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

    /// <summary>Offset of the 32-bit tooling index block relative to the GMSH magic.</summary>
    public uint IndexOffset { get; }

    /// <summary>The GPU triangle-list index buffer (16- or 32-bit, widened to <see cref="int"/>).</summary>
    public IReadOnlyList<int> Indices { get; }

    /// <summary>The 32-bit index copy stored after the declaration tables (tooling data).</summary>
    public IReadOnlyList<uint> SourceIndices { get; }

    /// <summary>Per-vertex positions decoded from the position stream, or the float3 copy if absent.</summary>
    public IReadOnlyList<Vector3> Positions { get; }

    /// <summary>The redundant float3 position array stored after the declarations.</summary>
    public IReadOnlyList<Vector3> SourcePositions { get; }

    /// <summary>Per-vertex normalized normals; empty when the mesh has no normal stream.</summary>
    public IReadOnlyList<Vector3> Normals { get; }

    /// <summary>Per-vertex normalized tangents; empty when the mesh has no tangent stream.</summary>
    public IReadOnlyList<Vector3> Tangents { get; }

    /// <summary>Per-vertex RGBA colors; empty when the mesh has no color stream.</summary>
    public IReadOnlyList<Vector4> Colors { get; }

    /// <summary>Per-vertex first texture-coordinate set; empty when the mesh has no UV stream.</summary>
    public IReadOnlyList<Vector2> TextureCoordinates { get; }

    /// <summary>Per-vertex second texture-coordinate set, when present.</summary>
    public IReadOnlyList<Vector2> TextureCoordinates1 { get; }

    /// <summary>Vertex attribute declarations.</summary>
    public IReadOnlyList<GmshVertexAttribute> VertexAttributes { get; }

    /// <summary>Vertex stream descriptors.</summary>
    public IReadOnlyList<GmshVertexStream> VertexStreams { get; }

    /// <summary>Embedded index/vertex buffer descriptors.</summary>
    public IReadOnlyList<GmshBufferDescriptor> BufferDescriptors { get; }

    /// <summary>Non-fatal decoding notes, such as an unknown optional attribute format.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Offset of the GPU index buffer relative to the GMSH magic.</summary>
    public int IndexDataOffset { get; }

    /// <summary>Offset of the packed vertex streams relative to the GMSH magic.</summary>
    public int VertexDataOffset { get; }

    /// <summary>Total byte length of the packed vertex streams.</summary>
    public int VertexDataLength { get; }

    /// <summary>Offset of the buffer descriptor block relative to the GMSH magic.</summary>
    public int BufferDescriptorOffset { get; }

    /// <summary>Byte length of the buffer descriptor block.</summary>
    public int BufferDescriptorLength { get; }

    /// <summary>Number of triangles when the index count is a multiple of three.</summary>
    public int TriangleCount => Indices.Count / 3;

    /// <summary>Whether the index buffer represents a triangle list.</summary>
    public bool IsTriangleList => Indices.Count % 3 == 0;

    /// <summary>Whether the mesh has decoded normals.</summary>
    public bool HasNormals => Normals.Count == Positions.Count && Normals.Count > 0;

    /// <summary>Whether the mesh has decoded texture coordinates.</summary>
    public bool HasTextureCoordinates =>
        TextureCoordinates.Count == Positions.Count && TextureCoordinates.Count > 0;

    /// <summary>Returns the position attribute when available.</summary>
    public GmshVertexAttribute? PositionAttribute =>
        VertexAttributes.FirstOrDefault(attribute => attribute.Usage == (int)GmshVertexUsage.Position);

    /// <summary>Parses a GMSH v2 resource, optionally wrapped in a GAR5 resource header.</summary>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="GmshFormatException">The mesh is malformed or unsupported.</exception>
    public static GmshMesh Parse(string name, string sourcePath, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(data);

        var gmshOffset = FindGmshOffset(data);
        if (gmshOffset + HeaderSize > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has a truncated GMSH header.");
        }

        var version = ReadUInt32(data, gmshOffset + 4);
        if (version != 2)
        {
            throw new GmshFormatException($"Mesh '{name}' uses unsupported GMSH version {version}.");
        }

        var headerValues = new uint[HeaderValueCount];
        for (var index = 0; index < headerValues.Length; index++)
        {
            headerValues[index] = ReadUInt32(data, gmshOffset + 8 + index * 4);
        }

        var indexOffset = headerValues[0];
        if (indexOffset >= data.Length - gmshOffset)
        {
            throw new GmshFormatException($"Mesh '{name}' has an invalid index offset.");
        }

        var indexCount = ReadUInt32(data, gmshOffset + (int)indexOffset);
        if (indexCount == 0 || indexCount > MaxIndexCount)
        {
            throw new GmshFormatException($"Mesh '{name}' has an invalid index count {indexCount}.");
        }

        var sourceIndexOffset = gmshOffset + (int)indexOffset + 4;
        var sourceIndexByteCount = checked((int)indexCount * 4);
        if (sourceIndexOffset + sourceIndexByteCount + 4 > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated tooling index data.");
        }

        var sourceIndices = new uint[indexCount];
        for (var index = 0; index < sourceIndices.Length; index++)
        {
            sourceIndices[index] = ReadUInt32(data, sourceIndexOffset + index * 4);
        }

        var vertexCountOffset = sourceIndexOffset + sourceIndexByteCount;
        var vertexCount = ReadUInt32(data, vertexCountOffset);
        if (vertexCount == 0 || vertexCount > MaxVertexCount)
        {
            throw new GmshFormatException($"Mesh '{name}' has an invalid vertex count {vertexCount}.");
        }

        var sourcePositionOffset = vertexCountOffset + 4;
        var sourcePositionByteCount = checked((int)vertexCount * 12);
        if (sourcePositionOffset + sourcePositionByteCount > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated position data.");
        }

        var sourcePositions = new Vector3[vertexCount];
        for (var index = 0; index < sourcePositions.Length; index++)
        {
            var offset = sourcePositionOffset + index * 12;
            var x = ReadSingle(data, offset);
            var y = ReadSingle(data, offset + 4);
            var z = ReadSingle(data, offset + 8);
            if (!IsUsableCoordinate(x) || !IsUsableCoordinate(y) || !IsUsableCoordinate(z))
            {
                throw new GmshFormatException($"Mesh '{name}' has a non-finite or unreasonable position.");
            }

            sourcePositions[index] = new Vector3(x, y, z);
        }

        for (var index = 0; index < sourceIndices.Length; index++)
        {
            if (sourceIndices[index] >= vertexCount)
            {
                throw new GmshFormatException(
                    $"Mesh '{name}' tooling index {index} references vertex {sourceIndices[index]} of {vertexCount}.");
            }
        }

        var vertexAttributes = ReadVertexAttributes(name, data, gmshOffset, headerValues[4], headerValues[5]);
        var vertexStreams = ReadVertexStreams(name, data, gmshOffset, headerValues[8], headerValues[9]);
        var (bufferDescriptors, descriptorError) = ReadBufferDescriptors(
            name, data, gmshOffset, headerValues[10], headerValues[11], headerValues[3]);

        var diagnostics = new List<string>();
        if (descriptorError is not null)
        {
            diagnostics.Add(descriptorError);
        }

        var indexElementSize = ResolveIndexElementSize(bufferDescriptors, vertexCount, indexCount, diagnostics);
        var indexDataOffset = gmshOffset + HeaderSize;
        var indexByteCount = checked((int)indexCount * indexElementSize);
        if (indexDataOffset + indexByteCount > data.Length)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated {indexElementSize * 8}-bit indices.");
        }

        var indices = new int[indexCount];
        for (var index = 0; index < indices.Length; index++)
        {
            var value = indexElementSize == 2
                ? ReadUInt16(data, indexDataOffset + index * 2)
                : ReadUInt32(data, indexDataOffset + index * 4);
            if (value >= vertexCount)
            {
                throw new GmshFormatException(
                    $"Mesh '{name}' index {index} references vertex {value} of {vertexCount}.");
            }

            indices[index] = checked((int)value);
        }

        var vertexDataOffset = Align16(HeaderSize + indexByteCount);
        var vertexDataLength = 0;
        foreach (var stream in vertexStreams)
        {
            vertexDataLength = checked(vertexDataLength + checked((int)stream.Stride * (int)vertexCount));
        }

        if (vertexDataOffset + vertexDataLength > data.Length - gmshOffset)
        {
            throw new GmshFormatException($"Mesh '{name}' has truncated vertex streams.");
        }

        var positions = sourcePositions;
        var normals = Array.Empty<Vector3>();
        var tangents = Array.Empty<Vector3>();
        var colors = Array.Empty<Vector4>();
        var textureCoordinates = Array.Empty<Vector2>();
        var textureCoordinates1 = Array.Empty<Vector2>();
        foreach (var attribute in vertexAttributes)
        {
            var stream = FindStream(vertexStreams, attribute.Usage);
            var size = GmshVertexFormat.GetSize(attribute.Format);
            if (stream is null)
            {
                diagnostics.Add($"{attribute.UsageName} attribute has no matching stream.");
                continue;
            }

            if (size == 0)
            {
                diagnostics.Add(
                    $"{attribute.UsageName} attribute uses unknown format {attribute.Format}.");
                continue;
            }

            var elementOffset = checked((int)stream.Offset + (int)attribute.UsageOffset);
            if ((int)attribute.UsageOffset + size > stream.Stride)
            {
                throw new GmshFormatException(
                    $"Mesh '{name}' attribute '{attribute.UsageName}' does not fit its stream record.");
            }

            switch ((GmshVertexUsage)attribute.Usage)
            {
                case GmshVertexUsage.Position:
                    var decodedPositions = DecodePositions(
                        data, gmshOffset + vertexDataOffset, elementOffset, (int)stream.Stride,
                        vertexCount, attribute.Format);
                    if (decodedPositions.Length == vertexCount)
                    {
                        positions = decodedPositions;
                    }
                    else
                    {
                        diagnostics.Add("Position stream is not a usable position format.");
                    }

                    break;

                case GmshVertexUsage.Normal:
                    normals = DecodeVectors(
                        data, gmshOffset + vertexDataOffset, elementOffset, (int)stream.Stride,
                        vertexCount, attribute.Format);
                    break;

                case GmshVertexUsage.Tangent:
                    tangents = DecodeVectors(
                        data, gmshOffset + vertexDataOffset, elementOffset, (int)stream.Stride,
                        vertexCount, attribute.Format);
                    break;

                case GmshVertexUsage.Color:
                    colors = DecodeColors(
                        data, gmshOffset + vertexDataOffset, elementOffset, (int)stream.Stride,
                        vertexCount, attribute.Format);
                    break;

                case GmshVertexUsage.TextureCoordinate:
                    var decodedUvs = DecodeTextureCoordinates(
                        data, gmshOffset + vertexDataOffset, elementOffset, (int)stream.Stride,
                        vertexCount, attribute.Format);
                    if (textureCoordinates.Length == 0)
                    {
                        textureCoordinates = decodedUvs;
                    }
                    else if (textureCoordinates1.Length == 0)
                    {
                        textureCoordinates1 = decodedUvs;
                    }

                    break;
            }
        }

        var bufferDescriptorOffset = (int)headerValues[11];
        var bufferDescriptorLength = (int)(headerValues[3] - headerValues[11]);
        if (bufferDescriptorLength < 0)
        {
            bufferDescriptorLength = 0;
        }

        return new GmshMesh(
            name,
            sourcePath,
            gmshOffset,
            version,
            headerValues,
            indexOffset,
            indices,
            sourceIndices,
            positions,
            sourcePositions,
            normals,
            tangents,
            colors,
            textureCoordinates,
            textureCoordinates1,
            vertexAttributes,
            vertexStreams,
            bufferDescriptors,
            diagnostics,
            indexDataOffset - gmshOffset,
            vertexDataOffset,
            vertexDataLength,
            bufferDescriptorOffset,
            bufferDescriptorLength);
    }

    private static int ResolveIndexElementSize(
        GmshBufferDescriptor[] descriptors,
        uint vertexCount,
        uint indexCount,
        List<string> diagnostics)
    {
        if (descriptors.Length > 0)
        {
            var elementSize = descriptors[0].ElementSize;
            if (elementSize is 2 or 4)
            {
                if (descriptors[0].Size != indexCount * elementSize)
                {
                    diagnostics.Add(
                        $"Index descriptor size {descriptors[0].Size} does not match {indexCount} indices.");
                }

                return (int)elementSize;
            }
        }

        return vertexCount > ushort.MaxValue + 1u ? 4 : 2;
    }

    private static GmshVertexStream? FindStream(GmshVertexStream[] streams, int usage)
    {
        foreach (var stream in streams)
        {
            if (stream.Usage == usage)
            {
                return stream;
            }
        }

        return null;
    }

    private static Vector3[] DecodePositions(
        byte[] data,
        int vertexDataOffset,
        int elementOffset,
        int stride,
        uint vertexCount,
        uint format)
    {
        var result = new Vector3[vertexCount];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = vertexDataOffset + elementOffset + index * stride;
            switch (format)
            {
                case GmshVertexFormat.R32G32B32Float:
                    result[index] = new Vector3(
                        ReadSingle(data, offset),
                        ReadSingle(data, offset + 4),
                        ReadSingle(data, offset + 8));
                    break;

                case GmshVertexFormat.R16G16B16A16Float:
                    result[index] = new Vector3(
                        ReadHalf(data, offset),
                        ReadHalf(data, offset + 2),
                        ReadHalf(data, offset + 4));
                    break;

                default:
                    return [];
            }
        }

        return result;
    }

    private static Vector3[] DecodeVectors(
        byte[] data,
        int vertexDataOffset,
        int elementOffset,
        int stride,
        uint vertexCount,
        uint format)
    {
        if (format != GmshVertexFormat.R10G10B10A2UNorm)
        {
            return [];
        }

        var result = new Vector3[vertexCount];
        for (var index = 0; index < result.Length; index++)
        {
            var packed = ReadUInt32(data, vertexDataOffset + elementOffset + index * stride);
            var x = DecodePackedComponent(packed & 0x3FF);
            var y = DecodePackedComponent((packed >> 10) & 0x3FF);
            var z = DecodePackedComponent((packed >> 20) & 0x3FF);
            var vector = new Vector3(x, y, z);
            result[index] = vector.LengthSquared() > 1e-12f
                ? Vector3.Normalize(vector)
                : Vector3.UnitY;
        }

        return result;
    }

    private static Vector4[] DecodeColors(
        byte[] data,
        int vertexDataOffset,
        int elementOffset,
        int stride,
        uint vertexCount,
        uint format)
    {
        if (format != GmshVertexFormat.B8G8R8A8UNorm)
        {
            return [];
        }

        var result = new Vector4[vertexCount];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = vertexDataOffset + elementOffset + index * stride;
            result[index] = new Vector4(
                data[offset + 2] / 255f,
                data[offset + 1] / 255f,
                data[offset] / 255f,
                data[offset + 3] / 255f);
        }

        return result;
    }

    private static Vector2[] DecodeTextureCoordinates(
        byte[] data,
        int vertexDataOffset,
        int elementOffset,
        int stride,
        uint vertexCount,
        uint format)
    {
        if (format != GmshVertexFormat.R16G16Float)
        {
            return [];
        }

        var result = new Vector2[vertexCount];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = vertexDataOffset + elementOffset + index * stride;
            result[index] = new Vector2(ReadHalf(data, offset), ReadHalf(data, offset + 2));
        }

        return result;
    }

    private static float DecodePackedComponent(uint raw) =>
        Math.Clamp(raw / 511.5f - 1f, -1f, 1f);

    private static bool IsUsableCoordinate(float value) =>
        float.IsFinite(value) && MathF.Abs(value) <= MaxCoordinateMagnitude;

    private static int Align16(int value) => (value + 15) & ~15;

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
                (int)ReadUInt32(data, record),
                (int)ReadUInt32(data, record + 4),
                ReadUInt32(data, record + 8),
                ReadUInt32(data, record + 12),
                ReadUInt32(data, record + 16),
                ReadUInt32(data, record + 20),
                ReadUInt32(data, record + 24));
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
                (int)ReadUInt32(data, record),
                ReadUInt32(data, record + 4),
                ReadUInt32(data, record + 8),
                ReadUInt32(data, record + 12));
        }

        return streams;
    }

    private static (GmshBufferDescriptor[] Descriptors, string? Error) ReadBufferDescriptors(
        string name,
        byte[] data,
        int gmshOffset,
        uint count,
        uint offset,
        uint meshHeaderOffset)
    {
        if (count == 0)
        {
            return ([], null);
        }

        if (count > MaxBufferDescriptorCount)
        {
            return ([], $"Mesh declares too many buffer descriptors ({count}).");
        }

        var byteCount = checked((int)count * BufferDescriptorSize);
        if (offset > data.Length - gmshOffset || offset + byteCount > data.Length - gmshOffset)
        {
            return ([], "Buffer descriptors are truncated.");
        }

        if (meshHeaderOffset > offset && meshHeaderOffset - offset != byteCount)
        {
            return ([], $"Buffer descriptor block is {meshHeaderOffset - offset} bytes, expected {byteCount}.");
        }

        var descriptors = new GmshBufferDescriptor[count];
        for (var index = 0; index < descriptors.Length; index++)
        {
            var values = new uint[10];
            for (var value = 0; value < values.Length; value++)
            {
                values[value] = ReadUInt32(data, gmshOffset + (int)offset + index * BufferDescriptorSize + value * 4);
            }

            descriptors[index] = new GmshBufferDescriptor(values);
        }

        return (descriptors, null);
    }

    private static int FindGmshOffset(byte[] data)
    {
        if (data.Length >= 4 && ReadUInt32(data, 0) == GmshMagic)
        {
            return 0;
        }

        if (data.Length >= Gar5WrapperSize + 4 &&
            ReadUInt32(data, 0) == Gar5Header.Magic &&
            ReadUInt32(data, Gar5WrapperSize) == GmshMagic)
        {
            return Gar5WrapperSize;
        }

        throw new GmshFormatException("The data does not start with a GMSH file or a GAR5 mesh wrapper.");
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    private static float ReadSingle(byte[] data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));

    private static float ReadHalf(byte[] data, int offset) =>
        (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)));
}
