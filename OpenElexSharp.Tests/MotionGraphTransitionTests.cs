using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionGraphTransitionTests
{
	[Fact]
	public void TryRead_ReadsTimedRecord()
	{
		var data = CreateRecord(7, 0, 1, 852);

		Assert.True(MotionGraphTransition.TryRead(data, 0, data.Length, out var transition));
		Assert.Equal(7, transition.Index);
		Assert.Equal(0, transition.Source);
		Assert.Equal(1, transition.Target);
		Assert.Equal(852, transition.DurationMilliseconds);
		Assert.Equal(0.852f, transition.DurationSeconds);
	}

	[Fact]
	public void TryRead_RejectsNonZeroPadding()
	{
		var data = CreateRecord(7, 0, 1, 852);
		data[20] = 1;

		Assert.False(MotionGraphTransition.TryRead(data, 0, data.Length, out _));
	}

	[Fact]
	public void TryRead_RejectsUnknownKind()
	{
		var data = CreateRecord(7, 0, 1, 852);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 3);

		Assert.False(MotionGraphTransition.TryRead(data, 0, data.Length, out _));
	}

	[Fact]
	public void FindAll_ReadsConsecutiveRun()
	{
		var data = new byte[MotionGraphTransition.RecordSize * 4];
		WriteRecord(data, 0, 7, 0, 1, 852);
		WriteRecord(data, MotionGraphTransition.RecordSize, 8, 1, 0, 808);
		WriteRecord(data, MotionGraphTransition.RecordSize * 2, 9, 1, 4, 764);
		data[MotionGraphTransition.RecordSize * 3] = 0xff;

		var transitions = MotionGraphTransition.FindAll(data, 0, data.Length);

		Assert.Equal(3, transitions.Count);
		Assert.Equal([7, 8, 9], transitions.Select(transition => transition.Index));
		Assert.Equal([852, 808, 764], transitions.Select(transition => transition.DurationMilliseconds));
	}

	[Fact]
	public void FindAll_StopsAtBrokenIndexSequence()
	{
		var data = new byte[MotionGraphTransition.RecordSize * 3];
		WriteRecord(data, 0, 7, 0, 1, 852);
		WriteRecord(data, MotionGraphTransition.RecordSize, 10, 1, 0, 808);
		WriteRecord(data, MotionGraphTransition.RecordSize * 2, 11, 0, 1, 764);

		var transitions = MotionGraphTransition.FindAll(data, 0, data.Length);

		Assert.Equal(3, transitions.Count);
		Assert.Equal(10, transitions[1].Index);
	}

	[Fact]
	public void FindAll_IgnoresIndicesBelowFour()
	{
		var data = CreateRecord(3, 0, 1, 116);

		Assert.Empty(MotionGraphTransition.FindAll(data, 0, data.Length));
	}

	[Fact]
	public void FindAll_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MotionGraphTransition.FindAll(null!, 0, 0));
		Assert.Throws<ArgumentNullException>(() => MotionGraphTransition.TryRead(null!, 0, 0, out _));
	}

	private static byte[] CreateRecord(int index, int source, int target, int duration)
	{
		var data = new byte[MotionGraphTransition.RecordSize];
		WriteRecord(data, 0, index, source, target, duration);
		return data;
	}

	private static void WriteRecord(
		byte[] data,
		int offset,
		int index,
		int source,
		int target,
		int duration)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 10), (ushort)index);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 12), (ushort)source);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 14), (ushort)target);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 16), (ushort)duration);
	}
}
