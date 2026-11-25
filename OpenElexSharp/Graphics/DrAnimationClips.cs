using DigitalRiseModel;
using DigitalRiseModel.Animation;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Builds DigitalRiseModel animation clips from poses that the ELEX asset layer can already produce.
/// The Morpheme keyframe tracks are still compressed, so the only clip available today holds the
/// model's bind pose; real tracks can be added here when their codec is decoded.
/// </summary>
public static class DrAnimationClips
{
	/// <summary>
	/// Builds a one-second clip that holds every model bone at its bind-pose local transform.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
	public static AnimationClip CreateBindPoseClip(DrModel model, string name = "bind-pose")
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var duration = TimeSpan.FromSeconds(1);
		var channels = new AnimationChannel[model.Bones.Length];
		for (var index = 0; index < channels.Length; index++)
		{
			var pose = model.Bones[index].DefaultPose;
			channels[index] = new AnimationChannel(
				index,
				[
					new AnimationChannelKeyframe(TimeSpan.Zero, pose),
					new AnimationChannelKeyframe(duration, pose),
				]);
		}

		return new AnimationClip(name, duration, channels);
	}
}
