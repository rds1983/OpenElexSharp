using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenElexSharp.Diagnostics;

/// <summary>Writes the current back buffer to a PNG, shared by the game and the crash screen.</summary>
internal static class FrameCapture
{
	/// <summary>Reads the back buffer and saves it as a PNG.</summary>
	/// <exception cref="IOException">The destination cannot be written.</exception>
	/// <exception cref="UnauthorizedAccessException">The destination cannot be written.</exception>
	public static void Save(GraphicsDevice device, string path)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var presentation = device.PresentationParameters;
		var width = Math.Max(1, presentation.BackBufferWidth);
		var height = Math.Max(1, presentation.BackBufferHeight);
		var pixels = new Color[width * height];
		device.GetBackBufferData(pixels);
		using var texture = new Texture2D(device, width, height);
		texture.SetData(pixels);
		using var stream = File.Create(path);
		texture.SaveAsPng(stream, width, height);
		Log.Info($"Captured {width}x{height} -> {path}");
	}
}
