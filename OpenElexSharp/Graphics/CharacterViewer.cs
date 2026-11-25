using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Loads one <c>skn</c> character/creature, its embedded skeleton and material submeshes, and renders
/// the bind pose with the stock <see cref="SkinnedEffect"/>. The camera slowly orbits the model so
/// body proportions, materials and skinning can be inspected.
/// </summary>
public sealed class CharacterViewer : IDisposable
{
	private readonly SkinnedMesh _mesh;
	private readonly MaterialLibrary _materials;
	private readonly Matrix[] _bindTransforms;
	private readonly Vector3 _center;
	private readonly float _radius;
	private bool _disposed;

	private CharacterViewer(
		string name,
		SkinnedMesh mesh,
		MaterialLibrary materials)
	{
		Name = name;
		_mesh = mesh;
		_materials = materials;
		_bindTransforms = mesh.CreateBindPoseTransforms();
		var bounds = ComputeBounds(mesh.Mesh);
		_center = (bounds.Min + bounds.Max) * 0.5f;
		_radius = MathF.Max(1f, Vector3.Distance(_center, bounds.Max));
	}

	/// <summary>Logical character resource name.</summary>
	public string Name { get; }

	/// <summary>Number of bones in the embedded skeleton.</summary>
	public int BoneCount => _mesh.BoneCount;

	/// <summary>Number of material/submesh draw ranges.</summary>
	public int PartCount => _mesh.Mesh.MeshParts.Count;

	/// <summary>Number of unique materials loaded for this character.</summary>
	public int MaterialCount => _materials.Count;

	/// <summary>Overlay status.</summary>
	public SceneStatus Status => new(Name, 1, 1, PartCount);

	/// <summary>Non-fatal load warnings.</summary>
	public IReadOnlyList<string> Warnings => _materials.Warnings;

	/// <summary>
	/// Loads a skinned character. Throws when the resource has no GMSH geometry, skeleton or skinning
	/// attributes.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The skeleton tail is not recognized.</exception>
	public static CharacterViewer Load(
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		ElexResource resource)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(resource);
		var data = ReadAll(assets, resource.VirtualPath);
		var source = GmshMesh.ParseSkinned(resource.Name, resource.VirtualPath, data);
		var tailOffset = source.GmshOffset + (int)source.IndexOffset;
		var skeleton = Skeleton.TryParse(data, tailOffset)
			?? throw new InvalidOperationException(
				$"Character '{resource.Name}' has an unrecognized skeleton tail.");
		if (skeleton.Bones.Count == 0)
		{
			Log.Warning(
				$"Character '{resource.Name}' has names/parents but no bind-pose transforms; " +
				"rendering the authored bind pose.");
		}

		MeshMetadata? metadata = null;
		try
		{
			using var classBlocks = new ResourceClassBlockReader(assets);
			metadata = MeshMetadata.Parse(classBlocks.Read(resource));
		}
		catch (Exception exception) when (exception is Gar5FormatException or GenomeFormatException)
		{
			Log.Warning($"Character metadata '{resource.Name}': {exception.Message}");
		}

		var materials = new MaterialLibrary(device, assets, resources);
		try
		{
			var mesh = SkinnedMesh.Load(device, source, skeleton, metadata, materials.Load);
			return new CharacterViewer(resource.Name, mesh, materials);
		}
		catch
		{
			materials.Dispose();
			throw;
		}
	}

	/// <summary>Draws the bind pose with a slowly orbiting camera.</summary>
	public void Draw(Renderer renderer, float time)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		var camera = _center + new Vector3(
			MathF.Cos(time * 0.35f) * _radius * 2.2f,
			_radius * 1.1f,
			MathF.Sin(time * 0.35f) * _radius * 2.2f);
		var view = Matrix.CreateLookAt(camera, _center, Vector3.Up);
		var projection = Matrix.CreatePerspectiveFieldOfView(
			MathHelper.PiOver4,
			renderer.ViewportAspectRatio,
			MathF.Max(0.01f, _radius * 0.001f),
			MathF.Max(1_000f, _radius * 20f));
		renderer.FogEnabled = false;
		renderer.CullMode = CullMode.None;
		renderer.BeginFrame(view, projection);
		renderer.Clear(new Color(16, 18, 24));
		renderer.DrawSkinnedMesh(_mesh.Mesh, Matrix.Identity, _bindTransforms);
		renderer.EndFrame();
	}

	private static BoundingBox ComputeBounds(DrMesh mesh)
	{
		var bounds = mesh.MeshParts[0].BoundingBox;
		for (var index = 1; index < mesh.MeshParts.Count; index++)
		{
			bounds = BoundingBox.CreateMerged(bounds, mesh.MeshParts[index].BoundingBox);
		}

		return bounds;
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
		_mesh.Dispose();
		_materials.Dispose();
	}
}
