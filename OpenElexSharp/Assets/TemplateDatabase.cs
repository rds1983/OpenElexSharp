using System.Diagnostics.CodeAnalysis;

namespace OpenElexSharp.Assets;

/// <summary>
/// A database of entity/item templates resolved from a <c>w_tpl_*.db</c> resource set, with GUID
/// and name lookup and parent-chain resolution.
/// </summary>
public sealed class TemplateDatabase
{
    private const long MaxTemplateSize = 32L * 1024 * 1024;

    private readonly Dictionary<string, GenomeTemplate> _byName;
    private readonly Dictionary<Guid, GenomeTemplate> _byGuid;

    private TemplateDatabase(
        GenomeTemplate[] templates,
        GenomeTemplate[] allTemplates,
        List<string> diagnostics)
    {
        Templates = templates;
        AllTemplates = allTemplates;
        Diagnostics = diagnostics;

        _byName = new Dictionary<string, GenomeTemplate>(StringComparer.OrdinalIgnoreCase);
        _byGuid = new Dictionary<Guid, GenomeTemplate>();
        foreach (var template in allTemplates)
        {
            AddName(template.Name, template);
            AddName(template.EntityName, template);
            var trimmed = template.EntityName.EndsWith(".entity", StringComparison.OrdinalIgnoreCase)
                ? template.EntityName[..^".entity".Length]
                : template.EntityName;
            AddName(trimmed, template);
            _byGuid[template.Guid] = template;
        }
    }

    /// <summary>Top-level templates, sorted by name.</summary>
    public IReadOnlyList<GenomeTemplate> Templates { get; }

    /// <summary>Top-level and embedded child templates.</summary>
    public IReadOnlyList<GenomeTemplate> AllTemplates { get; }

    /// <summary>Non-fatal parse and resolution problems.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Builds the template database from the mounted assets.</summary>
    public static TemplateDatabase Build(AssetCatalog catalog, AssetFileSystem assets)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(assets);

        var diagnostics = new List<string>();
        var templates = new List<GenomeTemplate>();
        var resources = ElexResourceIndex.Build(catalog, assets);
        diagnostics.AddRange(resources.Diagnostics);

        foreach (var resource in resources.Resources.Where(resource =>
                     string.Equals(resource.TypeToken, "tpl", StringComparison.OrdinalIgnoreCase)))
        {
            if (resource.Size > MaxTemplateSize)
            {
                diagnostics.Add($"Skipping template '{resource.Name}': resource is too large.");
                continue;
            }

            try
            {
                using var stream = assets.OpenRead(resource.VirtualPath);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                templates.Add(GenomeTemplate.Parse(resource.Name, resource.VirtualPath, buffer.ToArray()));
            }
            catch (Exception exception) when (
                exception is GenomeFormatException or IOException or UnauthorizedAccessException or
                    AssetNotFoundException)
            {
                diagnostics.Add($"Skipping template '{resource.Name}': {exception.Message}");
            }
        }

        return FromTemplates(templates, diagnostics);
    }

    /// <summary>Creates a resolved database from already parsed templates (used by tests).</summary>
    public static TemplateDatabase FromTemplates(
        IEnumerable<GenomeTemplate> templates,
        IEnumerable<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var roots = templates
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allTemplates = new List<GenomeTemplate>();
        foreach (var root in roots)
        {
            Collect(root, allTemplates);
        }

        var messages = diagnostics is null ? [] : new List<string>(diagnostics);
        var byGuid = new Dictionary<Guid, GenomeTemplate>();
        foreach (var template in allTemplates)
        {
            if (template.Guid == Guid.Empty)
            {
                messages.Add($"Template '{template.Name}' has an empty GUID.");
                continue;
            }

            if (!byGuid.TryAdd(template.Guid, template))
            {
                if (!string.Equals(byGuid[template.Guid].Name, template.Name, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add($"Duplicate template GUID {template.Guid} ('{template.Name}').");
                }
            }
        }

        foreach (var template in allTemplates)
        {
            if (template.ParentGuid != Guid.Empty && byGuid.TryGetValue(template.ParentGuid, out var parent))
            {
                template.ResolveParent(parent);
            }
        }

        return new TemplateDatabase(roots, [.. allTemplates], messages);
    }

    /// <summary>Finds a template by logical resource or entity name.</summary>
    public bool TryGetByName(string name, [NotNullWhen(true)] out GenomeTemplate? template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _byName.TryGetValue(name, out template);
    }

    /// <summary>Finds a template by GUID.</summary>
    public bool TryGetByGuid(Guid guid, [NotNullWhen(true)] out GenomeTemplate? template) =>
        _byGuid.TryGetValue(guid, out template);

    private void AddName(string name, GenomeTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _byName[name] = template;
        }
    }

    private static void Collect(GenomeTemplate template, List<GenomeTemplate> destination)
    {
        destination.Add(template);
        foreach (var child in template.Children)
        {
            Collect(child, destination);
        }
    }
}
