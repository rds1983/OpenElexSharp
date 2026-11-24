using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// Material resource metadata from the <c>eCMaterialResource2</c> class block stored in a
/// <c>w_mat</c> database. The class block records the shader used by a material instance; the
/// texture table lives in the material payload itself.
/// </summary>
public sealed class MaterialMetadata
{
	private static readonly uint InstanceMaterialHash =
		Djb2Hash.ComputeCaseSensitive("InstanceMaterial");

	private MaterialMetadata(string? instanceMaterial)
	{
		InstanceMaterial = instanceMaterial;
	}

	/// <summary>Shader instance name, for example <c>st_DfANmSmSm2.shader</c>.</summary>
	public string? InstanceMaterial { get; }

	/// <summary>Parses an <c>eCMaterialResource2</c> class block.</summary>
	/// <exception cref="ArgumentNullException">The buffer is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The class block is malformed.</exception>
	public static MaterialMetadata Parse(byte[] classBlock)
	{
		ArgumentNullException.ThrowIfNull(classBlock);
		var genomeObject = GenomeObject.Parse(classBlock, 0);
		foreach (var property in genomeObject.Properties)
		{
			if (property.NameHash != InstanceMaterialHash)
			{
				continue;
			}

			return new MaterialMetadata(ReadString(property));
		}

		return new MaterialMetadata(null);
	}

	private static string ReadString(GenomeProperty property)
	{
		if (property.Data.Length < 2)
		{
			throw new GenomeFormatException("The InstanceMaterial string is truncated.");
		}

		var length = BinaryPrimitives.ReadUInt16LittleEndian(property.Data);
		if (length > property.Data.Length - 2)
		{
			throw new GenomeFormatException("The InstanceMaterial string has an invalid length.");
		}

		return Encoding.Latin1.GetString(property.Data, 2, length);
	}
}
