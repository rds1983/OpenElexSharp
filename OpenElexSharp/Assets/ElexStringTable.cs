using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One decoded string-table column.</summary>
/// <param name="Name">Column name from the binary header, normally <c>English_Text</c>.</param>
/// <param name="Rows">ID hash to decoded text.</param>
public sealed record ElexStringColumn(string Name, IReadOnlyDictionary<uint, string> Rows);

/// <summary>One CSV source recorded in a compiled string table.</summary>
/// <param name="CsvPath">Raw CSV path, normally under <c>#G3:/Data/Raw/Strings/</c>.</param>
/// <param name="Modified">Raw file timestamp.</param>
public sealed record ElexStringTableSource(string CsvPath, ulong Modified);

/// <summary>
/// A compiled Genome string table (<c>STB&lt;version&gt;</c>, version 7 in ELEX). The table stores
/// column-compressed UTF-16 text addressed by 32-bit DJB2 ID hashes.
/// </summary>
public sealed class ElexStringTable
{
	/// <summary>Offset of the STB header, immediately after the GAR5 magic and tag.</summary>
	public const int HeaderOffset = 8;

	/// <summary>Minimum supported STB version.</summary>
	public const byte MinimumVersion = 5;

	private const int HeaderSize = 9 * 4;
	private const int MaxSources = 10_000;
	private const int MaxColumns = 64;
	private const int MaxRows = 1_000_000;

	private ElexStringTable(
		string name,
		string sourcePath,
		int version,
		ElexStringTableSource[] sources,
		ElexStringColumn[] columns,
		uint[] idHashes)
	{
		Name = name;
		SourcePath = sourcePath;
		Version = version;
		Sources = sources;
		Columns = columns;
		IdHashes = idHashes;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Virtual path of the compiled <c>str</c> resource.</summary>
	public string SourcePath { get; }

	/// <summary>STB header version.</summary>
	public int Version { get; }

	/// <summary>Source CSV files in table order.</summary>
	public IReadOnlyList<ElexStringTableSource> Sources { get; }

	/// <summary>String columns in table order.</summary>
	public IReadOnlyList<ElexStringColumn> Columns { get; }

	/// <summary>ID hashes in descending table order.</summary>
	public IReadOnlyList<uint> IdHashes { get; }

	/// <summary>Number of rows (IDs).</summary>
	public int RowCount => IdHashes.Count;

	/// <summary>Parses a compiled <c>str</c> resource.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The table is malformed.</exception>
	public static ElexStringTable Parse(string name, string sourcePath, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < HeaderOffset + HeaderSize ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new GenomeFormatException($"String table '{name}' has no GAR5/STB header.");
		}

		var magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset));
		if ((magic & 0x00FF_FFFF) != 0x0042_5453)
		{
			throw new GenomeFormatException($"String table '{name}' has an invalid STB signature.");
		}

		var version = (byte)(magic >> 24);
		if (version < MinimumVersion)
		{
			throw new GenomeFormatException($"String table '{name}' uses unsupported STB version {version}.");
		}

		var sourceCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 4));
		var columnCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 12));
		var rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 16));
		var sourceTable = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 20));
		var columnNames = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 24));
		var columnTable = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 28));
		var keyTable = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderOffset + 32));
		if (sourceCount < 0 || sourceCount > MaxSources ||
			columnCount < 1 || columnCount > MaxColumns ||
			rowCount < 0 || rowCount > MaxRows)
		{
			throw new GenomeFormatException($"String table '{name}' has invalid header counts.");
		}

		var sources = ReadSources(data, sourceTable, sourceCount, name);
		var hashes = ReadKeys(data, keyTable, rowCount, name);
		var names = ReadColumnNames(data, columnNames, columnCount, name);
		var columns = ReadColumns(data, columnTable, names, hashes, name);
		return new ElexStringTable(name, sourcePath, version, sources, columns, hashes);
	}

	/// <summary>
	/// Looks up a string by its 32-bit ID hash. When <paramref name="columnName"/> is
	/// <see langword="null"/>, columns are searched in table order.
	/// </summary>
	public bool TryGet(uint idHash, string? columnName, out string value)
	{
		foreach (var column in Columns)
		{
			if (columnName is not null &&
				!string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (column.Rows.TryGetValue(idHash, out value!))
			{
				return true;
			}
		}

		value = string.Empty;
		return false;
	}

	/// <summary>
	/// Looks up a string by a textual ID. The compiled table stores hashes, not ID text, so the
	/// caller must use the same case-insensitive DJB2 convention as the game; an optional prefix is
	/// joined with a colon (for example <c>item:It_...</c>).
	/// </summary>
	public bool TryGet(string id, string? prefix, string? columnName, out string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		var hashed = prefix is null ? id : $"{prefix}:{id}";
		return TryGet(Djb2Hash.Compute(hashed), columnName, out value);
	}

	/// <summary>Finds decoded strings containing <paramref name="text"/>.</summary>
	public IEnumerable<(uint IdHash, string Column, string Text)> Find(
		string text,
		int limit = int.MaxValue)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text);
		var found = 0;
		foreach (var column in Columns)
		{
			foreach (var pair in column.Rows)
			{
				if (pair.Value.Contains(text, StringComparison.OrdinalIgnoreCase))
				{
					yield return (pair.Key, column.Name, pair.Value);
					if (++found >= limit)
					{
						yield break;
					}
				}
			}
		}
	}

	private static ElexStringTableSource[] ReadSources(
		byte[] data,
		int offset,
		int count,
		string name)
	{
		if (count == 0)
		{
			return [];
		}

		var sources = new ElexStringTableSource[count];
		var position = offset;
		for (var index = 0; index < sources.Length; index++)
		{
			var length = ReadU16(data, position, name);
			position += 2;
			if (position + length + 8 > data.Length)
			{
				throw new GenomeFormatException($"String table '{name}' source path is truncated.");
			}

			var path = Encoding.Latin1.GetString(data, position, length);
			position += length;
			var modified = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(position));
			position += 8;
			sources[index] = new ElexStringTableSource(path, modified);
		}

		return sources;
	}

	private static uint[] ReadKeys(byte[] data, int keyTable, int rowCount, string name)
	{
		if (rowCount == 0)
		{
			return [];
		}

		var size = ReadU32(data, keyTable, name);
		var position = ReadU32(data, keyTable + 4, name);
		if (size < rowCount * 4L || position + rowCount * 4L > data.Length)
		{
			throw new GenomeFormatException($"String table '{name}' has an invalid key table.");
		}

		var keys = new uint[rowCount];
		for (var index = 0; index < keys.Length; index++)
		{
			keys[index] = BinaryPrimitives.ReadUInt32LittleEndian(
				data.AsSpan((int)position + index * 4));
		}

		return keys;
	}

	private static string[] ReadColumnNames(
		byte[] data,
		int offset,
		int count,
		string name)
	{
		var names = new string[count];
		for (var index = 0; index < count; index++)
		{
			var size = ReadU32(data, offset + index * 8, name);
			var position = ReadU32(data, offset + index * 8 + 4, name);
			if (size > 4096 || position + size > data.Length)
			{
				throw new GenomeFormatException($"String table '{name}' has an invalid column name.");
			}

			var text = Encoding.Latin1.GetString(data, (int)position, (int)size).TrimEnd('\0');
			if (text.Length == 0)
			{
				throw new GenomeFormatException($"String table '{name}' has an empty column name.");
			}

			names[index] = text;
		}

		return names;
	}

	private static ElexStringColumn[] ReadColumns(
		byte[] data,
		int columnTable,
		string[] names,
		uint[] hashes,
		string name)
	{
		var columns = new ElexStringColumn[names.Length];
		for (var columnIndex = 0; columnIndex < names.Length; columnIndex++)
		{
			var tableOffset = columnTable + columnIndex * 16;
			var stringSize = ReadU32(data, tableOffset, name);
			var stringPosition = ReadU32(data, tableOffset + 4, name);
			var symbolSize = ReadU32(data, tableOffset + 8, name);
			var symbolPosition = ReadU32(data, tableOffset + 12, name);
			var minimumStringSize = (long)hashes.Length * 4;
			if (stringSize < minimumStringSize || symbolSize % 4 != 0 ||
				stringPosition + stringSize > data.Length ||
				symbolPosition + symbolSize > data.Length)
			{
				throw new GenomeFormatException(
					$"String table '{name}' column '{names[columnIndex]}' is out of range.");
			}

			var stringBeginnings = new uint[hashes.Length];
			for (var index = 0; index < stringBeginnings.Length; index++)
			{
				stringBeginnings[index] = BinaryPrimitives.ReadUInt32LittleEndian(
					data.AsSpan((int)stringPosition + index * 4));
			}

			var sequenceCount = (int)((stringSize - minimumStringSize) / 2);
			var sequences = new ushort[sequenceCount];
			var sequenceStart = (int)stringPosition + hashes.Length * 4;
			for (var index = 0; index < sequences.Length; index++)
			{
				sequences[index] = BinaryPrimitives.ReadUInt16LittleEndian(
					data.AsSpan(sequenceStart + index * 2));
			}

			var symbolCount = (int)(symbolSize / 4);
			var symbols = new uint[symbolCount];
			for (var index = 0; index < symbols.Length; index++)
			{
				symbols[index] = BinaryPrimitives.ReadUInt32LittleEndian(
					data.AsSpan((int)symbolPosition + index * 4));
			}

			var rows = new Dictionary<uint, string>(hashes.Length);
			for (var row = 0; row < hashes.Length; row++)
			{
				var start = stringBeginnings[row];
				if (start == uint.MaxValue)
				{
					rows[hashes[row]] = string.Empty;
					continue;
				}

				if (start >= sequences.Length)
				{
					throw new GenomeFormatException(
						$"String table '{name}' column '{names[columnIndex]}' has an invalid row.");
				}

				rows[hashes[row]] = Decode(sequences, symbols, (int)start, name);
			}

			columns[columnIndex] = new ElexStringColumn(names[columnIndex], rows);
		}

		return columns;
	}

	private static string Decode(
		ushort[] sequences,
		uint[] symbols,
		int start,
		string name)
	{
		var text = new StringBuilder();
		var position = start;
		while (position < sequences.Length && sequences[position] != 0)
		{
			var symbolIndex = sequences[position];
			if (symbolIndex >= symbols.Length)
			{
				throw new GenomeFormatException($"String table '{name}' has an invalid symbol index.");
			}

			var sub = new StringBuilder();
			var symbol = symbols[symbolIndex];
			while (true)
			{
				var character = (char)(symbol >> 16);
				if (character == '\0')
				{
					throw new GenomeFormatException($"String table '{name}' has an empty symbol character.");
				}

				sub.Append(character);
				var previous = (ushort)(symbol & 0xFFFF);
				if (previous == 0)
				{
					break;
				}

				if (previous >= symbols.Length)
				{
					throw new GenomeFormatException($"String table '{name}' has an invalid symbol link.");
				}

				symbol = symbols[previous];
			}

			for (var index = sub.Length - 1; index >= 0; index--)
			{
				text.Append(sub[index]);
			}

			position++;
		}

		return text.ToString();
	}

	private static ushort ReadU16(byte[] data, int offset, string name)
	{
		if (offset < 0 || offset + 2 > data.Length)
		{
			throw new GenomeFormatException($"String table '{name}' is truncated.");
		}

		return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
	}

	private static uint ReadU32(byte[] data, int offset, string name)
	{
		if (offset < 0 || offset + 4 > data.Length)
		{
			throw new GenomeFormatException($"String table '{name}' is truncated.");
		}

		return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
	}
}
