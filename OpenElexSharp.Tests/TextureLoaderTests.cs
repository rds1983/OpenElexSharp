using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class TextureLoaderTests
{
    [Theory]
    [InlineData("DXT1", false, SurfaceFormat.Dxt1)]
    [InlineData("DXT1", true, SurfaceFormat.Dxt1SRgb)]
    [InlineData("DXT3", false, SurfaceFormat.Dxt3)]
    [InlineData("DXT5", true, SurfaceFormat.Dxt5SRgb)]
    public void GetDirectSurfaceFormat_MapsLegacyBlockFormats(
        string fourCc,
        bool sRGB,
        SurfaceFormat expected)
    {
        var data = sRGB
            ? TestDdsBuilder.CreateDx10(
                fourCc switch
                {
                    "DXT1" => 72u,
                    "DXT3" => 75u,
                    _ => 78u,
                },
                4,
                4,
                new byte[fourCc == "DXT1" ? 8 : 16])
            : TestDdsBuilder.CreateLegacyFourCc(
                fourCc,
                4,
                4,
                new byte[fourCc == "DXT1" ? 8 : 16]);
        var image = DdsImage.Parse(data);

        var format = TextureLoader.GetDirectSurfaceFormat(image);

        Assert.Equal(expected, format);
    }

    [Fact]
    public void GetDirectSurfaceFormat_Bc7_RequiresDecoding()
    {
        var image = DdsImage.Parse(
            TestDdsBuilder.CreateDx10(98, 4, 4, new byte[16]));

        Assert.Null(TextureLoader.GetDirectSurfaceFormat(image));
    }

    [Theory]
    [InlineData(98u, SurfaceFormat.Color)]
    [InlineData(99u, SurfaceFormat.ColorSRgb)]
    public void GetDecodedSurfaceFormat_UsesSrgbWhenDeclared(uint dxgi, SurfaceFormat expected)
    {
        var image = DdsImage.Parse(
            TestDdsBuilder.CreateDx10(dxgi, 4, 4, new byte[16]));

        Assert.Equal(expected, TextureLoader.GetDecodedSurfaceFormat(image));
    }

    [Fact]
    public void CreateFallbackPixels_IsCheckerboard()
    {
        var pixels = TextureLoader.CreateFallbackPixels(16);

        Assert.Equal(16 * 16, pixels.Length);
        Assert.Equal(new Color(255, 0, 255), pixels[0]);
        Assert.Equal(new Color(24, 24, 24), pixels[8]);
    }

    [Fact]
    public void CreateFallbackPixels_InvalidSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureLoader.CreateFallbackPixels(0));
    }

    [Fact]
    public void Validate2D_Volume_Throws()
    {
        var image = DdsImage.Parse(
            TestDdsBuilder.CreateUncompressed(
                4,
                4,
                32,
                0x000000FF,
                0x0000FF00,
                0x00FF0000,
                0xFF000000,
                [new byte[4 * 4 * 4 * 4]],
                depth: 4));

        Assert.Throws<NotSupportedException>(() => TextureLoader.Validate2D(image));
    }

    [Fact]
    public void Validate2D_Cubemap_Throws()
    {
        var face = new byte[8];
        var image = DdsImage.Parse(
            TestDdsBuilder.CreateDx10(71, 4, 4, 1, true, 1, 1, face, face, face, face, face, face));

        Assert.Throws<NotSupportedException>(() => TextureLoader.Validate2D(image));
    }

    [Fact]
    public void Validate2D_Array_Throws()
    {
        var face = new byte[8];
        var image = DdsImage.Parse(
            TestDdsBuilder.CreateDx10(71, 4, 4, 1, false, 2, 1, face, face));

        Assert.Throws<NotSupportedException>(() => TextureLoader.Validate2D(image));
    }

    [Fact]
    public void Load_NullDevice_Throws()
    {
        var image = DdsImage.Parse(
            TestDdsBuilder.CreateLegacyFourCc("DXT1", 4, 4, new byte[8]));

        Assert.Throws<ArgumentNullException>(() => TextureLoader.Load(null!, image));
    }
}
