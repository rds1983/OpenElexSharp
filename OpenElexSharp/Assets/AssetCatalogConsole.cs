namespace OpenElexSharp.Assets;

/// <summary>Headless output for the <c>assets</c> inspector commands.</summary>
public static class AssetCatalogConsole
{
    /// <summary>Runs a parsed asset command and returns a process exit code.</summary>
    public static int Run(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return command.Kind switch
        {
            AssetCommandKind.Help => WriteUsage(output),
            AssetCommandKind.List => List(command, catalog, output, error),
            AssetCommandKind.Stats => Stats(catalog, output),
            AssetCommandKind.Export => Export(command, catalog, assets, output, error),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    private static int WriteUsage(TextWriter output)
    {
        output.WriteLine(AssetCommandLine.Usage);
        return 0;
    }

    private static int List(AssetCommandLine command, AssetCatalog catalog, TextWriter output, TextWriter error)
    {
        IEnumerable<AssetCatalogEntry> query = catalog.Entries;
        if (!command.IncludeBinaries)
        {
            query = query.Where(entry => !entry.IsExecutable);
        }

        if (!string.IsNullOrEmpty(command.Filter))
        {
            query = query.Where(entry =>
                entry.VirtualPath.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(command.Extension))
        {
            query = query.Where(entry =>
                string.Equals(entry.Extension, command.Extension, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(command.Source))
        {
            query = query.Where(entry =>
                entry.SourceName.Contains(command.Source, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(command.Language))
        {
            query = query.Where(entry =>
                string.Equals(entry.Language, command.Language, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query.ToList();
        var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
        for (var index = 0; index < shown; index++)
        {
            var entry = matches[index];
            output.WriteLine($"{entry.VirtualPath}\t{entry.Length}\t{entry.SourceName}");
        }

        error.WriteLine(
            $"Showing {shown} of {matches.Count} matching asset(s); catalog has {catalog.Entries.Count:N0} entries.");
        return 0;
    }

    private static int Stats(AssetCatalog catalog, TextWriter output)
    {
        var stats = catalog.GetStatistics();
        output.WriteLine($"Assets: {stats.TotalCount:N0} (packed {stats.PackedCount:N0}, loose {stats.LooseCount:N0})");
        output.WriteLine($"Executables: {stats.ExecutableCount:N0}");
        output.WriteLine($"Total size: {FormatBytes(stats.TotalBytes)}");
        output.WriteLine();
        WriteGroups(output, "By extension:", stats.Extensions);
        output.WriteLine();
        WriteGroups(output, "By source:", stats.Sources);
        return 0;
    }

    private static int Export(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        if (command.AssetPath is null || command.DestinationPath is null)
        {
            error.WriteLine("export requires an asset path and a destination.");
            return 3;
        }

        if (!catalog.TryGetEntry(command.AssetPath, out var entry))
        {
            error.WriteLine($"Asset '{command.AssetPath}' was not found.");
            return 3;
        }

        if (entry.IsExecutable)
        {
            error.WriteLine($"Refusing to export executable file '{entry.VirtualPath}'.");
            return 3;
        }

        try
        {
            var destinationPath = Path.GetFullPath(command.DestinationPath);
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var source = assets.OpenRead(entry.VirtualPath);
            using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
            if (destination.Length != entry.Length)
            {
                throw new IOException(
                    $"Exported {destination.Length} bytes but the catalog reported {entry.Length}.");
            }

            output.WriteLine($"Exported {entry.VirtualPath} -> {destinationPath} ({FormatBytes(entry.Length)}).");
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                or AssetNotFoundException)
        {
            error.WriteLine(exception.Message);
            return 3;
        }
    }

    private static void WriteGroups(TextWriter output, string title, IEnumerable<AssetGroupStatistics> groups)
    {
        output.WriteLine(title);
        foreach (var group in groups)
        {
            output.WriteLine($"  {group.Count,8:N0}  {FormatBytes(group.Bytes),12}  {group.Name}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double Kilobyte = 1024;
        const double Megabyte = Kilobyte * 1024;
        const double Gigabyte = Megabyte * 1024;

        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / Kilobyte:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / Megabyte:F1} MB",
            _ => $"{bytes / Gigabyte:F1} GB",
        };
    }
}
