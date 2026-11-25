using System.Diagnostics;

namespace OpenElexSharp.Diagnostics;

/// <summary>Managed and process memory values sampled for the debug overlay.</summary>
/// <param name="ManagedBytes">Bytes currently allocated on the managed heap.</param>
/// <param name="WorkingSetBytes">Resident working set of the process.</param>
public readonly record struct MemorySnapshot(long ManagedBytes, long WorkingSetBytes)
{
	private static readonly Process CurrentProcess = Process.GetCurrentProcess();

	/// <summary>Reads the current process values.</summary>
	public static MemorySnapshot Capture() =>
		new(GC.GetTotalMemory(forceFullCollection: false), CurrentProcess.WorkingSet64);

	/// <summary>
	/// Reads the live memory values after a full collection. Intended for soak diagnostics, where
	/// a stable baseline matters more than the cost of a collection.
	/// </summary>
	public static MemorySnapshot CaptureCollected()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		return Capture();
	}
}

/// <summary>Debug-overlay values for one statistics interval.</summary>
/// <param name="FramesPerSecond">Frames counted over the last FPS window.</param>
/// <param name="UpdateMilliseconds">Average update time over the recent sample window.</param>
/// <param name="DrawMilliseconds">Average draw time over the recent sample window.</param>
/// <param name="Memory">Memory sampled when the snapshot was taken.</param>
public readonly record struct FrameSnapshot(
	int FramesPerSecond,
	double UpdateMilliseconds,
	double DrawMilliseconds,
	MemorySnapshot Memory);

/// <summary>
/// Accumulates frame, update and draw timings for the overlay without requiring a graphics
/// device. Averages use a fixed window so a slow frame does not dominate the display; FPS is
/// counted over a half-second period.
/// </summary>
public sealed class FrameStatistics
{
	/// <summary>Number of frames behind the update/draw averages.</summary>
	public const int SampleWindow = 120;

	/// <summary>Period used to count frames per second.</summary>
	public const double FpsWindowSeconds = 0.5;

	private readonly Queue<double> _updates = new();
	private readonly Queue<double> _draws = new();
	private readonly Func<MemorySnapshot> _memory;

	private double _updateSum;
	private double _drawSum;
	private double _fpsElapsed;
	private int _fpsFrames;
	private int _fps;

	/// <summary>Creates statistics with an optional memory source for tests.</summary>
	/// <param name="memory">Memory provider; defaults to <see cref="MemorySnapshot.Capture"/>.</param>
	public FrameStatistics(Func<MemorySnapshot>? memory = null)
	{
		_memory = memory ?? MemorySnapshot.Capture;
	}

	/// <summary>
	/// Adds one frame. Update and draw times are always sampled; a non-positive elapsed time does
	/// not count toward FPS.
	/// </summary>
	/// <param name="elapsed">Wall-clock time since the previous frame.</param>
	/// <param name="updateMilliseconds">Time spent in the update pass.</param>
	/// <param name="drawMilliseconds">Time spent in the draw pass.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Either timing value is negative.
	/// </exception>
	public void AddFrame(TimeSpan elapsed, double updateMilliseconds, double drawMilliseconds)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(updateMilliseconds);
		ArgumentOutOfRangeException.ThrowIfNegative(drawMilliseconds);

		AddSample(_updates, ref _updateSum, updateMilliseconds);
		AddSample(_draws, ref _drawSum, drawMilliseconds);

		if (elapsed <= TimeSpan.Zero)
		{
			return;
		}

		_fpsElapsed += elapsed.TotalSeconds;
		++_fpsFrames;
		if (_fpsElapsed >= FpsWindowSeconds)
		{
			_fps = (int)Math.Round(_fpsFrames / _fpsElapsed);
			_fpsElapsed = 0;
			_fpsFrames = 0;
		}
	}

	/// <summary>Returns the current overlay values and samples memory.</summary>
	public FrameSnapshot Snapshot() =>
		new(
			CurrentFramesPerSecond,
			Average(_updates, _updateSum),
			Average(_draws, _drawSum),
			_memory());

	/// <summary>
	/// FPS of the window in progress until a full window has elapsed, then the last completed
	/// window. This keeps the overlay useful during the first half second.
	/// </summary>
	private int CurrentFramesPerSecond =>
		_fpsFrames > 0 && _fpsElapsed > 0
			? (int)Math.Round(_fpsFrames / _fpsElapsed)
			: _fps;

	private static void AddSample(Queue<double> samples, ref double sum, double value)
	{
		if (samples.Count == SampleWindow)
		{
			sum -= samples.Dequeue();
		}

		samples.Enqueue(value);
		sum += value;
	}

	private static double Average(Queue<double> samples, double sum) =>
		samples.Count == 0 ? 0 : sum / samples.Count;
}
