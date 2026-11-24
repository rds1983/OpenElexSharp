using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionGraphNodeTests
{
	[Fact]
	public void FindAll_ReadsCandidateBlockAndReferences()
	{
		var data = new byte[24 + 2 * 40];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x53);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 24);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 7);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24 + 40), uint.MaxValue);

		var nodes = MotionGraphNode.FindAll(data, 0, data.Length);

		var node = Assert.Single(nodes);
		Assert.Equal(0, node.Offset);
		Assert.Equal(1, node.Type);
		Assert.Equal(104, node.Size);
		Assert.Equal(2, node.Count);
		Assert.Equal(24, node.HeaderSize);
		Assert.Equal([7u], node.References);
		Assert.Equal(2, node.Records.Count);
		Assert.Equal(7ul, node.Records[0][0]);
	}

	[Fact]
	public void FindAll_IgnoresInvalidBlock()
	{
		var data = new byte[64];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 100);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 24);

		Assert.Empty(MotionGraphNode.FindAll(data, 0, data.Length));
	}

	[Fact]
	public void FindAll_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MotionGraphNode.FindAll(null!, 0, 0));
	}
}
