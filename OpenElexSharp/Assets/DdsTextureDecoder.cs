using System.Buffers.Binary;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace OpenElexSharp.Assets;

/// <summary>Decoding options for DDS textures that need CPU conversion.</summary>
/// <param name="NormalMap">
/// Reconstruct the blue channel of two-channel (BC5/R8G8) data as a tangent-space normal map.
/// </param>
public sealed record DdsDecodeOptions(bool NormalMap = false);

/// <summary>A decoded mip level in tightly packed RGBA8 order.</summary>
/// <param name="Width">Mip width in pixels.</param>
/// <param name="Height">Mip height in pixels.</param>
/// <param name="Pixels">RGBA8 pixels, row-major from the top-left corner.</param>
public sealed record DdsDecodedLevel(int Width, int Height, byte[] Pixels);

/// <summary>
/// CPU decoder for DDS formats that MonoGame cannot upload directly (BC4, BC5, BC6H, BC7 and
/// uncompressed variants), plus a reference path used by tests.
/// </summary>
public static class DdsTextureDecoder
{
    /// <summary>Decodes the selected mip level of the first face.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mipIndex"/> is out of range.</exception>
    /// <exception cref="NotSupportedException">The format or texture layout is not supported.</exception>
    public static DdsDecodedLevel Decode(DdsImage image, int mipIndex, DdsDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (mipIndex < 0 || mipIndex >= image.MipCount)
        {
            throw new ArgumentOutOfRangeException(nameof(mipIndex));
        }

        if (image.Faces.Count == 0)
        {
            throw new NotSupportedException("The DDS has no faces.");
        }

        return Decode(image.Faces[0].Mipmaps[mipIndex], image.Format, image.IsSigned, image.HasAlpha, options);
    }

    /// <summary>Decodes one mip level to RGBA8.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="mip"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The format is not supported.</exception>
    public static DdsDecodedLevel Decode(
        DdsMipLevel mip,
        DdsFormat format,
        bool isSigned = false,
        bool hasAlpha = false,
        DdsDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mip);
        options ??= new DdsDecodeOptions();
        if (mip.Depth != 1)
        {
            throw new NotSupportedException("Volume textures cannot be decoded to a single 2D image.");
        }

        var expected = DdsImage.GetSurfaceSize(format, mip.Width, mip.Height);
        if (mip.Data.Length != expected)
        {
            throw new DdsFormatException(
                $"The mip has {mip.Data.Length} bytes but {format} requires {expected}.");
        }

        var pixelBytes = (long)mip.Width * mip.Height * 4;
        if (pixelBytes > int.MaxValue)
        {
            throw new DdsFormatException($"The decoded {mip.Width}x{mip.Height} mip is too large.");
        }

        var pixels = new byte[(int)pixelBytes];
        switch (format)
        {
            case DdsFormat.Bc1:
            case DdsFormat.Bc2:
            case DdsFormat.Bc3:
            case DdsFormat.Bc7:
                DecodeWithBCn(mip, format, hasAlpha, pixels);
                break;
            case DdsFormat.Bc4:
                DecodeBc4(mip, isSigned, pixels);
                break;
            case DdsFormat.Bc5:
                DecodeBc5(mip, isSigned, options.NormalMap, pixels);
                break;
            case DdsFormat.Bc6H:
                DecodeBc6H(mip, isSigned, pixels);
                break;
            case DdsFormat.Bgra8:
                DecodeBgra(mip, hasAlpha: true, pixels);
                break;
            case DdsFormat.Bgrx8:
                DecodeBgra(mip, hasAlpha: false, pixels);
                break;
            case DdsFormat.Rgba8:
                DecodeRgba(mip, isSigned, options.NormalMap, pixels);
                break;
            case DdsFormat.Rgbx8:
                DecodeRgbx(mip, pixels);
                break;
            case DdsFormat.R8G8:
                DecodeR8G8(mip, isSigned, options.NormalMap, pixels);
                break;
            case DdsFormat.R8:
                DecodeR8(mip, isSigned, pixels);
                break;
            case DdsFormat.A8:
                DecodeA8(mip, pixels);
                break;
            case DdsFormat.A8L8:
                DecodeA8L8(mip, pixels);
                break;
            case DdsFormat.R16:
                DecodeR16(mip, pixels);
                break;
            case DdsFormat.R10G10B10A2:
                DecodeR10G10B10A2(mip, pixels);
                break;
            case DdsFormat.R16Float:
                DecodeR16Float(mip, pixels);
                break;
            case DdsFormat.R16G16Float:
                DecodeR16G16Float(mip, pixels);
                break;
            case DdsFormat.Rgba16Float:
                DecodeRgba16Float(mip, pixels);
                break;
            case DdsFormat.R32Float:
                DecodeR32Float(mip, pixels);
                break;
            case DdsFormat.R32G32Float:
                DecodeR32G32Float(mip, pixels);
                break;
            case DdsFormat.Rgba32Float:
                DecodeRgba32Float(mip, pixels);
                break;
            default:
                throw new NotSupportedException($"Unsupported DDS format '{format}'.");
        }

        return new DdsDecodedLevel(mip.Width, mip.Height, pixels);
    }

    private static void DecodeWithBCn(
        DdsMipLevel mip,
        DdsFormat format,
        bool hasAlpha,
        byte[] pixels)
    {
        var compressionFormat = format switch
        {
            DdsFormat.Bc1 => hasAlpha ? CompressionFormat.Bc1WithAlpha : CompressionFormat.Bc1,
            DdsFormat.Bc2 => CompressionFormat.Bc2,
            DdsFormat.Bc3 => CompressionFormat.Bc3,
            DdsFormat.Bc7 => CompressionFormat.Bc7,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        var decoder = new BcDecoder();
        var colors = decoder.DecodeRaw(mip.Data.ToArray(), mip.Width, mip.Height, compressionFormat);
        for (var index = 0; index < colors.Length; index++)
        {
            var color = colors[index];
            var offset = index * 4;
            pixels[offset] = color.r;
            pixels[offset + 1] = color.g;
            pixels[offset + 2] = color.b;
            pixels[offset + 3] = color.a;
        }
    }

    private static void DecodeBc6H(DdsMipLevel mip, bool isSigned, byte[] pixels)
    {
        var decoder = new BcDecoder();
        var colors = decoder.DecodeRawHdr(
            mip.Data.ToArray(),
            mip.Width,
            mip.Height,
            isSigned ? CompressionFormat.Bc6S : CompressionFormat.Bc6U);
        for (var index = 0; index < colors.Length; index++)
        {
            var color = colors[index];
            var offset = index * 4;
            pixels[offset] = FloatToByte(color.r);
            pixels[offset + 1] = FloatToByte(color.g);
            pixels[offset + 2] = FloatToByte(color.b);
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeBc4(DdsMipLevel mip, bool isSigned, byte[] pixels)
    {
        var data = mip.Data.Span;
        var blockSize = 8;
        var blocksWide = Math.Max(1, (mip.Width + 3) / 4);
        var blocksHigh = Math.Max(1, (mip.Height + 3) / 4);
        Span<int> palette = stackalloc int[8];
        Span<int> components = stackalloc int[16];

        for (var blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (var blockX = 0; blockX < blocksWide; blockX++)
            {
                var block = data.Slice((blockY * blocksWide + blockX) * blockSize, blockSize);
                DecodeBc4Component(block, isSigned, palette, components);
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 4; x++)
                    {
                        var component = components[y * 4 + x];
                        var value = ComponentToByte(component, isSigned);
                        SetPixel(pixels, mip.Width, mip.Height, blockX * 4 + x, blockY * 4 + y,
                            value, value, value, 255);
                    }
                }
            }
        }
    }

    private static void DecodeBc5(
        DdsMipLevel mip,
        bool isSigned,
        bool normalMap,
        byte[] pixels)
    {
        var data = mip.Data.Span;
        const int blockSize = 16;
        var blocksWide = Math.Max(1, (mip.Width + 3) / 4);
        var blocksHigh = Math.Max(1, (mip.Height + 3) / 4);
        Span<int> palette = stackalloc int[8];
        Span<int> red = stackalloc int[16];
        Span<int> green = stackalloc int[16];

        for (var blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (var blockX = 0; blockX < blocksWide; blockX++)
            {
                var block = data.Slice((blockY * blocksWide + blockX) * blockSize, blockSize);
                DecodeBc4Component(block[..8], isSigned, palette, red);
                DecodeBc4Component(block[8..], isSigned, palette, green);
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 4; x++)
                    {
                        var index = y * 4 + x;
                        var r = ComponentToBiasedNormal(red[index], isSigned);
                        var g = ComponentToBiasedNormal(green[index], isSigned);
                        byte outR;
                        byte outG;
                        byte outB;
                        if (normalMap)
                        {
                            var z = MathF.Sqrt(Math.Max(0f, 1f - r * r - g * g));
                            outR = NormalizedToByte(r);
                            outG = NormalizedToByte(g);
                            outB = NormalizedToByte(z);
                        }
                        else
                        {
                            outR = ComponentToByte(red[index], isSigned);
                            outG = ComponentToByte(green[index], isSigned);
                            outB = 0;
                        }

                        SetPixel(
                            pixels,
                            mip.Width,
                            mip.Height,
                            blockX * 4 + x,
                            blockY * 4 + y,
                            outR,
                            outG,
                            outB,
                            255);
                    }
                }
            }
        }
    }

    private static void DecodeBc4Component(
        ReadOnlySpan<byte> block,
        bool isSigned,
        Span<int> palette,
        Span<int> components)
    {
        var endpoint0 = isSigned ? (int)(sbyte)block[0] : block[0];
        var endpoint1 = isSigned ? (int)(sbyte)block[1] : block[1];
        palette[0] = endpoint0;
        palette[1] = endpoint1;
        if (endpoint0 > endpoint1)
        {
            for (var index = 2; index < 8; index++)
            {
                palette[index] =
                    ((8 - index) * endpoint0 + (index - 1) * endpoint1) / 7;
            }
        }
        else
        {
            for (var index = 2; index < 6; index++)
            {
                palette[index] =
                    ((6 - index) * endpoint0 + (index - 1) * endpoint1) / 5;
            }

            palette[6] = isSigned ? -127 : 0;
            palette[7] = isSigned ? 127 : 255;
        }

        ulong indices = 0;
        for (var index = 0; index < 6; index++)
        {
            indices |= (ulong)block[2 + index] << (8 * index);
        }

        for (var pixel = 0; pixel < 16; pixel++)
        {
            var index = (int)((indices >> (pixel * 3)) & 0b111);
            components[pixel] = palette[index];
        }
    }

    private static void DecodeBgra(DdsMipLevel mip, bool hasAlpha, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 4;
            var targetOffset = index * 4;
            pixels[targetOffset] = source[sourceOffset + 2];
            pixels[targetOffset + 1] = source[sourceOffset + 1];
            pixels[targetOffset + 2] = source[sourceOffset];
            pixels[targetOffset + 3] = hasAlpha ? source[sourceOffset + 3] : (byte)255;
        }
    }

    private static void DecodeRgba(DdsMipLevel mip, bool isSigned, bool normalMap, byte[] pixels)
    {
        var source = mip.Data.Span;
        if (!isSigned)
        {
            source.CopyTo(pixels);
            return;
        }

        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var offset = index * 4;
            var r = (sbyte)source[offset];
            var g = (sbyte)source[offset + 1];
            var b = (sbyte)source[offset + 2];
            if (normalMap)
            {
                var nx = Math.Clamp(r / 127f, -1f, 1f);
                var ny = Math.Clamp(g / 127f, -1f, 1f);
                var nz = MathF.Sqrt(Math.Max(0f, 1f - nx * nx - ny * ny));
                pixels[offset] = NormalizedToByte(nx);
                pixels[offset + 1] = NormalizedToByte(ny);
                pixels[offset + 2] = NormalizedToByte(nz);
            }
            else
            {
                pixels[offset] = SignedToByte(r);
                pixels[offset + 1] = SignedToByte(g);
                pixels[offset + 2] = SignedToByte(b);
            }

            pixels[offset + 3] = SignedToByte((sbyte)source[offset + 3]);
        }
    }

    private static void DecodeRgbx(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var offset = index * 4;
            pixels[offset] = source[offset];
            pixels[offset + 1] = source[offset + 1];
            pixels[offset + 2] = source[offset + 2];
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeR8G8(DdsMipLevel mip, bool isSigned, bool normalMap, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 2;
            var targetOffset = index * 4;
            var r = isSigned ? (int)(sbyte)source[sourceOffset] : source[sourceOffset];
            var g = isSigned ? (int)(sbyte)source[sourceOffset + 1] : source[sourceOffset + 1];
            if (normalMap)
            {
                var nx = ComponentToBiasedNormal(r, isSigned);
                var ny = ComponentToBiasedNormal(g, isSigned);
                var nz = MathF.Sqrt(Math.Max(0f, 1f - nx * nx - ny * ny));
                pixels[targetOffset] = NormalizedToByte(nx);
                pixels[targetOffset + 1] = NormalizedToByte(ny);
                pixels[targetOffset + 2] = NormalizedToByte(nz);
            }
            else
            {
                pixels[targetOffset] = ComponentToByte(r, isSigned);
                pixels[targetOffset + 1] = ComponentToByte(g, isSigned);
                pixels[targetOffset + 2] = 0;
            }

            pixels[targetOffset + 3] = 255;
        }
    }

    private static void DecodeR8(DdsMipLevel mip, bool isSigned, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var value = ComponentToByte(isSigned ? (sbyte)source[index] : source[index], isSigned);
            var offset = index * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeA8(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var value = source[index];
            var offset = index * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeA8L8(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 2;
            var luminance = source[sourceOffset];
            var alpha = source[sourceOffset + 1];
            var offset = index * 4;
            pixels[offset] = luminance;
            pixels[offset + 1] = luminance;
            pixels[offset + 2] = luminance;
            pixels[offset + 3] = alpha;
        }
    }

    private static void DecodeR16(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var value = (byte)(BinaryPrimitives.ReadUInt16LittleEndian(source[(index * 2)..]) >> 8);
            var offset = index * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeR10G10B10A2(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(source[(index * 4)..]);
            var offset = index * 4;
            pixels[offset] = (byte)((value & 0x3FF) >> 2);
            pixels[offset + 1] = (byte)(((value >> 10) & 0x3FF) >> 2);
            pixels[offset + 2] = (byte)(((value >> 20) & 0x3FF) >> 2);
            pixels[offset + 3] = (byte)(((value >> 30) & 0x3) * 85);
        }
    }

    private static void DecodeR16Float(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var value = FloatToByte((float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(source[(index * 2)..])));
            var offset = index * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeR16G16Float(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 4;
            var r = FloatToByte((float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(source[sourceOffset..])));
            var g = FloatToByte((float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(source[(sourceOffset + 2)..])));
            var offset = index * 4;
            pixels[offset] = r;
            pixels[offset + 1] = g;
            pixels[offset + 2] = 0;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeRgba16Float(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 8;
            var offset = index * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var value = (float)BitConverter.UInt16BitsToHalf(
                    BinaryPrimitives.ReadUInt16LittleEndian(source[(sourceOffset + channel * 2)..]));
                pixels[offset + channel] = FloatToByte(value);
            }
        }
    }

    private static void DecodeR32Float(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var value = FloatToByte(BitConverter.UInt32BitsToSingle(
                BinaryPrimitives.ReadUInt32LittleEndian(source[(index * 4)..])));
            var offset = index * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeR32G32Float(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 8;
            var offset = index * 4;
            pixels[offset] = FloatToByte(BitConverter.UInt32BitsToSingle(
                BinaryPrimitives.ReadUInt32LittleEndian(source[sourceOffset..])));
            pixels[offset + 1] = FloatToByte(BitConverter.UInt32BitsToSingle(
                BinaryPrimitives.ReadUInt32LittleEndian(source[(sourceOffset + 4)..])));
            pixels[offset + 2] = 0;
            pixels[offset + 3] = 255;
        }
    }

    private static void DecodeRgba32Float(DdsMipLevel mip, byte[] pixels)
    {
        var source = mip.Data.Span;
        for (var index = 0; index < mip.Width * mip.Height; index++)
        {
            var sourceOffset = index * 16;
            var offset = index * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var value = BitConverter.UInt32BitsToSingle(
                    BinaryPrimitives.ReadUInt32LittleEndian(source[(sourceOffset + channel * 4)..]));
                pixels[offset + channel] = FloatToByte(value);
            }
        }
    }

    private static byte FloatToByte(float value)
    {
        if (float.IsNaN(value))
        {
            return 0;
        }

        return (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f), 0, 255);
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int height,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        var offset = (y * width + x) * 4;
        pixels[offset] = r;
        pixels[offset + 1] = g;
        pixels[offset + 2] = b;
        pixels[offset + 3] = a;
    }

    private static byte ComponentToByte(int component, bool isSigned) =>
        isSigned ? SignedToByte(component) : (byte)Math.Clamp(component, 0, 255);

    private static float ComponentToBiasedNormal(int component, bool isSigned) =>
        Math.Clamp(2f * component / (isSigned ? 127f : 255f) - 1f, -1f, 1f);

    private static byte SignedToByte(int value) =>
        (byte)Math.Clamp(value + 128, 0, 255);

    private static byte NormalizedToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round((value * 0.5f + 0.5f) * 255f), 0, 255);
}
