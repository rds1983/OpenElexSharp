using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionPoseTests
{
	[Fact]
	public void FromClip_MapsBonesAndStripsPrefix()
	{
		var clip = CreateClip(
			("CharacterWorldSpaceTM", Quaternion.Identity),
			("BONE_Root", Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f)),
			("BONE_Lid", Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.25f)));

		var pose = MotionPose.FromClip(clip, ["Root", "Lid", "Missing"]);

		Assert.Equal(3, pose.Count);
		AssertQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f), pose.Rotations[0]);
		AssertQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.25f), pose.Rotations[1]);
		AssertQuaternion(Quaternion.Identity, pose.Rotations[2]);
	}

	[Fact]
	public void Blend_InterpolatesBetweenClips()
	{
		var from = CreateClip(("BONE_Root", Quaternion.Identity));
		var to = CreateClip(
			("BONE_Root", Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)));

		var pose = MotionPose.Blend(from, to, 0.5f, ["Root"]);

		var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
		AssertQuaternion(expected, pose.Rotations[0]);
	}

	[Fact]
	public void Blend_ClampsTime()
	{
		var from = CreateClip(("BONE_Root", Quaternion.Identity));
		var to = CreateClip(
			("BONE_Root", Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)));

		var before = MotionPose.Blend(from, to, -1f, ["Root"]);
		var after = MotionPose.Blend(from, to, 2f, ["Root"]);

		AssertQuaternion(Quaternion.Identity, before.Rotations[0]);
		AssertQuaternion(
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI), after.Rotations[0]);
	}

	[Fact]
	public void FromClip_WithoutPose_UsesIdentity()
	{
		var clip = new MotionClip(
			0, 0, 0, ["BONE_Root"], [], -1, 0, -1);

		var pose = MotionPose.FromClip(clip, ["Root"]);

		AssertQuaternion(Quaternion.Identity, pose.Rotations[0]);
	}

	[Fact]
	public void SkeletonPose_ComposesParentRotation()
	{
		var pose = new SkeletonPose(
			[Vector3.Zero, new Vector3(1, 0, 0)],
			[Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Quaternion.Identity],
			[-1, 0]);

		var world = pose.ComputeWorldMatrices();

		Assert.Equal(2, world.Length);
		Assert.Equal(0f, world[1].Translation.X, 5);
		Assert.Equal(1f, world[1].Translation.Y, 5);
		Assert.Equal(0f, world[1].Translation.Z, 5);
	}

	[Fact]
	public void SkeletonPose_InvalidParent_Throws()
	{
		Assert.Throws<ArgumentException>(() => new SkeletonPose(
			[Vector3.Zero],
			[Quaternion.Identity],
			[0]));
	}

	private static MotionClip CreateClip(params (string Name, Quaternion Rotation)[] tracks)
	{
		var names = tracks.Select(track => track.Name).ToArray();
		var pose = tracks.Select(track => track.Rotation).ToArray();
		return new MotionClip(0, 0, 0, names, pose, -1, 0, 0);
	}

	private static void AssertQuaternion(Quaternion expected, Quaternion actual)
	{
		var sign = expected.X * actual.X + expected.Y * actual.Y +
			expected.Z * actual.Z + expected.W * actual.W < 0 ? -1f : 1f;
		Assert.Equal(expected.X, sign * actual.X, 5);
		Assert.Equal(expected.Y, sign * actual.Y, 5);
		Assert.Equal(expected.Z, sign * actual.Z, 5);
		Assert.Equal(expected.W, sign * actual.W, 5);
	}
}
