using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Graphics;

/// <summary>Uploads parsed DDS textures to MonoGame textures.</summary>
public static class TextureLoader
{
    private const int FallbackSize = 64;

    /// <summary>
    /// Returns the MonoGame surface format that can be uploaded without CPU decoding, or
    /// <see langword="null"/> when the texture must be decoded to RGBA first.
    /// </summary>
    public static SurfaceFormat? GetDirectSurfaceFormat(DdsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.Format switch
        {
            DdsFormat.Bc1 => image.IsSrgb ? SurfaceFormat.Dxt1SRgb : SurfaceFormat.Dxt1,
            DdsFormat.Bc2 => image.IsSrgb ? SurfaceFormat.Dxt3SRgb : SurfaceFormat.Dxt3,
            DdsFormat.Bc3 => image.IsSrgb ? SurfaceFormat.Dxt5SRgb : SurfaceFormat.Dxt5,
            _ => null,
        };
    }

    /// <summary>Returns the RGBA surface format used when a texture requires CPU decoding.</summary>
    public static SurfaceFormat GetDecodedSurfaceFormat(DdsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.IsSrgb ? SurfaceFormat.ColorSRgb : SurfaceFormat.Color;
    }

    /// <summary>Validates that a texture can be uploaded as a single 2D texture.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The texture is a volume, cubemap or array.</exception>
    public static void Validate2D(DdsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.IsVolume)
        {
            throw new NotSupportedException($"Texture '{image.FormatName}' is a volume texture.");
        }

        if (image.IsCubemap)
        {
            throw new NotSupportedException($"Texture '{image.FormatName}' is a cubemap.");
        }

        if (image.ArraySize != 1 || image.Faces.Count != 1)
        {
            throw new NotSupportedException($"Texture '{image.FormatName}' is a texture array.");
        }
    }

    /// <summary>
    /// Loads a DDS texture into a <see cref="Texture2D"/>. BC1, BC2 and BC3 are uploaded
    /// compressed; BC4, BC5, BC6H, BC7 and uncompressed variants are decoded to RGBA first.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The texture is a volume, cubemap or array.</exception>
    public static Texture2D Load(
        GraphicsDevice device,
        DdsImage image,
        DdsDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(image);
        options ??= new DdsDecodeOptions();
        Validate2D(image);

        var directFormat = GetDirectSurfaceFormat(image);
        if (directFormat is not null && !options.NormalMap)
        {
            var texture = new Texture2D(
                device,
                image.Width,
                image.Height,
                image.MipCount > 1,
                directFormat.Value);
            for (var mip = 0; mip < image.MipCount; mip++)
            {
                var data = image.Faces[0].Mipmaps[mip].Data.ToArray();
                texture.SetData(mip, null, data, 0, data.Length);
            }

            return texture;
        }

        var format = GetDecodedSurfaceFormat(image);
        var decodedTexture = new Texture2D(
            device,
            image.Width,
            image.Height,
            image.MipCount > 1,
            format);
        for (var mip = 0; mip < image.MipCount; mip++)
        {
            var level = DdsTextureDecoder.Decode(image, mip, options);
            decodedTexture.SetData(
                mip,
                null,
                level.Pixels,
                0,
                level.Pixels.Length);
        }

        return decodedTexture;
    }

    /// <summary>Creates a magenta/black checkerboard used when an asset cannot be loaded.</summary>
    public static Texture2D CreateFallback(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var texture = new Texture2D(device, FallbackSize, FallbackSize, false, SurfaceFormat.Color);
        texture.SetData(CreateFallbackPixels());
        return texture;
    }

    /// <summary>Creates the fallback checkerboard pixels. Exposed for tests and tools.</summary>
    public static Color[] CreateFallbackPixels(int size = FallbackSize)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var colors = new Color[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var checker = ((x / 8) + (y / 8)) % 2;
                colors[y * size + x] = checker == 0
                    ? new Color(255, 0, 255)
                    : new Color(24, 24, 24);
            }
        }

        return colors;
    }
}
