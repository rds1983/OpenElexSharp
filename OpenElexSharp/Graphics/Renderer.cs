using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Low-level drawing service for the test scenes. It owns the stock effects, device state and
/// draw-call statistics; scene code only supplies meshes, transforms and cameras.
/// </summary>
public sealed class Renderer : IDisposable
{
	private readonly GraphicsDevice _device;
	private readonly BasicEffect _effect;
	private readonly SkinnedEffect _skinnedEffect;
	private readonly BasicEffect _lineEffect;
	private readonly SpriteBatch _spriteBatch;
	private readonly BoundingFrustum _frustum = new(Matrix.Identity);
	private readonly Texture2D _fallbackTexture;
	private const int SkyTextureHeight = 128;
	private Texture2D? _skyTexture;
	private Color _skyTopColor = Color.Transparent;
	private Color _skyHorizonColor = Color.Transparent;

	private static readonly RasterizerState CullNoneState = new() { CullMode = CullMode.None };
	private static readonly RasterizerState CullCounterClockwiseState = new()
	{
		CullMode = CullMode.CullCounterClockwiseFace,
	};
	private static readonly RasterizerState CullClockwiseState = new()
	{
		CullMode = CullMode.CullClockwiseFace,
	};

	private bool _frameOpen;
	private CullMode _cullMode = CullMode.None;
	private bool _cullingEnabled = true;
	private Matrix _view;
	private Matrix _projection;
	private readonly List<MeshDrawCommand> _commands = [];

	private BlendState? _savedBlendState;
	private DepthStencilState? _savedDepthStencilState;
	private RasterizerState? _savedRasterizerState;
	private SamplerState? _savedSamplerState;
	private Rectangle _savedScissorRectangle;

	/// <summary>Creates a renderer for one graphics device.</summary>
	public Renderer(GraphicsDevice device)
	{
		ArgumentNullException.ThrowIfNull(device);
		_device = device;
		_spriteBatch = new SpriteBatch(device);
		_fallbackTexture = TextureLoader.CreateFallback(device);
		_effect = new BasicEffect(device)
		{
			LightingEnabled = true,
			TextureEnabled = true,
			VertexColorEnabled = false,
			PreferPerPixelLighting = true,
			SpecularColor = Vector3.Zero,
		};
		_effect.DirectionalLight2.Enabled = false;
		_skinnedEffect = new SkinnedEffect(device)
		{
			PreferPerPixelLighting = true,
			SpecularColor = Vector3.Zero,
			WeightsPerVertex = 4,
			Alpha = 1f,
		};
		_skinnedEffect.DirectionalLight2.Enabled = false;
		_lineEffect = new BasicEffect(device)
		{
			LightingEnabled = false,
			TextureEnabled = false,
			VertexColorEnabled = true,
		};
		ApplyLighting();
	}

	/// <summary>Draw calls issued during the current frame.</summary>
	public int DrawCalls { get; private set; }

	/// <summary>Aspect ratio of the current back-buffer viewport.</summary>
	public float ViewportAspectRatio => _device.Viewport.AspectRatio;

	/// <summary>Triangles submitted during the current frame.</summary>
	public int Triangles { get; private set; }

	/// <summary>Mesh parts submitted during the current frame.</summary>
	public int Meshes { get; private set; }

	/// <summary>Line primitives submitted during the current frame.</summary>
	public int Lines { get; private set; }

	/// <summary>Mesh parts considered by <see cref="SubmitMesh"/> this frame.</summary>
	public int MeshPartsSubmitted { get; private set; }

	/// <summary>Mesh parts rejected by the frustum this frame.</summary>
	public int MeshPartsCulled { get; private set; }

	/// <summary>Material/buffer groups issued by the last <see cref="Flush"/> this frame.</summary>
	public int Batches { get; private set; }

	/// <summary>Whether <see cref="SubmitMesh"/> applies frustum culling.</summary>
	public bool CullingEnabled
	{
		get => _cullingEnabled;
		set => _cullingEnabled = value;
	}

	/// <summary>Whether mesh draws apply linear distance fog.</summary>
	public bool FogEnabled { get; set; }

	/// <summary>Fog and background colour used while <see cref="FogEnabled"/> is on.</summary>
	public Vector3 FogColor { get; set; } = new(0.55f, 0.6f, 0.65f);

	/// <summary>Distance at which fog starts.</summary>
	public float FogStart { get; set; } = 1_000f;

	/// <summary>Distance at which fog fully replaces the surface colour.</summary>
	public float FogEnd { get; set; } = 10_000f;

	/// <summary>Top colour of the full-screen sky gradient.</summary>
	public Color SkyTopColor { get; set; } = new(58, 104, 158);

	/// <summary>Horizon colour of the full-screen sky gradient; usually matches the fog colour.</summary>
	public Color SkyHorizonColor { get; set; } = new(180, 202, 218);

	/// <summary>Direction the sun light travels, from the light toward the scene.</summary>
	public Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(-0.35f, -1f, 0.55f));

	/// <summary>Diffuse colour of the sun directional light.</summary>
	public Vector3 SunColor { get; set; } = new(0.82f, 0.80f, 0.72f);

	/// <summary>Ambient light colour added to every lit surface.</summary>
	public Vector3 AmbientColor { get; set; } = new(0.42f, 0.44f, 0.48f);

	/// <summary>Small constant emissive lift so dark materials do not read as flat black.</summary>
	public Vector3 EmissiveColor { get; set; } = new(0.04f, 0.045f, 0.05f);

	/// <summary>Direction of the fill light, from the light toward the scene.</summary>
	public Vector3 FillDirection { get; set; } = Vector3.Normalize(new Vector3(-0.6f, -0.4f, -0.45f));

	/// <summary>Diffuse colour of the fill light.</summary>
	public Vector3 FillColor { get; set; } = new(0.16f, 0.18f, 0.22f);

	/// <summary>
	/// Face culling used by mesh draws. Lines are unaffected. The value is applied at the start of
	/// the next frame.
	/// </summary>
	public CullMode CullMode
	{
		get => _cullMode;
		set => _cullMode = value;
	}

	/// <summary>Starts a 3D frame and installs the camera matrices.</summary>
	public void BeginFrame(Matrix view, Matrix projection)
	{
		BeginFrameCore();
		_view = view;
		_projection = projection;
		_effect.View = view;
		_effect.Projection = projection;
		_skinnedEffect.View = view;
		_skinnedEffect.Projection = projection;
		_lineEffect.View = view;
		_lineEffect.Projection = projection;
		_frustum.Matrix = view * projection;
	}

	/// <summary>Starts a 2D frame.</summary>
	public void BeginFrame()
	{
		BeginFrameCore();
		_view = Matrix.Identity;
		_projection = Matrix.Identity;
	}

	/// <summary>Clears the backbuffer.</summary>
	public void Clear(Color color)
	{
		EnsureFrameOpen(nameof(Clear));
		_device.Clear(color);
	}

	/// <summary>
	/// Draws the full-screen vertical sky gradient behind the 3D scene. Must be called after
	/// <see cref="BeginFrame(Matrix, Matrix)"/> and before meshes. The method restores the 3D device
	/// state it temporarily overrides for <see cref="SpriteBatch"/>.
	/// </summary>
	public void DrawSky()
	{
		EnsureFrameOpen(nameof(DrawSky));
		EnsureSkyTexture();
		var viewport = _device.Viewport;
		_spriteBatch.Begin(
			SpriteSortMode.Deferred,
			BlendState.Opaque,
			SamplerState.LinearClamp,
			DepthStencilState.None,
			RasterizerState.CullNone);
		_spriteBatch.Draw(
			_skyTexture,
			new Rectangle(0, 0, viewport.Width, viewport.Height),
			Color.White);
		_spriteBatch.End();
		_device.BlendState = BlendState.Opaque;
		_device.DepthStencilState = DepthStencilState.Default;
		_device.RasterizerState = StateFor(_cullMode);
		_device.SamplerStates[0] = SamplerState.LinearWrap;
		_device.ScissorRectangle = viewport.Bounds;
	}

	/// <summary>
	/// Draws every part of a DigitalRiseModel mesh. Each part uses its own material texture when
	/// present and the fallback checker texture otherwise.
	/// </summary>
	public void DrawMesh(DrMesh mesh, Matrix world, bool cull = false, MeshShading shading = MeshShading.Lit)
	{
		EnsureFrameOpen(nameof(DrawMesh));
		ArgumentNullException.ThrowIfNull(mesh);
		foreach (var part in mesh.MeshParts)
		{
			DrawMeshPart(part, world, cull, shading);
		}
	}

	/// <summary>
	/// Draws every skinned mesh part of an <see cref="ElexModel"/> with the stock
	/// <see cref="SkinnedEffect"/>. The vertex buffers must use
	/// <see cref="VertexPositionNormalTextureSkinned"/>; the skin transforms come from the model
	/// instance and the per-part material from <see cref="ElexModel.Materials"/>.
	/// </summary>
	/// <param name="model">Model, instance and material table to draw.</param>
	/// <param name="world">World transform.</param>
	/// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The model has no instance/model, or the skin has too many bones.</exception>
	public void DrawSkinnedMesh(ElexModel model, Matrix world)
	{
		EnsureFrameOpen(nameof(DrawSkinnedMesh));
		ArgumentNullException.ThrowIfNull(model);
		var instance = model.Instance
			?? throw new ArgumentException("The model has no instance.", nameof(model));
		var source = instance.Model
			?? throw new ArgumentException("The model instance has no model.", nameof(model));
		_skinnedEffect.World = world;
		var cachedSkin = -1;
		Matrix[]? boneTransforms = null;
		for (var meshIndex = 0; meshIndex < source.Meshes.Length; meshIndex++)
		{
			var mesh = source.Meshes[meshIndex];
			for (var partIndex = 0; partIndex < mesh.MeshParts.Count; partIndex++)
			{
				var part = mesh.MeshParts[partIndex];
				if (part.VertexBuffer is null || part.IndexBuffer is null)
				{
					continue;
				}

				var skinIndex = part.Skin?.SkinIndex ?? 0;
				if (boneTransforms is null || skinIndex != cachedSkin)
				{
					boneTransforms = instance.GetSkinTransforms(skinIndex);
					cachedSkin = skinIndex;
					if (boneTransforms.Length > SkinnedEffect.MaxBones)
					{
						throw new ArgumentException(
							$"SkinnedEffect supports at most {SkinnedEffect.MaxBones} bones.",
							nameof(model));
					}

					_skinnedEffect.SetBoneTransforms(boneTransforms);
				}

				var material = model.GetMaterial(meshIndex, partIndex);
				ApplySkinnedShading(material?.Texture ?? _fallbackTexture);
				_device.SetVertexBuffer(part.VertexBuffer);
				_device.Indices = part.IndexBuffer;
				foreach (var pass in _skinnedEffect.CurrentTechnique.Passes)
				{
					pass.Apply();
					_device.DrawIndexedPrimitives(
						part.PrimitiveType,
						part.VertexOffset,
						part.StartIndex,
						part.PrimitiveCount);
				}

				++DrawCalls;
				++Meshes;
				Triangles += PrimitiveCountToTriangles(part.PrimitiveType, part.PrimitiveCount);
			}
		}
	}

	private void ApplySkinnedShading(Texture2D texture)
	{
		_skinnedEffect.Texture = texture;
		_skinnedEffect.FogEnabled = FogEnabled;
		if (FogEnabled)
		{
			_skinnedEffect.FogColor = FogColor;
			_skinnedEffect.FogStart = FogStart;
			_skinnedEffect.FogEnd = FogEnd;
		}

		_skinnedEffect.DiffuseColor = Vector3.One;
		_skinnedEffect.SpecularColor = Vector3.Zero;
	}

	/// <summary>
	/// Draws one DigitalRiseModel mesh part. The part's <see cref="DrMeshPartTag"/> supplies the
	/// bounding sphere used for optional culling and the runtime material whose diffuse texture is
	/// bound for the draw.
	/// </summary>
	public void DrawMeshPart(
		DrMeshPart part,
		Matrix world,
		bool cull = false,
		MeshShading shading = MeshShading.Lit)
	{
		EnsureFrameOpen(nameof(DrawMeshPart));
		ArgumentNullException.ThrowIfNull(part);
		if (part.VertexBuffer is null || part.IndexBuffer is null)
		{
			return;
		}

		var tag = part.Tag as DrMeshPartTag;
		if (cull)
		{
			var sphere = tag?.Bounds ?? default;
			if (sphere.Radius > 0f && !_frustum.Intersects(sphere.Transform(world)))
			{
				return;
			}
		}

		ApplyShading(shading);
		DrawMeshPartCore(part, world, tag?.Material?.Texture ?? _fallbackTexture);
	}

	/// <summary>
	/// Queues every part of <paramref name="mesh"/> for the current frame. Parts outside the view
	/// frustum are counted in <see cref="MeshPartsCulled"/> and omitted unless
	/// <see cref="CullingEnabled"/> is off. Call <see cref="Flush"/> before ending the frame.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="mesh"/> is <see langword="null"/>.</exception>
	public void SubmitMesh(DrMesh mesh, Matrix world, MeshShading shading = MeshShading.Lit)
	{
		ArgumentNullException.ThrowIfNull(mesh);
		foreach (var part in mesh.MeshParts)
		{
			SubmitMeshPart(part, world, shading);
		}
	}

	/// <summary>Queues one mesh part, applying frustum culling and batch sorting.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="part"/> is <see langword="null"/>.</exception>
	public void SubmitMeshPart(DrMeshPart part, Matrix world, MeshShading shading = MeshShading.Lit)
	{
		EnsureFrameOpen(nameof(SubmitMeshPart));
		ArgumentNullException.ThrowIfNull(part);
		if (part.VertexBuffer is null || part.IndexBuffer is null)
		{
			return;
		}

		++MeshPartsSubmitted;
		var tag = part.Tag as DrMeshPartTag;
		if (_cullingEnabled)
		{
			var sphere = tag?.Bounds ?? default;
			if (sphere.Radius <= 0f)
			{
				sphere = BoundingSphere.CreateFromBoundingBox(part.BoundingBox);
			}

			if (!_frustum.Intersects(sphere.Transform(world)))
			{
				++MeshPartsCulled;
				return;
			}
		}

		var texture = tag?.Material?.Texture ?? _fallbackTexture;
		_commands.Add(new MeshDrawCommand(
			part,
			world,
			shading,
			texture,
			new BatchKey(
				RuntimeHelpers.GetHashCode(texture),
				RuntimeHelpers.GetHashCode(part.VertexBuffer),
				RuntimeHelpers.GetHashCode(part.IndexBuffer),
				shading)));
	}

	/// <summary>
	/// Sorts queued parts by material texture, buffer and shading, draws them and reports the number
	/// of state groups in <see cref="Batches"/>. Called automatically by <see cref="EndFrame"/>.
	/// </summary>
	public void Flush()
	{
		EnsureFrameOpen(nameof(Flush));
		if (_commands.Count == 0)
		{
			return;
		}

		_commands.Sort(CompareCommands);
		BatchKey? current = null;
		foreach (var command in _commands)
		{
			if (current is null || !current.Value.Equals(command.Batch))
			{
				++Batches;
				current = command.Batch;
			}

			ApplyShading(command.Shading);
			DrawMeshPartCore(command.Part, command.World, command.Texture);
		}

		_commands.Clear();
	}

	private void DrawMeshPartCore(DrMeshPart part, Matrix world, Texture2D texture)
	{
		_effect.World = world;
		_effect.Texture = texture;
		_effect.DiffuseColor = Vector3.One;
		foreach (var pass in _effect.CurrentTechnique.Passes)
		{
			pass.Apply();
			_device.SetVertexBuffer(part.VertexBuffer);
			_device.Indices = part.IndexBuffer;
			_device.DrawIndexedPrimitives(
				part.PrimitiveType,
				part.VertexOffset,
				part.StartIndex,
				part.PrimitiveCount);
		}

		++DrawCalls;
		++Meshes;
		Triangles += PrimitiveCountToTriangles(part.PrimitiveType, part.PrimitiveCount);
	}

	private static int CompareCommands(MeshDrawCommand first, MeshDrawCommand second)
	{
		var result = first.Batch.Texture.CompareTo(second.Batch.Texture);
		if (result != 0)
		{
			return result;
		}

		result = first.Batch.VertexBuffer.CompareTo(second.Batch.VertexBuffer);
		if (result != 0)
		{
			return result;
		}

		result = first.Batch.IndexBuffer.CompareTo(second.Batch.IndexBuffer);
		if (result != 0)
		{
			return result;
		}

		return first.Batch.Shading.CompareTo(second.Batch.Shading);
	}

	/// <summary>Draws a list of colored line segments.</summary>
	/// <param name="vertices">Line-list vertices.</param>
	/// <param name="world">World matrix.</param>
	/// <param name="depthTest">Whether the lines should be occluded by existing geometry.</param>
	/// <exception cref="ArgumentNullException"><paramref name="vertices"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The vertex count is not even.</exception>
	public void DrawLines(VertexPositionColor[] vertices, Matrix world, bool depthTest = true)
	{
		EnsureFrameOpen(nameof(DrawLines));
		ArgumentNullException.ThrowIfNull(vertices);
		if (vertices.Length == 0)
		{
			return;
		}

		if (vertices.Length % 2 != 0)
		{
			throw new ArgumentException("Line lists require an even vertex count.", nameof(vertices));
		}

		_lineEffect.World = world;
		var savedDepthState = _device.DepthStencilState;
		if (!depthTest)
		{
			_device.DepthStencilState = DepthStencilState.None;
		}

		try
		{
			foreach (var pass in _lineEffect.CurrentTechnique.Passes)
			{
				pass.Apply();
				_device.DrawUserPrimitives(
					PrimitiveType.LineList,
					vertices,
					0,
					vertices.Length / 2);
			}
		}
		finally
		{
			_device.DepthStencilState = savedDepthState;
		}

		++DrawCalls;
		Lines += vertices.Length / 2;
	}

	/// <summary>Draws a texture with the stock SpriteBatch path.</summary>
	public void DrawTexture(Texture2D texture, Rectangle destination)
	{
		EnsureFrameOpen(nameof(DrawTexture));
		ArgumentNullException.ThrowIfNull(texture);
		_spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
		_spriteBatch.Draw(texture, destination, Color.White);
		_spriteBatch.End();
		++DrawCalls;
	}

	/// <summary>Restores device state and closes the frame.</summary>
	public void EndFrame()
	{
		EnsureFrameOpen(nameof(EndFrame));
		Flush();
		_frameOpen = false;
		_device.BlendState = _savedBlendState!;
		_device.DepthStencilState = _savedDepthStencilState!;
		_device.RasterizerState = _savedRasterizerState!;
		_device.SamplerStates[0] = _savedSamplerState!;
		_device.ScissorRectangle = _savedScissorRectangle;
	}

	private void BeginFrameCore()
	{
		if (_frameOpen)
		{
			throw new InvalidOperationException(
				$"{nameof(BeginFrame)} was called while a frame is already open.");
		}

		_savedBlendState = _device.BlendState;
		_savedDepthStencilState = _device.DepthStencilState;
		_savedRasterizerState = _device.RasterizerState;
		_savedSamplerState = _device.SamplerStates[0];
		_savedScissorRectangle = _device.ScissorRectangle;
		DrawCalls = 0;
		Triangles = 0;
		Meshes = 0;
		Lines = 0;
		MeshPartsSubmitted = 0;
		MeshPartsCulled = 0;
		Batches = 0;
		_commands.Clear();
		_frameOpen = true;
		_device.BlendState = BlendState.Opaque;
		_device.DepthStencilState = DepthStencilState.Default;
		_device.RasterizerState = StateFor(_cullMode);
		_device.SamplerStates[0] = SamplerState.LinearWrap;
		_device.ScissorRectangle = _device.Viewport.Bounds;
		ApplyLighting();
	}

	private void ApplyLighting()
	{
		var sunDirection = SunDirection.LengthSquared() > 1e-6f
			? Vector3.Normalize(SunDirection)
			: Vector3.Down;
		var fillDirection = FillDirection.LengthSquared() > 1e-6f
			? Vector3.Normalize(FillDirection)
			: Vector3.Up;
		_effect.AmbientLightColor = AmbientColor;
		_effect.EmissiveColor = EmissiveColor;
		_effect.DirectionalLight0.Enabled = true;
		_effect.DirectionalLight0.Direction = sunDirection;
		_effect.DirectionalLight0.DiffuseColor = SunColor;
		_effect.DirectionalLight1.Enabled = true;
		_effect.DirectionalLight1.Direction = fillDirection;
		_effect.DirectionalLight1.DiffuseColor = FillColor;
		_effect.DirectionalLight2.Enabled = false;
		_skinnedEffect.AmbientLightColor = AmbientColor;
		_skinnedEffect.EmissiveColor = EmissiveColor;
		_skinnedEffect.DirectionalLight0.Enabled = true;
		_skinnedEffect.DirectionalLight0.Direction = sunDirection;
		_skinnedEffect.DirectionalLight0.DiffuseColor = SunColor;
		_skinnedEffect.DirectionalLight1.Enabled = true;
		_skinnedEffect.DirectionalLight1.Direction = fillDirection;
		_skinnedEffect.DirectionalLight1.DiffuseColor = FillColor;
		_skinnedEffect.DirectionalLight2.Enabled = false;
	}

	private void EnsureSkyTexture()
	{
		_skyTexture ??= new Texture2D(
			_device,
			1,
			SkyTextureHeight,
			false,
			SurfaceFormat.Color);
		if (_skyTopColor == SkyTopColor && _skyHorizonColor == SkyHorizonColor)
		{
			return;
		}

		var pixels = new Color[SkyTextureHeight];
		for (var y = 0; y < SkyTextureHeight; y++)
		{
			pixels[y] = Color.Lerp(
				SkyTopColor,
				SkyHorizonColor,
				y / (float)(SkyTextureHeight - 1));
		}

		_skyTexture.SetData(pixels);
		_skyTopColor = SkyTopColor;
		_skyHorizonColor = SkyHorizonColor;
	}

	private void ApplyShading(MeshShading shading)
	{
		_effect.FogEnabled = FogEnabled;
		if (FogEnabled)
		{
			_effect.FogColor = FogColor;
			_effect.FogStart = FogStart;
			_effect.FogEnd = FogEnd;
		}

		switch (shading)
		{
			case MeshShading.VertexColor:
				_effect.LightingEnabled = false;
				_effect.TextureEnabled = false;
				_effect.VertexColorEnabled = true;
				break;
			default:
				_effect.LightingEnabled = true;
				_effect.TextureEnabled = true;
				_effect.VertexColorEnabled = true;
				break;
		}
	}

	private static RasterizerState StateFor(CullMode mode) =>
		mode switch
		{
			CullMode.CullCounterClockwiseFace => CullCounterClockwiseState,
			CullMode.CullClockwiseFace => CullClockwiseState,
			_ => CullNoneState,
		};

	private static int PrimitiveCountToTriangles(PrimitiveType primitiveType, int primitiveCount)
	{
		return primitiveType switch
		{
			PrimitiveType.TriangleList => primitiveCount,
			PrimitiveType.TriangleStrip => Math.Max(0, primitiveCount - 2),
			_ => 0,
		};
	}

	private readonly record struct BatchKey(
		int Texture,
		int VertexBuffer,
		int IndexBuffer,
		MeshShading Shading);

	private readonly record struct MeshDrawCommand(
		DrMeshPart Part,
		Matrix World,
		MeshShading Shading,
		Texture2D Texture,
		BatchKey Batch);

	private void EnsureFrameOpen(string method)
	{
		if (!_frameOpen)
		{
			throw new InvalidOperationException(
				$"{method} requires an open frame. Call {nameof(BeginFrame)} first and {nameof(EndFrame)} after.");
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_effect.Dispose();
		_skinnedEffect.Dispose();
		_lineEffect.Dispose();
		_spriteBatch.Dispose();
		_fallbackTexture.Dispose();
		_skyTexture?.Dispose();
	}
}
