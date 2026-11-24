using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class DdsImageTests
{
	[Fact]
	public void Parse_Gar5WrappedDxt1_ReadsHeaderMipsAndTrailer()
	{
		var data = TestDdsBuilder.Wrap(
			TestDdsBuilder.CreateLegacyFourCc(
				"DXT1",
				8,
				8,
				new byte[32],
				new byte[8]));

		var image = DdsImage.Parse(data);

		Assert.Equal(44, image.DdsOffset);
		Assert.Equal(172, image.DataOffset);
		Assert.Equal(40, image.DataLength);
		Assert.Equal(36, image.TrailingLength);
		Assert.Equal(8, image.Width);
		Assert.Equal(8, image.Height);
		Assert.Equal(1, image.Depth);
		Assert.Equal(2, image.MipCount);
		Assert.Equal(DdsFormat.Bc1, image.Format);
		Assert.False(image.IsSrgb);
		Assert.False(image.IsVolume);
		Assert.False(image.IsCubemap);
		Assert.Single(image.Faces);
		Assert.Equal(32, image.Faces[0].Mipmaps[0].Data.Length);
		Assert.Equal(4, image.Faces[0].Mipmaps[1].Width);
		Assert.Equal(8, image.Faces[0].Mipmaps[1].Data.Length);
	}

	[Fact]
	public void Parse_Dx10Bc7Srgb_ReadsDxgiHeaderAndFormat()
	{
		var data = TestDdsBuilder.CreateDx10(99, 8, 8, new byte[64]);

		var image = DdsImage.Parse(data);

		Assert.Equal(0, image.DdsOffset);
		Assert.Equal(148, image.DataOffset);
		Assert.Equal(99u, image.DxgiFormat);
		Assert.Equal(DdsFormat.Bc7, image.Format);
		Assert.True(image.IsSrgb);
		Assert.False(image.IsSigned);
	}

	[Theory]
	[InlineData("BC4U", DdsFormat.Bc4, false, 32)]
	[InlineData("BC4S", DdsFormat.Bc4, true, 32)]
	[InlineData("BC5U", DdsFormat.Bc5, false, 64)]
	[InlineData("BC5S", DdsFormat.Bc5, true, 64)]
	[InlineData("ATI1", DdsFormat.Bc4, false, 32)]
	[InlineData("ATI2", DdsFormat.Bc5, false, 64)]
	public void Parse_LegacyBcFormats_MapsFourCc(
		string fourCc,
		DdsFormat expectedFormat,
		bool expectedSigned,
		int dataLength)
	{
		var data = TestDdsBuilder.CreateLegacyFourCc(fourCc, 8, 8, new byte[dataLength]);

		var image = DdsImage.Parse(data);

		Assert.Equal(expectedFormat, image.Format);
		Assert.Equal(expectedSigned, image.IsSigned);
	}

	[Fact]
	public void Parse_UncompressedBgra_ReadsMasks()
	{
		var data = TestDdsBuilder.CreateUncompressed(
			2,
			2,
			32,
			0x00FF0000,
			0x0000FF00,
			0x000000FF,
			0xFF000000,
			[new byte[16]]);

		var image = DdsImage.Parse(data);

		Assert.Equal(DdsFormat.Bgra8, image.Format);
		Assert.True(image.HasAlpha);
		Assert.Equal(0x00FF0000u, image.RedMask);
		Assert.Equal(0xFF000000u, image.AlphaMask);
	}

	[Fact]
	public void Parse_Dx10Rgba_ImpliesAlpha()
	{
		var image = DdsImage.Parse(
			TestDdsBuilder.CreateDx10(28, 1, 1, new byte[4]));

		Assert.True(image.HasAlpha);
	}

	[Fact]
	public void Parse_Volume_HalvesDepthPerMip()
	{
		var data = TestDdsBuilder.CreateUncompressed(
			4,
			4,
			32,
			0x000000FF,
			0x0000FF00,
			0x00FF0000,
			0xFF000000,
			[new byte[4 * 4 * 4 * 4], new byte[2 * 2 * 2 * 4]],
			depth: 4);

		var image = DdsImage.Parse(data);

		Assert.True(image.IsVolume);
		Assert.Equal(4, image.Depth);
		Assert.Equal(2, image.MipCount);
		Assert.Equal(4, image.Faces[0].Mipmaps[0].Depth);
		Assert.Equal(2, image.Faces[0].Mipmaps[1].Depth);
		Assert.Equal(256, image.Faces[0].Mipmaps[0].Data.Length);
		Assert.Equal(32, image.Faces[0].Mipmaps[1].Data.Length);
	}

	[Fact]
	public void Parse_Dx10Cubemap_ReadsSixFaces()
	{
		var face = new byte[8];
		var data = TestDdsBuilder.CreateDx10(
			71,
			4,
			4,
			1,
			true,
			1,
			1,
			face,
			face,
			face,
			face,
			face,
			face);

		var image = DdsImage.Parse(data);

		Assert.True(image.IsCubemap);
		Assert.Equal(1, image.ArraySize);
		Assert.Equal(6, image.Faces.Count);
		Assert.All(image.Faces, item => Assert.Single(item.Mipmaps));
	}

	[Fact]
	public void Parse_Dx10Array_ReadsArrayFaces()
	{
		var face = new byte[8];
		var data = TestDdsBuilder.CreateDx10(
			71,
			4,
			4,
			1,
			false,
			2,
			1,
			face,
			face);

		var image = DdsImage.Parse(data);

		Assert.False(image.IsCubemap);
		Assert.Equal(2, image.ArraySize);
		Assert.Equal(2, image.Faces.Count);
	}

	[Fact]
	public void Parse_LegacyFloatFourCc_MapsToRgba16Float()
	{
		var data = TestDdsBuilder.CreateLegacyFourCc(0x71u, 2, 2, new byte[2 * 2 * 8]);

		var image = DdsImage.Parse(data);

		Assert.Equal(DdsFormat.Rgba16Float, image.Format);
		Assert.True(image.HasAlpha);
	}

	[Fact]
	public void Parse_TooManyMips_Throws()
	{
		var data = TestDdsBuilder.CreateDx10(
			71,
			4,
			4,
			1,
			false,
			1,
			33,
			new byte[8]);

		var exception = Assert.Throws<DdsFormatException>(() => DdsImage.Parse(data));

		Assert.Contains("mip count", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Parse_UnknownUncompressedMasks_Throws()
	{
		var data = TestDdsBuilder.CreateUncompressed(
			1,
			1,
			24,
			0x00FF0000,
			0x0000FF00,
			0x000000FF,
			0,
			[new byte[3]]);

		var exception = Assert.Throws<DdsFormatException>(() => DdsImage.Parse(data));

		Assert.Contains("pixel format", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void GetSurfaceSize_HugeSurface_Throws()
	{
		Assert.Throws<DdsFormatException>(
			() => DdsImage.GetSurfaceSize(DdsFormat.Rgba8, 32768, 32768));
	}

	[Theory]
	[InlineData(95u, false)]
	[InlineData(96u, true)]
	public void Parse_Dx10Bc6H_ReadsSignedFlag(uint dxgi, bool expectedSigned)
	{
		var data = TestDdsBuilder.CreateDx10(dxgi, 4, 4, new byte[16]);

		var image = DdsImage.Parse(data);

		Assert.Equal(DdsFormat.Bc6H, image.Format);
		Assert.Equal(expectedSigned, image.IsSigned);
	}

	[Fact]
	public void Parse_TruncatedPixelData_Throws()
	{
		var data = TestDdsBuilder.CreateLegacyFourCc("DXT1", 8, 8, new byte[8]);

		var exception = Assert.Throws<DdsFormatException>(() => DdsImage.Parse(data));

		Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Parse_WrongMagic_Throws()
	{
		var data = TestDdsBuilder.CreateLegacyFourCc("DXT1", 4, 4, new byte[8]);
		data[0] = (byte)'X';

		Assert.Throws<DdsFormatException>(() => DdsImage.Parse(data));
	}

	[Fact]
	public void Parse_UnsupportedFourCc_Throws()
	{
		var data = TestDdsBuilder.CreateLegacyFourCc("ABCD", 4, 4, new byte[8]);

		var exception = Assert.Throws<DdsFormatException>(() => DdsImage.Parse(data));

		Assert.Contains("FourCC", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_InvalidDimensions_Throws()
	{
		var data = TestDdsBuilder.CreateLegacyFourCc("DXT1", 0, 4, []);

		var exception = Assert.Throws<DdsFormatException>(() => DdsImage.Parse(data));

		Assert.Contains("dimensions", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => DdsImage.Parse(null!));
	}
}
