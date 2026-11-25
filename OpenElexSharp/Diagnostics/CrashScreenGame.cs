using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace OpenElexSharp.Diagnostics;

/// <summary>
/// Minimal window shown after a fatal error. It deliberately owns no game content: it only needs
/// a graphics device, Myra's embedded font and a scrollable text block, so it can still appear
/// when the main game failed during loading.
/// </summary>
internal sealed class CrashScreenGame : Game
{
	private readonly string _report;
	private readonly string? _capturePath;
	private readonly TimeSpan? _autoCloseAfter;
	private Desktop? _desktop;
	private TimeSpan _elapsed;
	private int _frameCount;
	private bool _captured;

	public CrashScreenGame(string report, TimeSpan? autoCloseAfter = null, string? capturePath = null)
	{
		ArgumentNullException.ThrowIfNull(report);

		_report = report;
		_autoCloseAfter = autoCloseAfter;
		_capturePath = capturePath;
		new GraphicsDeviceManager(this)
		{
			PreferredBackBufferWidth = 960,
			PreferredBackBufferHeight = 640,
		};

		Content.RootDirectory = "Content";
		IsMouseVisible = true;
		Window.AllowUserResizing = true;
		Window.Title = "OpenElexSharp - crashed";
	}

	protected override void LoadContent()
	{
		MyraEnvironment.Game = this;

		var label = new Label
		{
			Text = _report + Environment.NewLine + Environment.NewLine + "Press Escape to close.",
			Wrap = true,
			TextColor = Color.White,
			Margin = new Thickness(16),
		};
		_desktop = new Desktop
		{
			Root = new ScrollViewer
			{
				Content = label,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				ShowHorizontalScrollBar = false,
			},
		};
	}

	protected override void Update(GameTime gameTime)
	{
		_elapsed += gameTime.ElapsedGameTime;
		if (Keyboard.GetState().IsKeyDown(Keys.Escape) ||
			(_autoCloseAfter is { } limit && _elapsed >= limit))
		{
			Exit();
		}

		base.Update(gameTime);
	}

	protected override void Draw(GameTime gameTime)
	{
		++_frameCount;
		GraphicsDevice.Clear(new Color(28, 18, 18));
		_desktop?.Render();
		CaptureIfRequested();
		base.Draw(gameTime);
	}

	private void CaptureIfRequested()
	{
		if (_captured || string.IsNullOrEmpty(_capturePath) || _frameCount < 4)
		{
			return;
		}

		_captured = true;
		try
		{
			FrameCapture.Save(GraphicsDevice, _capturePath);
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or NotSupportedException or
				ArgumentException)
		{
			Log.Error($"Crash-screen capture failed for '{_capturePath}'.", exception);
		}
	}

	protected override void UnloadContent()
	{
		_desktop = null;
		base.UnloadContent();
	}
}
