using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Tests;

public class FrameStatisticsTests
{
	private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1d / 60d);

	[Fact]
	public void Snapshot_BeforeFrames_ReturnsZeroesAndMemory()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(1024, 4096));

		var snapshot = statistics.Snapshot();

		Assert.Equal(0, snapshot.FramesPerSecond);
		Assert.Equal(0, snapshot.UpdateMilliseconds);
		Assert.Equal(0, snapshot.DrawMilliseconds);
		Assert.Equal(1024, snapshot.Memory.ManagedBytes);
		Assert.Equal(4096, snapshot.Memory.WorkingSetBytes);
	}

	[Fact]
	public void AddFrame_AveragesRecentSamples()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(0, 0));
		for (var index = 0; index < FrameStatistics.SampleWindow; index++)
		{
			statistics.AddFrame(FrameTime, 2, 4);
		}

		var snapshot = statistics.Snapshot();

		Assert.Equal(2, snapshot.UpdateMilliseconds, precision: 6);
		Assert.Equal(4, snapshot.DrawMilliseconds, precision: 6);
	}

	[Fact]
	public void AddFrame_DropsSamplesOutsideTheWindow()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(0, 0));
		for (var index = 0; index < FrameStatistics.SampleWindow; index++)
		{
			statistics.AddFrame(FrameTime, 1, 1);
		}

		statistics.AddFrame(FrameTime, 1000, 1000);
		var snapshot = statistics.Snapshot();

		var expected = (FrameStatistics.SampleWindow - 1 + 1000d) / FrameStatistics.SampleWindow;
		Assert.Equal(expected, snapshot.UpdateMilliseconds, precision: 6);
		Assert.Equal(expected, snapshot.DrawMilliseconds, precision: 6);
	}

	[Fact]
	public void AddFrame_CountsFramesPerSecond()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(0, 0));
		for (var index = 0; index < 31; index++)
		{
			statistics.AddFrame(FrameTime, 1, 1);
		}

		var snapshot = statistics.Snapshot();

		Assert.InRange(snapshot.FramesPerSecond, 59, 61);
	}

	[Fact]
	public void Snapshot_ReportsFpsFromPartialWindow()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(0, 0));
		statistics.AddFrame(FrameTime, 1, 1);

		var snapshot = statistics.Snapshot();

		Assert.InRange(snapshot.FramesPerSecond, 59, 61);
	}

	[Fact]
	public void AddFrame_NonPositiveElapsed_DoesNotCountFps()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(0, 0));

		statistics.AddFrame(TimeSpan.Zero, 1, 1);
		var snapshot = statistics.Snapshot();

		Assert.Equal(0, snapshot.FramesPerSecond);
		Assert.Equal(1, snapshot.UpdateMilliseconds, precision: 6);
	}

	[Fact]
	public void AddFrame_RejectsNegativeTimings()
	{
		var statistics = new FrameStatistics(() => new MemorySnapshot(0, 0));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => statistics.AddFrame(FrameTime, -1, 1));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => statistics.AddFrame(FrameTime, 1, -1));
	}
}
