using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>Decoded enum property value.</summary>
public sealed record GenomeEnumValue(uint MemberHash, int Value, string? MemberName);

/// <summary>
/// A decoded Genome property value. When <see cref="Value"/> is <see langword="null"/> the raw
/// bytes should be shown; this happens for unknown or not-yet-decoded types.
/// </summary>
public sealed record GenomeValue(string TypeName, object? Value, byte[] RawData);

/// <summary>Decodes common Genome property types.</summary>
public static class GenomePropertyDecoder
{
    private static readonly HashSet<string> GuidTypes = new(StringComparer.Ordinal)
    {
        "class bCGuid",
        "class eCTemplateEntityProxy",
        "class eCEntityProxy",
    };

    private static readonly HashSet<string> VersionedStringProxyTypes = new(StringComparer.Ordinal)
    {
        "class eCScriptProxyScript",
        "class gCScriptProxyAIFunction",
        "class gCScriptProxyAIState",
        "class gCUIDialogCloseScriptProxy",
        "class gCUICanExecuteScriptProxy",
        "class gCUIExecuteScriptProxy",
        "class gCUICancelEventScriptProxy",
        "class gCUIEventScriptProxy",
        "class gCUIMessageScriptProxy",
        "class gCUIDialogClosingScriptProxy",
        "class eCGuiBitmapProxy2",
        "class eCGuiFontProxy2",
        "class eCEntityStringProxy",
    };

    private static readonly HashSet<string> PlainStringProxyTypes = new(StringComparer.Ordinal)
    {
        "class eTResourceProxy",
        "class eCWeatherEnvironmentProxy",
        "class gCEffectProxy",
        "class eCImageEffectProxy",
        "class eCWeatherStatesProxy",
        "class gCFocusModeProxy2",
        "class gCMovementSpeciesProxy",
        "class eCParticleSystemProxy",
        "class gCUIBindingProxy",
        "class gCUISoundThemeProxy",
        "class eCVegetationDefinitionProxy",
        "class gCQuestProxy",
    };

    private const int MaxArrayCount = 1_000_000;

    /// <summary>Decodes a property using the embedded Genome name table.</summary>
    public static GenomeValue Decode(GenomeProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return Decode(property, hash => GenomeNameTable.TryGet(hash, out var name) ? name : null);
    }

    /// <summary>Decodes a property using a custom name resolver.</summary>
    public static GenomeValue Decode(
        GenomeProperty property,
        Func<uint, string?> nameResolver)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(nameResolver);

        var typeName = nameResolver(property.TypeHash) ?? $"0x{property.TypeHash:x8}";
        var value = DecodeValue(typeName, property.Data, nameResolver);
        return new GenomeValue(typeName, value, property.Data);
    }

    /// <summary>Formats a decoded value for display.</summary>
    public static string Format(GenomeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Value is null
            ? $"<raw {value.RawData.Length} byte(s)>"
            : FormatValue(value.Value);
    }

    private static object? DecodeValue(
        string typeName,
        ReadOnlySpan<byte> data,
        Func<uint, string?> nameResolver)
    {
        if (typeName == "blob")
        {
            return data.ToArray();
        }

        if (typeName.StartsWith("enum ", StringComparison.Ordinal))
        {
            RequireSize(data, 8, typeName);
            var memberHash = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var enumValue = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
            var memberName = nameResolver(memberHash);
            return new GenomeEnumValue(memberHash, enumValue, memberName);
        }

        var arrayElement = GetArrayElementType(typeName);
        if (arrayElement is not null)
        {
            return DecodeArray(arrayElement, data, nameResolver);
        }

        if (typeName.EndsWith("LocString", StringComparison.Ordinal))
        {
            return ReadString(data, typeName, skipBytes: 4, versioned: false);
        }

        if (VersionedStringProxyTypes.Contains(typeName))
        {
            return ReadString(data, typeName, skipBytes: 0, versioned: true);
        }

        if (PlainStringProxyTypes.Contains(typeName) ||
            typeName.StartsWith("class eTResourceProxy", StringComparison.Ordinal))
        {
            return ReadString(data, typeName, skipBytes: 0, versioned: false);
        }

        if (GuidTypes.Contains(typeName))
        {
            RequireSize(data, 16, typeName);
            return new Guid(data);
        }

        return typeName switch
        {
            "class bCString" => ReadString(data, typeName, 0, false),
            "bool" => ReadUInt32(data, typeName) != 0,
            "float" => ReadFloat(data, typeName),
            "char" => ReadChar(data, typeName),
            "signed char" => unchecked((sbyte)ReadByte(data, typeName)),
            "unsigned char" => ReadByte(data, typeName),
            "short" => ReadInt16(data, typeName),
            "unsigned short" => ReadUInt16(data, typeName),
            "int" or "long" => ReadInt32(data, typeName),
            "unsigned int" or "unsigned long" => ReadUInt32(data, typeName),
            "__int64" => ReadInt64(data, typeName),
            "unsigned __int64" => ReadUInt64(data, typeName),
            "class bCBox" => ReadFloats(data, typeName, 6),
            "class bCEulerAngles" => ReadFloats(data, typeName, 3),
            "class bCFloatColor" => ReadFloats(data, typeName, 3),
            "class bCMatrix" => ReadFloats(data, typeName, 16),
            "class bCQuaternion" => ReadFloats(data, typeName, 4),
            "class bCRange1" => ReadFloats(data, typeName, 2),
            "class bCVector" => ReadFloats(data, typeName, 3),
            "class bCVector2" => ReadFloats(data, typeName, 2),
            "class bCDateTime" => ReadDateTime(data, typeName),
            _ => null,
        };
    }

    private static string? GetArrayElementType(string typeName)
    {
        var open = typeName.IndexOf('<');
        if (open < 0 || !typeName.EndsWith('>'))
        {
            return null;
        }

        var container = typeName[..open];
        return container.Contains("Array", StringComparison.Ordinal)
            ? typeName[(open + 1)..^1]
            : null;
    }

    private static GenomeValue[] DecodeArray(
        string elementType,
        ReadOnlySpan<byte> data,
        Func<uint, string?> nameResolver)
    {
        if (data.Length < 4)
        {
            throw new GenomeFormatException($"Array '{elementType}' is missing its count.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count > MaxArrayCount)
        {
            throw new GenomeFormatException($"Array '{elementType}' declares too many items ({count}).");
        }

        if (count == 0)
        {
            if (data.Length != 4)
            {
                throw new GenomeFormatException($"Empty array '{elementType}' has trailing data.");
            }

            return [];
        }

        var payloadLength = data.Length - 4;
        var chunkSize = payloadLength / (int)count;
        var remainder = payloadLength % (int)count;
        var result = new GenomeValue[(int)count];
        for (var index = 0; index < result.Length; index++)
        {
            var size = chunkSize + (index == result.Length - 1 ? remainder : 0);
            var item = data.Slice(4 + (index * chunkSize), size);
            object? value;
            try
            {
                value = DecodeValue(elementType, item, nameResolver);
            }
            catch (GenomeFormatException)
            {
                value = null;
            }

            result[index] = new GenomeValue(elementType, value, item.ToArray());
        }

        return result;
    }

    private static string ReadString(
        ReadOnlySpan<byte> data,
        string typeName,
        int skipBytes,
        bool versioned)
    {
        var position = 0;
        if (versioned)
        {
            if (data.Length < 2)
            {
                throw new GenomeFormatException($"'{typeName}' is missing its version.");
            }

            position += 2;
        }

        position += skipBytes;
        if (position + 2 > data.Length)
        {
            throw new GenomeFormatException($"'{typeName}' is missing its string length.");
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        position += 2;
        if (length > data.Length - position || position + length != data.Length)
        {
            throw new GenomeFormatException($"'{typeName}' has an invalid string length.");
        }

        return Encoding.Latin1.GetString(data[position..(position + length)]);
    }

    private static object ReadDateTime(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 8, typeName);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(data);
        try
        {
            return DateTime.FromFileTimeUtc((long)value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return value;
        }
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> data, string typeName, int count)
    {
        RequireSize(data, count * 4, typeName);
        var result = new float[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(data[(index * 4)..]));
        }

        return result;
    }

    private static float ReadFloat(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 4, typeName);
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
    }

    private static char ReadChar(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 1, typeName);
        return (char)data[0];
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 1, typeName);
        return data[0];
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 2, typeName);
        return BinaryPrimitives.ReadInt16LittleEndian(data);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 2, typeName);
        return BinaryPrimitives.ReadUInt16LittleEndian(data);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 4, typeName);
        return BinaryPrimitives.ReadInt32LittleEndian(data);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 4, typeName);
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }

    private static long ReadInt64(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 8, typeName);
        return BinaryPrimitives.ReadInt64LittleEndian(data);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, string typeName)
    {
        RequireSize(data, 8, typeName);
        return BinaryPrimitives.ReadUInt64LittleEndian(data);
    }

    private static void RequireSize(ReadOnlySpan<byte> data, int size, string typeName)
    {
        if (data.Length != size)
        {
            throw new GenomeFormatException(
                $"'{typeName}' expects {size} byte(s), got {data.Length}.");
        }
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            string text => $"\"{Truncate(text)}\"",
            bool flag => flag ? "true" : "false",
            float number => number.ToString("G9", CultureInfo.InvariantCulture),
            double number => number.ToString("G9", CultureInfo.InvariantCulture),
            GenomeEnumValue enumValue =>
                $"{enumValue.MemberName ?? $"0x{enumValue.MemberHash:x8}"} ({enumValue.Value})",
            Guid guid => guid.ToString("B"),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            float[] floats => "(" + string.Join(", ", floats.Select(FormatFloat)) + ")",
            GenomeValue[] values => "[" + string.Join(", ", values.Take(8).Select(Format)) +
                                    (values.Length > 8 ? ", ..." : "") + "]",
            GenomeValue valueItem => Format(valueItem),
            byte[] bytes => $"<{bytes.Length} byte(s)>",
            IEnumerable<GenomeValue> values => "[" + string.Join(", ", values.Take(8).Select(Format)) +
                                               (values.Count() > 8 ? ", ..." : "") + "]",
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string FormatFloat(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private static string Truncate(string value) =>
        value.Length <= 120 ? value : value[..117] + "...";
}
