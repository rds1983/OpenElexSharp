using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionSampleProbeTests
{
	[Fact]
	public void FindQuaternionRuns_FindsConsecutiveQuantizedQuaternions()
	{
		var data = new byte[4 * 8];
		for (var index = 0; index < 4; index++)
		{
			BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 8), 0);
			BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 8 + 2), 0);
			BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 8 + 4), 0);
			BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 8 + 6), 32767);
		}

		var runs = MotionSampleProbe.FindQuaternionRuns(data, 0, data.Length);

		var run = Assert.Single(runs);
		Assert.Equal(0, run.Offset);
		Assert.Equal(4, run.Count);
	}

	[Fact]
	public void FindQuaternionRuns_IgnoresShortRunsAndZeros()
	{
		var data = new byte[3 * 8];

		Assert.Empty(MotionSampleProbe.FindQuaternionRuns(data, 0, data.Length));
	}

	[Fact]
	public void FindQuaternionRuns_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => MotionSampleProbe.FindQuaternionRuns(null!, 0, 0));
	}
}
