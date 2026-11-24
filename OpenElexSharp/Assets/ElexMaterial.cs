using System.Buffers.Binary;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace OpenElexSharp.Assets;

/// <summary>Semantic role of a texture referenced by an <c>eCMaterialResource2</c>.</summary>
public enum ElexMaterialTextureUsage
{
    /// <summary>Diffuse/albedo map (<c>_Df</c> or <c>_DfA</c>).</summary>
    Diffuse,

    /// <summary>Normal map (<c>_Nm</c>).</summary>
    Normal,

    /// <summary>Specular map (<c>_Sm</c>).</summary>
    Specular,

    /// <summary>Secondary specular map (<c>_Sm2</c>).</summary>
    Specular2,

    /// <summary>Emissive map (<c>_Em</c>).</summary>
    Emissive,

    /// <summary>Color map (<c>_Co</c>).</summary>
    Color,

    /// <summary>Texture whose suffix is not recognized.</summary>
    Other,
}

/// <summary>One texture reference inside a material resource.</summary>
/// <param name="Name">Logical image resource name.</param>
/// <param name="Usage">Role inferred from the resource-name suffix.</param>
public sealed record ElexMaterialTexture(string Name, ElexMaterialTextureUsage Usage);

/// <summary>
/// Parsed <c>eCMaterialResource2</c> payload. The payload is a GAR5-wrapped material instance with a
/// template name and one or more texture tables. Texture entries are length-prefixed name records
/// followed by 28 bytes of resource metadata; the surrounding parameter blocks are not decoded.
/// The runtime loader attaches the database shader name and the uploaded primary texture to this
/// instance; a parsed-only inspection leaves both <see langword="null"/>.
/// </summary>
public sealed class ElexMaterial
{
    private const int OuterWrapperSize = 44;
    private const int TextureEntryMetadataSize = 28;
    private const int MaxTextureNameLength = 512;
    private const int MaxTextureCount = 128;

    private ElexMaterial(string name, string templateName, ElexMaterialTexture[] textures)
    {
        Name = name;
        TemplateName = templateName;
        Textures = textures;
    }

    /// <summary>Logical material name.</summary>
    public string Name { get; }

    /// <summary>Material-instance template name stored in the payload.</summary>
    public string TemplateName { get; }

    /// <summary>Texture references in payload order, de-duplicated by name.</summary>
    public IReadOnlyList<ElexMaterialTexture> Textures { get; }

    /// <summary>
    /// Shader instance name from the material database. Set by the runtime material loader; a
    /// parsed-only inspection leaves it <see langword="null"/>.
    /// </summary>
    public string? Shader { get; set; }

    /// <summary>
    /// Uploaded primary (diffuse) texture. Set by the runtime material loader; a parsed-only
    /// inspection leaves it <see langword="null"/>.
    /// </summary>
    public Texture2D? Texture { get; set; }

    /// <summary>First diffuse texture reference, when present.</summary>
    public ElexMaterialTexture? DiffuseTexture =>
        Textures.FirstOrDefault(texture => texture.Usage == ElexMaterialTextureUsage.Diffuse);

    /// <summary>Normal-map texture reference, when present.</summary>
    public ElexMaterialTexture? NormalTexture =>
        Textures.FirstOrDefault(texture => texture.Usage == ElexMaterialTextureUsage.Normal);

    /// <summary>
    /// Primary texture reference: the diffuse map when present, otherwise the first non-normal
    /// texture, otherwise the first texture.
    /// </summary>
    public ElexMaterialTexture? PrimaryTexture =>
        DiffuseTexture ??
        Textures.FirstOrDefault(texture => texture.Usage != ElexMaterialTextureUsage.Normal) ??
        Textures.FirstOrDefault();

    /// <summary>Parses a material resource wrapped in a GAR5 header.</summary>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="Gar5FormatException">The material payload is malformed.</exception>
    public static ElexMaterial Parse(string name, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(data);

        var offset = FindMaterialOffset(data);
        if (offset + 21 > data.Length)
        {
            throw new Gar5FormatException($"Material '{name}' has a truncated header.");
        }

        if (ReadUInt32(data, offset + 4) != 0x20)
        {
            throw new Gar5FormatException($"Material '{name}' has an unexpected header value.");
        }

        var templateLength = ReadUInt16(data, offset + 13);
        if (templateLength > data.Length - offset - 15)
        {
            throw new Gar5FormatException($"Material '{name}' has an invalid template name length.");
        }

        var template = Encoding.Latin1.GetString(data, offset + 15, templateLength);
        var textures = ReadTextures(data, offset + 15 + templateLength);
        return new ElexMaterial(name, template, textures);
    }

    private static ElexMaterialTexture[] ReadTextures(byte[] data, int start)
    {
        var textures = new List<ElexMaterialTexture>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var position = start; position + 8 <= data.Length; position++)
        {
            var declaredSize = ReadUInt32(data, position);
            var nameLength = ReadUInt32(data, position + 4);
            if (nameLength < 3 ||
                nameLength > MaxTextureNameLength ||
                declaredSize != nameLength + 4 ||
                position + 8 + (int)nameLength + TextureEntryMetadataSize > data.Length)
            {
                continue;
            }

            if (!IsPrintableAscii(data.AsSpan(position + 8, (int)nameLength)))
            {
                continue;
            }

            var textureName = Encoding.Latin1.GetString(data, position + 8, (int)nameLength);
            if (textureName.IndexOf('_') < 0)
            {
                continue;
            }

            if (seen.Add(textureName))
            {
                textures.Add(new ElexMaterialTexture(textureName, Classify(textureName)));
                if (textures.Count >= MaxTextureCount)
                {
                    break;
                }
            }
        }

        return [.. textures];
    }

    private static ElexMaterialTextureUsage Classify(string textureName)
    {
        var underscore = textureName.LastIndexOf('_');
        if (underscore < 0 || underscore + 1 >= textureName.Length)
        {
            return ElexMaterialTextureUsage.Other;
        }

        return textureName[(underscore + 1)..].ToUpperInvariant() switch
        {
            "DF" or "DFA" => ElexMaterialTextureUsage.Diffuse,
            "NM" => ElexMaterialTextureUsage.Normal,
            "SM" => ElexMaterialTextureUsage.Specular,
            "SM2" => ElexMaterialTextureUsage.Specular2,
            "EM" => ElexMaterialTextureUsage.Emissive,
            "CO" => ElexMaterialTextureUsage.Color,
            _ => ElexMaterialTextureUsage.Other,
        };
    }

    private static int FindMaterialOffset(byte[] data)
    {
        if (data.Length >= OuterWrapperSize + 4 &&
            ReadUInt32(data, 0) == Gar5Header.Magic &&
            ReadUInt32(data, OuterWrapperSize) == Gar5Header.Magic)
        {
            return OuterWrapperSize;
        }

        if (data.Length >= 4 && ReadUInt32(data, 0) == Gar5Header.Magic)
        {
            return 0;
        }

        throw new Gar5FormatException("The material does not start with a GAR5 payload.");
    }

    private static bool IsPrintableAscii(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value < 0x20 || value >= 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
}
