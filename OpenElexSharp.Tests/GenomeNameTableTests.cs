using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GenomeNameTableTests
{
    [Theory]
    [InlineData(0x955C5FD7u, "class gCQuest")]
    [InlineData(0x1BC7FE51u, "class gCInfo")]
    [InlineData(0x36182D05u, "class eCDynamicEntity")]
    public void TryGet_KnownHashes_ReturnsNames(uint hash, string expected)
    {
        Assert.True(GenomeNameTable.TryGet(hash, out var name));
        Assert.Equal(expected, name);
    }

    [Fact]
    public void TryGet_UnknownHash_ReturnsFalse()
    {
        Assert.False(GenomeNameTable.TryGet(0xDEADBEEF, out _));
        Assert.Equal("0xdeadbeef", GenomeNameTable.GetNameOrHash(0xDEADBEEF));
    }

    [Fact]
    public void Count_ContainsPracticalRegistry()
    {
        Assert.True(GenomeNameTable.Count > 18_000);
    }
}
