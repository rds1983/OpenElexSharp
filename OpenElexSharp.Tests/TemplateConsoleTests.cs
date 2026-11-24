using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class TemplateConsoleTests
{
	[Fact]
	public void TemplatesCommand_ListsResolvedTemplate()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("data/compiled/0_na_tpl/0/1");
		var templateGuid = Guid.NewGuid();
		var templateData = TestTemplateBuilder.CreateTemplate(
			"TEST_TPL.entity",
			templateGuid,
			Guid.Empty,
			[TestTemplateBuilder.CreatePropertySet(0x11111111, 1, 0, "value")],
			[]);
		var hash = ResourceDatabase.FormatHash(Djb2Hash.Compute("TEST_TPL"));
		File.WriteAllBytes(
			Path.Combine(temp.Root, "data", "compiled", "0_na_tpl", "0", "1", $"w_tpl_0_na_{hash}.rom"),
			templateData);
		File.WriteAllBytes(
			Path.Combine(temp.Root, "data", "compiled", "0_na_tpl", "w_tpl_0_na.db"),
			new TestResourceDatabaseBuilder().AddResource("TEST_TPL").Build());
		var assets = new AssetFileSystem(
			[new LooseFolderSource("G3:data/compiled", "data/compiled", Path.Combine(temp.Root, "data", "compiled"))]);
		var catalog = AssetCatalog.Build(assets);
		var command = AssetCommandLine.Parse(["assets", "templates", "TEST_TPL", "--properties"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

		Assert.Equal(0, exitCode);
		var text = output.ToString();
		Assert.Contains("TEST_TPL", text, StringComparison.Ordinal);
		Assert.Contains(templateGuid.ToString("D"), text, StringComparison.Ordinal);
		Assert.Contains("11111111", text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Showing 1 of 1 template(s)", error.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void TemplatesCommand_UnknownFilter_ReturnsNoRows()
	{
		using var temp = new TempDirectory();
		var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, temp.Root)]);
		var catalog = AssetCatalog.Build(assets);
		var command = AssetCommandLine.Parse(["assets", "templates", "missing"]);
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = AssetCatalogConsole.Run(command, catalog, assets, output, error);

		Assert.Equal(0, exitCode);
		Assert.Empty(output.ToString());
		Assert.Contains("Showing 0 of 0 template(s)", error.ToString(), StringComparison.Ordinal);
	}
}
