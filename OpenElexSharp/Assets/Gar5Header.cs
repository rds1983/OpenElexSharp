using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// The common <c>GAR5</c> resource wrapper used by compiled ELEX assets.
/// </summary>
/// <param name="Value">The header value observed at offset 4, normally <c>0x20</c>.</param>
/// <param name="Tag">Four-character resource tag, for example <c>RT02</c> or <c>STB\x07</c>.</param>
/// <param name="ClassHash">Class hash for <c>RT02</c> databases; otherwise 0.</param>
/// <param name="ClassTag">Class tag for <c>RT02</c> databases, for example <c>SN11</c>.</param>
/// <param name="ResourceCount">Number of database records for <c>RT02</c>; otherwise 0.</param>
/// <param name="PayloadOffset">Offset where the tag-specific payload starts.</param>
public sealed record Gar5Header(
    uint Value,
    string Tag,
    uint ClassHash,
    string ClassTag,
    uint ResourceCount,
    int PayloadOffset)
{
    /// <summary>"GAR5" as a little-endian integer.</summary>
    public const uint Magic = 0x35524147;

    /// <summary>Reads and validates a GAR5 wrapper from a stream.</summary>
    /// <exception cref="Gar5FormatException">The magic is wrong or the header is truncated.</exception>
    public static Gar5Header Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> prefix = stackalloc byte[8];
        ReadExactly(stream, prefix);
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (magic != Magic)
        {
            throw new Gar5FormatException("The stream does not start with a GAR5 header.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(prefix[4..]);
        Span<byte> tagBytes = stackalloc byte[4];
        ReadExactly(stream, tagBytes);
        var tag = Encoding.Latin1.GetString(tagBytes);

        if (tag == "RT02")
        {
            Span<byte> metadata = stackalloc byte[12];
            ReadExactly(stream, metadata);
            var classHash = BinaryPrimitives.ReadUInt32LittleEndian(metadata);
            var classTag = Encoding.Latin1.GetString(metadata[4..8]);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(metadata[8..]);
            return new Gar5Header(value, tag, classHash, classTag, count, PayloadOffset: 24);
        }

        Span<byte> payloadHeader = stackalloc byte[20];
        ReadExactly(stream, payloadHeader);
        return new Gar5Header(value, tag, 0, string.Empty, 0, PayloadOffset: 32);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                throw new Gar5FormatException("The GAR5 header is truncated.");
            }

            offset += read;
        }
    }
}
