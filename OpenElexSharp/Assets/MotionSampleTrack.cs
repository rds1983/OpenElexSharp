using System.Buffers.Binary;
using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>One quantized animation sample: raw position components and a normalized quaternion.</summary>
/// <param name="RawPosition">Three raw <c>int16</c> position components.</param>
/// <param name="Rotation">Normalized rotation decoded from four <c>int16</c> components.</param>
public sealed record MotionSample(Vector3 RawPosition, Quaternion Rotation);

/// <summary>
/// A sample track found in a <c>net</c> payload. The observed header stores a byte size, a 16-byte
/// stride, duration, 60 Hz rate, a track/bone index and a relative sample offset.
/// </summary>
/// <param name="HeaderOffset">Absolute resource offset of the header.</param>
/// <param name="ByteSize">Sample data byte size.</param>
/// <param name="SampleCount">Number of 16-byte samples.</param>
/// <param name="Duration">Track duration in seconds.</param>
/// <param name="SampleRate">Samples per second (observed 60).</param>
/// <param name="TrackIndex">First header index field.</param>
/// <param name="BoneIndex">Second header index field.</param>
/// <param name="SampleOffset">Sample data offset relative to the header.</param>
/// <param name="Samples">Decoded samples.</param>
public sealed record MotionSampleTrack(
	int HeaderOffset,
	int ByteSize,
	int SampleCount,
	float Duration,
	float SampleRate,
	int TrackIndex,
	int BoneIndex,
	int SampleOffset,
	IReadOnlyList<MotionSample> Samples)
{
	private const int HeaderSize = 68;
	private const int RecordSize = 16;
	private const int MaxTracks = 4096;
	private const int MaxSamples = 512;

	/// <summary>Samples the track at a time in seconds, clamping to the first/last sample.</summary>
	public MotionSample SampleAt(float seconds)
	{
		if (Samples.Count == 0)
		{
			return new MotionSample(Vector3.Zero, Quaternion.Identity);
		}

		var amount = Math.Clamp(seconds / Duration, 0f, 1f) * (Samples.Count - 1);
		var index = (int)MathF.Floor(amount);
		if (index >= Samples.Count - 1)
		{
			return Samples[^1];
		}

		var fraction = amount - index;
		var a = Samples[index];
		var b = Samples[index + 1];
		return new MotionSample(
			Vector3.Lerp(a.RawPosition, b.RawPosition, fraction),
			Quaternion.Slerp(a.Rotation, b.Rotation, fraction));
	}

	/// <summary>Scans a payload for sample tracks and decodes their 16-byte samples.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<MotionSampleTrack> FindAll(byte[] data, int start, int end)
	{
		ArgumentNullException.ThrowIfNull(data);
		var tracks = new List<MotionSampleTrack>();
		end = Math.Min(end, data.Length);
		for (var offset = Math.Max(0, start); offset + HeaderSize <= end && tracks.Count < MaxTracks; offset += 4)
		{
			var byteSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
			var stride = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
			var duration = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8));
			var rate = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 12));
			var trackIndex = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16));
			var boneIndex = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 20));
			var sampleOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 64));
			if (stride != RecordSize || byteSize <= 0 || byteSize % RecordSize != 0 ||
				byteSize / RecordSize > MaxSamples || !(duration > 0.001f) || duration > 120f ||
				rate < 59f || rate > 61f || sampleOffset < HeaderSize ||
				offset + sampleOffset + byteSize > end)
			{
				continue;
			}

			var sampleCount = byteSize / RecordSize;
			var samples = new MotionSample[sampleCount];
			var sampleStart = offset + sampleOffset;
			for (var index = 0; index < sampleCount; index++)
			{
				var record = sampleStart + index * RecordSize;
				var px = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record));
				var py = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record + 2));
				var pz = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record + 4));
				var x = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record + 6)) / 32767f;
				var y = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record + 8)) / 32767f;
				var z = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record + 10)) / 32767f;
				var w = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(record + 12)) / 32767f;
				var rotation = new Quaternion(x, y, z, w);
				rotation = rotation.LengthSquared() > 1e-6f
					? Quaternion.Normalize(rotation)
					: Quaternion.Identity;
				samples[index] = new MotionSample(new Vector3(px, py, pz), rotation);
			}

			tracks.Add(new MotionSampleTrack(
				offset, byteSize, sampleCount, duration, rate, trackIndex, boneIndex,
				sampleOffset, samples));
			offset += sampleOffset + byteSize - 4;
		}

		return tracks;
	}
}
