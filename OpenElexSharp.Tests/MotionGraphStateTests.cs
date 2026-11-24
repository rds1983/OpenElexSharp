using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionGraphStateTests
{
	[Fact]
	public void TryRead_MapsRecordsToStateNames()
	{
		var data = CreateStates(2);

		var parsed = MotionGraphState.TryRead(data, 0, ["Idle", "Walk"], out var states);

		Assert.True(parsed);
		Assert.Equal(2, states.Length);
		Assert.Equal("Idle", states[0].Name);
		Assert.Equal("Walk", states[1].Name);
		Assert.Equal(MotionGraphState.RecordCount, states[0].Records.Count);
		Assert.Equal(0ul, states[0].Records[0][0]);
		Assert.Equal(100ul, states[1].Records[0][0]);
		Assert.Contains(100u, states[1].References);
		var tracks = new[]
		{
			new MotionSampleTrack(0, 16, 1, 1f, 60f, 100, 100, 0,
				[new MotionSample(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity)]),
		};
		Assert.Single(states[1].FindSampleTracks(tracks));
	}

	[Fact]
	public void TryRead_InvalidSecondWord_ReturnsFalse()
	{
		var data = CreateStates(1);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);

		Assert.False(MotionGraphState.TryRead(data, 0, ["Idle"], out _));
	}

	[Fact]
	public void TryRead_Truncated_ReturnsFalse()
	{
		Assert.False(MotionGraphState.TryRead(new byte[8], 0, ["Idle"], out _));
	}

	[Fact]
	public void TryRead_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => MotionGraphState.TryRead(null!, 0, ["Idle"], out _));
		Assert.Throws<ArgumentNullException>(
			() => MotionGraphState.TryRead([], 0, null!, out _));
	}

	private static byte[] CreateStates(int count)
	{
		var data = new byte[count * MotionGraphState.StateSize];
		for (var state = 0; state < count; state++)
		{
			var group = state * MotionGraphState.StateSize;
			for (var record = 0; record < MotionGraphState.RecordCount; record++)
			{
				BinaryPrimitives.WriteUInt64LittleEndian(
					data.AsSpan(group + record * MotionGraphState.RecordSize),
					(ulong)(state * 100 + record));
			}
		}

		return data;
	}
}
