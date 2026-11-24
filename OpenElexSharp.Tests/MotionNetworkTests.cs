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
}
