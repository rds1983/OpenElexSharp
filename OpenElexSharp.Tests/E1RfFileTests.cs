using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class E1RfFileTests
{
	[Fact]
	public void Parse_ValidFile_ReadsRomAndDatabaseEntry()
	{
		var rom = Encoding.ASCII.GetBytes("GAR5 rom data");
		var entry = new byte[] { 1, 2, 3, 4, 5 };
		var data = CreateE1Rf(rom, 0xAABBCCDD, "SN11", entry);

		var file = E1RfFile.Parse(data);

		Assert.Equal(rom, file.RomData);
		Assert.Equal(0xAABBCCDDu, file.ClassHash);
		Assert.Equal("SN11", file.ClassTag);
		Assert.Equal(entry, file.DatabaseEntry);
	}

	[Fact]
	public void Parse_WrongMagic_Throws()
	{
		var data = CreateE1Rf([1, 2, 3], 0, "SN11", []);
		data[0] = (byte)'X';

		Assert.Throws<Gar5FormatException>(() => E1RfFile.Parse(data));
	}

	[Fact]
	public void Parse_InvalidRomSize_Throws()
	{
		var data = CreateE1Rf([1, 2, 3], 0, "SN11", []);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1000);

		Assert.Throws<Gar5FormatException>(() => E1RfFile.Parse(data));
	}

	[Fact]
	public void Parse_InvalidEntrySize_Throws()
	{
		var data = CreateE1Rf([1, 2, 3], 0, "SN11", [1, 2]);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 6), 1);

		Assert.Throws<Gar5FormatException>(() => E1RfFile.Parse(data));
	}

	[Fact]
	public void Parse_Truncated_Throws()
	{
		Assert.Throws<Gar5FormatException>(() => E1RfFile.Parse([1, 2, 3]));
	}

	[Fact]
	public void Parse_NullData_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => E1RfFile.Parse(null!));
	}

	private static byte[] CreateE1Rf(byte[] rom, uint classHash, string classTag, byte[] entry)
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
		writer.Write(Encoding.ASCII.GetBytes("E1RF"));
		writer.Write((uint)rom.Length);
		writer.Write(rom);
		writer.Write(classHash);
		writer.Write(Encoding.ASCII.GetBytes(classTag));
		writer.Write((uint)entry.Length);
		writer.Write(entry);
		return stream.ToArray();
	}
}
