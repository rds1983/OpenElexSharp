using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexWorldTests
{
	[Fact]
	public void Parse_ReadsTerrainAndSectors()
	{
		var data = BuildWorld(
			"#G3:/data/raw/terrain3/terrain3.trn",
			["ABE_Items", "TAV_NPCs"]);

		var world = ElexWorld.Parse("World", "world.rom", data);

		Assert.Equal("World", world.Name);
		Assert.Equal("#G3:/data/raw/terrain3/terrain3.trn", world.TerrainFile);
		Assert.Equal(["ABE_Items", "TAV_NPCs"], world.SectorNames);
	}

	[Fact]
	public void Parse_RejectsBadGar5Header()
	{
		var data = BuildWorld("terrain.trn", ["Sector"]);
		data[0] = (byte)'X';

		Assert.Throws<GenomeFormatException>(() =>
			ElexWorld.Parse("World", "world.rom", data));
	}

	[Fact]
	public void Parse_RejectsWrongTerrainClass()
	{
		var terrain = Gec2TestBuilder.Object(
			0x12345678,
			2,
			[],
			[(ElexWorld.StringTypeHash, ElexWorld.FileNamePropertyHash,
				Gec2TestBuilder.String("terrain.trn"))]);
		var data = Gec2TestBuilder.Wrap([.. terrain, 0, 0]);

		Assert.Throws<GenomeFormatException>(() =>
			ElexWorld.Parse("World", "world.rom", data));
	}

	[Fact]
	public void Parse_RejectsTruncatedSectorName()
	{
		var data = BuildWorld("terrain.trn", ["Sector"]);
		data = data[..^1];

		Assert.Throws<GenomeFormatException>(() =>
			ElexWorld.Parse("World", "world.rom", data));
	}

	[Fact]
	public void Parse_RejectsMalformedTerrainName()
	{
		var terrain = Gec2TestBuilder.Object(
			ElexWorld.TerrainClassHash,
			2,
			[],
			[(ElexWorld.StringTypeHash, ElexWorld.FileNamePropertyHash, [2, 0, (byte)'x'])]);
		var data = Gec2TestBuilder.Wrap([.. terrain, 0, 0]);

		Assert.Throws<GenomeFormatException>(() =>
			ElexWorld.Parse("World", "world.rom", data));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => ElexWorld.Parse("", "world.rom", []));
		Assert.Throws<ArgumentException>(() => ElexWorld.Parse("World", "", []));
		Assert.Throws<ArgumentNullException>(() => ElexWorld.Parse("World", "world.rom", null!));
	}

	private static byte[] BuildWorld(string terrainFile, string[] sectors)
	{
		var terrain = Gec2TestBuilder.Object(
			ElexWorld.TerrainClassHash,
			2,
			[],
			[(ElexWorld.StringTypeHash, ElexWorld.FileNamePropertyHash,
				Gec2TestBuilder.String(terrainFile))]);
		var payload = new List<byte>(terrain);
		payload.Add((byte)sectors.Length);
		payload.Add((byte)(sectors.Length >> 8));
		foreach (var sector in sectors)
		{
			var name = Encoding.Latin1.GetBytes(sector);
			payload.Add((byte)name.Length);
			payload.Add((byte)(name.Length >> 8));
			payload.AddRange(name);
		}

		return Gec2TestBuilder.Wrap([.. payload]);
	}
}
