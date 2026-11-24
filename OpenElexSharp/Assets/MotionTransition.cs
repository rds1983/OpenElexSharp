namespace OpenElexSharp.Assets;

/// <summary>
/// A transition name decoded from a <c>net</c> state graph. Names use the
/// <c>Source_Target|ConditionGroup|Condition</c> convention.
/// </summary>
/// <param name="Name">Raw transition name.</param>
/// <param name="Source">Resolved source state or node name.</param>
/// <param name="Target">Resolved target state or node name.</param>
/// <param name="Group">Condition group segment, for example <c>ConditionGroup</c>.</param>
/// <param name="Condition">Condition name, for example <c>MessageCondition</c>.</param>
public sealed record MotionTransition(
	string Name,
	string Source,
	string Target,
	string Group,
	string Condition)
{
	/// <summary>
	/// Parses a transition name. Source and target are resolved by splitting the first segment at
	/// underscores and accepting the longest split whose parts are both known graph names.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static MotionTransition Parse(string name, IReadOnlySet<string> knownNames)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(knownNames);

		var parts = name.Split('|');
		var label = parts[0];
		var group = parts.Length > 1 ? parts[1] : string.Empty;
		var condition = parts.Length > 2 ? parts[2] : group;
		var source = label;
		var target = string.Empty;
		var bestSplit = -1;
		for (var index = 1; index < label.Length - 1; index++)
		{
			if (label[index] != '_')
			{
				continue;
			}

			var left = label[..index];
			var right = label[(index + 1)..];
			if (index > bestSplit && knownNames.Contains(left) && knownNames.Contains(right))
			{
				bestSplit = index;
				source = left;
				target = right;
			}
		}

		return new MotionTransition(name, source, target, group, condition);
	}
}
