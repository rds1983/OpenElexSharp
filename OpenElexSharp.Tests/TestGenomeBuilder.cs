using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Tests;

internal static class TestGenomeBuilder
{
    public static byte[] CreateObject(
        uint classHash,
        ushort version,
        (uint TypeHash, uint NameHash, byte[] Data)[] properties,
        (uint ClassHash, ushort Version, byte[] Data)[] classData)
    {
        var bodySize = 2
            + properties.Sum(property => 12 + property.Data.Length)
            + 4
            + classData.Sum(item => 10 + item.Data.Length);

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GEC2"));
        writer.Write(classHash);
        writer.Write(version);
        writer.Write((uint)bodySize);
        writer.Write((ushort)properties.Length);
        foreach (var property in properties)
        {
            writer.Write(property.TypeHash);
            writer.Write(property.NameHash);
            writer.Write((uint)property.Data.Length);
            writer.Write(property.Data);
        }

        writer.Write((uint)classData.Length);
        foreach (var item in classData)
        {
            writer.Write(item.ClassHash);
            writer.Write(item.Version);
            writer.Write((uint)item.Data.Length);
            writer.Write(item.Data);
        }

        return output.ToArray();
    }

    public static byte[] CreateHeaderDocument(
        (uint ClassHash, (string Name, ulong Timestamp, byte[] Object)[] Entries)[] sections)
    {
        var sectionOffsets = new int[sections.Length];
        var sectionData = new byte[sections.Length][];
        var offset = 24 + (sections.Length * 4);
        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
            writer.Write(0L);
            writer.Write(section.ClassHash);
            writer.Write((uint)section.Entries.Length);
            foreach (var entry in section.Entries)
            {
                var name = Encoding.Latin1.GetBytes(entry.Name);
                writer.Write((uint)name.Length);
                writer.Write(name);
                writer.Write(entry.Timestamp);
                writer.Write((uint)entry.Object.Length);
                writer.Write(entry.Object);
            }

            sectionData[index] = output.ToArray();
            sectionOffsets[index] = offset;
            offset += sectionData[index].Length;
        }

        using var document = new MemoryStream();
        using var documentWriter = new BinaryWriter(document, Encoding.Latin1, leaveOpen: true);
        documentWriter.Write(Encoding.ASCII.GetBytes("GAR5"));
        documentWriter.Write(0x20u);
        documentWriter.Write(Encoding.ASCII.GetBytes("DT01"));
        documentWriter.Write(0u);
        documentWriter.Write(1u);
        documentWriter.Write((uint)sections.Length);
        foreach (var sectionOffset in sectionOffsets)
        {
            documentWriter.Write((uint)sectionOffset);
        }

        foreach (var section in sectionData)
        {
            documentWriter.Write(section);
        }

        return document.ToArray();
    }

    public static byte[] CreateStringTable(string[] strings)
    {
        const int footerOffset = 14;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GENOMFLE"));
        writer.Write((ushort)1);
        writer.Write((uint)footerOffset);
        writer.Write(0xDEADBEEFu);
        writer.Write((byte)1);
        writer.Write((uint)strings.Length);
        foreach (var value in strings)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        return output.ToArray();
    }

    public static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
}
