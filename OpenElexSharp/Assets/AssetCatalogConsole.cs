using System.Buffers.Binary;

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
            AssetCommandKind.Resources => Resources(command, catalog, assets, output, error),
            AssetCommandKind.Objects => Objects(command, catalog, assets, output, error),
            AssetCommandKind.Templates => Templates(command, catalog, assets, output, error),
            AssetCommandKind.Textures => Textures(command, catalog, assets, output, error),
            AssetCommandKind.Meshes => Meshes(command, catalog, assets, output, error),
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

    private static int Resources(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        var index = ElexResourceIndex.Build(catalog, assets);
        foreach (var diagnostic in index.Diagnostics.Take(5))
        {
            error.WriteLine($"Resource warning: {diagnostic}");
        }

        IEnumerable<ElexResource> query = index.Resources;
        if (!string.IsNullOrEmpty(command.Filter))
        {
            query = query.Where(resource =>
                resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
                resource.LogicalPath.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(command.TypeToken))
        {
            query = query.Where(resource =>
                string.Equals(resource.TypeToken, command.TypeToken, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(command.Language))
        {
            query = query.Where(resource =>
                string.Equals(resource.Language, command.Language, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query.ToList();
        var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
        for (var position = 0; position < shown; position++)
        {
            var resource = matches[position];
            output.WriteLine(
                $"{resource.Name}\t{resource.Size}\t{resource.TypeToken}\t{resource.Language}\t" +
                $"{resource.LogicalPath}\t{resource.VirtualPath}");
        }

        error.WriteLine(
            $"Showing {shown} of {matches.Count} resource(s); index has {index.Count:N0} resolved resources.");
        return 0;
    }

    private static int Objects(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        const long MaxObjectFileSize = 128L * 1024 * 1024;

        if (command.AssetPath is null)
        {
            error.WriteLine("objects requires an asset path.");
            return 3;
        }

        if (!catalog.TryGetEntry(command.AssetPath, out var entry))
        {
            error.WriteLine($"Asset '{command.AssetPath}' was not found.");
            return 3;
        }

        if (entry.IsExecutable)
        {
            error.WriteLine($"Refusing to read executable file '{entry.VirtualPath}'.");
            return 3;
        }

        if (entry.Length > MaxObjectFileSize)
        {
            error.WriteLine($"Asset '{entry.VirtualPath}' is too large to inspect ({FormatBytes(entry.Length)}).");
            return 3;
        }

        try
        {
            using var stream = assets.OpenRead(entry.VirtualPath);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var data = buffer.ToArray();

            if (GenomeStringTable.IsGenomeFile(data))
            {
                var table = GenomeStringTable.Parse(data);
                output.WriteLine(
                    $"GENOMFLE\t{table.Strings.Count}\t{table.BodySize}\t{table.Version}");
                error.WriteLine($"String table with {table.Strings.Count:N0} strings; no top-level objects.");
                return 0;
            }

            if (data.Length >= GenomeObject.HeaderSize &&
                BinaryPrimitives.ReadUInt32LittleEndian(data) == GenomeObject.Magic)
            {
                var genomeObject = GenomeObject.Parse(data, 0);
                WriteObject(output, "<object>", genomeObject);
                error.WriteLine("One top-level object.");
                return 0;
            }

            if (data.Length >= 12 &&
                BinaryPrimitives.ReadUInt32LittleEndian(data) == Gar5Header.Magic &&
                string.Equals(System.Text.Encoding.Latin1.GetString(data, 8, 4), GenomeHeaderDocument.Tag,
                    StringComparison.Ordinal))
            {
                var document = GenomeHeaderDocument.Parse(data);
                var objects = document.Sections.SelectMany(section => section.Entries).ToList();
                var shown = command.Limit == 0 ? objects.Count : Math.Min(command.Limit, objects.Count);
                for (var index = 0; index < shown; index++)
                {
                    var item = objects[index];
                    WriteObject(output, item.Name, item.Object);
                    if (command.ShowProperties)
                    {
                        foreach (var property in item.Object.Properties)
                        {
                            var propertyName = GenomeNameTable.GetNameOrHash(property.NameHash);
                            var decoded = GenomePropertyDecoder.Decode(property);
                            output.WriteLine(
                                $"\t{propertyName}\t{decoded.TypeName}\t{GenomePropertyDecoder.Format(decoded)}");
                        }
                    }
                }

                var nestedCount = objects.Sum(item => item.Object.NestedObjects.Count);
                error.WriteLine(
                    $"Showing {shown} of {objects.Count:N0} object(s) in {document.Sections.Count:N0} section(s); " +
                    $"{nestedCount:N0} nested object(s).");
                return 0;
            }

            error.WriteLine($"Asset '{entry.VirtualPath}' is not a DT01 document, GEC2 object or GENOMFLE archive.");
            return 3;
        }
        catch (Exception exception) when (
            exception is GenomeFormatException or IOException or UnauthorizedAccessException or
                AssetNotFoundException)
        {
            error.WriteLine(exception.Message);
            return 3;
        }
    }

    private static void WriteObject(TextWriter output, string name, GenomeObject genomeObject)
    {
        output.WriteLine(
            $"{name}\t0x{genomeObject.ClassHash:x8}\t{genomeObject.Version}\t{genomeObject.Size}\t" +
            $"{genomeObject.Properties.Count}\t{genomeObject.ClassData.Count}\t{genomeObject.NestedObjects.Count}");
    }

    private static int Templates(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        var database = TemplateDatabase.Build(catalog, assets);
        foreach (var diagnostic in database.Diagnostics.Take(5))
        {
            error.WriteLine($"Template warning: {diagnostic}");
        }

        IEnumerable<GenomeTemplate> query = database.Templates;
        if (!string.IsNullOrEmpty(command.Filter))
        {
            query = query.Where(template =>
                template.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
                template.EntityName.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
                template.Guid.ToString("D").Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query.ToList();
        var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
        for (var index = 0; index < shown; index++)
        {
            var template = matches[index];
            var parent = template.Parent?.Name ??
                         (template.ParentGuid == Guid.Empty ? "" : template.ParentGuid.ToString("D"));
            output.WriteLine(
                $"{template.Name}\t{template.Guid:D}\t{parent}\t{template.Depth}\t" +
                $"{template.PropertySets.Count}\t{template.GetEffectivePropertySets().Count}\t" +
                $"{template.GetAllPropertySets().Count}\t{template.Children.Count}");

            if (command.ShowProperties)
            {
                foreach (var propertySet in template.GetAllPropertySets())
                {
                    var className = GenomeNameTable.GetNameOrHash(propertySet.ClassHash);
                    output.WriteLine($"\t{className}\t{propertySet.Version}\t{propertySet.Properties.Count}");
                    foreach (var property in propertySet.Properties)
                    {
                        var propertyName = GenomeNameTable.GetNameOrHash(property.NameHash);
                        var decoded = GenomePropertyDecoder.Decode(property);
                        output.WriteLine(
                            $"\t\t{propertyName}\t{decoded.TypeName}\t{GenomePropertyDecoder.Format(decoded)}");
                    }
                }
            }
        }

        error.WriteLine(
            $"Showing {shown} of {matches.Count:N0} template(s); database has " +
            $"{database.Templates.Count:N0} top-level and {database.AllTemplates.Count:N0} total templates.");
        return 0;
    }

    private static int Textures(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        var resources = ElexResourceIndex.Build(catalog, assets);
        foreach (var diagnostic in resources.Diagnostics.Take(5))
        {
            error.WriteLine($"Texture warning: {diagnostic}");
        }

        IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
            string.Equals(resource.TypeToken, "img", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(command.Filter))
        {
            query = query.Where(resource =>
                resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
        var parsed = 0;
        var decodeFailures = 0;
        for (var index = 0; index < shown; index++)
        {
            var resource = matches[index];
            DdsImage image;
            try
            {
                using var stream = assets.OpenRead(resource.VirtualPath);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                image = DdsImage.Parse(buffer.ToArray());
                parsed++;
            }
            catch (Exception exception) when (
                exception is DdsFormatException or IOException or UnauthorizedAccessException or
                    AssetNotFoundException)
            {
                error.WriteLine($"Skipping texture '{resource.Name}': {exception.Message}");
                continue;
            }

            output.WriteLine(
                $"{resource.Name}\t{image.Width}\t{image.Height}\t{image.MipCount}\t" +
                $"{image.FormatName}\t{image.IsSrgb}\t{image.IsVolume}\t{image.IsCubemap}\t" +
                $"{image.ArraySize}\t{resource.Size}\t{resource.VirtualPath}");

            if (command.ShowProperties)
            {
                output.WriteLine(
                    $"\tddsOffset={image.DdsOffset}\tdataOffset={image.DataOffset}\t" +
                    $"dataLength={image.DataLength}\ttrailing={image.TrailingLength}");
                output.WriteLine(
                    $"\tfourCc={image.FourCcText}\tdxgi={image.DxgiFormat?.ToString() ?? ""}\t" +
                    $"flags=0x{image.Flags:x8}\tcaps=0x{image.Caps:x8}\tcaps2=0x{image.Caps2:x8}");
                output.WriteLine(
                    $"\tpixelFlags=0x{image.PixelFormatFlags:x8}\tbits={image.RgbBitCount}\t" +
                    $"masks={image.RedMask:x8}/{image.GreenMask:x8}/{image.BlueMask:x8}/{image.AlphaMask:x8}");
                for (var face = 0; face < image.Faces.Count; face++)
                {
                    for (var mip = 0; mip < image.Faces[face].Mipmaps.Count; mip++)
                    {
                        var level = image.Faces[face].Mipmaps[mip];
                        output.WriteLine(
                            $"\tface={face}\tmip={mip}\t{level.Width}x{level.Height}x{level.Depth}\t" +
                            $"bytes={level.Data.Length}");
                    }
                }
            }

            if (command.Decode)
            {
                try
                {
                    var normalMap = resource.Name.Contains("_Nm", StringComparison.OrdinalIgnoreCase);
                    var level = DdsTextureDecoder.Decode(
                        image,
                        0,
                        new DdsDecodeOptions(NormalMap: normalMap));
                    output.WriteLine(
                        $"\tdecoded\t{level.Width}x{level.Height}\tchecksum={Checksum(level.Pixels):x8}");
                }
                catch (Exception exception) when (
                    exception is DdsFormatException or NotSupportedException or ArgumentException)
                {
                    decodeFailures++;
                    error.WriteLine($"Could not decode texture '{resource.Name}': {exception.Message}");
                }
            }
        }

        error.WriteLine(
            $"Showing {shown} of {matches.Count:N0} texture(s); parsed {parsed:N0}" +
            (command.Decode ? $", decode failures {decodeFailures:N0}" : string.Empty) + ".");
        return 0;
    }

    private static int Meshes(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {
        var resources = ElexResourceIndex.Build(catalog, assets);
        foreach (var diagnostic in resources.Diagnostics.Take(5))
        {
            error.WriteLine($"Mesh warning: {diagnostic}");
        }

        IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
            string.Equals(resource.TypeToken, "msh", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(command.Filter))
        {
            query = query.Where(resource =>
                resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
        var parsed = 0;
        for (var index = 0; index < shown; index++)
        {
            var resource = matches[index];
            GmshMesh mesh;
            try
            {
                using var stream = assets.OpenRead(resource.VirtualPath);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                mesh = GmshMesh.Parse(resource.Name, resource.VirtualPath, buffer.ToArray());
                parsed++;
            }
            catch (Exception exception) when (
                exception is GmshFormatException or IOException or UnauthorizedAccessException or
                    AssetNotFoundException)
            {
                error.WriteLine($"Skipping mesh '{resource.Name}': {exception.Message}");
                continue;
            }

            var min = mesh.Positions[0];
            var max = mesh.Positions[0];
            foreach (var position in mesh.Positions)
            {
                min = System.Numerics.Vector3.Min(min, position);
                max = System.Numerics.Vector3.Max(max, position);
            }

            output.WriteLine(
                $"{resource.Name}\t{mesh.Positions.Count}\t{mesh.TriangleCount}\t" +
                $"{mesh.VertexAttributes.Count}\t{mesh.VertexStreams.Count}\t" +
                $"{mesh.MaterialReferences.Count}\t" +
                $"{min.X:0.###},{min.Y:0.###},{min.Z:0.###}\t" +
                $"{max.X:0.###},{max.Y:0.###},{max.Z:0.###}\t{resource.VirtualPath}");

            if (command.ShowProperties)
            {
                output.WriteLine(
                    $"\tgmshOffset={mesh.GmshOffset}\tversion={mesh.Version}\t" +
                    $"indexOffset={mesh.IndexOffset}\tindices={mesh.Indices.Count}\t" +
                    $"isTriangleList={mesh.IsTriangleList}");
                foreach (var attribute in mesh.VertexAttributes)
                {
                    output.WriteLine(
                        $"\tattribute\t{attribute.UsageName}\t{attribute.UsageIndex}\t" +
                        $"format={attribute.Format}\tstream={attribute.Stream}\t" +
                        $"unknown={attribute.Unknown0:x8}/{attribute.Unknown1:x8}/{attribute.Unknown2:x8}");
                }

                foreach (var stream in mesh.VertexStreams)
                {
                    output.WriteLine(
                        $"\tstream\t{stream.UsageName}\toffset={stream.Offset}\t" +
                        $"stride={stream.Stride}\tunknown={stream.Unknown}");
                }

                for (var material = 0; material < mesh.MaterialReferences.Count; material++)
                {
                    var reference = mesh.MaterialReferences[material];
                    output.WriteLine(
                        $"\tmaterial\t{material}\t" +
                        string.Join('/', reference.Raw.Select(value => value.ToString("x8"))));
                }
            }
        }

        error.WriteLine(
            $"Showing {shown} of {matches.Count:N0} mesh(es); parsed {parsed:N0}.");
        return 0;
    }

    private static uint Checksum(byte[] data)
    {
        var hash = 2166136261u;
        foreach (var value in data)
        {
            hash ^= value;
            hash *= 16777619u;
        }

        return hash;
    }

    private static int Export(
        AssetCommandLine command,
        AssetCatalog catalog,
        AssetFileSystem assets,
        TextWriter output,
        TextWriter error)
    {        if (command.AssetPath is null || command.DestinationPath is null)
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
