using System.Diagnostics.CodeAnalysis;

namespace OpenElexSharp.Assets;

/// <summary>
/// Mounts all PAK volumes in a folder under one virtual root. Volumes are read in file-name order,
/// so later volumes (typically mods) shadow earlier ones.
/// </summary>
public sealed class PackedAssetSource : IAssetSource
{
    private readonly Dictionary<string, PackedFile> _entries;
    private readonly AssetEntry[] _assetEntries;
    private readonly List<string> _diagnostics;

    /// <param name="name">Diagnostic name, for example "G3:data/compiled".</param>
    /// <param name="virtualRoot">Virtual folder that archive contents are mounted at.</param>
    /// <param name="physicalFolder">Folder that contains the <c>.pak</c> files.</param>
    public PackedAssetSource(string name, string virtualRoot, string physicalFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(virtualRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalFolder);

        Name = name;
        VirtualRoot = VirtualPath.Normalize(virtualRoot);
        _diagnostics = [];
        _entries = new Dictionary<string, PackedFile>(VirtualPath.Comparer);

        if (!Directory.Exists(physicalFolder))
        {
            _diagnostics.Add($"Packed folder '{physicalFolder}' does not exist.");
            _assetEntries = [];
            return;
        }

        var pakFiles = Directory
            .EnumerateFiles(physicalFolder, "*.pak", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        foreach (var pakPath in pakFiles)
        {
            PakArchive archive;
            try
            {
                archive = PakArchive.Open(pakPath);
            }
            catch (Exception exception) when (
                exception is PakFormatException or IOException or UnauthorizedAccessException)
            {
                _diagnostics.Add($"Skipping PAK '{Path.GetFileName(pakPath)}': {exception.Message}");
                continue;
            }

            foreach (var entry in archive.Entries)
            {
                string virtualPath;
                try
                {
                    virtualPath = VirtualPath.Normalize($"{VirtualRoot}/{entry.VirtualPath}");
                }
                catch (ArgumentException exception)
                {
                    _diagnostics.Add(
                        $"Skipping entry '{entry.VirtualPath}' in '{Path.GetFileName(pakPath)}': {exception.Message}");
                    continue;
                }

                _entries[virtualPath] = new PackedFile(
                    archive,
                    entry,
                    new AssetEntry(
                        virtualPath,
                        entry.UncompressedSize,
                        $"{Name}/{Path.GetFileName(archive.FilePath)}",
                        null));
            }
        }

        _assetEntries = [.. _entries.Values.Select(file => file.AssetEntry)];
    }

    public string Name { get; }

    public string VirtualRoot { get; }

    /// <summary>Problems encountered while loading archives or entries.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Number of files exposed by this source.</summary>
    public int EntryCount => _entries.Count;

    public bool TryGetEntry(string virtualPath, [NotNullWhen(true)] out AssetEntry? entry)
    {
        var normalizedPath = VirtualPath.Normalize(virtualPath);
        if (_entries.TryGetValue(normalizedPath, out var packedFile))
        {
            entry = packedFile.AssetEntry;
            return true;
        }

        entry = null;
        return false;
    }

    public Stream OpenRead(string virtualPath)
    {
        var normalizedPath = VirtualPath.Normalize(virtualPath);
        if (!_entries.TryGetValue(normalizedPath, out var packedFile))
        {
            throw new AssetNotFoundException($"Asset '{virtualPath}' was not found in packed source '{Name}'.");
        }

        return packedFile.Archive.OpenRead(packedFile.Entry);
    }

    public IEnumerable<AssetEntry> EnumerateEntries() => _assetEntries;

    private sealed record PackedFile(PakArchive Archive, PakEntry Entry, AssetEntry AssetEntry);
}
