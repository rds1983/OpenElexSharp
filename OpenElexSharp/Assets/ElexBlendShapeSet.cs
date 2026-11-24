using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// Parsed <c>bln</c> blend-shape set (<c>class eCBlendShapeResource2</c>). The payload contains a
/// header, target-name table, a base-vertex remap, a per-vertex sparse-target descriptor and
/// quantized position/normal deltas.
/// </summary>
public sealed class ElexBlendShapeSet
{
	private const int Gar5WrapperSize = 44;
	private const int HeaderSize = 28;
	private const int MaxTargetCount = 4096;
	private const int MaxVertexCount = 1_000_000;
	private const int MaxDeltaCount = 10_000_000;
	private const int MaxNameLength = 256;

	private ElexBlendShapeSet(
		string name,
		int targetCount,
		int vertexCount,
		int field0,
		int field1,
		int field2,
		int secondCount,
		IReadOnlyList<uint> nameOffsets,
		IReadOnlyList<string> names,
		IReadOnlyList<ElexBlendShapeTarget> targets,
		bool deltasDecoded,
		int nameTableOffset)
	{
		Name = name;
		TargetCount = targetCount;
		VertexCount = vertexCount;
		Field0 = field0;
		Field1 = field1;
		Field2 = field2;
		SecondCount = secondCount;
		NameOffsets = nameOffsets;
		Names = names;
		Targets = targets;
		DeltasDecoded = deltasDecoded;
		NameTableOffset = nameTableOffset;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Number of blend-shape targets.</summary>
	public int TargetCount { get; }

	/// <summary>Base-mesh vertex count.</summary>
	public int VertexCount { get; }

	/// <summary>Unresolved header value at offset 8.</summary>
	public int Field0 { get; }

	/// <summary>Unresolved header value at offset 12.</summary>
	public int Field1 { get; }

	/// <summary>Unresolved header value at offset 16.</summary>
	public int Field2 { get; }

	/// <summary>Repeated target count at offset 20.</summary>
	public int SecondCount { get; }

	/// <summary>
	/// End offsets of targets <c>0..targetCount-2</c>, relative to the first name. Target 0 starts at
	/// relative offset 0; target <c>i</c> starts at <c>NameOffsets[i-1]</c>.
	/// </summary>
	public IReadOnlyList<uint> NameOffsets { get; }

	/// <summary>Blend-shape target names in table order.</summary>
	public IReadOnlyList<string> Names { get; }

	/// <summary>Decoded targets with sparse position and normal deltas; empty when not decoded.</summary>
	public IReadOnlyList<ElexBlendShapeTarget> Targets { get; }

	/// <summary>Whether the vertex remap, descriptor and delta arrays were decoded.</summary>
	public bool DeltasDecoded { get; }

	/// <summary>
	/// Applies blend-shape weights to base-mesh positions and normals. Each target delta is scaled by
	/// its weight; normals are accumulated, not normalized.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="weights"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">Deltas were not decoded.</exception>
	/// <exception cref="ArgumentException">A weight or buffer count is wrong.</exception>
	public void ApplyWeights(
		IReadOnlyList<float> weights,
		Span<Vector3> positions,
		Span<Vector3> normals)
	{
		ArgumentNullException.ThrowIfNull(weights);
		if (!DeltasDecoded)
		{
			throw new InvalidOperationException("Blend-shape deltas were not decoded.");
		}

		if (weights.Count != TargetCount)
		{
			throw new ArgumentException("One weight per target is required.", nameof(weights));
		}

		if (positions.Length != VertexCount || normals.Length != VertexCount)
		{
			throw new ArgumentException("Position and normal buffers must match the vertex count.");
		}

		for (var targetIndex = 0; targetIndex < Targets.Count; targetIndex++)
		{
			var weight = weights[targetIndex];
			if (weight == 0f)
			{
				continue;
			}

			foreach (var delta in Targets[targetIndex].Deltas)
			{
				positions[delta.VertexIndex] += delta.Position * weight;
				normals[delta.VertexIndex] += delta.Normal * weight;
			}
		}
	}

	/// <summary>Offset of the name table in the payload.</summary>
	public int NameTableOffset { get; }

	/// <summary>Parses a <c>bln</c> resource wrapped in a GAR5 header.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="Gar5FormatException">The payload is malformed.</exception>
	public static ElexBlendShapeSet Parse(string name, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + HeaderSize ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has no GAR5 payload.");
		}

		var payload = Gar5WrapperSize;
		var targetCount = (int)ReadUInt32(data, payload);
		if (targetCount == 0 || targetCount > MaxTargetCount)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has an invalid target count.");
		}

		var offsetsStart = payload + HeaderSize;
		var offsetCount = targetCount - 1;
		var offsetsByteCount = checked(offsetCount * 4);
		if (offsetsStart + offsetsByteCount > data.Length)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has truncated name offsets.");
		}

		var nameOffsets = new uint[offsetCount];
		var previousOffset = 0u;
		for (var index = 0; index < nameOffsets.Length; index++)
		{
			var value = ReadUInt32(data, offsetsStart + index * 4);
			if (value < previousOffset || value > data.Length)
			{
				throw new Gar5FormatException($"Blend-shape set '{name}' has invalid name offsets.");
			}

			nameOffsets[index] = value;
			previousOffset = value;
		}

		var nameTable = FindNameTable(data, offsetsStart + offsetsByteCount, nameOffsets, targetCount);
		if (nameTable < 0)
		{
			throw new Gar5FormatException($"Blend-shape set '{name}' has no target-name table.");
		}

		var names = new List<string>(targetCount);
		var namesEnd = (long)nameTable;
		for (var index = 0; index < targetCount; index++)
		{
			var relative = index == 0 ? 0u : nameOffsets[index - 1];
			var start = nameTable + (long)relative;
			long end;
			if (index < nameOffsets.Length)
			{
				end = nameTable + (long)nameOffsets[index];
			}
			else
			{
				if (start < 0 || start >= data.Length)
				{
					throw new Gar5FormatException($"Blend-shape set '{name}' has an invalid name table.");
				}

				var zero = Array.IndexOf(
					data, (byte)0, (int)start, Math.Min(MaxNameLength + 1, data.Length - (int)start));
				if (zero < 0)
				{
					throw new Gar5FormatException($"Blend-shape set '{name}' has an unterminated target name.");
				}

				end = zero + 1L;
			}

			if (start < 0 || end <= start || end > data.Length)
			{
				throw new Gar5FormatException($"Blend-shape set '{name}' has an invalid name table.");
			}

			names.Add(Encoding.Latin1.GetString(data, (int)start, (int)(end - start - 1)));
			namesEnd = Math.Max(namesEnd, end);
		}

		var vertexCount = (int)ReadUInt32(data, payload + 4);
		var field0 = (int)ReadUInt32(data, payload + 8);
		var field1 = (int)ReadUInt32(data, payload + 12);
		var field2 = (int)ReadUInt32(data, payload + 16);
		var targets = TryDecodeDeltas(
			data,
			(int)namesEnd,
			targetCount,
			vertexCount,
			field0,
			field1,
			field2,
			names);

		return new ElexBlendShapeSet(
			name,
			targetCount,
			vertexCount,
			field0,
			field1,
			field2,
			(int)ReadUInt32(data, payload + 20),
			nameOffsets,
			names,
			targets,
			targets.Count > 0,
			nameTable);
	}

	private static IReadOnlyList<ElexBlendShapeTarget> TryDecodeDeltas(
		byte[] data,
		int nameEnd,
		int targetCount,
		int vertexCount,
		int affectedVertexCount,
		int deltaCount,
		int positionScaleRaw,
		IReadOnlyList<string> names)
	{
		if (vertexCount <= 0 || vertexCount > MaxVertexCount ||
			affectedVertexCount <= 0 || affectedVertexCount > vertexCount ||
			deltaCount < 0 || deltaCount > MaxDeltaCount)
		{
			return [];
		}

		var mapStart = nameEnd;
		var mapBytes = checked(vertexCount * 4);
		var descriptorStart = mapStart + mapBytes;
		var descriptorBytes = checked(affectedVertexCount * 16);
		var deltaStart = descriptorStart + descriptorBytes;
		var deltaBytes = checked(deltaCount * 12);
		if (deltaStart + deltaBytes > data.Length)
		{
			return [];
		}

		var inverse = new int[affectedVertexCount];
		Array.Fill(inverse, -1);
		for (var vertex = 0; vertex < vertexCount; vertex++)
		{
			var compact = ReadUInt32(data, mapStart + vertex * 4);
			if (compact == 0)
			{
				continue;
			}

			if (compact > affectedVertexCount)
			{
				return [];
			}

			inverse[compact - 1] = vertex;
		}

		if (inverse.Any(vertex => vertex < 0))
		{
			return [];
		}

		var deltas = new List<ElexBlendShapeDelta>[targetCount];
		for (var target = 0; target < deltas.Length; target++)
		{
			deltas[target] = [];
		}

		var positionScale = positionScaleRaw > 0 ? positionScaleRaw / 1000f : 1f;
		for (var descriptor = 0; descriptor < affectedVertexCount; descriptor++)
		{
			var offset = descriptorStart + descriptor * 16;
			var startDelta = ReadUInt32(data, offset);
			var mask0 = ReadUInt32(data, offset + 4);
			var mask1 = ReadUInt32(data, offset + 8);
			var mask2 = ReadUInt32(data, offset + 12);
			var ordinal = 0u;
			for (var word = 0; word < 3; word++)
			{
				var mask = word switch
				{
					0 => mask0,
					1 => mask1,
					_ => mask2,
				};
				while (mask != 0)
				{
					var bit = BitOperations.TrailingZeroCount(mask);
					var target = word * 32 + bit;
					mask &= mask - 1;
					if (target >= targetCount)
					{
						return [];
					}

					var deltaIndex = startDelta + ordinal;
					if (deltaIndex >= deltaCount)
					{
						return [];
					}

					var deltaOffset = deltaStart + (int)deltaIndex * 12;
					var px = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(deltaOffset));
					var py = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(deltaOffset + 2));
					var pz = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(deltaOffset + 4));
					var nx = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(deltaOffset + 6));
					var ny = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(deltaOffset + 8));
					var nz = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(deltaOffset + 10));
					deltas[target].Add(new ElexBlendShapeDelta(
						inverse[descriptor],
						new Vector3(px, py, pz) * (positionScale / 32767f),
						new Vector3(nx, ny, nz) / 32767f));
					ordinal++;
				}
			}
		}

		var targets = new ElexBlendShapeTarget[targetCount];
		for (var target = 0; target < targets.Length; target++)
		{
			targets[target] = new ElexBlendShapeTarget(names[target], deltas[target]);
		}

		return targets;
	}

	private static int FindNameTable(byte[] data, int searchStart, uint[] nameOffsets, int targetCount)
	{
		for (var index = searchStart; index + 1 < data.Length; index++)
		{
			if (!IsPrintableByte(data[index]))
			{
				continue;
			}

			if (nameOffsets.Length > 0 && nameOffsets[0] > 0)
			{
				var firstEnd = index + (long)nameOffsets[0] - 1;
				if (firstEnd < data.Length && data[(int)firstEnd] == 0 &&
					TryValidateNames(data, index, nameOffsets, targetCount))
				{
					return index;
				}
			}

			if (nameOffsets.Length == 0)
			{
				var end = Array.IndexOf(data, (byte)0, index, Math.Min(MaxNameLength + 1, data.Length - index));
				if (end > index && end - index <= MaxNameLength && IsPrintableRange(data, index, end - index))
				{
					return index;
				}
			}
		}

		return -1;
	}

	private static bool TryValidateNames(byte[] data, int nameTable, uint[] nameOffsets, int targetCount)
	{
		for (var index = 0; index < targetCount; index++)
		{
			var relative = index == 0 ? 0u : nameOffsets[index - 1];
			var start = nameTable + (long)relative;
			if (start < 0 || start >= data.Length)
			{
				return false;
			}

			long end;
			if (index < nameOffsets.Length)
			{
				end = nameTable + (long)nameOffsets[index];
				if (end <= start || end > data.Length || data[(int)end - 1] != 0)
				{
					return false;
				}
			}
			else
			{
				var zero = Array.IndexOf(
					data, (byte)0, (int)start, Math.Min(MaxNameLength + 1, data.Length - (int)start));
				if (zero < 0)
				{
					return false;
				}

				end = zero + 1L;
			}

			var length = (int)(end - start - 1);
			if (length <= 0 || length > MaxNameLength || !IsPrintableRange(data, (int)start, length))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsPrintableRange(byte[] data, int start, int length)
	{
		for (var index = 0; index < length; index++)
		{
			if (!IsPrintableByte(data[start + index]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsPrintableByte(byte value) => value >= 0x20 && value < 0x7F;

	private static uint ReadUInt32(byte[] data, int offset) =>
		BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
}
