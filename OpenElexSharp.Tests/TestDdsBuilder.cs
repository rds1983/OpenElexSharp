using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Tests;

/// <summary>Builds small synthetic DDS files and ELEX image resources for tests.</summary>
internal static class TestDdsBuilder
{
	public static byte[] CreateLegacyFourCc(
		string fourCc,
		int width,
		int height,
		params byte[][] mipmaps) =>
		CreateLegacyFourCc(
			BinaryPrimitives.ReadUInt32LittleEndian(
				Encoding.ASCII.GetBytes(fourCc.PadRight(4)[..4])),
			width,
			height,
			mipmaps);

	public static byte[] CreateLegacyFourCc(
		uint fourCc,
		int width,
		int height,
		params byte[][] mipmaps)
	{
		ArgumentNullException.ThrowIfNull(mipmaps);
		var fourCcBytes = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(fourCcBytes, fourCc);
		return Build(
			width,
			height,
			1,
			Math.Max(1, mipmaps.Length),
			flags: 0x000A100F,
			pixelFormatFlags: 0x4,
			fourCc: fourCcBytes,
			rgbBitCount: 0,
			redMask: 0,
			greenMask: 0,
			blueMask: 0,
			alphaMask: 0,
			caps2: 0,
			dx10: null,
			mipmaps);
	}

	public static byte[] CreateDx10(
		uint dxgiFormat,
		int width,
		int height,
		params byte[][] mipmaps) =>
		CreateDx10(dxgiFormat, width, height, 1, false, 1, mipmaps);

	public static byte[] CreateDx10(
		uint dxgiFormat,
		int width,
		int height,
		int depth,
		bool cubemap,
		uint arraySize,
		params byte[][] mipmaps) =>
		CreateDx10(dxgiFormat, width, height, depth, cubemap, arraySize, Math.Max(1, mipmaps.Length), mipmaps);

	public static byte[] CreateDx10(
		uint dxgiFormat,
		int width,
		int height,
		int depth,
		bool cubemap,
		uint arraySize,
		int mipCount,
		params byte[][] mipmaps)
	{
		ArgumentNullException.ThrowIfNull(mipmaps);
		var dx10 = new byte[20];
		BinaryPrimitives.WriteUInt32LittleEndian(dx10, dxgiFormat);
		BinaryPrimitives.WriteUInt32LittleEndian(dx10.AsSpan(4), depth > 1 ? 4u : 3u);
		BinaryPrimitives.WriteUInt32LittleEndian(dx10.AsSpan(8), cubemap ? 4u : 0u);
		BinaryPrimitives.WriteUInt32LittleEndian(dx10.AsSpan(12), arraySize);
		return Build(
			width,
			height,
			depth,
			Math.Max(1, mipCount),
			flags: 0x000A100F | (depth > 1 ? 0x00800000u : 0),
			pixelFormatFlags: 0x4,
			fourCc: Encoding.ASCII.GetBytes("DX10"),
			rgbBitCount: 0,
			redMask: 0,
			greenMask: 0,
			blueMask: 0,
			alphaMask: 0,
			caps2: cubemap ? 0x200u : depth > 1 ? 0x200000u : 0,
			dx10,
			mipmaps);
	}

	public static byte[] CreateUncompressed(
		int width,
		int height,
		uint rgbBitCount,
		uint redMask,
		uint greenMask,
		uint blueMask,
		uint alphaMask,
		byte[][] mipmaps,
		int depth = 1,
		uint pixelFormatFlags = 0x41)
	{
		ArgumentNullException.ThrowIfNull(mipmaps);
		return Build(
			width,
			height,
			depth,
			Math.Max(1, mipmaps.Length),
			flags: 0x000A100F | (depth > 1 ? 0x00800000u : 0),
			pixelFormatFlags,
			fourCc: new byte[4],
			rgbBitCount,
			redMask,
			greenMask,
			blueMask,
			alphaMask,
			caps2: depth > 1 ? 0x200000u : 0,
			dx10: null,
			mipmaps);
	}

	public static byte[] Wrap(byte[] dds)
	{
		ArgumentNullException.ThrowIfNull(dds);
		var result = new byte[44 + dds.Length + 36];
		Encoding.ASCII.GetBytes("GAR5").CopyTo(result, 0);
		BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 0x20);
		dds.CopyTo(result, 44);

		var trailer = result.AsSpan(44 + dds.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(trailer, 44);
		BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], (uint)dds.Length);
		trailer[8] = 0;
		var offset = (uint)(44 + dds.Length);
		for (var entry = 0; entry < 3; entry++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(trailer[(9 + entry * 9)..], offset);
			BinaryPrimitives.WriteUInt32LittleEndian(trailer[(13 + entry * 9)..], 0);
			trailer[17 + entry * 9] = 0;
		}

		return result;
	}

	public static byte[] CreateBc4Block(byte endpoint0, byte endpoint1, ulong indices = 0)
	{
		var block = new byte[8];
		var value = (ulong)endpoint0 | ((ulong)endpoint1 << 8) | (indices << 16);
		BinaryPrimitives.WriteUInt64LittleEndian(block, value);
		return block;
	}

	public static byte[] CreateBc5Block(
		byte red0,
		byte red1,
		byte green0,
		byte green1,
		ulong redIndices = 0,
		ulong greenIndices = 0)
	{
		var block = new byte[16];
		var red = CreateBc4Block(red0, red1, redIndices);
		var green = CreateBc4Block(green0, green1, greenIndices);
		red.CopyTo(block, 0);
		green.CopyTo(block, 8);
		return block;
	}

	private static byte[] Build(
		int width,
		int height,
		int depth,
		int mipCount,
		uint flags,
		uint pixelFormatFlags,
		byte[] fourCc,
		uint rgbBitCount,
		uint redMask,
		uint greenMask,
		uint blueMask,
		uint alphaMask,
		uint caps2,
		byte[]? dx10,
		byte[][] mipmaps)
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
		writer.Write(Encoding.ASCII.GetBytes("DDS "));
		writer.Write(124u);
		writer.Write(flags);
		writer.Write((uint)height);
		writer.Write((uint)width);
		writer.Write(0u);
		writer.Write((uint)depth);
		writer.Write((uint)mipCount);
		writer.Write(new byte[44]);
		writer.Write(32u);
		writer.Write(pixelFormatFlags);
		writer.Write(fourCc);
		writer.Write(rgbBitCount);
		writer.Write(redMask);
		writer.Write(greenMask);
		writer.Write(blueMask);
		writer.Write(alphaMask);
		writer.Write(mipCount > 1 ? 0x00401008u : 0x00001000u);
		writer.Write(caps2);
		writer.Write(0u);
		writer.Write(0u);
		writer.Write(0u);
		if (dx10 is not null)
		{
			writer.Write(dx10);
		}

		foreach (var mip in mipmaps)
		{
			writer.Write(mip);
		}

		return output.ToArray();
	}
}
