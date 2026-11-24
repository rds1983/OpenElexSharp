using System.Buffers.Binary;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class DdsTextureDecoderTests
{
    [Fact]
    public void Decode_Bgra_ReordersAndPreservesAlpha()
    {
        var mip = new DdsMipLevel(1, 1, 1, new byte[] { 0x10, 0x20, 0x30, 0x40 });

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bgra8);

        Assert.Equal(new byte[] { 0x30, 0x20, 0x10, 0x40 }, level.Pixels);
    }

    [Fact]
    public void Decode_Bgrx_ForcesOpaqueAlpha()
    {
        var mip = new DdsMipLevel(1, 1, 1, new byte[] { 0x10, 0x20, 0x30, 0x00 });

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bgrx8, hasAlpha: false);

        Assert.Equal(new byte[] { 0x30, 0x20, 0x10, 0xFF }, level.Pixels);
    }

    [Fact]
    public void Decode_Rgba16Float_ConvertsAndClamps()
    {
        var mip = new DdsMipLevel(
            1,
            1,
            1,
            new byte[] { 0x00, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x38 });

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Rgba16Float);

        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0x80 }, level.Pixels);
    }

    [Fact]
    public void Decode_R10G10B10A2_UnpacksChannels()
    {
        var value = 0x3FFu | (0x200u << 10) | (0x100u << 20) | (2u << 30);
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        var mip = new DdsMipLevel(1, 1, 1, data);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.R10G10B10A2);

        Assert.Equal(new byte[] { 0xFF, 0x80, 0x40, 0xAA }, level.Pixels);
    }

    [Fact]
    public void Decode_A8L8_ExpandsLuminanceAndAlpha()
    {
        var mip = new DdsMipLevel(1, 1, 1, new byte[] { 0x40, 0x80 });

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.A8L8);

        Assert.Equal(new byte[] { 0x40, 0x40, 0x40, 0x80 }, level.Pixels);
    }

    [Fact]
    public void Decode_R8_ExpandsToGrayscale()
    {
        var mip = new DdsMipLevel(1, 1, 1, new byte[] { 0x80 });

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.R8);

        Assert.Equal(new byte[] { 0x80, 0x80, 0x80, 0xFF }, level.Pixels);
    }

    [Fact]
    public void Decode_Bc4Unorm_ExpandsToGrayscale()
    {
        var block = TestDdsBuilder.CreateBc4Block(255, 0);
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc4);

        Assert.Equal(64, level.Pixels.Length);
        for (var index = 0; index < 16; index++)
        {
            Assert.Equal(255, level.Pixels[index * 4]);
            Assert.Equal(255, level.Pixels[index * 4 + 3]);
        }
    }

    [Fact]
    public void Decode_Bc4Signed_MapsNegativeEndpoint()
    {
        var block = TestDdsBuilder.CreateBc4Block(0x81, 0x7F);
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc4, isSigned: true);

        Assert.Equal(1, level.Pixels[0]);
    }

    [Fact]
    public void Decode_Bc5SignedWithoutNormalMap_BiasesChannels()
    {
        var block = TestDdsBuilder.CreateBc5Block(127, 129, 129, 127);
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc5, isSigned: true);

        Assert.Equal(new byte[] { 255, 1, 0, 255 }, level.Pixels[..4]);
    }

    [Fact]
    public void Decode_RgbaSigned_BiasesChannels()
    {
        var mip = new DdsMipLevel(
            1,
            1,
            1,
            new byte[] { 0x81, 0x00, 0x7F, 0x80 });

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Rgba8, isSigned: true);

        Assert.Equal(new byte[] { 1, 128, 255, 0 }, level.Pixels);
    }

    [Fact]
    public void Decode_Bc5Unorm_KeepsRedAndGreenChannels()
    {
        var block = TestDdsBuilder.CreateBc5Block(255, 0, 0, 255);
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc5);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, level.Pixels[..4]);
    }

    [Fact]
    public void Decode_Bc5SignedNormalMap_ReconstructsBlueChannel()
    {
        var block = TestDdsBuilder.CreateBc5Block(127, 129, 127, 129);
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(
            mip,
            DdsFormat.Bc5,
            isSigned: true,
            options: new DdsDecodeOptions(NormalMap: true));

        Assert.Equal(new byte[] { 255, 255, 128, 255 }, level.Pixels[..4]);
    }

    [Fact]
    public void Decode_Bc5SignedNormalMap_TreatsChannelsAsBiased()
    {
        var block = TestDdsBuilder.CreateBc5Block(64, 64, 64, 64);
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(
            mip,
            DdsFormat.Bc5,
            isSigned: true,
            options: new DdsDecodeOptions(NormalMap: true));

        Assert.Equal(new byte[] { 129, 129, 255, 255 }, level.Pixels[..4]);
    }

    [Fact]
    public void Decode_Bc1_ProducesRgbaPixels()
    {
        var block = Convert.FromHexString("00F81F0000005555");
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc1);

        Assert.True(level.Pixels[0] > level.Pixels[2]);
        Assert.True(level.Pixels[8 * 4 + 2] > level.Pixels[8 * 4]);
    }

    [Fact]
    public void Decode_Bc6H_ClampsHdrToRgba8()
    {
        var block = Convert.FromHexString("A339009A6B0E80E60000000000000000");
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc6H);

        Assert.True(level.Pixels[0] > 100);
        Assert.True(level.Pixels[2] > 100);
        Assert.Equal(255, level.Pixels[3]);
    }

    [Fact]
    public void Decode_Bc7_ProducesRgbaPixels()
    {
        var block = Convert.FromHexString("C03F000000FCFFFF01000000FFFFFFFF");
        var mip = new DdsMipLevel(4, 4, 1, block);

        var level = DdsTextureDecoder.Decode(mip, DdsFormat.Bc7);

        Assert.True(level.Pixels[0] > 200);
        Assert.True(level.Pixels[2] < 32);
        Assert.True(level.Pixels[8 * 4 + 2] > 200);
        Assert.True(level.Pixels[8 * 4] < 32);
    }

    [Fact]
    public void Decode_Volume_Throws()
    {
        var mip = new DdsMipLevel(4, 4, 2, new byte[128]);

        Assert.Throws<NotSupportedException>(() => DdsTextureDecoder.Decode(mip, DdsFormat.Rgba8));
    }

    [Fact]
    public void Decode_WrongDataLength_Throws()
    {
        var mip = new DdsMipLevel(4, 4, 1, new byte[12]);

        Assert.Throws<DdsFormatException>(() => DdsTextureDecoder.Decode(mip, DdsFormat.Bc1));
    }

    [Fact]
    public void Decode_NullMip_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DdsTextureDecoder.Decode(null!, DdsFormat.Bc1));
    }

    [Fact]
    public void Decode_ImageMipOutOfRange_Throws()
    {
        var data = TestDdsBuilder.CreateLegacyFourCc("DXT1", 4, 4, new byte[8]);
        var image = DdsImage.Parse(data);

        Assert.Throws<ArgumentOutOfRangeException>(() => DdsTextureDecoder.Decode(image, 1));
    }
}
