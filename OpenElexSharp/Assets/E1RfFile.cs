using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// The <c>E1RF</c> resolved-resource wrapper. ELEX resource databases and their hashed ROM
/// payloads can be combined into this format, as the Elex Resource Manager does on extraction.
/// </summary>
public sealed class E1RfFile
{
    /// <summary>"E1RF" as a little-endian integer.</summary>
    public const uint Magic = 0x46523145;

    private E1RfFile(byte[] romData, uint classHash, uint classRevision, byte[] databaseEntry)
    {
        RomData = romData;
        ClassHash = classHash;
        ClassRevision = classRevision;
        DatabaseEntry = databaseEntry;
    }

    /// <summary>The hashed ROM payload (normally a GAR5 file).</summary>
    public byte[] RomData { get; }

    /// <summary>Resource class hash from the database header.</summary>
    public uint ClassHash { get; }

    /// <summary>Resource class revision from the database header.</summary>
    public uint ClassRevision { get; }

    /// <summary>The raw database record that describes this resource.</summary>
    public byte[] DatabaseEntry { get; }

    /// <summary>The four-character class tag encoded in <see cref="ClassRevision"/>.</summary>
    public string ClassTag
    {
        get
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, ClassRevision);
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Parses an extracted <c>E1RF</c> file.</summary>
    /// <exception cref="Gar5FormatException">The wrapper is malformed.</exception>
    public static E1RfFile Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 8)
        {
            throw new Gar5FormatException("The E1RF file is truncated.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            throw new Gar5FormatException("The file does not start with an E1RF header.");
        }

        var romSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var metadataOffset = 8L + romSize;
        if (metadataOffset + 12 > data.Length)
        {
            throw new Gar5FormatException("The E1RF file has an invalid ROM size.");
        }

        var metadata = (int)metadataOffset;
        var classHash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(metadata));
        var classRevision = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(metadata + 4));
        var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(metadata + 8));
        var totalLength = metadataOffset + 12 + entrySize;
        if (totalLength != data.Length)
        {
            throw new Gar5FormatException(
                $"The E1RF file has an invalid database-entry size ({entrySize}).");
        }

        var romData = data.AsSpan(8, (int)romSize).ToArray();
        var databaseEntry = data.AsSpan(metadata + 12, (int)entrySize).ToArray();
        return new E1RfFile(romData, classHash, classRevision, databaseEntry);
    }
}
