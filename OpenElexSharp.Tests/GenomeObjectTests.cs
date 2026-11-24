using OpenElexSharp.Assets;
using System.Text;

namespace OpenElexSharp.Tests;

public class GenomeObjectTests
{
    [Fact]
    public void Parse_ValidObject_ReadsPropertiesAndClassData()
    {
        var data = TestGenomeBuilder.CreateObject(
            0xAABBCCDD,
            7,
            [
                (0x11111111, 0x22222222, new byte[] { 1, 2, 3 }),
                (0x33333333, 0x44444444, []),
            ],
            [
                (0x55555555, 2, new byte[] { 9, 8 }),
            ]);

        var genomeObject = GenomeObject.Parse(data, 0);

        Assert.Equal(0xAABBCCDDu, genomeObject.ClassHash);
        Assert.Equal(7, genomeObject.Version);
        Assert.Equal(data.Length, genomeObject.Size);
        Assert.Equal(2, genomeObject.Properties.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, genomeObject.Properties[0].Data);
        Assert.Equal(0x33333333u, genomeObject.Properties[1].TypeHash);
        var classData = Assert.Single(genomeObject.ClassData);
        Assert.Equal(0x55555555u, classData.ClassHash);
        Assert.Equal(new byte[] { 9, 8 }, classData.Data);
    }

    [Fact]
    public void Parse_WithOffset_PreservesOffsetAndSize()
    {
        var objectBytes = TestGenomeBuilder.CreateObject(1, 1, [], []);
        var data = new byte[objectBytes.Length + 16];
        objectBytes.CopyTo(data, 16);

        var genomeObject = GenomeObject.Parse(data, 16);

        Assert.Equal(16, genomeObject.Offset);
        Assert.Equal(objectBytes.Length, genomeObject.Size);
    }

    [Fact]
    public void Parse_InvalidMagic_Throws()
    {
        var data = TestGenomeBuilder.CreateObject(1, 1, [], []);
        data[0] = (byte)'X';

        Assert.Throws<GenomeFormatException>(() => GenomeObject.Parse(data, 0));
    }

    [Fact]
    public void Parse_InvalidBodySize_Throws()
    {
        var data = TestGenomeBuilder.CreateObject(1, 1, [], []);
        BitConverter.GetBytes(0u).CopyTo(data, 10);

        Assert.Throws<GenomeFormatException>(() => GenomeObject.Parse(data, 0));
    }

    [Fact]
    public void Parse_GcInfoClassData_DecodesNestedCommandsAndConditions()
    {
        var command = TestGenomeBuilder.CreateObject(0x12345678, 1, [], []);
        var condition = TestGenomeBuilder.CreateObject(0x87654321, 1, [], []);
        using var classDataStream = new MemoryStream();
        using var writer = new BinaryWriter(classDataStream, Encoding.Latin1, leaveOpen: true);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(command);
        writer.Write(condition);
        var info = TestGenomeBuilder.CreateObject(
            GenomeObject.InfoClassHash,
            1,
            [],
            [(GenomeObject.InfoClassHash, 1, classDataStream.ToArray())]);

        var genomeObject = GenomeObject.Parse(info, 0);

        Assert.Equal(2, genomeObject.NestedObjects.Count);
        Assert.Equal(GenomeNestedObjectKind.Command, genomeObject.NestedObjects[0].Kind);
        Assert.Equal(0x12345678u, genomeObject.NestedObjects[0].Object.ClassHash);
        Assert.Equal(GenomeNestedObjectKind.Condition, genomeObject.NestedObjects[1].Kind);
        Assert.Equal(0x87654321u, genomeObject.NestedObjects[1].Object.ClassHash);
    }

    [Fact]
    public void Parse_GcInfoClassDataWithTrailingBytes_Throws()
    {
        var classData = new byte[] { 0, 0, 0, 0, 1, 2, 3 };
        var info = TestGenomeBuilder.CreateObject(
            GenomeObject.InfoClassHash,
            1,
            [],
            [(GenomeObject.InfoClassHash, 1, classData)]);

        Assert.Throws<GenomeFormatException>(() => GenomeObject.Parse(info, 0));
    }

    [Fact]
    public void Parse_PropertyOverrun_Throws()
    {
        var data = TestGenomeBuilder.CreateObject(
            1, 1, [(1, 2, new byte[] { 1, 2 })], []);
        // Property data size field follows the two hashes at offset 16 + 8.
        BitConverter.GetBytes(100u).CopyTo(data, 16 + 8);

        Assert.Throws<GenomeFormatException>(() => GenomeObject.Parse(data, 0));
    }

    [Fact]
    public void Parse_Truncated_Throws()
    {
        var data = TestGenomeBuilder.CreateObject(1, 1, [], []);

        Assert.Throws<GenomeFormatException>(() => GenomeObject.Parse(data[..10], 0));
    }

    [Fact]
    public void Parse_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GenomeObject.Parse(null!, 0));
    }
}
