using System.Buffers.Binary;
using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>One bind-pose bone from the skeleton tail of an <c>skn</c> resource.</summary>
/// <param name="Index">Bone index in stream order.</param>
/// <param name="Position">Bind-pose translation.</param>
/// <param name="Rotation">Bind-pose rotation quaternion.</param>
public sealed record SkeletonBone(int Index, Vector3 Position, Quaternion Rotation);

/// <summary>
/// Skeleton embedded at the end of a <c>skn</c> (<c>eCMotionSkinResource2</c>) payload. Depending on
/// the character resource the tail stores either 28-byte bind-pose records (position + quaternion)
/// or a separate transform block, followed by a <c>0xFFFFFFFF</c> separator, parent indices, name
/// offsets, case-sensitive DJB2 hashes and NUL-terminated bone names.
/// </summary>
public sealed class Skeleton
{
	/// <summary>Size of the unknown header before the first bone record.</summary>
	public const int HeaderSize = 0x20;

	/// <summary>Size of one bind-pose bone record: position (12 bytes) + rotation (16 bytes).</summary>
	public const int BoneSize = 28;

	/// <summary>Word that separates the transform block from the hierarchy/name tables.</summary>
	public const uint Separator = 0xFFFFFFFF;

	private const int MaxBones = 4096;
	private const int MaxNameLength = 128;
	private const int MaxNames = 512;

	private Skeleton(
		IReadOnlyList<float> header,
		IReadOnlyList<SkeletonBone> bones,
		IReadOnlyList<string> names,
		IReadOnlyList<uint> nameHashes,
		IReadOnlyList<uint> nameOffsets,
		IReadOnlyList<uint> parentIndices)
	{
		Header = header;
		Bones = bones;
		Names = names;
		NameHashes = nameHashes;
		NameOffsets = nameOffsets;
		ParentIndices = parentIndices;
	}

	/// <summary>The eight header floats before the first bone record.</summary>
	public IReadOnlyList<float> Header { get; }

	/// <summary>Bind-pose bones when the tail uses 28-byte records; empty otherwise.</summary>
	public IReadOnlyList<SkeletonBone> Bones { get; }

	/// <summary>Bone names in name-table order.</summary>
	public IReadOnlyList<string> Names { get; }

	/// <summary>Case-sensitive DJB2 hashes that precede <see cref="Names"/>.</summary>
	public IReadOnlyList<uint> NameHashes { get; }

	/// <summary>Name offsets that precede the hashes (first name is implicit offset 0).</summary>
	public IReadOnlyList<uint> NameOffsets { get; }

	/// <summary>
	/// Best-effort parent indices between the separator and the name-offset table. The count is the
	/// bone count for body skeletons; small head/attachment skeletons can carry extra words.
	/// </summary>
	public IReadOnlyList<uint> ParentIndices { get; }

	/// <summary>Bone count: parsed transform records when available, otherwise the name count.</summary>
	public int BoneCount => Bones.Count > 0 ? Bones.Count : Names.Count;

	/// <summary>
	/// Parses the skeleton tail of a skinned mesh. Returns <see langword="null"/> when no separator or
	/// name table can be recognized.
	/// </summary>
	/// <param name="data">Complete resource bytes.</param>
	/// <param name="tailOffset">Absolute offset of the tail (the GMSH header's first value).</param>
	public static Skeleton? TryParse(byte[] data, int tailOffset)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (tailOffset < 0 || tailOffset > data.Length - HeaderSize)
		{
			return null;
		}

		var header = new float[HeaderSize / 4];
		for (var index = 0; index < header.Length; index++)
		{
			header[index] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(tailOffset + index * 4));
		}

		for (var separator = tailOffset + HeaderSize; separator + 4 <= data.Length; separator += 4)
		{
			if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(separator)) != Separator)
			{
				continue;
			}

			var (names, hashes, namesStart, namesEnd) = FindNames(data, separator + 4);
			if (names.Length == 0)
			{
				continue;
			}

			var hashesStart = namesStart - names.Length * 4;
			var offsetsStart = hashesStart;
			var offsets = Array.Empty<uint>();
			if (names.Length >= 2)
			{
				var candidate = hashesStart - (names.Length - 1) * 4;
				var parsedOffsets = ReadNameOffsets(data, candidate, hashesStart, namesEnd - namesStart);
				if (parsedOffsets.Length == names.Length - 1)
				{
					offsetsStart = candidate;
					offsets = parsedOffsets;
				}
			}

			var parents = ReadParentIndices(data, separator + 4, offsetsStart);
			SkeletonBone[] bones;
			if (names.Length >= 8 &&
				TryReadBodyBones(data, tailOffset, separator, names.Length, parents, out var bodyBones))
			{
				bones = bodyBones;
			}
			else
			{
				bones = ReadBones(data, tailOffset, separator);
			}

			var boneCount = bones.Length > 0 ? bones.Length : names.Length;
			var parentCount = Math.Max(0, boneCount - 1);
			if (parents.Length > parentCount)
			{
				parents = parents[..parentCount];
			}

			if (parents.Length != parentCount || parents.Any(parent => parent >= boneCount))
			{
				parents = [];
			}

			return new Skeleton(header, bones, names, hashes, offsets, parents);
		}

		return null;
	}

	private static (string[] Names, uint[] Hashes, int Start, int End) FindNames(byte[] data, int searchStart)
	{
		var end = Math.Min(data.Length, searchStart + 0x800);
		for (var start = searchStart; start < end; start++)
		{
			var names = new List<string>();
			var position = start;
			while (position < end && names.Count < MaxNames)
			{
				var terminator = Array.IndexOf(data, (byte)0, position, Math.Min(MaxNameLength, end - position));
				if (terminator <= position)
				{
					break;
				}

				var length = terminator - position;
				if (length == 0 || length > MaxNameLength)
				{
					break;
				}

				var name = System.Text.Encoding.Latin1.GetString(data, position, length);
				if (!IsPrintable(name))
				{
					break;
				}

				names.Add(name);
				position = terminator + 1;
			}

			if (names.Count == 0)
			{
				continue;
			}

			var hashStart = start - names.Count * 4;
			if (hashStart < searchStart)
			{
				continue;
			}

			var hashes = new uint[names.Count];
			var matches = true;
			for (var index = 0; index < hashes.Length; index++)
			{
				hashes[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(hashStart + index * 4));
				if (hashes[index] != Djb2Hash.ComputeCaseSensitive(names[index]))
				{
					matches = false;
					break;
				}
			}

			if (matches)
			{
				return ([.. names], hashes, start, position);
			}
		}

		return ([], [], -1, -1);
	}

	private static uint[] ReadNameOffsets(byte[] data, int start, int end, int tableLength)
	{
		if (start < 0 || end - start != (end - start) / 4 * 4 || (end - start) == 0)
		{
			return [];
		}

		var offsets = new uint[(end - start) / 4];
		uint previous = 0;
		for (var index = 0; index < offsets.Length; index++)
		{
			offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + index * 4));
			if (offsets[index] < previous || offsets[index] >= tableLength)
			{
				return [];
			}

			previous = offsets[index];
		}

		return offsets;
	}

	private static uint[] ReadParentIndices(byte[] data, int start, int end)
	{
		if (end <= start || (end - start) % 4 != 0)
		{
			return [];
		}

		var count = (end - start) / 4;
		if (count > MaxBones)
		{
			return [];
		}

		var values = new uint[count];
		for (var index = 0; index < values.Length; index++)
		{
			values[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + index * 4));
		}

		return values;
	}

	private static SkeletonBone[] ReadBones(byte[] data, int tailOffset, int separator)
	{
		var start = tailOffset + HeaderSize;
		var length = separator - start;
		if (length <= 0 || length % BoneSize != 0 || length / BoneSize > MaxBones)
		{
			return [];
		}

		var bones = new SkeletonBone[length / BoneSize];
		for (var index = 0; index < bones.Length; index++)
		{
			if (!TryReadBone(data, start + index * BoneSize, index, out bones[index]))
			{
				return [];
			}
		}

		return bones;
	}

	private static bool TryReadBodyBones(
		byte[] data,
		int tailOffset,
		int separator,
		int nameCount,
		uint[] parents,
		out SkeletonBone[] bones)
	{
		bones = [];
		var count = nameCount - 1;
		if (count <= 0 || parents.Length < count)
		{
			return false;
		}

		var recordBytes = checked(2 * count * BoneSize);
		var start = separator - recordBytes;
		if (start < tailOffset + HeaderSize)
		{
			return false;
		}

		var globals = new SkeletonBone[count];
		for (var index = 0; index < count; index++)
		{
			if (!TryReadBone(data, start + index * BoneSize, index, out globals[index]))
			{
				return false;
			}
		}

		// The second half is the cached local transform block. Validate it so the layout is
		// confirmed, but derive locals from the global block because that hierarchy is consistent.
		for (var index = 0; index < count; index++)
		{
			if (!TryReadBone(data, start + (count + index) * BoneSize, index, out _))
			{
				return false;
			}
		}

		var local = new SkeletonBone[nameCount];
		local[0] = new SkeletonBone(0, globals[0].Position, globals[0].Rotation);
		for (var index = 1; index < count; index++)
		{
			var parent = (int)parents[index - 1];
			if (parent < 0 || parent >= count)
			{
				return false;
			}

			var parentBone = globals[parent];
			var childBone = globals[index];
			var inverseParent = Quaternion.Conjugate(parentBone.Rotation);
			var position = Vector3.Transform(childBone.Position - parentBone.Position, inverseParent);
			var rotation = Quaternion.Normalize(Quaternion.Multiply(inverseParent, childBone.Rotation));
			local[index] = new SkeletonBone(index, position, rotation);
		}

		local[count] = new SkeletonBone(count, Vector3.Zero, Quaternion.Identity);
		bones = local;
		return true;
	}

	private static bool TryReadBone(byte[] data, int offset, int index, out SkeletonBone bone)
	{
		bone = null!;
		if (offset < 0 || offset + BoneSize > data.Length)
		{
			return false;
		}

		var position = new Vector3(
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)),
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4)),
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8)));
		var rotation = new Quaternion(
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 12)),
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 16)),
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 20)),
			BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 24)));
		if (!IsFinite(position) || !float.IsFinite(rotation.X) ||
			MathF.Abs(rotation.Length() - 1f) > 0.25f)
		{
			return false;
		}

		bone = new SkeletonBone(index, position, rotation);
		return true;
	}

	private static bool IsPrintable(string value)
	{
		foreach (var character in value)
		{
			if (character < 0x20 || character >= 0x7F)
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsFinite(Vector3 value) =>
		float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
