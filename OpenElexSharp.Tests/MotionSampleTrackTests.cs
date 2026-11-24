using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionSampleTrackTests
{
	[Fact]
	public void FindAll_DecodesHeaderAndSamples()
	{
		var data = CreateTrack();
		var tracks = MotionSampleTrack.FindAll(data, 0, data.Length);

		var track = Assert.Single(tracks);
		Assert.Equal(0, track.HeaderOffset);
		Assert.Equal(32, track.ByteSize);
		Assert.Equal(2, track.SampleCount);
		Assert.Equal(2f, track.Duration);
		Assert.Equal(60f, track.SampleRate);
		Assert.Equal(5, track.TrackIndex);
		Assert.Equal(7, track.BoneIndex);
		Assert.Equal(80, track.SampleOffset);
		Assert.Equal(1000f, track.Samples[0].RawPosition.X, 5);
		Assert.Equal(-3000f, track.Samples[0].RawPosition.Z, 5);
		Assert.Equal(1f, track.Samples[0].Rotation.W, 5);
		Assert.Equal(-1000f, track.Samples[1].RawPosition.X, 5);
		Assert.Equal(1000f / 32767f * 100f, track.Samples[0].GetPosition(100f).X, 5);
		var names = Enumerable.Range(0, 8).Select(index => $"n{index}").ToArray();
		Assert.Equal("n5", track.GetTrackName(names));
		Assert.Equal("n7", track.GetBoneName(names));
	}

	[Fact]
	public void SampleAt_InterpolatesSamplesAndClamps()
	{
		var track = Assert.Single(MotionSampleTrack.FindAll(CreateTrack(), 0, 112));

		var first = track.SampleAt(0f);
		Assert.Equal(1000f, first.RawPosition.X, 5);
		Assert.Equal(2000f, first.RawPosition.Y, 5);
		var middle = track.SampleAt(1f);
		Assert.Equal(0f, middle.RawPosition.X, 5);
		Assert.Equal(1000f, middle.RawPosition.Y, 5);
		var last = track.SampleAt(10f);
		Assert.Equal(-1000f, last.RawPosition.X, 5);
		Assert.Equal(1f, last.Rotation.W, 5);
	}

	[Fact]
	public void FindAll_InvalidSampleOffset_IsIgnored()
	{
		var data = CreateTrack();
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(64), 0x1000u);

		Assert.Empty(MotionSampleTrack.FindAll(data, 0, data.Length));
	}

	[Fact]
	public void FindAll_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MotionSampleTrack.FindAll(null!, 0, 0));
	}

	private static byte[] CreateTrack()
	{
		var data = new byte[80 + 32];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 32);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 16);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), 2f);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(12), 60f);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 5);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 7);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(64), 80);

		WriteSample(data, 80, 1000, 2000, -3000);
		WriteSample(data, 96, -1000, 0, 1000);
		return data;
	}

	private static void WriteSample(byte[] data, int offset, short x, short y, short z)
	{
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), x);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 2), y);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 4), z);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 6), 0);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 8), 0);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 10), 0);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 12), short.MaxValue);
	}
}
