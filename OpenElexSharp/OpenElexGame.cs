using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenElexSharp;

/// <summary>
/// The MonoGame application. For now it only opens a window, clears it and handles the
/// smoke-test exit; world loading and rendering are added by later tasks.
/// </summary>
public sealed class OpenElexGame : Game
{
    private const int SmokeTestFrameCount = 10;

    private readonly GraphicsDeviceManager _graphics;
    private readonly LaunchOptions _options;
    private int _updateCount;

    public OpenElexGame(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
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

        base.Draw(gameTime);
    }
}
