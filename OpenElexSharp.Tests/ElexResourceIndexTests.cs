using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexResourceIndexTests
{
    [Fact]
    public void Build_ResolvesResourceNameToHashedRom()
    {
        using var temp = new TempDirectory();
        var resourceName = "TEST_SOUND";
        var rom = CreateFixture(temp, resourceName);
        var (assets, catalog) = CreateCatalog(temp);

        var index = ElexResourceIndex.Build(catalog, assets);

        var resolved = Assert.Single(index.Resources);
        Assert.Equal(resourceName, resolved.Name);
        Assert.Equal("snd", resolved.TypeToken);
        Assert.Equal("en", resolved.Language);
        Assert.Equal(0, resolved.Generation);
        Assert.Equal("data/compiled/0_en_snd/TEST_SOUND.elexsnd", resolved.LogicalPath);
        Assert.Equal(rom.VirtualPath, resolved.VirtualPath);
        Assert.Equal("romdata".Length, resolved.Size);
        Assert.Equal("data/compiled/0_en_snd/w_snd_0_en.db", resolved.DatabasePath);
        Assert.Equal(0x12345678u, resolved.ClassHash);
        Assert.Equal("SN11", resolved.ClassTag);
        Assert.Equal(24, resolved.DatabaseEntryOffset);
        Assert.True(resolved.DatabaseEntrySize > 0);
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void TryResolve_IsCaseInsensitiveAndQualified()
    {
        using var temp = new TempDirectory();
        CreateFixture(temp, "TEST_SOUND");
        var (assets, catalog) = CreateCatalog(temp);
        var index = ElexResourceIndex.Build(catalog, assets);

        Assert.True(index.TryResolve("test_sound", out var simple));
        Assert.Equal("TEST_SOUND", simple!.Name);
        Assert.True(index.TryResolve("TEST_SOUND", "SND", "EN", out var qualified));
        Assert.Equal(simple.VirtualPath, qualified!.VirtualPath);
        Assert.False(index.TryResolve("TEST_SOUND", "dlg", "en", out _));
        Assert.False(index.TryResolve("UNKNOWN", out _));
    }

    [Fact]
    public void Build_MissingRom_SkipsResource()
    {
        using var temp = new TempDirectory();
        WriteDatabase(temp, new TestResourceDatabaseBuilder().AddResource("MISSING_ROM"));
        var (assets, catalog) = CreateCatalog(temp);

        var index = ElexResourceIndex.Build(catalog, assets);

        Assert.Empty(index.Resources);
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void Build_MalformedDatabase_IsReportedAndSkipped()
    {
        using var temp = new TempDirectory();
        CreateFixture(temp, "TEST_SOUND");
        var brokenPath = Path.Combine(temp.Root, "data", "compiled", "0_other", "w_other_0_en.db");
        Directory.CreateDirectory(Path.GetDirectoryName(brokenPath)!);
        File.WriteAllText(brokenPath, "not a database");
        var (assets, catalog) = CreateCatalog(temp);

        var index = ElexResourceIndex.Build(catalog, assets);

        Assert.Single(index.Resources);
        var diagnostic = Assert.Single(index.Diagnostics);
        Assert.Contains("w_other_0_en.db", diagnostic);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        using var temp = new TempDirectory();
        var (assets, catalog) = CreateCatalog(temp);

        Assert.Throws<ArgumentNullException>(() => ElexResourceIndex.Build(null!, assets));
        Assert.Throws<ArgumentNullException>(() => ElexResourceIndex.Build(catalog, null!));
    }

    [Fact]
    public void AssetsResourcesCommand_PrintsResolvedResource()
    {
        using var temp = new TempDirectory();
        CreateFixture(temp, "TEST_SOUND");
        var (assets, catalog) = CreateCatalog(temp);
        var command = AssetCommandLine.Parse(["assets", "resources", "--type", "snd"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("TEST_SOUND", text, StringComparison.Ordinal);
        Assert.Contains("elexsnd", text, StringComparison.Ordinal);
        Assert.Contains("Showing 1 of 1 resource(s)", error.ToString(), StringComparison.Ordinal);
    }

    private static AssetCatalogEntry CreateFixture(TempDirectory temp, string resourceName)
    {
        WriteDatabase(temp, new TestResourceDatabaseBuilder().AddResource(resourceName));
        var hash = ResourceDatabase.FormatHash(Djb2Hash.Compute(resourceName));
        var romPath = $"data/compiled/0_en_snd/0/{hash[0]}/w_snd_0_en_{hash}.rom";
        temp.CreateFile(romPath, "romdata");
        return new AssetCatalogEntry(romPath, 7, "G3:data/compiled", ".rom", "en", true, false);
    }

    private static void WriteDatabase(TempDirectory temp, TestResourceDatabaseBuilder builder)
    {
        var databaseBytes = builder.Build();
        var path = Path.Combine(temp.Root, "data", "compiled", "0_en_snd", "w_snd_0_en.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, databaseBytes);
    }

    private static (AssetFileSystem Assets, AssetCatalog Catalog) CreateCatalog(TempDirectory temp)
    {
        var assets = new AssetFileSystem(
        [
            new LooseFolderSource(
                "G3:data/compiled",
                "data/compiled",
                Path.Combine(temp.Root, "data", "compiled")),
        ]);
        return (assets, AssetCatalog.Build(assets));
    }
}
