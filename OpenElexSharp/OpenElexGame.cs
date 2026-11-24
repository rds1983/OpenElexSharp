using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using OpenElexSharp.Assets;
using OpenElexSharp.Graphics;
using OpenElexSharp.UI;

namespace OpenElexSharp;

/// <summary>
/// The MonoGame application. For now it opens a window and displays one game texture or one
/// static mesh in a test scene. World loading and gameplay systems are added by later tasks.
/// </summary>
public sealed class OpenElexGame : Game
{
    private const int SmokeTestFrameCount = 10;
    private const int TextureMargin = 24;

    private readonly GraphicsDeviceManager _graphics;
    private readonly LaunchOptions _options;
    private int _updateCount;
    private Renderer? _renderer;
    private Desktop? _desktop;
    private Hud? _hud;
    private Texture2D? _texture;
    private Texture2D? _fallback;
    private DrMeshPart? _meshPart;
    private KeyboardState _previousKeyboard;

    public OpenElexGame(LaunchOptions options, AssetFileSystem assets)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assets);

        _options = options;
        Assets = assets;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            GraphicsProfile = GraphicsProfile.HiDef,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "OpenElexSharp";
        Window.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Keys.Escape)
            {
                Exit();
            }
        };
    }

    /// <summary>The mounted game assets.</summary>
    public AssetFileSystem Assets { get; }

    protected override void LoadContent()
    {
        _renderer = new Renderer(GraphicsDevice);
        MyraEnvironment.Game = this;
        _desktop = new Desktop();
        _hud = new Hud();
        _desktop.Root = _hud;
        if (!string.IsNullOrWhiteSpace(_options.Mesh))
        {
            try
            {
                _meshPart = LoadMesh(_options.Mesh);
            }
            catch (Exception exception) when (
                exception is GmshFormatException or NotSupportedException or IOException or
                    UnauthorizedAccessException or AssetNotFoundException or InvalidOperationException or
                    ArgumentException)
            {
                Console.Error.WriteLine($"Mesh warning: {exception.Message}");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Texture))
        {
            return;
        }

        try
        {
            _texture = LoadTexture(_options.Texture);
        }
        catch (Exception exception) when (
            exception is DdsFormatException or NotSupportedException or IOException or
                UnauthorizedAccessException or AssetNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Texture warning: {exception.Message}");
            _fallback = TextureLoader.CreateFallback(GraphicsDevice);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape) && _previousKeyboard.IsKeyUp(Keys.Escape))
        {
            Exit();
        }

        _previousKeyboard = keyboard;

        if (_options.SmokeTest && ++_updateCount >= SmokeTestFrameCount)
        {
            if (_renderer is not null)
            {
                Console.WriteLine(
                    $"Render statistics: drawCalls={_renderer.DrawCalls} " +
                    $"triangles={_renderer.Triangles} meshes={_renderer.Meshes}");
            }

            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_renderer is null)
        {
            return;
        }

        if (_meshPart is not null)
        {
            DrawMesh(gameTime);
        }
        else
        {
            DrawTexture();
        }

        _hud?.Update(gameTime, _renderer);
        _desktop?.Render();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _renderer?.Dispose();
        _renderer = null;
        if (!ReferenceEquals(_texture, _fallback))
        {
            _texture?.Dispose();
        }

        _texture = null;
        _fallback?.Dispose();
        _fallback = null;
        _meshPart?.Dispose();
        _meshPart = null;
        _desktop = null;
        _hud = null;
        base.UnloadContent();
    }

    private void DrawMesh(GameTime gameTime)
    {
        var bounds = (_meshPart!.Tag as DrMeshPartTag)?.Bounds ?? new BoundingSphere(Vector3.Zero, 1f);
        var radius = Math.Max(0.001f, bounds.Radius);
        var time = (float)gameTime.TotalGameTime.TotalSeconds;
        var camera = bounds.Center + new Vector3(
            MathF.Cos(time * 0.35f) * radius * 2.2f,
            radius * 1.1f,
            MathF.Sin(time * 0.35f) * radius * 2.2f);
        var view = Matrix.CreateLookAt(camera, bounds.Center, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4,
            GraphicsDevice.Viewport.AspectRatio,
            radius * 0.001f,
            radius * 20f);

        _renderer!.BeginFrame(view, projection);
        _renderer.Clear(new Color(16, 18, 24));
        _renderer.DrawMeshPart(_meshPart, Matrix.Identity);
        _renderer.EndFrame();
    }

    private void DrawTexture()
    {
        var texture = _texture ?? _fallback;
        _renderer!.BeginFrame();
        _renderer.Clear(new Color(16, 18, 24));
        if (texture is not null)
        {
            var viewport = GraphicsDevice.Viewport;
            var availableWidth = Math.Max(1, viewport.Width - TextureMargin * 2);
            var availableHeight = Math.Max(1, viewport.Height - TextureMargin * 2);
            var scale = Math.Min(
                availableWidth / (float)texture.Width,
                availableHeight / (float)texture.Height);
            var width = Math.Max(1, (int)MathF.Round(texture.Width * scale));
            var height = Math.Max(1, (int)MathF.Round(texture.Height * scale));
            var destination = new Rectangle(
                (viewport.Width - width) / 2,
                (viewport.Height - height) / 2,
                width,
                height);
            _renderer.DrawTexture(texture, destination);
        }

        _renderer.EndFrame();
    }

    private DrMeshPart LoadMesh(string query)
    {
        var resource = FindResource(query, "msh");
        using var stream = Assets.OpenRead(resource.VirtualPath);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var mesh = GmshMesh.Parse(resource.Name, resource.VirtualPath, buffer.ToArray());
        var result = MeshLoader.Load(GraphicsDevice, mesh);
        Console.WriteLine(
            $"Mesh: {resource.Name} vertices={mesh.Positions.Count} triangles={mesh.TriangleCount} " +
            $"attributes={mesh.VertexAttributes.Count} streams={mesh.VertexStreams.Count} " +
            $"materials={mesh.MaterialReferences.Count}");
        return result;
    }

    private ElexResource FindResource(string query, string typeToken)
    {
        var catalog = AssetCatalog.Build(Assets);
        var resources = ElexResourceIndex.Build(catalog, Assets);
        return resources.Resources
            .Where(candidate => string.Equals(candidate.TypeToken, typeToken, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase) ||
                candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            ?? throw new AssetNotFoundException($"No {typeToken} resource matches '{query}'.");
    }

    private Texture2D LoadTexture(string query)
    {
        var resource = FindResource(query, "img");
        using var stream = Assets.OpenRead(resource.VirtualPath);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var image = DdsImage.Parse(buffer.ToArray());
        var normalMap = resource.Name.Contains("_Nm", StringComparison.OrdinalIgnoreCase);
        var texture = TextureLoader.Load(
            GraphicsDevice,
            image,
            new DdsDecodeOptions(NormalMap: normalMap));
        Console.WriteLine(
            $"Texture: {resource.Name} {image.Width}x{image.Height} {image.FormatName} " +
            $"mips={image.MipCount} normalMap={normalMap}");
        return texture;
    }
}
