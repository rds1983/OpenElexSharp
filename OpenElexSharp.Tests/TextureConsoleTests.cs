using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class TextureConsoleTests
{
    [Fact]
    public void TexturesCommand_InspectsAndDecodesImageResource()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("data/compiled/0_na_img/0/1");
        var imageData = TestDdsBuilder.Wrap(
            TestDdsBuilder.CreateLegacyFourCc("DXT1", 4, 4, new byte[8]));
        var hash = ResourceDatabase.FormatHash(Djb2Hash.Compute("TEST_IMG"));
        File.WriteAllBytes(
            Path.Combine(temp.Root, "data", "compiled", "0_na_img", "0", "1", $"w_img_0_na_{hash}.rom"),
            imageData);
        File.WriteAllBytes(
            Path.Combine(temp.Root, "data", "compiled", "0_na_img", "w_img_0_na.db"),
            new TestResourceDatabaseBuilder().AddResource("TEST_IMG").Build());
        var assets = new AssetFileSystem(
            [new LooseFolderSource("G3:data/compiled", "data/compiled", Path.Combine(temp.Root, "data", "compiled"))]);
        var catalog = AssetCatalog.Build(assets);
        var command = AssetCommandLine.Parse(
            ["assets", "textures", "TEST_IMG", "--properties", "--decode"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("TEST_IMG", text, StringComparison.Ordinal);
        Assert.Contains("Bc1", text, StringComparison.Ordinal);
        Assert.Contains("decoded", text, StringComparison.Ordinal);
        Assert.Contains("checksum=", text, StringComparison.Ordinal);
        Assert.Contains("Showing 1 of 1 texture(s); parsed 1", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TexturesCommand_UnknownFilter_ReturnsNoRows()
    {
        using var temp = new TempDirectory();
        var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, temp.Root)]);
        var catalog = AssetCatalog.Build(assets);
        var command = AssetCommandLine.Parse(["assets", "textures", "missing"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Showing 0 of 0 texture(s)", error.ToString(), StringComparison.Ordinal);
    }
}
