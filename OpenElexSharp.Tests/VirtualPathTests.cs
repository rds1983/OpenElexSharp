using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class VirtualPathTests
{
	[Theory]
	[InlineData("data/extern/file.txt", "data/extern/file.txt")]
	[InlineData(@"data\extern\file.txt", "data/extern/file.txt")]
	[InlineData("/data//extern/./file.txt", "data/extern/file.txt")]
	[InlineData("~/data/extern/file.txt", "data/extern/file.txt")]
	[InlineData("  data/extern/file.txt  ", "data/extern/file.txt")]
	public void Normalize_VariousInputs_ReturnsCanonicalPath(string input, string expected)
	{
		Assert.Equal(expected, VirtualPath.Normalize(input));
	}

	[Fact]
	public void Normalize_PreservesCase()
	{
		Assert.Equal("Data/Compiled/Strings.tab", VirtualPath.Normalize(@"Data\Compiled\Strings.tab"));
	}

	[Theory]
	[InlineData("data/../secret.txt")]
	[InlineData("../secret.txt")]
	public void Normalize_ParentTraversal_Throws(string input)
	{
		Assert.Throws<ArgumentException>(() => VirtualPath.Normalize(input));
	}

	[Fact]
	public void Normalize_VolumePrefix_Throws()
	{
		Assert.Throws<ArgumentException>(() => VirtualPath.Normalize("#G3:/Data/Compiled/strings.tab"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("/")]
	[InlineData("///")]
	public void Normalize_EmptyPath_Throws(string input)
	{
		Assert.Throws<ArgumentException>(() => VirtualPath.Normalize(input));
	}

	[Fact]
	public void Normalize_NullPath_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => VirtualPath.Normalize(null!));
	}

	[Theory]
	[InlineData("Data/Compiled/Strings.tab", "data/compiled/strings.tab")]
	[InlineData("DATA/COMPILED/STRINGS.TAB", "data/compiled/Strings.tab")]
	public void Comparer_IgnoresCase(string left, string right)
	{
		Assert.True(VirtualPath.Comparer.Equals(left, right));
	}
}
