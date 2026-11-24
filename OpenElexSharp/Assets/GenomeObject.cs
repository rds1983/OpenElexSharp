using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>One property of a GEC2 object. Data is preserved verbatim.</summary>
/// <param name="TypeHash">Case-sensitive DJB2 hash of the property type name.</param>
/// <param name="NameHash">Case-sensitive DJB2 hash of the property name.</param>
/// <param name="Data">Raw property payload.</param>
public sealed record GenomeProperty(uint TypeHash, uint NameHash, byte[] Data);

/// <summary>One class-data block attached to a GEC2 object.</summary>
/// <param name="ClassHash">Case-sensitive DJB2 hash of the class name.</param>
/// <param name="Version">Class version.</param>
/// <param name="Data">Raw class-data payload.</param>
public sealed record GenomeClassData(uint ClassHash, ushort Version, byte[] Data);

/// <summary>Kind of a nested object found inside known class data.</summary>
public enum GenomeNestedObjectKind
{
    Command,
    Condition,
}

/// <summary>A nested object decoded from known class data.</summary>
public sealed record GenomeNestedObject(GenomeNestedObjectKind Kind, GenomeObject Object);

/// <summary>
/// A Genome Engine GEC2 serialized object. Unknown properties and class data are preserved as raw
/// bytes so later layers can decode formats that are not implemented yet.
/// </summary>
public sealed class GenomeObject
{
    /// <summary>"GEC2" as a little-endian integer.</summary>
    public const uint Magic = 0x32434547;

    /// <summary>Hash of <c>class gCInfo</c>; its class data contains nested command/condition objects.</summary>
    public const uint InfoClassHash = 0x1BC7FE51;

    /// <summary>Bytes before the property count: magic, class hash, version and body size.</summary>
    public const int HeaderSize = 14;

    private const uint MaxBodySize = 256 * 1024 * 1024;
    private const uint MaxDataCount = 1_000_000;
    private const int MaxNestedDepth = 16;

    private GenomeObject(
        int offset,
        uint classHash,
        ushort version,
        uint bodySize,
        GenomeProperty[] properties,
        GenomeClassData[] classData,
        GenomeNestedObject[] nestedObjects)
    {
        Offset = offset;
        ClassHash = classHash;
        Version = version;
        BodySize = bodySize;
        Properties = properties;
        ClassData = classData;
        NestedObjects = nestedObjects;
    }

    /// <summary>Offset of the GEC2 magic in the source buffer.</summary>
    public int Offset { get; }

    /// <summary>Case-sensitive DJB2 hash of the class name.</summary>
    public uint ClassHash { get; }

    /// <summary>Serialized class version.</summary>
    public ushort Version { get; }

    /// <summary>Body size from the header. Total object size is <see cref="HeaderSize"/> + this value.</summary>
    public uint BodySize { get; }

    /// <summary>Total object size in bytes.</summary>
    public int Size => HeaderSize + (int)BodySize;

    /// <summary>Properties in stream order.</summary>
    public IReadOnlyList<GenomeProperty> Properties { get; }

    /// <summary>Attached class-data blocks in stream order.</summary>
    public IReadOnlyList<GenomeClassData> ClassData { get; }

    /// <summary>Nested objects decoded from known class data (currently <c>gCInfo</c> commands/conditions).</summary>
    public IReadOnlyList<GenomeNestedObject> NestedObjects { get; }

    /// <summary>Parses one object at <paramref name="offset"/>.</summary>
    /// <exception cref="GenomeFormatException">The object is malformed or truncated.</exception>
    public static GenomeObject Parse(byte[] data, int offset) => ParseCore(data, offset, depth: 0);

    private static GenomeObject ParseCore(byte[] data, int offset, int depth)
    {
        if (depth > MaxNestedDepth)
        {
            throw new GenomeFormatException("GEC2 nesting exceeds the supported depth.");
        }

        ArgumentNullException.ThrowIfNull(data);
        if (offset < 0 || offset > data.Length - HeaderSize)
        {
            throw new GenomeFormatException("The GEC2 object header is truncated.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) != Magic)
        {
            throw new GenomeFormatException("The object does not start with GEC2.");
        }

        var classHash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
        var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 8));
        var bodySize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 10));

        if (bodySize < 2 || bodySize > MaxBodySize)
        {
            throw new GenomeFormatException($"The GEC2 object declares an invalid body size ({bodySize}).");
        }

        var objectEnd = (long)offset + HeaderSize + bodySize;
        if (objectEnd > data.Length)
        {
            throw new GenomeFormatException("The GEC2 object extends beyond the buffer.");
        }

        var end = (int)objectEnd;
        var position = offset + HeaderSize;
        var propertyCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
        position += 2;

        var properties = new GenomeProperty[propertyCount];
        for (var index = 0; index < properties.Length; index++)
        {
            if (position + 12 > end)
            {
                throw new GenomeFormatException($"GEC2 property {index} is truncated.");
            }

            var typeHash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
            var nameHash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 8));
            position += 12;

            if (dataSize > end - position)
            {
                throw new GenomeFormatException($"GEC2 property {index} extends beyond the object.");
            }

            properties[index] = new GenomeProperty(
                typeHash,
                nameHash,
                data.AsSpan(position, (int)dataSize).ToArray());
            position += (int)dataSize;
        }

        if (position + 4 > end)
        {
            throw new GenomeFormatException("The GEC2 class-data count is missing.");
        }

        var classDataCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
        position += 4;
        if (classDataCount > MaxDataCount)
        {
            throw new GenomeFormatException($"The GEC2 object declares too many class-data blocks ({classDataCount}).");
        }

        var classData = new GenomeClassData[(int)classDataCount];
        for (var index = 0; index < classData.Length; index++)
        {
            if (position + 10 > end)
            {
                throw new GenomeFormatException($"GEC2 class-data block {index} is truncated.");
            }

            var childHash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
            var childVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 4));
            var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 6));
            position += 10;

            if (dataSize > end - position)
            {
                throw new GenomeFormatException($"GEC2 class-data block {index} extends beyond the object.");
            }

            classData[index] = new GenomeClassData(
                childHash,
                childVersion,
                data.AsSpan(position, (int)dataSize).ToArray());
            position += (int)dataSize;
        }

        if (position != end)
        {
            throw new GenomeFormatException(
                $"GEC2 object ended at offset {position}, expected {end}.");
        }

        var nestedObjects = DecodeNestedObjects(classData, depth);
        return new GenomeObject(
            offset,
            classHash,
            version,
            bodySize,
            properties,
            classData,
            nestedObjects);
    }

    private static GenomeNestedObject[] DecodeNestedObjects(GenomeClassData[] classData, int depth)
    {
        var nested = new List<GenomeNestedObject>();
        foreach (var item in classData)
        {
            if (item.ClassHash != InfoClassHash || item.Version != 1)
            {
                continue;
            }

            if (item.Data.Length < 4)
            {
                throw new GenomeFormatException("gCInfo class data is missing its command counts.");
            }

            var commandCount = BinaryPrimitives.ReadUInt16LittleEndian(item.Data);
            var conditionCount = BinaryPrimitives.ReadUInt16LittleEndian(item.Data.AsSpan(2));
            var position = 4;
            for (var index = 0; index < commandCount; index++)
            {
                var child = ParseCore(item.Data, position, depth + 1);
                nested.Add(new GenomeNestedObject(GenomeNestedObjectKind.Command, child));
                position += child.Size;
            }

            for (var index = 0; index < conditionCount; index++)
            {
                var child = ParseCore(item.Data, position, depth + 1);
                nested.Add(new GenomeNestedObject(GenomeNestedObjectKind.Condition, child));
                position += child.Size;
            }

            if (position != item.Data.Length)
            {
                throw new GenomeFormatException("gCInfo class data has trailing bytes.");
            }
        }

        return [.. nested];
    }
}
