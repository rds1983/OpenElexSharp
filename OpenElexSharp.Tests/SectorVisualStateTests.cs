using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class SectorVisualStateTests
{
	[Fact]
	public void Register_AppliesCurrentVisibility()
	{
		var state = new SectorVisualState();
		var node = new SceneNode("light");
		state.SetVisible(SectorVisualCategory.Lights, false);

		state.Register(node, SectorVisualCategory.Lights);

		Assert.False(node.Visible);
	}

	[Fact]
	public void Toggle_HidesAndShowsOnlyThatCategory()
	{
		var state = new SectorVisualState();
		var light = new SceneNode("light");
		var tree = new SceneNode("tree");
		state.Register(light, SectorVisualCategory.Lights);
		state.Register(tree, SectorVisualCategory.Vegetation);

		Assert.False(state.Toggle(SectorVisualCategory.Lights));
		Assert.False(light.Visible);
		Assert.True(tree.Visible);

		Assert.True(state.Toggle(SectorVisualCategory.Lights));
		Assert.True(light.Visible);
	}

	[Fact]
	public void GetCount_IgnoresOtherAndUnregisteredCategories()
	{
		var state = new SectorVisualState();
		state.Register(new SceneNode("building"), SectorVisualCategory.Other);
		state.Register(new SceneNode("decal"), SectorVisualCategory.Decals);

		Assert.Equal(0, state.GetCount(SectorVisualCategory.Other));
		Assert.Equal(1, state.GetCount(SectorVisualCategory.Decals));
		Assert.Equal(0, state.GetCount(SectorVisualCategory.Water));
	}

	[Fact]
	public void Summary_ReportsToggleStatesAndCounts()
	{
		var state = new SectorVisualState();
		state.Register(new SceneNode("light"), SectorVisualCategory.Lights);
		state.SetVisible(SectorVisualCategory.Lights, false);

		Assert.Contains("lights=off/1", state.Summary);
		Assert.Contains("decals=on/0", state.Summary);
	}
}
