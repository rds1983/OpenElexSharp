using System.Numerics;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class SkeletonTests
{
	[Fact]
	public void TryParse_HeadStyleTail_ReadsBonesNamesAndParents()
	{
		var data = CreateTail(boneCount: 2, ["Male_Neck", "Male_Head"], parentCount: 1);

		var skeleton = Skeleton.TryParse(data, 0);

		Assert.NotNull(skeleton);
		Assert.Equal(2, skeleton.Bones.Count);
		Assert.Equal(["Male_Neck", "Male_Head"], skeleton.Names);
		Assert.Equal([0u], skeleton.ParentIndices);
		Assert.Equal(2, skeleton.BoneCount);
		Assert.All(skeleton.Bones, bone => Assert.InRange(bone.Rotation.Length(), 0.99f, 1.01f));
		Assert.Equal(Djb2Hash.ComputeCaseSensitive("Male_Neck"), skeleton.NameHashes[0]);
	}

	[Fact]
	public void TryParse_NamesOnlyTail_ReadsHierarchy()
	{
		var data = CreateTail(boneCount: 0, ["Root", "Spine", "Head"], parentCount: 2);

		var skeleton = Skeleton.TryParse(data, 0);

		Assert.NotNull(skeleton);
		Assert.Empty(skeleton.Bones);
		Assert.Equal(3, skeleton.Names.Count);
		Assert.Equal(3, skeleton.BoneCount);
		Assert.Equal([0u, 1u], skeleton.ParentIndices);
		Assert.Equal(2, skeleton.NameOffsets.Count);
	}

	[Fact]
	public void TryParse_InvalidParentIndices_AreDiscarded()
	{
		var data = CreateTail(boneCount: 0, ["Root", "Spine", "Head"], parentCount: 2, invalidParent: 99);

		var skeleton = Skeleton.TryParse(data, 0);

		Assert.NotNull(skeleton);
		Assert.Empty(skeleton.ParentIndices);
	}

	[Fact]
	public void TryParse_NoSeparator_ReturnsNull()
	{
		var data = new byte[Skeleton.HeaderSize + 64];

		Assert.Null(Skeleton.TryParse(data, 0));
	}

	[Fact]
	public void TryParse_TruncatedTail_ReturnsNull()
	{
		Assert.Null(Skeleton.TryParse([], 0));
		Assert.Null(Skeleton.TryParse(new byte[8], 4));
	}

	[Fact]
	public void TryParse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => Skeleton.TryParse(null!, 0));
	}

	private static byte[] CreateTail(
		int boneCount,
		string[] names,
		int parentCount,
		uint? invalidParent = null)
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		for (var index = 0; index < Skeleton.HeaderSize / 4; index++)
		{
			writer.Write(0f);
		}

		for (var index = 0; index < boneCount; index++)
		{
			writer.Write((float)index);
			writer.Write(0f);
			writer.Write(0f);
			writer.Write(0f);
			writer.Write(0f);
			writer.Write(0f);
			writer.Write(1f);
		}

		writer.Write(Skeleton.Separator);
		for (var index = 0; index < parentCount; index++)
		{
			writer.Write(invalidParent ?? (uint)(index % Math.Max(1, boneCount > 0 ? boneCount : names.Length)));
		}

		var offset = names[0].Length + 1;
		for (var index = 1; index < names.Length; index++)
		{
			writer.Write((uint)offset);
			offset += names[index].Length + 1;
		}

		foreach (var name in names)
		{
			writer.Write(Djb2Hash.ComputeCaseSensitive(name));
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
