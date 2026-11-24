using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class GenomePropertyDecoderTests
{
	[Fact]
	public void Decode_Bool_ReturnsBoolean()
	{
		var value = Decode("bool", [1, 0, 0, 0]);

		Assert.True(Assert.IsType<bool>(value.Value));
	}

	[Fact]
	public void Decode_Int_ReturnsInteger()
	{
		var value = Decode("int", BitConverter.GetBytes(42));

		Assert.Equal(42, Assert.IsType<int>(value.Value));
	}

	[Fact]
	public void Decode_String_ReturnsText()
	{
		var data = new byte[] { 5, 0, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };

		var value = Decode("class bCString", data);

		Assert.Equal("hello", Assert.IsType<string>(value.Value));
	}

	[Fact]
	public void Decode_Guid_ReturnsGuid()
	{
		var guid = Guid.NewGuid();

		var value = Decode("class bCGuid", guid.ToByteArray());

		Assert.Equal(guid, Assert.IsType<Guid>(value.Value));
	}

	[Fact]
	public void Decode_Vector_ReturnsFloats()
	{
		var data = new byte[12];
		BinaryPrimitives.WriteInt32LittleEndian(data, BitConverter.SingleToInt32Bits(1.5f));
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), BitConverter.SingleToInt32Bits(2.5f));
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), BitConverter.SingleToInt32Bits(3.5f));

		var value = Decode("class bCVector", data);

		Assert.Equal([1.5f, 2.5f, 3.5f], Assert.IsType<float[]>(value.Value));
	}

	[Fact]
	public void Decode_Enum_ReturnsMemberName()
	{
		var memberHash = Djb2Hash.ComputeCaseSensitive("gEInfoType_Refuse");
		var data = new byte[8];
		BinaryPrimitives.WriteUInt32LittleEndian(data, memberHash);
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0);

		var value = Decode("enum gEInfoType", data);

		var enumValue = Assert.IsType<GenomeEnumValue>(value.Value);
		Assert.Equal(0, enumValue.Value);
		Assert.Equal("gEInfoType_Refuse", enumValue.MemberName);
	}

	[Fact]
	public void Decode_Array_DecodesElements()
	{
		var data = new byte[16];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 3);
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 10);
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 20);
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 30);
		var typeHash = Djb2Hash.ComputeCaseSensitive("class bTObjArray<int>");

		var value = GenomePropertyDecoder.Decode(
			new GenomeProperty(typeHash, 0, data),
			hash => hash == typeHash ? "class bTObjArray<int>" : null);

		var items = Assert.IsType<GenomeValue[]>(value.Value);
		Assert.Equal([10, 20, 30], items.Select(item => Assert.IsType<int>(item.Value)));
	}

	[Fact]
	public void Decode_UnknownType_ReturnsRaw()
	{
		var value = Decode("class UnknownType", [1, 2, 3]);

		Assert.Null(value.Value);
		Assert.Equal(3, value.RawData.Length);
		Assert.Equal("<raw 3 byte(s)>", GenomePropertyDecoder.Format(value));
	}

	[Fact]
	public void Decode_MalformedPrimitive_Throws()
	{
		Assert.Throws<GenomeFormatException>(() => Decode("int", [1, 2]));
	}

	[Fact]
	public void Decode_NullProperty_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => GenomePropertyDecoder.Decode(null!));
	}

	private static GenomeValue Decode(string typeName, byte[] data)
	{
		var typeHash = Djb2Hash.ComputeCaseSensitive(typeName);
		return GenomePropertyDecoder.Decode(
			new GenomeProperty(typeHash, 0, data),
			hash => hash == typeHash
				? typeName
				: GenomeNameTable.TryGet(hash, out var name) ? name : null);
	}
}
