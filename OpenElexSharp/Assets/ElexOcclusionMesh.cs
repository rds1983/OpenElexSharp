using System.Buffers.Binary;
using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// An ELEX occlusion mesh (<c>ocm</c> resource). The payload starts with four 32-bit offsets/counts,
/// followed by <c>float4</c> vertices and <c>uint16</c> triangle indices.
/// </summary>
public sealed class ElexOcclusionMesh
{
	/// <summary>Outer GAR5 wrapper size observed for <c>ocm</c> resources.</summary>
	public const int Gar5WrapperSize = 44;

	/// <summary>Bytes per occlusion vertex (three floats plus a fourth value).</summary>
	public const int VertexStride = 16;

	/// <summary>Header size before the vertex array.</summary>
	public const int HeaderSize = 48;

	private const int MaxVertexCount = 4_000_000;
	private const int MaxIndexCount = 24_000_000;

	private ElexOcclusionMesh(
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

	/// <summary>Parses an <c>ocm</c> occlusion resource.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The payload is malformed.</exception>
	public static ElexOcclusionMesh Parse(string name, string sourcePath, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + HeaderSize ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new GenomeFormatException($"Occlusion mesh '{name}' has no GAR5 wrapper.");
		}

		var payload = Gar5WrapperSize;
		var vertexOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(payload));
		var vertexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(payload + 8));
		var indexOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(payload + 16));
		var indexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(payload + 24));
		if (vertexOffset != HeaderSize || vertexCount < 3 || vertexCount > MaxVertexCount ||
			indexCount < 3 || indexCount > MaxIndexCount || indexCount % 3 != 0)
		{
			throw new GenomeFormatException(
				$"Occlusion mesh '{name}' has an invalid header " +
				$"(vertices {vertexCount}, indices {indexCount}).");
		}

		var vertexStart = payload + vertexOffset;
		var indexStart = payload + indexOffset;
		if (indexStart < vertexStart + vertexCount * VertexStride ||
			indexStart + indexCount * 2 > data.Length)
		{
			throw new GenomeFormatException($"Occlusion mesh '{name}' arrays are out of range.");
		}

		var vertices = new Vector3[vertexCount];
		for (var index = 0; index < vertices.Length; index++)
		{
			var offset = vertexStart + index * VertexStride;
			var x = ReadSingle(data, offset);
			var y = ReadSingle(data, offset + 4);
			var z = ReadSingle(data, offset + 8);
			if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
			{
				throw new GenomeFormatException($"Occlusion mesh '{name}' has a non-finite vertex.");
			}

			vertices[index] = new Vector3(x, y, z);
		}

		var indices = new int[indexCount];
		for (var index = 0; index < indices.Length; index++)
		{
			indices[index] = BinaryPrimitives.ReadUInt16LittleEndian(
				data.AsSpan(indexStart + index * 2));
			if (indices[index] >= vertexCount)
			{
				throw new GenomeFormatException($"Occlusion mesh '{name}' has an out-of-range index.");
			}
		}

		return new ElexOcclusionMesh(name, sourcePath, vertices, indices);
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
