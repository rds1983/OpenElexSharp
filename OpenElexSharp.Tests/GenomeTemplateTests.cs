using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GenomeTemplateTests
{
	[Fact]
	public void Parse_ValidTemplate_ReadsIdentityAndChildren()
	{
		var childProperty = TestTemplateBuilder.CreatePropertySet(0x11111111, 1, 0, "mesh");
		var child = TestTemplateBuilder.CreateTemplateObject(
			"Child.entity",
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Guid.Empty,
			[childProperty],
			[]);
		var ownProperty = TestTemplateBuilder.CreatePropertySet(0x22222222, 2, 0, "item");
		var data = TestTemplateBuilder.CreateTemplate(
			"Parent.entity",
			Guid.Parse("22222222-2222-2222-2222-222222222222"),
			Guid.Empty,
			[ownProperty],
			[child]);

		var template = GenomeTemplate.Parse("Parent", "data/test.tpl", data);

		Assert.Equal("Parent", template.Name);
		Assert.Equal("Parent.entity", template.EntityName);
		Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), template.Guid);
		Assert.Equal(Guid.Empty, template.ParentGuid);
		Assert.Single(template.PropertySets);
		Assert.Single(template.Children);
		Assert.Equal("Child.entity", template.Children[0].EntityName);
		Assert.Equal(2, template.GetAllPropertySets().Count);
		Assert.Single(template.GetEffectivePropertySets());
	}

	[Fact]
	public void GetAllPropertySets_DeduplicatesByClassAndVersion()
	{
		var own = TestTemplateBuilder.CreatePropertySet(0x11111111, 1, 0, "parent");
		var child = TestTemplateBuilder.CreateTemplateObject(
			"Child.entity",
			Guid.NewGuid(),
			Guid.Empty,
			[TestTemplateBuilder.CreatePropertySet(0x11111111, 1, 0, "child")],
			[]);
		var data = TestTemplateBuilder.CreateTemplate("Parent", Guid.NewGuid(), Guid.Empty, [own], [child]);

		var template = GenomeTemplate.Parse("Parent", "data/test.tpl", data);
		var propertySets = template.GetAllPropertySets();

		Assert.Single(propertySets);
		Assert.Equal("child", System.Text.Encoding.Latin1.GetString(propertySets[0].Properties[0].Data, 2, 5));
	}

	[Fact]
	public void Parse_WrongWrapper_Throws()
	{
		var data = TestTemplateBuilder.CreateTemplate("Parent", Guid.NewGuid(), Guid.Empty, [], []);
		data[52] = (byte)'X';

		Assert.Throws<GenomeFormatException>(() => GenomeTemplate.Parse("Parent", "data/test.tpl", data));
	}

	[Fact]
	public void Parse_Truncated_Throws()
	{
		Assert.Throws<GenomeFormatException>(() => GenomeTemplate.Parse("Parent", "data/test.tpl", [1, 2, 3]));
	}

	[Fact]
	public void Parse_MissingEntityClassData_Throws()
	{
		var templateEntity = new byte[54];
		var objectBytes = TestGenomeBuilder.CreateObject(
			GenomeTemplate.TemplateEntityClassHash,
			6,
			[],
			[(GenomeTemplate.TemplateEntityClassHash, 6, templateEntity)]);
		using var inner = new MemoryStream();
		using var writer = new BinaryWriter(inner);
		writer.Write(System.Text.Encoding.ASCII.GetBytes("GAR5"));
		writer.Write(0x20u);
		writer.Write(System.Text.Encoding.ASCII.GetBytes("GTP0"));
		writer.Write(0UL);
		writer.Write(objectBytes);
		using var outer = new MemoryStream();
		using var outerWriter = new BinaryWriter(outer);
		outerWriter.Write(System.Text.Encoding.ASCII.GetBytes("GAR5"));
		outerWriter.Write(0x20u);
		outerWriter.Write(new byte[36]);
		outerWriter.Write(inner.ToArray());

		Assert.Throws<GenomeFormatException>(
			() => GenomeTemplate.Parse("Parent", "data/test.tpl", outer.ToArray()));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentNullException>(() => GenomeTemplate.Parse("Parent", "data/test.tpl", null!));
		Assert.Throws<ArgumentNullException>(() => GenomeTemplate.Parse("Parent", null!, []));
	}
}
