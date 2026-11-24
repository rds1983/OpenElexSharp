using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexStringTableTests
{
	[Fact]
	public void Parse_DecodesColumnsAndHashes()
	{
		var data = BuildTable(
			["Hello", "World"],
			[0x11111111, 0x22222222]);

		var table = ElexStringTable.Parse("en.str", "test.rom", data);

		Assert.Equal(7, table.Version);
		Assert.Single(table.Sources);
		Assert.Equal("#G3:/Data/Raw/Strings/test.csv", table.Sources[0].CsvPath);
		var column = Assert.Single(table.Columns);
		Assert.Equal("English_Text", column.Name);
		Assert.Equal(2, table.RowCount);
		Assert.True(table.TryGet(0x11111111, "English_Text", out var hello));
		Assert.Equal("Hello", hello);
		Assert.True(table.TryGet(0x22222222, "English_Text", out var world));
		Assert.Equal("World", world);
	}

	[Fact]
	public void Find_SearchesDecodedText()
	{
		var table = ElexStringTable.Parse(
			"en.str",
			"test.rom",
			BuildTable(["Hello", "World"], [0x11111111, 0x22222222]));

		var match = Assert.Single(table.Find("orl"));

		Assert.Equal(0x22222222u, match.IdHash);
		Assert.Equal("English_Text", match.Column);
		Assert.Equal("World", match.Text);
	}

	[Fact]
	public void TryGet_ComputesPrefixedIdHash()
	{
		var table = ElexStringTable.Parse(
			"en.str",
			"test.rom",
			BuildTable(["Hello"], [Djb2Hash.Compute("item:Hello")]));

		Assert.True(table.TryGet("Hello", "item", null, out var value));
		Assert.Equal("Hello", value);
	}

	[Fact]
	public void Parse_RejectsBadSignature()
	{
		var data = BuildTable(["Hello"], [0x11111111]);
		data[8] = (byte)'X';

		Assert.Throws<GenomeFormatException>(() =>
			ElexStringTable.Parse("en.str", "test.rom", data));
	}

	[Fact]
	public void Parse_RejectsTruncatedData()
	{
		var data = BuildTable(["Hello"], [0x11111111]);
		data = data[..40];

		Assert.Throws<GenomeFormatException>(() =>
			ElexStringTable.Parse("en.str", "test.rom", data));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => ElexStringTable.Parse("", "test.rom", []));
		Assert.Throws<ArgumentException>(() => ElexStringTable.Parse("en.str", "", []));
		Assert.Throws<ArgumentNullException>(() => ElexStringTable.Parse("en.str", "test.rom", null!));
	}

	private static byte[] BuildTable(string[] strings, uint[] hashes)
	{
		var sourcePath = Encoding.Latin1.GetBytes("#G3:/Data/Raw/Strings/test.csv");
		var columnName = Encoding.Latin1.GetBytes("English_Text");
		var sourceSize = 2 + sourcePath.Length + 8;
		var sourceTable = 44;
		var columnNames = Align4(sourceTable + sourceSize);
		var columnNameRef = columnNames;
		var columnNameData = Align4(columnNameRef + 8);
		var keyTable = Align4(columnNameData + 16);
		var keyRef = keyTable;
		var keyData = Align4(keyRef + 8);
		var columnTable = Align4(keyData + hashes.Length * 4);
		var stringTable = Align4(columnTable + 16);

		var symbols = new List<uint> { 0 };
		var symbolIndex = new Dictionary<char, ushort>();
		var sequence = new List<ushort>();
		var beginnings = new uint[strings.Length];
		for (var row = 0; row < strings.Length; row++)
		{
			beginnings[row] = (uint)sequence.Count;
			foreach (var character in strings[row])
			{
				if (!symbolIndex.TryGetValue(character, out var index))
				{
					index = (ushort)symbols.Count;
					symbolIndex[character] = index;
					symbols.Add((uint)character << 16);
				}

				sequence.Add(index);
			}

			sequence.Add(0);
		}

		var stringSize = hashes.Length * 4 + sequence.Count * 2;
		var stringPosition = stringTable;
		var symbolTable = Align4(stringPosition + stringSize);
		var symbolSize = symbols.Count * 4;
		var data = new byte[Align4(symbolTable + symbolSize)];

		"GAR5"u8.CopyTo(data);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x20);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x07425453);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 1);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), (uint)hashes.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), (uint)sourceTable);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), (uint)columnNames);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(36), (uint)columnTable);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(40), (uint)keyTable);

		var source = sourceTable;
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(source), (ushort)sourcePath.Length);
		sourcePath.CopyTo(data, source + 2);
		source += 2 + sourcePath.Length;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(source), 0);

		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(columnNameRef), 16);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(columnNameRef + 4), (uint)columnNameData);
		columnName.CopyTo(data, columnNameData);

		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(keyRef), (uint)(hashes.Length * 4));
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(keyRef + 4), (uint)keyData);
		for (var index = 0; index < hashes.Length; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(keyData + index * 4), hashes[index]);
		}

		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(columnTable), (uint)stringSize);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(columnTable + 4), (uint)stringPosition);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(columnTable + 8), (uint)symbolSize);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(columnTable + 12), (uint)symbolTable);
		for (var index = 0; index < beginnings.Length; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(
				data.AsSpan(stringPosition + index * 4), beginnings[index]);
		}

		var sequenceStart = stringPosition + hashes.Length * 4;
		for (var index = 0; index < sequence.Count; index++)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(
				data.AsSpan(sequenceStart + index * 2), sequence[index]);
		}

		for (var index = 0; index < symbols.Count; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(
				data.AsSpan(symbolTable + index * 4), symbols[index]);
		}

		return data;
	}

	private static int Align4(int value) => (value + 3) & ~3;
}
