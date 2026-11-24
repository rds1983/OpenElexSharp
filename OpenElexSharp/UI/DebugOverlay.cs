using System.Globalization;
using Microsoft.Xna.Framework;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.UI;

public partial class DebugOverlay
{
    private const float FpsInterval = 0.5f;

    private float _elapsed;
    private int _frames;
    private int _fps;

    private static readonly NumberFormatInfo NumberFormat = new()
    {
        NumberGroupSeparator = ",",
        NumberGroupSizes = [3],
    };

    public DebugOverlay()
    {
        BuildUI();
    }

    /// <summary>Updates the statistics labels from the renderer's current-frame counters.</summary>
    public void Update(GameTime gameTime, Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        ++_frames;
        if (_elapsed >= FpsInterval)
        {
            _fps = (int)(_frames / _elapsed);
            _elapsed = 0;
            _frames = 0;
        }

        _labelFps.Text = Format("FPS: {0}", _fps);
        _labelDrawCalls.Text = Format("Draw calls: {0:N0}", renderer.DrawCalls);
        _labelTriangles.Text = Format("Triangles: {0:N0}", renderer.Triangles);
        _labelMeshes.Text = Format("Meshes: {0:N0}", renderer.Meshes);
    }

    private static string Format(string format, params object[] args) =>
        string.Format(NumberFormat, format, args);
}
