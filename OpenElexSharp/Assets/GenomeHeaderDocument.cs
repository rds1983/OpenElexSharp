using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One named object in a DT01 header document.</summary>
/// <param name="Name">Object name, for example a quest or info identifier.</param>
/// <param name="Timestamp">Raw eight-byte timestamp field.</param>
/// <param name="Object">Parsed GEC2 object.</param>
public sealed record GenomeHeaderEntry(string Name, ulong Timestamp, GenomeObject Object);

/// <summary>One class section in a DT01 header document.</summary>
/// <param name="ClassHash">Class hash for entries in this section.</param>
/// <param name="Entries">Objects in stream order.</param>
public sealed record GenomeHeaderSection(uint ClassHash, IReadOnlyList<GenomeHeaderEntry> Entries);

/// <summary>
/// Parser for Genome <c>DT01</c> document files such as the retail <c>documents/*.hdr</c> assets.
/// </summary>
public sealed class GenomeHeaderDocument
{
    /// <summary>"DT01" tag.</summary>
    public const string Tag = "DT01";

    private const int MaxSectionCount = 100_000;
    private const int MaxEntryCount = 2_000_000;
    private const int MaxNameLength = 4096;

    private GenomeHeaderDocument(uint headerValue, GenomeHeaderSection[] sections)
    {
        HeaderValue = headerValue;
        Sections = sections;
    }

    /// <summary>Value from the GAR5 header; observed as <c>0x20</c>.</summary>
    public uint HeaderValue { get; }

    /// <summary>Sections in file order.</summary>
    public IReadOnlyList<GenomeHeaderSection> Sections { get; }

    /// <summary>Total number of top-level objects.</summary>
    public int ObjectCount => Sections.Sum(section => section.Entries.Count);

    /// <summary>Parses a complete DT01 document.</summary>
    /// <exception cref="GenomeFormatException">The document is malformed, truncated or uses another tag.</exception>
    public static GenomeHeaderDocument Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 24 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
        {
            throw new GenomeFormatException("The document does not start with a GAR5 header.");
        }

        var headerValue = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var tag = Encoding.Latin1.GetString(data, 8, 4);
        if (tag != Tag)
        {
            throw new GenomeFormatException($"'{tag}' is not a DT01 document.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)) != 1)
        {
            throw new GenomeFormatException("The DT01 document header has unexpected values.");
        }

        var sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20));
        if (sectionCount > MaxSectionCount || 24L + (sectionCount * 4L) > data.Length)
        {
            throw new GenomeFormatException($"The DT01 document declares an invalid section count ({sectionCount}).");
        }

        var sections = new GenomeHeaderSection[(int)sectionCount];
        var totalEntries = 0;
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var sectionOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(24 + (sectionIndex * 4)));
            if (sectionOffset > data.Length - 16)
            {
                throw new GenomeFormatException($"DT01 section {sectionIndex} points outside the file.");
            }

            var position = (int)sectionOffset + 8;
            var classHash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
            var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            position += 8;

            if (entryCount > MaxEntryCount - totalEntries)
            {
                throw new GenomeFormatException($"DT01 section {sectionIndex} declares too many objects.");
            }

            var entries = new GenomeHeaderEntry[(int)entryCount];
            for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                if (position + 4 > data.Length)
                {
                    throw new GenomeFormatException($"DT01 entry {entryIndex} is truncated.");
                }

                var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
                position += 4;
                if (nameLength == 0 || nameLength > MaxNameLength)
                {
                    throw new GenomeFormatException($"DT01 entry {entryIndex} has an invalid name length.");
                }

                if (position + (int)nameLength + 12L > data.Length)
                {
                    throw new GenomeFormatException($"DT01 entry {entryIndex} name or metadata is truncated.");
                }

                var name = Encoding.Latin1.GetString(data, position, (int)nameLength);
                position += (int)nameLength;
                var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(position));
                position += 8;
                var objectSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
                position += 4;

                var genomeObject = GenomeObject.Parse(data, position);
                if (genomeObject.Size != objectSize)
                {
                    throw new GenomeFormatException(
                        $"DT01 entry '{name}' declares {objectSize} bytes but its object uses {genomeObject.Size}.");
                }

                entries[entryIndex] = new GenomeHeaderEntry(name, timestamp, genomeObject);
                position += genomeObject.Size;
            }

            sections[sectionIndex] = new GenomeHeaderSection(classHash, entries);
            totalEntries += (int)entryCount;
        }

        return new GenomeHeaderDocument(headerValue, sections);
    }
}
