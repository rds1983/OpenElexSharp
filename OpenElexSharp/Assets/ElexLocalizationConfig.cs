using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One localization section from <c>loc.ini</c>.</summary>
/// <param name="Section">INI section name.</param>
/// <param name="Prefix">ID prefix used in the string table.</param>
/// <param name="Csv">Raw CSV path from the section.</param>
/// <param name="Bin">Compiled binary path from the section.</param>
public sealed record ElexLocalizationEntry(string Section, string Prefix, string Csv, string Bin);

/// <summary>
/// Parser for <c>ini/loc.ini</c>. The file maps string-table ID prefixes to raw CSV files and their
/// compiled binaries; the retail compiled binaries themselves are not mounted in the asset catalogue.
/// </summary>
public sealed class ElexLocalizationConfig
{
	private ElexLocalizationConfig(ElexLocalizationEntry[] entries)
	{
		Entries = entries;
	}

	/// <summary>Sections in file order.</summary>
	public IReadOnlyList<ElexLocalizationEntry> Entries { get; }

	/// <summary>Parses a <c>loc.ini</c> payload.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">A required section value is missing.</exception>
	public static ElexLocalizationConfig Parse(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);

		var entries = new List<ElexLocalizationEntry>();
		string? section = null;
		string? prefix = null;
		string? csv = null;
		string? bin = null;
		foreach (var rawLine in Encoding.Latin1.GetString(data).Split('\n'))
		{
			var line = rawLine.TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
			{
				continue;
			}

			if (line.StartsWith('[') && line.EndsWith(']'))
			{
				Flush(entries, section, prefix, csv, bin);
				section = line[1..^1].Trim();
				prefix = null;
				csv = null;
				bin = null;
				continue;
			}

			var separator = line.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}

			var key = line[..separator].Trim();
			var value = line[(separator + 1)..].Trim();
			switch (key.ToLowerInvariant())
			{
				case "prefix":
					prefix = value;
					break;
				case "csv":
					csv = value;
					break;
				case "bin":
					bin = value;
					break;
			}
		}

		Flush(entries, section, prefix, csv, bin);
		return new ElexLocalizationConfig([.. entries]);
	}

	/// <summary>Finds the section for a raw CSV path (case-insensitive).</summary>
	public bool TryGetCsv(string csvPath, out ElexLocalizationEntry entry)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
		foreach (var candidate in Entries)
		{
			if (string.Equals(candidate.Csv, csvPath, StringComparison.OrdinalIgnoreCase))
			{
				entry = candidate;
				return true;
			}
		}

		entry = null!;
		return false;
	}

	private static void Flush(
		List<ElexLocalizationEntry> entries,
		string? section,
		string? prefix,
		string? csv,
		string? bin)
	{
		if (section is null || prefix is null || csv is null)
		{
			return;
		}

		entries.Add(new ElexLocalizationEntry(section, prefix, csv, bin ?? string.Empty));
	}
}
