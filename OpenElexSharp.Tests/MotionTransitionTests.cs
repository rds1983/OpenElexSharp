using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionTransitionTests
{
	[Fact]
	public void Parse_ResolvesSourceAndTarget()
	{
		var known = new HashSet<string>(StringComparer.Ordinal) { "Closed", "Opened" };

		var transition = MotionTransition.Parse(
			"Closed_Opened|ConditionGroup|MessageCondition", known);

		Assert.Equal("Closed_Opened|ConditionGroup|MessageCondition", transition.Name);
		Assert.Equal("Closed", transition.Source);
		Assert.Equal("Opened", transition.Target);
		Assert.Equal("ConditionGroup", transition.Group);
		Assert.Equal("MessageCondition", transition.Condition);
	}

	[Fact]
	public void Parse_UsesLongestKnownSplit()
	{
		var known = new HashSet<string>(StringComparer.Ordinal)
		{
			"StateMachine1",
			"StateMachine2",
		};

		var transition = MotionTransition.Parse(
			"StateMachine1_StateMachine2|ConditionGroup|StopCustomPlayback", known);

		Assert.Equal("StateMachine1", transition.Source);
		Assert.Equal("StateMachine2", transition.Target);
	}

	[Fact]
	public void Parse_ResolvesNodeSource()
	{
		var known = new HashSet<string>(StringComparer.Ordinal)
		{
			"chst_bsk1_open",
			"Opened",
		};

		var transition = MotionTransition.Parse(
			"chst_bsk1_open_Opened|ConditionGroup|FractionThroughSource", known);

		Assert.Equal("chst_bsk1_open", transition.Source);
		Assert.Equal("Opened", transition.Target);
		Assert.Equal("FractionThroughSource", transition.Condition);
	}

	[Fact]
	public void Parse_UnknownLabel_KeepsRawName()
	{
		var transition = MotionTransition.Parse("Unknown_Label|Group|Condition", new HashSet<string>());

		Assert.Equal("Unknown_Label", transition.Source);
		Assert.Empty(transition.Target);
	}

	[Fact]
	public void Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => MotionTransition.Parse(null!, new HashSet<string>()));
		Assert.Throws<ArgumentNullException>(
			() => MotionTransition.Parse("A_B", null!));
	}
}
