using Microsoft.Xna.Framework;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class LevelLightingTests
{
	[Theory]
	[InlineData("TAV_Fort_Buildings", "Tavar")]
	[InlineData("TAV_Center_Nature", "Tavar")]
	[InlineData("ABE_AbessaCity_Buildings", "Edan")]
	[InlineData("EDA_NPCs", "Edan")]
	[InlineData("IGN_Volcano", "Ignadon")]
	[InlineData("XAC_Snow", "Xacor")]
	[InlineData("CombatTest", "Default")]
	[InlineData("Testlevel", "Default")]
	public void ForSector_SelectsRegionPalette(string sectorNames, string expected)
	{
		Assert.Equal(expected, LevelLighting.ForSector(sectorNames).Name);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ForSector_EmptyInput_ReturnsDefault(string? sectorNames)
	{
		Assert.Same(LevelLighting.Default, LevelLighting.ForSector(sectorNames));
	}

	[Fact]
	public void FogColor_MatchesSkyHorizon()
	{
		foreach (var lighting in new[]
		{
			LevelLighting.Default,
			LevelLighting.Tavar,
			LevelLighting.Edan,
			LevelLighting.Ignadon,
			LevelLighting.Xacor,
		})
		{
			Assert.Equal(lighting.SkyHorizon.R / 255f, lighting.FogColor.X, precision: 3);
			Assert.Equal(lighting.SkyHorizon.G / 255f, lighting.FogColor.Y, precision: 3);
			Assert.Equal(lighting.SkyHorizon.B / 255f, lighting.FogColor.Z, precision: 3);
		}
	}

	[Fact]
	public void LightDirections_AreNormalized()
	{
		foreach (var lighting in new[]
		{
			LevelLighting.Default,
			LevelLighting.Tavar,
			LevelLighting.Edan,
			LevelLighting.Ignadon,
			LevelLighting.Xacor,
		})
		{
			Assert.Equal(1f, lighting.SunDirection.Length(), precision: 3);
			Assert.Equal(1f, lighting.FillDirection.Length(), precision: 3);
		}
	}
}
