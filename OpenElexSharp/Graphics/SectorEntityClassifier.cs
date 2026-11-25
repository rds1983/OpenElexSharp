using Microsoft.Xna.Framework;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Classifies scene entities into the runtime visual categories used by T108 toggles. The parser
/// keeps the raw Genome property sets, so the classifier matches class names and a few decoded
/// properties rather than relying on entity names alone.
/// </summary>
public static class SectorEntityClassifier
{
	private const string PointLightClass = "class eCPointLight_PS";
	private const string DecalClass = "class eCDecal2_PS";
	private const string RiverClass = "class eCTerrainRiver_PS";
	private const string SpeedTreeClass = "class eCSpeedTreeStatic_PS";
	private const string MeshClass = "class eCMesh_PS";

	private static readonly string[] LightPrefixes = ["Obj_LS_", "Obj_Dmy_Light", "LightSource"];
	private static readonly string[] DecalPrefixes = ["DC_", "Decal"];
	private static readonly string[] WaterNames =
		["Water", "Lava", "River", "Lake", "Pond", "Ocean", "Flood"];
	private static readonly string[] VegetationNames =
		[
			"Tree", "Bush", "Plant", "Grass", "Fern", "Leaf", "Reed", "Vine", "Flower", "Veg",
			"Cactus", "Crop", "Pumpkin", "Stump", "Branch",
		];

	/// <summary>Classifies one scene entity.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
	public static SectorVisualCategory Classify(ElexSceneEntity entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var classNames = new HashSet<string>(
			entity.PropertySets.Select(set => GenomeNameTable.GetNameOrHash(set.ClassHash)),
			StringComparer.OrdinalIgnoreCase);
		return Classify(entity.Name, entity.MeshFileName, classNames, IsWaterMap(entity));
	}

	/// <summary>
	/// Pure classification used by tests. <paramref name="classNames"/> contains Genome property-set
	/// class names such as <c>class eCPointLight_PS</c>.
	/// </summary>
	public static SectorVisualCategory Classify(
		string? name,
		string? meshName,
		IReadOnlyCollection<string> classNames,
		bool waterMap)
	{
		ArgumentNullException.ThrowIfNull(classNames);
		if (classNames.Contains(DecalClass, StringComparer.OrdinalIgnoreCase) ||
			HasAnyPrefix(name, DecalPrefixes) ||
			HasAnyPrefix(meshName, DecalPrefixes))
		{
			return SectorVisualCategory.Decals;
		}

		if (classNames.Contains(RiverClass, StringComparer.OrdinalIgnoreCase) ||
			waterMap ||
			HasAnyPart(name, WaterNames) ||
			HasAnyPart(meshName, WaterNames))
		{
			return SectorVisualCategory.Water;
		}

		if (classNames.Contains(SpeedTreeClass, StringComparer.OrdinalIgnoreCase) ||
			HasAnyPrefix(name, ["ST_"]) ||
			HasAnyPart(name, VegetationNames) ||
			HasAnyPart(meshName, VegetationNames))
		{
			return SectorVisualCategory.Vegetation;
		}

		if (classNames.Contains(PointLightClass, StringComparer.OrdinalIgnoreCase) ||
			HasAnyPrefix(name, LightPrefixes) ||
			HasAnyPrefix(meshName, LightPrefixes))
		{
			return SectorVisualCategory.Lights;
		}

		return SectorVisualCategory.Other;
	}

	/// <summary>Returns the SpeedTree resource referenced by an entity, or <see langword="null"/>.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
	public static string? GetSpeedTreeName(ElexSceneEntity entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		return FindProperty(entity, SpeedTreeClass, "SpeedTree")?.Value as string;
	}

	/// <summary>Decodes the projection parameters of a decal entity.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
	public static SectorDecalData GetDecalData(ElexSceneEntity entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var material = FindProperty(entity, DecalClass, "Material")?.Value as string;
		return new SectorDecalData(
			material,
			ReadVector(entity, DecalClass, "Extends") ?? new Vector3(250f, 250f, 30f),
			ReadVector(entity, DecalClass, "Offset") ?? Vector3.Zero,
			ReadVector(entity, DecalClass, "Rotation") ?? Vector3.Zero);
	}

	private static bool IsWaterMap(ElexSceneEntity entity)
	{
		var value = FindProperty(entity, MeshClass, "GameMapType");
		return value?.Value is GenomeEnumValue enumValue &&
			string.Equals(
				enumValue.MemberName,
				"eSGameMapType_Water",
				StringComparison.OrdinalIgnoreCase);
	}

	private static GenomeValue? FindProperty(
		ElexSceneEntity entity,
		string className,
		string propertyName)
	{
		foreach (var propertySet in entity.PropertySets)
		{
			if (!string.Equals(
				GenomeNameTable.GetNameOrHash(propertySet.ClassHash),
				className,
				StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			foreach (var property in propertySet.Properties)
			{
				if (string.Equals(
					GenomeNameTable.GetNameOrHash(property.NameHash),
					propertyName,
					StringComparison.Ordinal))
				{
					return GenomePropertyDecoder.Decode(property);
				}
			}
		}

		return null;
	}

	private static Vector3? ReadVector(
		ElexSceneEntity entity,
		string className,
		string propertyName)
	{
		if (FindProperty(entity, className, propertyName)?.Value is not float[] values ||
			values.Length < 3)
		{
			return null;
		}

		return new Vector3(values[0], values[1], values[2]);
	}

	private static bool HasAnyPrefix(string? value, IReadOnlyList<string> prefixes)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		foreach (var prefix in prefixes)
		{
			if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasAnyPart(string? value, IReadOnlyList<string> parts)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		foreach (var part in parts)
		{
			if (value.Contains(part, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
