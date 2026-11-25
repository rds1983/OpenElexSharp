using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenElexSharp.Tests;

/// <summary>Builds small synthetic GMSH v2 meshes for tests.</summary>
internal static class TestGmshBuilder
{
	private const int GmshHeaderSize = 8 + 14 * 4;
	private const int BufferDescriptorSize = 40;
	private const uint BufferHash = 0x81d1b3f7;

	/// <summary>
	/// Builds a GMSH v2 resource with a packed GPU index buffer, contiguous vertex streams and the
	/// redundant tooling index/position block.
	/// </summary>
	public static byte[] Create(
		Vector3[] positions,
		int[] indices,
		Vector3[]? normals = null,
		Vector2[]? textureCoordinates = null,
		Vector2[]? textureCoordinates1 = null,
		Vector3[]? tangents = null,
		Vector4[]? colors = null,
		byte[]? boneIndices = null,
		Vector4[]? boneWeights = null,
		bool halfPrecisionPositions = false,
		bool use32BitIndices = false,
		bool wrap = true)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(indices);
		ValidateLength(normals, positions.Length, nameof(normals));
		ValidateLength(textureCoordinates, positions.Length, nameof(textureCoordinates));
		ValidateLength(textureCoordinates1, positions.Length, nameof(textureCoordinates1));
		ValidateLength(tangents, positions.Length, nameof(tangents));
		ValidateLength(colors, positions.Length, nameof(colors));
		ValidateLength(boneWeights, positions.Length, nameof(boneWeights));
		if (boneIndices is not null && boneIndices.Length != positions.Length * 4)
		{
			throw new ArgumentException("boneIndices must contain four entries per vertex.", nameof(boneIndices));
		}

		var attributes = new List<AttributeSpec>();
		var streams = new List<StreamSpec>();

		var positionFormat = halfPrecisionPositions
			? GmshVertexFormats.R16G16B16A16Float
			: GmshVertexFormats.R32G32B32Float;
		attributes.Add(new AttributeSpec(0, 0, positionFormat, 0));
		streams.Add(new StreamSpec(0, halfPrecisionPositions ? 8 : 12));

		if (normals is not null)
		{
			attributes.Add(new AttributeSpec(1, 0, GmshVertexFormats.R10G10B10A2UNorm, 1));
			streams.Add(new StreamSpec(1, 4));
		}

		if (tangents is not null)
		{
			attributes.Add(new AttributeSpec(2, 0, GmshVertexFormats.R10G10B10A2UNorm, 3));
			streams.Add(new StreamSpec(2, 4));
		}

		if (colors is not null)
		{
			attributes.Add(new AttributeSpec(3, 0, GmshVertexFormats.B8G8R8A8UNorm, 5));
			streams.Add(new StreamSpec(3, 4));
		}

		if (boneIndices is not null || boneWeights is not null)
		{
			if (boneIndices is null || boneWeights is null)
			{
				throw new ArgumentException("Bone indices and weights must be supplied together.");
			}

			attributes.Add(new AttributeSpec(5, 0, GmshVertexFormats.R8G8B8A8UInt, 6));
			attributes.Add(new AttributeSpec(5, 4, GmshVertexFormats.R16G16B16A16Float, 6));
			streams.Add(new StreamSpec(5, 12));
		}

		if (textureCoordinates is not null)
		{
			var uvStride = textureCoordinates1 is null ? 4 : 8;
			attributes.Add(new AttributeSpec(4, 0, GmshVertexFormats.R16G16Float, 2));
			if (textureCoordinates1 is not null)
			{
				attributes.Add(new AttributeSpec(4, 4, GmshVertexFormats.R16G16Float, 2));
			}

			streams.Add(new StreamSpec(4, uvStride));
		}

		var indexElementSize = use32BitIndices ? 4 : 2;
		var indexByteCount = indices.Length * indexElementSize;
		var vertexDataOffset = Align16(GmshHeaderSize + indexByteCount);
		var streamTable = new List<StreamSpec>(streams.Count);
		var streamOffset = 0;
		foreach (var stream in streams)
		{
			streamTable.Add(stream with { Offset = streamOffset });
			streamOffset += stream.Stride * positions.Length;
		}

		var vertexByteCount = streamOffset;
		var descriptorOffset = vertexDataOffset + vertexByteCount;
		var meshHeaderOffset = descriptorOffset + 2 * BufferDescriptorSize;
		var attributeOffset = meshHeaderOffset + 28;
		var submeshOffset = attributeOffset + attributes.Count * 28;
		var streamTableOffset = submeshOffset + 16;
		var sourceIndexOffset = streamTableOffset + streamTable.Count * 16;

		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		writer.Write(Encoding.ASCII.GetBytes("GMSH"));
		writer.Write(2u);
		var headerPosition = output.Position;
		writer.Write(new byte[14 * 4]);

		Seek(output, GmshHeaderSize);
		foreach (var index in indices)
		{
			if (use32BitIndices)
			{
				writer.Write((uint)index);
			}
			else
			{
				writer.Write((ushort)index);
			}
		}

		foreach (var stream in streamTable)
		{
			Seek(output, vertexDataOffset + stream.Offset);
			for (var vertex = 0; vertex < positions.Length; vertex++)
			{
				switch (stream.Usage)
				{
					case 0:
						if (halfPrecisionPositions)
						{
							writer.Write(ToHalf(positions[vertex].X));
							writer.Write(ToHalf(positions[vertex].Y));
							writer.Write(ToHalf(positions[vertex].Z));
							writer.Write((ushort)0);
						}
						else
						{
							writer.Write(positions[vertex].X);
							writer.Write(positions[vertex].Y);
							writer.Write(positions[vertex].Z);
						}

						break;
					case 1:
						writer.Write(EncodePackedVector(normals![vertex]));
						break;
					case 2:
						writer.Write(EncodePackedVector(tangents![vertex]));
						break;
					case 3:
						writer.Write((byte)Math.Clamp(MathF.Round(colors![vertex].Z * 255f), 0f, 255f));
						writer.Write((byte)Math.Clamp(MathF.Round(colors[vertex].Y * 255f), 0f, 255f));
						writer.Write((byte)Math.Clamp(MathF.Round(colors[vertex].X * 255f), 0f, 255f));
						writer.Write((byte)Math.Clamp(MathF.Round(colors[vertex].W * 255f), 0f, 255f));
						break;
					case 4:
						writer.Write(ToHalf(textureCoordinates![vertex].X));
						writer.Write(ToHalf(textureCoordinates[vertex].Y));
						if (textureCoordinates1 is not null)
						{
							writer.Write(ToHalf(textureCoordinates1[vertex].X));
							writer.Write(ToHalf(textureCoordinates1[vertex].Y));
						}

						break;
					case 5:
						for (var influence = 0; influence < 4; influence++)
						{
							writer.Write(boneIndices![vertex * 4 + influence]);
						}

						writer.Write(ToHalf(boneWeights![vertex].X));
						writer.Write(ToHalf(boneWeights[vertex].Y));
						writer.Write(ToHalf(boneWeights[vertex].Z));
						writer.Write(ToHalf(boneWeights[vertex].W));
						break;
				}
			}
		}

		Seek(output, descriptorOffset);
		WriteDescriptor(writer, indexByteCount, indexElementSize);
		WriteDescriptor(writer, vertexByteCount, 0);

		Seek(output, meshHeaderOffset);
		writer.Write(3u);
		writer.Write(0u);
		writer.Write(1u);
		writer.Write(0u);
		writer.Write((uint)indices.Length);
		writer.Write(0u);
		writer.Write(0u);

		Seek(output, attributeOffset);
		foreach (var attribute in attributes)
		{
			writer.Write((uint)attribute.Usage);
			writer.Write((uint)attribute.UsageOffset);
			writer.Write(attribute.Format);
			writer.Write((uint)attribute.Slot);
			writer.Write(0u);
			writer.Write(0u);
			writer.Write(0u);
		}

		Seek(output, submeshOffset);
		writer.Write(new byte[16]);

		Seek(output, streamTableOffset);
		foreach (var stream in streamTable)
		{
			writer.Write((uint)stream.Usage);
			writer.Write(1u);
			writer.Write((uint)stream.Offset);
			writer.Write((uint)stream.Stride);
		}

		Seek(output, sourceIndexOffset);
		writer.Write((uint)indices.Length);
		foreach (var index in indices)
		{
			writer.Write((uint)index);
		}

		writer.Write((uint)positions.Length);
		foreach (var position in positions)
		{
			writer.Write(position.X);
			writer.Write(position.Y);
			writer.Write(position.Z);
		}

		Seek(output, headerPosition);
		writer.Write((uint)sourceIndexOffset);
		writer.Write(0u);
		writer.Write(1u);
		writer.Write((uint)meshHeaderOffset);
		writer.Write((uint)attributes.Count);
		writer.Write((uint)attributeOffset);
		writer.Write(1u);
		writer.Write((uint)submeshOffset);
		writer.Write((uint)streamTable.Count);
		writer.Write((uint)streamTableOffset);
		writer.Write(2u);
		writer.Write((uint)descriptorOffset);
		writer.Write(0u);
		writer.Write(0u);

		var data = output.ToArray();
		return wrap ? Wrap(data) : data;
	}

	/// <summary>Wraps a GMSH payload in the 44-byte GAR5 image-style header.</summary>
	public static byte[] Wrap(byte[] gmsh)
	{
		ArgumentNullException.ThrowIfNull(gmsh);
		var result = new byte[44 + gmsh.Length];
		Encoding.ASCII.GetBytes("GAR5").CopyTo(result, 0);
		BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 0x20);
		gmsh.CopyTo(result, 44);
		return result;
	}

	private static void WriteDescriptor(BinaryWriter writer, int size, int elementSize)
	{
		writer.Write((uint)size);
		writer.Write(0u);
		writer.Write(2u);
		writer.Write((uint)elementSize);
		writer.Write(0x28u);
		writer.Write(1u);
		writer.Write(BufferHash);
		writer.Write(1u);
		writer.Write(0x40u);
		writer.Write((uint)size);
	}

	private static uint EncodePackedVector(Vector3 value)
	{
		var x = EncodePackedComponent(value.X);
		var y = EncodePackedComponent(value.Y);
		var z = EncodePackedComponent(value.Z);
		return x | (y << 10) | (z << 20);
	}

	private static uint EncodePackedComponent(float value)
	{
		var encoded = MathF.Round((Math.Clamp(value, -1f, 1f) + 1f) * 511.5f);
		return (uint)Math.Clamp(encoded, 0f, 1023f);
	}

	private static ushort ToHalf(float value) => BitConverter.HalfToUInt16Bits((Half)value);

	private static void Seek(MemoryStream stream, long position) => stream.Position = position;

	private static int Align16(int value) => (value + 15) & ~15;

	private static void ValidateLength<T>(T[]? values, int expected, string name)
	{
		if (values is not null && values.Length != expected)
		{
			throw new ArgumentException($"{name} must have {expected} entries.", name);
		}
	}

	private sealed record AttributeSpec(int Usage, int UsageOffset, uint Format, int Slot);

	private sealed record StreamSpec(int Usage, int Stride, int Offset = 0);

	private static class GmshVertexFormats
	{
		public const uint R32G32B32Float = 4;
		public const uint R16G16B16A16Float = 7;
		public const uint R10G10B10A2UNorm = 15;
		public const uint R16G16Float = 23;
		public const uint R8G8B8A8UInt = 20;
		public const uint B8G8R8A8UNorm = 58;
	}
}
