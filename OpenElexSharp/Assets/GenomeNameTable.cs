using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace OpenElexSharp.Assets;

/// <summary>
/// Hash-to-name registry for Genome classes, property types, property names and enum members.
/// The embedded table is generated from the credited rmtools name registry.
/// </summary>
public static class GenomeNameTable
{
	private static readonly Lazy<IReadOnlyDictionary<uint, string>> Table = new(Load);

	/// <summary>Number of known names.</summary>
	public static int Count => Table.Value.Count;

	/// <summary>Looks up a name by its case-sensitive DJB2 hash.</summary>
	public static bool TryGet(uint hash, [NotNullWhen(true)] out string? name) =>
		Table.Value.TryGetValue(hash, out name);

	/// <summary>Returns a name or a formatted hash when the name is unknown.</summary>
	public static string GetNameOrHash(uint hash) =>
		Table.Value.TryGetValue(hash, out var name) ? name : $"0x{hash:x8}";

	private static IReadOnlyDictionary<uint, string> Load()
	{
		var assembly = typeof(GenomeNameTable).Assembly;
		var resourceName = assembly
			.GetManifestResourceNames()
			.FirstOrDefault(name => name.EndsWith("GenomeNames.json", StringComparison.Ordinal))
			?? throw new InvalidOperationException("The embedded Genome name table is missing.");

		using var stream = assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException("The embedded Genome name table could not be opened.");
		using var document = JsonDocument.Parse(stream);
		var names = document.RootElement.GetProperty("names");
		var result = new Dictionary<uint, string>(names.EnumerateObject().Count());
		foreach (var property in names.EnumerateObject())
		{
			var hash = uint.Parse(property.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			result[hash] = property.Value.GetString() ?? string.Empty;
		}

		return result;
	}
}
