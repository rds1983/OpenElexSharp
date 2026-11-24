using OpenElexSharp.Assets;

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
