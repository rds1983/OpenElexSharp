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
	public void Parse_DecodesSparsePositionAndNormalDeltas()
	{
		var data = CreateBlendShapeWithDeltas();

		var set = ElexBlendShapeSet.Parse("Test", data);

		Assert.True(set.DeltasDecoded);
		Assert.Equal(2, set.Targets.Count);
		var first = Assert.Single(set.Targets[0].Deltas);
		Assert.Equal(0, first.VertexIndex);
		Assert.Equal(1000f / 32767f, first.Position.X, 5);
		Assert.Equal(2000f / 32767f, first.Position.Y, 5);
		Assert.Equal(-3000f / 32767f, first.Position.Z, 5);
		Assert.Equal(100f / 32767f, first.Normal.X, 5);

		Assert.Equal(2, set.Targets[1].Deltas.Count);
		Assert.Equal(0, set.Targets[1].Deltas[0].VertexIndex);
		Assert.Equal(short.MaxValue / 32767f, set.Targets[1].Deltas[0].Position.Y, 5);
		Assert.Equal(short.MinValue / 32767f, set.Targets[1].Deltas[0].Normal.X, 5);
		Assert.Equal(2, set.Targets[1].Deltas[1].VertexIndex);
		Assert.Equal(short.MaxValue / 32767f, set.Targets[1].Deltas[1].Normal.Y, 5);
	}

	[Fact]
	public void Parse_InvalidVertexMap_DoesNotDecodeDeltas()
	{
		var data = CreateBlendShapeWithDeltas();
		var mapStart = 44 + 28 + 4;
		mapStart += Encoding.Latin1.GetByteCount("blendShape1.A") + 1 +
			Encoding.Latin1.GetByteCount("blendShape1.B") + 1;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(mapStart), 3u);

		var set = ElexBlendShapeSet.Parse("Test", data);

		Assert.False(set.DeltasDecoded);
		Assert.Empty(set.Targets);
	}

	[Fact]
	public void Parse_TruncatedDeltas_DoesNotDecode()
	{
		var data = CreateBlendShapeWithDeltas();
		var truncated = data[..^4];

		var set = ElexBlendShapeSet.Parse("Test", truncated);

		Assert.False(set.DeltasDecoded);
		Assert.Empty(set.Targets);
	}

	[Fact]
	public void ApplyWeights_BlendsPositionAndNormalDeltas()
	{
		var set = ElexBlendShapeSet.Parse("Test", CreateBlendShapeWithDeltas());
		var positions = new System.Numerics.Vector3[3];
		var normals = new System.Numerics.Vector3[3];

		set.ApplyWeights([1f, 0.5f], positions, normals);

		var expectedX = 1000f / 32767f;
		var expectedY = 2000f / 32767f + 0.5f;
		var expectedZ = -3000f / 32767f;
		Assert.Equal(expectedX, positions[0].X, 5);
		Assert.Equal(expectedY, positions[0].Y, 5);
		Assert.Equal(expectedZ, positions[0].Z, 5);
		Assert.Equal(0.5f * -1000f / 32767f, positions[2].X, 5);
		Assert.Equal(0.5f * 1000f / 32767f, positions[2].Z, 5);
		Assert.Equal(100f / 32767f + 0.5f * short.MinValue / 32767f, normals[0].X, 4);
	}

	[Fact]
	public void ApplyWeights_WrongWeightCount_Throws()
	{
		var set = ElexBlendShapeSet.Parse("Test", CreateBlendShapeWithDeltas());
		var positions = new System.Numerics.Vector3[3];
		var normals = new System.Numerics.Vector3[3];

		Assert.Throws<ArgumentException>(() => set.ApplyWeights([1f], positions, normals));
		Assert.Throws<ArgumentException>(() => set.ApplyWeights([1f, 0f], positions[..2], normals));
	}

	[Fact]
	public void ApplyWeights_UndecodedDeltas_Throws()
	{
		var set = ElexBlendShapeSet.Parse("Test", CreateBlendShape(["Aaa"], 1));

		Assert.Throws<InvalidOperationException>(
			() => set.ApplyWeights([0f], new System.Numerics.Vector3[1], new System.Numerics.Vector3[1]));
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

	internal static byte[] CreateBlendShapeWithDeltas()
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		var names = new[] { "blendShape1.A", "blendShape1.B" };
		writer.Write(Gar5Header.Magic);
		writer.Write(0x20u);
		writer.Write(new byte[36]);
		writer.Write(2u); // targets
		writer.Write(3u); // vertices
		writer.Write(2u); // affected vertices
		writer.Write(3u); // deltas
		writer.Write(1000u); // position scale (1.0)
		writer.Write(2u);
		writer.Write(0u);
		writer.Write((uint)(names[0].Length + 1));
		foreach (var name in names)
		{
			writer.Write(Encoding.Latin1.GetBytes(name));
			writer.Write((byte)0);
		}

		// Base vertex -> compact index (0 = unaffected).
		writer.Write(1u);
		writer.Write(0u);
		writer.Write(2u);
		// Descriptor: (startDelta, targetMask)
		writer.Write(0u);
		writer.Write(3u); // targets 0 and 1
		writer.Write(0u);
		writer.Write(0u);
		writer.Write(2u);
		writer.Write(2u); // target 1
		writer.Write(0u);
		writer.Write(0u);
		// Delta records: position xyz + normal xyz.
		writer.Write((short)1000);
		writer.Write((short)2000);
		writer.Write((short)-3000);
		writer.Write((short)100);
		writer.Write((short)200);
		writer.Write((short)300);
		writer.Write((short)0);
		writer.Write(short.MaxValue);
		writer.Write((short)0);
		writer.Write(short.MinValue);
		writer.Write((short)0);
		writer.Write((short)0);
		writer.Write((short)-1000);
		writer.Write((short)0);
		writer.Write((short)1000);
		writer.Write((short)0);
		writer.Write(short.MaxValue);
		writer.Write((short)0);
		return output.ToArray();
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
