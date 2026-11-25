using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>State shown by the viewer rows of the debug overlay.</summary>
/// <param name="Name">Current resource name.</param>
/// <param name="Index">Zero-based position in the catalog order.</param>
/// <param name="Count">Number of mesh resources.</param>
/// <param name="Shading">Current shading mode.</param>
/// <param name="ShowTangents">Whether tangent lines are drawn.</param>
/// <param name="CullMode">Current face-culling mode.</param>
public readonly record struct MeshViewerStatus(
	string Name,
	int Index,
	int Count,
	MeshShading Shading,
	bool ShowTangents,
	CullMode CullMode);

/// <summary>
/// Loads one static mesh at a time and cycles through every <c>msh</c> resource in catalog order.
/// The viewer owns the current mesh and its material library; changing the mesh releases both.
/// </summary>
public sealed class MeshViewer : IDisposable
{
	private const int MaxTangentLines = 2000;

	private readonly GraphicsDevice _device;
	private readonly AssetFileSystem _assets;
	private readonly ElexResourceIndex _resources;
	private readonly ResourceClassBlockReader _classBlocks;
	private readonly ElexResource[] _meshResources;
	private readonly List<string> _warnings = [];

	private MaterialLibrary? _materials;
	private DrMesh? _mesh;
	private VertexPositionColor[] _tangentLines = [];
	private int _index = -1;
	private bool _disposed;

	/// <summary>Builds the viewer over the mounted mesh resources.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public MeshViewer(GraphicsDevice device, AssetFileSystem assets, ElexResourceIndex resources)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(resources);

		_device = device;
		_assets = assets;
		_resources = resources;
		_classBlocks = new ResourceClassBlockReader(assets);
		_meshResources = resources.Resources
			.Where(resource => string.Equals(resource.TypeToken, "msh", StringComparison.OrdinalIgnoreCase))
			.DistinctBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	/// <summary>Number of mesh resources available to the viewer.</summary>
	public int Count => _meshResources.Length;

	/// <summary>Zero-based catalog position of the current mesh, or -1 before the first load.</summary>
	public int Index => _index;

	/// <summary>The current GPU mesh, or <see langword="null"/> when no mesh loaded.</summary>
	public DrMesh? Mesh => _mesh;

	/// <summary>Tangent line segments for the current mesh, empty when it has no tangent stream.</summary>
	public VertexPositionColor[] TangentLines => _tangentLines;

	/// <summary>Vertex count of the current mesh.</summary>
	public int VertexCount { get; private set; }

	/// <summary>Triangle count of the current mesh.</summary>
	public int TriangleCount { get; private set; }

	/// <summary>Number of mesh parts (submeshes) in the current mesh.</summary>
	public int PartCount => _mesh?.MeshParts.Count ?? 0;

	/// <summary>Number of materials resolved for the current mesh.</summary>
	public int MaterialCount => _materials?.Count ?? 0;

	/// <summary>Shading used for the current mesh.</summary>
	public MeshShading Shading { get; set; } = MeshShading.Lit;

	/// <summary>Whether tangent lines are enabled.</summary>
	public bool ShowTangents { get; set; }

	/// <summary>Face culling used when drawing the current mesh.</summary>
	public CullMode CullMode { get; set; } = CullMode.None;

	/// <summary>Non-fatal warnings from the last load.</summary>
	public IReadOnlyList<string> Warnings => _warnings;

	/// <summary>Overlay status, or <see langword="null"/> before a mesh loads.</summary>
	public MeshViewerStatus? Status => _index >= 0 && _index < _meshResources.Length
		? new MeshViewerStatus(
			_meshResources[_index].Name,
			_index,
			_meshResources.Length,
			Shading,
			ShowTangents,
			CullMode)
		: null;

	/// <summary>
	/// Loads the first mesh matching <paramref name="query"/>. An exact name wins over a
	/// substring match; a missing query loads the first catalog mesh.
	/// </summary>
	/// <returns><see langword="true"/> when a mesh is loaded.</returns>
	public bool TryLoad(string? query, out string? error)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return LoadIndex(0, out error);
		}

		var index = FindIndex(query.Trim(), exact: true);
		if (index < 0)
		{
			index = FindIndex(query.Trim(), exact: false);
		}

		if (index < 0)
		{
			error = $"No mesh resource matches '{query}'.";
			return false;
		}

		return LoadIndex(index, out error);
	}

	/// <summary>Loads the mesh at <paramref name="index"/>.</summary>
	/// <returns><see langword="true"/> when the mesh loaded; the previous one stays on failure.</returns>
	public bool LoadIndex(int index, out string? error)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if ((uint)index >= (uint)_meshResources.Length)
		{
			error = $"Mesh index {index} is outside the catalog.";
			return false;
		}

		var resource = _meshResources[index];
		MaterialLibrary? materials = null;
		_warnings.Clear();
		try
		{
			using var stream = _assets.OpenRead(resource.VirtualPath);
			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			var source = GmshMesh.Parse(resource.Name, resource.VirtualPath, buffer.ToArray());

			MeshMetadata? metadata = null;
			try
			{
				metadata = MeshMetadata.Parse(_classBlocks.Read(resource));
			}
			catch (Exception exception) when (exception is Gar5FormatException or GenomeFormatException)
			{
				_warnings.Add($"Mesh metadata '{resource.Name}': {exception.Message}");
			}

			materials = new MaterialLibrary(_device, _assets, _resources);
			var mesh = MeshLoader.Load(_device, source, metadata, materials.Load);

			DisposeCurrent();
			_materials = materials;
			_mesh = mesh;
			_tangentLines = BuildTangentLines(source);
			_index = index;
			VertexCount = source.Positions.Count;
			TriangleCount = source.TriangleCount;
			error = null;
			return true;
		}
		catch (Exception exception) when (
			exception is GmshFormatException or Gar5FormatException or GenomeFormatException or
				DdsFormatException or NotSupportedException or IOException or UnauthorizedAccessException or
				AssetNotFoundException or InvalidOperationException or ArgumentException)
		{
			materials?.Dispose();
			error = exception.Message;
			return false;
		}
	}

	/// <summary>Moves by <paramref name="delta"/> entries, wrapping at the ends.</summary>
	public bool Cycle(int delta, out string? error)
	{
		if (_meshResources.Length == 0)
		{
			error = "No mesh resources are available.";
			return false;
		}

		var start = _index < 0 ? 0 : _index;
		var next = ((start + delta) % _meshResources.Length + _meshResources.Length) %
			_meshResources.Length;
		return LoadIndex(next, out error);
	}

	/// <summary>Loads the first mesh in catalog order.</summary>
	public bool LoadFirst(out string? error) => LoadIndex(0, out error);

	/// <summary>Loads the last mesh in catalog order.</summary>
	public bool LoadLast(out string? error) => LoadIndex(_meshResources.Length - 1, out error);

	/// <summary>Resource name of the current mesh, or <see langword="null"/>.</summary>
	public string? CurrentName => _index >= 0 && _index < _meshResources.Length
		? _meshResources[_index].Name
		: null;

	private int FindIndex(string query, bool exact)
	{
		for (var index = 0; index < _meshResources.Length; index++)
		{
			var name = _meshResources[index].Name;
			if (exact
				? string.Equals(name, query, StringComparison.OrdinalIgnoreCase)
				: name.Contains(query, StringComparison.OrdinalIgnoreCase))
			{
				return index;
			}
		}

		return -1;
	}

	private static VertexPositionColor[] BuildTangentLines(GmshMesh source)
	{
		if (source.Tangents.Count != source.Positions.Count || source.Positions.Count == 0)
		{
			return [];
		}

		var min = source.Positions[0];
		var max = source.Positions[0];
		foreach (var position in source.Positions)
		{
			min = NumericsVector3.Min(min, position);
			max = NumericsVector3.Max(max, position);
		}

		var radius = MathF.Max(0.001f, NumericsVector3.Distance(min, max) * 0.5f);
		var length = MathF.Max(1f, radius * 0.03f);
		var step = Math.Max(1, source.Positions.Count / MaxTangentLines);
		var lines = new List<VertexPositionColor>(Math.Min(source.Positions.Count, MaxTangentLines) * 2);
		var color = new Color(255, 0, 255);
		for (var index = 0; index < source.Positions.Count; index += step)
		{
			var tangent = source.Tangents[index];
			if (tangent.LengthSquared() <= 1e-8f)
			{
				continue;
			}

			var start = ToXna(source.Positions[index]);
			var end = start + ToXna(NumericsVector3.Normalize(tangent)) * length;
			lines.Add(new VertexPositionColor(start, color));
			lines.Add(new VertexPositionColor(end, color));
		}

		return [.. lines];
	}

	private static Vector3 ToXna(NumericsVector3 value) => new(value.X, value.Y, value.Z);

	private void DisposeCurrent()
	{
		_mesh?.Dispose();
		_mesh = null;
		_materials?.Dispose();
		_materials = null;
		_tangentLines = [];
		VertexCount = 0;
		TriangleCount = 0;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		DisposeCurrent();
		_classBlocks.Dispose();
	}
}
