using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexLocalizationConfigTests
{
	[Fact]
	public void Parse_ReadsSectionsAndPrefixes()
	{
		const string text = """
			[default]
			prefix=default
			csv=#G3:/Data/Raw/Strings/strings.csv
			bin=#G3:/Data/Compiled/Strings/strings.tab

			[item]
			prefix=item
			csv=#G3:/Data/Raw/Strings/items.csv
			bin=#G3:/Data/Compiled/Strings/items.tab
			""";

		var config = ElexLocalizationConfig.Parse(Encoding.Latin1.GetBytes(text));

		Assert.Equal(2, config.Entries.Count);
		Assert.Equal("default", config.Entries[0].Prefix);
		Assert.Equal("item", config.Entries[1].Section);
		Assert.True(config.TryGetCsv("#G3:/Data/Raw/Strings/items.csv", out var item));
		Assert.Equal("item", item.Prefix);
	}

	[Fact]
	public void Parse_SkipsIncompleteSections()
	{
		const string text = """
			[broken]
			prefix=broken

			[ok]
			prefix=ok
			csv=test.csv
			""";

		var config = ElexLocalizationConfig.Parse(Encoding.Latin1.GetBytes(text));

		var entry = Assert.Single(config.Entries);
		Assert.Equal("ok", entry.Section);
	}

	[Fact]
	public void TryGetCsv_IsCaseInsensitive()
	{
		var config = ElexLocalizationConfig.Parse(
			Encoding.Latin1.GetBytes("[item]\nprefix=item\ncsv=ITEMS.CSV\n"));

		Assert.True(config.TryGetCsv("items.csv", out var entry));
		Assert.Equal("item", entry.Prefix);
	}

	[Fact]
	public void Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => ElexLocalizationConfig.Parse(null!));
	}
}
