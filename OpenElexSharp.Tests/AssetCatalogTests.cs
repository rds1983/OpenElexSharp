using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class AssetCatalogTests
{
    [Fact]
    public void Build_ComputesTotalsExtensionsAndSources()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("data/extern/assets/texture.dds", "1234");
        temp.CreateFile("data/extern/readme.txt", "12");
        temp.CreateFile("system/game.exe", "exe");

        var assets = new AssetFileSystem(
        [
            new LooseFolderSource("G3:data/extern", "data/extern", Path.Combine(temp.Root, "data/extern")),
            new LooseFolderSource("G3:scripts", "scripts", Path.Combine(temp.Root, "system")),
        ]);
        var catalog = AssetCatalog.Build(assets);

        Assert.Equal(3, catalog.Entries.Count);
        Assert.True(catalog.TryGetEntry("DATA/EXTERN/assets/texture.DDS", out var texture));
        Assert.Equal(4, texture!.Length);
        Assert.Equal(".dds", texture.Extension);
        Assert.False(texture.IsPacked);
        Assert.False(texture.IsExecutable);
        Assert.Null(texture.Language);

        Assert.True(catalog.TryGetEntry("scripts/game.exe", out var executable));
        Assert.True(executable!.IsExecutable);

        var stats = catalog.GetStatistics();
        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(0, stats.PackedCount);
        Assert.Equal(3, stats.LooseCount);
        Assert.Equal(1, stats.ExecutableCount);
        Assert.Equal(9, stats.TotalBytes);
        Assert.Contains(stats.Extensions, group => group.Name == ".dds" && group.Count == 1 && group.Bytes == 4);
        Assert.Contains(stats.Extensions, group => group.Name == ".exe" && group.Count == 1);
        Assert.Contains(stats.Sources, group => group.Name == "G3:data/extern" && group.Count == 2);
        Assert.Contains(stats.Sources, group => group.Name == "G3:scripts" && group.Count == 1);
    }

    [Fact]
    public void Build_PackedEntry_InfersLanguageAndSource()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        File.WriteAllBytes(
            Path.Combine(temp.Root, "paks", "c_1_en.pak"),
            new TestPakBuilder().AddFile("textures/a.dds", "12345").Build());
        var source = new PackedAssetSource(
            "G3:data/compiled",
            "data/compiled",
            Path.Combine(temp.Root, "paks"));

        var catalog = AssetCatalog.Build(new AssetFileSystem([source]));

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("data/compiled/textures/a.dds", entry.VirtualPath);
        Assert.Equal(5, entry.Length);
        Assert.True(entry.IsPacked);
        Assert.Equal("en", entry.Language);
        Assert.Contains("c_1_en.pak", entry.SourceName);
    }

    [Fact]
    public void Build_Overlay_UsesWinningEntry()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("paks");
        File.WriteAllBytes(
            Path.Combine(temp.Root, "paks", "c_1_en.pak"),
            new TestPakBuilder().AddFile("shared.bin", "base").Build());
        File.WriteAllBytes(
            Path.Combine(temp.Root, "paks", "m_9_mod.pak"),
            new TestPakBuilder().AddFile("shared.bin", "mod-content").Build());
        var source = new PackedAssetSource(
            "G3:data/compiled",
            "data/compiled",
            Path.Combine(temp.Root, "paks"));

        var catalog = AssetCatalog.Build(new AssetFileSystem([source]));

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("mod-content".Length, entry.Length);
        Assert.Contains("m_9_mod.pak", entry.SourceName);
        Assert.Null(entry.Language);
    }

    [Fact]
    public void TryGetEntry_Missing_ReturnsFalse()
    {
        using var temp = new TempDirectory();
        var catalog = AssetCatalog.Build(new AssetFileSystem(
            [new LooseFolderSource("test", string.Empty, temp.Root)]));

        Assert.False(catalog.TryGetEntry("missing.txt", out _));
    }

    [Fact]
    public void Build_NullAssets_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AssetCatalog.Build(null!));
    }

    [Theory]
    [InlineData(".exe", true)]
    [InlineData(".DLL", true)]
    [InlineData("dll", true)]
    [InlineData(".dds", false)]
    [InlineData("(none)", false)]
    public void IsExecutable_ClassifiesExtensions(string extension, bool expected)
    {
        Assert.Equal(expected, AssetKinds.IsExecutable(extension));
    }
}
