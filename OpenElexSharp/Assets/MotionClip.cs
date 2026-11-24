using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// One animation clip decoded from a <c>net</c> motion network: a track-name table and the
/// quaternion pose stored with it.
/// </summary>
public sealed class MotionClip
{
	/// <summary>Size of the clip header before the track-name table.</summary>
	public const int HeaderSize = 0x20;

	/// <summary>Fixed byte sequence at header offset <c>0x14</c>.</summary>
	internal static ReadOnlySpan<byte> HeaderMarker => [0x0a, 0xd7, 0x23, 0x3c, 0x00, 0x00, 0x80, 0x3e];

	/// <summary>Creates a clip. Intended for tests and parsers.</summary>
	/// <exception cref="ArgumentNullException">A list argument is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The pose length does not match the track count.</exception>
	public MotionClip(
		int index,
		float field0,
		float field1,
		IReadOnlyList<string> trackNames,
		IReadOnlyList<Quaternion> pose,
		int headerOffset,
		int nameTableOffset,
		int poseOffset)
	{
		ArgumentNullException.ThrowIfNull(trackNames);
		ArgumentNullException.ThrowIfNull(pose);
		if (trackNames.Count > 0 && pose.Count != 0 && pose.Count != trackNames.Count)
		{
			throw new ArgumentException("The pose must have one quaternion per track.", nameof(pose));
		}

		Index = index;
		Field0 = field0;
		Field1 = field1;
		TrackNames = trackNames;
		Pose = pose;
		HeaderOffset = headerOffset;
		NameTableOffset = nameTableOffset;
		PoseOffset = poseOffset;
	}

	/// <summary>Zero-based clip index in the network.</summary>
	public int Index { get; }

	/// <summary>First floating-point header value (header offset <c>0x04</c>).</summary>
	public float Field0 { get; }

	/// <summary>Second floating-point header value (header offset <c>0x10</c>).</summary>
	public float Field1 { get; }

	/// <summary>Track names in pose order.</summary>
	public IReadOnlyList<string> TrackNames { get; }

	/// <summary>Per-track quaternion pose; empty when no pose array was found.</summary>
	public IReadOnlyList<Quaternion> Pose { get; }

	/// <summary>Absolute resource offset of the clip header, or <c>-1</c> when inferred from a table.</summary>
	public int HeaderOffset { get; }

	/// <summary>Absolute resource offset of the first track-name string.</summary>
	public int NameTableOffset { get; }

	/// <summary>Absolute resource offset of the pose array, or <c>-1</c>.</summary>
	public int PoseOffset { get; }

	/// <summary>Whether the clip has one pose quaternion per track.</summary>
	public bool HasPose => TrackNames.Count > 0 && Pose.Count == TrackNames.Count;

	/// <summary>
	/// Finds a track by bone name. Exact names win; otherwise a <c>BONE_</c>/<c>bone_</c> prefix on
	/// the track is ignored. Returns <c>-1</c> when the track is absent.
	/// </summary>
	public int FindTrackIndex(string boneName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(boneName);

		for (var index = 0; index < TrackNames.Count; index++)
		{
			if (string.Equals(TrackNames[index], boneName, StringComparison.OrdinalIgnoreCase))
			{
				return index;
			}
		}

		for (var index = 0; index < TrackNames.Count; index++)
		{
			var name = TrackNames[index];
			if (name.StartsWith("BONE_", StringComparison.OrdinalIgnoreCase))
			{
				name = name[5..];
			}

			if (string.Equals(name, boneName, StringComparison.OrdinalIgnoreCase))
			{
				return index;
			}
		}

		return -1;
	}
}
