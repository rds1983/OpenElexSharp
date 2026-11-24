using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// Samples a timed transition between two clip poses. The clips currently contain static poses, so
/// this is a pose-to-pose blend; keyframe curves are decoded separately when available.
/// </summary>
public sealed class MotionTransitionSampler
{
	/// <summary>Creates a transition sampler.</summary>
	/// <exception cref="ArgumentNullException">A clip is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is not positive.</exception>
	public MotionTransitionSampler(MotionClip from, MotionClip to, float duration)
	{
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		if (!(duration > 0f) || !float.IsFinite(duration))
		{
			throw new ArgumentOutOfRangeException(nameof(duration));
		}

		From = from;
		To = to;
		Duration = duration;
	}

	/// <summary>Source pose.</summary>
	public MotionClip From { get; }

	/// <summary>Target pose.</summary>
	public MotionClip To { get; }

	/// <summary>Transition duration in seconds.</summary>
	public float Duration { get; }

	/// <summary>Returns the normalized transition progress in <c>[0,1]</c>.</summary>
	public float Normalize(float seconds) =>
		Math.Clamp(seconds / Duration, 0f, 1f);

	/// <summary>Samples a one-shot transition. Returns the target pose at and after the duration.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="boneNames"/> is <see langword="null"/>.</exception>
	public MotionPose Sample(float seconds, IReadOnlyList<string> boneNames) =>
		MotionPose.Blend(From, To, Normalize(seconds), boneNames);

	/// <summary>Samples a ping-pong loop: source at <c>0</c>, target at half duration, source at full duration.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="boneNames"/> is <see langword="null"/>.</exception>
	public MotionPose SampleLoop(float seconds, IReadOnlyList<string> boneNames)
	{
		ArgumentNullException.ThrowIfNull(boneNames);
		var phase = seconds / Duration;
		phase -= MathF.Floor(phase);
		var amount = 1f - MathF.Abs(2f * phase - 1f);
		return MotionPose.Blend(From, To, amount, boneNames);
	}
}
