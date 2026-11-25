using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class SectorCompanionsTests
{
	[Fact]
	public void CandidateNames_CitySector_IncludesNatureSpeedTreeRiversLights()
	{
		var candidates = SectorCompanions.CandidateNames("ABE_AbessaCity_Buildings");

		Assert.Contains("ABE_AbessaCity_Nature", candidates);
		Assert.Contains("ABE_AbessaCity_Speedtree", candidates);
		Assert.Contains("ABE_Rivers", candidates);
		Assert.Contains("ABE_Lights", candidates);
		Assert.DoesNotContain("ABE_AbessaCity_Buildings", candidates);
	}

	[Fact]
	public void CandidateNames_FortSector_UsesSingleRegionCompanions()
	{
		var candidates = SectorCompanions.CandidateNames("TAV_Fort_Buildings");

		Assert.Contains("TAV_Fort_Nature", candidates);
		Assert.Contains("TAV_Fort_SpeedTree", candidates);
		Assert.Contains("TAV_Rivers", candidates);
		Assert.Contains("TAV_Lights", candidates);
	}

	[Fact]
	public void CandidateNames_XacSector_UsesPluralSpeedTrees()
	{
		var candidates = SectorCompanions.CandidateNames("XAC_Buildings");

		Assert.Contains("XAC_Nature", candidates);
		Assert.Contains("XAC_SpeedTrees", candidates);
		Assert.Contains("XAC_Rivers", candidates);
		Assert.Contains("XAC_Lights", candidates);
	}

	[Fact]
	public void CandidateNames_AlreadyVisualSector_StillUsesBasePrefix()
	{
		var candidates = SectorCompanions.CandidateNames("TAV_Fort_SpeedTree");

		Assert.Contains("TAV_Fort_Nature", candidates);
		Assert.DoesNotContain("TAV_Fort_SpeedTree", candidates);
		Assert.Contains("TAV_Lights", candidates);
	}

	[Fact]
	public void CandidateNames_HasNoDuplicates()
	{
		var candidates = SectorCompanions.CandidateNames("XAC_Buildings");

		Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.Ordinal).Count());
	}
}
