using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// An ELEX entity/item template: a GTP0 file wrapping a GEC2 <c>eCTemplateEntity</c> object.
/// </summary>
public sealed class GenomeTemplate
{
	/// <summary>Hash of <c>class eCTemplateEntity</c>.</summary>
	public const uint TemplateEntityClassHash = 0x60DE515C;

	/// <summary>Hash of <c>class eCEntity</c>.</summary>
	public const uint EntityClassHash = 0x4BF02D80;

	private const int MaxNestingDepth = 16;

	private GenomeTemplate(
		string name,
		string entityName,
		Guid guid,
		Guid parentGuid,
		ulong timestamp,
		string sourcePath,
		GenomeObject templateObject,
		GenomeObject[] propertySets,
		GenomeTemplate[] children)
	{
		Name = name;
		EntityName = entityName;
		Guid = guid;
		ParentGuid = parentGuid;
		Timestamp = timestamp;
		SourcePath = sourcePath;
		Object = templateObject;
		PropertySets = propertySets;
		Children = children;
	}

	/// <summary>Logical resource name (or child entity name).</summary>
	public string Name { get; }

	/// <summary>Name stored in the template's <c>eCEntity</c> class data, usually ending in <c>.entity</c>.</summary>
	public string EntityName { get; }

	/// <summary>Template GUID.</summary>
	public Guid Guid { get; }

	/// <summary>Parent template GUID, or <see cref="Guid.Empty"/> for roots.</summary>
	public Guid ParentGuid { get; }

	/// <summary>GTP0 timestamp.</summary>
	public ulong Timestamp { get; }

	/// <summary>Virtual path of the owning resource.</summary>
	public string SourcePath { get; }

	/// <summary>The parsed template object.</summary>
	public GenomeObject Object { get; }

	/// <summary>Own property-set objects embedded in the entity class data.</summary>
	public IReadOnlyList<GenomeObject> PropertySets { get; }

	/// <summary>Child templates embedded in the entity class data.</summary>
	public IReadOnlyList<GenomeTemplate> Children { get; }

	/// <summary>Resolved parent template, when available.</summary>
	public GenomeTemplate? Parent { get; private set; }

	/// <summary>Inheritance depth; roots are 0.</summary>
	public int Depth => Parent is null ? 0 : Parent.Depth + 1;

	/// <summary>
	/// Returns property sets from the root ancestor through this template, with later definitions
	/// overriding earlier ones with the same class hash and version.
	/// </summary>
	public IReadOnlyList<GenomeObject> GetEffectivePropertySets()
	{
		var chain = new List<GenomeTemplate>();
		var visited = new HashSet<Guid>();
		for (var current = this; current is not null; current = current.Parent)
		{
			if (!visited.Add(current.Guid))
			{
				throw new GenomeFormatException($"Template inheritance cycle detected at '{current.Name}'.");
			}

			chain.Add(current);
		}

		chain.Reverse();
		var merged = new Dictionary<(uint ClassHash, ushort Version), GenomeObject>();
		var order = new List<(uint, ushort)>();
		foreach (var template in chain)
		{
			MergePropertySets(template, merged, order);
		}

		return [.. order.Select(key => merged[key])];
	}

	/// <summary>
	/// Returns the full composed property set: inherited sets, own sets and all descendant
	/// templates' property sets. This is the effective item/entity composition.
	/// </summary>
	public IReadOnlyList<GenomeObject> GetAllPropertySets()
	{
		var merged = new Dictionary<(uint ClassHash, ushort Version), GenomeObject>();
		var order = new List<(uint, ushort)>();
		foreach (var template in GetEffectiveChain())
		{
			MergePropertySets(template, merged, order);
		}

		var visited = new HashSet<Guid>();
		foreach (var child in Children)
		{
			CollectPropertySets(child, merged, order, visited);
		}

		return [.. order.Select(key => merged[key])];
	}

	private IEnumerable<GenomeTemplate> GetEffectiveChain()
	{
		var chain = new List<GenomeTemplate>();
		var visited = new HashSet<Guid>();
		for (var current = this; current is not null; current = current.Parent)
		{
			if (!visited.Add(current.Guid))
			{
				throw new GenomeFormatException($"Template inheritance cycle detected at '{current.Name}'.");
			}

			chain.Add(current);
		}

		chain.Reverse();
		return chain;
	}

	private static void CollectPropertySets(
		GenomeTemplate template,
		Dictionary<(uint ClassHash, ushort Version), GenomeObject> merged,
		List<(uint, ushort)> order,
		HashSet<Guid> visited)
	{
		if (!visited.Add(template.Guid))
		{
			return;
		}

		MergePropertySets(template, merged, order);
		foreach (var child in template.Children)
		{
			CollectPropertySets(child, merged, order, visited);
		}
	}

	private static void MergePropertySets(
		GenomeTemplate template,
		Dictionary<(uint ClassHash, ushort Version), GenomeObject> merged,
		List<(uint, ushort)> order)
	{
		foreach (var propertySet in template.PropertySets)
		{
			var key = (propertySet.ClassHash, propertySet.Version);
			if (!merged.ContainsKey(key))
			{
				order.Add(key);
			}

			merged[key] = propertySet;
		}
	}

	internal void ResolveParent(GenomeTemplate? parent) => Parent = parent;

	/// <summary>Parses a complete GTP0 template resource.</summary>
	/// <exception cref="GenomeFormatException">The wrapper or template object is malformed.</exception>
	public static GenomeTemplate Parse(string name, string sourcePath, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(sourcePath);

		var innerOffset = FindInnerTemplateOffset(data);
		if (innerOffset + 20 > data.Length)
		{
			throw new GenomeFormatException($"Template '{name}' is truncated.");
		}

		var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(innerOffset + 12));
		var genomeObject = GenomeObject.Parse(data, innerOffset + 20);
		return ParseObject(name, sourcePath, timestamp, genomeObject, depth: 0);
	}

	private static GenomeTemplate ParseObject(
		string name,
		string sourcePath,
		ulong timestamp,
		GenomeObject genomeObject,
		int depth)
	{
		if (depth > MaxNestingDepth)
		{
			throw new GenomeFormatException($"Template '{name}' nesting exceeds the supported depth.");
		}

		if (genomeObject.ClassHash != TemplateEntityClassHash)
		{
			throw new GenomeFormatException(
				$"Template '{name}' has class hash 0x{genomeObject.ClassHash:x8}, expected eCTemplateEntity.");
		}

		var templateEntityData = genomeObject.ClassData.FirstOrDefault(data => data.ClassHash == TemplateEntityClassHash)
			?? throw new GenomeFormatException($"Template '{name}' is missing eCTemplateEntity class data.");
		var entityData = genomeObject.ClassData.FirstOrDefault(data => data.ClassHash == EntityClassHash)
			?? throw new GenomeFormatException($"Template '{name}' is missing eCEntity class data.");

		if (templateEntityData.Data.Length < 16 || entityData.Data.Length < 18)
		{
			throw new GenomeFormatException($"Template '{name}' has truncated class data.");
		}

		var parentGuid = new Guid(templateEntityData.Data.AsSpan(0, 16));
		var guid = new Guid(entityData.Data.AsSpan(0, 16));
		var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entityData.Data.AsSpan(16));
		if (nameLength == 0 || 18 + nameLength > entityData.Data.Length)
		{
			throw new GenomeFormatException($"Template '{name}' has an invalid entity name length.");
		}

		var entityName = Encoding.Latin1.GetString(entityData.Data, 18, nameLength);
		var entityPosition = 18 + nameLength;
		if (entityPosition + 6 + 1 > entityData.Data.Length)
		{
			throw new GenomeFormatException($"Template '{name}' entity data is truncated.");
		}

		entityPosition += 6; // Unknown fixed fields.
		var propertySetCount = entityData.Data[entityPosition++];
		var propertySets = new GenomeObject[propertySetCount];
		for (var index = 0; index < propertySets.Length; index++)
		{
			propertySets[index] = GenomeObject.Parse(entityData.Data, entityPosition);
			entityPosition += propertySets[index].Size;
		}

		if (entityPosition + 4 > entityData.Data.Length)
		{
			throw new GenomeFormatException($"Template '{name}' is missing its child-template count.");
		}

		var childCount = BinaryPrimitives.ReadUInt32LittleEndian(entityData.Data.AsSpan(entityPosition));
		entityPosition += 4;
		if (childCount > 4096)
		{
			throw new GenomeFormatException($"Template '{name}' declares too many child templates ({childCount}).");
		}

		var children = new GenomeTemplate[childCount];
		for (var index = 0; index < children.Length; index++)
		{
			var childObject = GenomeObject.Parse(entityData.Data, entityPosition);
			entityPosition += childObject.Size;
			var childEntityData = childObject.ClassData.FirstOrDefault(data => data.ClassHash == EntityClassHash)
				?? throw new GenomeFormatException($"Template '{name}' child {index} is missing eCEntity data.");
			var childNameLength = childEntityData.Data.Length >= 18
				? BinaryPrimitives.ReadUInt16LittleEndian(childEntityData.Data.AsSpan(16))
				: throw new GenomeFormatException($"Template '{name}' child {index} has truncated entity data.");
			var childName = childNameLength > 0 && 18 + childNameLength <= childEntityData.Data.Length
				? Encoding.Latin1.GetString(childEntityData.Data, 18, childNameLength)
				: $"child{index}";
			children[index] = ParseObject(childName, sourcePath, timestamp, childObject, depth + 1);
		}

		if (entityPosition != entityData.Data.Length)
		{
			throw new GenomeFormatException($"Template '{name}' entity data has trailing bytes.");
		}

		return new GenomeTemplate(
			name,
			entityName,
			guid,
			parentGuid,
			timestamp,
			sourcePath,
			genomeObject,
			propertySets,
			children);
	}

	private static int FindInnerTemplateOffset(byte[] data)
	{
		if (data.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new GenomeFormatException("The template does not start with a GAR5 wrapper.");
		}

		for (var offset = 0; offset <= data.Length - 12; offset++)
		{
			if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) != Gar5Header.Magic)
			{
				continue;
			}

			if (string.Equals(Encoding.Latin1.GetString(data, offset + 8, 4), "GTP0", StringComparison.Ordinal))
			{
				return offset;
			}
		}

		throw new GenomeFormatException("The template has no inner GTP0 header.");
	}
}
