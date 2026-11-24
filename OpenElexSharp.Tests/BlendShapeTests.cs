using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class BlendShapeTests
{
	[Fact]
	public void Parse_ReadsHeaderOffsetsAndNames()
	{
		var data = CreateBlendShape(["Aaa", "Bbb", "Ccc"], vertexCount: 123);

		var set = ElexBlendShapeSet.Parse("Test", data);

		Assert.Equal(3, set.TargetCount);
		Assert.Equal(123, set.VertexCount);
		Assert.Equal(3, set.Names.Count);
		Assert.Equal(["Aaa", "Bbb", "Ccc"], set.Names);
		Assert.Equal([4u, 8u], set.NameOffsets);
		Assert.Equal(7, set.Field0);
		Assert.Equal(135, set.Field1);
		Assert.Equal(2, set.Field2);
	}

	[Fact]
	public void Parse_SingleTarget_HasNoOffsets()
	{
		var data = CreateBlendShape(["Solo"], vertexCount: 10);

		var set = ElexBlendShapeSet.Parse("Test", data);

		Assert.Empty(set.NameOffsets);
		Assert.Equal(["Solo"], set.Names);
	}

	[Fact]
	public void Parse_PrintablePrefixBeforeNames_StillFindsTable()
	{
		var data = CreateBlendShape(["Aaa", "Bbb"], vertexCount: 10);
		var namesStart = 44 + 28 + 4;
		var shifted = data.Take(namesStart).Append((byte)'0').Concat(data.Skip(namesStart)).ToArray();

		var set = ElexBlendShapeSet.Parse("Test", shifted);

		Assert.Equal(["Aaa", "Bbb"], set.Names);
	}

	[Fact]
	public void Parse_NonMonotonicOffsets_Throws()
	{
		var data = CreateBlendShape(["Aaa", "Bbb", "Ccc"], vertexCount: 1);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44 + 28), 999u);

		Assert.Throws<Gar5FormatException>(() => ElexBlendShapeSet.Parse("Test", data));
	}

	[Fact]
	public void Parse_NoGar5_Throws()
	{
		var data = CreateBlendShape(["Aaa"], 1);
		data[0] = (byte)'X';

		Assert.Throws<Gar5FormatException>(() => ElexBlendShapeSet.Parse("Test", data));
	}

	[Fact]
	public void Parse_Truncated_Throws()
	{
		Assert.Throws<Gar5FormatException>(() => ElexBlendShapeSet.Parse("Test", new byte[10]));
	}

	[Fact]
	public void Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => ElexBlendShapeSet.Parse("Test", null!));
		Assert.Throws<ArgumentNullException>(() => ElexBlendShapeSet.Parse(null!, []));
	}

	internal static byte[] CreateBlendShape(string[] names, int vertexCount)
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		writer.Write(Gar5Header.Magic);
		writer.Write(0x20u);
		writer.Write(new byte[36]);
		writer.Write((uint)names.Length);
		writer.Write((uint)vertexCount);
		writer.Write(7u);
		writer.Write(135u);
		writer.Write(2u);
		writer.Write((uint)names.Length);
		writer.Write(0u);
		var offset = names[0].Length + 1;
		for (var index = 1; index < names.Length; index++)
		{
			writer.Write((uint)offset);
			offset += names[index].Length + 1;
		}

		foreach (var name in names)
		{
			writer.Write(Encoding.Latin1.GetBytes(name));
			writer.Write((byte)0);
		}

		writer.Write(0u);
		return output.ToArray();
	}
}
