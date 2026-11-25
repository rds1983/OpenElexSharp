using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Additional per-part metadata that does not belong in DigitalRiseModel's <see cref="DrMeshPart"/>.
/// </summary>
/// <param name="Bounds">Bounding sphere used by viewers and future scene culling.</param>
/// <param name="Material">Runtime material bound to this range, when known.</param>
/// <param name="FirstIndex">First index of the range in the shared index buffer.</param>
/// <param name="IndexCount">Number of indices in the range.</param>
/// <param name="LODRangeStart">Distance at which the LOD containing this mesh becomes active.</param>
public sealed record DrMeshPartTag(
	BoundingSphere Bounds,
	ElexMaterial? Material = null,
	int FirstIndex = 0,
	int IndexCount = 0,
	float LODRangeStart = 0f);

/// <summary>One submesh index range resolved from mesh metadata.</summary>
/// <param name="StartIndex">First index in the source index buffer.</param>
/// <param name="IndexCount">Number of indices in the range; always a multiple of three.</param>
/// <param name="MaterialName">Material resource name, when known.</param>
/// <param name="Bounds">Submesh box from metadata, when valid.</param>
public sealed record MeshSubmeshRange(
	int StartIndex,
	int IndexCount,
	string? MaterialName,
	BoundingBox? Bounds)
{
	/// <summary>Number of triangles in the range.</summary>
	public int PrimitiveCount => IndexCount / 3;
}

/// <summary>Builds DigitalRiseModel meshes from parsed GMSH resources and mesh metadata.</summary>
public static class MeshLoader
{
	/// <summary>
	/// Builds a <see cref="DrMesh"/> with one part per submesh. When <paramref name="metadata"/> is
	/// missing or has no valid ranges, a single part covers the whole index buffer.
	/// </summary>
	/// <param name="device">Graphics device used to create buffers.</param>
	/// <param name="source">Parsed GMSH geometry.</param>
	/// <param name="metadata">Optional <c>eCMeshResource2</c> metadata with submesh ranges and materials.</param>
	/// <param name="materialResolver">Optional resolver that maps a material name to a runtime material.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GmshFormatException">The mesh has no usable triangle/position data.</exception>
	public static DrMesh Load(
		GraphicsDevice device,
		GmshMesh source,
		MeshMetadata? metadata = null,
		Func<string, ElexMaterial?>? materialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(source);
		if (!source.IsTriangleList || source.Indices.Count == 0 || source.Positions.Count == 0)
		{
			throw new GmshFormatException($"Mesh '{source.Name}' has no usable triangle list.");
		}

		var positions = source.Positions;
		var normals = source.Normals.Count == positions.Count
			? source.Normals
			: ComputeNormals(positions, source.Indices);
		var textureCoordinates = source.TextureCoordinates.Count == positions.Count
			? source.TextureCoordinates
			: null;
		var colors = source.Colors.Count == positions.Count
			? source.Colors
			: null;

		var vertices = new VertexPositionColorNormalTexture[positions.Count];
		for (var index = 0; index < vertices.Length; index++)
		{
			var color = colors is null
				? Color.White
				: new Color(colors[index].X, colors[index].Y, colors[index].Z, colors[index].W);
			vertices[index] = new VertexPositionColorNormalTexture(
				new Vector3(positions[index].X, positions[index].Y, positions[index].Z),
				color,
				new Vector3(normals[index].X, normals[index].Y, normals[index].Z),
				textureCoordinates is null
					? Vector2.Zero
					: new Vector2(textureCoordinates[index].X, textureCoordinates[index].Y));
		}

		var indices = new int[source.Indices.Count];
		for (var index = 0; index < indices.Length; index++)
		{
			indices[index] = source.Indices[index];
		}

		var vertexBuffer = new VertexBuffer(
			device,
			VertexPositionColorNormalTexture.VertexDeclaration,
			vertices.Length,
			BufferUsage.WriteOnly);
		vertexBuffer.SetData(vertices);

		var indexBuffer = CreateIndexBuffer(device, indices);

		var (box, _) = ComputeBounds(positions);
		var mesh = new DrMesh
		{
			Name = source.Name,
		};

		var ranges = ResolveSubmeshes(source, metadata);
		foreach (var range in ranges)
		{
			var rangeBox = range.Bounds ?? box;
			var rangeSphere = new BoundingSphere(
				(rangeBox.Min + rangeBox.Max) * 0.5f,
				Math.Max(0.001f, Vector3.Distance((rangeBox.Min + rangeBox.Max) * 0.5f, rangeBox.Max)));
			var part = new DrMeshPart(
				vertexBuffer,
				indexBuffer,
				rangeBox,
				PrimitiveType.TriangleList,
				vertices.Length,
				range.PrimitiveCount,
				0,
				range.StartIndex)
			{
				Tag = new DrMeshPartTag(
					rangeSphere,
					ResolveMaterial(materialResolver, range.MaterialName),
					range.StartIndex,
					range.IndexCount,
					metadata?.LODRangeStart ?? 0f),
			};

			mesh.MeshParts.Add(part);
		}

		return mesh;
	}

	internal static IndexBuffer CreateIndexBuffer(GraphicsDevice device, int[] indices)
	{
		var use32Bit = indices.Any(index => index > ushort.MaxValue);
		var indexBuffer = use32Bit
			? new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly)
			: new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
		if (use32Bit)
		{
			indexBuffer.SetData(indices);
		}
		else
		{
			var shorts = new ushort[indices.Length];
			for (var index = 0; index < shorts.Length; index++)
			{
				shorts[index] = (ushort)indices[index];
			}

			indexBuffer.SetData(shorts);
		}

		return indexBuffer;
	}

	private static ElexMaterial? ResolveMaterial(
		Func<string, ElexMaterial?>? materialResolver,
		string? materialName)
	{
		if (materialResolver is null || string.IsNullOrEmpty(materialName))
		{
			return null;
		}

		return materialResolver(materialName);
	}

	/// <summary>Generates normalized smooth normals from the face list.</summary>
	public static NumericsVector3[] ComputeNormals(
		IReadOnlyList<NumericsVector3> positions,
		IReadOnlyList<int> indices)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(indices);
		var normals = new NumericsVector3[positions.Count];
		for (var index = 0; index + 2 < indices.Count; index += 3)
		{
			var a = positions[indices[index]];
			var b = positions[indices[index + 1]];
			var c = positions[indices[index + 2]];
			var normal = NumericsVector3.Cross(b - a, c - a);
			normals[indices[index]] += normal;
			normals[indices[index + 1]] += normal;
			normals[indices[index + 2]] += normal;
		}

		for (var index = 0; index < normals.Length; index++)
		{
			normals[index] = normals[index].LengthSquared() > 1e-12f
				? NumericsVector3.Normalize(normals[index])
				: NumericsVector3.UnitY;
		}

		return normals;
	}

	private static (BoundingBox Box, BoundingSphere Sphere) ComputeBounds(
		IReadOnlyList<NumericsVector3> positions)
	{
		var min = positions[0];
		var max = positions[0];
		foreach (var position in positions)
		{
			min = NumericsVector3.Min(min, position);
			max = NumericsVector3.Max(max, position);
		}

		var center = (min + max) * 0.5f;
		var radius = Math.Max(0.001f, NumericsVector3.Distance(center, max));
		var box = new BoundingBox(
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(max.X, max.Y, max.Z));
		var sphere = new BoundingSphere(new Vector3(center.X, center.Y, center.Z), radius);
		return (box, sphere);
	}

	/// <summary>
	/// Resolves submesh ranges from metadata, falling back to one range over the whole index
	/// buffer. Ranges outside the index list or not divisible by three are skipped.
	/// </summary>
	/// <param name="source">Parsed mesh geometry.</param>
	/// <param name="metadata">Optional database metadata.</param>
	/// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
	public static List<MeshSubmeshRange> ResolveSubmeshes(GmshMesh source, MeshMetadata? metadata)
	{
		ArgumentNullException.ThrowIfNull(source);
		var ranges = new List<MeshSubmeshRange>();
		if (metadata is not null)
		{
			foreach (var subMesh in metadata.SubMeshes)
			{
				if (subMesh.IndexCount <= 0 || subMesh.FirstIndex < 0)
				{
					continue;
				}

				if (subMesh.FirstIndex + subMesh.IndexCount > source.Indices.Count)
				{
					continue;
				}

				if (subMesh.IndexCount % 3 != 0)
				{
					continue;
				}

				ranges.Add(new MeshSubmeshRange(
					subMesh.FirstIndex,
					subMesh.IndexCount,
					subMesh.MaterialName,
					TryCreateBox(subMesh.Extends)));
			}
		}

		if (ranges.Count == 0)
		{
			ranges.Add(new MeshSubmeshRange(0, source.Indices.Count, null, null));
		}

		return ranges;
	}

	private static BoundingBox? TryCreateBox(IReadOnlyList<float> extends)
	{
		if (extends.Count != 6)
		{
			return null;
		}

		var min = new Vector3(extends[0], extends[1], extends[2]);
		var max = new Vector3(extends[3], extends[4], extends[5]);
		if (!IsFinite(min) || !IsFinite(max) || min.X > max.X || min.Y > max.Y || min.Z > max.Z)
		{
			return null;
		}

		return new BoundingBox(min, max);
	}

	private static bool IsFinite(Vector3 value) =>
		float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
