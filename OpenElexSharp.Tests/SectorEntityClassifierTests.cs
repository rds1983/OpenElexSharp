using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class SectorEntityClassifierTests
{
	[Fact]
	public void Classify_PointLightClass_ReturnsLights()
	{
		var category = SectorEntityClassifier.Classify(
			"Obj_LS_Bsk_FirePlace_1_L1",
			"Obj_LS_Bsk_FirePlace_1_L1",
			["class eCPointLight_PS"],
			waterMap: false);

		Assert.Equal(SectorVisualCategory.Lights, category);
	}

	[Fact]
	public void Classify_DecalClass_ReturnsDecals()
	{
		var category = SectorEntityClassifier.Classify(
			"DC_Logo_Cle_01",
			"Obj_Dmy_Decal_L1",
			["class eCDecal2_PS"],
			waterMap: false);

		Assert.Equal(SectorVisualCategory.Decals, category);
	}

	[Fact]
	public void Classify_WaterMap_ReturnsWater()
	{
		var category = SectorEntityClassifier.Classify(
			"Obj_Nat_WaterPlane_1",
			"Obj_Nat_WaterPlane_1_L1",
			["class eCMesh_PS"],
			waterMap: true);

		Assert.Equal(SectorVisualCategory.Water, category);
	}

	[Fact]
	public void Classify_TerrainRiver_ReturnsWater()
	{
		var category = SectorEntityClassifier.Classify(
			"River",
			null,
			["class eCTerrainRiver_PS"],
			waterMap: false);

		Assert.Equal(SectorVisualCategory.Water, category);
	}

	[Fact]
	public void Classify_SpeedTree_ReturnsVegetation()
	{
		var category = SectorEntityClassifier.Classify(
			"ST_Dracaena_Tree_2",
			null,
			["class eCSpeedTreeStatic_PS"],
			waterMap: false);

		Assert.Equal(SectorVisualCategory.Vegetation, category);
	}

	[Fact]
	public void Classify_VegetationMeshName_ReturnsVegetation()
	{
		var category = SectorEntityClassifier.Classify(
			"plant",
			"Obj_Nat_SwampPlant_7a_L1",
			[],
			waterMap: false);

		Assert.Equal(SectorVisualCategory.Vegetation, category);
	}

	[Fact]
	public void Classify_RockMesh_ReturnsOther()
	{
		var category = SectorEntityClassifier.Classify(
			"rock",
			"Obj_Nat_Rock_Big_05_L1",
			["class eCMesh_PS"],
			waterMap: false);

		Assert.Equal(SectorVisualCategory.Other, category);
	}

	[Fact]
	public void Classify_EmptyInputs_ReturnsOther()
	{
		var category = SectorEntityClassifier.Classify(null, null, [], waterMap: false);

		Assert.Equal(SectorVisualCategory.Other, category);
	}
}
