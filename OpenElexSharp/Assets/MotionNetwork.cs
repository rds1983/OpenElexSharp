using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// A <c>net</c> motion-network resource (<c>class eCMotionNetworkDefResource2</c>). The payload is a
/// Morpheme network definition; the current parser exposes the readable node, track, bone, condition
/// and event names. Keyframe and graph decoding is open work.
/// </summary>
public sealed class MotionNetwork
{
	private const int Gar5WrapperSize = 44;
	private const int MinStringLength = 4;
	private const int MaxStrings = 100_000;

	private MotionNetwork(string name, IReadOnlyList<string> strings, int payloadOffset)
	{
		Name = name;
		Strings = strings;
		PayloadOffset = payloadOffset;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Readable names found in the network payload, in file order.</summary>
	public IReadOnlyList<string> Strings { get; }

	/// <summary>Offset of the GAR5 payload.</summary>
	public int PayloadOffset { get; }

	/// <summary>Bone names (entries starting with <c>BONE_</c>).</summary>
	public IReadOnlyList<string> BoneNames =>
		Strings.Where(value => value.StartsWith("BONE_", StringComparison.Ordinal)).ToArray();

	/// <summary>Parses a <c>net</c> resource wrapped in a GAR5 header.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="Gar5FormatException">The payload is malformed.</exception>
	public static MotionNetwork Parse(string name, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + 4 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new Gar5FormatException($"Motion network '{name}' has no GAR5 payload.");
		}

		var strings = new List<string>();
		var position = Gar5WrapperSize;
		while (position < data.Length && strings.Count < MaxStrings)
		{
			if (!IsPrintableByte(data[position]))
			{
				position++;
				continue;
			}

			var start = position;
			while (position < data.Length && IsPrintableByte(data[position]))
			{
				position++;
			}

			var length = position - start;
			if (length >= MinStringLength)
			{
				strings.Add(Encoding.Latin1.GetString(data, start, length));
			}
		}

		return new MotionNetwork(name, strings, Gar5WrapperSize);
	}

	private static bool IsPrintableByte(byte value) => value >= 0x20 && value < 0x7F;
}
