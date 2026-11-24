using System.Diagnostics.CodeAnalysis;

namespace OpenElexSharp.Assets;

/// <summary>A loose folder on disk mounted at a virtual folder.</summary>
public sealed class LooseFolderSource : IAssetSource
{
    private readonly string _physicalRoot;
    private readonly object _indexLock = new();
    private Dictionary<string, AssetEntry>? _index;

    /// <param name="name">Diagnostic name, for example "G3:data/extern".</param>
    /// <param name="virtualRoot">Virtual folder; empty for the virtual root.</param>
    /// <param name="physicalRoot">Absolute physical folder path.</param>
    public LooseFolderSource(string name, string virtualRoot, string physicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(virtualRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        Name = name;
        VirtualRoot = virtualRoot.Length == 0 ? string.Empty : VirtualPath.Normalize(virtualRoot);
        _physicalRoot = Path.GetFullPath(physicalRoot);
    }

    public string Name { get; }

    public string VirtualRoot { get; }

    public bool TryGetEntry(string virtualPath, [NotNullWhen(true)] out AssetEntry? entry)
    {
        var normalizedPath = VirtualPath.Normalize(virtualPath);
        return GetEntries().TryGetValue(normalizedPath, out entry);
    }

    public Stream OpenRead(string virtualPath)
    {
        if (!TryGetEntry(virtualPath, out var entry))
        {
            throw new AssetNotFoundException($"Asset '{virtualPath}' was not found in source '{Name}'.");
        }

        return new FileStream(entry.PhysicalPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public IEnumerable<AssetEntry> EnumerateEntries() => GetEntries().Values;

    private Dictionary<string, AssetEntry> GetEntries()
    {
        var existing = Volatile.Read(ref _index);
        if (existing is not null)
        {
            return existing;
        }

        lock (_indexLock)
        {
            existing = _index;
            if (existing is null)
            {
                existing = BuildIndex();
                Volatile.Write(ref _index, existing);
            }

            return existing;
        }
    }

    private Dictionary<string, AssetEntry> BuildIndex()
    {
        var entries = new Dictionary<string, AssetEntry>(VirtualPath.Comparer);
        if (!Directory.Exists(_physicalRoot))
        {
            return entries;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var physicalPath in Directory.EnumerateFiles(_physicalRoot, "*", options))
        {
            var relativePath = Path.GetRelativePath(_physicalRoot, physicalPath).Replace('\\', '/');
            var virtualPath = VirtualRoot.Length == 0 ? relativePath : $"{VirtualRoot}/{relativePath}";
            virtualPath = VirtualPath.Normalize(virtualPath);
            var length = new FileInfo(physicalPath).Length;
            entries[virtualPath] = new AssetEntry(virtualPath, length, Name, physicalPath);
        }

        return entries;
    }
}
