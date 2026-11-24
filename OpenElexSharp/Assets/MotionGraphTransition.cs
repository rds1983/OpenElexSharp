using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>
/// One timed transition record from a binary <c>net</c> state machine. Records are 48 bytes and are
/// stored as a run with consecutive indices.
/// </summary>
/// <param name="Offset">Absolute resource offset of the record.</param>
/// <param name="Index">Transition index inside the network.</param>
/// <param name="Source">Source state slot; <c>0</c> is the implicit disabled/entry state.</param>
/// <param name="Target">Target state slot.</param>
/// <param name="DurationMilliseconds">Blend duration in milliseconds; <c>0</c> means immediate.</param>
public sealed record MotionGraphTransition(
	int Offset,
	int Index,
	int Source,
	int Target,
	int DurationMilliseconds)
{
	/// <summary>Bytes per transition record.</summary>
	public const int RecordSize = 48;

	/// <summary>Largest transition index observed in retail data.</summary>
	public const int MaxIndex = 0x1000;

	/// <summary>Largest duration accepted by the detector.</summary>
	public const int MaxDurationMilliseconds = 30_000;

	/// <summary>Duration in seconds.</summary>
	public float DurationSeconds => DurationMilliseconds / 1000f;

	/// <summary>
	/// Reads one record at <paramref name="offset"/>. A record starts with the kind (<c>1</c> or
	/// <c>2</c>) and a flags word of <c>1</c>, then stores the transition index, source, target and
	/// duration as 16-bit values; the remaining bytes are zero padding.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static bool TryRead(
		byte[] data,
		int offset,
		int end,
		out MotionGraphTransition transition)
	{
		ArgumentNullException.ThrowIfNull(data);
		transition = null!;
		end = Math.Min(end, data.Length);
		if (offset < 0 || offset + RecordSize > end)
		{
			return false;
		}

		var kind = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
		var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
		if (kind is not (1 or 2) || flags != 1 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4)) != 0 ||
			BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 8)) != 0)
		{
			return false;
		}

		var index = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 10));
		var source = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 12));
		var target = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 14));
		var duration = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 16));
		if (index > MaxIndex || source > 0x100 || target > 0x100 ||
			duration > MaxDurationMilliseconds)
		{
			return false;
		}

		for (var indexByte = offset + 18; indexByte < offset + RecordSize; indexByte++)
		{
			if (data[indexByte] != 0)
			{
				return false;
			}
		}

		transition = new MotionGraphTransition(offset, index, source, target, duration);
		return true;
	}

	/// <summary>
	/// Scans a payload for consecutive timed-transition records. A run requires at least one record;
	/// records inside a run have strictly increasing indices.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<MotionGraphTransition> FindAll(byte[] data, int start, int end)
	{
		ArgumentNullException.ThrowIfNull(data);
		end = Math.Min(end, data.Length);
		var transitions = new List<MotionGraphTransition>();
		for (var offset = Math.Max(0, start); offset + RecordSize <= end;)
		{
			if (!TryRead(data, offset, end, out var transition) || transition.Index < 4)
			{
				offset += 4;
				continue;
			}

			transitions.Add(transition);
			var next = offset + RecordSize;
			while (next + RecordSize <= end &&
				TryRead(data, next, end, out var following) &&
				following.Index == transitions[^1].Index + 1)
			{
				transitions.Add(following);
				next += RecordSize;
			}

			offset = next;
		}

		return transitions;
	}
}
