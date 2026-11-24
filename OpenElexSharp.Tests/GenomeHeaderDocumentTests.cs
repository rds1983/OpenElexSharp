using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GenomeHeaderDocumentTests
{
	[Fact]
	public void Parse_ValidDocument_ReadsSectionsAndObjects()
	{
		var firstObject = TestGenomeBuilder.CreateObject(0x955C5FD7, 6, [], []);
		var secondObject = TestGenomeBuilder.CreateObject(0x1BC7FE51, 1, [], []);
		var data = TestGenomeBuilder.CreateHeaderDocument(
		[
			(0x955C5FD7, [("QuestOne", 123UL, firstObject), ("QuestTwo", 456UL, secondObject)]),
		]);

		var document = GenomeHeaderDocument.Parse(data);

		Assert.Equal(2, document.ObjectCount);
		var section = Assert.Single(document.Sections);
		Assert.Equal(0x955C5FD7u, section.ClassHash);
		Assert.Equal("QuestOne", section.Entries[0].Name);
		Assert.Equal(123UL, section.Entries[0].Timestamp);
		Assert.Equal(0x1BC7FE51u, section.Entries[1].Object.ClassHash);
	}

	[Fact]
	public void Parse_MultipleSections_ReadsAll()
	{
		var data = TestGenomeBuilder.CreateHeaderDocument(
		[
			(1u, [("A", 0UL, TestGenomeBuilder.CreateObject(1, 1, [], []))]),
			(2u, [("B", 0UL, TestGenomeBuilder.CreateObject(2, 1, [], []))]),
		]);

		var document = GenomeHeaderDocument.Parse(data);

		Assert.Equal(2, document.Sections.Count);
		Assert.Equal(2, document.ObjectCount);
	}

	[Fact]
	public void Parse_WrongTag_Throws()
	{
		var data = TestGenomeBuilder.CreateHeaderDocument([]);
		data[8] = (byte)'X';

		Assert.Throws<GenomeFormatException>(() => GenomeHeaderDocument.Parse(data));
	}

	[Fact]
	public void Parse_InvalidSectionOffset_Throws()
	{
		var data = TestGenomeBuilder.CreateHeaderDocument(
			[(1u, [("A", 0UL, TestGenomeBuilder.CreateObject(1, 1, [], []))])]);
		BitConverter.GetBytes((uint)data.Length + 100).CopyTo(data, 24);

		Assert.Throws<GenomeFormatException>(() => GenomeHeaderDocument.Parse(data));
	}

	[Fact]
	public void Parse_Truncated_Throws()
	{
		var data = TestGenomeBuilder.CreateHeaderDocument([]);

		Assert.Throws<GenomeFormatException>(() => GenomeHeaderDocument.Parse(data[..16]));
	}

	[Fact]
	public void Parse_NullData_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => GenomeHeaderDocument.Parse(null!));
	}
}
