namespace OpenElexSharp.Assets;

/// <summary>
/// Helpers for virtual asset paths. Virtual paths always use forward slashes, have no leading
/// slash and are matched case-insensitively, mirroring the game's virtual file system.
/// </summary>
public static class VirtualPath
{
	/// <summary>The comparer used for virtual path lookups.</summary>
	public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

	/// <summary>
	/// Normalizes a virtual path: converts backslashes, removes empty and "." segments, strips a
	/// leading slash and rejects parent traversal and volume prefixes.
	/// </summary>
	/// <exception cref="ArgumentException">The path is empty or contains ".." or ":".</exception>
	public static string Normalize(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var normalizedInput = path.Replace('\\', '/').Trim();
		if (normalizedInput.StartsWith("~/", StringComparison.Ordinal))
		{
			normalizedInput = normalizedInput[2..];
		}

		var segments = new List<string>();
		foreach (var segment in normalizedInput.Split('/'))
		{
			if (segment.Length == 0 || segment == ".")
			{
				continue;
			}

			if (segment == "..")
			{
				throw new ArgumentException($"Virtual path '{path}' must not contain '..'.", nameof(path));
			}

			if (segment.Contains(':', StringComparison.Ordinal))
			{
				throw new ArgumentException($"Virtual path '{path}' must not contain a volume prefix.", nameof(path));
			}

			segments.Add(segment);
		}

		if (segments.Count == 0)
		{
			throw new ArgumentException($"Virtual path '{path}' is empty.", nameof(path));
		}

		return string.Join('/', segments);
	}
}
