using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class TerrainSceneTests
{
	[Theory]
	[InlineData(-409600f, 0)]
	[InlineData(-406400f, 1)]
	[InlineData(-406399f, 1)]
	[InlineData(41600f, 141)]
	[InlineData(-1f, 127)]
	[InlineData(0f, 128)]
	[InlineData(409600f, 256)]
	public void ChunkIndex_MapsWorldCoordinates(float coordinate, int expected)
	{
		Assert.Equal(expected, TerrainScene.ChunkIndex(coordinate));
	}

	[Fact]
	public void StitchHeights_AveragesSharedEdgeSamples()
	{
		var west = BuildChunk(0, 0, (column, _) => column == 136 ? (ushort)200 : (ushort)100);
		var east = BuildChunk(1, 0, (column, _) => column == 0 ? (ushort)300 : (ushort)100);
		var byGrid = new Dictionary<(int X, int Z), ElexTerrainChunk>
		{
			[(0, 0)] = west,
			[(1, 0)] = east,
		};

		TerrainScene.StitchHeights(byGrid);

		Assert.Equal(250, west.Heights[10 * 137 + 136]);
		Assert.Equal(250, east.Heights[10 * 137]);
		Assert.Equal(245, west.Heights[68 * 137 + 135]);
		Assert.Equal(245, east.Heights[68 * 137 + 1]);
		Assert.Equal(100, west.Heights[68 * 137 + 68]);
		Assert.Equal(100, east.Heights[68 * 137 + 68]);
	}

	[Fact]
	public void StitchHeights_AveragesFourTileCorner()
	{
		var northWest = BuildChunk(0, 0, (_, row) => row == 136 ? (ushort)100 : (ushort)0);
		var northEast = BuildChunk(1, 0, (_, row) => row == 136 ? (ushort)200 : (ushort)0);
		var southWest = BuildChunk(0, 1, (_, row) => row == 0 ? (ushort)300 : (ushort)0);
		var southEast = BuildChunk(1, 1, (_, row) => row == 0 ? (ushort)400 : (ushort)0);
		var byGrid = new Dictionary<(int X, int Z), ElexTerrainChunk>
		{
			[(0, 0)] = northWest,
			[(1, 0)] = northEast,
			[(0, 1)] = southWest,
			[(1, 1)] = southEast,
		};

		TerrainScene.StitchHeights(byGrid);

		Assert.Equal(250, northWest.Heights[136 * 137 + 136]);
		Assert.Equal(250, northEast.Heights[136 * 137]);
		Assert.Equal(250, southWest.Heights[136]);
		Assert.Equal(250, southEast.Heights[0]);
	}

	[Theory]
	[InlineData(0, 0, "data/compiled/staticscene/World/World_00_00_00/World_00_000_000.blob")]
	[InlineData(141, 58, "data/compiled/staticscene/World/World_00_08_03/World_00_141_058.blob")]
	[InlineData(144, 66, "data/compiled/staticscene/World/World_00_09_04/World_00_144_066.blob")]
	[InlineData(255, 255, "data/compiled/staticscene/World/World_00_15_15/World_00_255_255.blob")]
	public void ChunkPath_FormatsBlockAndFileNames(int chunkX, int chunkZ, string expected)
	{
		Assert.Equal(expected, TerrainScene.ChunkPath(chunkX, chunkZ));
	}

	private static ElexTerrainChunk BuildChunk(
		int chunkX,
		int chunkZ,
		Func<int, int, ushort> height)
	{
		const int headerOffset = 32;
		const int resolution = ElexTerrainChunk.Resolution;
		var data = new byte[headerOffset + 32 + resolution * resolution * 2];
		Encoding.ASCII.GetBytes("GAR5").CopyTo(data, 0);
		Encoding.ASCII.GetBytes("GESC").CopyTo(data, 24);
		BinaryPrimitives.WriteSingleLittleEndian(
			data.AsSpan(headerOffset),
			TerrainScene.BaseCoordinate + chunkZ * TerrainScene.TileSize);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 4), 0);
		BinaryPrimitives.WriteSingleLittleEndian(
			data.AsSpan(headerOffset + 8),
			TerrainScene.BaseCoordinate + chunkX * TerrainScene.TileSize);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 12), TerrainScene.TileSize);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 16), 3.125f);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(headerOffset + 20), TerrainScene.TileSize);
		var heights = data.AsSpan(headerOffset + 32);
		for (var row = 0; row < resolution; row++)
		{
			for (var column = 0; column < resolution; column++)
			{
				// The blob is column-major; the parser transposes it to logical row-major.
				var index = column * resolution + row;
				BinaryPrimitives.WriteUInt16LittleEndian(heights[(index * 2)..], height(column, row));
			}
		}

		return ElexTerrainChunk.Parse(
			$"World_00_{chunkX:000}_{chunkZ:000}.blob",
			data);
	}
}

