using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>
/// One state of a binary state-machine block in a <c>net</c> payload. A block starts with a header
/// that stores one offset per state; each state has <see cref="RecordCount"/> records of
/// <see cref="ValuesPerRecord"/> 32-bit values followed by a 24-byte trailer.
/// </summary>
/// <param name="BlockOffset">Absolute resource offset of the owning block.</param>
/// <param name="Index">Zero-based state index inside the block.</param>
/// <param name="Name">State name when a matching name table was recognized.</param>
/// <param name="Values">16 records of five values each.</param>
public sealed record MotionGraphState(
	int BlockOffset,
	int Index,
	string? Name,
	IReadOnlyList<IReadOnlyList<uint>> Values)
{
	/// <summary>Records per state.</summary>
	public const int RecordCount = 16;

	/// <summary>Values per record.</summary>
	public const int ValuesPerRecord = 5;

	/// <summary>Bytes per record (five <c>uint64</c> slots).</summary>
	public const int RecordSize = 40;

	/// <summary>Trailer bytes after the records.</summary>
	public const int TrailerSize = 24;

	/// <summary>Total bytes per state.</summary>
	public const int StateSize = RecordCount * RecordSize + TrailerSize;

	/// <summary>Header bytes shared by every block.</summary>
	public const int HeaderBaseSize = 16;

	/// <summary>Marker word that follows the block kind.</summary>
	public const uint Marker = 0x53;

	/// <summary>Sentinel value used for empty slots.</summary>
	public const uint Sentinel = uint.MaxValue;

	/// <summary>Distinct non-sentinel values across all records.</summary>
	public IReadOnlyList<uint> References => Values
		.SelectMany(record => record)
		.Where(value => value != Sentinel && value <= 0x10000)
		.Distinct()
		.Order()
		.ToArray();

	/// <summary>
	/// Reads a state-machine block header at <paramref name="start"/>. The first word is the state
	/// count, followed by the <see cref="Marker"/> and a fixed record count of 16. The header then
	/// stores one <c>uint64</c> offset per state.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static bool TryReadHeader(
		byte[] data,
		int start,
		int end,
		out int stateCount,
		out int size)
	{
		ArgumentNullException.ThrowIfNull(data);
		stateCount = 0;
		size = 0;
		end = Math.Min(end, data.Length);
		if (start < 0 || start + HeaderBaseSize > end)
		{
			return false;
		}

		var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start));
		if (count == 0 || count > 64 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 4)) != Marker ||
			BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(start + 8)) != RecordCount)
		{
			return false;
		}

		var headerSize = HeaderBaseSize + (int)count * 8;
		if (start + headerSize > end ||
			BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(start + 16)) != (ulong)headerSize)
		{
			return false;
		}

		var previous = -1L;
		var last = 0L;
		for (var index = 0; index < count; index++)
		{
			var offset = BinaryPrimitives.ReadUInt64LittleEndian(
				data.AsSpan(start + HeaderBaseSize + index * 8));
			if (offset < (ulong)headerSize || offset > int.MaxValue ||
				start + (long)offset + StateSize > end || (long)offset <= previous)
			{
				return false;
			}

			previous = (long)offset;
			last = (long)offset;
		}

		stateCount = (int)count;
		size = (int)(last + StateSize);
		return true;
	}

	/// <summary>
	/// Reads the states of a block at <paramref name="start"/>. When <paramref name="names"/> has
	/// exactly one entry per state, the names are attached to the states.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static bool TryRead(
		byte[] data,
		int start,
		int end,
		IReadOnlyList<string>? names,
		out MotionGraphState[] states,
		out int size)
	{
		ArgumentNullException.ThrowIfNull(data);
		states = [];
		if (!TryReadHeader(data, start, end, out var stateCount, out size))
		{
			return false;
		}

		var offsets = new ulong[stateCount];
		for (var index = 0; index < stateCount; index++)
		{
			offsets[index] = BinaryPrimitives.ReadUInt64LittleEndian(
				data.AsSpan(start + HeaderBaseSize + index * 8));
		}

		var result = new MotionGraphState[stateCount];
		for (var state = 0; state < stateCount; state++)
		{
			var records = new IReadOnlyList<uint>[RecordCount];
			var stateStart = start + (int)offsets[state];
			for (var record = 0; record < RecordCount; record++)
			{
				var recordStart = stateStart + record * RecordSize;
				var values = new uint[ValuesPerRecord];
				for (var value = 0; value < ValuesPerRecord; value++)
				{
					var slot = recordStart + value * 8;
					if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(slot + 4)) != 0)
					{
						return false;
					}

					values[value] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(slot));
				}

				records[record] = values;
			}

			var name = names is not null && names.Count == stateCount ? names[state] : null;
			result[state] = new MotionGraphState(start, state, name, records);
		}

		states = result;
		return true;
	}

	/// <summary>Returns whether <paramref name="value"/> can be a state name.</summary>
	public static bool IsNameCandidate(string value)
	{
		if (value.Length < 2 || value.Length > 64 || value.Contains('|') || value.Contains(' ') ||
			value.StartsWith("BONE_", StringComparison.Ordinal) ||
			value.StartsWith("blendShape", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "CharacterWorldSpaceTM", StringComparison.Ordinal) ||
			string.Equals(value, "TRAJECTORY", StringComparison.Ordinal))
		{
			return false;
		}

		foreach (var character in value)
		{
			if (!char.IsAsciiLetterOrDigit(character) && character != '_' &&
				character != '.' && character != '-')
			{
				return false;
			}
		}

		return true;
	}
}
