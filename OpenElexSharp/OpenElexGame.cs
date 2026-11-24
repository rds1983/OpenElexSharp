using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using OpenElexSharp.Graphics;

namespace OpenElexSharp;

/// <summary>
/// The MonoGame application. For now it opens a window, clears it and, when requested, draws a
/// single game texture to exercise the DDS loader. World loading and rendering are added by later tasks.
/// </summary>
public sealed class OpenElexGame : Game
{
    private const int SmokeTestFrameCount = 10;
    private const int TextureMargin = 24;

    private readonly GraphicsDeviceManager _graphics;
    private readonly LaunchOptions _options;
    private int _updateCount;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _texture;
    private Texture2D? _fallback;

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
    }

    /// <summary>The mounted game assets.</summary>
    public AssetFileSystem Assets { get; }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
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
        if (_options.SmokeTest && ++_updateCount >= SmokeTestFrameCount)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(16, 18, 24));

        var texture = _texture ?? _fallback;
        if (texture is not null && _spriteBatch is not null)
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

            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            _spriteBatch.Draw(texture, destination, Color.White);
            _spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _spriteBatch?.Dispose();
        _spriteBatch = null;
        if (!ReferenceEquals(_texture, _fallback))
        {
            _texture?.Dispose();
        }

        _texture = null;
        _fallback?.Dispose();
        _fallback = null;
        base.UnloadContent();
    }

    private Texture2D LoadTexture(string query)
    {
        var catalog = AssetCatalog.Build(Assets);
        var resources = ElexResourceIndex.Build(catalog, Assets);
        var resource = resources.Resources
            .Where(candidate => string.Equals(candidate.TypeToken, "img", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase) ||
                candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            ?? throw new AssetNotFoundException($"No image resource matches '{query}'.");

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
