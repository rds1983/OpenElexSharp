using System.Xml;
using System.Xml.Linq;

namespace OpenElexSharp.Assets;

/// <summary>The root of <c>mount_packed.xml</c>.</summary>
public sealed record MountConfiguration(IReadOnlyList<MountVolume> Volumes);

/// <summary>One mounted volume. Paths are raw values from the file and are resolved separately.</summary>
public sealed record MountVolume(string Name, string RootPath, bool NoPacked, IReadOnlyList<MountFolder> Folders);

/// <summary>One folder mapping inside a volume.</summary>
public sealed record MountFolder(string VirtualPath, string? PhysicalPath, string? PackedPath, bool CreateFolder);

/// <summary>
/// Parses the Genome mount configuration (<c>data/ini/mount_packed.xml</c>).
/// </summary>
public static class MountConfigurationParser
{
    /// <summary>Loads and parses a mount configuration file.</summary>
    /// <exception cref="MountConfigurationException">The file is not valid XML or is malformed.</exception>
    public static MountConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    /// <summary>Parses a mount configuration from a stream.</summary>
    /// <exception cref="MountConfigurationException">The document is malformed.</exception>
    public static MountConfiguration Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new MountConfigurationException($"Invalid mount configuration XML: {exception.Message}", exception);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "MountList")
        {
            throw new MountConfigurationException("Mount configuration root element must be <MountList>.");
        }

        var volumes = new List<MountVolume>();
        foreach (var volumeElement in root.Elements().Where(e => e.Name.LocalName == "Volume"))
        {
            volumes.Add(ParseVolume(volumeElement));
        }

        return new MountConfiguration(volumes);
    }

    private static MountVolume ParseVolume(XElement element)
    {
        var name = ((string?)element.Attribute("Name"))?.Trim() ?? string.Empty;
        var rootPath = ((string?)element.Attribute("RootPath"))?.Trim() ?? string.Empty;
        var noPacked = ParseBoolean(element, "NoPacked", defaultValue: false);

        var folders = new List<MountFolder>();
        var foldersElement = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Folders");
        if (foldersElement is not null)
        {
            foreach (var folderElement in foldersElement.Elements().Where(e => e.Name.LocalName == "Folder"))
            {
                folders.Add(ParseFolder(folderElement));
            }
        }

        return new MountVolume(name, rootPath, noPacked, folders);
    }

    private static MountFolder ParseFolder(XElement element)
    {
        var virtualPath = ((string?)element.Attribute("VirtualPath"))?.Trim();
        if (string.IsNullOrEmpty(virtualPath))
        {
            throw new MountConfigurationException("A <Folder> element is missing the required VirtualPath attribute.");
        }

        var physicalPath = NullIfEmpty((string?)element.Attribute("PhysicalPath"));
        var packedPath = NullIfEmpty((string?)element.Attribute("PackedPath"));
        var createFolder = ParseBoolean(element, "CreateFolder", defaultValue: false);

        return new MountFolder(virtualPath, physicalPath, packedPath, createFolder);
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool ParseBoolean(XElement element, string attributeName, bool defaultValue)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is null)
        {
            return defaultValue;
        }

        return attribute.Value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new MountConfigurationException(
                $"Attribute {attributeName}='{attribute.Value}' on <{element.Name.LocalName}> is not a boolean."),
        };
    }
}
