using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// A retargeted animation pose: one local rotation per skeleton bone, sampled from a
/// <see cref="MotionClip"/> or blended between two clips.
/// </summary>
public sealed class MotionPose
{
	/// <summary>Creates a pose from bone-local rotations.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="rotations"/> is <see langword="null"/>.</exception>
	public MotionPose(IReadOnlyList<Quaternion> rotations)
	{
		ArgumentNullException.ThrowIfNull(rotations);
		Rotations = rotations;
	}

	/// <summary>Bone-local rotations in skeleton order.</summary>
	public IReadOnlyList<Quaternion> Rotations { get; }

	/// <summary>Number of bone rotations.</summary>
	public int Count => Rotations.Count;

	/// <summary>
	/// Samples one clip into local rotations for the supplied skeleton bone names. Bones without a
	/// matching track receive the identity rotation.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static MotionPose FromClip(MotionClip clip, IReadOnlyList<string> boneNames)
	{
		ArgumentNullException.ThrowIfNull(clip);
		ArgumentNullException.ThrowIfNull(boneNames);
		return new MotionPose(Retarget(clip, boneNames));
	}

	/// <summary>
	/// Blends two clips at <paramref name="time"/> in <c>[0,1]</c> using quaternion slerp. At
	/// <c>0</c> the result equals <paramref name="from"/>; at <c>1</c> it equals
	/// <paramref name="to"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static MotionPose Blend(
		MotionClip from,
		MotionClip to,
		float time,
		IReadOnlyList<string> boneNames)
	{
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		ArgumentNullException.ThrowIfNull(boneNames);
		var start = Retarget(from, boneNames);
		var end = Retarget(to, boneNames);
		var rotations = new Quaternion[start.Length];
		var amount = Math.Clamp(time, 0f, 1f);
		for (var index = 0; index < rotations.Length; index++)
		{
			rotations[index] = Quaternion.Slerp(start[index], end[index], amount);
		}

		return new MotionPose(rotations);
	}

	/// <summary>Blends two already-retargeted poses.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The poses have different bone counts.</exception>
	public static MotionPose Blend(MotionPose from, MotionPose to, float time)
	{
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		if (from.Count != to.Count)
		{
			throw new ArgumentException("The poses must have the same bone count.", nameof(to));
		}

		var rotations = new Quaternion[from.Count];
		var amount = Math.Clamp(time, 0f, 1f);
		for (var index = 0; index < rotations.Length; index++)
		{
			rotations[index] = Quaternion.Slerp(from.Rotations[index], to.Rotations[index], amount);
		}

		return new MotionPose(rotations);
	}

	private static Quaternion[] Retarget(MotionClip clip, IReadOnlyList<string> boneNames)
	{
		var rotations = new Quaternion[boneNames.Count];
		for (var bone = 0; bone < rotations.Length; bone++)
		{
			var track = clip.FindTrackIndex(boneNames[bone]);
			rotations[bone] = track >= 0 && clip.HasPose ? clip.Pose[track] : Quaternion.Identity;
		}

		return rotations;
	}
}
