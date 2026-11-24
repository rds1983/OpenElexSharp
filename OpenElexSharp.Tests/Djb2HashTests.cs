using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class Djb2HashTests
{
    [Theory]
    [InlineData("INFO_PANKRATZI7_00002094_English", 0xB1CCBF72u)]
    [InlineData("INFO_IKENI7_00001457_English", 0xD0DC3290u)]
    [InlineData("INFO_WEIDMANNX6_00007944_English", 0x901F6271u)]
    public void Compute_KnownResourceNames_ReturnsExpectedHash(string name, uint expected)
    {
        Assert.Equal(expected, Djb2Hash.Compute(name));
    }

    [Fact]
    public void Compute_IsCaseInsensitive()
    {
        Assert.Equal(
            Djb2Hash.Compute("INFO_Test_English"),
            Djb2Hash.Compute("info_test_english"));
    }

    [Fact]
    public void Compute_EmptyString_ReturnsDjb2Seed()
    {
        Assert.Equal(5381u, Djb2Hash.Compute(string.Empty));
    }

    [Fact]
    public void Compute_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Djb2Hash.Compute(null!));
    }

    [Fact]
    public void FormatHash_FormatsEightLowercaseDigits()
    {
        Assert.Equal("d0dc3290", ResourceDatabase.FormatHash(0xD0DC3290));
    }
}
