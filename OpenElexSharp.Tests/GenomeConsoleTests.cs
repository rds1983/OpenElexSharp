using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GenomeConsoleTests
{
    [Fact]
    public void ObjectsCommand_ListsDocumentObjects()
    {
        using var temp = new TempDirectory();
        var document = TestGenomeBuilder.CreateHeaderDocument(
        [
            (0x955C5FD7u,
            [
                ("QuestOne", 0UL, TestGenomeBuilder.CreateObject(0x955C5FD7, 6, [], [])),
                ("QuestTwo", 0UL, TestGenomeBuilder.CreateObject(0x955C5FD7, 6, [], [])),
            ]),
        ]);
        temp.CreateDirectory("data");
        File.WriteAllBytes(Path.Combine(temp.Root, "data", "test.hdr"), document);
        var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, temp.Root)]);
        var catalog = AssetCatalog.Build(assets);
        var command = AssetCommandLine.Parse(["assets", "objects", "data/test.hdr", "--limit", "1"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(0, exitCode);
        var line = Assert.Single(Lines(output));
        Assert.StartsWith("QuestOne\t0x955c5fd7\t6\t", line, StringComparison.Ordinal);
        Assert.Contains("Showing 1 of 2 object(s)", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectsCommand_UnknownAsset_ReturnsError()
    {
        using var temp = new TempDirectory();
        var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, temp.Root)]);
        var catalog = AssetCatalog.Build(assets);
        var command = AssetCommandLine.Parse(["assets", "objects", "data/missing.hdr"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(3, exitCode);
        Assert.Contains("not found", error.ToString(), StringComparison.Ordinal);
    }

    private static List<string> Lines(StringWriter writer) =>
        writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToList();
}
