# Textures (DDS image resources)

ELEX image resources (`img` / `.eleximg`) are standard DirectDraw Surface (DDS) files wrapped in a
small ELEX resource envelope. The engine parses the DDS header, validates the mip/face layout, uploads
BC1/BC2/BC3 directly to MonoGame and decodes BC4/BC5/BC6H/BC7 plus uncompressed variants to RGBA8.

## References

- [Microsoft DXGI_FORMAT](https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format)
  for the DX10 format identifiers.
- [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0), `mimicry/source/MimicryApp/elexresman.cpp`,
  for the ELEX image wrapper, the legacy FourCC table and the DXGI table.
- [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) (MIT OR Unlicense) for BC6H/BC7 CPU decoding.

## Resource layout

ELEX `.rom` image resources use a 44-byte GAR5 header followed by a DDS file and a 36-byte
E1RF-style offset-table trailer:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `GAR5`. |
| 4 | `uint32` | Header value `0x20`. |
| 8 | `byte[36]` | Zero/metadata bytes. |
| 44 | `char[4]` | DDS magic `DDS `. |
| 44 + DDS length | `byte[36]` | Offset table: `uint32 44`, `uint32 ddsLength`, `uint8 0`, then three `{uint32 offset, uint32 0, uint8 0}` entries. The first offset is `44 + ddsLength`. |

The parser locates the DDS header at offset 44 for `GAR5` resources and also accepts loose files
that start directly with `DDS `. Only the declared DDS header and pixel data are used; trailing
bytes are reported as `TrailingLength` and ignored.

Example (`data/compiled/0_na_img/4HousesShot_01_TAVfactory.eleximg`):

```text
47 41 52 35 20 00 00 00 00 ... 00 44 44 53 20 7c 00 00 00
GAR5 ....                     DDS |....
```

## DDS header

All integers are little-endian. The header is the standard 124-byte DDS layout:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `DDS `. |
| 4 | `uint32` | Header size, `124`. |
| 8 | `uint32` | Flags (`DDSD_*`). |
| 12 | `uint32` | Height in pixels. |
| 16 | `uint32` | Width in pixels. |
| 20 | `uint32` | Pitch or linear size. |
| 24 | `uint32` | Depth; 1 for 2D textures. |
| 28 | `uint32` | Mip count when `DDSD_MIPMAPCOUNT` is set. |
| 32 | `byte[44]` | Reserved. |
| 76 | `uint32` | Pixel-format size, `32`. |
| 80 | `uint32` | Pixel-format flags (`DDPF_*`). |
| 84 | `uint32` | FourCC, or 0 for uncompressed RGB/luminance data. |
| 88 | `uint32` | RGB bit count. |
| 92 | `uint32` | Red mask. |
| 96 | `uint32` | Green mask. |
| 100 | `uint32` | Blue mask. |
| 104 | `uint32` | Alpha mask. |
| 108 | `uint32` | Caps. |
| 112 | `uint32` | Caps2 (cubemap/volume flags). |
| 116 | `uint32` | Caps3. |
| 120 | `uint32` | Caps4. |
| 124 | `uint32` | Reserved2. |
| 128 | `byte[20]` | DX10 header when FourCC is `DX10`. |

### DX10 header

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | DXGI format. |
| 4 | `uint32` | Resource dimension: 2 = 1D, 3 = 2D, 4 = 3D. |
| 8 | `uint32` | Misc flags; `0x4` = cubemap. |
| 12 | `uint32` | Array size, or depth for 3D textures. |
| 16 | `uint32` | Misc flags 2. |

## Format mapping

### DXGI formats observed in retail data

| DXGI | Format | Count | sRGB |
| ---: | --- | ---: | --- |
| 29 | R8G8B8A8_UNORM_SRGB | 105 | yes |
| 31 | R8G8B8A8_SNORM | 1 | no |
| 72 | BC1_UNORM_SRGB | 1,691 | yes |
| 75 | BC2_UNORM_SRGB | 10 | yes |
| 78 | BC3_UNORM_SRGB | 77 | yes |
| 98 | BC7_UNORM | 1,983 | no |
| 99 | BC7_UNORM_SRGB | 1 | yes |

### Legacy FourCC formats observed in retail data

| FourCC | Format | Count |
| --- | --- | ---: |
| `BC4U` | BC4_UNORM | 3,269 |
| `BC5S` | BC5_SNORM | 1,335 |
| `BC5U` | BC5_UNORM | 929 |
| `DXT1` | BC1_UNORM | 72 |
| `DXT5` | BC3_UNORM | 18 |
| `DXT3` | BC2_UNORM | 11 |
| `0x71` | R16G16B16A16_FLOAT (`D3DFMT_A16B16G16R16F`) | 2 |

### Uncompressed masks observed in retail data

| Bit count | R / G / B / A masks | Format | Count |
| ---: | --- | --- | ---: |
| 32 | `00ff0000 / 0000ff00 / 000000ff / ff000000` | B8G8R8A8 | 81 |
| 32 | `000000ff / 0000ff00 / 00ff0000 / ff000000` | R8G8B8A8 | 25 |
| 16 | `ff / 0 / 0 / ff00` (luminance + alpha) | A8L8 | 16 |
| 8 | `ff / 0 / 0 / 0` (luminance) | L8 | 5 |

The parser also recognizes the standard ATI1/ATI2 aliases, DXT2/DXT4, BC4S, B8G8R8X8 and
R8G8B8X8 masks so that mods and future data sets are handled predictably. Unknown FourCCs and
unrecognized masks fail with a `DdsFormatException` that names the offending fields.

## Data layout

Pixel data starts immediately after the DDS header (and DX10 header when present). For a single
face the mip chain is stored largest to smallest. Each mip is tightly packed with no row padding:

- Compressed formats occupy `max(1, ceil(width / 4)) * max(1, ceil(height / 4)) * blockSize` bytes.
  BC1/BC4 blocks are 8 bytes; BC2/BC3/BC5/BC6H/BC7 blocks are 16 bytes.
- Uncompressed formats occupy `width * height * bytesPerPixel`.
- Volume mip levels also halve their depth, so a `4x4x4` volume with two mips stores `4x4x4` and
  `2x2x2` slices.
- Cubemap and array faces are stored face-major; each face has its own mip chain.

The loader rejects truncated pixel data, invalid dimensions, unsupported formats and impossible
mip counts before allocating upload buffers.

## Decoding and upload

| Format | Upload path |
| --- | --- |
| BC1, BC2, BC3 | Direct compressed upload as `Dxt1`/`Dxt3`/`Dxt5`, with the sRGB variants when the DDS declares sRGB. |
| BC4, BC5 | CPU decode to RGBA8 with the in-tree BC4/BC5 decoder, then upload as `Color` or `ColorSRgb`. |
| BC6H, BC7 | CPU decode to RGBA8 with BCnEncoder.NET, then upload as `Color` or `ColorSRgb`. BC6H values are clamped to `[0, 1]`. |
| R8G8B8A8, B8G8R8A8, B8G8R8X8, R8G8, R8, A8, A8L8, R16, R10G10B10A2 | CPU conversion to RGBA8. |
| R16F, R16G16F, R16G16B16A16F, R32F, R32G32F, R32G32B32A32F | CPU conversion to RGBA8, clamped to `[0, 1]`. |

BC4 is displayed as grayscale. BC5 is treated as a two-channel tangent-space normal map when the
caller requests normal-map decoding: the stored channels are biased normal components, so
`x = 2 * c / max - 1`, `y = 2 * c / max - 1`, `z = sqrt(1 - x*x - y*y)`. Retail `BC5S` normals
cluster around 64 (not 0), confirming the biased convention. The game test scene enables this for
resources whose names contain `_Nm`.

Legacy DXT and uncompressed formats do not carry an sRGB flag; they are treated as linear. Only
DX10 sRGB formats select an sRGB MonoGame surface.

## Evidence

The first retail image (`data/compiled/0_na_img/4HousesShot_01_TAVfactory.eleximg`)
starts with the 44-byte wrapper and then:

```text
44 44 53 20 7c 00 00 00 0f 10 02 00 56 01 00 00 00 02 00 00 00 08 00 00 01 00 00 00
D  D  S    124           flags       h=342       w=512       pitch=2048  mips=1
```

Its DX10 header is:

```text
1d 00 00 00 03 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00
DXGI 29     dimension 3   misc 0      array 1     misc2 0
```

The trailer after the pixel data is `2c000000 <ddsLength> 00 <offset> 00000000 00 ...`,
where the first offset is `44 + ddsLength`.

A `BC5S` normal map (`AS_AlbSigns_1_Nm`) decodes to channel values clustered around 64
rather than 0, which is why the decoder treats BC5 normal channels as biased rather than
direct signed components.

## Observed retail set

| Metric | Value |
| --- | ---: |
| `img` resources | 9,631 |
| Parsed successfully | 9,631 |
| 2D textures | 9,625 |
| Volume textures | 6 |
| Cubemaps/arrays | 0 |
| sRGB textures | 1,884 |
| Distinct dimensions | 82 |
| Largest dimensions | 4,096 x 8,192 |
| Mip counts | 1 to 13 |
| Dominant formats | BC4 (3,269), BC5 (2,264), BC7 (1,984) |

All 2D resources decode to RGBA8. The six volume textures (colour LUTs and noise volumes) are
parsed but rejected by the `Texture2D` uploader; a later task can add `Texture3D` support.

## Implementation

| Component | File |
| --- | --- |
| DDS parser and format mapping | `OpenElexSharp/Assets/DdsImage.cs` |
| CPU decoder | `OpenElexSharp/Assets/DdsTextureDecoder.cs` |
| MonoGame upload and fallback | `OpenElexSharp/Graphics/TextureLoader.cs` |
| Game texture test scene | `OpenElexSharp/OpenElexGame.cs` |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` |

CLI:

```powershell
dotnet run --project OpenElexSharp -- assets textures AS_DirtMasks_X3 --properties --decode
dotnet run --project OpenElexSharp -- assets textures --limit 20
dotnet run --project OpenElexSharp -- assets textures --all
```

Test scene:

```powershell
dotnet run --project OpenElexSharp -- --texture AS_AlbSigns_1_DfA
dotnet run --project OpenElexSharp -- --texture AS_AlbSigns_1_Nm --smoke-test
```

The viewer preserves the DDS top-left origin, letterboxes the texture, uses alpha blending and
falls back to a magenta checkerboard when an asset is missing or unsupported.

Tests:

- `OpenElexSharp.Tests/DdsImageTests.cs` - wrapper, header, mip, face, volume, mask and error cases.
- `OpenElexSharp.Tests/DdsTextureDecoderTests.cs` - BC1/BC4/BC5/BC6H/BC7, signed channels,
  normal-map reconstruction, uncompressed formats and float formats.
- `OpenElexSharp.Tests/TextureLoaderTests.cs` - direct surface selection, sRGB selection and fallback.
- `OpenElexSharp.Tests/TextureConsoleTests.cs` - inspector output and synthetic resource resolution.
- `OpenElexSharp.Tests/LaunchOptionsTests.cs` - `--texture` parsing.

## Open questions

- Semantics of the three offset-table entries in the 36-byte ELEX trailer.
- Whether any legacy DXT/uncompressed texture should be treated as sRGB based on material metadata;
  the DDS header contains no such flag.
- Exact engine convention for BC5 normal maps beyond the observed biased channels; the current
  decoder reconstructs Z but does not flip the green channel.
- `Texture3D` upload is not implemented; no retail cubemap textures were observed. BC6H is decoded
  but clamped to 8-bit RGBA, and no retail BC6H texture was found.
- Float textures are clamped to 8-bit RGBA for display; a future HDR pipeline should preserve the
  original float data.
- Alpha semantics of legacy `DXT1` resources with `DDPF_ALPHAPIXELS`; none were observed.

## Related tasks

- T007 - this texture parser, decoder, uploader and test scene.
- T008 - materials will bind these textures to mesh submeshes.
- T010 - world entities will reference materials and textures.
