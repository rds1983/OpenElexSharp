using System.Buffers.Binary;
using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexOcclusionMeshTests
{
	[Fact]
	public void Parse_ReadsTriangleMesh()
	{
		var data = BuildMesh(
			[
				new Vector3(0, 0, 0),
				new Vector3(1, 0, 0),
				new Vector3(1, 1, 0),
				new Vector3(0, 1, 0),
			],
			[0, 1, 2, 2, 3, 0]);

		var mesh = ElexOcclusionMesh.Parse("Test", "test.rom", data);

		Assert.Equal(4, mesh.Vertices.Count);
		Assert.Equal(2, mesh.TriangleCount);
		Assert.Equal([0, 1, 2, 2, 3, 0], mesh.Indices);
		Assert.Equal(Vector3.Zero, mesh.BoundsMin);
		Assert.Equal(new Vector3(1, 1, 0), mesh.BoundsMax);
	}

	[Fact]
	public void Parse_RejectsBadHeader()
	{
		var data = BuildMesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44), 0x40);

		Assert.Throws<GenomeFormatException>(() =>
			ElexOcclusionMesh.Parse("Bad", "test.rom", data));
	}

	[Fact]
	public void Parse_RejectsOutOfRangeIndex()
	{
		var data = BuildMesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(44 + 48 + 3 * 16), 9);

		Assert.Throws<GenomeFormatException>(() =>
			ElexOcclusionMesh.Parse("Bad", "test.rom", data));
	}

	[Fact]
	public void Parse_RejectsTruncatedPayload()
	{
		var data = BuildMesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		data = data[..^40];

		Assert.Throws<GenomeFormatException>(() =>
			ElexOcclusionMesh.Parse("Bad", "test.rom", data));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => ElexOcclusionMesh.Parse("", "test.rom", []));
		Assert.Throws<ArgumentException>(() => ElexOcclusionMesh.Parse("Test", "", []));
		Assert.Throws<ArgumentNullException>(() => ElexOcclusionMesh.Parse("Test", "test.rom", null!));
	}

	private static byte[] BuildMesh(Vector3[] vertices, int[] indices)
	{
		var indexOffset = 48 + vertices.Length * 16;
		var payload = new byte[indexOffset + indices.Length * 2 + 36];
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 48);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)vertices.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), (uint)indexOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24), (uint)indices.Length);
		for (var index = 0; index < vertices.Length; index++)
		{
			var offset = 48 + index * 16;
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset), vertices[index].X);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 4), vertices[index].Y);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 8), vertices[index].Z);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 12), 1f);
		}

		for (var index = 0; index < indices.Length; index++)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(
				payload.AsSpan(indexOffset + index * 2), (ushort)indices[index]);
		}

		return Gec2TestBuilder.WrapRaw(payload);
	}
}
