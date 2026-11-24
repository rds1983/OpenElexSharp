using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>A logical resource resolved through an <c>RT02</c> database and its hashed ROM file.</summary>
/// <param name="Name">Logical resource name from the database.</param>
/// <param name="TypeToken">Resource type token from the database file name, for example <c>snd</c>.</param>
/// <param name="Language">Language token from the database file name, for example <c>en</c>.</param>
/// <param name="Generation">Generation number from the database file name.</param>
/// <param name="LogicalPath">Convenience path of the form <c>&lt;dir&gt;/&lt;name&gt;.elex&lt;type&gt;</c>.</param>
/// <param name="VirtualPath">Virtual path of the hashed ROM payload.</param>
/// <param name="Size">Logical size of the ROM payload.</param>
/// <param name="DatabasePath">Virtual path of the owning database.</param>
/// <param name="ClassHash">Resource class hash from the database header.</param>
/// <param name="ClassTag">Resource class tag from the database header.</param>
/// <param name="DatabaseEntryOffset">Offset of the record in the database.</param>
/// <param name="DatabaseEntrySize">Size of the record in the database.</param>
public sealed record ElexResource(
    string Name,
    string TypeToken,
    string Language,
    int Generation,
    string LogicalPath,
    string VirtualPath,
    long Size,
    string DatabasePath,
    uint ClassHash,
    string ClassTag,
    int DatabaseEntryOffset,
    int DatabaseEntrySize);

/// <summary>
/// Resolves ELEX logical resource names to hashed ROM files using the <c>RT02</c> databases in an
/// asset catalog.
/// </summary>
public sealed class ElexResourceIndex
{
    private const long MaxDatabaseSize = 128L * 1024 * 1024;

    private readonly Dictionary<string, ElexResource> _byName;
    private readonly List<string> _diagnostics;

    private ElexResourceIndex(ElexResource[] resources, List<string> diagnostics)
    {
        Resources = resources;
        _diagnostics = diagnostics;
        _byName = new Dictionary<string, ElexResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            _byName[resource.Name] = resource;
        }
    }

    /// <summary>Resolved resources, sorted by type and name.</summary>
    public IReadOnlyList<ElexResource> Resources { get; }

    /// <summary>Non-fatal problems encountered while parsing databases.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Number of resolved resources.</summary>
    public int Count => Resources.Count;

    /// <summary>Builds a resource index from a catalog and its backing asset system.</summary>
    public static ElexResourceIndex Build(AssetCatalog catalog, AssetFileSystem assets)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(assets);

        var diagnostics = new List<string>();
        var resources = new List<ElexResource>();
        var romsByName = catalog.Entries
            .Where(entry => string.Equals(entry.Extension, ".rom", StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => Path.GetFileName(entry.VirtualPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var databaseEntry in catalog.Entries.Where(entry =>
                     string.Equals(entry.Extension, ".db", StringComparison.OrdinalIgnoreCase)))
        {
            if (databaseEntry.Length > MaxDatabaseSize)
            {
                diagnostics.Add($"Skipping resource database '{databaseEntry.VirtualPath}': it is too large.");
                continue;
            }

            ResourceDatabase database;
            try
            {
                using var stream = assets.OpenRead(databaseEntry.VirtualPath);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                database = ResourceDatabase.Parse(buffer.ToArray());
            }
            catch (Exception exception) when (
                exception is Gar5FormatException or IOException or UnauthorizedAccessException or AssetNotFoundException)
            {
                diagnostics.Add($"Skipping resource database '{databaseEntry.VirtualPath}': {exception.Message}");
                continue;
            }

            var databaseDirectory = GetDirectory(databaseEntry.VirtualPath);
            var databaseName = Path.GetFileNameWithoutExtension(databaseEntry.VirtualPath);
            var (typeToken, language, generation) = ParseVolumeName(databaseName);

            foreach (var record in database.Entries)
            {
                var hash = ResourceDatabase.FormatHash(Djb2Hash.Compute(record.Name));
                var romName = $"{databaseName}_{hash}.rom";
                if (!romsByName.TryGetValue(romName, out var candidates))
                {
                    continue;
                }

                var rom = candidates.FirstOrDefault(candidate =>
                    candidate.VirtualPath.StartsWith(databaseDirectory + "/", StringComparison.OrdinalIgnoreCase));
                if (rom is null)
                {
                    continue;
                }

                var logicalPath = databaseDirectory.Length == 0
                    ? $"{record.Name}.elex{typeToken}"
                    : $"{databaseDirectory}/{record.Name}.elex{typeToken}";
                resources.Add(new ElexResource(
                    record.Name,
                    typeToken,
                    language,
                    generation,
                    logicalPath,
                    rom.VirtualPath,
                    rom.Length,
                    databaseEntry.VirtualPath,
                    database.ClassHash,
                    database.ClassTag,
                    record.Offset,
                    record.Size));
            }
        }

        var ordered = resources
            .OrderBy(resource => resource.TypeToken, StringComparer.OrdinalIgnoreCase)
            .ThenBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ElexResourceIndex(ordered, diagnostics);
    }

    /// <summary>Resolves a logical resource name.</summary>
    public bool TryResolve(string name, [NotNullWhen(true)] out ElexResource? resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _byName.TryGetValue(name, out resource);
    }

    /// <summary>Resolves a logical resource name with optional type and language filters.</summary>
    public bool TryResolve(
        string name,
        string? typeToken,
        string? language,
        [NotNullWhen(true)] out ElexResource? resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var candidate in Resources)
        {
            if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(typeToken) &&
                !string.Equals(candidate.TypeToken, typeToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(language) &&
                !string.Equals(candidate.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resource = candidate;
            return true;
        }

        resource = null;
        return false;
    }

    private static string GetDirectory(string virtualPath)
    {
        var separator = virtualPath.LastIndexOf('/');
        return separator < 0 ? string.Empty : virtualPath[..separator];
    }

    private static (string Type, string Language, int Generation) ParseVolumeName(string databaseName)
    {
        var parts = databaseName.Split('_');
        if (parts.Length >= 4 &&
            string.Equals(parts[0], "w", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var generation))
        {
            return (parts[1].ToLowerInvariant(), parts[^1].ToLowerInvariant(), generation);
        }

        return (string.Empty, string.Empty, 0);
    }
}
