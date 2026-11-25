using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexTerrainChunkTests
{
	[Fact]
	public void Parse_ReadsTileHeaderAndHeights()
	{
		var data = BuildTerrain(minX: -100, minZ: 200, heightScale: 3.125f, height: 42);

		var chunk = ElexTerrainChunk.Parse("test.blob", data);

		Assert.Equal(-100, chunk.MinX);
		Assert.Equal(200, chunk.MinZ);
		Assert.Equal(0, chunk.MinY);
		Assert.Equal(3200, chunk.SizeX);
		Assert.Equal(3200, chunk.SizeZ);
		Assert.Equal(3.125f, chunk.HeightScale);
		Assert.Equal(ElexTerrainChunk.Resolution * ElexTerrainChunk.Resolution, chunk.Heights.Count);
		Assert.Equal(42, chunk.Heights[0]);
		Assert.Equal(131.25f, chunk.SampleY(0, 0), precision: 4);
	}

	[Fact]
	public void SampleCoordinates_CoverTile()
	{
		var chunk = ElexTerrainChunk.Parse(
			"test.blob",
			BuildTerrain(minX: -100, minZ: 200, heightScale: 3.125f, height: 42));

		Assert.Equal(-100, chunk.SampleX(0));
		Assert.Equal(200, chunk.SampleZ(0));
		Assert.Equal(3100, chunk.SampleX(ElexTerrainChunk.Resolution - 1), precision: 4);
		Assert.Equal(3400, chunk.SampleZ(ElexTerrainChunk.Resolution - 1), precision: 4);
	}

	[Fact]
	public void Parse_TransposesStoredColumnMajorSamples()
	{
		const int resolution = ElexTerrainChunk.Resolution;
		var data = BuildTerrain(minX: 0, minZ: 0, heightScale: 3.125f, height: 0);
		var raw = data.AsSpan(32 + 32);
		for (var row = 0; row < resolution; row++)
		{
			for (var column = 0; column < resolution; column++)
			{
				BinaryPrimitives.WriteUInt16LittleEndian(
					raw[((row * resolution + column) * 2)..],
					(ushort)(row * 100 + column));
			}
		}

		var chunk = ElexTerrainChunk.Parse("test.blob", data);

		Assert.Equal((ushort)(7 * 100 + 5), chunk.Heights[5 * resolution + 7]);
		Assert.Equal((ushort)(3 * 100 + 11), chunk.Heights[11 * resolution + 3]);
	}

	[Fact]
	public void Parse_FindsHeaderAfterPreamble()
	{
		var data = BuildTerrain(
			minX: 123, minZ: 456, heightScale: 3.125f, height: 7, headerOffset: 128);

		var chunk = ElexTerrainChunk.Parse("test.blob", data);

		Assert.Equal(123, chunk.MinX);
		Assert.Equal(456, chunk.MinZ);
		Assert.Equal(7, chunk.Heights[0]);
	}

	[Fact]
	public void Parse_RejectsMissingGar5()
	{
		Assert.Throws<GenomeFormatException>(
			() => ElexTerrainChunk.Parse("test.blob", new byte[1024]));
	}

	[Fact]
	public void Parse_RejectsMissingGesc()
	{
		var data = new byte[1024];
		Encoding.ASCII.GetBytes("GAR5").CopyTo(data, 0);

		Assert.Throws<GenomeFormatException>(
			() => ElexTerrainChunk.Parse("test.blob", data));
	}

	[Fact]
	public void Parse_RejectsMissingTileHeader()
	{
		var data = BuildTerrain(minX: 0, minZ: 0, heightScale: 3.125f, height: 1);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(44), 0);

		Assert.Throws<GenomeFormatException>(
			() => ElexTerrainChunk.Parse("test.blob", data));
	}

	[Fact]
	public void Parse_RejectsTruncatedHeights()
	{
		var data = BuildTerrain(minX: 0, minZ: 0, heightScale: 3.125f, height: 1);
		data = data[..^1024];

		Assert.Throws<GenomeFormatException>(
			() => ElexTerrainChunk.Parse("test.blob", data));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => ElexTerrainChunk.Parse(" ", []));
		Assert.Throws<ArgumentNullException>(() => ElexTerrainChunk.Parse("test", null!));
	}

	private static byte[] BuildTerrain(
		float minX,
		float minZ,
		float heightScale,
		ushort height,
		int headerOffset = 32)
	{
		var data = new byte[headerOffset + 32 + ElexTerrainChunk.Resolution * ElexTerrainChunk.Resolution * 2];
		Encoding.ASCII.GetBytes("GAR5").CopyTo(data, 0);
		Encoding.ASCII.GetBytes("GESC").CopyTo(data, 24);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset), minZ);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 4), 0);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 8), minX);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 12), 3200);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 16), heightScale);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 20), 3200);
		var heights = data.AsSpan(headerOffset + 32);
		for (var index = 0; index < ElexTerrainChunk.Resolution * ElexTerrainChunk.Resolution; index++)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(heights[(index * 2)..], height);
		}

		return data;
	}
}
