using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MeshMetadataTests
{
    [Fact]
    public void Parse_ReadsSubMeshesAndLodRange()
    {
        var block = CreateMeshBlock();

        var metadata = MeshMetadata.Parse(block);

        Assert.Equal(MeshMetadata.MeshResourceClassHash, metadata.ClassHash);
        Assert.Equal(12, metadata.IndexCount);
        Assert.Equal(8, metadata.VertexCount);
        Assert.Equal(123.5f, metadata.LODRangeStart);
        Assert.Equal([-1f, -2f, -3f, 4f, 5f, 6f], metadata.Boundary);
        Assert.Equal([0f, 0f, 0f, 7f], metadata.BoundingSphere);
        var subMesh = Assert.Single(metadata.SubMeshes);
        Assert.Equal("AS_Test_Material", subMesh.MaterialName);
        Assert.Equal(6, subMesh.FirstIndex);
        Assert.Equal(12, subMesh.IndexCount);
        Assert.Equal(2, subMesh.FirstVertex);
        Assert.Equal(8, subMesh.VertexCount);
        Assert.Equal(1, subMesh.MaterialSwitchIndex);
        Assert.Equal([-1f, -2f, -3f, 4f, 5f, 6f], subMesh.Extends);
        Assert.Equal(["AS_Test_Material"], metadata.MaterialNames);
    }

    [Fact]
    public void Parse_WithoutSubMeshes_ReturnsEmptyList()
    {
        var block = TestGenomePropertyBuilder.Object(
            MeshMetadata.MeshResourceClassHash,
            1,
            ("IndexCount", TestGenomePropertyBuilder.UInt32(3)),
            ("VertexCount", TestGenomePropertyBuilder.UInt32(3)));

        var metadata = MeshMetadata.Parse(block);

        Assert.Empty(metadata.SubMeshes);
        Assert.Empty(metadata.MaterialNames);
        Assert.Equal(3, metadata.IndexCount);
    }

    [Fact]
    public void Parse_WrongClassHash_Throws()
    {
        var block = TestGenomePropertyBuilder.Object(0x12345678, 1);

        var exception = Assert.Throws<GenomeFormatException>(() => MeshMetadata.Parse(block));

        Assert.Contains("eCMeshResource2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TruncatedSubMeshArray_Throws()
    {
        var block = TestGenomePropertyBuilder.Object(
            MeshMetadata.MeshResourceClassHash,
            1,
            ("SubMeshes", TestGenomePropertyBuilder.UInt32(2)));

        Assert.Throws<GenomeFormatException>(() => MeshMetadata.Parse(block));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MeshMetadata.Parse(null!));
    }

    private static byte[] CreateMeshBlock()
    {
        var subMesh = TestGenomePropertyBuilder.Object(
            Djb2Hash.ComputeCaseSensitive("class eCSubMesh"),
            2,
            ("MaterialName", TestGenomePropertyBuilder.String("AS_Test_Material")),
            ("Extends", TestGenomePropertyBuilder.Floats(-1, -2, -3, 4, 5, 6)),
            ("FirstIndex", TestGenomePropertyBuilder.UInt32(6)),
            ("IndexCount", TestGenomePropertyBuilder.UInt32(12)),
            ("FirstVertex", TestGenomePropertyBuilder.UInt32(2)),
            ("VertexCount", TestGenomePropertyBuilder.UInt32(8)),
            ("FirstBone", TestGenomePropertyBuilder.UInt32(0)),
            ("BoneCount", TestGenomePropertyBuilder.UInt32(0)),
            ("MaterialSwitchIndex", TestGenomePropertyBuilder.Int32(1)),
            ("Flags", TestGenomePropertyBuilder.UInt32(0)));

        return TestGenomePropertyBuilder.Object(
            MeshMetadata.MeshResourceClassHash,
            1,
            ("SubMeshes", TestGenomePropertyBuilder.Array(subMesh)),
            ("IndexCount", TestGenomePropertyBuilder.UInt32(12)),
            ("VertexCount", TestGenomePropertyBuilder.UInt32(8)),
            ("Boundary", TestGenomePropertyBuilder.Floats(-1, -2, -3, 4, 5, 6)),
            ("BoundingSphere", TestGenomePropertyBuilder.Floats(0, 0, 0, 7)),
            ("LoDRangeStart", TestGenomePropertyBuilder.Single(123.5f)));
    }
}
