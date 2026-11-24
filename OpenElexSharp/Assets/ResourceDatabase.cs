using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One logical resource listed in an <c>RT02</c> resource database.</summary>
/// <param name="Name">Logical resource name.</param>
/// <param name="Offset">Offset of the full database record.</param>
/// <param name="Size">Size of the full database record.</param>
/// <param name="ClassBlockOffset">Offset of the record's <c>GEC2</c> class block.</param>
/// <param name="ClassBlockSize">Size of the class block starting at <paramref name="ClassBlockOffset"/>.</param>
public sealed record ResourceDatabaseEntry(
	string Name,
	int Offset,
	int Size,
	int ClassBlockOffset,
	int ClassBlockSize);

/// <summary>
/// Parser for the <c>RT02</c> resource databases stored as <c>w_&lt;type&gt;_&lt;generation&gt;_&lt;language&gt;.db</c>.
/// </summary>
public sealed class ResourceDatabase
{
	private const int MaxNameLength = 4096;
	private const int MaxEntries = 2_000_000;
	private const uint MaxClassBlockSize = 64 * 1024 * 1024;
	private static ReadOnlySpan<byte> ClassBlockMagic => "GEC2"u8;

	private ResourceDatabase(uint classHash, string classTag, ResourceDatabaseEntry[] entries)
	{
		ClassHash = classHash;
		ClassTag = classTag;
		Entries = entries;
	}

	/// <summary>Class hash from the database header.</summary>
	public uint ClassHash { get; }

	/// <summary>Four-character class tag, for example <c>SN11</c> or <c>DI22</c>.</summary>
	public string ClassTag { get; }

	/// <summary>Resource records in file order.</summary>
	public IReadOnlyList<ResourceDatabaseEntry> Entries { get; }

	/// <summary>Parses a complete <c>RT02</c> database file.</summary>
	/// <exception cref="Gar5FormatException">The wrapper, header or record structure is invalid.</exception>
	public static ResourceDatabase Parse(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);

		using var stream = new MemoryStream(data, writable: false);
		var header = Gar5Header.Read(stream);
		if (header.Tag != "RT02")
		{
			throw new Gar5FormatException($"'{header.Tag}' is not an RT02 resource database.");
		}

		if (header.PayloadOffset != 24 || data.Length < 24)
		{
			throw new Gar5FormatException("The RT02 database header is truncated.");
		}

		if (header.ResourceCount > MaxEntries)
		{
			throw new Gar5FormatException($"The database declares too many resources ({header.ResourceCount}).");
		}

		var entries = new List<ResourceDatabaseEntry>((int)header.ResourceCount);
		var position = 24;
		for (var index = 0; index < header.ResourceCount; index++)
		{
			var recordStart = position;
			if (position + 36 > data.Length)
			{
				throw new Gar5FormatException($"Database record {index} is truncated.");
			}

			position += 32; // Four 64-bit metadata fields (timestamps and unknown values).
			var nameLength = ReadUInt32(data, position);
			position += 4;

			if (nameLength == 0 || nameLength > MaxNameLength || position + nameLength + 1 > data.Length)
			{
				throw new Gar5FormatException($"Database record {index} has an invalid name length ({nameLength}).");
			}

			var name = Encoding.Latin1.GetString(data, position, (int)nameLength);
			position += (int)nameLength;
			position++; // Separator (observed as ',').

			var searchStart = position;
			var recordEnd = FindRecordEnd(
				data,
				searchStart,
				index + 1 == header.ResourceCount,
				out var classBlockOffset,
				out var classBlockSize);
			if (recordEnd < 0)
			{
				throw new Gar5FormatException($"Database record {index} has no valid GEC2 class block.");
			}

			entries.Add(new ResourceDatabaseEntry(
				name,
				recordStart,
				recordEnd - recordStart,
				classBlockOffset,
				classBlockSize));
			position = recordEnd;
		}

		if (position != data.Length)
		{
			throw new Gar5FormatException(
				$"Resource database ended at offset {position}, expected {data.Length}.");
		}

		return new ResourceDatabase(header.ClassHash, header.ClassTag, [.. entries]);
	}

	private static int FindRecordEnd(
		byte[] data,
		int searchStart,
		bool lastRecord,
		out int classBlockOffset,
		out int classBlockSize)
	{
		classBlockOffset = 0;
		classBlockSize = 0;
		var search = searchStart;
		while (search < data.Length)
		{
			var relative = data.AsSpan(search).IndexOf(ClassBlockMagic);
			if (relative < 0)
			{
				return -1;
			}

			var magicOffset = search + relative;
			if (magicOffset < 4 || magicOffset - 4 < searchStart)
			{
				search = magicOffset + 4;
				continue;
			}

			var blockSize = ReadUInt32(data, magicOffset - 4);
			if (blockSize < 8 || blockSize > MaxClassBlockSize)
			{
				search = magicOffset + 4;
				continue;
			}

			var end = (long)magicOffset + blockSize;
			if (end > data.Length || end < magicOffset)
			{
				search = magicOffset + 4;
				continue;
			}

			var endOffset = (int)end;
			if (lastRecord)
			{
				if (endOffset == data.Length)
				{
					classBlockOffset = magicOffset;
					classBlockSize = (int)blockSize;
					return endOffset;
				}
			}
			else if (IsPlausibleRecordStart(data, endOffset))
			{
				classBlockOffset = magicOffset;
				classBlockSize = (int)blockSize;
				return endOffset;
			}

			search = magicOffset + 4;
		}

		return -1;
	}

	private static bool IsPlausibleRecordStart(byte[] data, int offset)
	{
		if (offset + 36 > data.Length)
		{
			return false;
		}

		var nameLength = ReadUInt32(data, offset + 32);
		if (nameLength == 0 || nameLength > MaxNameLength || offset + 36L + nameLength > data.Length)
		{
			return false;
		}

		var name = data.AsSpan(offset + 36, (int)nameLength);
		foreach (var value in name)
		{
			if (value < 0x20 || value == 0x7F)
			{
				return false;
			}
		}

		return true;
	}

	private static uint ReadUInt32(byte[] data, int offset) =>
		BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

	/// <summary>Formats a hash as the eight lower-case hex digits used in ROM file names.</summary>
	public static string FormatHash(uint hash) => hash.ToString("x8", CultureInfo.InvariantCulture);
}
