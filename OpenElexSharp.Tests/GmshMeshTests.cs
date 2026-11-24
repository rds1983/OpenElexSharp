using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GmshMeshTests
{
    private static readonly Vector3[] Triangle =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 1, 0),
    ];

    [Fact]
    public void Parse_SyntheticMesh_ReadsGeometryAndVertexStreams()
    {
        var data = TestGmshBuilder.Create(
            Triangle,
            [0, 1, 2],
            normals: [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            textureCoordinates: [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)],
            colors:
            [
                new Vector4(1f, 0.5f, 0.25f, 1f),
                new Vector4(1f, 0.5f, 0.25f, 1f),
                new Vector4(1f, 0.5f, 0.25f, 1f),
            ],
            halfPrecisionPositions: true);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.msh", data);

        Assert.Equal(2u, mesh.Version);
        Assert.Equal(3, mesh.Positions.Count);
        Assert.Equal(3, mesh.Indices.Count);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.True(mesh.IsTriangleList);
        Assert.Equal(4, mesh.VertexAttributes.Count);
        Assert.Equal(4, mesh.VertexStreams.Count);
        Assert.Equal(2, mesh.BufferDescriptors.Count);
        Assert.Equal(2u, mesh.BufferDescriptors[0].ElementSize);
        Assert.Equal(0x81d1b3f7u, mesh.BufferDescriptors[0].Hash);
        Assert.True(mesh.HasNormals);
        Assert.True(mesh.HasTextureCoordinates);
        Assert.Equal(new Vector3(0, 1, 0), mesh.SourcePositions[2]);
        Assert.Equal(1f, mesh.Positions[1].X, 3);
        Assert.Equal(0, mesh.Positions[1].Y, 3);
        Assert.InRange(Vector3.Distance(Vector3.UnitZ, mesh.Normals[0]), 0f, 0.01f);
        Assert.Equal(new Vector2(1, 0), mesh.TextureCoordinates[1]);
        Assert.Equal(1f, mesh.Colors[0].X, 3);
        Assert.Equal(0.502f, mesh.Colors[0].Y, 2);
        Assert.Equal(0.251f, mesh.Colors[0].Z, 2);
        Assert.Equal(1f, mesh.Colors[0].W, 3);
        Assert.Empty(mesh.Diagnostics);
    }

    [Fact]
    public void Parse_FloatPositions_AreDecodedExactly()
    {
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 2]);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.msh", data);

        Assert.Equal(Triangle, mesh.Positions);
        Assert.False(mesh.HasNormals);
        Assert.False(mesh.HasTextureCoordinates);
    }

    [Fact]
    public void Parse_32BitIndices_AreDecoded()
    {
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 2], use32BitIndices: true);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.msh", data);

        Assert.Equal([0, 1, 2], mesh.Indices);
        Assert.Equal(4u, mesh.BufferDescriptors[0].ElementSize);
        Assert.Equal(3 * 4, (int)mesh.BufferDescriptors[0].Size);
    }

    [Fact]
    public void Parse_TwoTextureCoordinateSets_AreDecoded()
    {
        var data = TestGmshBuilder.Create(
            Triangle,
            [0, 1, 2],
            textureCoordinates: [new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f), new Vector2(0.5f, 0.6f)],
            textureCoordinates1: [new Vector2(0.7f, 0.8f), new Vector2(0.9f, 1f), new Vector2(0.25f, 0.5f)]);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.msh", data);

        Assert.Equal(0.3f, mesh.TextureCoordinates[1].X, 2);
        Assert.Equal(0.4f, mesh.TextureCoordinates[1].Y, 2);
        Assert.Equal(0.9f, mesh.TextureCoordinates1[1].X, 2);
        Assert.Equal(1f, mesh.TextureCoordinates1[1].Y, 2);
        Assert.Equal(2, mesh.VertexAttributes.Count(attribute => attribute.UsageKind == GmshVertexUsage.TextureCoordinate));
        Assert.Equal(8u, mesh.VertexStreams.Single(stream => stream.UsageKind == GmshVertexUsage.TextureCoordinate).Stride);
    }

    [Fact]
    public void Parse_PackedNormals_DecodeAxisDirections()
    {
        var normals = new[]
        {
            new Vector3(0, 1, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 0, 1),
        };
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 2], normals: normals);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.msh", data);

        for (var index = 0; index < normals.Length; index++)
        {
            Assert.InRange(Vector3.Distance(normals[index], mesh.Normals[index]), 0f, 0.01f);
        }
    }

    [Fact]
    public void Parse_DirectGmshWithoutWrapper_IsAccepted()
    {
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 2], wrap: false);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.gmsh", data);

        Assert.Equal(0, mesh.GmshOffset);
        Assert.Equal(3, mesh.Positions.Count);
    }

    [Fact]
    public void Parse_TruncatedHeader_Throws()
    {
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 2], wrap: false);

        Assert.Throws<GmshFormatException>(() => GmshMesh.Parse("TestMesh", "data/test.gmsh", data[..40]));
    }

    [Fact]
    public void Parse_UnsupportedVersion_Throws()
    {
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 2], wrap: false);
        data[4] = 1;

        var exception = Assert.Throws<GmshFormatException>(
            () => GmshMesh.Parse("TestMesh", "data/test.gmsh", data));

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IndexOutOfRange_Throws()
    {
        var data = TestGmshBuilder.Create(Triangle, [0, 1, 7]);

        var exception = Assert.Throws<GmshFormatException>(
            () => GmshMesh.Parse("TestMesh", "data/test.msh", data));

        Assert.Contains("references vertex", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_TruncatedVertexStreams_Throws()
    {
        var data = TestGmshBuilder.Create(
            Triangle,
            [0, 1, 2],
            normals: [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);
        var gmshOffset = 44;
        var streamTableOffset = (int)BitConverter.ToUInt32(data, gmshOffset + 8 + 9 * 4);
        var strideOffset = gmshOffset + streamTableOffset + 12;
        BitConverter.GetBytes(0xFFFFu).CopyTo(data, strideOffset);

        var exception = Assert.Throws<GmshFormatException>(
            () => GmshMesh.Parse("TestMesh", "data/test.msh", data));

        Assert.Contains("vertex streams", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonFinitePosition_Throws()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(float.NaN, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2]);

        Assert.Throws<GmshFormatException>(() => GmshMesh.Parse("TestMesh", "data/test.msh", data));
    }

    [Fact]
    public void Parse_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => GmshMesh.Parse("TestMesh", "data/test.msh", null!));
        Assert.Throws<ArgumentNullException>(() => GmshMesh.Parse("TestMesh", null!, []));
    }

    [Theory]
    [InlineData(4u, 12)]
    [InlineData(7u, 8)]
    [InlineData(15u, 4)]
    [InlineData(23u, 4)]
    [InlineData(58u, 4)]
    [InlineData(999u, 0)]
    public void VertexFormat_ReturnsElementSize(uint format, int expected)
    {
        Assert.Equal(expected, GmshVertexFormat.GetSize(format));
    }

    [Theory]
    [InlineData(0, "position")]
    [InlineData(1, "normal")]
    [InlineData(2, "tangent")]
    [InlineData(3, "color")]
    [InlineData(4, "texcoord")]
    public void VertexUsage_ReturnsName(int usage, string expected)
    {
        var attribute = new GmshVertexAttribute(usage, 0, 4, 0, 0, 0, 0);

        Assert.Equal(expected, attribute.UsageName);
    }

    [Fact]
    public void ComputeNormals_ReturnsUnitNormals()
    {
        var positions = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
        };

        var normals = OpenElexSharp.Graphics.MeshLoader.ComputeNormals(
            positions,
            [0, 1, 2]);

        Assert.All(normals, normal => Assert.InRange(normal.Length(), 0.999f, 1.001f));
        Assert.Equal(0f, normals[0].X, 3);
        Assert.Equal(0f, normals[0].Y, 3);
        Assert.Equal(1f, normals[0].Z, 3);
    }
}
