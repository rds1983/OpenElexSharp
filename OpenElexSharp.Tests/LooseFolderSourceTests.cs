using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class LooseFolderSourceTests
{
	[Fact]
	public void EnumerateEntries_MapsFilesUnderVirtualRoot()
	{
		using var temp = new TempDirectory();
		temp.CreateFile("data/extern/assets/texture.dds", "dds");
		temp.CreateFile("data/extern/readme.txt", "hi");
		var source = new LooseFolderSource("test", "data/extern", Path.Combine(temp.Root, "data/extern"));

		var entries = source.EnumerateEntries().OrderBy(entry => entry.VirtualPath).ToArray();

		Assert.Equal(2, entries.Length);
		Assert.Equal("data/extern/assets/texture.dds", entries[0].VirtualPath);
		Assert.Equal("data/extern/readme.txt", entries[1].VirtualPath);
		Assert.Equal("test", entries[0].SourceName);
		Assert.Equal(3, entries[0].Length);
	}

	[Fact]
	public void TryGetEntry_IsCaseInsensitive()
	{
		using var temp = new TempDirectory();
		temp.CreateFile("data/extern/Assets/Texture.dds", "dds");
		var source = new LooseFolderSource("test", "data/extern", Path.Combine(temp.Root, "data/extern"));

		Assert.True(source.TryGetEntry("DATA/EXTERN/assets/TEXTURE.DDS", out var entry));
		Assert.Equal("data/extern/Assets/Texture.dds", entry!.VirtualPath);
		Assert.True(source.TryGetEntry(@"data\extern\assets\texture.dds", out _));
	}

	[Fact]
	public void OpenRead_ReturnsFileContent()
	{
		using var temp = new TempDirectory();
		temp.CreateFile("data/extern/readme.txt", "hello asset");
		var source = new LooseFolderSource("test", "data/extern", Path.Combine(temp.Root, "data/extern"));

		using var reader = new StreamReader(source.OpenRead("data/extern/readme.txt"));

		Assert.Equal("hello asset", reader.ReadToEnd());
	}

	[Fact]
	public void OpenRead_MissingFile_Throws()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("data/extern");
		var source = new LooseFolderSource("test", "data/extern", Path.Combine(temp.Root, "data/extern"));

		var exception = Assert.Throws<AssetNotFoundException>(
			() => source.OpenRead("data/extern/missing.txt"));

		Assert.Contains("missing.txt", exception.Message);
	}

	[Fact]
	public void MissingPhysicalFolder_ProducesEmptySource()
	{
		using var temp = new TempDirectory();
		var source = new LooseFolderSource("test", "data/extern", Path.Combine(temp.Root, "missing"));

		Assert.Empty(source.EnumerateEntries());
		Assert.False(source.TryGetEntry("data/extern/file.txt", out _));
	}

	[Fact]
	public void EmptyVirtualRoot_MapsPhysicalRootAtVirtualRoot()
	{
		using var temp = new TempDirectory();
		temp.CreateFile("readme.txt", "hi");
		var source = new LooseFolderSource("root", string.Empty, temp.Root);

		Assert.True(source.TryGetEntry("readme.txt", out var entry));
		Assert.Equal("readme.txt", entry!.VirtualPath);
		Assert.Equal(string.Empty, source.VirtualRoot);
	}

	[Fact]
	public void NormalizesVirtualRoot()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("assets");
		var source = new LooseFolderSource("root", @"data\extern/", Path.Combine(temp.Root, "assets"));

		Assert.Equal("data/extern", source.VirtualRoot);
	}

	[Fact]
	public void Constructor_InvalidArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => new LooseFolderSource(" ", "data", Path.GetTempPath()));
		Assert.Throws<ArgumentNullException>(() => new LooseFolderSource("test", null!, Path.GetTempPath()));
		Assert.Throws<ArgumentException>(() => new LooseFolderSource("test", "data", " "));
	}
}
