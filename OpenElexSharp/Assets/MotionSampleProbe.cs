using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>A run of 8-byte quantized quaternions (four <c>int16</c> values) in a <c>net</c> payload.</summary>
/// <param name="Offset">Absolute resource offset of the first quaternion.</param>
/// <param name="Count">Number of consecutive unit-length quaternions.</param>
public sealed record MotionQuaternionRun(int Offset, int Count);

/// <summary>
/// Diagnostic scanner for quantized animation samples in a <c>net</c> payload. The high-entropy
/// region contains runs of four <c>int16</c> values whose squared length is close to
/// <c>32767²</c>; these are candidate compressed quaternions.
/// </summary>
public static class MotionSampleProbe
{
	private const int RecordSize = 8;
	private const int MaxRuns = 10_000;
	private const float Target = 32767f * 32767f;

	/// <summary>Finds runs of at least four quantized quaternions.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<MotionQuaternionRun> FindQuaternionRuns(byte[] data, int start, int end)
	{
		ArgumentNullException.ThrowIfNull(data);
		var runs = new List<MotionQuaternionRun>();
		var position = Math.Max(0, start);
		end = Math.Min(end, data.Length);
		while (position + RecordSize <= end && runs.Count < MaxRuns)
		{
			if (!IsQuaternion(data, position, end))
			{
				position += 2;
				continue;
			}

			var runStart = position;
			var count = 0;
			while (position + RecordSize <= end && IsQuaternion(data, position, end))
			{
				count++;
				position += RecordSize;
			}

			if (count >= 4)
			{
				runs.Add(new MotionQuaternionRun(runStart, count));
			}
		}

		return runs;
	}

	private static bool IsQuaternion(byte[] data, int offset, int end)
	{
		if (offset + RecordSize > end)
		{
			return false;
		}

		long sum = 0;
		for (var index = 0; index < 4; index++)
		{
			var value = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + index * 2));
			sum += (long)value * value;
		}

		return sum >= Target * 0.5f && sum <= Target * 2f;
	}
}
