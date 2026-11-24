using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionStatePathTests
{
	[Fact]
	public void TryParse_SimpleTransitionPath()
	{
		var parsed = MotionStatePath.TryParse(
			"Closed_Opened|ConditionGroup|MessageCondition", out var path);

		Assert.True(parsed);
		Assert.NotNull(path);
		Assert.Equal(["Closed_Opened"], path.Segments);
		Assert.Equal("ConditionGroup", path.Group);
		Assert.Equal("MessageCondition", path.Condition);
		Assert.Equal("Closed_Opened", path.Leaf);
	}

	[Fact]
	public void TryParse_NestedPath()
	{
		var parsed = MotionStatePath.TryParse(
			"Main|Animation|Locomotion|Walk|ConditionGroup|MessageCondition", out var path);

		Assert.True(parsed);
		Assert.NotNull(path);
		Assert.Equal(["Main", "Animation", "Locomotion", "Walk"], path.Segments);
		Assert.Equal("Walk", path.Leaf);
		Assert.Equal("MessageCondition", path.Condition);
	}

	[Fact]
	public void TryParse_NoSeparator_ReturnsFalse()
	{
		Assert.False(MotionStatePath.TryParse("plainName", out var path));
		Assert.Null(path);
	}

	[Fact]
	public void TryParse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MotionStatePath.TryParse(null!, out _));
	}
}
