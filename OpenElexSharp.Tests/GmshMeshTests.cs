using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GmshMeshTests
{
    [Fact]
    public void Parse_SyntheticMesh_ReadsGeometryAndDeclarations()
    {
        var data = TestGmshBuilder.Create(
            [
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
            ],
            [0, 1, 2],
            [
                new(0, 0, 7, 0),
                new(1, 0, 15, 1),
            ],
            [
                new(0, 1, 0, 12),
                new(1, 1, 36, 12),
            ],
            [
                [1, 0, 2, 2, 40, 1, 0x81d1b3f7, 1, 64, 72],
                [2, 0, 1, 0, 0, 1, 0x81d1b3f7, 1, 144, 72],
            ]);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.msh", data);

        Assert.Equal(2u, mesh.Version);
        Assert.Equal(3, mesh.Positions.Count);
        Assert.Equal(3, mesh.Indices.Count);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.True(mesh.IsTriangleList);
        Assert.Equal(2, mesh.VertexAttributes.Count);
        Assert.Equal("position", mesh.VertexAttributes[0].UsageName);
        Assert.Equal("normal", mesh.VertexAttributes[1].UsageName);
        Assert.Equal(2, mesh.VertexStreams.Count);
        Assert.Equal(2, mesh.MaterialReferences.Count);
        Assert.Equal(0x81d1b3f7u, mesh.MaterialReferences[0].Hash);
        Assert.Equal(new Vector3(0, 1, 0), mesh.Positions[2]);
    }

    [Fact]
    public void Parse_DirectGmshWithoutWrapper_IsAccepted()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            wrap: false);

        var mesh = GmshMesh.Parse("TestMesh", "data/test.gmsh", data);

        Assert.Equal(0, mesh.GmshOffset);
        Assert.Equal(3, mesh.Positions.Count);
    }

    [Fact]
    public void Parse_TruncatedHeader_Throws()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            wrap: false);

        Assert.Throws<GmshFormatException>(() => GmshMesh.Parse("TestMesh", "data/test.gmsh", data[..40]));
    }

    [Fact]
    public void Parse_UnsupportedVersion_Throws()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            wrap: false);
        data[4] = 1;

        var exception = Assert.Throws<GmshFormatException>(
            () => GmshMesh.Parse("TestMesh", "data/test.gmsh", data));

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IndexOutOfRange_Throws()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0)],
            [0, 1, 2],
            wrap: false);

        Assert.Throws<GmshFormatException>(() => GmshMesh.Parse("TestMesh", "data/test.gmsh", data));
    }

    [Fact]
    public void Parse_TruncatedPositions_Throws()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            wrap: false);
        data = data[..^8];

        Assert.Throws<GmshFormatException>(() => GmshMesh.Parse("TestMesh", "data/test.gmsh", data));
    }

    [Fact]
    public void Parse_NonFinitePosition_Throws()
    {
        var data = TestGmshBuilder.Create(
            [new Vector3(float.NaN, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            wrap: false);

        Assert.Throws<GmshFormatException>(() => GmshMesh.Parse("TestMesh", "data/test.gmsh", data));
    }

    [Fact]
    public void Parse_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => GmshMesh.Parse("TestMesh", "data/test.gmsh", null!));
        Assert.Throws<ArgumentNullException>(() => GmshMesh.Parse("TestMesh", null!, []));
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
