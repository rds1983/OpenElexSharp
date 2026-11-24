using System.Buffers.Binary;

namespace OpenElexSharp.Assets;

/// <summary>
/// A candidate binary state-graph record in a <c>net</c> payload. The observed block has a 24-byte
/// header and <see cref="Count"/> fixed-size references; the first reference word is exposed.
/// </summary>
/// <param name="Offset">Absolute resource offset of the block.</param>
/// <param name="Type">Block type field (observed <c>1</c>).</param>
/// <param name="Size">Block byte size.</param>
/// <param name="Count">Reference count.</param>
/// <param name="HeaderSize">Header byte size before the references.</param>
/// <param name="References">First reference word of each non-sentinel record.</param>
public sealed record MotionGraphNode(
	int Offset,
	int Type,
	int Size,
	int Count,
	int HeaderSize,
	IReadOnlyList<uint> References)
{
	private const int MaxNodes = 1024;
	private const int MaxCount = 64;
	private const int RecordSize = 40;

	/// <summary>
	/// Scans a payload for candidate state-graph blocks. This is a layout probe, not a full semantic
	/// graph parser.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<MotionGraphNode> FindAll(byte[] data, int start, int end)
	{
		ArgumentNullException.ThrowIfNull(data);
		var nodes = new List<MotionGraphNode>();
		for (var offset = Math.Max(0, start); offset + 24 <= end && nodes.Count < MaxNodes; offset += 4)
		{
			var type = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
			var marker = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
			var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8));
			var zero = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 12));
			var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16));
			var tail = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 20));
			var size = headerSize + count * RecordSize;
			if (type != 1 || zero != 0 || tail != 0 || count <= 0 || count > MaxCount ||
				headerSize != 24 || marker <= 0 || size > 0x10000 || offset + size > end)
			{
				continue;
			}

			var references = new List<uint>(count);
			var valid = true;
			for (var index = 0; index < count; index++)
			{
				var record = offset + headerSize + index * RecordSize;
				if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 4)) != 0)
				{
					valid = false;
					break;
				}

				var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record));
				if (value != uint.MaxValue)
				{
					references.Add(value);
				}
			}

			if (!valid)
			{
				continue;
			}

			nodes.Add(new MotionGraphNode(offset, type, size, count, headerSize, references));
			offset += size - 4;
		}

		return nodes;
	}
}
