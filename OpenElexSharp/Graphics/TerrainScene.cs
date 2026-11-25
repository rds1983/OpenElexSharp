using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Loads and renders a small block of terrain tiles around a world position. Each tile is one
/// <see cref="ElexTerrainChunk"/> heightmap; every tile gets its own GPU mesh and shares one
/// material/texture, which is deliberately simple until terrain splatting is decoded.
/// </summary>
public sealed class TerrainScene : IDisposable
{
	/// <summary>World coordinate of chunk index zero on both axes.</summary>
	public const float BaseCoordinate = -409600f;

	/// <summary>Terrain tile footprint in world units.</summary>
	public const float TileSize = ElexTerrainChunk.TileSize;

	/// <summary>Default number of tiles loaded on each side of the center tile.</summary>
	public const int DefaultRadius = 2;

	/// <summary>Material used for all terrain tiles.</summary>
	public const string DefaultMaterial = "TR_Edan_Grass_01";

	private const float OuterSkirtDepth = 200f;

	private readonly record struct SkirtDepths(float North, float South, float West, float East);

	private readonly MaterialLibrary _materials;
	private readonly List<DrMesh> _chunks = [];
	private readonly BoundingBox _bounds;
	private bool _disposed;

	/// <summary>Loads terrain tiles around <paramref name="center"/>.</summary>
	/// <param name="device">Graphics device.</param>
	/// <param name="assets">Mounted assets.</param>
	/// <param name="resources">Resolved resource index.</param>
	/// <param name="center">World-space center of the tile block.</param>
	/// <param name="radius">Tiles to load on each side; clamped to 0–4.</param>
	public TerrainScene(
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		Vector3 center,
		int radius = DefaultRadius,
		float? targetY = null)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(resources);

		radius = Math.Clamp(radius, 0, 4);
		Radius = radius;
		_materials = new MaterialLibrary(device, assets, resources);
		var material = _materials.Load(DefaultMaterial);
		if (material is null)
		{
			Log.Warning($"Terrain material '{DefaultMaterial}' is unavailable; using the fallback texture.");
		}

		var centerX = ChunkIndex(center.X);
		var centerZ = ChunkIndex(center.Z);
		var parsed = new List<(ElexTerrainChunk Chunk, string Path)>();
		for (var x = centerX - radius; x <= centerX + radius; x++)
		{
			for (var z = centerZ - radius; z <= centerZ + radius; z++)
			{
				var path = ChunkPath(x, z);
				if (!assets.Exists(path))
				{
					continue;
				}

				try
				{
					parsed.Add((ElexTerrainChunk.Parse(path, ReadAll(assets, path)), path));
				}
				catch (Exception exception) when (
					exception is GenomeFormatException or IOException or UnauthorizedAccessException)
				{
					Log.Warning($"Terrain chunk '{path}': {exception.Message}");
				}
			}
		}

		var byGrid = new Dictionary<(int X, int Z), ElexTerrainChunk>();
		foreach (var (chunk, _) in parsed)
		{
			byGrid[(ChunkIndex(chunk.MinX), ChunkIndex(chunk.MinZ))] = chunk;
		}

		StitchHeights(byGrid);

		var averageHeight = parsed.Count == 0
			? 0f
			: parsed.Average(item => AverageHeight(item.Chunk));
		VerticalOffset = targetY is { } target && float.IsFinite(target)
			? target - averageHeight
			: 0f;

		BoundingBox? bounds = null;
		foreach (var (chunk, path) in parsed)
		{
			try
			{
				var gridX = ChunkIndex(chunk.MinX);
				var gridZ = ChunkIndex(chunk.MinZ);
				var skirts = ComputeSkirtDepths(byGrid, gridX, gridZ);
				var mesh = BuildMesh(
					device,
					chunk,
					material,
					VerticalOffset,
					skirts,
					byGrid,
					gridX,
					gridZ);
				_chunks.Add(mesh);
				var meshBounds = chunkBounds(mesh);
				bounds = bounds is null
					? meshBounds
					: BoundingBox.CreateMerged(bounds.Value, meshBounds);
			}
			catch (Exception exception) when (exception is InvalidOperationException)
			{
				Log.Warning($"Terrain chunk '{path}': {exception.Message}");
			}
		}

		_bounds = bounds ?? new BoundingBox(center - Vector3.One, center + Vector3.One);
		ChunkCount = _chunks.Count;
		if (ChunkCount == 0)
		{
			Log.Warning($"Terrain: no chunks found around {center.X:0},{center.Z:0}.");
		}

		static BoundingBox chunkBounds(DrMesh mesh)
		{
			var result = mesh.MeshParts[0].BoundingBox;
			for (var index = 1; index < mesh.MeshParts.Count; index++)
			{
				result = BoundingBox.CreateMerged(result, mesh.MeshParts[index].BoundingBox);
			}

			return result;
		}
	}

	/// <summary>Number of terrain chunks loaded.</summary>
	public int ChunkCount { get; }

	/// <summary>Tile radius used during loading.</summary>
	public int Radius { get; }

	/// <summary>
	/// Vertical shift applied to every tile so the block can be aligned with nearby entities.
	/// Zero when no target height was supplied.
	/// </summary>
	public float VerticalOffset { get; }

	/// <summary>Combined terrain bounds.</summary>
	public BoundingBox Bounds => _bounds;

	/// <summary>Material name used by every tile.</summary>
	public string MaterialName => DefaultMaterial;

	/// <summary>Submits every terrain chunk.</summary>
	public void Submit(Renderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		foreach (var chunk in _chunks)
		{
			renderer.SubmitMesh(chunk, Matrix.Identity);
		}
	}

	/// <summary>Chunk index containing a world coordinate.</summary>
	public static int ChunkIndex(float worldCoordinate) =>
		(int)MathF.Floor((worldCoordinate - BaseCoordinate) / TileSize);

	/// <summary>Virtual path of a terrain chunk blob.</summary>
	public static string ChunkPath(int chunkX, int chunkZ) =>
		$"data/compiled/staticscene/World/World_00_{chunkX / 16:00}_{chunkZ / 16:00}/" +
		$"World_00_{chunkX:000}_{chunkZ:000}.blob";

	/// <summary>
	/// Averages height samples that occupy the same world position on neighbouring tiles, writes the
	/// shared value back to both, then blends the correction into a band along each edge so the
	/// transition is not compressed into a single sample.
	/// </summary>
	internal static void StitchHeights(Dictionary<(int X, int Z), ElexTerrainChunk> byGrid)
	{
		const int resolution = ElexTerrainChunk.Resolution;
		const int stride = resolution - 1;
		var averages = new Dictionary<(int X, int Z), ushort>();
		var sums = new Dictionary<(int X, int Z), (long Sum, int Count)>();
		foreach (var pair in byGrid)
		{
			var gridX = pair.Key.X;
			var gridZ = pair.Key.Z;
			var heights = pair.Value.HeightData;
			for (var row = 0; row < resolution; row++)
			{
				for (var column = 0; column < resolution; column++)
				{
					if (column != 0 && column != resolution - 1 &&
						row != 0 && row != resolution - 1)
					{
						continue;
					}

					var key = (gridX * stride + column, gridZ * stride + row);
					var value = heights[row * resolution + column];
					if (sums.TryGetValue(key, out var existing))
					{
						sums[key] = (existing.Sum + value, existing.Count + 1);
					}
					else
					{
						sums[key] = (value, 1);
					}
				}
			}
		}

		foreach (var pair in sums)
		{
			averages[pair.Key] = (ushort)((pair.Value.Sum + pair.Value.Count / 2) / pair.Value.Count);
		}

		foreach (var pair in byGrid)
		{
			var gridX = pair.Key.X;
			var gridZ = pair.Key.Z;
			var heights = pair.Value.HeightData;
			for (var row = 0; row < resolution; row++)
			{
				for (var column = 0; column < resolution; column++)
				{
					if (column != 0 && column != resolution - 1 &&
						row != 0 && row != resolution - 1)
					{
						continue;
					}

					var key = (gridX * stride + column, gridZ * stride + row);
					heights[row * resolution + column] = averages[key];
				}
			}
		}

		BlendEdgeCorrections(byGrid, averages);
	}

	private static void BlendEdgeCorrections(
		Dictionary<(int X, int Z), ElexTerrainChunk> byGrid,
		Dictionary<(int X, int Z), ushort> averages)
	{
		const int resolution = ElexTerrainChunk.Resolution;
		const int stride = resolution - 1;
		const int blendSamples = 32;
		foreach (var pair in byGrid)
		{
			var gridX = pair.Key.X;
			var gridZ = pair.Key.Z;
			var heights = pair.Value.HeightData;
			for (var index = 0; index < resolution; index++)
			{
				BlendEdge(
					heights,
					averages[(gridX * stride + index, gridZ * stride)],
					index,
					distance => index + distance * resolution);
				BlendEdge(
					heights,
					averages[(gridX * stride + index, (gridZ + 1) * stride)],
					index,
					distance => (resolution - 1 - distance) * resolution + index);
				BlendEdge(
					heights,
					averages[(gridX * stride, gridZ * stride + index)],
					index,
					distance => index * resolution + distance);
				BlendEdge(
					heights,
					averages[((gridX + 1) * stride, gridZ * stride + index)],
					index,
					distance => index * resolution + resolution - 1 - distance);
			}
		}

		static void BlendEdge(
			ushort[] heights,
			ushort edgeValue,
			int tangentialIndex,
			Func<int, int> sampleIndex)
		{
			for (var distance = 1; distance <= blendSamples; distance++)
			{
				var weight = 1f - distance / (float)(blendSamples + 1);
				var index = sampleIndex(distance);
				heights[index] = (ushort)MathF.Round(
					MathHelper.Lerp(heights[index], edgeValue, weight));
			}
		}
	}

	private static DrMesh BuildMesh(
		GraphicsDevice device,
		ElexTerrainChunk chunk,
		ElexMaterial? material,
		float verticalOffset,
		SkirtDepths skirts,
		Dictionary<(int X, int Z), ElexTerrainChunk> byGrid,
		int gridX,
		int gridZ)
	{
		const int resolution = ElexTerrainChunk.Resolution;
		const int stride = resolution - 1;
		var vertices = new List<VertexPositionColorNormalTexture>(resolution * resolution);
		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		var stepX = chunk.SizeX / (resolution - 1);
		var stepZ = chunk.SizeZ / (resolution - 1);
		var globalX = gridX * stride;
		var globalZ = gridZ * stride;
		for (var row = 0; row < resolution; row++)
		{
			for (var column = 0; column < resolution; column++)
			{
				var centerY = chunk.SampleY(column, row);
				var position = new Vector3(
					chunk.SampleX(column),
					centerY + verticalOffset,
					chunk.SampleZ(row));
				var left = SampleGlobalY(byGrid, globalX + column - 1, globalZ + row, centerY);
				var right = SampleGlobalY(byGrid, globalX + column + 1, globalZ + row, centerY);
				var back = SampleGlobalY(byGrid, globalX + column, globalZ + row - 1, centerY);
				var front = SampleGlobalY(byGrid, globalX + column, globalZ + row + 1, centerY);
				var normal = Vector3.Normalize(new Vector3(
					-(right - left) / (2f * stepX),
					1f,
					-(front - back) / (2f * stepZ)));
				vertices.Add(new VertexPositionColorNormalTexture(
					position,
					Color.White,
					normal,
					new Vector2(column / (float)(resolution - 1) * 8f, row / (float)(resolution - 1) * 8f)));
				min = Vector3.Min(min, position);
				max = Vector3.Max(max, position);
			}
		}

		var indices = new List<ushort>((resolution - 1) * (resolution - 1) * 6);
		for (var row = 0; row < resolution - 1; row++)
		{
			for (var column = 0; column < resolution - 1; column++)
			{
				var topLeft = row * resolution + column;
				var topRight = topLeft + 1;
				var bottomLeft = topLeft + resolution;
				var bottomRight = bottomLeft + 1;
				indices.Add((ushort)topLeft);
				indices.Add((ushort)bottomLeft);
				indices.Add((ushort)topRight);
				indices.Add((ushort)topRight);
				indices.Add((ushort)bottomLeft);
				indices.Add((ushort)bottomRight);
			}
		}

		AddSkirt(vertices, indices, resolution, skirts, ref min, ref max);

		var vertexBuffer = new VertexBuffer(
			device,
			VertexPositionColorNormalTexture.VertexDeclaration,
			vertices.Count,
			BufferUsage.WriteOnly);
		vertexBuffer.SetData(vertices.ToArray());
		var indexBuffer = new IndexBuffer(
			device,
			IndexElementSize.SixteenBits,
			indices.Count,
			BufferUsage.WriteOnly);
		indexBuffer.SetData(indices.ToArray());

		var bounds = new BoundingBox(min, max);
		var sphere = BoundingSphere.CreateFromBoundingBox(bounds);
		var mesh = new DrMesh { Name = chunk.Name };
		mesh.MeshParts.Add(new DrMeshPart(
			vertexBuffer,
			indexBuffer,
			bounds,
			PrimitiveType.TriangleList,
			vertices.Count,
			indices.Count / 3,
			0,
			0)
		{
			Tag = new DrMeshPartTag(sphere, material, 0, indices.Count),
		});
		return mesh;
	}

	/// <summary>
	/// Adds a short skirt to edges that have no loaded neighbour, hiding the outer block boundary.
	/// Shared edges are stitched and do not need a skirt.
	/// </summary>
	private static void AddSkirt(
		List<VertexPositionColorNormalTexture> vertices,
		List<ushort> indices,
		int resolution,
		SkirtDepths skirts,
		ref Vector3 min,
		ref Vector3 max)
	{
		var southRow = (resolution - 1) * resolution;
		for (var column = 0; column < resolution - 1; column++)
		{
			AddSkirtQuad(
				vertices,
				indices,
				column,
				column + 1,
				new Vector3(0f, 0f, -1f),
				skirts.North,
				ref min,
				ref max);
			AddSkirtQuad(
				vertices,
				indices,
				southRow + column,
				southRow + column + 1,
				new Vector3(0f, 0f, 1f),
				skirts.South,
				ref min,
				ref max);
		}

		for (var row = 0; row < resolution - 1; row++)
		{
			AddSkirtQuad(
				vertices,
				indices,
				row * resolution,
				(row + 1) * resolution,
				new Vector3(-1f, 0f, 0f),
				skirts.West,
				ref min,
				ref max);
			AddSkirtQuad(
				vertices,
				indices,
				row * resolution + resolution - 1,
				(row + 1) * resolution + resolution - 1,
				new Vector3(1f, 0f, 0f),
				skirts.East,
				ref min,
				ref max);
		}
	}

	private static void AddSkirtQuad(
		List<VertexPositionColorNormalTexture> vertices,
		List<ushort> indices,
		int topAIndex,
		int topBIndex,
		Vector3 normal,
		float skirtDepth,
		ref Vector3 min,
		ref Vector3 max)
	{
		if (skirtDepth <= 0f)
		{
			return;
		}

		var topA = vertices[topAIndex];
		var topB = vertices[topBIndex];
		var bottomA = new VertexPositionColorNormalTexture(
			new Vector3(topA.Position.X, topA.Position.Y - skirtDepth, topA.Position.Z),
			Color.White,
			normal,
			topA.TextureCoordinate);
		var bottomB = new VertexPositionColorNormalTexture(
			new Vector3(topB.Position.X, topB.Position.Y - skirtDepth, topB.Position.Z),
			Color.White,
			normal,
			topB.TextureCoordinate);
		var bottomAIndex = (ushort)vertices.Count;
		vertices.Add(bottomA);
		var bottomBIndex = (ushort)vertices.Count;
		vertices.Add(bottomB);

		indices.Add((ushort)topAIndex);
		indices.Add(bottomAIndex);
		indices.Add((ushort)topBIndex);
		indices.Add((ushort)topBIndex);
		indices.Add(bottomAIndex);
		indices.Add(bottomBIndex);

		min = Vector3.Min(min, bottomA.Position);
		min = Vector3.Min(min, bottomB.Position);
	}

	private static SkirtDepths ComputeSkirtDepths(
		Dictionary<(int X, int Z), ElexTerrainChunk> byGrid,
		int gridX,
		int gridZ) =>
		new(
			byGrid.ContainsKey((gridX, gridZ - 1)) ? 0f : OuterSkirtDepth,
			byGrid.ContainsKey((gridX, gridZ + 1)) ? 0f : OuterSkirtDepth,
			byGrid.ContainsKey((gridX - 1, gridZ)) ? 0f : OuterSkirtDepth,
			byGrid.ContainsKey((gridX + 1, gridZ)) ? 0f : OuterSkirtDepth);

	private static float SampleGlobalY(
		Dictionary<(int X, int Z), ElexTerrainChunk> byGrid,
		int globalX,
		int globalZ,
		float fallback)
	{
		const int resolution = ElexTerrainChunk.Resolution;
		const int stride = resolution - 1;
		if (globalX < 0 || globalZ < 0)
		{
			return fallback;
		}

		var gridX = globalX / stride;
		var gridZ = globalZ / stride;
		var column = globalX - gridX * stride;
		var row = globalZ - gridZ * stride;
		if (column >= resolution || row >= resolution ||
			!byGrid.TryGetValue((gridX, gridZ), out var chunk))
		{
			return fallback;
		}

		return chunk.SampleY(column, row);
	}

	private static float AverageHeight(ElexTerrainChunk chunk)
	{
		var sum = 0f;
		for (var row = 0; row < ElexTerrainChunk.Resolution; row++)
		{
			for (var column = 0; column < ElexTerrainChunk.Resolution; column++)
			{
				sum += chunk.SampleY(column, row);
			}
		}

		return sum / (ElexTerrainChunk.Resolution * ElexTerrainChunk.Resolution);
	}

	private static byte[] ReadAll(AssetFileSystem assets, string virtualPath)
	{
		using var stream = assets.OpenRead(virtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (var chunk in _chunks)
		{
			chunk.Dispose();
		}

		_chunks.Clear();
		_materials.Dispose();
	}
}
