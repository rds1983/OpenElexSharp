using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One submesh range and material binding from an <c>eCMeshResource2</c> record.</summary>
/// <param name="MaterialName">Material resource name, for example <c>AS_Alb_Tileset_Wall_3</c>.</param>
/// <param name="FirstIndex">First triangle index in the mesh index buffer.</param>
/// <param name="IndexCount">Number of indices in the submesh.</param>
/// <param name="FirstVertex">First vertex used for skinning/batching.</param>
/// <param name="VertexCount">Number of vertices in the skinning/batch range.</param>
/// <param name="FirstBone">First bone index (skinned meshes).</param>
/// <param name="BoneCount">Number of bones (skinned meshes).</param>
/// <param name="MaterialSwitchIndex">Material-switch index used by animated materials.</param>
/// <param name="Extends">Six floats describing the submesh bounding box.</param>
public sealed record MeshSubMesh(
	string MaterialName,
	int FirstIndex,
	int IndexCount,
	int FirstVertex,
	int VertexCount,
	int FirstBone,
	int BoneCount,
	int MaterialSwitchIndex,
	IReadOnlyList<float> Extends)
{
	/// <summary>Bounding box as (min x, min y, min z, max x, max y, max z), when present.</summary>
	public (int Index, int Count) IndexRange => (FirstIndex, IndexCount);
}

/// <summary>
/// Mesh resource metadata from the <c>eCMeshResource2</c> class block stored in a <c>w_msh</c>
/// database. This is where submesh material names, index ranges and LOD distances live; the GMSH
/// payload itself only stores geometry and buffer descriptors.
/// </summary>
public sealed class MeshMetadata
{
	/// <summary>Case-sensitive DJB2 hash of <c>class eCMeshResource2</c>.</summary>
	public const uint MeshResourceClassHash = 0x1C78680A;

	private static readonly uint SubMeshesHash = Djb2Hash.ComputeCaseSensitive("SubMeshes");
	private static readonly uint IndexCountHash = Djb2Hash.ComputeCaseSensitive("IndexCount");
	private static readonly uint VertexCountHash = Djb2Hash.ComputeCaseSensitive("VertexCount");
	private static readonly uint BoundaryHash = Djb2Hash.ComputeCaseSensitive("Boundary");
	private static readonly uint BoundingSphereHash = Djb2Hash.ComputeCaseSensitive("BoundingSphere");
	private static readonly uint LodRangeStartHash = Djb2Hash.ComputeCaseSensitive("LoDRangeStart");
	private static readonly uint MaterialNameHash = Djb2Hash.ComputeCaseSensitive("MaterialName");
	private static readonly uint ExtendsHash = Djb2Hash.ComputeCaseSensitive("Extends");
	private static readonly uint FirstIndexHash = Djb2Hash.ComputeCaseSensitive("FirstIndex");
	private static readonly uint SubMeshIndexCountHash = Djb2Hash.ComputeCaseSensitive("IndexCount");
	private static readonly uint FirstVertexHash = Djb2Hash.ComputeCaseSensitive("FirstVertex");
	private static readonly uint SubMeshVertexCountHash = Djb2Hash.ComputeCaseSensitive("VertexCount");
	private static readonly uint FirstBoneHash = Djb2Hash.ComputeCaseSensitive("FirstBone");
	private static readonly uint BoneCountHash = Djb2Hash.ComputeCaseSensitive("BoneCount");
	private static readonly uint MaterialSwitchIndexHash = Djb2Hash.ComputeCaseSensitive("MaterialSwitchIndex");

	private MeshMetadata(
		uint classHash,
		int indexCount,
		int vertexCount,
		float[] boundary,
		float[] boundingSphere,
		float lodRangeStart,
		MeshSubMesh[] subMeshes)
	{
		ClassHash = classHash;
		IndexCount = indexCount;
		VertexCount = vertexCount;
		Boundary = boundary;
		BoundingSphere = boundingSphere;
		LODRangeStart = lodRangeStart;
		SubMeshes = subMeshes;
	}

	/// <summary>Class hash from the deserialized object.</summary>
	public uint ClassHash { get; }

	/// <summary>Total index count recorded by the resource.</summary>
	public int IndexCount { get; }

	/// <summary>Total vertex count recorded by the resource.</summary>
	public int VertexCount { get; }

	/// <summary>Bounding box as (min x, min y, min z, max x, max y, max z).</summary>
	public IReadOnlyList<float> Boundary { get; }

	/// <summary>Bounding sphere as (center x, center y, center z, radius).</summary>
	public IReadOnlyList<float> BoundingSphere { get; }

	/// <summary>Distance at which this LOD becomes active; 0 for the first LOD.</summary>
	public float LODRangeStart { get; }

	/// <summary>Submesh ranges and material bindings.</summary>
	public IReadOnlyList<MeshSubMesh> SubMeshes { get; }

	/// <summary>Distinct material names in submesh order.</summary>
	public IReadOnlyList<string> MaterialNames =>
		SubMeshes.Select(subMesh => subMesh.MaterialName)
			.Where(name => !string.IsNullOrEmpty(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

	/// <summary>Parses an <c>eCMeshResource2</c> class block.</summary>
	/// <exception cref="ArgumentNullException">The buffer is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The class block is malformed.</exception>
	public static MeshMetadata Parse(byte[] classBlock)
	{
		ArgumentNullException.ThrowIfNull(classBlock);
		var genomeObject = GenomeObject.Parse(classBlock, 0);
		if (genomeObject.ClassHash != MeshResourceClassHash)
		{
			throw new GenomeFormatException(
				$"Expected class eCMeshResource2, found 0x{genomeObject.ClassHash:x8}.");
		}

		var subMeshes = ParseSubMeshes(genomeObject);
		return new MeshMetadata(
			genomeObject.ClassHash,
			(int)ReadUInt32Property(genomeObject, IndexCountHash),
			(int)ReadUInt32Property(genomeObject, VertexCountHash),
			ReadFloatsProperty(genomeObject, BoundaryHash, 6),
			ReadFloatsProperty(genomeObject, BoundingSphereHash, 4),
			ReadFloatProperty(genomeObject, LodRangeStartHash),
			subMeshes);
	}

	private static MeshSubMesh[] ParseSubMeshes(GenomeObject genomeObject)
	{
		var property = FindProperty(genomeObject, SubMeshesHash);
		if (property is null)
		{
			return [];
		}

		var data = property.Data;
		if (data.Length < 4)
		{
			throw new GenomeFormatException("The SubMeshes array is truncated.");
		}

		var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
		if (count > 4096)
		{
			throw new GenomeFormatException($"The SubMeshes array declares too many entries ({count}).");
		}

		var result = new MeshSubMesh[count];
		var position = 4;
		for (var index = 0; index < result.Length; index++)
		{
			var child = GenomeObject.Parse(data, position);
			result[index] = new MeshSubMesh(
				ReadStringProperty(child, MaterialNameHash) ?? string.Empty,
				(int)ReadUInt32Property(child, FirstIndexHash),
				(int)ReadUInt32Property(child, SubMeshIndexCountHash),
				(int)ReadUInt32Property(child, FirstVertexHash),
				(int)ReadUInt32Property(child, SubMeshVertexCountHash),
				(int)ReadUInt32Property(child, FirstBoneHash),
				(int)ReadUInt32Property(child, BoneCountHash),
				(int)ReadInt32Property(child, MaterialSwitchIndexHash),
				ReadFloatsProperty(child, ExtendsHash, 6));
			position += child.Size;
		}

		if (position != data.Length)
		{
			throw new GenomeFormatException("The SubMeshes array has trailing data.");
		}

		return result;
	}

	private static GenomeProperty? FindProperty(GenomeObject genomeObject, uint nameHash)
	{
		foreach (var property in genomeObject.Properties)
		{
			if (property.NameHash == nameHash)
			{
				return property;
			}
		}

		return null;
	}

	private static uint ReadUInt32Property(GenomeObject genomeObject, uint nameHash)
	{
		var property = FindProperty(genomeObject, nameHash)
			?? throw new GenomeFormatException($"Missing mesh property 0x{nameHash:x8}.");
		RequireSize(property, 4);
		return BinaryPrimitives.ReadUInt32LittleEndian(property.Data);
	}

	private static int ReadInt32Property(GenomeObject genomeObject, uint nameHash)
	{
		var property = FindProperty(genomeObject, nameHash);
		if (property is null)
		{
			return 0;
		}

		RequireSize(property, 4);
		return BinaryPrimitives.ReadInt32LittleEndian(property.Data);
	}

	private static float ReadFloatProperty(GenomeObject genomeObject, uint nameHash)
	{
		var property = FindProperty(genomeObject, nameHash);
		if (property is null)
		{
			return 0f;
		}

		RequireSize(property, 4);
		return BinaryPrimitives.ReadSingleLittleEndian(property.Data);
	}

	private static float[] ReadFloatsProperty(GenomeObject genomeObject, uint nameHash, int count)
	{
		var property = FindProperty(genomeObject, nameHash);
		if (property is null)
		{
			return [];
		}

		if (property.Data.Length != count * 4)
		{
			throw new GenomeFormatException(
				$"Mesh property 0x{nameHash:x8} has {property.Data.Length} bytes, expected {count * 4}.");
		}

		var result = new float[count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = BinaryPrimitives.ReadSingleLittleEndian(property.Data.AsSpan(index * 4));
		}

		return result;
	}

	private static string? ReadStringProperty(GenomeObject genomeObject, uint nameHash)
	{
		var property = FindProperty(genomeObject, nameHash);
		if (property is null)
		{
			return null;
		}

		if (property.Data.Length < 2)
		{
			throw new GenomeFormatException($"Mesh string property 0x{nameHash:x8} is truncated.");
		}

		var length = BinaryPrimitives.ReadUInt16LittleEndian(property.Data);
		if (length > property.Data.Length - 2)
		{
			throw new GenomeFormatException($"Mesh string property 0x{nameHash:x8} has an invalid length.");
		}

		return Encoding.Latin1.GetString(property.Data, 2, length);
	}

	private static void RequireSize(GenomeProperty property, int size)
	{
		if (property.Data.Length != size)
		{
			throw new GenomeFormatException(
				$"Mesh property 0x{property.NameHash:x8} has {property.Data.Length} bytes, expected {size}.");
		}
	}
}
