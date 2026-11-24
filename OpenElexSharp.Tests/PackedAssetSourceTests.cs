using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class PackedAssetSourceTests
{
    [Fact]
    public void Constructor_LoadsPakUnderVirtualRoot()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(temp, "c_1_test.pak", new TestPakBuilder().AddFile("localization/en.str", "hello"));

        var source = CreateSource(temp, "paks");

        Assert.Equal(1, source.EntryCount);
        Assert.True(source.TryGetEntry("data/compiled/localization/en.str", out var entry));
        Assert.Equal(5, entry!.Length);
        Assert.Null(entry.PhysicalPath);
        Assert.Contains("c_1_test.pak", entry.SourceName);
        Assert.Equal("hello", ReadText(source, "data/compiled/localization/en.str"));
    }

    [Fact]
    public void TryGetEntry_IsCaseInsensitive()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(temp, "c_1_test.pak", new TestPakBuilder().AddFile("Localization/EN.str", "hello"));

        var source = CreateSource(temp, "paks");

        Assert.True(source.TryGetEntry("DATA/COMPILED/localization/en.STR", out _));
        Assert.True(source.TryGetEntry(@"data\compiled\localization\en.str", out _));
    }

    [Fact]
    public void Constructor_CompressedEntries_AreReadable()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        var content = string.Concat(Enumerable.Repeat("abc", 1000));
        WritePak(temp, "c_1_test.pak", new TestPakBuilder().AddFile("data.bin", content, compressed: true));

        var source = CreateSource(temp, "paks");

        Assert.Equal(content, ReadText(source, "data/compiled/data.bin"));
    }

    [Fact]
    public void Constructor_LaterPakWins()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(temp, "c_1_test.pak", new TestPakBuilder().AddFile("shared.txt", "base"));
        WritePak(temp, "m_9_mod.pak", new TestPakBuilder().AddFile("shared.txt", "mod"));

        var source = CreateSource(temp, "paks");

        Assert.Equal("mod", ReadText(source, "data/compiled/shared.txt"));
        Assert.Single(source.EnumerateEntries());
    }

    [Fact]
    public void Constructor_InvalidPak_IsSkippedAndReported()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(temp, "c_1_test.pak", new TestPakBuilder().AddFile("ok.txt", "ok"));
        File.WriteAllText(Path.Combine(temp.Root, "paks", "broken.pak"), "not a pak");

        var source = CreateSource(temp, "paks");

        Assert.Equal(1, source.EntryCount);
        Assert.True(source.TryGetEntry("data/compiled/ok.txt", out _));
        var diagnostic = Assert.Single(source.Diagnostics);
        Assert.Contains("broken.pak", diagnostic);
    }

    [Fact]
    public void Constructor_MissingFolder_IsEmptyAndReported()
    {
        using var temp = new TempDirectory();

        var source = new PackedAssetSource("test", "data/compiled", Path.Combine(temp.Root, "missing"));

        Assert.Equal(0, source.EntryCount);
        Assert.Empty(source.EnumerateEntries());
        Assert.Single(source.Diagnostics);
    }

    [Fact]
    public void Constructor_EmptyPak_IsValid()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(temp, "c_0_empty.pak", new TestPakBuilder());

        var source = CreateSource(temp, "paks");

        Assert.Equal(0, source.EntryCount);
        Assert.Empty(source.Diagnostics);
    }

    [Fact]
    public void Constructor_PakWithInvalidEntryPath_IsSkippedAndReported()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(
            temp,
            "c_1_test.pak",
            new TestPakBuilder()
                .AddFile("good.txt", "good")
                .AddFile("../evil.txt", "evil"));

        var source = CreateSource(temp, "paks");

        Assert.Equal(0, source.EntryCount);
        Assert.Empty(source.EnumerateEntries());
        Assert.Single(source.Diagnostics);
    }

    [Fact]
    public void OpenRead_MissingFile_Throws()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        WritePak(temp, "c_1_test.pak", new TestPakBuilder().AddFile("a.txt", "a"));
        var source = CreateSource(temp, "paks");

        Assert.Throws<AssetNotFoundException>(() => source.OpenRead("data/compiled/missing.txt"));
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new PackedAssetSource(" ", "data", Path.GetTempPath()));
        Assert.Throws<ArgumentNullException>(() => new PackedAssetSource("test", null!, Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => new PackedAssetSource("test", "data", " "));
    }

    private static PackedAssetSource CreateSource(TempDirectory temp, string folder) =>
        new("G3:data/compiled", "data/compiled", Path.Combine(temp.Root, folder));

    private static void WritePak(TempDirectory temp, string name, TestPakBuilder builder) =>
        File.WriteAllBytes(Path.Combine(temp.Root, "paks", name), builder.Build());

    private static string ReadText(PackedAssetSource source, string path)
    {
        using var stream = source.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
