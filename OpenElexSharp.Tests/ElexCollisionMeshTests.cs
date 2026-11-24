using System.Buffers.Binary;
using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexCollisionMeshTests
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

		var mesh = ElexCollisionMesh.Parse("Test", "test.rom", data);

		Assert.Equal(4, mesh.Vertices.Count);
		Assert.Equal(2, mesh.TriangleCount);
		Assert.Equal([0, 1, 2, 2, 3, 0], mesh.Indices);
		Assert.Equal(Vector3.Zero, mesh.BoundsMin);
		Assert.Equal(new Vector3(1, 1, 0), mesh.BoundsMax);
	}

	[Fact]
	public void Parse_ReadsWideIndices()
	{
		var vertices = new Vector3[65536];
		for (var index = 0; index < vertices.Length; index++)
		{
			vertices[index] = new Vector3(index, index + 1, 0);
		}

		var data = BuildMesh(vertices, [0, 1, 65535], wideIndices: true);

		var mesh = ElexCollisionMesh.Parse("Wide", "test.rom", data);

		Assert.Equal(65536, mesh.Vertices.Count);
		Assert.Equal(1, mesh.TriangleCount);
		Assert.Equal([0, 1, 65535], mesh.Indices);
	}

	[Fact]
	public void Parse_RejectsBadMagic()
	{
		var data = BuildMesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		data[44] = (byte)'X';

		Assert.Throws<GenomeFormatException>(() =>
			ElexCollisionMesh.Parse("Bad", "test.rom", data));
	}

	[Fact]
	public void Parse_RejectsTruncatedPayload()
	{
		var data = BuildMesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		data = data[..^0x50];

		Assert.Throws<GenomeFormatException>(() =>
			ElexCollisionMesh.Parse("Bad", "test.rom", data));
	}

	[Fact]
	public void Parse_RejectsOutOfRangeCounts()
	{
		var data = BuildMesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44 + 0xA0), 0);

		Assert.Throws<GenomeFormatException>(() =>
			ElexCollisionMesh.Parse("Bad", "test.rom", data));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => ElexCollisionMesh.Parse("", "test.rom", []));
		Assert.Throws<ArgumentException>(() => ElexCollisionMesh.Parse("Test", "", []));
		Assert.Throws<ArgumentNullException>(() => ElexCollisionMesh.Parse("Test", "test.rom", null!));
	}

	private static byte[] BuildMesh(
		Vector3[] vertices,
		int[] indices,
		bool wideIndices = false)
	{
		var indexStride = wideIndices ? 4 : 2;
		var vertexOffset = 0x200;
		var payload = new byte[vertexOffset + vertices.Length * 12 + indices.Length * indexStride + 0x40];
		"SEBD"u8.CopyTo(payload);
		BinaryPrimitives.WriteUInt32LittleEndian(
			payload.AsSpan(0xA0), (uint)vertices.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(
			payload.AsSpan(0xA4), (uint)(indices.Length / 3));
		for (var index = 0; index < vertices.Length; index++)
		{
			var offset = vertexOffset + index * 12;
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset), vertices[index].X);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 4), vertices[index].Y);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 8), vertices[index].Z);
		}

		var indexOffset = vertexOffset + vertices.Length * 12;
		for (var index = 0; index < indices.Length; index++)
		{
			if (wideIndices)
			{
				BinaryPrimitives.WriteUInt32LittleEndian(
					payload.AsSpan(indexOffset + index * 4), (uint)indices[index]);
			}
			else
			{
				BinaryPrimitives.WriteUInt16LittleEndian(
					payload.AsSpan(indexOffset + index * 2), (ushort)indices[index]);
			}
		}

		return Gec2TestBuilder.WrapRaw(payload);
	}
}
