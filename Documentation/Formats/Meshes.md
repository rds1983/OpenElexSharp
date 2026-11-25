# Static meshes (GMSH v2)

ELEX static mesh resources use the `msh` type (`.elexmsh`) and resolve through `w_msh_*.db`
databases to hashed `.rom` files. A resource contains a `GMSH` version 2 block with a GPU index
buffer, tightly packed vertex streams, two buffer descriptors and declaration tables. Submesh
ranges, material names and LOD distances are **not** in the GMSH payload; they live in the
`eCMeshResource2` class block of the owning RT02 database record (see
[Mesh metadata](#mesh-metadata-materials-and-lods)).

## Purpose and scope

This specification covers the mesh container, the GPU index buffer, vertex streams, attribute
formats, buffer descriptors and the tooling copies stored after the declarations. It also documents
the database-side submesh/material/LOD metadata needed to bind meshes. Material payloads are covered
in [Materials.md](Materials.md).

## Container layout

All integers are little-endian. A `.rom` mesh file is a 44-byte GAR5 image-style wrapper followed by
the GMSH payload:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `GAR5`. |
| 4 | `uint32` | Header value `0x20`. |
| 8 | `byte[36]` | Zero/metadata bytes. |
| 44 | `char[4]` | Mesh magic `GMSH`. |
| 48 | `uint32` | Version, `2`. |

Relative to the `GMSH` magic:

| Offset | Contents |
| ---: | --- |
| `0x00` | `GMSH`, version, 14 `uint32` header values (64 bytes total). |
| `0x40` | GPU index buffer: `indexCount` × 2 or 4 bytes. |
| align16 | Vertex streams, contiguous: `sum(stride × vertexCount)` bytes. |
| ... | Two 40-byte buffer descriptors. |
| ... | 28-byte mesh header. |
| ... | Vertex attribute records (`attributeCount` × 28 bytes). |
| ... | Submesh section (`submeshCount` × 16 bytes; observed zero-filled). |
| ... | Vertex stream records (`streamCount` × 16 bytes). |
| ... | Tooling index block: `uint32 indexCount` + `uint32[indexCount]`. |
| ... | `uint32 vertexCount` + `float32[vertexCount][3]` positions. |
| ... | Per-triangle cluster data, `0xFEEDFACE` sentinel and a 44-byte GAR5 trailer. |

The final per-triangle block and trailer are not decoded; they are not needed to render the mesh.
The vertex data starts at `align16(0x40 + indexCount × indexElementSize)`.

## GMSH header

After `GMSH` and the version, 14 `uint32` values follow:

| Index | Meaning |
| ---: | --- |
| 0 | Offset of the 32-bit **tooling** index block. |
| 1 | Reserved, observed `0`. |
| 2 | Mesh count, observed `1`. |
| 3 | Mesh-header offset; equals the end of the buffer-descriptor block. |
| 4 | Vertex-attribute count. |
| 5 | Vertex-attribute offset. |
| 6 | Submesh-record count. |
| 7 | Submesh-record offset. |
| 8 | Vertex-stream count. |
| 9 | Vertex-stream offset. |
| 10 | Buffer-descriptor count, observed `2`. |
| 11 | Buffer-descriptor offset. |
| 12 | Reserved, observed `0`. |
| 13 | Reserved, observed `0`. |

The `indexCount` used by the GPU index buffer is stored in the tooling index block at `header[0]`.
The vertex count is stored after that block, so the header alone is not sufficient to size the GPU
buffer.

## GPU index buffer

At `GMSH + 0x40`:

| Type | Meaning |
| --- | --- |
| `uint16[indexCount]` or `uint32[indexCount]` | Triangle-list indices. |

The element size is stored in buffer descriptor 0, field 3 (see below). Meshes with up to 65,535
vertices use 16-bit indices; larger meshes use 32-bit indices. The buffer size always equals
`indexCount × elementSize`. Indices reference the vertex streams in the same order as the tooling
position copy.

## Vertex streams

At `header[9]`, each record is 16 bytes:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Usage: 0 position, 1 normal, 2 tangent, 3 color, 4 texcoord. |
| 4 | `uint32` | Observed `1`. |
| 8 | `uint32` | Byte offset from the start of the vertex data. |
| 12 | `uint32` | Record stride in bytes. |

Streams are **contiguous arrays**, not interleaved per vertex: stream *k* occupies
`vertexDataOffset + offset` through `+ offset + stride × vertexCount`. When two attributes share a
stream (for example two texture-coordinate sets), the stream stride is the sum of their element
sizes and each attribute reads at `UsageOffset` within the record.

## Vertex attributes

At `header[5]`, each record is 28 bytes:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Usage (see below). |
| 4 | `uint32` | Byte offset of this attribute inside its stream record. |
| 8 | `uint32` | `eEGfxFormat2` format code. |
| 12 | `uint32` | Observed shader-slot value; **not** a stream index. |
| 16 | `uint32` | Observed `0`. |
| 20 | `uint32` | Observed `0`. |
| 24 | `uint32` | Observed `0`. |

Attributes bind to the stream with the same usage. Two attributes with the same usage share that
stream and differ by `UsageOffset` (observed `0` and `4` for the second UV set).

## Vertex formats

The format codes are indexes into the engine's DXGI-style `eEGfxFormat2` list with typeless formats
removed. The retail set uses only five codes:

| Code | Name | Size | Decoding |
| ---: | --- | ---: | --- |
| 4 | `R32G32B32_Float` | 12 | Three little-endian floats (position). |
| 7 | `R16G16B16A16_Float` | 8 | Four half floats; positions use x, y, z and ignore w. |
| 15 | `R10G10B10A2_UNorm` | 4 | Packed normal/tangent: x bits 0–9, y bits 10–19, z bits 20–29, 2 unused bits. |
| 23 | `R16G16_Float` | 4 | Two half floats (texture coordinates). |
| 58 | `B8G8R8A8_UNorm` | 4 | Four bytes in blue, green, red, alpha order (vertex color). |

Packed components decode as `component = clamp(raw / 511.5 - 1, -1, 1)`; normals and tangents are
normalized after decoding. A zero-length decoded vector falls back to `(0, 1, 0)`.

## Coordinate system

Positions and normals are stored in object space with Y up. The retail cube is 500 units tall
(`-250..250` on X/Z, `0..500` on Y) and the door is roughly 600 units wide, which is consistent with
centimetre-like units. The loader applies **no** axis conversion, scale or pivot shift; the test
scene uses MonoGame's right-handed `CreateLookAt`/`CreatePerspectiveFieldOfView` directly and the
meshes render upright.

### Winding and face culling

`MeshWinding` compares `cross(b - a, c - a)` with the summed decoded vertex normals for every
triangle. Across the 5,868 retail `msh` resources, 14,810,301 triangles disagree with the vertex
normals, 12,346 agree and 6,099 are degenerate: **99.92% of non-degenerate triangles are clockwise
when viewed from the side the normals point to**. The 0.08% exceptions are inconsistent meshes or
vertices without a reliable normal.

After MonoGame's projection and viewport transform those front faces are counter-clockwise on
screen: a closed cube rendered with `CullCounterClockwiseFace` shows its outside correctly. The
renderer still defaults to `CullNone` for static props because several meshes are open or
single-sided (the closed door loses interior panels under culling) and the material two-sided flags
are not decoded yet (T401). The viewer's `C` key cycles none/CCW/CW for inspection; once material
flags are available, closed props should default to counter-clockwise culling.

## Buffer descriptors

At `header[11]`, each record is 40 bytes (ten `uint32` values). The first descriptor describes the
index buffer and the second the vertex buffer:

| Field | Index descriptor | Vertex descriptor |
| ---: | --- | --- |
| 0 | Index buffer size in bytes. | Vertex buffer size in bytes (`sum(stride) × vertexCount`). |
| 1 | `0`. | `0`. |
| 2 | `2`. | `1`. |
| 3 | Index element size, `2` or `4`. | `0`. |
| 4 | `0x28`, or `0x1E` for 32-bit indices. | `0`. |
| 5 | `1`. | `1`. |
| 6 | Constant `0x81D1B3F7`. | Constant `0x81D1B3F7`. |
| 7 | `1`. | `1`. |
| 8 | `0x40`. | Alignment/size hint, varies. |
| 9 | Same as field 0. | Same as field 0. |

The repeated hash is a buffer/class identifier, not a material name. It does not match any material
or image resource-name hash in the retail data.

## Tooling copies

The bytes after the stream records are a second, tooling-oriented copy of the geometry:

| Type | Meaning |
| --- | --- |
| `uint32 indexCount` + `uint32[indexCount]` | 32-bit index list. Triangle order can differ from the GPU buffer. |
| `uint32 vertexCount` + `float32[vertexCount][3]` | Widened position copy. |

Verified against all 5,868 retail meshes: the widened positions exactly match the packed stream
positions (maximum deviation `0`). The GPU buffer is authoritative; the copies are useful for
diagnostics and for formats that are not decoded yet.

## Mesh metadata: materials and LODs

GMSH itself has no material names or submesh ranges. The owning `w_msh_*.db` record contains a
`GEC2` class block of `class eCMeshResource2` version 1 with six properties:

| Property | Type | Meaning |
| --- | --- | --- |
| `SubMeshes` | `class bTObjArray<class eCSubMesh>` | Submesh ranges and material bindings. |
| `IndexCount` | `unsigned int` | Total GPU index count. |
| `VertexCount` | `unsigned int` | Total vertex count. |
| `Boundary` | `class bCBox` | Six floats: min x/y/z, max x/y/z. |
| `BoundingSphere` | `class bCSphere` | Four floats: centre x/y/z, radius. |
| `LoDRangeStart` | `float` | Distance at which this LOD becomes active; `0` for LOD1. |

`SubMeshes` stores a `uint32` count followed by that many consecutive `GEC2` objects of
`class eCSubMesh` version 2. Each submesh has ten properties:

| Property | Type | Meaning |
| --- | --- | --- |
| `MaterialName` | `class bCString` | Material resource name, for example `AS_Alb_Tileset_Wall_3`. |
| `Extends` | `class bCBox` | Submesh bounding box. |
| `FirstIndex` | `unsigned int` | First index in the GPU index buffer. |
| `IndexCount` | `unsigned int` | Number of indices in the submesh. |
| `FirstVertex` | `unsigned int` | First vertex of the skinning/batch range. |
| `VertexCount` | `unsigned int` | Vertices in the skinning/batch range. |
| `FirstBone` | `unsigned int` | First bone (skinned meshes). |
| `BoneCount` | `unsigned int` | Bone count (skinned meshes). |
| `MaterialSwitchIndex` | `int` | Material-switch index for animated materials. |
| `Flags` | `enum eSSubMeshFlags` | Submesh flags. |

Submesh `FirstIndex`/`IndexCount` ranges are contiguous and sum exactly to the mesh index count.
`FirstVertex`/`VertexCount` are not a disjoint vertex partition (ranges may overlap); they describe
skin/batch windows. Render one draw range per submesh and bind `MaterialName` to that range. The
runtime resolves it to an `ElexMaterial` and stores the reference in `DrMeshPartTag`.

LODs are separate resources named `_L1`, `_L2`, `_L3` (with decreasing vertex counts). `LoDRangeStart`
is the activation distance: `0` for `_L1`, a positive distance for later stages. Select `_L1`
explicitly for the default LOD. Verified on `Obj_Bld_Alb_Door_Closed_L1/L2/L3`: 758/430/216
vertices, 2,010/693/300 indices and `LoDRangeStart` 0/5000/10000.

## Skinned meshes (`skn`)

Character, creature and animated-prop resources use the `skn` type and the same GMSH geometry with
three differences:

- the database class is `eCMotionSkinResource2` (same six properties);
- `header[0]` points at an embedded skeleton tail instead of the 32-bit tooling index block, so the
  index and vertex counts are derived from the buffer descriptors;
- usage 5 attributes carry four bone indices (`format 20`, `R8G8B8A8_UInt`, offset 0) and four half
  weights (`format 7`, `R16G16B16A16_Float`, offset 4); usage 7 (`format 30`, `R32_SInt`) is unknown.

The skeleton tail, bone names, parent indices and the retail bone budget are documented in
[Skeletons.md](Skeletons.md).

## Observed retail set

| Metric | Value |
| --- | ---: |
| `msh` resources | 5,868 |
| Parsed with decoded streams | 5,868 |
| Distinct attribute/stream layouts | 6 |
| Submesh records (DB) | 13,406 |
| Maximum submeshes per mesh | 15 |
| Meshes with decoded normals | 5,868 |
| Meshes with texture coordinates | 5,738 |
| Meshes with two UV sets | 1,044 |
| Meshes with tangents | 5,718 |
| Meshes with vertex colors | 1,614 |
| Meshes with 32-bit indices | 28 |
| Triangles clockwise vs. vertex normals | 14,810,301 (99.92% of non-degenerate) |
| Triangles agreeing / degenerate | 12,346 / 6,099 |
| Maximum vertices | 327,858 (`Obj_Bld_Cle_Cathedral_1_L1`) |
| Maximum indices | 654,600 (32-bit) |

Layouts observed:

| Attributes | Streams | Contents | Count |
| ---: | ---: | --- | ---: |
| 4 | 4 | position, normal, tangent, UV | 3,487 |
| 5 | 5 | position, normal, tangent, color, UV | 1,185 |
| 5 | 4 | position, normal, tangent, UV0+UV1 | 620 |
| 6 | 5 | position, normal, tangent, color, UV0+UV1 | 424 |
| 2 | 2 | position, normal | 123 |
| 3 | 3 | position, normal, UV | 29 |

## Runtime rendering and viewer

`MeshLoader` uploads the stock MonoGame `VertexPositionColorNormalTexture` (position, color,
normal and one texture coordinate). Meshes without a color stream get white. `BasicEffect` is used
in two modes:

- **lit** (default): lighting, diffuse texture and vertex color; the stock effect supports vertex
  colors together with lighting (verified on `It_Plasma_Rifle_1_L1`, where the blue/ochre color
  stream tints the metal);
- **vertex colors** (`V`): unlit, texture disabled, to inspect the decoded color stream directly.

Missing materials and textures still bind the checker placeholder. Tangents stay on the CPU: the
viewer builds up to 2,000 magenta tangent segments (3% of the mesh radius) and `T` draws them with
depth testing, which verifies the 5,718 meshes that carry a tangent stream.

`MeshViewer` loads one mesh at a time from the 5,868 `msh` resources in name order. Left/Right
cycles, Home/End jumps to the ends, and every change disposes the previous mesh and its material
library so textures do not accumulate. The overlay shows the current resource, catalog position and
active modes.

## Evidence

`Obj_Dec_Hum_Plane_L1` (4 attributes, half positions):

```text
header[0]=0x1CC header[3]=0xF0 header[4]=4 header[5]=0x10C
header[6]=1    header[7]=0x17C header[8]=4 header[9]=0x18C
header[10]=2   header[11]=0xA0
indexCount=6, vertexCount=4
GPU index buffer at 0x40: 00 00 01 00 02 00 02 00 03 00 00 00
vertex data at 0x50: a7 59 12 3a a7 59 00 00  ->  half3 (180.875, 0.758789, 180.875)
```

`Obj_Bld_Alb_Door_Closed_L1` (6 attributes, 5 streams, 4 submeshes):

```text
vertex data base = 0x1000, stream sizes 6064/3032/3032/3032/6064 -> end 0x62E8 = descriptor offset
submeshes: AS_Alb_Tileset_Wall_3 / AS_Alb_Tileset_Grilles_1 /
           AS_Alb_Tileset_Wall_6 / AS_Alb_Tileset_Wall_10
ranges: 0+180, 180+168, 348+1440, 1788+222 = 2,010 indices
```

## Open questions

- Meaning of the per-triangle cluster block before the `0xFEEDFACE` sentinel. T011 found no reference
  from collision (`col`) or occlusion (`ocm`) resources, so it is render-side metadata rather than
  physics data.
- Meaning of the 44-byte GAR5 trailer and its offsets.
- Semantics of attribute field 12 (observed shader-slot values) and the unused 2 bits of packed normals.
- Whether more than two UV sets can share a stream and how `UsageOffset` scales for them.
- Two-sided material flags: static props render without culling until the material parameter blocks
  are decoded (T401); closed meshes can already use `CullCounterClockwiseFace`.
- Whether any mesh uses a position format other than codes 4 and 7.

## Implementation

| Component | File |
| --- | --- |
| GMSH parser and vertex decoding | `OpenElexSharp/Assets/GmshMesh.cs` |
| Format/usage definitions | `OpenElexSharp/Assets/GmshMesh.cs` |
| DB-side mesh metadata | `OpenElexSharp/Assets/MeshMetadata.cs` |
| RT02 class-block reader | `OpenElexSharp/Assets/ResourceClassBlockReader.cs` |
| DigitalRiseModel mesh-part creation and material binding | `OpenElexSharp/Graphics/MeshLoader.cs` |
| Winding analyzer | `OpenElexSharp/Graphics/MeshWinding.cs` |
| Catalog mesh viewer | `OpenElexSharp/Graphics/MeshViewer.cs` |
| Runtime material cache and texture upload | `OpenElexSharp/Graphics/MaterialLibrary.cs` |
| Low-level renderer, shading and culling | `OpenElexSharp/Graphics/Renderer.cs` |
| Test scene | `OpenElexSharp/OpenElexGame.cs` (`--mesh`, `--viewer`, `--capture`) |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets meshes`) |

CLI and viewer:

```powershell
dotnet run --project OpenElexSharp -- assets meshes Obj_Bld_Alb_Door_Closed_L1 --properties
dotnet run --project OpenElexSharp -- assets meshes --all
dotnet run --project OpenElexSharp -- --mesh Obj_Dmy_MaterialCube_1_L1
dotnet run --project OpenElexSharp -- --viewer
```

Viewer keys: Left/Right cycles the catalog, Home/End jumps to the first/last mesh, `V` toggles
unlit vertex colors, `T` toggles tangent lines and `C` cycles culling. The debug overlay shows the
current resource, position and modes; `--smoke-test --capture` saves a frame for automated checks.

Test scene:

```powershell
dotnet run --project OpenElexSharp -- --mesh Obj_Bld_Alb_Door_Closed_L1
dotnet run --project OpenElexSharp -- --mesh Obj_Bld_Cle_Cathedral_1_L1 --smoke-test --capture shot.png
```

## Related tasks

- T008 - static-mesh container and preview.
- T008b - vertex streams, materials and LODs.
- T009a - skeleton and skinned-mesh parsing.
- T009b - animation clips and blend shapes.
- T102 - full mesh renderer and material binding.
