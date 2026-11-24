using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MotionNetworkTests
{
	[Fact]
	public void Parse_ExtractsReadableNames()
	{
		var data = CreateNetwork("BONE_Root", "TRAJECTORY", "abc", "Male_Spine");

		var network = MotionNetwork.Parse("Test", data);

		Assert.Equal(["BONE_Root", "TRAJECTORY", "Male_Spine"], network.Strings);
		Assert.Equal(["BONE_Root"], network.BoneNames);
		Assert.Empty(network.Clips);
	}

	[Fact]
	public void Parse_ExtractsClipHeaderTracksAndPose()
	{
		var pose = new[]
		{
			Quaternion.Identity,
			Quaternion.Identity,
			Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
		};
		var data = CreateClipNetwork(
			["CharacterWorldSpaceTM", "TRAJECTORY", "BONE_Root"],
			pose);

		var network = MotionNetwork.Parse("Test", data);

		var clip = Assert.Single(network.Clips);
		Assert.Equal(0, clip.Index);
		Assert.Equal(1.3f, clip.Field0);
		Assert.Equal(0.35f, clip.Field1);
		Assert.Equal(44, clip.HeaderOffset);
		Assert.Equal(44 + 0x100, clip.PoseOffset);
		Assert.True(clip.HasPose);
		Assert.Equal(["CharacterWorldSpaceTM", "TRAJECTORY", "BONE_Root"], clip.TrackNames);
		Assert.Equal(3, clip.Pose.Count);
		Assert.Equal(MathF.Cos(MathF.PI / 4), clip.Pose[2].W, 5);
		Assert.Equal(MathF.Sin(MathF.PI / 4), clip.Pose[2].X, 5);
		Assert.Equal(2, clip.FindTrackIndex("Root"));
		Assert.Equal(1, clip.FindTrackIndex("TRAJECTORY"));
		Assert.Equal(-1, clip.FindTrackIndex("Missing"));
	}

	[Fact]
	public void Parse_TableWithoutHeader_UsesDefaultFields()
	{
		var data = CreateClipNetwork(
			["CharacterWorldSpaceTM", "TRAJECTORY", "BONE_Root"],
			[Quaternion.Identity, Quaternion.Identity, Quaternion.Identity],
			includeHeader: false);

		var clip = Assert.Single(MotionNetwork.Parse("Test", data).Clips);

		Assert.Equal(-1, clip.HeaderOffset);
		Assert.Equal(0f, clip.Field0);
		Assert.Equal(0f, clip.Field1);
		Assert.True(clip.HasPose);
	}

	[Fact]
	public void Parse_ClipWithoutPose_IsExposedWithoutRotations()
	{
		var data = CreateClipNetwork(
			["CharacterWorldSpaceTM", "TRAJECTORY", "BONE_Root"],
			pose: null);

		var clip = Assert.Single(MotionNetwork.Parse("Test", data).Clips);

		Assert.False(clip.HasPose);
		Assert.Empty(clip.Pose);
		Assert.Equal(-1, clip.PoseOffset);
	}

	[Fact]
	public void Parse_IgnoresTrailingGar5ResourceTable()
	{
		var data = CreateClipNetwork(
			["CharacterWorldSpaceTM", "TRAJECTORY", "BONE_Root"],
			[Quaternion.Identity, Quaternion.Identity, Quaternion.Identity]);
		var withTrailer = data.Concat("GAR5"u8.ToArray()).Concat(new byte[16]).ToArray();

		var clip = Assert.Single(MotionNetwork.Parse("Test", withTrailer).Clips);

		Assert.True(clip.HasPose);
	}

	[Fact]
	public void Parse_ClassifiesStatesNodesAndTransitions()
	{
		var data = CreateGraphNetwork();

		var network = MotionNetwork.Parse("Test", data);

		Assert.Empty(network.Clips);
		Assert.Equal(["Closed", "Opened"], network.StateNames);
		Assert.Equal(["Walk"], network.NodeNames);
		Assert.Equal(2, network.Transitions.Count);
		Assert.Equal(2, network.StatePaths.Count);
		Assert.Equal("Closed", network.Transitions[0].Source);
		Assert.Equal("Opened", network.Transitions[0].Target);
		Assert.Equal("FractionThroughSource", network.Transitions[1].Condition);
	}

	[Fact]
	public void Parse_ResolvesStatesFromStandaloneStrings()
	{
		var data = CreateTransitionOnlyNetwork();

		var network = MotionNetwork.Parse("Test", data);

		Assert.Equal(["Closed", "Opened"], network.StateNames);
		Assert.Empty(network.NodeNames);
		Assert.Equal(2, network.Transitions.Count);
		Assert.Equal("Closed", network.Transitions[0].Source);
		Assert.Equal("Opened", network.Transitions[0].Target);
	}

	[Fact]
	public void Parse_NoGar5_Throws()
	{
		var data = CreateNetwork("BONE_Root");
		data[0] = (byte)'X';

		Assert.Throws<Gar5FormatException>(() => MotionNetwork.Parse("Test", data));
	}

	[Fact]
	public void Parse_Truncated_Throws()
	{
		Assert.Throws<Gar5FormatException>(() => MotionNetwork.Parse("Test", new byte[8]));
	}

	[Fact]
	public void Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => MotionNetwork.Parse("Test", null!));
		Assert.Throws<ArgumentNullException>(() => MotionNetwork.Parse(null!, []));
	}

	private static byte[] CreateGraphNetwork()
	{
		var data = new byte[44 + 0x300];
		BinaryPrimitives.WriteUInt32LittleEndian(data, Gar5Header.Magic);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x20u);
		WriteTable(data, 44 + 0x60, ["Closed", "Opened", "Walk"]);
		WriteTable(
			data,
			44 + 0x140,
			[
				"Closed_Opened|ConditionGroup|MessageCondition",
				"Opened_Closed|ConditionGroup|FractionThroughSource",
			]);
		return data;
	}

	private static byte[] CreateTransitionOnlyNetwork()
	{
		var data = new byte[44 + 0x300];
		BinaryPrimitives.WriteUInt32LittleEndian(data, Gar5Header.Magic);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x20u);
		var states = Encoding.Latin1.GetBytes("Closed\0Opened\0");
		states.CopyTo(data.AsSpan(44 + 0x60));
		WriteTable(
			data,
			44 + 0x140,
			[
				"Closed_Opened|ConditionGroup|MessageCondition",
				"Opened_Closed|ConditionGroup|FractionThroughSource",
			]);
		return data;
	}

	private static void WriteTable(byte[] data, int nameStart, string[] names)
	{
		var offsetsStart = nameStart - names.Length * 4;
		uint offset = 0;
		var nameOffset = nameStart;
		for (var index = 0; index < names.Length; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offsetsStart + index * 4), offset);
			var bytes = Encoding.Latin1.GetBytes(names[index]);
			bytes.CopyTo(data.AsSpan(nameOffset));
			nameOffset += bytes.Length;
			data[nameOffset++] = 0;
			offset += (uint)(bytes.Length + 1);
		}
	}

	private static byte[] CreateNetwork(params string[] values)
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		writer.Write(Gar5Header.Magic);
		writer.Write(0x20u);
		writer.Write(new byte[36]);
		foreach (var value in values)
		{
			writer.Write((byte)0);
			writer.Write(Encoding.Latin1.GetBytes(value));
			writer.Write((byte)0);
		}

		return output.ToArray();
	}

	private static byte[] CreateClipNetwork(
		string[] trackNames,
		Quaternion[]? pose,
		bool includeHeader = true)
	{
		const int payload = 44;
		const int nameStart = payload + 0x60;
		const int poseStart = payload + 0x100;
		var data = new byte[payload + 0x200];
		BinaryPrimitives.WriteUInt32LittleEndian(data, Gar5Header.Magic);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x20u);
		if (includeHeader)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(payload), 1);
			BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(payload + 4), 1.3f);
			BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(payload + 0x10), 0.35f);
			ReadOnlySpan<byte> marker = [0x0a, 0xd7, 0x23, 0x3c, 0x00, 0x00, 0x80, 0x3e];
			marker.CopyTo(data.AsSpan(payload + 0x14));
		}

		var offsetsStart = nameStart - trackNames.Length * 4;
		uint offset = 0;
		var nameOffset = nameStart;
		for (var index = 0; index < trackNames.Length; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offsetsStart + index * 4), offset);
			var bytes = Encoding.Latin1.GetBytes(trackNames[index]);
			bytes.CopyTo(data.AsSpan(nameOffset));
			nameOffset += bytes.Length;
			data[nameOffset++] = 0;
			offset += (uint)(bytes.Length + 1);
		}

		if (pose is not null)
		{
			for (var index = 0; index < pose.Length; index++)
			{
				var value = pose[index];
				var offsetBytes = poseStart + index * 16;
				BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offsetBytes), value.X);
				BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offsetBytes + 4), value.Y);
				BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offsetBytes + 8), value.Z);
				BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offsetBytes + 12), value.W);
			}
		}

		return data;
	}
}
