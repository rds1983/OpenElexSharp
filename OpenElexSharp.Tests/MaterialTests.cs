using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MaterialTests
{
	[Fact]
	public void ElexMaterial_Parse_ReadsTexturesAndRoles()
	{
		var data = CreateMaterial("Mat_DfA", "Mat_Sm", "Mat_Nm", "Mat_Sm2");

		var material = ElexMaterial.Parse("Mat", data);

		Assert.Equal("Mat", material.Name);
		Assert.Equal("default", material.TemplateName);
		Assert.Equal(4, material.Textures.Count);
		Assert.Equal(ElexMaterialTextureUsage.Diffuse, material.Textures[0].Usage);
		Assert.Equal(ElexMaterialTextureUsage.Specular, material.Textures[1].Usage);
		Assert.Equal(ElexMaterialTextureUsage.Normal, material.Textures[2].Usage);
		Assert.Equal(ElexMaterialTextureUsage.Specular2, material.Textures[3].Usage);
		Assert.Equal("Mat_DfA", material.DiffuseTexture?.Name);
		Assert.Equal("Mat_Nm", material.NormalTexture?.Name);
		Assert.Equal("Mat_DfA", material.PrimaryTexture?.Name);
		Assert.Null(material.Shader);
		Assert.Null(material.Texture);
	}

	[Fact]
	public void ElexMaterial_Parse_WithoutTextures_ReturnsEmpty()
	{
		var material = ElexMaterial.Parse("Mat", CreateMaterial());

		Assert.Empty(material.Textures);
		Assert.Null(material.DiffuseTexture);
		Assert.Null(material.PrimaryTexture);
	}

	[Fact]
	public void ElexMaterial_Parse_WithoutDiffuse_UsesFirstOtherTexture()
	{
		var material = ElexMaterial.Parse("Mat", CreateMaterial("Mat_FxG", "Mat_Nm"));

		Assert.Null(material.DiffuseTexture);
		Assert.Equal("Mat_FxG", material.PrimaryTexture?.Name);
		Assert.Equal(ElexMaterialTextureUsage.Other, material.PrimaryTexture?.Usage);
	}

	[Fact]
	public void ElexMaterial_Parse_DuplicateTextures_AreDeduplicated()
	{
		var material = ElexMaterial.Parse("Mat", CreateMaterial("Mat_Df", "Mat_Df", "Mat_Nm"));

		Assert.Equal(2, material.Textures.Count);
	}

	[Fact]
	public void ElexMaterial_Parse_InvalidMagic_Throws()
	{
		var data = CreateMaterial("Mat_Df");
		data[0] = (byte)'X';

		Assert.Throws<Gar5FormatException>(() => ElexMaterial.Parse("Mat", data));
	}

	[Fact]
	public void ElexMaterial_Parse_TruncatedHeader_Throws()
	{
		var data = CreateMaterial("Mat_Df");

		Assert.Throws<Gar5FormatException>(() => ElexMaterial.Parse("Mat", data[..50]));
	}

	[Fact]
	public void ElexMaterial_Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => ElexMaterial.Parse("Mat", null!));
		Assert.Throws<ArgumentNullException>(() => ElexMaterial.Parse(null!, []));
	}

	[Fact]
	public void MaterialMetadata_Parse_ReadsInstanceMaterial()
	{
		var block = TestGenomePropertyBuilder.Object(
			Djb2Hash.ComputeCaseSensitive("class eCMaterialResource2"),
			2,
			("InstanceMaterial", TestGenomePropertyBuilder.String("st_DfANm.shader")));

		var metadata = MaterialMetadata.Parse(block);

		Assert.Equal("st_DfANm.shader", metadata.InstanceMaterial);
	}

	[Fact]
	public void MaterialMetadata_Parse_WithoutInstanceMaterial_ReturnsNull()
	{
		var block = TestGenomePropertyBuilder.Object(
			Djb2Hash.ComputeCaseSensitive("class eCMaterialResource2"),
			2);

		var metadata = MaterialMetadata.Parse(block);

		Assert.Null(metadata.InstanceMaterial);
	}

	private static byte[] CreateMaterial(params string[] textureNames)
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		writer.Write(Encoding.ASCII.GetBytes("GAR5"));
		writer.Write(0x20u);
		writer.Write(new byte[36]);
		writer.Write(Encoding.ASCII.GetBytes("GAR5"));
		writer.Write(0x20u);
		writer.Write(1u);
		writer.Write((byte)0);
		writer.Write((ushort)7);
		writer.Write(Encoding.ASCII.GetBytes("default"));
		writer.Write(new byte[40]);
		foreach (var name in textureNames)
		{
			var bytes = Encoding.ASCII.GetBytes(name);
			writer.Write((uint)(bytes.Length + 4));
			writer.Write((uint)bytes.Length);
			writer.Write(bytes);
			writer.Write(1u);
			writer.Write(0u);
			writer.Write(new byte[16]);
			writer.Write(0u);
		}

		return output.ToArray();
	}
}
