# Terrain heightmaps (`GESC` in `staticscene` blobs)

This document describes the terrain heightmap found in the compiled static-scene blobs and the
runtime that turns it into chunk meshes. Sector entities and their formats are covered in
[Worlds.md](Worlds.md); the renderer is covered in [Rendering.md](../Rendering.md).

## Purpose and location

`World.wrl` names `#G3:/data/raw/terrain3/terrain3.trn` as the world terrain resource
([Worlds.md](Worlds.md)), but that path is not present in the retail installation: there is no
`data/raw` tree and no mount entry for a `.trn` file. Instead, every compiled static-scene tile
carries its own heightmap:

```text
data/compiled/staticscene/World/World_00_<blockX>_<blockZ>/World_00_<chunkX>_<chunkZ>.blob
```

The blobs are the same files that hold per-tile static objects. Their `GESC` section starts with a
terrain tile header followed by the height samples; the rest of the section contains static-object
data and is not decoded by the terrain loader.

## Container

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | `GAR5` magic (`47 41 52 35`). |
| 8 | `uint32` | Little-endian marker `0xFEEDFACE` (observed `CE FA ED FE` bytes). |
| 12 | `uint32` | Wrapper payload size. |
| 24 | `char[4]` | `GESC` section magic. |
| 28 | `uint32` | Section version; observed `1`. |
| 32 | `uint32` | Embedded section count; observed `1`–`4`. |
| 36 | `uint32` | `GESC` section payload size in bytes. |

The bytes between the section header and the tile header are variable-length preamble/bounds data.
A reader must not use a fixed tile-header offset.

## Tile header

The tile header is 32 bytes of little-endian values and is located by its invariant fields. All
observed tiles use these values:

| Offset | Type | Meaning | Observed |
| ---: | --- | --- | --- |
| 0 | `float32` | Minimum world Z of the tile | chunk-dependent |
| 4 | `float32` | Base world Y added to scaled heights | `0` |
| 8 | `float32` | Minimum world X of the tile | chunk-dependent |
| 12 | `float32` | Tile extent along X | `3200` |
| 16 | `float32` | World units per height sample | `3.125` |
| 20 | `float32` | Tile extent along Z | `3200` |
| 24 | `uint32` | Reserved | `0` |
| 28 | `uint32` | Reserved | `0` |

Note the field order: **Z, Y, X**. The world-space origin of a tile is
(`minX`, `minY`, `minZ`), and the tile covers `[minX, minX + sizeX)` × `[minZ, minZ + sizeZ)`.

Example from a flat tile (`World_00_000_000.blob`, header at file offset `0x38`):

```text
00 00 c8 c8  00 00 00 00  00 00 c8 c8  00 00 48 45
00 00 48 40  00 00 48 45  00 00 00 00  00 00 00 00
```

which decodes to `minZ = -409600`, `minY = 0`, `minX = -409600`, `sizeX = 3200`,
`heightScale = 3.125`, `sizeZ = 3200`, two zero reserved words.

## Height samples

Immediately after the 32-byte header:

| Property | Value |
| --- | --- |
| Resolution | **137 × 137** samples |
| Sample count | `137² = 18,769` |
| Sample type | `uint16` little-endian |
| Order | **column-major in the blob**: outer index X, inner index Z. The parser transposes once to logical row-major (row = Z, column = X). |
| Sample spacing | `sizeX / 136` and `sizeZ / 136` (about 23.53 world units) |
| World Y | `minY + sample * heightScale` |

The last sample of a row and the first sample of the next row line up in the data. Adjacent tiles do
**not** store identical edge heights, however: checked pairs differ by a few units up to several
thousand raw samples. The runtime averages every perimeter sample shared by neighbouring tiles into
one value and blends the correction over a 32-sample band, so the rendered top surfaces meet
smoothly (see below).

Row width 137 is confirmed by continuity: for the Abessa and fort tiles, the average absolute
difference between adjacent rows is minimal at stride 137 and rises sharply at 128, 136, 138 and 144.
A square grid is smooth in both row-major and column-major order, so the **orientation** was
determined by adjacent-tile edge consistency instead: transposing the samples reduces the mean edge
difference from about 487 to 195 raw units across the Abessa block and from 198 to 107 across the
Tavār fort block. Rendering the untransposed data made neighbouring tiles disagree and produced the
rough seams visible in earlier captures.

## World mapping and file names

The world starts at `(-409600, -409600)` on the X/Z plane and uses 3200-unit tiles:

```text
chunkX = floor((worldX - -409600) / 3200)
chunkZ = floor((worldZ - -409600) / 3200)
```

The tile header of the blob must agree with those indices. For example,
`World_00_141_058.blob` has `minX = 41600` and `minZ = -224000`, which are
`-409600 + 141 * 3200` and `-409600 + 58 * 3200`.

Block directories group 16 × 16 chunks:

```text
blockX = chunkX / 16        (integer division)
blockZ = chunkZ / 16
path   = data/compiled/staticscene/World/World_00_{blockX:00}_{blockZ:00}/
         World_00_{chunkX:000}_{chunkZ:000}.blob
```

## Chunk stitching and skirts

Adjacent tiles do not share exact edge heights. Across the 81-tile TAV fort block the largest raw
edge differences are about 11,000 samples, with many edges in the 1,500–2,400 range. The X/Z tile
origins are exact, so these are vertical cracks rather than horizontal overlaps.

`TerrainScene` makes the rendered surface seamless in three steps:

1. **Stitch shared samples.** Every perimeter sample of every loaded tile is mapped to a global
   sample coordinate (`tileIndex * 136 + localIndex`). All tiles that contribute to the same global
   coordinate are averaged, including four-tile corners, and the average is written back to every
   owner. The correction is blended into a 32-sample band along each edge so the transition is not
   compressed into a single sample. Interior samples beyond that band are untouched.
2. **Compute normals on the global grid.** The normal at a tile vertex samples the stitched heights
   one global step to the left, right, back and front, even when those samples belong to a
   neighbouring tile. Both sides of a seam therefore get the same normal and the lighting does not
   break at the tile border.
3. **Skirt only unshared edges.** A 200-unit skirt is added only where no neighbour tile is loaded,
   hiding the outer boundary of the loaded block. Shared edges need no skirt and get none, which
   also avoids two coplanar skirt walls z-fighting along every seam.

The result is a continuous surface across the whole loaded block. Earlier per-edge skirts of up to
8,000 units covered the holes but left dark grid lines because the top surfaces still disagreed;
stitching removes the cause instead of hiding it. This follows the same principle as Nursia's
terrain patches: edges sampled from one heightfield agree, and skirts are only a fallback for
discontinuities such as LOD transitions or the outer boundary.

The original blob heights are still what the parser reads; the parser transposes the column-major
samples once, and stitching/blending happen in memory before meshes are built.

Tests cover a two-tile shared edge and a four-tile corner in
`OpenElexSharp.Tests/TerrainSceneTests.cs`.

## Vertical alignment

The X/Z mapping and the relative relief of each tile are consistent with the static entities, but
the absolute Y from `sample * 3.125` does not line up with entity positions everywhere:

| Location | Tile | Raw height range | `sample * 3.125` | Entity ground |
| --- | --- | ---: | ---: | ---: |
| Tavar fort | `World_00_141_058.blob` | 3988–4136 | ≈ 12.5k–12.9k | ≈ 30.6k |
| Abessa city | `World_00_067_095.blob` | 11289–12615 | ≈ 35.3k–39.4k | ≈ 41.1k |

The current runtime therefore applies one constant vertical offset per loaded terrain block so that
the **average** terrain height matches an estimated ground level from the loaded sector. The
estimated ground is the 10th percentile of the mesh boxes' bottom Y values in `SectorScene`; this
ignores the lowest outlying props while staying below most building origins.

This is an approximation. The raw height formula is reliable for relative relief within and between
nearby tiles, but the absolute origin or per-region base needs further research (see open questions).

## Edge tiles and border bands

Some tiles at the edge of the populated area have a flat interior and a 1–2 sample band of much
higher values along their outer columns. Example: `World_00_144_066.blob` has an interior of
`3986` and a border rising to `16067`. Neighbouring tiles show the same pattern with values up to
`18101`.

These bands look like terrain walls or LOD/map-edge skirts rather than ordinary slopes. The current
loader renders them as-is; they can appear as tall cliff spikes at the edges of a terrain block.
They are kept because they may be intentional map edges, but clamping or skipping the band is a
candidate follow-up once the GESC metadata around the tile header is decoded.

## Evidence

- `data/compiled/staticscene/World/World_00_00_00/World_00_000_000.blob`: `GESC` at `0x18`,
  tile header at `0x38`, heights at `0x58`, flat value `36`; tail contains `TRNH`.
- `data/compiled/staticscene/World/World_00_08_03/World_00_141_058.blob`: tile header at `0xD8`
  (`minZ = -224000`, `minX = 41600`), heights `3988`–`4136`.
- `data/compiled/staticscene/World/World_00_04_05/World_00_067_095.blob`: tile header at `0xE8`
  (`minZ = -105600`, `minX = -195200`), heights `11289`–`12615`; the Abessa hut entity is at
  approximately `(-194880, 37355, -102631)`, and its terrain sample gives ≈ 35.9k before
  alignment.
- The sample count `18,769 = 137²` admits only a 137 × 137 grid (or a single row), which fixes the
  resolution without a header field.
- Transposing the stored grid reduces the mean absolute difference between adjacent tile edges from
  about 487 to 195 raw units across the Abessa block and from 198 to 107 across the TAV fort block.
  That is the evidence for column-major storage; the former row-major interpretation produced
  visible mismatches with static geometry and rough seams.
- The four static-object section count values `1`, `3` and `4` do not change the tile layout; the
  header scanner finds exactly one matching tile header per blob.

## Implementation status

| Piece | Status |
| --- | --- |
| `ElexTerrainChunk` parser | Implemented; validates `GAR5`/`GESC`, scans for the invariant tile header, reads 137² samples and transposes the stored column-major grid to logical row-major. |
| `TerrainScene` | Implemented; loads 1–4 tiles around a sector center, stitches shared perimeter samples with a 32-sample blend, computes normals on the stitched global grid, builds one `DrMesh` per tile and submits through `Renderer`. |
| `--terrain` | Implemented; loads terrain around the current `--sector` bounds. |
| Materials | One shared stock material (`TR_Edan_Grass_01`) for every tile until terrain splatting is decoded. |
| Seam handling | Shared edge/corner samples averaged in memory; normals sampled globally; 200-unit skirts only on the outer block boundary. |
| Vertical alignment | Approximate per-block offset from the sector ground estimate. |
| Terrain holes, splatting, LOD | Not implemented. |

Measured on the reference installation at 1280 × 720, culling on:

| Sector | Chunks | Radius | Offset | Terrain bounds (Y) | Draw calls | Triangles | FPS |
| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: |
| `TAV_Fort_Buildings` | 81 | 4 | 17,659 | 29,915–67,722 | 2,617 | 3,866,422 | 60 |
| `ABE_AbessaCity_Buildings` | 81 | 4 | 2,469 | 32,014–47,801 | 1,669 | 3,301,616 | 60 |

## Open questions

- Why do adjacent tiles not share identical edge samples? The runtime averages them; the game may use
  a different edge convention, overlap or another source layer.
- Is `heightScale = 3.125` the final world multiplier for every region, with a separate per-region
  Y base stored outside the tile header? The tile header reserves two zero words, and the `GESC`
  count/preamble is not fully decoded.
- What do the `GESC` count and the static-object sections before `GEGC`/`TRNH` mean? They may
  include material-layer or hole-mask metadata needed for splatting.
- Are the high edge bands real cliffs, LOD stitching geometry or a different sample layer? The
  runtime averages them with neighbours, which can form a steep ridge.
- The UV convention (8 repeats per tile in the runtime) is a visual choice, not decoded from the
  asset.
- No LOD or hole-mask handling; every tile is a full 137² grid.

## References

The `GESC` layout was derived from the retail assets with the project's existing `GAR5`/`GEC2`
readers. The seam strategy was informed by
[Nursia](https://github.com/Nursia3D/Nursia) (MIT): terrain patches sample one shared heightfield,
and skirts cover discontinuities such as LOD transitions. No Nursia code was copied.

## Related tests

- `OpenElexSharp.Tests/ElexTerrainChunkTests.cs` - synthetic `GAR5`/`GESC` fixtures: valid header,
  sample-to-world mapping, column-major transpose, missing wrapper/section, invalid tile header,
  truncated heights, null arguments.
- `OpenElexSharp.Tests/TerrainSceneTests.cs` — chunk index rounding, blob-path formatting and
  shared-edge/four-tile-corner stitching.

## Related tasks

- T109 — terrain heightmap loading and rendering (current work).
- T105 — sky/lighting; terrain currently uses the T104 fog and a fixed BasicEffect rig.
- T401/T108 — material flags, splatting and terrain decoration.
- T406 — LOD, instancing and dense-scene performance.
