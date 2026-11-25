using OpenElexSharp.Assets;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Resolves the optional visual companion sectors for a requested building/decoration sector:
/// nature, SpeedTree, rivers and lights. Exact names are preferred; a missing companion is simply
/// skipped.
/// </summary>
public static class SectorCompanions
{
	private static readonly string[] Suffixes =
		["_Buildings", "_Decoration", "_Nature", "_SpeedTrees", "_SpeedTree", "_Speedtree", "_Lights", "_Rivers"];

	/// <summary>
	/// Returns candidate companion sector names for <paramref name="sectorName"/> in preference order.
	/// </summary>
	public static IReadOnlyList<string> CandidateNames(string sectorName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sectorName);
		var prefix = StripSuffix(sectorName);
		var region = prefix.Split('_', 2)[0];
		var candidates = new List<string>();
		Add(prefix + "_Nature");
		Add(prefix + "_Speedtree");
		Add(prefix + "_SpeedTree");
		Add(prefix + "_SpeedTrees");
		Add(prefix + "_Lights");
		Add(prefix + "_Rivers");
		Add(region + "_Nature");
		Add(region + "_SpeedTrees");
		Add(region + "_SpeedTree");
		Add(region + "_Lights");
		Add(region + "_Rivers");
		return candidates;

		void Add(string candidate)
		{
			if (!string.Equals(candidate, sectorName, StringComparison.OrdinalIgnoreCase) &&
				!candidates.Contains(candidate, StringComparer.Ordinal))
			{
				candidates.Add(candidate);
			}
		}
	}

	/// <summary>
	/// Resolves existing companion resources for the requested sectors. The requested sector names
	/// themselves are excluded.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static IReadOnlyList<ElexResource> Resolve(
		ElexResourceIndex resources,
		IReadOnlyCollection<string> sectorNames)
	{
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(sectorNames);
		var result = new List<ElexResource>();
		var seen = new HashSet<string>(sectorNames, StringComparer.OrdinalIgnoreCase);
		foreach (var sectorName in sectorNames)
		{
			foreach (var candidate in CandidateNames(sectorName))
			{
				if (!resources.TryResolve(candidate, "sec", null, out var resource) ||
					!seen.Add(resource.Name))
				{
					continue;
				}

				result.Add(resource);
			}
		}

		return result;
	}

	private static string StripSuffix(string sectorName)
	{
		foreach (var suffix in Suffixes)
		{
			if (sectorName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return sectorName[..^suffix.Length];
			}
		}

		return sectorName;
	}
}
