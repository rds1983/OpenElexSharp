using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ResourceDatabaseTests
{
	[Fact]
	public void Parse_ValidDatabase_ReadsHeaderAndRecords()
	{
		var data = new TestResourceDatabaseBuilder()
			.AddResource("INFO_FIRST_English", 20)
			.AddResource("INFO_SECOND_WITH_LONGER_NAME_English", 49)
			.Build(0xAABBCCDD, "SN11");

		var database = ResourceDatabase.Parse(data);

		Assert.Equal(0xAABBCCDDu, database.ClassHash);
		Assert.Equal("SN11", database.ClassTag);
		Assert.Equal(2, database.Entries.Count);
		Assert.Equal("INFO_FIRST_English", database.Entries[0].Name);
		Assert.Equal("INFO_SECOND_WITH_LONGER_NAME_English", database.Entries[1].Name);
		Assert.Equal(24, database.Entries[0].Offset);
		Assert.Equal(24 + database.Entries[0].Size, database.Entries[1].Offset);
		Assert.Equal(data.Length, database.Entries[1].Offset + database.Entries[1].Size);
		foreach (var entry in database.Entries)
		{
			Assert.True(entry.ClassBlockSize >= 8);
			Assert.Equal("GEC2", Encoding.ASCII.GetString(data, entry.ClassBlockOffset, 4));
			Assert.Equal(
				entry.ClassBlockOffset + entry.ClassBlockSize,
				entry.Offset + entry.Size);
		}
	}

	[Fact]
	public void Parse_EmptyDatabase_IsValid()
	{
		var data = new TestResourceDatabaseBuilder().Build();

		var database = ResourceDatabase.Parse(data);

		Assert.Empty(database.Entries);
	}

	[Fact]
	public void Parse_WrongMagic_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();
		data[0] = (byte)'X';

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(data));
	}

	[Fact]
	public void Parse_NonRt02Tag_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();
		Encoding.ASCII.GetBytes("XXXX").CopyTo(data, 8);

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(data));
	}

	[Fact]
	public void Parse_TruncatedHeader_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(data[..20]));
	}

	[Fact]
	public void Parse_InvalidNameLength_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();
		BitConverter.GetBytes(0u).CopyTo(data, 24 + 32);

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(data));
	}

	[Fact]
	public void Parse_MissingClassBlock_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();
		var magic = data.AsSpan().IndexOf("GEC2"u8);
		Assert.True(magic >= 0);
		data.AsSpan(magic, 4).Clear();

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(data));
	}

	[Fact]
	public void Parse_InvalidClassBlockSize_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();
		var magic = data.AsSpan().IndexOf("GEC2"u8);
		Assert.True(magic >= 4);
		BitConverter.GetBytes(0u).CopyTo(data, magic - 4);

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(data));
	}

	[Fact]
	public void Parse_TrailingData_Throws()
	{
		var data = new TestResourceDatabaseBuilder().AddResource("A").Build();
		var extended = new byte[data.Length + 1];
		data.CopyTo(extended, 0);

		Assert.Throws<Gar5FormatException>(() => ResourceDatabase.Parse(extended));
	}

	[Fact]
	public void Parse_NullData_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => ResourceDatabase.Parse(null!));
	}
}
