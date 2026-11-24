using System.Diagnostics.CodeAnalysis;

namespace OpenElexSharp.Assets;

/// <summary>An immutable source of virtual assets (a loose folder today, a PAK archive later).</summary>
public interface IAssetSource
{
	/// <summary>A human-readable name used in diagnostics, for example "G3:data/extern".</summary>
	string Name { get; }

	/// <summary>The normalized virtual folder this source is mounted at, or an empty string for the root.</summary>
	string VirtualRoot { get; }

	/// <summary>Tries to find a file by its virtual path.</summary>
	bool TryGetEntry(string virtualPath, [NotNullWhen(true)] out AssetEntry? entry);

	/// <summary>Opens a file by its virtual path for reading.</summary>
	/// <exception cref="AssetNotFoundException">The file does not exist in this source.</exception>
	Stream OpenRead(string virtualPath);

	/// <summary>Enumerates every file provided by this source.</summary>
	IEnumerable<AssetEntry> EnumerateEntries();
}
