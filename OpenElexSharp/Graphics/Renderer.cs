using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Low-level drawing service for the test scenes. It owns the stock effects, device state and
/// draw-call statistics; scene code only supplies meshes, transforms and cameras.
/// </summary>
public sealed class Renderer : IDisposable
{
	private readonly GraphicsDevice _device;
	private readonly BasicEffect _effect;
	private readonly SpriteBatch _spriteBatch;
	private readonly BoundingFrustum _frustum = new(Matrix.Identity);
	private readonly Texture2D _fallbackTexture;

	private bool _frameOpen;
	private Matrix _view;
	private Matrix _projection;

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
			AmbientLightColor = new Vector3(0.35f),
		};
		_effect.DirectionalLight0.Enabled = true;
		_effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(0.35f, -1f, -0.55f));
		_effect.DirectionalLight0.DiffuseColor = new Vector3(0.75f);
		_effect.DirectionalLight1.Enabled = true;
		_effect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(-0.6f, -0.4f, -0.45f));
		_effect.DirectionalLight1.DiffuseColor = new Vector3(0.25f);
		_effect.DirectionalLight2.Enabled = false;
	}

	/// <summary>Draw calls issued during the current frame.</summary>
	public int DrawCalls { get; private set; }

	/// <summary>Triangles submitted during the current frame.</summary>
	public int Triangles { get; private set; }

	/// <summary>Mesh parts submitted during the current frame.</summary>
	public int Meshes { get; private set; }

	/// <summary>Starts a 3D frame and installs the camera matrices.</summary>
	public void BeginFrame(Matrix view, Matrix projection)
	{
		BeginFrameCore();
		_view = view;
		_projection = projection;
		_effect.View = view;
		_effect.Projection = projection;
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
	/// Draws every part of a DigitalRiseModel mesh. Each part uses its own material texture when
	/// present and the fallback checker texture otherwise.
	/// </summary>
	public void DrawMesh(DrMesh mesh, Matrix world, bool cull = false)
	{
		EnsureFrameOpen(nameof(DrawMesh));
		ArgumentNullException.ThrowIfNull(mesh);
		foreach (var part in mesh.MeshParts)
		{
			DrawMeshPart(part, world, cull);
		}
	}

	/// <summary>
	/// Draws one DigitalRiseModel mesh part. The part's <see cref="DrMeshPartTag"/> supplies the
	/// bounding sphere used for optional culling and the runtime material whose diffuse texture is
	/// bound for the draw.
	/// </summary>
	public void DrawMeshPart(DrMeshPart part, Matrix world, bool cull = false)
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

		_effect.World = world;
		_effect.Texture = tag?.Material?.Texture ?? _fallbackTexture;
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
		_frameOpen = true;
		_device.BlendState = BlendState.Opaque;
		_device.DepthStencilState = DepthStencilState.Default;
		_device.RasterizerState = RasterizerState.CullNone;
		_device.SamplerStates[0] = SamplerState.LinearWrap;
		_device.ScissorRectangle = _device.Viewport.Bounds;
	}

	private static int PrimitiveCountToTriangles(PrimitiveType primitiveType, int primitiveCount)
	{
		return primitiveType switch
		{
			PrimitiveType.TriangleList => primitiveCount,
			PrimitiveType.TriangleStrip => Math.Max(0, primitiveCount - 2),
			_ => 0,
		};
	}

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
		_spriteBatch.Dispose();
		_fallbackTexture.Dispose();
	}
}
