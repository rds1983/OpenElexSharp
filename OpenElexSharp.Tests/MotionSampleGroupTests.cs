using System.Numerics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionSampleGroupTests
{
	[Fact]
	public void GroupByDuration_CombinesTracksWithSharedDuration()
	{
		var tracks = new[]
		{
			CreateTrack(0, 2f, Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)),
			CreateTrack(1, 2f, Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)),
		};

		var groups = MotionSampleGroup.GroupByDuration(tracks);

		var group = Assert.Single(groups);
		Assert.Equal(2, group.Tracks.Count);
		Assert.Equal(1, group.MaxTrackIndex);
	}

	[Fact]
	public void SamplePose_MapsTrackIndicesAndInterpolates()
	{
		var group = new[]
		{
			CreateTrack(0, 2f, Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)),
			CreateTrack(2, 2f, Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
		};
		var sampled = Assert.Single(MotionSampleGroup.GroupByDuration(group));

		var pose = sampled.SamplePose(1f, ["A", "B", "C"]);

		Assert.Equal(3, pose.Count);
		AssertQuaternion(
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4), pose.Rotations[0]);
		AssertQuaternion(Quaternion.Identity, pose.Rotations[1]);
		AssertQuaternion(
			Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4), pose.Rotations[2]);
	}

	[Fact]
	public void GroupByDuration_SeparatesDifferentDurations()
	{
		var groups = MotionSampleGroup.GroupByDuration(
		[
			CreateTrack(0, 1f, Quaternion.Identity, Quaternion.Identity),
			CreateTrack(1, 2f, Quaternion.Identity, Quaternion.Identity),
		]);

		Assert.Equal(2, groups.Count);
	}

	[Fact]
	public void GroupByDuration_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MotionSampleGroup.GroupByDuration(null!));
	}

	private static MotionSampleTrack CreateTrack(
		int index,
		float duration,
		Quaternion first,
		Quaternion last) =>
		new(index, 32, 2, duration, 60f, index, index, 0,
		[
			new MotionSample(Vector3.Zero, first),
			new MotionSample(Vector3.Zero, last),
		]);

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
