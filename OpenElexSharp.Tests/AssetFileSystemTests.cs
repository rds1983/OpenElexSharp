using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class AssetFileSystemTests
{
	[Fact]
	public void TryGetEntry_LastMountedSourceWins()
	{
		using var baseAssets = new TempDirectory();
		baseAssets.CreateFile("shared.txt", "base");
		using var modAssets = new TempDirectory();
		modAssets.CreateFile("shared.txt", "mod");

		var assets = new AssetFileSystem(
		[
			new LooseFolderSource("base", string.Empty, baseAssets.Root),
			new LooseFolderSource("mod", string.Empty, modAssets.Root),
		]);

		Assert.True(assets.TryGetEntry("SHARED.TXT", out var entry));
		Assert.Equal("mod", entry!.SourceName);
		Assert.Equal("shared.txt", entry.VirtualPath);
	}

	[Fact]
	public void OpenRead_LastMountedSourceWins()
	{
		using var baseAssets = new TempDirectory();
		baseAssets.CreateFile("shared.txt", "base");
		using var modAssets = new TempDirectory();
		modAssets.CreateFile("shared.txt", "mod");

		var assets = new AssetFileSystem(
		[
			new LooseFolderSource("base", string.Empty, baseAssets.Root),
			new LooseFolderSource("mod", string.Empty, modAssets.Root),
		]);

		using var reader = new StreamReader(assets.OpenRead("shared.txt"));

		Assert.Equal("mod", reader.ReadToEnd());
	}

	[Fact]
	public void EnumerateEntries_DeduplicatesAndSorts()
	{
		using var baseAssets = new TempDirectory();
		baseAssets.CreateFile("b.txt", "base-b");
		baseAssets.CreateFile("z.txt", "base-z");
		using var modAssets = new TempDirectory();
		modAssets.CreateFile("a.txt", "mod-a");
		modAssets.CreateFile("z.txt", "mod-z");

		var assets = new AssetFileSystem(
		[
			new LooseFolderSource("base", string.Empty, baseAssets.Root),
			new LooseFolderSource("mod", string.Empty, modAssets.Root),
		]);

		var entries = assets.EnumerateEntries();

		Assert.Equal(["a.txt", "b.txt", "z.txt"], entries.Select(entry => entry.VirtualPath));
		Assert.Equal("mod-z", File.ReadAllText(entries[2].PhysicalPath!));
	}

	[Fact]
	public void Exists_ReturnsFalseForMissingFile()
	{
		using var assetsFolder = new TempDirectory();
		var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, assetsFolder.Root)]);

		Assert.False(assets.Exists("missing.txt"));
	}

	[Fact]
	public void OpenRead_MissingFile_Throws()
	{
		using var assetsFolder = new TempDirectory();
		var assets = new AssetFileSystem([new LooseFolderSource("test", string.Empty, assetsFolder.Root)]);

		var exception = Assert.Throws<AssetNotFoundException>(() => assets.OpenRead("missing.txt"));

		Assert.Contains("missing.txt", exception.Message);
	}

	[Fact]
	public void Constructor_WithoutPackedMounts_ReturnsEmptyList()
	{
		var assets = new AssetFileSystem([]);

		Assert.Empty(assets.Sources);
		Assert.Empty(assets.PackedMounts);
	}

	[Fact]
	public void Constructor_NullSources_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new AssetFileSystem(null!));
	}

	[Fact]
	public void Constructor_NullSourceEntry_Throws()
	{
		Assert.Throws<ArgumentException>(() => new AssetFileSystem([null!]));
	}

	[Fact]
	public void MountFromInstallation_ResolvesLooseAndPackedFolders()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("system");
		temp.CreateFile("system/dev.txt", "dev");
		temp.CreateFile(
			"data/ini/mount_packed.xml",
			"""
            <MountList>
              <Volume Name="G3" RootPath=".." NoPacked="false">
                <Folders>
                  <Folder VirtualPath="scripts" PhysicalPath="$(app)" />
                  <Folder VirtualPath="ini" PhysicalPath="data/ini" />
                  <Folder VirtualPath="data/extern" />
                  <Folder VirtualPath="data/common" PhysicalPath="data/common" />
                  <Folder VirtualPath="data/compiled" PackedPath="data/packed" />
                </Folders>
              </Volume>
            </MountList>
            """);
		temp.CreateFile("data/extern/assets/file.txt", "asset");
		temp.CreateDirectory("data/packed");

		var installation = ElexInstallation.Open(temp.Root);
		var assets = AssetFileSystem.MountFromInstallation(installation);

		Assert.Equal(
			["G3:data/compiled", "G3:data/extern", "G3:ini", "G3:scripts"],
			assets.Sources.Select(source => source.Name).OrderBy(name => name, StringComparer.Ordinal));

		var packed = Assert.Single(assets.PackedMounts);
		Assert.Equal("data/compiled", packed.VirtualRoot);
		Assert.Equal(Path.Combine(temp.Root, "data", "packed"), packed.PhysicalPath);
		Assert.True(packed.Enabled);

		Assert.True(assets.Exists("INI/MOUNT_PACKED.XML"));
		Assert.True(assets.Exists("Data/Extern/Assets/File.txt"));
		Assert.True(assets.Exists("scripts/dev.txt"));

		using var reader = new StreamReader(assets.OpenRead("data/extern/assets/file.txt"));
		Assert.Equal("asset", reader.ReadToEnd());

		Assert.Equal(3, assets.EnumerateEntries().Count);
	}

	[Fact]
	public void Mount_InvalidVirtualPath_ThrowsConfigurationException()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("system");
		temp.CreateFile("data/ini/mount_packed.xml", "<MountList />");
		var installation = ElexInstallation.Open(temp.Root);
		var configuration = new MountConfiguration(
		[
			new MountVolume(
				"G3",
				string.Empty,
				NoPacked: false,
				[new MountFolder("../escape", null, null, false)]),
		]);

		Assert.Throws<MountConfigurationException>(() => AssetFileSystem.Mount(installation, configuration));
	}

	[Fact]
	public void MountFromInstallation_IncludesPackedEntries()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("system");
		temp.CreateFile(
			"data/ini/mount_packed.xml",
			"""
            <MountList>
              <Volume Name="G3" RootPath="..">
                <Folders>
                  <Folder VirtualPath="data/compiled" PackedPath="data/packed" />
                </Folders>
              </Volume>
            </MountList>
            """);
		temp.CreateDirectory("data/packed");
		File.WriteAllBytes(
			Path.Combine(temp.Root, "data", "packed", "c_1_test.pak"),
			new TestPakBuilder().AddFile("localization/en.str", "packed text").Build());

		var installation = ElexInstallation.Open(temp.Root);
		var assets = AssetFileSystem.MountFromInstallation(installation);

		Assert.True(assets.Exists("data/compiled/localization/en.str"));
		using var stream = assets.OpenRead("DATA/COMPILED/localization/EN.str");
		using var reader = new StreamReader(stream);
		Assert.Equal("packed text", reader.ReadToEnd());
		Assert.Empty(assets.Diagnostics);
	}

	[Fact]
	public void Mount_NoPackedVolume_DisablesPackedMount()
	{
		using var temp = new TempDirectory();
		temp.CreateDirectory("system");
		temp.CreateFile("data/ini/mount_packed.xml", "<MountList />");
		var installation = ElexInstallation.Open(temp.Root);
		var configuration = new MountConfiguration(
		[
			new MountVolume(
				"G3",
				string.Empty,
				NoPacked: true,
				[new MountFolder("data/compiled", null, "data/packed", false)]),
		]);

		var assets = AssetFileSystem.Mount(installation, configuration);

		Assert.False(Assert.Single(assets.PackedMounts).Enabled);
		Assert.DoesNotContain(assets.Sources, source => source is PackedAssetSource);
	}
}
