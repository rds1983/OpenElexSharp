# Static meshes (GMSH v2)

ELEX static mesh resources use the `msh` type (`.elexmsh`) and resolve through `w_msh_*.db`
databases to hashed `.rom` files. The payload is a 44-byte GAR5 image-style wrapper followed by a
`GMSH` version 2 mesh block.

## Purpose and scope

This specification covers the mesh container, triangle indices, vertex positions and the
attribute/stream/material declarations. Normal, UV, tangent and material binding semantics are
partially decoded; see [Open questions](#open-questions).

## References

- [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0), `mimicry/source/Mimicry/mi_xmshreader.cpp`
  for the related `eCMeshResource2` property model and vertex declaration concepts.
- The embedded Genome name registry for `class eCMeshResource2`, `class eCSubMesh`,
  `class eCSubMesh` arrays, `eEGfxFormat2` and `eEMeshLodStages` names.

## Container layout

The observed `.rom` file starts with the same 44-byte GAR5 wrapper used by image resources:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `GAR5`. |
| 4 | `uint32` | Header value `0x20`. |
| 8 | `byte[36]` | Zero/metadata bytes. |
| 44 | `char[4]` | Mesh magic `GMSH`. |
| 48 | `uint32` | Version, `2`. |

The end of the file contains a `0xFEEDFACE` sentinel and a 44-byte offset table. The current parser
locates the `GMSH` block and ignores the trailer.

## GMSH header

All integers are little-endian. After `GMSH` and the version, 14 `uint32` values follow:

| Index | Meaning |
| ---: | --- |
| 0 | Index-block offset relative to `GMSH`. |
| 1 | Reserved, observed `0`. |
| 2 | Mesh count, observed `1`. |
| 3 | Mesh-header offset; in observed files this is the end of the material section. |
| 4 | Vertex-attribute count. |
| 5 | Vertex-attribute offset. |
| 6 | Submesh count, observed `1`. |
| 7 | Submesh-section offset. |
| 8 | Vertex-stream count. |
| 9 | Vertex-stream offset. |
| 10 | Material-reference count, observed `2`. |
| 11 | Material-reference offset. |
| 12 | Reserved, observed `0`. |
| 13 | Reserved, observed `0`. |

## Index block

At `header[0]`:

| Type | Meaning |
| --- | --- |
| `uint32` | Index count. |
| `uint32[indexCount]` | Triangle-list indices. |

Observed index counts are multiples of three. Indices are 32-bit and reference the vertex positions
that follow the index block.

## Position block

Immediately after the index data:

| Type | Meaning |
| --- | --- |
| `uint32` | Vertex count. |
| `float32[vertexCount][3]` | Position XYZ. |

Coordinates are float32. The observed cube has positions in `[-250, 250]` on X/Z and `[0, 500]` on
Y, consistent with centimetre-like units.

## Vertex attributes

At `header[4]`, `header[5]`, each record is 28 bytes:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Usage: 0 position, 1 normal, 2 texcoord, 3 tangent, 4 color. |
| 4 | `uint32` | Usage index. |
| 8 | `uint32` | Format code (an `eEGfxFormat2` value). |
| 12 | `uint32` | Stream index or related attribute field. |
| 16 | `uint32` | Unknown. |
| 20 | `uint32` | Unknown. |
| 24 | `uint32` | Unknown. |

Observed format codes: `7` (position), `15` (normal/texcoord), `23` (color), `58` (tangent on the
five-attribute layouts).

## Vertex streams

At `header[8]`, `header[9]`, each record is 16 bytes:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Usage, matching the attribute usage. |
| 4 | `uint32` | Observed `1`. |
| 8 | `uint32` | Offset. |
| 12 | `uint32` | Stride or size. |

Example (`Obj_Dmy_MaterialCube_1_L1`):

```text
position  offset=0    stride=8
normal    offset=192  stride=4
texcoord  offset=288  stride=4
color     offset=384  stride=4
```

The descriptors are preserved and exposed by the parser. Their offsets do not yet fully explain the
12-byte position buffer, so the renderer currently uses the position block directly and generates
smooth normals from the triangle list. UV/material stream decoding is open work.

## Material references

At `header[10]`, `header[11]`, each observed record is 40 bytes (ten `uint32` values). Two records
are present in all 5,868 retail meshes. A repeated value `0x81d1b3f7` appears at record field 6.
Its meaning is unresolved; it is not a DJB2 resource hash of any material name in the reference
installation.

## Observed retail set

| Metric | Value |
| --- | ---: |
| `msh` resources | 5,868 |
| Parsed with positions/indices | 5,868 |
| Distinct attribute/stream layouts | 6 |
| Most common layout | 4 attributes, 4 streams, 2 material records (3,487) |
| Maximum vertices | 327,858 |
| Maximum indices | 654,600 |

Layouts observed:

| Attributes | Streams | Materials | Count |
| ---: | ---: | ---: | ---: |
| 4 | 4 | 2 | 3,487 |
| 5 | 5 | 2 | 1,185 |
| 5 | 4 | 2 | 620 |
| 6 | 5 | 2 | 424 |
| 2 | 2 | 2 | 123 |
| 3 | 3 | 2 | 29 |

## Evidence

`Obj_Dmy_MaterialCube_1_L1` starts with `GAR5` at 0, `GMSH` at 44 and version 2. Its header values
include index offset `0x39C`, mesh-header offset `0x2C0`, index count `36`, vertex count `24`, and
positions forming a cube with extents `(-250, 0, -250)` to `(250, 500, 250)`.

## Implementation

| Component | File |
| --- | --- |
| GMSH parser | `OpenElexSharp/Assets/GmshMesh.cs` |
| DigitalRiseModel mesh-part creation and normal generation | `OpenElexSharp/Graphics/MeshLoader.cs` |
| `DrMeshPartTag` bounds metadata | `OpenElexSharp/Graphics/MeshLoader.cs` |
| Low-level renderer and render statistics | `OpenElexSharp/Graphics/Renderer.cs` |
| Test scene | `OpenElexSharp/OpenElexGame.cs` (`--mesh`) |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets meshes`) |

CLI:

```powershell
dotnet run --project OpenElexSharp -- assets meshes Obj_Dmy_MaterialCube_1_L1 --properties
dotnet run --project OpenElexSharp -- assets meshes --all
```

Test scene:

```powershell
dotnet run --project OpenElexSharp -- --mesh Obj_Dmy_MaterialCube_1_L1
dotnet run --project OpenElexSharp -- --mesh Obj_Bld_Cle_Mixer_2_Apps_L1 --smoke-test
```

The scene shows a Myra HUD with FPS, draw calls, triangles and mesh count; `Escape` exits.

Meshes are uploaded as `DrMeshPart` from DigitalRiseModel. The bounding sphere is stored in a
per-part `DrMeshPartTag`; the part itself carries the vertex/index buffers, primitive type and
bounding box. A dedicated `Renderer` owns the stock `BasicEffect`, device state and draw-call /
triangle statistics. It draws positions and indices with generated smooth normals and a fallback
checker texture. It does not yet bind decoded UVs, real normals or material textures.

## Open questions

- Mapping of `eEGfxFormat2` format codes to component types and byte sizes.
- Exact meaning of the vertex-stream offset/stride fields; they do not yet explain the 12-byte
  position data.
- How per-vertex normals, UVs and colors are laid out and whether they are interleaved or streamed.
- Meaning of the repeated `0x81d1b3f7` material field and how mesh material records resolve to
  `eCMaterialResource2` resources.
- LOD/submesh data after the position block: observed counts and bounding boxes are not decoded.
- Coordinate handedness, unit scale and whether meshes require a coordinate shift.
- Skinned meshes and bone/weight streams (T009).

## Related tasks

- T008 - this parser and static-mesh viewer.
- T009 - skeleton, skinning and animation.
- T102 - full mesh renderer and material binding.
