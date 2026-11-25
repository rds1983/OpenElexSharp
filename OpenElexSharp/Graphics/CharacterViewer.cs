using DigitalRiseModel.Animation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Loads one <c>skn</c> character/creature into an <see cref="ElexModel"/> and renders it through
/// the stock <see cref="SkinnedEffect"/>. The camera slowly orbits the model so body proportions,
/// materials and skinning can be inspected. An optional motion network supplies the animation
/// controller; the network's compressed keyframe tracks are not decoded yet, so the viewer keeps
/// the bind pose until a clip is started through <see cref="Animation"/>.
/// </summary>
public sealed class CharacterViewer : IDisposable
{
	private readonly ElexModel _model;
	private readonly MaterialLibrary _materials;
	private readonly AnimationController? _animation;
	private readonly int _boneCount;
	private readonly Vector3 _center;
	private readonly float _radius;
	private float _lastAnimationTime;
	private bool _disposed;

	private CharacterViewer(
		string name,
		ElexModel model,
		MaterialLibrary materials,
		AnimationController? animation,
		string? animationDescription)
	{
		Name = name;
		_model = model;
		_materials = materials;
		_animation = animation;
		AnimationDescription = animationDescription;
		_boneCount = model.Instance?.GetSkinTransforms(0).Length ?? 0;
		var bounds = model.BoundingBox ?? new BoundingBox(Vector3.Zero, Vector3.Zero);
		_center = (bounds.Min + bounds.Max) * 0.5f;
		_radius = MathF.Max(1f, Vector3.Distance(_center, bounds.Max));
	}

	/// <summary>Logical character resource name.</summary>
	public string Name { get; }

	/// <summary>Number of bones in the embedded skeleton.</summary>
	public int BoneCount => _boneCount;

	/// <summary>Number of material/submesh draw ranges.</summary>
	public int PartCount => _model.Model?.Meshes.Sum(mesh => mesh.MeshParts.Count) ?? 0;

	/// <summary>Number of unique materials loaded for this character.</summary>
	public int MaterialCount => _materials.Count;

	/// <summary>
	/// DigitalRiseModel playback controller, or <see langword="null"/> when no motion network was
	/// supplied. Start clips through this controller; <see cref="Draw"/> advances it when playing.
	/// </summary>
	public AnimationController? Animation => _animation;

	/// <summary>Animation summary, or <see langword="null"/> when no network was supplied.</summary>
	public string? AnimationDescription { get; }

	/// <summary>Overlay status.</summary>
	public SceneStatus Status => new(Name, 1, 1, PartCount);

	/// <summary>Non-fatal load warnings.</summary>
	public IReadOnlyList<string> Warnings => _materials.Warnings;

	/// <summary>
	/// Loads a skinned character. Throws when the resource has no GMSH geometry, skeleton or skinning
	/// attributes.
	/// </summary>
	public static CharacterViewer Load(
		GraphicsDevice device,
		AssetFileSystem assets,
		ElexResourceIndex resources,
		ElexResource resource,
		MotionNetwork? network = null)
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
			var model = ElexModelLoader.Load(
				device,
				source,
				skeleton,
				metadata,
				materials.Load);
			var animation = network is null ? null : new AnimationController(model.Instance!);
			var description = network is null
				? null
				: $"network={network.Name} clips={network.Clips.Count} " +
					"(keyframe tracks pending; bind-pose)";
			return new CharacterViewer(
				resource.Name,
				model,
				materials,
				animation,
				description);
		}
		catch
		{
			materials.Dispose();
			throw;
		}
	}

	/// <summary>Draws the current pose with a slowly orbiting camera.</summary>
	public void Draw(Renderer renderer, float time)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		if (_animation is not null && _animation.IsPlaying)
		{
			var delta = MathF.Max(0f, time - _lastAnimationTime);
			_animation.Update(TimeSpan.FromSeconds(delta));
		}

		_lastAnimationTime = time;
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
		renderer.DrawSkinnedMesh(_model, Matrix.Identity);
		renderer.EndFrame();
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
		_model.Model?.Dispose();
		_materials.Dispose();
	}
}
