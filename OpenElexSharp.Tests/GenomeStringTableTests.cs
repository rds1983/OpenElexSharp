using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GenomeStringTableTests
{
    [Fact]
    public void Parse_ValidTable_ReadsStrings()
    {
        var data = TestGenomeBuilder.CreateStringTable(["class gCQuest", "Player_CollectElexBattery"]);

        var table = GenomeStringTable.Parse(data);

        Assert.Equal(1, table.Version);
        Assert.Equal(
            ["class gCQuest", "Player_CollectElexBattery"],
            table.Strings);
        Assert.True(GenomeStringTable.IsGenomeFile(data));
        Assert.True(GenomeStringTable.TryParse(data, out var sameTable));
        Assert.Equal(table.Strings, sameTable!.Strings);
    }

    [Fact]
    public void Parse_WrongMagic_Throws()
    {
        var data = TestGenomeBuilder.CreateStringTable([]);
        data[0] = (byte)'X';

        Assert.False(GenomeStringTable.TryParse(data, out _));
        Assert.Throws<GenomeFormatException>(() => GenomeStringTable.Parse(data));
    }

    [Fact]
    public void Parse_MissingFooter_Throws()
    {
        var data = TestGenomeBuilder.CreateStringTable(["a"]);
        BitConverter.GetBytes(0u).CopyTo(data, 14);

        Assert.Throws<GenomeFormatException>(() => GenomeStringTable.Parse(data));
    }

    [Fact]
    public void Parse_Truncated_Throws()
    {
        Assert.Throws<GenomeFormatException>(() => GenomeStringTable.Parse([1, 2, 3]));
    }

    [Fact]
    public void Parse_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GenomeStringTable.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => GenomeStringTable.TryParse(null!, out _));
    }
}
