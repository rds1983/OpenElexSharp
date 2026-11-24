using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MeshConsoleTests
{
    [Fact]
    public void MeshesCommand_ListsAndDescribesMesh()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("data/compiled/0_na_msh/0/1");
        var meshData = TestGmshBuilder.Create(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            [new(0, 0, 7, 0), new(1, 0, 15, 1)],
            [new(0, 1, 0, 12), new(1, 1, 36, 12)],
            [[1, 0, 2, 2, 40, 1, 0x81d1b3f7, 1, 64, 72]]);
        var hash = ResourceDatabase.FormatHash(Djb2Hash.Compute("TEST_MESH"));
        File.WriteAllBytes(
            Path.Combine(temp.Root, "data", "compiled", "0_na_msh", "0", "1", $"w_msh_0_na_{hash}.rom"),
            meshData);
        File.WriteAllBytes(
            Path.Combine(temp.Root, "data", "compiled", "0_na_msh", "w_msh_0_na.db"),
            new TestResourceDatabaseBuilder().AddResource("TEST_MESH").Build());
        var assets = new AssetFileSystem(
            [new LooseFolderSource("G3:data/compiled", "data/compiled", Path.Combine(temp.Root, "data", "compiled"))]);
        var catalog = AssetCatalog.Build(assets);
        var command = AssetCommandLine.Parse(
            ["assets", "meshes", "TEST_MESH", "--properties"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("TEST_MESH", text, StringComparison.Ordinal);
        Assert.Contains("attribute\tposition", text, StringComparison.Ordinal);
        Assert.Contains("stream\tposition", text, StringComparison.Ordinal);
        Assert.Contains("81d1b3f7", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Showing 1 of 1 mesh(es)", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MeshesCommand_UnknownFilter_ReturnsNoRows()
    {
        using var temp = new TempDirectory();
        var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, temp.Root)]);
        var catalog = AssetCatalog.Build(assets);
        var command = AssetCommandLine.Parse(["assets", "meshes", "missing"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Showing 0 of 0 mesh(es)", error.ToString(), StringComparison.Ordinal);
    }
}
