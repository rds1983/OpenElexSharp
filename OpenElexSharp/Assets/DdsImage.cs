using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>Pixel formats found in ELEX DDS textures.</summary>
public enum DdsFormat
{
	Unknown,
	Bc1,
	Bc2,
	Bc3,
	Bc4,
	Bc5,
	Bc6H,
	Bc7,
	Bgra8,
	Bgrx8,
	Rgba8,
	Rgbx8,
	R8G8,
	R8,
	A8,
	A8L8,
	R16,
	R10G10B10A2,
	R16Float,
	R16G16Float,
	Rgba16Float,
	R32Float,
	R32G32Float,
	Rgba32Float,
}

/// <summary>One mip level of one DDS face or array slice.</summary>
/// <param name="Width">Mip width in pixels.</param>
/// <param name="Height">Mip height in pixels.</param>
/// <param name="Depth">Number of depth slices; 1 for ordinary 2D textures.</param>
/// <param name="Data">Raw mip data, excluding headers and ELEX resource trailers.</param>
public sealed record DdsMipLevel(int Width, int Height, int Depth, ReadOnlyMemory<byte> Data)
{
	/// <summary>Size in bytes of one depth slice.</summary>
	public int SliceSize { get; init; }
}

/// <summary>All mip levels of one DDS face or array slice.</summary>
public sealed class DdsFace
{
	internal DdsFace(DdsMipLevel[] mipmaps)
	{
		Mipmaps = mipmaps;
	}

	/// <summary>Mip levels from largest to smallest.</summary>
	public IReadOnlyList<DdsMipLevel> Mipmaps { get; }
}

/// <summary>
/// A parsed DirectDraw Surface texture. ELEX <c>img</c> resources are a 44-byte GAR5 wrapper,
/// a standard DDS file and a 36-byte E1RF-style offset table trailer.
/// </summary>
public sealed class DdsImage
{
	private const uint DdsMagic = 0x20534444; // "DDS "
	private const uint Gar5Magic = Gar5Header.Magic;
	private const int DdsHeaderSize = 124;
	private const int Dx10HeaderSize = 20;
	private const int MaxDimension = 32768;
	private const int MaxDepth = 4096;
	private const int MaxArraySize = 4096;
	private const int MaxMipCount = 32;

	private const uint DdsdMipMapCount = 0x00020000;
	private const uint DdpfAlphaPixels = 0x00000001;
	private const uint DdpfFourCc = 0x00000004;
	private const uint DdpfRgb = 0x00000040;
	private const uint DdpfLuminance = 0x00020000;
	private const uint Ddscaps2Cubemap = 0x00000200;
	private const uint Ddscaps2Volume = 0x00200000;
	private const uint D3d10ResourceDimensionTexture3D = 4;
	private const uint D3d10ResourceMiscTextureCube = 0x4;

	private const uint FourCcDxt1 = 0x31545844;
	private const uint FourCcDxt2 = 0x32545844;
	private const uint FourCcDxt3 = 0x33545844;
	private const uint FourCcDxt4 = 0x34545844;
	private const uint FourCcDxt5 = 0x35545844;
	private const uint FourCcAti1 = 0x31495441;
	private const uint FourCcBc4U = 0x55344342;
	private const uint FourCcBc4S = 0x53344342;
	private const uint FourCcAti2 = 0x32495441;
	private const uint FourCcBc5U = 0x55354342;
	private const uint FourCcBc5S = 0x53354342;
	private const uint FourCcDx10 = 0x30315844;

	private DdsImage(
		int ddsOffset,
		int dataOffset,
		int dataLength,
		int trailingLength,
		int width,
		int height,
		int depth,
		int mipCount,
		int arraySize,
		bool isCubemap,
		bool isVolume,
		DdsFormat format,
		bool isSrgb,
		bool isSigned,
		uint flags,
		uint caps,
		uint caps2,
		uint pixelFormatFlags,
		uint rgbBitCount,
		uint redMask,
		uint greenMask,
		uint blueMask,
		uint alphaMask,
		uint? dxgiFormat,
		uint dx10Dimension,
		uint dx10MiscFlags,
		uint dx10MiscFlags2,
		uint fourCc,
		DdsFace[] faces)
	{
		DdsOffset = ddsOffset;
		DataOffset = dataOffset;
		DataLength = dataLength;
		TrailingLength = trailingLength;
		Width = width;
		Height = height;
		Depth = depth;
		MipCount = mipCount;
		ArraySize = arraySize;
		IsCubemap = isCubemap;
		IsVolume = isVolume;
		Format = format;
		IsSrgb = isSrgb;
		IsSigned = isSigned;
		Flags = flags;
		Caps = caps;
		Caps2 = caps2;
		PixelFormatFlags = pixelFormatFlags;
		RgbBitCount = rgbBitCount;
		RedMask = redMask;
		GreenMask = greenMask;
		BlueMask = blueMask;
		AlphaMask = alphaMask;
		DxgiFormat = dxgiFormat;
		Dx10Dimension = dx10Dimension;
		Dx10MiscFlags = dx10MiscFlags;
		Dx10MiscFlags2 = dx10MiscFlags2;
		FourCc = fourCc;
		Faces = faces;
	}

	/// <summary>Width of mip 0 in pixels.</summary>
	public int Width { get; }

	/// <summary>Height of mip 0 in pixels.</summary>
	public int Height { get; }

	/// <summary>Depth of mip 0 in slices; 1 for ordinary 2D textures.</summary>
	public int Depth { get; }

	/// <summary>Number of parsed mip levels per face.</summary>
	public int MipCount { get; }

	/// <summary>Number of array slices. A cubemap slice contains six faces.</summary>
	public int ArraySize { get; }

	/// <summary>Whether the texture is a cubemap.</summary>
	public bool IsCubemap { get; }

	/// <summary>Whether the texture is a volume texture.</summary>
	public bool IsVolume { get; }

	/// <summary>Decoded pixel format.</summary>
	public DdsFormat Format { get; }

	/// <summary>Whether RGB data is sRGB-encoded.</summary>
	public bool IsSrgb { get; }

	/// <summary>Whether uncompressed or BC4/BC5 channels are signed normalized values.</summary>
	public bool IsSigned { get; }

	/// <summary>Whether the texture declares or implies an alpha channel.</summary>
	public bool HasAlpha =>
		(PixelFormatFlags & DdpfAlphaPixels) != 0 ||
		AlphaMask != 0 ||
		Format is DdsFormat.Bgra8 or DdsFormat.Rgba8 or DdsFormat.R10G10B10A2 or
			DdsFormat.Bc2 or DdsFormat.Bc3 or DdsFormat.Bc7 or DdsFormat.A8 or
			DdsFormat.A8L8 or DdsFormat.Rgba16Float or DdsFormat.Rgba32Float;

	/// <summary>Offset where the <c>DDS </c> magic starts.</summary>
	public int DdsOffset { get; }

	/// <summary>Offset where pixel data starts.</summary>
	public int DataOffset { get; }

	/// <summary>Number of bytes occupied by pixel data.</summary>
	public int DataLength { get; }

	/// <summary>Bytes after the pixel data; ELEX resources have a 36-byte offset table.</summary>
	public int TrailingLength { get; }

	/// <summary>Raw DDS header flags.</summary>
	public uint Flags { get; }

	/// <summary>Raw DDS caps field.</summary>
	public uint Caps { get; }

	/// <summary>Raw DDS caps2 field.</summary>
	public uint Caps2 { get; }

	/// <summary>Raw DDS pixel-format flags.</summary>
	public uint PixelFormatFlags { get; }

	/// <summary>Raw DDS pixel-format bit count.</summary>
	public uint RgbBitCount { get; }

	/// <summary>Red channel mask for uncompressed formats.</summary>
	public uint RedMask { get; }

	/// <summary>Green channel mask for uncompressed formats.</summary>
	public uint GreenMask { get; }

	/// <summary>Blue channel mask for uncompressed formats.</summary>
	public uint BlueMask { get; }

	/// <summary>Alpha channel mask for uncompressed formats.</summary>
	public uint AlphaMask { get; }

	/// <summary>DXGI format from the DX10 header, or <see langword="null"/> for legacy DDS files.</summary>
	public uint? DxgiFormat { get; }

	/// <summary>DX10 resource dimension; 0 for legacy DDS files.</summary>
	public uint Dx10Dimension { get; }

	/// <summary>DX10 misc flags; 0 for legacy DDS files.</summary>
	public uint Dx10MiscFlags { get; }

	/// <summary>DX10 misc flags 2; 0 for legacy DDS files.</summary>
	public uint Dx10MiscFlags2 { get; }

	/// <summary>Raw four-character code, or 0 for uncompressed DDS files.</summary>
	public uint FourCc { get; }

	/// <summary>Faces (or array slices) and their mip levels.</summary>
	public IReadOnlyList<DdsFace> Faces { get; }

	/// <summary>Gets the four-character code as text for diagnostics.</summary>
	public string FourCcText
	{
		get
		{
			if (FourCc == 0)
			{
				return string.Empty;
			}

			Span<byte> bytes = stackalloc byte[4];
			BinaryPrimitives.WriteUInt32LittleEndian(bytes, FourCc);
			return Encoding.Latin1.GetString(bytes).TrimEnd('\0');
		}
	}

	/// <summary>Gets the format name, including the sRGB marker when applicable.</summary>
	public string FormatName => IsSrgb ? $"{Format} sRGB" : Format.ToString();

	/// <summary>Parses a DDS file, optionally wrapped in an ELEX <c>GAR5</c> image resource.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
	/// <exception cref="DdsFormatException">The header or pixel data is malformed or unsupported.</exception>
	public static DdsImage Parse(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);

		var ddsOffset = FindDdsOffset(data);
		if (ddsOffset + 4 + DdsHeaderSize > data.Length)
		{
			throw new DdsFormatException("The DDS header is truncated.");
		}

		if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(ddsOffset + 4)) != DdsHeaderSize)
		{
			throw new DdsFormatException("The DDS header size is not 124 bytes.");
		}

		var flags = ReadUInt32(data, ddsOffset + 8);
		var height = ReadUInt32(data, ddsOffset + 12);
		var width = ReadUInt32(data, ddsOffset + 16);
		var depth = ReadUInt32(data, ddsOffset + 24);
		var declaredMipCount = ReadUInt32(data, ddsOffset + 28);
		var pixelFormatSize = ReadUInt32(data, ddsOffset + 76);
		if (pixelFormatSize != 32)
		{
			throw new DdsFormatException($"The DDS pixel-format size is {pixelFormatSize}, expected 32.");
		}

		var pixelFormatFlags = ReadUInt32(data, ddsOffset + 80);
		var fourCc = ReadUInt32(data, ddsOffset + 84);
		var rgbBitCount = ReadUInt32(data, ddsOffset + 88);
		var redMask = ReadUInt32(data, ddsOffset + 92);
		var greenMask = ReadUInt32(data, ddsOffset + 96);
		var blueMask = ReadUInt32(data, ddsOffset + 100);
		var alphaMask = ReadUInt32(data, ddsOffset + 104);
		var caps = ReadUInt32(data, ddsOffset + 108);
		var caps2 = ReadUInt32(data, ddsOffset + 112);

		if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
		{
			throw new DdsFormatException($"The DDS dimensions {width}x{height} are invalid.");
		}

		if (depth > MaxDepth)
		{
			throw new DdsFormatException($"The DDS depth {depth} is invalid.");
		}

		uint? dxgiFormat = null;
		var dx10Dimension = 0u;
		var dx10MiscFlags = 0u;
		var dx10MiscFlags2 = 0u;
		var arraySize = 1;
		var hasDx10Header = fourCc == FourCcDx10;
		var dataOffset = ddsOffset + 4 + DdsHeaderSize;
		if (hasDx10Header)
		{
			if (dataOffset + Dx10HeaderSize > data.Length)
			{
				throw new DdsFormatException("The DDS DX10 header is truncated.");
			}

			dxgiFormat = ReadUInt32(data, dataOffset);
			dx10Dimension = ReadUInt32(data, dataOffset + 4);
			dx10MiscFlags = ReadUInt32(data, dataOffset + 8);
			var declaredArraySize = ReadUInt32(data, dataOffset + 12);
			dx10MiscFlags2 = ReadUInt32(data, dataOffset + 16);
			if (declaredArraySize == 0 || declaredArraySize > MaxArraySize)
			{
				throw new DdsFormatException($"The DDS array size {declaredArraySize} is invalid.");
			}

			arraySize = (int)declaredArraySize;
			dataOffset += Dx10HeaderSize;
		}

		var (format, isSrgb, isSigned) = ResolveFormat(
			fourCc,
			hasDx10Header,
			dxgiFormat,
			pixelFormatFlags,
			rgbBitCount,
			redMask,
			greenMask,
			blueMask,
			alphaMask);

		var isCubemap = (caps2 & Ddscaps2Cubemap) != 0 || (dx10MiscFlags & D3d10ResourceMiscTextureCube) != 0;
		var isVolume = (caps2 & Ddscaps2Volume) != 0 || dx10Dimension == D3d10ResourceDimensionTexture3D;
		if (isCubemap && isVolume)
		{
			throw new DdsFormatException("The DDS declares a cubemap volume, which is not supported.");
		}

		var mipCount = (flags & DdsdMipMapCount) != 0 ? (int)Math.Max(1, declaredMipCount) : 1;
		if (mipCount > MaxMipCount)
		{
			throw new DdsFormatException($"The DDS mip count {mipCount} is invalid.");
		}

		var textureDepth = isVolume ? (int)Math.Max(1, depth) : 1;
		if (isVolume && textureDepth == 1 && hasDx10Header)
		{
			textureDepth = arraySize;
		}

		if (isVolume && arraySize != 1 && !hasDx10Header)
		{
			throw new DdsFormatException("The DDS volume has an invalid array size.");
		}

		var faceCount = isCubemap ? arraySize * 6 : arraySize;
		if (isCubemap && arraySize == 1)
		{
			faceCount = 6;
		}

		if (isVolume)
		{
			faceCount = 1;
		}

		var faceArray = new DdsFace[faceCount];
		var offset = dataOffset;
		for (var face = 0; face < faceArray.Length; face++)
		{
			var mipmaps = new DdsMipLevel[mipCount];
			for (var mip = 0; mip < mipCount; mip++)
			{
				var mipWidth = Math.Max(1, (int)width >> mip);
				var mipHeight = Math.Max(1, (int)height >> mip);
				var mipDepth = isVolume ? Math.Max(1, textureDepth >> mip) : 1;
				var sliceSize = GetSurfaceSize(format, mipWidth, mipHeight);
				var sizeLong = (long)sliceSize * mipDepth;
				if (sizeLong > int.MaxValue)
				{
					throw new DdsFormatException(
						$"The DDS mip {mipWidth}x{mipHeight}x{mipDepth} is too large.");
				}

				var size = (int)sizeLong;
				if (offset > data.Length || size > data.Length - offset)
				{
					throw new DdsFormatException(
						$"The DDS pixel data is truncated at face {face}, mip {mip} " +
						$"(need {size} bytes, have {Math.Max(0, data.Length - offset)}).");
				}

				mipmaps[mip] = new DdsMipLevel(
					mipWidth,
					mipHeight,
					mipDepth,
					new ReadOnlyMemory<byte>(data, offset, size))
				{
					SliceSize = sliceSize,
				};
				offset += size;
			}

			faceArray[face] = new DdsFace(mipmaps);
		}

		return new DdsImage(
			ddsOffset,
			dataOffset,
			offset - dataOffset,
			data.Length - offset,
			(int)width,
			(int)height,
			textureDepth,
			mipCount,
			arraySize,
			isCubemap,
			isVolume,
			format,
			isSrgb,
			isSigned,
			flags,
			caps,
			caps2,
			pixelFormatFlags,
			rgbBitCount,
			redMask,
			greenMask,
			blueMask,
			alphaMask,
			dxgiFormat,
			dx10Dimension,
			dx10MiscFlags,
			dx10MiscFlags2,
			fourCc,
			faceArray);
	}

	/// <summary>Gets the byte size of one uncompressed surface or compressed 4x4 block row set.</summary>
	/// <exception cref="DdsFormatException">The format is unknown.</exception>
	public static int GetSurfaceSize(DdsFormat format, int width, int height)
	{
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");
		}

		var blockSize = GetBlockSize(format);
		if (blockSize > 0)
		{
			var blocksWide = Math.Max(1, (width + 3) / 4);
			var blocksHigh = Math.Max(1, (height + 3) / 4);
			var size = (long)blockSize * blocksWide * blocksHigh;
			if (size > int.MaxValue)
			{
				throw new DdsFormatException($"The {format} surface {width}x{height} is too large.");
			}

			return (int)size;
		}

		var bytesPerPixel = format switch
		{
			DdsFormat.Rgba32Float => 16,
			DdsFormat.Rgba16Float or DdsFormat.R32G32Float => 8,
			DdsFormat.Bgra8 or DdsFormat.Bgrx8 or DdsFormat.Rgba8 or DdsFormat.Rgbx8 or
				DdsFormat.R10G10B10A2 or DdsFormat.R16G16Float or DdsFormat.R32Float => 4,
			DdsFormat.R8G8 or DdsFormat.A8L8 or DdsFormat.R16 or DdsFormat.R16Float => 2,
			DdsFormat.R8 or DdsFormat.A8 => 1,
			_ => throw new DdsFormatException($"Unknown DDS format '{format}'."),
		};

		var uncompressedSize = (long)bytesPerPixel * width * height;
		if (uncompressedSize > int.MaxValue)
		{
			throw new DdsFormatException($"The {format} surface {width}x{height} is too large.");
		}

		return (int)uncompressedSize;
	}

	/// <summary>Gets the compressed block size in bytes, or 0 for uncompressed formats.</summary>
	public static int GetBlockSize(DdsFormat format) => format switch
	{
		DdsFormat.Bc1 or DdsFormat.Bc4 => 8,
		DdsFormat.Bc2 or DdsFormat.Bc3 or DdsFormat.Bc5 or DdsFormat.Bc6H or DdsFormat.Bc7 => 16,
		_ => 0,
	};

	private static (DdsFormat Format, bool IsSrgb, bool IsSigned) ResolveFormat(
		uint fourCc,
		bool hasDx10Header,
		uint? dxgiFormat,
		uint pixelFormatFlags,
		uint rgbBitCount,
		uint redMask,
		uint greenMask,
		uint blueMask,
		uint alphaMask)
	{
		if (hasDx10Header)
		{
			return dxgiFormat switch
			{
				28 => (DdsFormat.Rgba8, false, false),
				29 => (DdsFormat.Rgba8, true, false),
				31 => (DdsFormat.Rgba8, false, true),
				49 => (DdsFormat.R8G8, false, false),
				51 => (DdsFormat.R8G8, false, true),
				61 => (DdsFormat.R8, false, false),
				63 => (DdsFormat.R8, false, true),
				65 => (DdsFormat.A8, false, false),
				71 => (DdsFormat.Bc1, false, false),
				72 => (DdsFormat.Bc1, true, false),
				74 => (DdsFormat.Bc2, false, false),
				75 => (DdsFormat.Bc2, true, false),
				77 => (DdsFormat.Bc3, false, false),
				78 => (DdsFormat.Bc3, true, false),
				80 => (DdsFormat.Bc4, false, false),
				81 => (DdsFormat.Bc4, false, true),
				83 => (DdsFormat.Bc5, false, false),
				84 => (DdsFormat.Bc5, false, true),
				95 => (DdsFormat.Bc6H, false, false),
				96 => (DdsFormat.Bc6H, false, true),
				98 => (DdsFormat.Bc7, false, false),
				99 => (DdsFormat.Bc7, true, false),
				87 => (DdsFormat.Bgra8, false, false),
				88 => (DdsFormat.Bgrx8, false, false),
				91 => (DdsFormat.Bgra8, true, false),
				93 => (DdsFormat.Bgrx8, true, false),
				_ => throw new DdsFormatException($"Unsupported DXGI texture format {dxgiFormat}."),
			};
		}

		if ((pixelFormatFlags & DdpfFourCc) != 0)
		{
			return fourCc switch
			{
				FourCcDxt1 => (DdsFormat.Bc1, false, false),
				FourCcDxt2 or FourCcDxt3 => (DdsFormat.Bc2, false, false),
				FourCcDxt4 or FourCcDxt5 => (DdsFormat.Bc3, false, false),
				FourCcAti1 or FourCcBc4U => (DdsFormat.Bc4, false, false),
				FourCcBc4S => (DdsFormat.Bc4, false, true),
				FourCcAti2 or FourCcBc5U => (DdsFormat.Bc5, false, false),
				FourCcBc5S => (DdsFormat.Bc5, false, true),
				111 => (DdsFormat.R16Float, false, false),
				112 => (DdsFormat.R16G16Float, false, false),
				113 => (DdsFormat.Rgba16Float, false, false),
				114 => (DdsFormat.R32Float, false, false),
				115 => (DdsFormat.R32G32Float, false, false),
				116 => (DdsFormat.Rgba32Float, false, false),
				_ => throw new DdsFormatException(
					$"Unsupported DDS FourCC 0x{fourCc:x8} ('{FormatFourCc(fourCc)}')."),
			};
		}

		if ((pixelFormatFlags & (DdpfRgb | DdpfLuminance)) != 0)
		{
			return (rgbBitCount, redMask, greenMask, blueMask, alphaMask) switch
			{
				(32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000) => (DdsFormat.Bgra8, false, false),
				(32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000) => (DdsFormat.Rgba8, false, false),
				(32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0) => (DdsFormat.Bgrx8, false, false),
				(32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0) => (DdsFormat.Rgbx8, false, false),
				(32, 0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000) => (DdsFormat.R10G10B10A2, false, false),
				(16, 0x000000FF, 0, 0, 0x0000FF00) => (DdsFormat.A8L8, false, false),
				(16, 0x000000FF, 0x0000FF00, 0, 0) => (DdsFormat.R8G8, false, false),
				(16, 0x0000FFFF, 0, 0, 0) => (DdsFormat.R16, false, false),
				(8, 0x000000FF, 0, 0, 0) => (DdsFormat.R8, false, false),
				_ => throw new DdsFormatException(
					$"Unsupported uncompressed DDS pixel format: {rgbBitCount} bits, " +
					$"R=0x{redMask:x8}, G=0x{greenMask:x8}, B=0x{blueMask:x8}, A=0x{alphaMask:x8}."),
			};
		}

		if ((pixelFormatFlags & DdpfAlphaPixels) != 0 && alphaMask == 0xFF)
		{
			return (DdsFormat.A8, false, false);
		}

		throw new DdsFormatException(
			$"Unsupported DDS pixel format: flags=0x{pixelFormatFlags:x8}, bits={rgbBitCount}, " +
			$"FourCC='{FormatFourCc(fourCc)}'.");
	}

	private static int FindDdsOffset(byte[] data)
	{
		if (data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == DdsMagic)
		{
			return 0;
		}

		if (data.Length >= 48 && BinaryPrimitives.ReadUInt32LittleEndian(data) == Gar5Magic)
		{
			if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(44)) == DdsMagic)
			{
				return 44;
			}

			throw new DdsFormatException(
				"The GAR5 image resource does not contain a DDS file at offset 44.");
		}

		throw new DdsFormatException("The data does not start with a DDS file or a GAR5 image wrapper.");
	}

	private static uint ReadUInt32(byte[] data, int offset) =>
		BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

	private static string FormatFourCc(uint fourCc)
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(bytes, fourCc);
		var text = Encoding.Latin1.GetString(bytes).TrimEnd('\0');
		return string.Concat(text.Select(character =>
			char.IsControl(character) ? $"\\x{(int)character:x2}" : character.ToString()));
	}
}
