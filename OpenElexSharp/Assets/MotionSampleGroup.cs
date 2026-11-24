using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// A group of sample tracks that share a duration, treated as one sampled animation. The group can
/// produce a retargeted pose at a given time.
/// </summary>
public sealed class MotionSampleGroup
{
	private MotionSampleGroup(float duration, IReadOnlyList<MotionSampleTrack> tracks)
	{
		Duration = duration;
		Tracks = tracks;
	}

	/// <summary>Group duration in seconds.</summary>
	public float Duration { get; }

	/// <summary>Tracks in the group, ordered by track index.</summary>
	public IReadOnlyList<MotionSampleTrack> Tracks { get; }

	/// <summary>Largest track index in the group.</summary>
	public int MaxTrackIndex => Tracks.Count == 0 ? -1 : Tracks.Max(track => track.TrackIndex);

	/// <summary>
	/// Groups tracks by duration (rounded to milliseconds). Tracks of one animation share the same
	/// duration in the observed data.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="tracks"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<MotionSampleGroup> GroupByDuration(
		IReadOnlyList<MotionSampleTrack> tracks)
	{
		ArgumentNullException.ThrowIfNull(tracks);
		return tracks
			.GroupBy(track => MathF.Round(track.Duration * 1000f) / 1000f)
			.OrderBy(group => group.Key)
			.Select(group => new MotionSampleGroup(
				group.Key,
				group.OrderBy(track => track.TrackIndex).ToArray()))
			.ToArray();
	}

	/// <summary>
	/// Samples the group into a retargeted pose. Track indices are mapped to <paramref name="boneNames"/>;
	/// tracks outside the list are ignored.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="boneNames"/> is <see langword="null"/>.</exception>
	public MotionPose SamplePose(float seconds, IReadOnlyList<string> boneNames)
	{
		ArgumentNullException.ThrowIfNull(boneNames);
		var rotations = new Quaternion[boneNames.Count];
		for (var index = 0; index < rotations.Length; index++)
		{
			rotations[index] = Quaternion.Identity;
		}

		foreach (var track in Tracks)
		{
			if (track.TrackIndex < 0 || track.TrackIndex >= rotations.Length)
			{
				continue;
			}

			rotations[track.TrackIndex] = track.SampleAt(seconds).Rotation;
		}

		return new MotionPose(rotations);
	}
}
