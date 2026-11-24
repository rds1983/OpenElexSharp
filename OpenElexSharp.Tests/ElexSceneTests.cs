using System.Numerics;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexSceneTests
{
	private const uint MeshClassHash = 0x1CB652B2;
	private const uint ItemClassHash = 0xF3C909F6;
	private const uint ItemInventoryPropertyHash = 0xBE2D6DC2;
	private const uint MotionNetworkProxyTypeHash = 0xD0D289FF;

	[Fact]
	public void Parse_ReadsEntityGraph()
	{
		var childGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
		var templateGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
		var child = BuildEntity(
			"Child",
			childGuid,
			Matrix4x4.CreateTranslation(1, 2, 3),
			[
				Gec2TestBuilder.PropertySet(
					MeshClassHash,
					2,
					(ElexScene.StringTypeHash, ElexScene.MeshFileNamePropertyHash,
						Gec2TestBuilder.String("Mesh_A"))),
				Gec2TestBuilder.PropertySet(
					ItemClassHash,
					2,
					(ElexScene.TemplateEntityProxyTypeHash, ItemInventoryPropertyHash,
						templateGuid.ToByteArray())),
			],
			[]);
		var root = BuildEntity("", Guid.Empty, Matrix4x4.Identity, [], [child]);
		var data = BuildScene(root);

		var scene = ElexScene.Parse("TestSector", "test.rom", data);

		Assert.Equal("TestSector", scene.Name);
		Assert.Equal(2, scene.Entities.Count);
		Assert.Equal(1, scene.MeshEntityCount);
		var rootEntity = scene.Root;
		Assert.Equal(string.Empty, rootEntity.Name);
		Assert.Equal(0, rootEntity.Depth);
		var childEntity = Assert.Single(rootEntity.Children);
		Assert.Equal(1, childEntity.Depth);
		Assert.Equal("Child", childEntity.Name);
		Assert.Equal(childGuid, childEntity.EntityGuid);
		Assert.Equal(new Vector3(1, 2, 3), childEntity.WorldPosition);
		Assert.Equal("Mesh_A", childEntity.MeshFileName);
		Assert.Equal(templateGuid, childEntity.TemplateGuid);
		Assert.Contains(
			childEntity.References,
			reference => reference.Property == "ItemInventory" &&
				reference.ReferenceGuid == templateGuid &&
				reference.TypeName == "class eCTemplateEntityProxy");
	}

	[Fact]
	public void Parse_ComposesParentAndChildTransforms()
	{
		var child = BuildEntity(
			"Child",
			Guid.NewGuid(),
			Matrix4x4.CreateTranslation(0, 5, 0),
			[],
			[]);
		var root = BuildEntity(
			"Root",
			Guid.NewGuid(),
			Matrix4x4.CreateTranslation(10, 0, 0),
			[],
			[child]);

		var scene = ElexScene.Parse("Nested", "test.rom", BuildScene(root));

		var childEntity = scene.Entities[1];
		Assert.Equal(new Vector3(10, 5, 0), childEntity.WorldPosition);
		Assert.Equal(Quaternion.Identity, childEntity.WorldRotation);
		Assert.Equal(Vector3.One, childEntity.WorldScale);
	}

	[Fact]
	public void Parse_ReadsNetworkResource()
	{
		var child = BuildEntity(
			"Gregor",
			Guid.NewGuid(),
			Matrix4x4.Identity,
			[
				Gec2TestBuilder.PropertySet(
					MotionNetworkProxyTypeHash,
					1,
					(MotionNetworkProxyTypeHash, ElexScene.NetworkResourcePropertyHash,
						Gec2TestBuilder.String("Male"))),
			],
			[]);
		var root = BuildEntity("", Guid.Empty, Matrix4x4.Identity, [], [child]);

		var scene = ElexScene.Parse("NPCs", "test.rom", BuildScene(root));

		Assert.Equal("Male", scene.Entities[1].NetworkResource);
		Assert.Contains(
			scene.Entities[1].References,
			reference => reference.Resource == "Male");
	}

	[Fact]
	public void Parse_SanitizesNonFiniteTransform()
	{
		var matrix = Matrix4x4.Identity;
		matrix.M41 = float.NaN;
		matrix.M42 = float.PositiveInfinity;
		var entity = BuildEntity("Decal", Guid.NewGuid(), matrix, [], []);

		var scene = ElexScene.Parse("Decals", "test.rom", BuildScene(entity));

		var decal = Assert.Single(scene.Entities);
		Assert.False(decal.IsTransformFinite);
		Assert.True(float.IsFinite(decal.WorldPosition.X));
		Assert.True(float.IsFinite(decal.WorldPosition.Y));
		Assert.Equal(0, decal.WorldPosition.Z);
	}

	[Fact]
	public void Parse_RejectsWrongLayerClass()
	{
		var layer = Gec2TestBuilder.Object(0x12345678, 1, []);

		Assert.Throws<GenomeFormatException>(() =>
			ElexScene.Parse("Bad", "test.rom", Gec2TestBuilder.Wrap(layer, "Bad")));
	}

	[Fact]
	public void Parse_RejectsTruncatedPayload()
	{
		var data = BuildScene(BuildEntity("", Guid.Empty, Matrix4x4.Identity, [], []));

		Assert.Throws<GenomeFormatException>(() =>
			ElexScene.Parse("Bad", "test.rom", data[..^1]));
	}

	[Fact]
	public void Parse_RejectsTooManyChildren()
	{
		var entity = BuildEntity(
			"",
			Guid.Empty,
			Matrix4x4.Identity,
			[],
			[],
			childCount: 200_000);

		Assert.Throws<GenomeFormatException>(() =>
			ElexScene.Parse("Bad", "test.rom", BuildScene(entity)));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => ElexScene.Parse("", "test.rom", []));
		Assert.Throws<ArgumentException>(() => ElexScene.Parse("Test", "", []));
		Assert.Throws<ArgumentNullException>(() => ElexScene.Parse("Test", "test.rom", null!));
	}

	private static byte[] BuildScene(byte[] rootEntity)
	{
		var sceneData = new List<byte>();
		WriteUInt32(sceneData, 1);
		sceneData.AddRange(rootEntity);
		var sceneObject = Gec2TestBuilder.Object(
			ElexScene.SceneClassHash,
			2,
			[(ElexScene.SceneClassHash, 2, [.. sceneData])]);
		var layerData = new List<byte> { 0xA9, 0x31, 0xDA, 0x34, 0, 0, 0, 0 };
		layerData.AddRange(sceneObject);
		var layer = Gec2TestBuilder.Object(
			ElexScene.LayerClassHash,
			1,
			[(ElexScene.LayerClassHash, 1, [.. layerData])]);
		return Gec2TestBuilder.Wrap(layer, "TestSector");
	}

	private static byte[] BuildEntity(
		string name,
		Guid guid,
		Matrix4x4 localTransform,
		byte[][] propertySets,
		byte[][] children,
		uint? childCount = null)
	{
		var entityData = new List<byte>();
		entityData.AddRange(guid.ToByteArray());
		var nameBytes = Encoding.Latin1.GetBytes(name);
		WriteUInt16(entityData, (ushort)nameBytes.Length);
		entityData.AddRange(nameBytes);
		entityData.AddRange([1, 2, 3, 4, 5, 6]);
		entityData.Add((byte)propertySets.Length);
		foreach (var propertySet in propertySets)
		{
			entityData.AddRange(propertySet);
		}

		WriteUInt32(entityData, childCount ?? (uint)children.Length);
		foreach (var child in children)
		{
			entityData.AddRange(child);
		}

		return Gec2TestBuilder.Object(
			ElexScene.DynamicEntityClassHash,
			9,
			[
				(ElexScene.DynamicEntityClassHash, 9, Gec2TestBuilder.Matrix(localTransform)),
				(ElexScene.EntityClassHash, 4, [.. entityData]),
			]);
	}

	private static void WriteUInt16(List<byte> data, ushort value)
	{
		data.Add((byte)value);
		data.Add((byte)(value >> 8));
	}

	private static void WriteUInt32(List<byte> data, uint value)
	{
		data.Add((byte)value);
		data.Add((byte)(value >> 8));
		data.Add((byte)(value >> 16));
		data.Add((byte)(value >> 24));
	}
}
