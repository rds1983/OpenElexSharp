using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>Summary shown by the scene row of the debug overlay.</summary>
/// <param name="Name">Scene name.</param>
/// <param name="Instances">Scene-node mesh instances.</param>
/// <param name="Chunks">Static batches submitted to the renderer.</param>
/// <param name="Parts">Mesh parts across all batches.</param>
/// <param name="Details">Optional extra scene information, such as visual category toggles.</param>
public readonly record struct SceneStatus(
	string Name,
	int Instances,
	int Chunks,
	int Parts,
	string? Details = null);

/// <summary>
/// A deterministic large test scene: a three-dimensional grid of one mesh, grouped under row
/// nodes so the transform hierarchy is exercised. Instances are merged into static spatial chunks
/// (one vertex buffer and material-grouped index ranges per chunk) to keep draw calls low, while
/// chunks still let the renderer frustum-cull large parts of the grid.
/// </summary>
public sealed class StressScene : IDisposable
{
	/// <summary>Default mesh used when no <c>--mesh</c> query is supplied.</summary>
	public const string DefaultMeshName = "Obj_Bld_Alb_Door_Closed_L1";

	private const int Columns = 12;
	private const int Rows = 6;
	private const int Layers = 12;
	private const int ChunkColumns = 4;
	private const int ChunkRows = 3;
	private const int ChunkLayers = 4;

	private readonly MaterialLibrary _materials;
	private readonly List<DrMesh> _chunks = [];
	private int _batchPartCount;
	private bool _disposed;

	/// <summary>Builds the grid from a mounted mesh resource.</summary>
	/// <param name="device">Graphics device used for buffers and textures.</param>
	/// <param name="assets">Mounted assets.</param>
	/// <param name="resources">Resolved resource index.</param>
	/// <param name="meshQuery">Mesh name or substring; defaults to <see cref="DefaultMeshName"/>.</param>
	/// <exception cref="AssetNotFoundException">No mesh matches the query.</exception>
	public StressScene(
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		string? meshQuery)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(resources);

		var query = string.IsNullOrWhiteSpace(meshQuery) ? DefaultMeshName : meshQuery.Trim();
		var resource = FindMesh(resources, query);
		using var classBlocks = new ResourceClassBlockReader(assets);
		MeshMetadata? metadata = null;
		try
		{
			metadata = MeshMetadata.Parse(classBlocks.Read(resource));
		}
		catch (Exception exception) when (exception is Gar5FormatException or GenomeFormatException)
		{
			Log.Warning($"Stress mesh metadata '{resource.Name}': {exception.Message}");
		}

		using var stream = assets.OpenRead(resource.VirtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		var source = GmshMesh.Parse(resource.Name, resource.VirtualPath, buffer.ToArray());
		var ranges = MeshLoader.ResolveSubmeshes(source, metadata);
		_materials = new MaterialLibrary(device, assets, resources);
		MeshName = resource.Name;

		var bounds = SourceBounds(source);
		var size = bounds.Max - bounds.Min;
		var stepX = MathF.Max(size.X, 400f) * 1.6f;
		var stepY = MathF.Max(size.Y, 400f) * 2.2f;
		var stepZ = MathF.Max(size.Z, 400f) * 1.6f;
		var totalX = (Columns - 1) * stepX;
		var totalY = (Rows - 1) * stepY;
		var totalZ = (Layers - 1) * stepZ;

		Root = new SceneNode("stress");
		var instances = new List<(int Column, int Row, int Layer, SceneNode Node)>();
		for (var row = 0; row < Rows; row++)
		{
			var group = new SceneNode($"row-{row}")
			{
				LocalTransform = Matrix.CreateTranslation(-totalX * 0.5f, row * stepY, -totalZ * 0.5f),
			};
			Root.AddChild(group);
			for (var column = 0; column < Columns; column++)
			{
				for (var layer = 0; layer < Layers; layer++)
				{
					var node = new SceneNode($"{resource.Name}[{column},{row},{layer}]")
					{
						LocalTransform = Matrix.CreateTranslation(column * stepX, 0f, layer * stepZ),
					};
					group.AddChild(node);
					instances.Add((column, row, layer, node));
				}
			}
		}

		Root.UpdateWorldTransform();
		foreach (var chunk in instances.GroupBy(instance => (
			Column: instance.Column / ChunkColumns,
			Row: instance.Row / ChunkRows,
			Layer: instance.Layer / ChunkLayers)))
		{
			var transforms = chunk.Select(instance => instance.Node.WorldTransform).ToArray();
			_chunks.Add(BuildChunk(
				device,
				_materials,
				source,
				ranges,
				transforms,
				$"{resource.Name}[{chunk.Key.Column},{chunk.Key.Row},{chunk.Key.Layer}]"));
		}

		InstanceCount = instances.Count;
		PartCount = instances.Count * ranges.Count;
		_batchPartCount = _chunks.Sum(mesh => mesh.MeshParts.Count);

		var center = new Vector3(0f, totalY * 0.5f, 0f);
		Camera = new FlyCamera
		{
			Position = center,
			Yaw = MathHelper.PiOver4,
			Pitch = -0.05f,
		};
	}

	/// <summary>Hierarchy root.</summary>
	public SceneNode Root { get; }

	/// <summary>Mesh resource used by every instance.</summary>
	public string MeshName { get; }

	/// <summary>Number of logical mesh instances.</summary>
	public int InstanceCount { get; }

	/// <summary>Logical mesh parts before batching.</summary>
	public int PartCount { get; }

	/// <summary>Number of static batch meshes.</summary>
	public int ChunkCount => _chunks.Count;

	/// <summary>Number of submitted batch parts.</summary>
	public int BatchPartCount => _batchPartCount;

	/// <summary>Free-fly camera starting inside the grid.</summary>
	public FlyCamera Camera { get; }

	/// <summary>Overlay status.</summary>
	public SceneStatus Status => new("Stress", InstanceCount, ChunkCount, BatchPartCount);

	/// <summary>Submits every static batch to the renderer.</summary>
	public void Submit(Renderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		foreach (var chunk in _chunks)
		{
			renderer.SubmitMesh(chunk, Matrix.Identity);
		}
	}

	/// <summary>Applies fly-camera movement and arrow-key look for one frame.</summary>
	public void UpdateCamera(KeyboardState keyboard, float seconds)
	{
		Camera.UpdateLook(keyboard, seconds);
		Camera.UpdateMovement(keyboard, seconds);
	}

	private static DrMesh BuildChunk(
		GraphicsDevice device,
		MaterialLibrary materials,
		GmshMesh source,
		IReadOnlyList<MeshSubmeshRange> ranges,
		IReadOnlyList<Matrix> transforms,
		string name)
	{
		var hasColors = source.Colors.Count == source.Positions.Count;
		var hasTextureCoordinates = source.TextureCoordinates.Count == source.Positions.Count;
		var hasNormals = source.Normals.Count == source.Positions.Count;
		var vertices = new List<VertexPositionColorNormalTexture>(
			transforms.Count * source.Positions.Count);
		var materialIndices = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);

		foreach (var world in transforms)
		{
			var baseVertex = vertices.Count;
			for (var index = 0; index < source.Positions.Count; index++)
			{
				var position = Vector3.Transform(ToXna(source.Positions[index]), world);
				var normal = hasNormals
					? Vector3.TransformNormal(ToXna(source.Normals[index]), world)
					: Vector3.UnitY;
				normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
				var textureCoordinate = hasTextureCoordinates
					? ToXna(source.TextureCoordinates[index])
					: Vector2.Zero;
				var color = hasColors
					? new Color(
						source.Colors[index].X,
						source.Colors[index].Y,
						source.Colors[index].Z,
						source.Colors[index].W)
					: Color.White;
				vertices.Add(new VertexPositionColorNormalTexture(
					position,
					color,
					normal,
					textureCoordinate));
				min = Vector3.Min(min, position);
				max = Vector3.Max(max, position);
			}

			foreach (var range in ranges)
			{
				var key = range.MaterialName ?? string.Empty;
				if (!materialIndices.TryGetValue(key, out var indices))
				{
					indices = [];
					materialIndices[key] = indices;
				}

				for (var index = 0; index < range.IndexCount; index++)
				{
					indices.Add(source.Indices[range.StartIndex + index] + baseVertex);
				}
			}
		}

		var bounds = new BoundingBox(min, max);
		var sphere = BoundingSphere.CreateFromBoundingBox(bounds);
		var vertexBuffer = new VertexBuffer(
			device,
			VertexPositionColorNormalTexture.VertexDeclaration,
			vertices.Count,
			BufferUsage.WriteOnly);
		vertexBuffer.SetData([.. vertices]);

		var totalIndices = materialIndices.Values.Sum(indices => indices.Count);
		var indexBuffer = new IndexBuffer(
			device,
			IndexElementSize.ThirtyTwoBits,
			totalIndices,
			BufferUsage.WriteOnly);
		var allIndices = new int[totalIndices];
		var cursor = 0;
		foreach (var indices in materialIndices.Values)
		{
			indices.CopyTo(allIndices, cursor);
			cursor += indices.Count;
		}

		indexBuffer.SetData(allIndices);

		var mesh = new DrMesh { Name = name };
		cursor = 0;
		foreach (var pair in materialIndices)
		{
			var material = string.IsNullOrEmpty(pair.Key) ? null : materials.Load(pair.Key);
			var part = new DrMeshPart(
				vertexBuffer,
				indexBuffer,
				bounds,
				PrimitiveType.TriangleList,
				vertices.Count,
				pair.Value.Count / 3,
				0,
				cursor)
			{
				Tag = new DrMeshPartTag(sphere, material, cursor, pair.Value.Count),
			};
			mesh.MeshParts.Add(part);
			cursor += pair.Value.Count;
		}

		return mesh;
	}

	private static BoundingBox SourceBounds(GmshMesh source)
	{
		var min = ToXna(source.Positions[0]);
		var max = min;
		foreach (var position in source.Positions)
		{
			min = Vector3.Min(min, ToXna(position));
			max = Vector3.Max(max, ToXna(position));
		}

		return new BoundingBox(min, max);
	}

	private static ElexResource FindMesh(ElexResourceIndex resources, string query)
	{
		var exact = resources.Resources.FirstOrDefault(resource =>
			string.Equals(resource.TypeToken, "msh", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(resource.Name, query, StringComparison.OrdinalIgnoreCase));
		if (exact is not null)
		{
			return exact;
		}

		return resources.Resources
			.Where(resource =>
				string.Equals(resource.TypeToken, "msh", StringComparison.OrdinalIgnoreCase) &&
				resource.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault()
			?? throw new AssetNotFoundException($"No stress mesh resource matches '{query}'.");
	}

	private static Vector3 ToXna(NumericsVector3 value) => new(value.X, value.Y, value.Z);

	private static Vector2 ToXna(System.Numerics.Vector2 value) => new(value.X, value.Y);

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
