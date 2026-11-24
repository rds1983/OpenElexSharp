using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// Parsed <c>bln</c> blend-shape set (<c>class eCBlendShapeResource2</c>). The payload starts with a
/// small header, a table of name offsets and a NUL-terminated list of <c>blendShape1.*</c> target
/// names. The per-target delta data after the names is not decoded yet.
/// </summary>
public sealed class ElexBlendShapeSet
{
	private const int Gar5WrapperSize = 44;
	private const int HeaderSize = 28;
	private const int MaxTargetCount = 4096;
	private const int MaxNameLength = 256;

	private ElexBlendShapeSet(
		string name,
		int targetCount,
		int vertexCount,
		int field0,
		int field1,
		int field2,
		int secondCount,
		IReadOnlyList<uint> nameOffsets,
		IReadOnlyList<string> names,
		int nameTableOffset)
	{
		Name = name;
		TargetCount = targetCount;
		VertexCount = vertexCount;
		Field0 = field0;
		Field1 = field1;
		Field2 = field2;
		SecondCount = secondCount;
		NameOffsets = nameOffsets;
		Names = names;
		NameTableOffset = nameTableOffset;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Number of blend-shape targets.</summary>
	public int TargetCount { get; }

	/// <summary>Base-mesh vertex count.</summary>
	public int VertexCount { get; }

	/// <summary>Unresolved header value at offset 8.</summary>
	public int Field0 { get; }

	/// <summary>Unresolved header value at offset 12.</summary>
	public int Field1 { get; }

	/// <summary>Unresolved header value at offset 16.</summary>
	public int Field2 { get; }

	/// <summary>Repeated target count at offset 20.</summary>
	public int SecondCount { get; }

	/// <summary>
	/// End offsets of targets <c>0..targetCount-2</c>, relative to the first name. Target 0 starts at
	/// relative offset 0; target <c>i</c> starts at <c>NameOffsets[i-1]</c>.
	/// </summary>
	public IReadOnlyList<uint> NameOffsets { get; }

	/// <summary>Blend-shape target names in table order.</summary>
	public IReadOnlyList<string> Names { get; }

	/// <summary>Offset of the name table in the payload.</summary>
	public int NameTableOffset { get; }

	/// <summary>Parses a <c>bln</c> resource wrapped in a GAR5 header.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="Gar5FormatException">The payload is malformed.</exception>
	public static ElexBlendShapeSet Parse(string name, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + HeaderSize ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has no GAR5 payload.");
		}

		var payload = Gar5WrapperSize;
		var targetCount = (int)ReadUInt32(data, payload);
		if (targetCount == 0 || targetCount > MaxTargetCount)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has an invalid target count.");
		}

		var offsetsStart = payload + HeaderSize;
		var offsetCount = targetCount - 1;
		var offsetsByteCount = checked(offsetCount * 4);
		if (offsetsStart + offsetsByteCount > data.Length)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has truncated name offsets.");
		}

		var nameOffsets = new uint[offsetCount];
		var previousOffset = 0u;
		for (var index = 0; index < nameOffsets.Length; index++)
		{
			var value = ReadUInt32(data, offsetsStart + index * 4);
			if (value < previousOffset || value > data.Length)
			{
				throw new Gar5FormatException($"Blend-shape set '{name}' has invalid name offsets.");
			}

			nameOffsets[index] = value;
			previousOffset = value;
		}

		var nameTable = FindNameTable(data, offsetsStart + offsetsByteCount, nameOffsets, targetCount);
		if (nameTable < 0)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has no target-name table.");
		}

		var names = new List<string>(targetCount);
		for (var index = 0; index < targetCount; index++)
		{
			var relative = index == 0 ? 0u : nameOffsets[index - 1];
			var start = nameTable + (long)relative;
			long end;
			if (index < nameOffsets.Length)
			{
				end = nameTable + (long)nameOffsets[index];
			}
			else
			{
				if (start < 0 || start >= data.Length)
				{
					throw new Gar5FormatException($"Blend-shape set '{name}' has an invalid name table.");
				}

				var zero = Array.IndexOf(
					data, (byte)0, (int)start, Math.Min(MaxNameLength + 1, data.Length - (int)start));
				if (zero < 0)
				{
					throw new Gar5FormatException($"Blend-shape set '{name}' has an unterminated target name.");
				}

				end = zero + 1L;
			}

			if (start < 0 || end <= start || end > data.Length)
			{
				throw new Gar5FormatException($"Blend-shape set '{name}' has an invalid name table.");
			}

			names.Add(Encoding.Latin1.GetString(data, (int)start, (int)(end - start - 1)));
		}

		return new ElexBlendShapeSet(
			name,
			targetCount,
			(int)ReadUInt32(data, payload + 4),
			(int)ReadUInt32(data, payload + 8),
			(int)ReadUInt32(data, payload + 12),
			(int)ReadUInt32(data, payload + 16),
			(int)ReadUInt32(data, payload + 20),
			nameOffsets,
			names,
			nameTable);
	}

	private static int FindNameTable(byte[] data, int searchStart, uint[] nameOffsets, int targetCount)
	{
		for (var index = searchStart; index + 1 < data.Length; index++)
		{
			if (!IsPrintableByte(data[index]))
			{
				continue;
			}

			if (nameOffsets.Length > 0 && nameOffsets[0] > 0)
			{
				var firstEnd = index + (long)nameOffsets[0] - 1;
				if (firstEnd < data.Length && data[(int)firstEnd] == 0 &&
					TryValidateNames(data, index, nameOffsets, targetCount))
				{
					return index;
				}
			}

			if (nameOffsets.Length == 0)
			{
				var end = Array.IndexOf(data, (byte)0, index, Math.Min(MaxNameLength + 1, data.Length - index));
				if (end > index && end - index <= MaxNameLength && IsPrintableRange(data, index, end - index))
				{
					return index;
				}
			}
		}

		return -1;
	}

	private static bool TryValidateNames(byte[] data, int nameTable, uint[] nameOffsets, int targetCount)
	{
		for (var index = 0; index < targetCount; index++)
		{
			var relative = index == 0 ? 0u : nameOffsets[index - 1];
			var start = nameTable + (long)relative;
			if (start < 0 || start >= data.Length)
			{
				return false;
			}

			long end;
			if (index < nameOffsets.Length)
			{
				end = nameTable + (long)nameOffsets[index];
				if (end <= start || end > data.Length || data[(int)end - 1] != 0)
				{
					return false;
				}
			}
			else
			{
				var zero = Array.IndexOf(
					data, (byte)0, (int)start, Math.Min(MaxNameLength + 1, data.Length - (int)start));
				if (zero < 0)
				{
					return false;
				}

				end = zero + 1L;
			}

			var length = (int)(end - start - 1);
			if (length <= 0 || length > MaxNameLength || !IsPrintableRange(data, (int)start, length))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsPrintableRange(byte[] data, int start, int length)
	{
		for (var index = 0; index < length; index++)
		{
			if (!IsPrintableByte(data[start + index]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsPrintableByte(byte value) => value >= 0x20 && value < 0x7F;

	private static uint ReadUInt32(byte[] data, int offset) =>
		BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
}
