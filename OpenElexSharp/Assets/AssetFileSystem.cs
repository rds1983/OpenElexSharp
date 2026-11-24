using System.Diagnostics.CodeAnalysis;

namespace OpenElexSharp.Assets;

/// <summary>
/// A layered virtual file system. Sources are checked in reverse mount order, so a source mounted
/// later (a mod, for example) shadows earlier ones.
/// </summary>
public sealed class AssetFileSystem
{
    private readonly List<IAssetSource> _sources;
    private readonly List<PackedMount> _packedMounts;
    private readonly List<string> _diagnostics;

    /// <param name="sources">Sources in mount order; later sources take precedence.</param>
    /// <param name="packedMounts">Packed folders that still need a PAK reader.</param>
    /// <param name="diagnostics">Warnings collected while mounting, for example skipped broken mods.</param>
    public AssetFileSystem(
        IEnumerable<IAssetSource> sources,
        IEnumerable<PackedMount>? packedMounts = null,
        IEnumerable<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = new List<IAssetSource>(sources);
        if (_sources.Any(source => source is null))
        {
            throw new ArgumentException("Asset sources must not contain null entries.", nameof(sources));
        }

        _packedMounts = packedMounts is null ? [] : [.. packedMounts];
        _diagnostics = diagnostics is null ? [] : [.. diagnostics];
    }

    /// <summary>Loose sources in mount order.</summary>
    public IReadOnlyList<IAssetSource> Sources => _sources;

    /// <summary>Packed folders discovered in the mount configuration.</summary>
    public IReadOnlyList<PackedMount> PackedMounts => _packedMounts;

    /// <summary>Non-fatal problems encountered while mounting assets.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>
    /// Mounts an installation using its <c>mount_packed.xml</c>. Loose folders and PAK volumes are
    /// both exposed through the resulting file system.
    /// </summary>
    public static AssetFileSystem MountFromInstallation(ElexInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var configuration = MountConfigurationParser.Load(installation.MountConfigurationPath);
        return Mount(installation, configuration);
    }

    /// <summary>Mounts an installation using an already parsed mount configuration.</summary>
    public static AssetFileSystem Mount(ElexInstallation installation, MountConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(configuration);

        var resolver = MountPathResolver.CreateDefault(installation);
        var sources = new List<IAssetSource>();
        var packedMounts = new List<PackedMount>();
        var diagnostics = new List<string>();

        foreach (var volume in configuration.Volumes)
        {
            string volumeRoot;
            try
            {
                volumeRoot = resolver.Resolve(volume.RootPath, installation.AppPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new MountConfigurationException(
                    $"Invalid RootPath '{volume.RootPath}' in volume '{volume.Name}': {exception.Message}",
                    exception);
            }

            foreach (var folder in volume.Folders)
            {
                try
                {
                    var virtualRoot = VirtualPath.Normalize(folder.VirtualPath);
                    var sourceName = volume.Name.Length == 0
                        ? folder.VirtualPath
                        : $"{volume.Name}:{folder.VirtualPath}";

                    if (folder.PackedPath is not null)
                    {
                        var packedPath = resolver.Resolve(folder.PackedPath, volumeRoot);
                        packedMounts.Add(new PackedMount(virtualRoot, packedPath, Enabled: !volume.NoPacked));
                        if (!volume.NoPacked)
                        {
                            var packedSource = new PackedAssetSource(sourceName, virtualRoot, packedPath);
                            sources.Add(packedSource);
                            diagnostics.AddRange(packedSource.Diagnostics);
                        }

                        continue;
                    }

                    var physicalPath = resolver.Resolve(folder.PhysicalPath ?? folder.VirtualPath, volumeRoot);
                    if (!Directory.Exists(physicalPath))
                    {
                        continue;
                    }

                    sources.Add(new LooseFolderSource(sourceName, virtualRoot, physicalPath));
                }
                catch (Exception exception) when (
                    exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    throw new MountConfigurationException(
                        $"Invalid path in volume '{volume.Name}', folder '{folder.VirtualPath}': {exception.Message}",
                        exception);
                }
            }
        }

        return new AssetFileSystem(sources, packedMounts, diagnostics);
    }

    /// <summary>Returns whether a file exists in any source.</summary>
    public bool Exists(string virtualPath) => TryGetEntry(virtualPath, out _);

    /// <summary>Finds a file, preferring the most recently mounted source.</summary>
    public bool TryGetEntry(string virtualPath, [NotNullWhen(true)] out AssetEntry? entry)
    {
        var normalizedPath = VirtualPath.Normalize(virtualPath);
        for (var index = _sources.Count - 1; index >= 0; index--)
        {
            if (_sources[index].TryGetEntry(normalizedPath, out entry))
            {
                return true;
            }
        }

        entry = null;
        return false;
    }

    /// <summary>Opens a file for reading, preferring the most recently mounted source.</summary>
    /// <exception cref="AssetNotFoundException">The file does not exist in any source.</exception>
    public Stream OpenRead(string virtualPath)
    {
        var normalizedPath = VirtualPath.Normalize(virtualPath);
        for (var index = _sources.Count - 1; index >= 0; index--)
        {
            var source = _sources[index];
            if (source.TryGetEntry(normalizedPath, out _))
            {
                return source.OpenRead(normalizedPath);
            }
        }

        throw new AssetNotFoundException($"Asset '{virtualPath}' was not found in any mounted source.");
    }

    /// <summary>
    /// Enumerates all files from all sources, with later sources shadowing earlier ones. The result
    /// is sorted by virtual path.
    /// </summary>
    public IReadOnlyList<AssetEntry> EnumerateEntries()
    {
        var entries = new Dictionary<string, AssetEntry>(VirtualPath.Comparer);
        foreach (var source in _sources)
        {
            foreach (var entry in source.EnumerateEntries())
            {
                entries[entry.VirtualPath] = entry;
            }
        }

        return [.. entries.Values.OrderBy(entry => entry.VirtualPath, VirtualPath.Comparer)];
    }
}
