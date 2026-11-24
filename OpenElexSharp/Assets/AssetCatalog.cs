using System.Diagnostics.CodeAnalysis;

namespace OpenElexSharp.Assets;

/// <summary>One indexed virtual asset.</summary>
/// <param name="VirtualPath">Normalized virtual path.</param>
/// <param name="Length">Logical (uncompressed) size in bytes.</param>
/// <param name="SourceName">Mount and archive that provides the file.</param>
/// <param name="Extension">Lower-case extension including the dot, or "(none)".</param>
/// <param name="Language">Language inferred from a packed volume name, when available.</param>
/// <param name="IsPacked">Whether the file comes from a PAK volume.</param>
/// <param name="IsExecutable">Whether the extension is an executable or loadable binary.</param>
public sealed record AssetCatalogEntry(
    string VirtualPath,
    long Length,
    string SourceName,
    string Extension,
    string? Language,
    bool IsPacked,
    bool IsExecutable);

/// <summary>Aggregated counts for one extension or source.</summary>
public sealed record AssetGroupStatistics(string Name, int Count, long Bytes);

/// <summary>Summary of an asset catalog.</summary>
public sealed record AssetStatistics(
    int TotalCount,
    int PackedCount,
    int LooseCount,
    int ExecutableCount,
    long TotalBytes,
    IReadOnlyList<AssetGroupStatistics> Extensions,
    IReadOnlyList<AssetGroupStatistics> Sources);

/// <summary>
/// A searchable snapshot of all virtual assets. Building the catalog does not open asset
/// payloads; it only reads metadata already indexed by the mounted sources.
/// </summary>
public sealed class AssetCatalog
{
    private static readonly HashSet<string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "de", "fr", "it", "es", "pl", "ru", "cs", "zh", "ko", "ja", "pt", "tr", "nl", "hu", "na",
    };

    private readonly Dictionary<string, AssetCatalogEntry> _byPath;

    private AssetCatalog(AssetCatalogEntry[] entries)
    {
        Entries = entries;
        _byPath = new Dictionary<string, AssetCatalogEntry>(VirtualPath.Comparer);
        foreach (var entry in entries)
        {
            _byPath[entry.VirtualPath] = entry;
        }
    }

    /// <summary>All entries, sorted by virtual path.</summary>
    public IReadOnlyList<AssetCatalogEntry> Entries { get; }

    /// <summary>Builds a catalog from the currently mounted assets.</summary>
    public static AssetCatalog Build(AssetFileSystem assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var entries = assets
            .EnumerateEntries()
            .Select(entry =>
            {
                var extension = GetExtension(entry.VirtualPath);
                return new AssetCatalogEntry(
                    entry.VirtualPath,
                    entry.Length,
                    entry.SourceName,
                    extension,
                    entry.PhysicalPath is null ? GetLanguage(entry.SourceName) : null,
                    entry.PhysicalPath is null,
                    AssetKinds.IsExecutable(extension));
            })
            .OrderBy(entry => entry.VirtualPath, VirtualPath.Comparer)
            .ToArray();

        return new AssetCatalog(entries);
    }

    /// <summary>Finds a catalog entry by virtual path.</summary>
    public bool TryGetEntry(string virtualPath, [NotNullWhen(true)] out AssetCatalogEntry? entry)
    {
        var normalizedPath = VirtualPath.Normalize(virtualPath);
        return _byPath.TryGetValue(normalizedPath, out entry);
    }

    /// <summary>Computes totals grouped by extension and source.</summary>
    public AssetStatistics GetStatistics()
    {
        var extensions = Entries
            .GroupBy(entry => entry.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AssetGroupStatistics(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.Length)))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sources = Entries
            .GroupBy(entry => entry.SourceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AssetGroupStatistics(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.Length)))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var packed = Entries.Count(entry => entry.IsPacked);
        return new AssetStatistics(
            Entries.Count,
            packed,
            Entries.Count - packed,
            Entries.Count(entry => entry.IsExecutable),
            Entries.Sum(entry => entry.Length),
            extensions,
            sources);
    }

    private static string GetExtension(string virtualPath)
    {
        var extension = Path.GetExtension(virtualPath);
        return extension.Length == 0 ? "(none)" : extension.ToLowerInvariant();
    }

    private static string? GetLanguage(string sourceName)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceName);
        var parts = fileName.Split('_');
        if (parts.Length != 3 ||
            parts[0] is not ("c" or "m") ||
            parts[1].Length == 0 ||
            !parts[1].All(char.IsAsciiDigit) ||
            !LanguageCodes.Contains(parts[2]))
        {
            return null;
        }

        return parts[2].ToLowerInvariant();
    }
}
