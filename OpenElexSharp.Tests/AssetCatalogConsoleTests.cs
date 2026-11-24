using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class AssetCatalogConsoleTests
{
	[Fact]
	public void List_ExcludesExecutablesByDefault()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(["assets", "list"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(0, exitCode);
		var lines = Lines(output);
		Assert.Contains(lines, line => line.Contains("texture.dds", StringComparison.Ordinal));
		Assert.Contains(lines, line => line.Contains("readme.txt", StringComparison.Ordinal));
		Assert.DoesNotContain(lines, line => line.Contains("game.exe", StringComparison.Ordinal));
	}

	[Fact]
	public void List_IncludeBinaries_ShowsExecutables()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(["assets", "list", "--include-binaries"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Contains(Lines(output), line => line.Contains("game.exe", StringComparison.Ordinal));
	}

	[Fact]
	public void List_AppliesExtensionAndLimit()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(["assets", "list", "--ext", "dds", "--limit", "1"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		var line = Assert.Single(Lines(output));
		Assert.StartsWith("data/extern/assets/texture.dds\t", line, StringComparison.Ordinal);
	}

	[Fact]
	public void List_AllReturnsEveryMatch()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(["assets", "list", "--all", "--source", "data/extern"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(2, Lines(output).Count);
	}

	[Fact]
	public void Stats_PrintsTotalsAndGroups()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(["assets", "stats"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(0, exitCode);
		var text = output.ToString();
		Assert.Contains("Assets: 3", text, StringComparison.Ordinal);
		Assert.Contains("Executables: 1", text, StringComparison.Ordinal);
		Assert.Contains("By extension:", text, StringComparison.Ordinal);
		Assert.Contains(".dds", text, StringComparison.Ordinal);
		Assert.Contains("G3:scripts", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Export_WritesAssetAndCreatesDirectories()
	{
		using var fixture = new CatalogFixture();
		var destination = Path.Combine(fixture.Temp.Root, "out", "nested", "texture.bin");
		var command = AssetCommandLine.Parse(
			["assets", "export", "data/extern/assets/texture.dds", destination]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(0, exitCode);
		Assert.Equal("1234", File.ReadAllText(destination));
		Assert.Contains("Exported", output.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Export_Executable_IsRefused()
	{
		using var fixture = new CatalogFixture();
		var destination = Path.Combine(fixture.Temp.Root, "game.bin");
		var command = AssetCommandLine.Parse(["assets", "export", "scripts/game.exe", destination]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(3, exitCode);
		Assert.False(File.Exists(destination));
		Assert.Contains("Refusing", error.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Export_MissingAsset_ReturnsError()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(
			["assets", "export", "data/extern/missing.bin", Path.Combine(fixture.Temp.Root, "x.bin")]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(3, exitCode);
		Assert.Contains("not found", error.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Help_PrintsUsage()
	{
		using var fixture = new CatalogFixture();
		var command = AssetCommandLine.Parse(["assets", "help"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, fixture.Catalog, fixture.Assets, output, error);

		Assert.Equal(0, exitCode);
		Assert.Contains("assets list", output.ToString(), StringComparison.Ordinal);
	}

	private static List<string> Lines(StringWriter writer) =>
		writer.ToString()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.TrimEnd('\r'))
			.ToList();

	private sealed class CatalogFixture : IDisposable
	{
		public CatalogFixture()
		{
			Temp = new TempDirectory();
			Temp.CreateFile("data/extern/assets/texture.dds", "1234");
			Temp.CreateFile("data/extern/readme.txt", "12");
			Temp.CreateFile("system/game.exe", "exe");
			Assets = new AssetFileSystem(
			[
				new LooseFolderSource("G3:data/extern", "data/extern", Path.Combine(Temp.Root, "data/extern")),
				new LooseFolderSource("G3:scripts", "scripts", Path.Combine(Temp.Root, "system")),
			]);
			Catalog = AssetCatalog.Build(Assets);
		}

		public TempDirectory Temp { get; }

		public AssetFileSystem Assets { get; }

		public AssetCatalog Catalog { get; }

		public void Dispose() => Temp.Dispose();
	}
}
