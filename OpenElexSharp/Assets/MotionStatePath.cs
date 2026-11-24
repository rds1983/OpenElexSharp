namespace OpenElexSharp.Assets;

/// <summary>
/// A hierarchical state-machine path decoded from a <c>net</c> name such as
/// <c>Main|Animation|Locomotion|Walk|ConditionGroup|MessageCondition</c>.
/// </summary>
/// <param name="Name">Raw path name.</param>
/// <param name="Segments">State-machine path segments before the condition group.</param>
/// <param name="Group">Condition group segment, for example <c>ConditionGroup</c>.</param>
/// <param name="Condition">Condition segment(s) after the group.</param>
public sealed record MotionStatePath(
	string Name,
	IReadOnlyList<string> Segments,
	string Group,
	string Condition)
{
	/// <summary>The deepest state segment, or an empty string when there are no segments.</summary>
	public string Leaf => Segments.Count > 0 ? Segments[^1] : string.Empty;

	/// <summary>Parses a path name. Returns <see langword="false"/> when it has no path separator.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public static bool TryParse(string name, out MotionStatePath? path)
	{
		ArgumentNullException.ThrowIfNull(name);
		path = null;
		var parts = name.Split('|');
		if (parts.Length < 2)
		{
			return false;
		}

		var groupIndex = Array.FindIndex(
			parts, part => string.Equals(part, "ConditionGroup", StringComparison.Ordinal));
		IReadOnlyList<string> segments;
		string group;
		string condition;
		if (groupIndex >= 0)
		{
			segments = parts[..groupIndex].Where(part => part.Length > 0).ToArray();
			group = parts[groupIndex];
			condition = string.Join('|', parts[(groupIndex + 1)..]);
		}
		else
		{
			segments = [parts[0]];
			group = parts.Length > 1 ? parts[1] : string.Empty;
			condition = parts.Length > 2 ? parts[2] : string.Empty;
		}

		if (segments.Count == 0)
		{
			return false;
		}

		path = new MotionStatePath(name, segments, group, condition);
		return true;
	}
}
