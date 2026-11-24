using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>
/// One binary state record group mapped to a state name. Each group has 16 records of five
/// <c>uint64</c> values followed by 24 bytes of trailer data.
/// </summary>
/// <param name="Name">State name.</param>
/// <param name="Records">16 five-value records.</param>
public sealed record MotionGraphState(string Name, IReadOnlyList<IReadOnlyList<ulong>> Records)
{
	/// <summary>Record count per state.</summary>
	public const int RecordCount = 16;

	/// <summary>Bytes per record.</summary>
	public const int RecordSize = 40;

	/// <summary>Trailer bytes after the records.</summary>
	public const int TrailerSize = 24;

	/// <summary>Total bytes per state group.</summary>
	public const int StateSize = RecordCount * RecordSize + TrailerSize;

	/// <summary>Distinct non-sentinel references across all records.</summary>
	public IReadOnlyList<uint> References => Records
		.SelectMany(record => record)
		.Select(value => (uint)value)
		.Where(value => value != uint.MaxValue && value <= 0x10000)
		.Distinct()
		.Order()
		.ToArray();

	/// <summary>Returns sample tracks whose <see cref="MotionSampleTrack.TrackIndex"/> is referenced.</summary>
	public IReadOnlyList<MotionSampleTrack> FindSampleTracks(
		IReadOnlyList<MotionSampleTrack> sampleTracks)
	{
		ArgumentNullException.ThrowIfNull(sampleTracks);
		var references = References.ToHashSet();
		return sampleTracks
			.Where(track => references.Contains((uint)track.TrackIndex))
			.ToArray();
	}

	/// <summary>
	/// Tries to read <paramref name="names"/>.Length consecutive state groups starting at
	/// <paramref name="start"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static bool TryRead(
		byte[] data,
		int start,
		string[] names,
		out MotionGraphState[] states)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(names);
		states = [];
		if (names.Length == 0 || start < 0 || start + names.Length * StateSize > data.Length)
		{
			return false;
		}

		var result = new MotionGraphState[names.Length];
		for (var state = 0; state < names.Length; state++)
		{
			var records = new IReadOnlyList<ulong>[RecordCount];
			var groupStart = start + state * StateSize;
			for (var record = 0; record < RecordCount; record++)
			{
				var offset = groupStart + record * RecordSize;
				if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4)) != 0)
				{
					return false;
				}

				var values = new ulong[5];
				for (var value = 0; value < values.Length; value++)
				{
					values[value] = BinaryPrimitives.ReadUInt64LittleEndian(
						data.AsSpan(offset + value * 8));
				}

				var reference = (uint)values[0];
				if (reference > 0x10000 && reference != uint.MaxValue)
				{
					return false;
				}

				records[record] = values;
			}

			result[state] = new MotionGraphState(names[state], records);
		}

		states = result;
		return true;
	}
}
