using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One GUID or resource reference found in a scene entity property set.</summary>
/// <param name="PropertySet">Property-set class name.</param>
/// <param name="Property">Property name.</param>
/// <param name="TypeName">Property type name.</param>
/// <param name="Guid">Referenced GUID, when the property is a GUID proxy.</param>
/// <param name="Resource">Referenced resource name, when the property is a resource proxy.</param>
public sealed record ElexSceneReference(
	string PropertySet,
	string Property,
	string TypeName,
	Guid? ReferenceGuid,
	string? Resource);

/// <summary>
/// One <c>eCDynamicEntity</c> in a scene-sector resource. The local transform is the leading
/// <c>bCMatrix</c> of its <c>eCDynamicEntity</c> class data; the world transform is composed with the
/// parent using the row-vector <c>local × parent</c> convention.
/// </summary>
public sealed record ElexSceneEntity(
	string Name,
	Guid EntityGuid,
	int Depth,
	byte[] FixedData,
	bool IsTransformFinite,
	Matrix4x4 LocalTransform,
	Matrix4x4 WorldTransform,
	IReadOnlyList<GenomeObject> PropertySets,
	IReadOnlyList<ElexSceneEntity> Children,
	string? MeshFileName,
	string? NetworkResource,
	IReadOnlyList<ElexSceneReference> References)
{
	/// <summary>World-space translation.</summary>
	public Vector3 WorldPosition => WorldTransform.Translation;

	/// <summary>Rotation decomposed from the world transform.</summary>
	public Quaternion WorldRotation =>
		Matrix4x4.Decompose(WorldTransform, out _, out var rotation, out _) ?
			Quaternion.Normalize(rotation) :
			Quaternion.Identity;

	/// <summary>Scale decomposed from the world transform.</summary>
	public Vector3 WorldScale =>
		Matrix4x4.Decompose(WorldTransform, out var scale, out _, out _) ?
			scale :
			Vector3.One;

	/// <summary>First non-empty template GUID referenced by the entity's property sets.</summary>
	public Guid? TemplateGuid => References
		.FirstOrDefault(reference =>
			reference.ReferenceGuid is not null &&
			string.Equals(reference.TypeName, "class eCTemplateEntityProxy", StringComparison.Ordinal))
		?.ReferenceGuid;
}

/// <summary>
/// A static-scene sector (<c>sec</c> resource). The payload is a GAR5-wrapped
/// <c>gCEmbeddedLayer</c> object containing an <c>eCScene</c> and one <c>eCDynamicEntity</c> tree.
/// </summary>
public sealed class ElexScene
{
	/// <summary>Hash of <c>class gCEmbeddedLayer</c>.</summary>
	public const uint LayerClassHash = 0x7A99504C;

	/// <summary>Hash of <c>class eCScene</c>.</summary>
	public const uint SceneClassHash = 0xFB8237D1;

	/// <summary>Hash of <c>class eCDynamicEntity</c>.</summary>
	public const uint DynamicEntityClassHash = 0x36182D05;

	/// <summary>Hash of <c>class eCEntity</c>.</summary>
	public const uint EntityClassHash = 0x4BF02D80;

	/// <summary>Hash of <c>class bCString</c>.</summary>
	public const uint StringTypeHash = 0xA83E6117;

	/// <summary>Hash of the <c>MeshFileName</c> property.</summary>
	public const uint MeshFileNamePropertyHash = 0x0296D7B3;

	/// <summary>Hash of the <c>NetworkResource</c> property.</summary>
	public const uint NetworkResourcePropertyHash = 0xE186AC77;

	/// <summary>Hash of <c>class eCTemplateEntityProxy</c>.</summary>
	public const uint TemplateEntityProxyTypeHash = 0x69FB9E7E;

	/// <summary>Hash of <c>class eCEntityProxy</c>.</summary>
	public const uint EntityProxyTypeHash = 0x6A90B122;

	/// <summary>Hash of <c>class bCGuid</c>.</summary>
	public const uint GuidTypeHash = 0x101D3889;

	private const int Gar5WrapperSize = 44;
	private const int MatrixSize = 64;
	private const int MaxChildren = 100_000;

	private ElexScene(string name, string sourcePath, ElexSceneEntity root, ElexSceneEntity[] entities)
	{
		Name = name;
		SourcePath = sourcePath;
		Root = root;
		Entities = entities;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Virtual path of the owning ROM resource.</summary>
	public string SourcePath { get; }

	/// <summary>Scene root entity.</summary>
	public ElexSceneEntity Root { get; }

	/// <summary>All entities in depth-first order, including the root.</summary>
	public IReadOnlyList<ElexSceneEntity> Entities { get; }

	/// <summary>Number of entities that carry a mesh-file name.</summary>
	public int MeshEntityCount => Entities.Count(entity => !string.IsNullOrEmpty(entity.MeshFileName));

	/// <summary>Parses a <c>sec</c> resource.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The scene payload is malformed.</exception>
	public static ElexScene Parse(string name, string sourcePath, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + 8 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new GenomeFormatException($"Scene '{name}' has no GAR5 wrapper.");
		}

		var layerOffset = FindObjectOffset(data, Gar5WrapperSize, LayerClassHash, $"Scene '{name}'");
		var layer = GenomeObject.Parse(data, layerOffset);
		var layerData = layer.ClassData.FirstOrDefault(item => item.ClassHash == LayerClassHash)?.Data
			?? throw new GenomeFormatException($"Scene '{name}' is missing gCEmbeddedLayer class data.");

		var sceneOffset = FindObjectOffset(layerData, 0, SceneClassHash, $"Scene '{name}'");
		var scene = GenomeObject.Parse(layerData, sceneOffset);
		var sceneData = scene.ClassData.FirstOrDefault(item => item.ClassHash == SceneClassHash)?.Data
			?? throw new GenomeFormatException($"Scene '{name}' is missing eCScene class data.");

		var rootOffset = FindObjectOffset(sceneData, 0, DynamicEntityClassHash, $"Scene '{name}'");
		var (root, _) = ParseEntity(sceneData, rootOffset, Matrix4x4.Identity, depth: 0, name);
		var entities = new List<ElexSceneEntity>();
		Flatten(root, entities);
		return new ElexScene(name, sourcePath, root, [.. entities]);
	}

	private static (ElexSceneEntity Entity, int End) ParseEntity(
		byte[] data,
		int offset,
		Matrix4x4 parentTransform,
		int depth,
		string sceneName)
	{
		var genomeObject = GenomeObject.Parse(data, offset);
		if (genomeObject.ClassHash != DynamicEntityClassHash)
		{
			throw new GenomeFormatException(
				$"Scene '{sceneName}' entity at {offset} has class " +
				$"{GenomeNameTable.GetNameOrHash(genomeObject.ClassHash)}, expected eCDynamicEntity.");
		}

		var dynamicData = genomeObject.ClassData
			.FirstOrDefault(item => item.ClassHash == DynamicEntityClassHash)?.Data
			?? throw new GenomeFormatException($"Scene '{sceneName}' entity has no eCDynamicEntity class data.");
		var entityData = genomeObject.ClassData
			.FirstOrDefault(item => item.ClassHash == EntityClassHash)?.Data
			?? throw new GenomeFormatException($"Scene '{sceneName}' entity has no eCEntity class data.");

		if (dynamicData.Length < MatrixSize)
		{
			throw new GenomeFormatException($"Scene '{sceneName}' entity transform is truncated.");
		}

		var (localTransform, isTransformFinite) = ReadMatrix(dynamicData);
		var worldTransform = depth == 0 ?
			localTransform :
			Matrix4x4.Multiply(localTransform, parentTransform);

		if (entityData.Length < 18)
		{
			throw new GenomeFormatException($"Scene '{sceneName}' entity name is truncated.");
		}

		var guid = new Guid(entityData.AsSpan(0, 16));
		var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entityData.AsSpan(16));
		if (nameLength > 0 && nameLength > 4096 || 18 + nameLength > entityData.Length)
		{
			throw new GenomeFormatException($"Scene '{sceneName}' entity has an invalid name length.");
		}

		var entityName = nameLength == 0 ?
			string.Empty :
			Encoding.Latin1.GetString(entityData, 18, nameLength);
		var position = 18 + nameLength;
		if (position + 6 + 1 > entityData.Length)
		{
			throw new GenomeFormatException($"Scene '{sceneName}' entity '{entityName}' is truncated.");
		}

		var fixedData = entityData.AsSpan(position, 6).ToArray();
		position += 6;
		var propertySetCount = entityData[position++];
		var propertySets = new GenomeObject[propertySetCount];
		for (var index = 0; index < propertySets.Length; index++)
		{
			propertySets[index] = GenomeObject.Parse(entityData, position);
			position += propertySets[index].Size;
		}

		if (position + 4 > entityData.Length)
		{
			throw new GenomeFormatException($"Scene '{sceneName}' entity '{entityName}' is missing children.");
		}

		var childCount = BinaryPrimitives.ReadUInt32LittleEndian(entityData.AsSpan(position));
		position += 4;
		if (childCount > MaxChildren)
		{
			throw new GenomeFormatException(
				$"Scene '{sceneName}' entity '{entityName}' declares too many children.");
		}

		var children = new ElexSceneEntity[(int)childCount];
		for (var index = 0; index < children.Length; index++)
		{
			var (child, childEnd) = ParseEntity(
				entityData, position, worldTransform, depth + 1, sceneName);
			children[index] = child;
			position = childEnd;
		}

		if (position != entityData.Length)
		{
			throw new GenomeFormatException(
				$"Scene '{sceneName}' entity '{entityName}' has trailing bytes.");
		}

		var meshFileName = FindStringProperty(propertySets, MeshFileNamePropertyHash, allowResource: false);
		var networkResource = FindStringProperty(propertySets, NetworkResourcePropertyHash, allowResource: true);
		var references = CollectReferences(propertySets);
		var entity = new ElexSceneEntity(
			entityName,
			guid,
			depth,
			fixedData,
			isTransformFinite,
			localTransform,
			worldTransform,
			propertySets,
			children,
			meshFileName,
			networkResource,
			references);
		return (entity, genomeObject.Offset + genomeObject.Size);
	}

	private static void Flatten(ElexSceneEntity entity, List<ElexSceneEntity> entities)
	{
		entities.Add(entity);
		foreach (var child in entity.Children)
		{
			Flatten(child, entities);
		}
	}

	private static (Matrix4x4 Matrix, bool IsFinite) ReadMatrix(byte[] data)
	{
		var values = new float[16];
		var isFinite = true;
		for (var index = 0; index < values.Length; index++)
		{
			var value = BitConverter.Int32BitsToSingle(
				BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(index * 4)));
			if (!float.IsFinite(value))
			{
				value = 0;
				isFinite = false;
			}

			values[index] = value;
		}

		var matrix = new Matrix4x4(
			values[0], values[1], values[2], values[3],
			values[4], values[5], values[6], values[7],
			values[8], values[9], values[10], values[11],
			values[12], values[13], values[14], values[15]);
		return (matrix, isFinite);
	}

	private static string? FindStringProperty(
		IReadOnlyList<GenomeObject> propertySets,
		uint nameHash,
		bool allowResource)
	{
		foreach (var propertySet in propertySets)
		{
			foreach (var property in propertySet.Properties)
			{
				if (property.NameHash != nameHash)
				{
					continue;
				}

				if (property.TypeHash == StringTypeHash ||
					(allowResource && IsResourceProxyType(property.TypeHash)))
				{
					return ReadString(property.Data);
				}
			}
		}

		return null;
	}

	private static bool IsResourceProxyType(uint typeHash) =>
		GenomeNameTable.GetNameOrHash(typeHash)
			.StartsWith("class eTResourceProxy", StringComparison.Ordinal);

	private static ElexSceneReference[] CollectReferences(IReadOnlyList<GenomeObject> propertySets)
	{
		var references = new List<ElexSceneReference>();
		foreach (var propertySet in propertySets)
		{
			var propertySetName = GenomeNameTable.GetNameOrHash(propertySet.ClassHash);
			foreach (var property in propertySet.Properties)
			{
				var typeName = GenomeNameTable.GetNameOrHash(property.TypeHash);
				if (property.TypeHash is TemplateEntityProxyTypeHash or EntityProxyTypeHash or GuidTypeHash)
				{
					if (property.Data.Length == 16)
					{
						var guid = new Guid(property.Data);
						if (guid != Guid.Empty)
						{
							references.Add(new ElexSceneReference(
								propertySetName,
								GenomeNameTable.GetNameOrHash(property.NameHash),
								typeName,
								guid,
								null));
						}
					}

					continue;
				}

				if (!typeName.StartsWith("class eTResourceProxy", StringComparison.Ordinal))
				{
					continue;
				}

				var resource = ReadString(property.Data);
				if (!string.IsNullOrEmpty(resource))
				{
					references.Add(new ElexSceneReference(
						propertySetName,
						GenomeNameTable.GetNameOrHash(property.NameHash),
						typeName,
						null,
						resource));
				}
			}
		}

		return [.. references];
	}

	private static string? ReadString(byte[] data)
	{
		if (data.Length < 2)
		{
			return null;
		}

		var length = BinaryPrimitives.ReadUInt16LittleEndian(data);
		return length + 2 == data.Length ?
			Encoding.Latin1.GetString(data, 2, length) :
			null;
	}

	private static int FindObjectOffset(byte[] data, int start, uint expectedClass, string owner)
	{
		var position = Math.Max(0, start);
		while (position + 4 <= data.Length)
		{
			var relative = data.AsSpan(position).IndexOf("GEC2"u8);
			if (relative < 0)
			{
				break;
			}

			var offset = position + relative;
			try
			{
				var header = GenomeObject.Parse(data, offset);
				if (header.ClassHash == expectedClass)
				{
					return offset;
				}
			}
			catch (GenomeFormatException)
			{
				// Sector names may contain the magic; keep looking.
			}

			position = offset + 4;
		}

		throw new GenomeFormatException(
			$"{owner} has no {GenomeNameTable.GetNameOrHash(expectedClass)} object.");
	}
}
