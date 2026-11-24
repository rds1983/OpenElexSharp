using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

/// <summary>Builds minimal GEC2 objects and property payloads for metadata tests.</summary>
internal static class TestGenomePropertyBuilder
{
    public static byte[] Object(
        uint classHash,
        ushort version,
        params (string Name, byte[] Data)[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var bodySize = 2 + properties.Sum(property => 12 + property.Data.Length) + 4;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GEC2"));
        writer.Write(classHash);
        writer.Write(version);
        writer.Write((uint)bodySize);
        writer.Write((ushort)properties.Length);
        foreach (var property in properties)
        {
            writer.Write(Djb2Hash.ComputeCaseSensitive("blob"));
            writer.Write(Djb2Hash.ComputeCaseSensitive(property.Name));
            writer.Write((uint)property.Data.Length);
            writer.Write(property.Data);
        }

        writer.Write(0u);
        return output.ToArray();
    }

    public static byte[] Array(params byte[][] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
        writer.Write((uint)items.Length);
        foreach (var item in items)
        {
            writer.Write(item);
        }

        return output.ToArray();
    }

    public static byte[] UInt32(uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return data;
    }

    public static byte[] Int32(int value) => UInt32(unchecked((uint)value));

    public static byte[] Single(float value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(data, value);
        return data;
    }

    public static byte[] Floats(params float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var data = new byte[values.Length * 4];
        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(index * 4), values[index]);
        }

        return data;
    }

    public static byte[] String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.Latin1.GetBytes(value);
        var data = new byte[2 + bytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)bytes.Length);
        bytes.CopyTo(data, 2);
        return data;
    }
}
