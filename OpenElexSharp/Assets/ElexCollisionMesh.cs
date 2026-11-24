using System.Buffers.Binary;
using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// An ELEX collision mesh (<c>col</c> resource, <c>SEBD</c> payload). The file stores a triangle list
/// in centimetre-like local coordinates; the parser locates the vertex/index arrays from the
/// duplicated vertex/triangle counts in the header.
/// </summary>
public sealed class ElexCollisionMesh
{
	/// <summary>Payload magic <c>SEBD</c> as a little-endian integer.</summary>
	public const uint Magic = 0x44424553;

	/// <summary>Outer GAR5 wrapper size observed for <c>col</c> resources.</summary>
	public const int Gar5WrapperSize = 44;

	/// <summary>Header offset (relative to the payload) of the vertex count.</summary>
	public const int VertexCountOffset = 0xA0;

	/// <summary>Header offset (relative to the payload) of the triangle count.</summary>
	public const int TriangleCountOffset = 0xA4;

	/// <summary>Bytes per collision vertex.</summary>
	public const int VertexStride = 12;

	private const int MinArrayOffset = 0x100;
	private const int MaxVertexCount = 4_000_000;
	private const int MaxTriangleCount = 8_000_000;
	private const float MaxCoordinate = 10_000_000f;

	private ElexCollisionMesh(
		string name,
		string sourcePath,
		Vector3[] vertices,
		int[] indices)
	{
		Name = name;
		SourcePath = sourcePath;
		Vertices = vertices;
		Indices = indices;
		BoundsMin = ComputeBounds(vertices, out var boundsMax);
		BoundsMax = boundsMax;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Virtual path of the owning ROM resource.</summary>
	public string SourcePath { get; }

	/// <summary>Local-space vertices.</summary>
	public IReadOnlyList<Vector3> Vertices { get; }

	/// <summary>Triangle-list indices.</summary>
	public IReadOnlyList<int> Indices { get; }

	/// <summary>Number of triangles.</summary>
	public int TriangleCount => Indices.Count / 3;

	/// <summary>Local-space minimum bound.</summary>
	public Vector3 BoundsMin { get; }

	/// <summary>Local-space maximum bound.</summary>
	public Vector3 BoundsMax { get; }

	/// <summary>Parses a <c>col</c> collision resource.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The payload is malformed.</exception>
	public static ElexCollisionMesh Parse(string name, string sourcePath, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + 0xB0 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic ||
			BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize)) != Magic)
		{
			throw new GenomeFormatException($"Collision mesh '{name}' has no GAR5/SEBD header.");
		}

		var vertexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(
			data.AsSpan(Gar5WrapperSize + VertexCountOffset));
		var triangleCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(
			data.AsSpan(Gar5WrapperSize + TriangleCountOffset));
		if (vertexCount < 3 || vertexCount > MaxVertexCount ||
			triangleCount < 1 || triangleCount > MaxTriangleCount)
		{
			throw new GenomeFormatException(
				$"Collision mesh '{name}' declares {vertexCount} vertices and {triangleCount} triangles.");
		}

		var indexCount = checked(triangleCount * 3);
		var indexOffset = FindIndexArray(
			data, vertexCount, indexCount, name, out var indexStride);
		var vertexOffset = indexOffset - vertexCount * VertexStride;
		if (vertexOffset < Gar5WrapperSize + MinArrayOffset)
		{
			throw new GenomeFormatException($"Collision mesh '{name}' vertex array is out of range.");
		}

		var vertices = new Vector3[vertexCount];
		for (var index = 0; index < vertices.Length; index++)
		{
			var offset = vertexOffset + index * VertexStride;
			var x = ReadSingle(data, offset);
			var y = ReadSingle(data, offset + 4);
			var z = ReadSingle(data, offset + 8);
			if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
			{
				throw new GenomeFormatException($"Collision mesh '{name}' has a non-finite vertex.");
			}

			vertices[index] = new Vector3(x, y, z);
		}

		var indices = new int[indexCount];
		for (var index = 0; index < indices.Length; index++)
		{
			indices[index] = indexStride == 2 ?
				BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(indexOffset + index * 2)) :
				(int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(indexOffset + index * 4));
		}

		return new ElexCollisionMesh(name, sourcePath, vertices, indices);
	}

	private static int FindIndexArray(
		byte[] data,
		int vertexCount,
		int indexCount,
		string name,
		out int indexStride)
	{
		var payload = Gar5WrapperSize;
		indexStride = vertexCount <= ushort.MaxValue ? 2 : 4;
		var valueCount = (data.Length - payload) / indexStride;
		var values = new uint[valueCount];
		var invalid = new int[valueCount + 1];
		for (var valueIndex = 0; valueIndex < valueCount; valueIndex++)
		{
			var value = indexStride == 2 ?
				BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(payload + valueIndex * 2)) :
				BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(payload + valueIndex * 4));
			values[valueIndex] = value;
			invalid[valueIndex + 1] = invalid[valueIndex] + (value >= vertexCount ? 1 : 0);
		}

		var minimumDistinct = Math.Min(
			Math.Max(3, vertexCount / 2),
			Math.Min(64, indexCount));
		var capacity = Math.Min(indexCount + 1, valueCount);
		var deque = new int[capacity];
		var head = 0;
		var tail = 0;
		var count = 0;
		for (var valueIndex = 0; valueIndex < valueCount; valueIndex++)
		{
			while (count > 0 && values[deque[(tail - 1 + capacity) % capacity]] <= values[valueIndex])
			{
				tail = (tail - 1 + capacity) % capacity;
				count--;
			}

			deque[tail] = valueIndex;
			tail = (tail + 1) % capacity;
			count++;
			var start = valueIndex - indexCount + 1;
			if (start < MinArrayOffset / indexStride)
			{
				continue;
			}

			while (count > 0 && deque[head] < start)
			{
				head = (head + 1) % capacity;
				count--;
			}

			if (count == 0 || values[deque[head]] != vertexCount - 1 ||
				invalid[start + indexCount] != invalid[start])
			{
				continue;
			}

			var seen = new HashSet<uint>();
			var zeroSeen = false;
			for (var offset = 0; offset < indexCount; offset++)
			{
				var value = values[start + offset];
				seen.Add(value);
				zeroSeen |= value == 0;
			}

			if (!zeroSeen || seen.Count < minimumDistinct)
			{
				continue;
			}

			var vertexStart = payload + start * indexStride - vertexCount * VertexStride;
			if (!AreVerticesPlausible(data, vertexStart, vertexCount))
			{
				continue;
			}

			return payload + start * indexStride;
		}

		throw new GenomeFormatException(
			$"Collision mesh '{name}' has no index array with {indexCount} entries.");
	}

	private static bool AreVerticesPlausible(byte[] data, int offset, int count)
	{
		if (offset < Gar5WrapperSize + MinArrayOffset || offset % 4 != 0 ||
			offset + count * VertexStride > data.Length)
		{
			return false;
		}

		for (var index = 0; index < count; index++)
		{
			var vertexOffset = offset + index * VertexStride;
			var x = ReadSingle(data, vertexOffset);
			var y = ReadSingle(data, vertexOffset + 4);
			var z = ReadSingle(data, vertexOffset + 8);
			if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
				MathF.Abs(x) > MaxCoordinate || MathF.Abs(y) > MaxCoordinate ||
				MathF.Abs(z) > MaxCoordinate)
			{
				return false;
			}
		}

		return true;
	}

	private static float ReadSingle(byte[] data, int offset) =>
		BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)));

	private static Vector3 ComputeBounds(IReadOnlyList<Vector3> vertices, out Vector3 maximum)
	{
		if (vertices.Count == 0)
		{
			maximum = Vector3.Zero;
			return Vector3.Zero;
		}

		var min = vertices[0];
		var max = vertices[0];
		foreach (var vertex in vertices)
		{
			min = Vector3.Min(min, vertex);
			max = Vector3.Max(max, vertex);
		}

		maximum = max;
		return min;
	}
}
