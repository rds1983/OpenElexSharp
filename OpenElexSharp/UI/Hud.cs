using Myra.Graphics2D.UI;
using OpenElexSharp.Diagnostics;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.UI;

/// <summary>
/// HUD root: render statistics in the top-left and the current controls in the bottom-right.
/// The widget tree is generated from <c>Hud.xmmp</c>, <c>DebugOverlay.xmmp</c> and
/// <c>Controls.xmmp</c>; this partial owns only layout and update logic.
/// </summary>
public partial class Hud
{
	private const int ScreenEdge = 8;

	private readonly DebugOverlay _debugOverlay;
	private readonly Controls _controls;

	public Hud()
	{
		BuildUI();

		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;

		_debugOverlay = new DebugOverlay
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Left = ScreenEdge,
			Top = ScreenEdge,
		};

		_controls = new Controls
		{
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Left = -ScreenEdge,
			Top = -ScreenEdge,
		};

		Widgets.Add(_debugOverlay);
		Widgets.Add(_controls);
	}

	/// <summary>The stats block.</summary>
	public DebugOverlay DebugOverlay => _debugOverlay;

	/// <summary>Updates the HUD from the current frame statistics and renderer counters.</summary>
	public void Update(
		Renderer renderer,
		FrameSnapshot snapshot,
		MeshViewerStatus? viewer,
		SceneStatus? scene) =>
		_debugOverlay.Update(renderer, snapshot, viewer, scene);

	/// <summary>Shows or hides the stats block and returns the new visibility.</summary>
	public bool ToggleDebugOverlay()
	{
		_debugOverlay.Visible = !_debugOverlay.Visible;
		return _debugOverlay.Visible;
	}
}
