using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Loads one or more static scene sectors (<c>sec</c>), resolves each entity's mesh and builds the
/// runtime scene graph. Meshes shared by several entities are uploaded once. The camera frames the
/// combined bounds and fog is derived from the scene radius.
/// </summary>
public sealed class SectorScene : IDisposable
{
	private const float MinimumRadius = 100f;

	private readonly MaterialLibrary _materials;
	private readonly List<DrMesh> _meshes = [];
	private readonly List<string> _warnings = [];
	private readonly List<(SceneNode Node, ElexSceneEntity Entity)> _entityPairs = [];
	private bool _disposed;

	/// <summary>Loads and builds the requested sectors.</summary>
	/// <param name="device">Graphics device used for buffers and textures.</param>
	/// <param name="assets">Mounted assets.</param>
	/// <param name="resources">Resolved resource index.</param>
	/// <param name="sectors">Sector resource names; one or more.</param>
	/// <exception cref="ArgumentException"><paramref name="sectors"/> is empty.</exception>
	/// <exception cref="AssetNotFoundException">A sector or one of its meshes cannot be resolved.</exception>
	public SectorScene(
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		IReadOnlyList<string> sectors)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(sectors);
		if (sectors.Count == 0)
		{
			throw new ArgumentException("At least one sector is required.", nameof(sectors));
		}

		_materials = new MaterialLibrary(device, assets, resources);
		Root = new SceneNode("sectors");
		using var classBlocks = new ResourceClassBlockReader(assets);
		var meshCache = new Dictionary<string, DrMesh?>(StringComparer.OrdinalIgnoreCase);
		var missingMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var names = new List<string>();
		var instances = 0;
		var parts = 0;

		foreach (var sectorName in sectors)
		{
			var resource = FindSector(resources, sectorName);
			var scene = ElexScene.Parse(
				resource.Name,
				resource.VirtualPath,
				ReadAll(assets, resource.VirtualPath));
			names.Add(scene.Name);
			var sceneRoot = BuildNode(
				scene.Root,
				device,
				assets,
				resources,
				classBlocks,
				meshCache,
				missingMeshes);
			Root.AddChild(sceneRoot);
		}

		Root.UpdateWorldTransform();
		CountMeshes(Root, ref instances, ref parts);
		Instances = instances;
		PartCount = parts;
		UniqueMeshCount = _meshes.Count;
		MissingMeshCount = missingMeshes.Count;
		foreach (var missing in missingMeshes)
		{
			_warnings.Add($"Mesh '{missing}' was not found.");
		}

		foreach (var warning in _materials.Warnings)
		{
			_warnings.Add(warning);
		}

		TransformMismatches = CountTransformMismatches();
		if (TransformMismatches > 0)
		{
			_warnings.Add(
				$"{TransformMismatches} entity world transform(s) differ from the parsed scene hierarchy.");
		}

		Bounds = ComputeBounds();
		GroundLevel = ComputeGroundLevel();
		var center = (Bounds.Min + Bounds.Max) * 0.5f;
		var radius = MathF.Max(MinimumRadius, Vector3.Distance(center, Bounds.Max));
		Camera = new FlyCamera
		{
			Position = center + new Vector3(radius * 0.9f, radius * 0.55f, -radius * 1.0f),
			NearPlane = MathF.Max(1f, radius * 0.001f),
			FarPlane = MathF.Max(10_000f, radius * 4f),
			MoveSpeed = MathF.Max(2_000f, radius * 0.3f),
		};
		var direction = Vector3.Normalize(center - Camera.Position);
		Camera.Yaw = MathF.Atan2(direction.X, direction.Z);
		Camera.Pitch = MathF.Asin(direction.Y);
		FogColor = new Vector3(0.63f, 0.69f, 0.76f);
		FogStart = MathF.Max(1_000f, radius * 0.9f);
		FogEnd = MathF.Max(FogStart + 1_000f, radius * 2.8f);
		DisplayName = string.Join("+", names);
	}

	/// <summary>Combined scene-graph root.</summary>
	public SceneNode Root { get; }

	/// <summary>Entities with a resolved mesh.</summary>
	public int Instances { get; }

	/// <summary>Mesh parts across all entities.</summary>
	public int PartCount { get; }

	/// <summary>Unique uploaded meshes.</summary>
	public int UniqueMeshCount { get; }

	/// <summary>Entity mesh names that did not resolve.</summary>
	public int MissingMeshCount { get; }

	/// <summary>Entity transforms whose composed value differs from the parsed scene data.</summary>
	public int TransformMismatches { get; }

	/// <summary>
	/// Framing bounds: the union of the mesh boxes whose centers fall inside the 90th percentile
	/// distance from the average center. Distant outlier props do not drag the camera away.
	/// </summary>
	public BoundingBox Bounds { get; }

	/// <summary>
	/// Estimated ground height, taken from the 10th percentile of mesh-box bottoms so that props
	/// extending below ground do not pull the estimate down. Used to align terrain tiles.
	/// </summary>
	public float GroundLevel { get; }

	/// <summary>Camera framed on the scene bounds.</summary>
	public FlyCamera Camera { get; }

	/// <summary>Fog and background colour.</summary>
	public Vector3 FogColor { get; }

	/// <summary>Fog start distance.</summary>
	public float FogStart { get; }

	/// <summary>Fog end distance.</summary>
	public float FogEnd { get; }

	/// <summary>Loaded sector names, joined with <c>+</c>.</summary>
	public string DisplayName { get; }

	/// <summary>Non-fatal load warnings.</summary>
	public IReadOnlyList<string> Warnings => _warnings;

	/// <summary>Overlay status.</summary>
	public SceneStatus Status => new(DisplayName, Instances, 0, PartCount);

	/// <summary>Submits every visible mesh node to the renderer.</summary>
	public void Submit(Renderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		SubmitNode(renderer, Root);
	}

	/// <summary>Applies fly-camera movement and arrow-key look for one frame.</summary>
	public void UpdateCamera(KeyboardState keyboard, float seconds)
	{
		Camera.UpdateLook(keyboard, seconds);
		Camera.UpdateMovement(keyboard, seconds);
	}

	private SceneNode BuildNode(
		ElexSceneEntity entity,
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		ResourceClassBlockReader classBlocks,
		Dictionary<string, DrMesh?> meshCache,
		HashSet<string> missingMeshes)
	{
		var node = new SceneNode(string.IsNullOrEmpty(entity.Name) ? "entity" : entity.Name);
		if (entity.IsTransformFinite)
		{
			node.LocalTransform = SceneTransforms.ToXna(entity.LocalTransform);
		}
		else
		{
			_warnings.Add($"Entity '{entity.Name}' has a non-finite transform; identity used.");
		}

		if (!string.IsNullOrEmpty(entity.MeshFileName))
		{
			node.Mesh = ResolveMesh(
				entity.MeshFileName,
				device,
				assets,
				resources,
				classBlocks,
				meshCache,
				missingMeshes);
		}

		foreach (var child in entity.Children)
		{
			node.AddChild(BuildNode(
				child,
				device,
				assets,
				resources,
				classBlocks,
				meshCache,
				missingMeshes));
		}

		_entityPairs.Add((node, entity));
		return node;
	}

	private DrMesh? ResolveMesh(
		string meshName,
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		ResourceClassBlockReader classBlocks,
		Dictionary<string, DrMesh?> meshCache,
		HashSet<string> missingMeshes)
	{
		if (meshCache.TryGetValue(meshName, out var cached))
		{
			return cached;
		}

		if (!resources.TryResolve(meshName, "msh", null, out var resource))
		{
			missingMeshes.Add(meshName);
			meshCache[meshName] = null;
			return null;
		}

		try
		{
			var source = GmshMesh.Parse(
				resource.Name,
				resource.VirtualPath,
				ReadAll(assets, resource.VirtualPath));
			MeshMetadata? metadata = null;
			try
			{
				metadata = MeshMetadata.Parse(classBlocks.Read(resource));
			}
			catch (Exception exception) when (exception is Gar5FormatException or GenomeFormatException)
			{
				_warnings.Add($"Mesh metadata '{resource.Name}': {exception.Message}");
			}

			var mesh = MeshLoader.Load(device, source, metadata, _materials.Load);
			meshCache[meshName] = mesh;
			_meshes.Add(mesh);
			return mesh;
		}
		catch (Exception exception) when (
			exception is GmshFormatException or Gar5FormatException or GenomeFormatException or
				NotSupportedException or IOException or UnauthorizedAccessException or
				AssetNotFoundException or InvalidOperationException or ArgumentException)
		{
			_warnings.Add($"Mesh '{meshName}': {exception.Message}");
			meshCache[meshName] = null;
			return null;
		}
	}

	private static ElexResource FindSector(ElexResourceIndex resources, string query)
	{
		if (resources.TryResolve(query, "sec", null, out var exact))
		{
			return exact;
		}

		return resources.Resources
			.Where(resource =>
				string.Equals(resource.TypeToken, "sec", StringComparison.OrdinalIgnoreCase) &&
				resource.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault()
			?? throw new AssetNotFoundException($"No scene sector matches '{query}'.");
	}

	private static void CountMeshes(SceneNode node, ref int instances, ref int parts)
	{
		if (!node.Visible)
		{
			return;
		}

		if (node.Mesh is not null)
		{
			++instances;
			parts += node.PartCount;
		}

		foreach (var child in node.Children)
		{
			CountMeshes(child, ref instances, ref parts);
		}
	}

	private static void SubmitNode(Renderer renderer, SceneNode node)
	{
		if (!node.Visible)
		{
			return;
		}

		if (node.Mesh is not null)
		{
			renderer.SubmitMesh(node.Mesh, node.WorldTransform, node.Shading);
		}

		foreach (var child in node.Children)
		{
			SubmitNode(renderer, child);
		}
	}

	private int CountTransformMismatches()
	{
		var mismatches = 0;
		foreach (var (node, entity) in _entityPairs)
		{
			if (!entity.IsTransformFinite)
			{
				continue;
			}

			var expected = SceneTransforms.ToXna(entity.WorldTransform);
			if (!MatricesClose(node.WorldTransform, expected))
			{
				++mismatches;
			}
		}

		return mismatches;
	}

	private static bool MatricesClose(Matrix first, Matrix second)
	{
		var translation =
			MathF.Max(MathF.Abs(first.M41), MathF.Abs(second.M41)) +
			MathF.Max(MathF.Abs(first.M42), MathF.Abs(second.M42)) +
			MathF.Max(MathF.Abs(first.M43), MathF.Abs(second.M43));
		var tolerance = MathF.Max(1f, translation) * 1e-4f;
		return
			MathF.Abs(first.M41 - second.M41) <= tolerance &&
			MathF.Abs(first.M42 - second.M42) <= tolerance &&
			MathF.Abs(first.M43 - second.M43) <= tolerance &&
			MathF.Abs(first.M11 - second.M11) <= 1e-3f &&
			MathF.Abs(first.M22 - second.M22) <= 1e-3f &&
			MathF.Abs(first.M33 - second.M33) <= 1e-3f;
	}

	private float ComputeGroundLevel()
	{
		var bottoms = new List<float>();
		Root.ForEach(node =>
		{
			if (node.HasBounds)
			{
				bottoms.Add(node.WorldBounds.Min.Y);
			}
		});

		if (bottoms.Count == 0)
		{
			return Bounds.Min.Y;
		}

		bottoms.Sort();
		var index = Math.Clamp((int)MathF.Floor(bottoms.Count * 0.1f), 0, bottoms.Count - 1);
		return bottoms[index];
	}

	private BoundingBox ComputeBounds()
	{
		var nodes = new List<(SceneNode Node, Vector3 Center)>();
		Root.ForEach(node =>
		{
			if (node.HasBounds)
			{
				var bounds = node.WorldBounds;
				nodes.Add((node, (bounds.Min + bounds.Max) * 0.5f));
			}
		});

		if (nodes.Count == 0)
		{
			return new BoundingBox(-MinimumRadius * Vector3.One, MinimumRadius * Vector3.One);
		}

		var average = Vector3.Zero;
		foreach (var item in nodes)
		{
			average += item.Center;
		}

		average /= nodes.Count;
		var distances = nodes
			.Select(item => Vector3.Distance(average, item.Center))
			.OrderBy(distance => distance)
			.ToArray();
		var keep = Math.Max(1, (int)MathF.Ceiling(distances.Length * 0.9f));
		var radius = distances[keep - 1];
		BoundingBox? bounds = null;
		foreach (var item in nodes)
		{
			if (Vector3.Distance(average, item.Center) <= radius + 1f)
			{
				bounds = bounds is null
					? item.Node.WorldBounds
					: BoundingBox.CreateMerged(bounds.Value, item.Node.WorldBounds);
			}
		}

		return bounds ?? new BoundingBox(-MinimumRadius * Vector3.One, MinimumRadius * Vector3.One);
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
		foreach (var mesh in _meshes)
		{
			mesh.Dispose();
		}

		_meshes.Clear();
		_materials.Dispose();
	}
}
