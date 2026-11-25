using DigitalRiseModel;
using Microsoft.Xna.Framework;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class DrAnimationClipsTests
{
	[Fact]
	public void CreateBindPoseClip_EmitsOneChannelPerBone()
	{
		var model = CreateModel();
		var clip = DrAnimationClips.CreateBindPoseClip(model, "bind");

		Assert.Equal("bind", clip.Name);
		Assert.Equal(TimeSpan.FromSeconds(1), clip.Duration);
		Assert.Equal(model.Bones.Length, clip.Channels.Length);
		for (var index = 0; index < model.Bones.Length; index++)
		{
			var channel = clip.Channels[index];
			Assert.Equal(model.Bones[index].Index, channel.BoneIndex);
			Assert.Equal(2, channel.Keyframes.Length);
			Assert.Equal(TimeSpan.Zero, channel.Keyframes[0].Time);
			Assert.Equal(clip.Duration, channel.Keyframes[1].Time);
			Assert.Equal(
				model.Bones[index].DefaultPose.Translation,
				channel.Keyframes[0].Pose.Translation);
			Assert.Equal(
				model.Bones[index].DefaultPose.Rotation,
				channel.Keyframes[1].Pose.Rotation);
			Assert.Equal(
				model.Bones[index].DefaultPose.Scale,
				channel.Keyframes[1].Pose.Scale);
		}
	}

	[Fact]
	public void CreateBindPoseClip_UsesDefaultName()
	{
		var clip = DrAnimationClips.CreateBindPoseClip(CreateModel());

		Assert.Equal("bind-pose", clip.Name);
	}

	[Fact]
	public void CreateBindPoseClip_RejectsNullModel()
	{
		Assert.Throws<ArgumentNullException>(() => DrAnimationClips.CreateBindPoseClip(null!));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void CreateBindPoseClip_RejectsEmptyName(string name)
	{
		Assert.Throws<ArgumentException>(() => DrAnimationClips.CreateBindPoseClip(CreateModel(), name));
	}

	private static DrModel CreateModel()
	{
		var root = new DrModelBone("root")
		{
			DefaultPose = new SrtTransform(
				new Vector3(1f, 2f, 3f),
				Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f),
				Vector3.One),
		};
		var child = new DrModelBone("child")
		{
			DefaultPose = new SrtTransform(
				new Vector3(0f, 1f, 0f),
				Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.25f),
				new Vector3(2f, 2f, 2f)),
		};
		root.Children = [child];
		return new DrModel(root);
	}
}
