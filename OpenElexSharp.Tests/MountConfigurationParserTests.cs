using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MountConfigurationParserTests
{
	[Fact]
	public void Parse_RetailShape_ReadsVolumesAndFolders()
	{
		var configuration = Parse(
			"""
            <MountList>
              <Volume Name="G3" RootPath=".." NoPacked="false">
                <Folders>
                  <Folder PhysicalPath="$(app)" VirtualPath="scripts" />
                  <Folder VirtualPath="ini" PhysicalPath="data/ini" />
                  <Folder VirtualPath="data/compiled" PackedPath="data/packed" />
                </Folders>
              </Volume>
            </MountList>
            """);

		var volume = Assert.Single(configuration.Volumes);
		Assert.Equal("G3", volume.Name);
		Assert.Equal("..", volume.RootPath);
		Assert.False(volume.NoPacked);
		Assert.Equal(3, volume.Folders.Count);

		Assert.Equal("scripts", volume.Folders[0].VirtualPath);
		Assert.Equal("$(app)", volume.Folders[0].PhysicalPath);
		Assert.Null(volume.Folders[0].PackedPath);

		Assert.Equal("ini", volume.Folders[1].VirtualPath);
		Assert.Equal("data/ini", volume.Folders[1].PhysicalPath);

		Assert.Equal("data/compiled", volume.Folders[2].VirtualPath);
		Assert.Null(volume.Folders[2].PhysicalPath);
		Assert.Equal("data/packed", volume.Folders[2].PackedPath);
	}

	[Fact]
	public void Parse_MissingOptionalAttributes_UsesDefaults()
	{
		var configuration = Parse(
			"""
            <MountList>
              <Volume>
                <Folders>
                  <Folder VirtualPath="data/extern" />
                </Folders>
              </Volume>
            </MountList>
            """);

		var volume = Assert.Single(configuration.Volumes);
		Assert.Equal(string.Empty, volume.Name);
		Assert.Equal(string.Empty, volume.RootPath);
		Assert.False(volume.NoPacked);

		var folder = Assert.Single(volume.Folders);
		Assert.Null(folder.PhysicalPath);
		Assert.Null(folder.PackedPath);
		Assert.False(folder.CreateFolder);
	}

	[Fact]
	public void Parse_MultipleVolumes_ReturnsAll()
	{
		var configuration = Parse(
			"""
            <MountList>
              <Volume Name="Base"><Folders><Folder VirtualPath="a" /></Folders></Volume>
              <Volume Name="Mod"><Folders><Folder VirtualPath="b" /></Folders></Volume>
            </MountList>
            """);

		Assert.Equal(2, configuration.Volumes.Count);
		Assert.Equal("Base", configuration.Volumes[0].Name);
		Assert.Equal("Mod", configuration.Volumes[1].Name);
	}

	[Fact]
	public void Parse_UnknownElementsAndAttributes_AreIgnored()
	{
		var configuration = Parse(
			"""
            <MountList Unused="value">
              <Volume Name="G3" Unknown="1">
                <Folders>
                  <Folder VirtualPath="data" FutureAttribute="x" />
                  <UnknownFolder VirtualPath="ignored" />
                </Folders>
                <Extra />
              </Volume>
              <Other />
            </MountList>
            """);

		var volume = Assert.Single(configuration.Volumes);
		var folder = Assert.Single(volume.Folders);
		Assert.Equal("data", folder.VirtualPath);
	}

	[Theory]
	[InlineData("TRUE")]
	[InlineData("True")]
	[InlineData("1")]
	public void Parse_NoPackedTrueVariants_EnableNoPacked(string value)
	{
		var configuration = Parse($"""<MountList><Volume NoPacked="{value}" /></MountList>""");

		Assert.True(Assert.Single(configuration.Volumes).NoPacked);
	}

	[Theory]
	[InlineData("FALSE")]
	[InlineData("0")]
	public void Parse_NoPackedFalseVariants_DisableNoPacked(string value)
	{
		var configuration = Parse($"""<MountList><Volume NoPacked="{value}" /></MountList>""");

		Assert.False(Assert.Single(configuration.Volumes).NoPacked);
	}

	[Fact]
	public void Parse_CreateFolder_ReadsTrue()
	{
		var configuration = Parse(
			"""<MountList><Volume><Folders><Folder VirtualPath="save" CreateFolder="true" /></Folders></Volume></MountList>""");

		Assert.True(Assert.Single(Assert.Single(configuration.Volumes).Folders).CreateFolder);
	}

	[Fact]
	public void Parse_InvalidNoPacked_Throws()
	{
		Assert.Throws<MountConfigurationException>(
			() => Parse("""<MountList><Volume NoPacked="maybe" /></MountList>"""));
	}

	[Fact]
	public void Parse_InvalidCreateFolder_Throws()
	{
		Assert.Throws<MountConfigurationException>(
			() => Parse("""<MountList><Volume><Folders><Folder VirtualPath="a" CreateFolder="maybe" /></Folders></Volume></MountList>"""));
	}

	[Fact]
	public void Parse_FolderWithoutVirtualPath_Throws()
	{
		Assert.Throws<MountConfigurationException>(
			() => Parse("""<MountList><Volume><Folders><Folder PhysicalPath="x" /></Folders></Volume></MountList>"""));
	}

	[Fact]
	public void Parse_EmptyVirtualPath_Throws()
	{
		Assert.Throws<MountConfigurationException>(
			() => Parse("""<MountList><Volume><Folders><Folder VirtualPath="   " /></Folders></Volume></MountList>"""));
	}

	[Theory]
	[InlineData("<NotAMountList />")]
	[InlineData("<mountlist />")]
	public void Parse_WrongRootElement_Throws(string xml)
	{
		Assert.Throws<MountConfigurationException>(() => Parse(xml));
	}

	[Fact]
	public void Parse_MalformedXml_Throws()
	{
		Assert.Throws<MountConfigurationException>(() => Parse("<MountList>"));
	}

	[Fact]
	public void Parse_EmptyDocument_Throws()
	{
		Assert.Throws<MountConfigurationException>(() => Parse(string.Empty));
	}

	[Fact]
	public void Parse_DoctypeIsProhibited()
	{
		const string Xml = """
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///c:/windows/win.ini"> ]>
            <MountList><Volume Name="&xxe;" /></MountList>
            """;

		Assert.Throws<MountConfigurationException>(() => Parse(Xml));
	}

	[Fact]
	public void Parse_NullStream_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MountConfigurationParser.Parse(null!));
	}

	private static MountConfiguration Parse(string xml)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
		return MountConfigurationParser.Parse(stream);
	}
}
