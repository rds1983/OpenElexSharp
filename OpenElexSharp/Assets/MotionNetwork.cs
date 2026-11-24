using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// A <c>net</c> motion-network resource (<c>class eCMotionNetworkDefResource2</c>). The payload is a
/// Morpheme network definition; the parser exposes readable node, track, bone, condition and event
/// names, decoded clip poses and name-derived transitions. Binary graph edges and keyframe curves
/// are not decoded yet.
/// </summary>
public sealed class MotionNetwork
{
	private const int Gar5WrapperSize = 44;
	private const int MinStringLength = 4;
	private const int MaxStrings = 100_000;
	private const int MaxTransitions = 20_000;
	private const int MaxStatePaths = 10_000;
	private const int MaxTracks = 4096;
	private const int MaxNameLength = 512;
	private const int MaxPoseSearch = 0x20_000;

	private MotionNetwork(
		string name,
		IReadOnlyList<string> strings,
		IReadOnlyList<MotionClip> clips,
		IReadOnlyList<MotionTransition> transitions,
		IReadOnlyList<MotionStatePath> statePaths,
		IReadOnlyList<MotionGraphNode> graphNodes,
		IReadOnlyList<MotionQuaternionRun> quantizedQuaternionRuns,
		IReadOnlyList<MotionSampleTrack> sampleTracks,
		IReadOnlyList<MotionSampleGroup> sampleGroups,
		IReadOnlyList<string> stateNames,
		IReadOnlyList<string> nodeNames,
		int payloadOffset)
	{
		Name = name;
		Strings = strings;
		Clips = clips;
		Transitions = transitions;
		StatePaths = statePaths;
		GraphNodes = graphNodes;
		QuantizedQuaternionRuns = quantizedQuaternionRuns;
		SampleTracks = sampleTracks;
		SampleGroups = sampleGroups;
		StateNames = stateNames;
		NodeNames = nodeNames;
		PayloadOffset = payloadOffset;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Readable names found in the network payload, in file order.</summary>
	public IReadOnlyList<string> Strings { get; }

	/// <summary>Decoded animation clips with track names and pose quaternions.</summary>
	public IReadOnlyList<MotionClip> Clips { get; }

	/// <summary>Transitions decoded from <c>Source_Target|ConditionGroup|Condition</c> names.</summary>
	public IReadOnlyList<MotionTransition> Transitions { get; }

	/// <summary>Hierarchical state-machine paths decoded from <c>|</c>-separated names.</summary>
	public IReadOnlyList<MotionStatePath> StatePaths { get; }

	/// <summary>Candidate binary state-graph blocks found in the payload.</summary>
	public IReadOnlyList<MotionGraphNode> GraphNodes { get; }

	/// <summary>Runs of 8-byte quantized quaternions found in the high-entropy payload regions.</summary>
	public IReadOnlyList<MotionQuaternionRun> QuantizedQuaternionRuns { get; }

	/// <summary>Decoded 16-byte animation sample tracks.</summary>
	public IReadOnlyList<MotionSampleTrack> SampleTracks { get; }

	/// <summary>Sample tracks grouped into animations by shared duration.</summary>
	public IReadOnlyList<MotionSampleGroup> SampleGroups { get; }

	/// <summary>Graph names that appear as transition endpoints.</summary>
	public IReadOnlyList<string> StateNames { get; }

	/// <summary>Remaining graph names, for example motion nodes or event-track names.</summary>
	public IReadOnlyList<string> NodeNames { get; }

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

		var strings = ReadStrings(data);
		var mainEnd = FindMainPayloadEnd(data, Gar5WrapperSize);
		var allTables = FindStringTables(data, Gar5WrapperSize, mainEnd);
		var tables = allTables
			.Where(table => table.Names.Count > 0 &&
				string.Equals(table.Names[0], "CharacterWorldSpaceTM", StringComparison.Ordinal))
			.ToList();
		var headers = FindClipHeaders(data, Gar5WrapperSize, mainEnd);
		var clips = new List<MotionClip>(tables.Count);
		var usedHeaders = new bool[headers.Count];
		var headerCursor = 0;
		for (var index = 0; index < tables.Count; index++)
		{
			var table = tables[index];
			while (headerCursor < headers.Count && headers[headerCursor].Offset < table.StringStart)
			{
				headerCursor++;
			}

			ClipHeader? header = null;
			if (headerCursor > 0 && !usedHeaders[headerCursor - 1])
			{
				header = headers[headerCursor - 1];
				usedHeaders[headerCursor - 1] = true;
			}

			var nextStart = index + 1 < tables.Count ? tables[index + 1].StringStart : mainEnd;
			var (poseOffset, pose) = FindPose(data, table, nextStart, table.Names.Count);
			clips.Add(new MotionClip(
				index,
				header?.Field0 ?? 0,
				header?.Field1 ?? 0,
				table.Names,
				pose,
				header?.Offset ?? -1,
				table.StringStart,
				poseOffset));
		}

		var transitions = new List<MotionTransition>();
		var graphNames = new List<string>();
		var knownNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var table in allTables)
		{
			if (IsClipTable(table) || IsTransitionTable(table) || IsEventTable(table) || IsBlendShapeTable(table))
			{
				continue;
			}

			foreach (var graphName in table.Names)
			{
				if (knownNames.Add(graphName))
				{
					graphNames.Add(graphName);
				}
			}
		}

		foreach (var value in strings)
		{
			if (IsGraphIdentifier(value))
			{
				knownNames.Add(value);
			}
		}

		var transitionNames = new List<string>();
		var seenTransitions = new HashSet<string>(StringComparer.Ordinal);
		foreach (var table in allTables.Where(IsTransitionTable))
		{
			foreach (var transitionName in table.Names)
			{
				if (seenTransitions.Add(transitionName))
				{
					transitionNames.Add(transitionName);
				}
			}
		}

		foreach (var transitionName in ReadTransitionNames(data, Gar5WrapperSize, mainEnd))
		{
			if (seenTransitions.Add(transitionName))
			{
				transitionNames.Add(transitionName);
			}
		}

		foreach (var transitionName in transitionNames)
		{
			transitions.Add(MotionTransition.Parse(transitionName, knownNames));
		}

		var statePaths = new List<MotionStatePath>();
		var seenPaths = new HashSet<string>(StringComparer.Ordinal);
		foreach (var value in strings.Concat(transitionNames))
		{
			if (seenPaths.Add(value) &&
				MotionStatePath.TryParse(value, out var path) && path is not null)
			{
				statePaths.Add(path);
				if (statePaths.Count >= MaxStatePaths)
				{
					break;
				}
			}
		}

		var endpoints = transitions
			.Where(transition => transition.Target.Length > 0)
			.SelectMany(transition => new[] { transition.Source, transition.Target })
			.ToHashSet(StringComparer.Ordinal);
		var nodeNames = graphNames.Where(name => !endpoints.Contains(name)).ToList();
		var stateNames = transitions
			.Where(transition => transition.Target.Length > 0)
			.SelectMany(transition => new[] { transition.Source, transition.Target })
			.Where(name => name.Length > 0 && !nodeNames.Contains(name))
			.Distinct(StringComparer.Ordinal)
			.ToList();

		var sampleTracks = MotionSampleTrack.FindAll(data, Gar5WrapperSize, mainEnd);
		var sampleGroups = MotionSampleGroup.GroupByDuration(sampleTracks);

		return new MotionNetwork(
			name,
			strings,
			clips,
			transitions,
			statePaths,
			MotionGraphNode.FindAll(data, Gar5WrapperSize, mainEnd),
			MotionSampleProbe.FindQuaternionRuns(data, Gar5WrapperSize, mainEnd),
			sampleTracks,
			sampleGroups,
			stateNames,
			nodeNames,
			Gar5WrapperSize);
	}

	private static List<string> ReadTransitionNames(byte[] data, int start, int end)
	{
		var names = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var position = start;
		while (position < end && names.Count < MaxTransitions)
		{
			if (!IsPrintableByte(data[position]))
			{
				position++;
				continue;
			}

			var runStart = position;
			while (position < end && IsPrintableByte(data[position]))
			{
				position++;
			}

			if (position - runStart < 8)
			{
				continue;
			}

			var value = Encoding.Latin1.GetString(data, runStart, position - runStart);
			if (IsTransitionName(value) && seen.Add(value))
			{
				names.Add(value);
			}
		}

		return names;
	}

	private static bool IsTransitionName(string value)
	{
		var parts = value.Split('|');
		if (parts.Length != 3 || !string.Equals(parts[1], "ConditionGroup", StringComparison.Ordinal))
		{
			return false;
		}

		return IsNameToken(parts[0]) && parts[0].Length >= 3 &&
			parts[0].Contains('_') && IsNameToken(parts[2]);
	}

	private static bool IsNameToken(string value)
	{
		if (value.Length == 0 || value[0] == '_')
		{
			return false;
		}

		foreach (var character in value)
		{
			if (!char.IsAsciiLetterOrDigit(character) && character != '_' &&
				character != '.' && character != '-')
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsGraphIdentifier(string value)
	{
		if (value.Length < 2 || value.Length > 64)
		{
			return false;
		}

		foreach (var character in value)
		{
			if (character < 0x20 || character >= 0x7F || character == '|' ||
				character == '/' || character == ' ')
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsClipTable(StringTable table) =>
		table.Names.Count > 0 &&
		string.Equals(table.Names[0], "CharacterWorldSpaceTM", StringComparison.Ordinal);

	private static bool IsTransitionTable(StringTable table) =>
		table.Names.Exists(value => value.Contains('|'));

	private static bool IsEventTable(StringTable table) =>
		table.Names.Exists(value => value.Contains("EventTrack", StringComparison.OrdinalIgnoreCase));

	private static bool IsBlendShapeTable(StringTable table) =>
		table.Names.Exists(value => value.StartsWith("blendShape", StringComparison.OrdinalIgnoreCase));

	private static List<string> ReadStrings(byte[] data)
	{
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

			if (position - start >= MinStringLength)
			{
				strings.Add(Encoding.Latin1.GetString(data, start, position - start));
			}
		}

		return strings;
	}

	private static int FindMainPayloadEnd(byte[] data, int payloadStart)
	{
		var relative = data.AsSpan(payloadStart).LastIndexOf("GAR5"u8);
		if (relative <= 0x40)
		{
			return data.Length;
		}

		return payloadStart + relative;
	}

	private static List<ClipHeader> FindClipHeaders(byte[] data, int start, int end)
	{
		var headers = new List<ClipHeader>();
		var search = start;
		while (search + 0x20 <= end)
		{
			var marker = data.AsSpan(search, end - search).IndexOf(MotionClip.HeaderMarker);
			if (marker < 0)
			{
				break;
			}

			var header = search + marker - 0x14;
			if (header >= start && header + 0x20 <= end)
			{
				var field0 = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(header + 4));
				var field1 = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(header + 0x10));
				if (float.IsFinite(field0) && float.IsFinite(field1) &&
					MathF.Abs(field0) < 10_000 && MathF.Abs(field1) < 10_000)
				{
					headers.Add(new ClipHeader(header, field0, field1));
				}
			}

			search += marker + 1;
		}

		return headers;
	}

	private static (int Offset, Quaternion[] Pose) FindPose(
		byte[] data,
		StringTable table,
		int nextStart,
		int trackCount)
	{
		if (trackCount <= 0)
		{
			return (-1, []);
		}

		var searchStart = (table.StringEnd + 3) & ~3;
		var searchEnd = Math.Min(nextStart, searchStart + MaxPoseSearch);
		for (var offset = searchStart; offset + 16 <= searchEnd; offset += 4)
		{
			if (!TryReadQuaternion(data, offset, out _))
			{
				continue;
			}

			var run = 0;
			var cursor = offset;
			while (cursor + 16 <= searchEnd && TryReadQuaternion(data, cursor, out _))
			{
				run++;
				cursor += 16;
			}

			if (run < trackCount)
			{
				offset = cursor - 4;
				continue;
			}

			var pose = new Quaternion[trackCount];
			for (var index = 0; index < pose.Length; index++)
			{
				TryReadQuaternion(data, offset + index * 16, out pose[index]);
			}

			return (offset, pose);
		}

		return (-1, []);
	}

	private static bool TryReadQuaternion(byte[] data, int offset, out Quaternion value)
	{
		value = default;
		if (offset < 0 || offset + 16 > data.Length)
		{
			return false;
		}

		var x = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
		var y = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4));
		var z = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8));
		var w = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 12));
		if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(w))
		{
			return false;
		}

		var lengthSquared = x * x + y * y + z * z + w * w;
		if (lengthSquared < 0.72f || lengthSquared > 1.32f)
		{
			return false;
		}

		value = new Quaternion(x, y, z, w);
		return true;
	}

	private static List<StringTable> FindStringTables(byte[] data, int start, int end)
	{
		var tables = new List<StringTable>();
		var position = start;
		while (position < end)
		{
			if (!IsPrintableByte(data[position]))
			{
				position++;
				continue;
			}

			var nameStart = position;
			var names = new List<string>();
			while (position < end && IsPrintableByte(data[position]))
			{
				var zero = Array.IndexOf(
					data, (byte)0, position, Math.Min(MaxNameLength + 1, end - position));
				if (zero < 0)
				{
					break;
				}

				names.Add(Encoding.Latin1.GetString(data, position, zero - position));
				position = zero + 1;
				if (names.Count > MaxTracks)
				{
					break;
				}
			}

			if (names.Count >= 2 && ValidateOffsets(data, start, nameStart, names))
			{
				tables.Add(new StringTable(nameStart, position, names));
				continue;
			}

			if (position <= nameStart)
			{
				position = nameStart + 1;
			}
		}

		return tables;
	}

	private static bool ValidateOffsets(byte[] data, int start, int nameStart, List<string> names)
	{
		var offsetsStart = nameStart - names.Count * 4;
		if (offsetsStart < start)
		{
			return false;
		}

		uint expected = 0;
		for (var index = 0; index < names.Count; index++)
		{
			if (index > 0)
			{
				expected += (uint)(names[index - 1].Length + 1);
			}

			if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetsStart + index * 4)) != expected)
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsPrintableByte(byte value) => value >= 0x20 && value < 0x7F;

	private readonly record struct ClipHeader(int Offset, float Field0, float Field1);

	private readonly record struct StringTable(int StringStart, int StringEnd, List<string> Names);
}
