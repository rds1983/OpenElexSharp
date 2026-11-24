using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// The optional <c>GENOMFLE</c> string table used by some Genome serialized archives. Strings are
/// referenced by 16-bit indices instead of being embedded in the object stream.
/// </summary>
public sealed class GenomeStringTable
{
	/// <summary>Magic bytes.</summary>
	public const string Magic = "GENOMFLE";

	private const uint FooterMagic = 0xDEADBEEF;
	private const int MaxNameLength = 4096;

	private GenomeStringTable(ushort version, uint bodySize, string[] strings)
	{
		Version = version;
		BodySize = bodySize;
		Strings = strings;
	}

	/// <summary>String-table version (observed as 1).</summary>
	public ushort Version { get; }

	/// <summary>Offset of the trailing string table from the start of the archive.</summary>
	public uint BodySize { get; }

	/// <summary>Strings in index order.</summary>
	public IReadOnlyList<string> Strings { get; }

	/// <summary>Returns whether the buffer starts with a <c>GENOMFLE</c> archive.</summary>
	public static bool IsGenomeFile(ReadOnlySpan<byte> data) =>
		data.Length >= 8 && data[..8].SequenceEqual("GENOMFLE"u8);

	/// <summary>Tries to parse a <c>GENOMFLE</c> string table.</summary>
	public static bool TryParse(byte[] data, [NotNullWhen(true)] out GenomeStringTable? table)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (!IsGenomeFile(data))
		{
			table = null;
			return false;
		}

		table = Parse(data);
		return true;
	}

	/// <summary>Parses a <c>GENOMFLE</c> string table.</summary>
	/// <exception cref="GenomeFormatException">The archive is malformed or truncated.</exception>
	public static GenomeStringTable Parse(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Length < 14 || !IsGenomeFile(data))
		{
			throw new GenomeFormatException("The file is not a GENOMFLE archive.");
		}

		var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
		var bodySize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10));
		if (bodySize < 14 || bodySize > data.Length)
		{
			throw new GenomeFormatException($"The GENOMFLE archive has an invalid body size ({bodySize}).");
		}

		var footer = (int)bodySize;
		if (footer + 9 > data.Length ||
			BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(footer)) != FooterMagic)
		{
			throw new GenomeFormatException("The GENOMFLE string table footer is missing.");
		}

		_ = data[footer + 4]; // Footer version.
		var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(footer + 5));
		if (count > 1_000_000)
		{
			throw new GenomeFormatException($"The GENOMFLE string table declares too many strings ({count}).");
		}

		var strings = new string[(int)count];
		var position = footer + 9;
		for (var index = 0; index < strings.Length; index++)
		{
			if (position + 2 > data.Length)
			{
				throw new GenomeFormatException($"GENOMFLE string {index} is truncated.");
			}

			var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
			position += 2;
			if (length > MaxNameLength || position + length > data.Length)
			{
				throw new GenomeFormatException($"GENOMFLE string {index} has an invalid length.");
			}

			strings[index] = Encoding.Latin1.GetString(data, position, length);
			position += length;
		}

		if (position != data.Length)
		{
			throw new GenomeFormatException("The GENOMFLE string table has trailing data.");
		}

		return new GenomeStringTable(version, bodySize, strings);
	}
}
