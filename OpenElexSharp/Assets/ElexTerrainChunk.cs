using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>
/// One terrain heightmap tile from a <c>staticscene</c> blob. The blob wraps a <c>GESC</c>
/// section; its tile header gives the world-space origin and scale, followed by a
/// <see cref="Resolution"/>² grid of little-endian height samples.
/// </summary>
public sealed class ElexTerrainChunk
{
	/// <summary>Height samples per tile edge.</summary>
	public const int Resolution = 137;

	/// <summary>Expected tile footprint in world units.</summary>
	public const float TileSize = 3200f;

	private const int Gar5WrapperSize = 24;
	private const int GescHeaderOffset = 24;
	private const int TileHeaderSize = 32;
	private const int HeightCount = Resolution * Resolution;

	private readonly ushort[] _heights;

	private ElexTerrainChunk(
		string name,
		float minX,
		float minY,
		float minZ,
		float sizeX,
		float sizeZ,
		float heightScale,
		ushort[] heights)
	{
		Name = name;
		MinX = minX;
		MinY = minY;
		MinZ = minZ;
		SizeX = sizeX;
		SizeZ = sizeZ;
		HeightScale = heightScale;
		_heights = heights;
	}

	/// <summary>Logical source name (the blob path).</summary>
	public string Name { get; }

	/// <summary>World-space minimum X of the tile.</summary>
	public float MinX { get; }

	/// <summary>World-space base Y added to scaled height samples.</summary>
	public float MinY { get; }

	/// <summary>World-space minimum Z of the tile.</summary>
	public float MinZ { get; }

	/// <summary>Tile extent along X.</summary>
	public float SizeX { get; }

	/// <summary>Tile extent along Z.</summary>
	public float SizeZ { get; }

	/// <summary>World units per height sample.</summary>
	public float HeightScale { get; }

	/// <summary>
	/// Logical row-major height samples (row = Z, column = X). The blob stores them column-major;
	/// the parser transposes once on load.
	/// </summary>
	public IReadOnlyList<ushort> Heights => _heights;

	/// <summary>Mutable height storage used by the runtime to stitch shared tile edges.</summary>
	internal ushort[] HeightData => _heights;

	/// <summary>World-space X of a sample column.</summary>
	public float SampleX(int column) => MinX + column * (SizeX / (Resolution - 1));

	/// <summary>World-space Z of a sample row.</summary>
	public float SampleZ(int row) => MinZ + row * (SizeZ / (Resolution - 1));

	/// <summary>World-space Y of a sample.</summary>
	public float SampleY(int column, int row) =>
		MinY + Heights[row * Resolution + column] * HeightScale;

	/// <summary>
	/// Parses a terrain blob. The parser locates the tile header by its invariant footprint and
	/// scale rather than trusting the variable-length section preambles.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The blob has no valid GESC terrain tile.</exception>
	public static ElexTerrainChunk Parse(string name, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(data);
		if (data.Length < Gar5WrapperSize + 8 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new GenomeFormatException($"Terrain '{name}' has no GAR5 wrapper.");
		}

		if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(GescHeaderOffset)) != 0x43534547)
		{
			throw new GenomeFormatException($"Terrain '{name}' has no GESC section.");
		}

		for (var offset = GescHeaderOffset + 8; offset + TileHeaderSize + HeightCount * 2 <= data.Length; offset += 4)
		{
			var minZ = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
			var minY = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4));
			var minX = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8));
			var sizeX = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 12));
			var heightScale = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 16));
			var sizeZ = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 20));
			var reserved0 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 24));
			var reserved1 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 28));
			if (reserved0 != 0 || reserved1 != 0 ||
				!IsFinite(minX) || !IsFinite(minZ) || !IsFinite(minY) ||
				MathF.Abs(sizeX - TileSize) > 0.5f ||
				MathF.Abs(sizeZ - TileSize) > 0.5f ||
				MathF.Abs(heightScale - 3.125f) > 0.05f)
			{
				continue;
			}

			var heights = new ushort[HeightCount];
			for (var index = 0; index < heights.Length; index++)
			{
				heights[index] = BinaryPrimitives.ReadUInt16LittleEndian(
					data.AsSpan(offset + TileHeaderSize + index * 2));
			}

			// The blob stores samples column-major (X outer, Z inner). Transpose once so the rest of
			// the runtime can use the natural row-major X/Z indexing.
			var transposed = new ushort[HeightCount];
			for (var row = 0; row < Resolution; row++)
			{
				for (var column = 0; column < Resolution; column++)
				{
					transposed[row * Resolution + column] = heights[column * Resolution + row];
				}
			}

			return new ElexTerrainChunk(
				name,
				minX,
				minY,
				minZ,
				sizeX,
				sizeZ,
				heightScale,
				transposed);
		}

		throw new GenomeFormatException($"Terrain '{name}' has no recognized heightmap tile.");
	}

	private static bool IsFinite(float value) => float.IsFinite(value);
}
