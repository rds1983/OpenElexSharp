using System.Globalization;
using OpenElexSharp.Diagnostics;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.UI;

public partial class DebugOverlay
{
	private const double BytesPerMegabyte = 1024 * 1024;

	private static readonly NumberFormatInfo NumberFormat = new()
	{
		NumberGroupSeparator = ",",
		NumberGroupSizes = [3],
	};

	public DebugOverlay()
	{
		BuildUI();
	}

	/// <summary>Updates the statistics labels from the current frame sample.</summary>
	/// <param name="renderer">Renderer providing the current-frame counters.</param>
	/// <param name="snapshot">Timing and memory values collected by <see cref="FrameStatistics"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
	public void Update(Renderer renderer, FrameSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(renderer);

		_labelFps.Text = Format("FPS: {0}", snapshot.FramesPerSecond);
		_labelUpdate.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Update: {0:0.000} ms",
			snapshot.UpdateMilliseconds);
		_labelDraw.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Draw: {0:0.000} ms",
			snapshot.DrawMilliseconds);
		_labelMemory.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Memory: {0:0.0} MB (managed {1:0.0} MB)",
			snapshot.Memory.WorkingSetBytes / BytesPerMegabyte,
			snapshot.Memory.ManagedBytes / BytesPerMegabyte);
		_labelDrawCalls.Text = Format("Draw calls: {0:N0}", renderer.DrawCalls);
		_labelTriangles.Text = Format("Triangles: {0:N0}", renderer.Triangles);
		_labelMeshes.Text = Format("Meshes: {0:N0}", renderer.Meshes);
	}

	private static string Format(string format, params object[] args) =>
		string.Format(NumberFormat, format, args);
}
