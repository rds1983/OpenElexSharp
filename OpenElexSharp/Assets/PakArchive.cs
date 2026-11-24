using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// Read-only access to a Genome Engine G3V0 PAK volume, including the extended directory format
/// used by ELEX (Risen 3 style).
/// </summary>
public sealed class PakArchive
{
	/// <summary>"G3V0" as a little-endian integer.</summary>
	public const uint Magic = 0x30563347;

	/// <summary>The fixed header size and the start of file data.</summary>
	public const int HeaderSize = 48;

	private const int MaxNameLength = 4096;
	private const int MaxEntryCount = 2_000_000;
	private const int MaxDirectoryDepth = 32;
	private const long MaxDecompressedSize = 512L * 1024 * 1024;
	private const uint DirectoryAttribute = 0x00000010;
	private const uint DeletedOrEncryptedAttributes = 0x0000C000;

	private PakArchive(
		string filePath,
		uint version,
		uint revision,
		uint compression,
		long dataOffset,
		long directoryOffset,
		long volumeSize,
		IReadOnlyList<PakEntry> entries)
	{
		FilePath = filePath;
		Version = version;
		Revision = revision;
		Compression = compression;
		DataOffset = dataOffset;
		DirectoryOffset = directoryOffset;
		VolumeSize = volumeSize;
		Entries = entries;
	}

	/// <summary>The physical path of the archive.</summary>
	public string FilePath { get; }

	/// <summary>The container version (ELEX uses 1).</summary>
	public uint Version { get; }

	/// <summary>The revision field from the header.</summary>
	public uint Revision { get; }

	/// <summary>The compression mode field from the header (ELEX uses 1).</summary>
	public uint Compression { get; }

	/// <summary>Absolute offset where file data starts.</summary>
	public long DataOffset { get; }

	/// <summary>Absolute offset of the directory tree.</summary>
	public long DirectoryOffset { get; }

	/// <summary>The archive size recorded in the header.</summary>
	public long VolumeSize { get; }

	/// <summary>All live file entries, in directory order.</summary>
	public IReadOnlyList<PakEntry> Entries { get; }

	/// <summary>Opens and parses a PAK volume.</summary>
	/// <exception cref="PakFormatException">The archive is malformed or uses an unsupported variant.</exception>
	public static PakArchive Open(string filePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);

		var fileLength = stream.Length;
		if (fileLength < HeaderSize)
		{
			throw new PakFormatException($"'{filePath}' is too small to be a PAK archive.");
		}

		var version = reader.ReadUInt32();
		var magic = reader.ReadUInt32();
		var revision = reader.ReadUInt32();
		var encryption = reader.ReadUInt32();
		var compression = reader.ReadUInt32();
		_ = reader.ReadUInt32(); // Reserved marker (0xFEEDFACE in ELEX volumes).
		var dataOffset = ReadOffset(reader, filePath, "data offset");
		var directoryOffset = ReadOffset(reader, filePath, "directory offset");
		var volumeSize = ReadOffset(reader, filePath, "volume size");

		if (magic != Magic)
		{
			throw new PakFormatException($"'{filePath}' is not a G3V0 PAK archive.");
		}

		if (version != 1)
		{
			throw new PakFormatException($"'{filePath}' uses unsupported PAK version {version}; only version 1 is supported.");
		}

		if (encryption != 0)
		{
			throw new PakFormatException($"'{filePath}' is encrypted, which is not supported.");
		}

		if (compression > 2)
		{
			throw new PakFormatException($"'{filePath}' uses unknown compression mode {compression}.");
		}

		if (dataOffset < HeaderSize || directoryOffset < dataOffset || volumeSize < directoryOffset || volumeSize > fileLength)
		{
			throw new PakFormatException($"'{filePath}' has inconsistent header offsets.");
		}

		var entries = new List<PakEntry>();
		stream.Seek(directoryOffset, SeekOrigin.Begin);
		try
		{
			ReadDirectory(reader, stream, filePath, string.Empty, depth: 0, dataOffset, directoryOffset, entries);
		}
		catch (EndOfStreamException exception)
		{
			throw new PakFormatException($"'{filePath}' has a truncated directory.", exception);
		}

		if (stream.Position != volumeSize)
		{
			throw new PakFormatException(
				$"'{filePath}' directory ended at offset {stream.Position}, expected {volumeSize}.");
		}

		return new PakArchive(
			Path.GetFullPath(filePath),
			version,
			revision,
			compression,
			dataOffset,
			directoryOffset,
			volumeSize,
			entries);
	}

	/// <summary>
	/// Opens an entry for reading. Compressed entries are decompressed into memory so callers get a
	/// seekable stream with a reliable length.
	/// </summary>
	/// <exception cref="PakFormatException">A compressed payload is invalid.</exception>
	public Stream OpenRead(PakEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		if (!entry.IsCompressed)
		{
			var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			return new BoundedReadStream(stream, entry.Offset, entry.StoredSize);
		}

		if (entry.UncompressedSize > MaxDecompressedSize)
		{
			throw new PakFormatException(
				$"'{entry.VirtualPath}' declares {entry.UncompressedSize} decompressed bytes, " +
				$"which exceeds the safety limit of {MaxDecompressedSize} bytes.");
		}

		using var raw = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		raw.Seek(entry.Offset, SeekOrigin.Begin);
		using var bounded = new BoundedReadStream(raw, entry.Offset, entry.StoredSize);
		using var inflater = new System.IO.Compression.DeflateStream(
			bounded,
			System.IO.Compression.CompressionMode.Decompress);

		var destination = new MemoryStream((int)entry.UncompressedSize);
		try
		{
			inflater.CopyTo(destination);
		}
		catch (InvalidDataException exception)
		{
			destination.Dispose();
			throw new PakFormatException($"'{entry.VirtualPath}' contains invalid DEFLATE data.", exception);
		}

		var decompressedLength = destination.Length;
		if (decompressedLength != entry.UncompressedSize)
		{
			destination.Dispose();
			throw new PakFormatException(
				$"'{entry.VirtualPath}' decompressed to {decompressedLength} bytes, expected {entry.UncompressedSize}.");
		}

		destination.Position = 0;
		return destination;
	}

	private static void ReadDirectory(
		BinaryReader reader,
		Stream stream,
		string filePath,
		string parentPath,
		int depth,
		long dataOffset,
		long directoryOffset,
		List<PakEntry> entries)
	{
		if (depth > MaxDirectoryDepth)
		{
			throw new PakFormatException($"'{filePath}' directory tree exceeds the maximum depth of {MaxDirectoryDepth}.");
		}

		var name = ReadString(reader, stream, filePath);
		_ = reader.ReadUInt64(); // created
		_ = reader.ReadUInt64(); // accessed
		_ = reader.ReadUInt64(); // modified
		_ = reader.ReadUInt32(); // directory attributes
		var count = reader.ReadUInt32();

		if (count > MaxEntryCount - entries.Count)
		{
			throw new PakFormatException($"'{filePath}' contains too many entries.");
		}

		var directoryPath = name.Length == 0 ? parentPath : parentPath + name + "/";

		for (var index = 0u; index < count; index++)
		{
			var attributes = reader.ReadUInt32();
			if ((attributes & DirectoryAttribute) != 0)
			{
				ReadDirectory(reader, stream, filePath, directoryPath, depth + 1, dataOffset, directoryOffset, entries);
			}
			else
			{
				ReadFile(reader, stream, filePath, directoryPath, attributes, dataOffset, directoryOffset, entries);
			}
		}
	}

	private static void ReadFile(
		BinaryReader reader,
		Stream stream,
		string filePath,
		string directoryPath,
		uint prefixAttributes,
		long dataOffset,
		long directoryOffset,
		List<PakEntry> entries)
	{
		var name = ReadString(reader, stream, filePath);
		if (name.Length == 0)
		{
			throw new PakFormatException($"'{filePath}' contains a file with an empty name.");
		}

		var offset = ReadOffset(reader, filePath, "file offset");
		var created = reader.ReadUInt64();
		var accessed = reader.ReadUInt64();
		var modified = reader.ReadUInt64();
		var attributes = reader.ReadUInt32();
		_ = reader.ReadUInt32(); // Unknown, always 0 in observed archives.
		var compressed = reader.ReadUInt32() != 0;
		var storedSize = (long)reader.ReadUInt32();
		var uncompressedSize = (long)reader.ReadUInt32();

		if ((prefixAttributes & DeletedOrEncryptedAttributes) != 0 || (attributes & DeletedOrEncryptedAttributes) != 0)
		{
			return;
		}

		if (offset < dataOffset || storedSize < 0 || offset > directoryOffset || storedSize > directoryOffset - offset)
		{
			throw new PakFormatException($"'{filePath}' entry '{name}' has an out-of-range data range.");
		}

		string virtualPath;
		try
		{
			virtualPath = VirtualPath.Normalize(directoryPath + name);
		}
		catch (ArgumentException exception)
		{
			throw new PakFormatException($"'{filePath}' contains an invalid entry path '{directoryPath + name}'.", exception);
		}

		entries.Add(new PakEntry(
			virtualPath,
			offset,
			storedSize,
			uncompressedSize,
			compressed,
			attributes,
			created,
			accessed,
			modified));
	}

	private static string ReadString(BinaryReader reader, Stream stream, string filePath)
	{
		var length = reader.ReadUInt32();
		if (length > MaxNameLength)
		{
			throw new PakFormatException($"'{filePath}' contains an invalid name length of {length}.");
		}

		if (length == 0)
		{
			return string.Empty;
		}

		var bytes = reader.ReadBytes((int)length);
		if (bytes.Length != length)
		{
			throw new PakFormatException($"'{filePath}' ends inside a name.");
		}

		if (stream.Position >= stream.Length)
		{
			throw new PakFormatException($"'{filePath}' ends before a name terminator.");
		}

		_ = reader.ReadByte(); // NUL terminator.
		return Encoding.Latin1.GetString(bytes);
	}

	private static long ReadOffset(BinaryReader reader, string filePath, string fieldName)
	{
		var value = reader.ReadUInt64();
		if (value > long.MaxValue)
		{
			throw new PakFormatException($"'{filePath}' has an invalid {fieldName}.");
		}

		return (long)value;
	}
}
