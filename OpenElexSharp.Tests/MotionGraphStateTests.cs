using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionGraphStateTests
{
	[Fact]
	public void TryRead_MapsStatesAndNames()
	{
		var data = CreateBlock(2, out var size);

		var parsed = MotionGraphState.TryRead(data, 0, data.Length, ["Idle", "Walk"], out var states, out var readSize);

		Assert.True(parsed);
		Assert.Equal(size, readSize);
		Assert.Equal(2, states.Length);
		Assert.Equal(0, states[0].BlockOffset);
		Assert.Equal(0, states[0].Index);
		Assert.Equal("Idle", states[0].Name);
		Assert.Equal("Walk", states[1].Name);
		Assert.Equal(MotionGraphState.RecordCount, states[0].Values.Count);
		Assert.Equal(0u, states[0].Values[0][0]);
		Assert.Equal(100u, states[1].Values[0][0]);
		Assert.Contains(100u, states[1].References);
	}

	[Fact]
	public void TryRead_NoNames_LeavesNameNull()
	{
		var data = CreateBlock(1, out _);

		Assert.True(MotionGraphState.TryRead(data, 0, data.Length, null, out var states, out _));
		Assert.Null(Assert.Single(states).Name);
	}

	[Fact]
	public void TryReadHeader_RejectsMisalignedHeader()
	{
		var data = CreateBlock(2, out _);

		Assert.False(MotionGraphState.TryReadHeader(data, 8, data.Length, out _, out _));
		Assert.False(MotionGraphState.TryRead(data, 8, data.Length, null, out _, out _));
	}

	[Fact]
	public void TryReadHeader_RejectsBadRecordCount()
	{
		var data = CreateBlock(1, out _);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), 15);

		Assert.False(MotionGraphState.TryReadHeader(data, 0, data.Length, out _, out _));
	}

	[Fact]
	public void TryReadHeader_RejectsOverlappingStateOffsets()
	{
		var data = CreateBlock(2, out _);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), 32);

		Assert.False(MotionGraphState.TryReadHeader(data, 0, data.Length, out _, out _));
	}

	[Fact]
	public void TryRead_Truncated_ReturnsFalse()
	{
		var data = CreateBlock(2, out _);

		Assert.False(MotionGraphState.TryRead(data, 0, data.Length - 1, null, out _, out _));
		Assert.False(MotionGraphState.TryRead(new byte[8], 0, 8, null, out _, out _));
	}

	[Fact]
	public void TryRead_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => MotionGraphState.TryRead(null!, 0, 0, null, out _, out _));
		Assert.Throws<ArgumentNullException>(
			() => MotionGraphState.TryReadHeader(null!, 0, 0, out _, out _));
	}

	private static byte[] CreateBlock(int stateCount, out int size)
	{
		var headerSize = MotionGraphState.HeaderBaseSize + stateCount * 8;
		size = headerSize + stateCount * MotionGraphState.StateSize;
		var data = new byte[size];
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), (uint)stateCount);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), MotionGraphState.Marker);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), MotionGraphState.RecordCount);
		for (var state = 0; state < stateCount; state++)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(
				data.AsSpan(MotionGraphState.HeaderBaseSize + state * 8),
				(ulong)(headerSize + state * MotionGraphState.StateSize));
			var stateStart = headerSize + state * MotionGraphState.StateSize;
			for (var record = 0; record < MotionGraphState.RecordCount; record++)
			{
				for (var value = 0; value < MotionGraphState.ValuesPerRecord; value++)
				{
					BinaryPrimitives.WriteUInt64LittleEndian(
						data.AsSpan(stateStart + record * MotionGraphState.RecordSize + value * 8),
						(ulong)(state * 100 + record * 5 + value));
				}
			}
		}

		return data;
	}
}
