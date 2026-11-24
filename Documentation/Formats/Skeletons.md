# Skeletons and skinned meshes (`skn`)

ELEX character, creature and animated-prop resources use the `skn` type (`.elexskn`) and resolve
through `w_skn_*.db` databases. A resource is a GMSH version 2 **skinned mesh** followed by an
embedded skeleton. No separate animation-clip resource type has been found; animation data is still
open work.

## Purpose and scope

This specification covers the skinned GMSH variant, the skinned vertex attributes (bone indices and
weights), the embedded skeleton tail (bind poses, parent indices and bone names) and the retail bone
budget. Static meshes are documented in [Meshes.md](Meshes.md). Blend shapes (`bln`) and animation
clips are not decoded yet.

## Container and GMSH differences

`skn` resources use the same 44-byte GAR5 wrapper and GMSH v2 header as static meshes, with two
important differences:

1. The database class block is `class eCMotionSkinResource2` (not `eCMeshResource2`); its properties
   are the same (`SubMeshes`, `IndexCount`, `VertexCount`, `Boundary`, `BoundingSphere`,
   `LoDRangeStart`).
2. `header[0]` points at the **skeleton tail**, not at a 32-bit tooling index block. The index and
   vertex counts come from the two buffer descriptors instead:
   - `indexCount = descriptor[0].Size / descriptor[0].ElementSize`,
   - `vertexCount = descriptor[1].Size / sum(stream.Stride)`.

The GPU index buffer, vertex data base (`align16(0x40 + indexCount × elementSize)`) and stream
layout are unchanged. The tail after the skeleton contains the usual tooling index block
(`uint32 indexCount` + 32-bit indices), widened `float3` positions, per-triangle cluster data, the
`0xFEEDFACE` sentinel and the GAR5 trailer.

## Skinned vertex attributes

Skinned meshes add bone data on top of the static layouts. The observed attribute records are:

| Usage | Usage offset | Format | Size | Meaning |
| ---: | ---: | ---: | ---: | --- |
| 5 | 0 | `20` (`R8G8B8A8_UInt`) | 4 | Four byte-sized bone indices. |
| 5 | 4 | `7` (`R16G16B16A16_Float`) | 8 | Four half-float bone weights. |
| 7 | 0 | `30` (`R32_SInt`) | 4 | Unknown per-vertex skin value; not decoded. |

Usage 5 streams have stride 12 (4 index bytes + 8 weight bytes). Four influences per vertex are
stored. Weights do not necessarily sum to exactly one after half-float quantization.

## Skeleton tail

The tail starts at `GMSH + header[0]`:

| Offset | Contents |
| ---: | --- |
| `0x00` | Eight `float32` header values (observed bounding-box-like data). |
| `0x20` | Transform block (variant-dependent; see below). |
| … | `0xFFFFFFFF` separator. |
| … | Parent indices: `boneCount - 1` × `uint32` for body skeletons (root excluded). |
| … | Name offsets: `nameCount - 1` × `uint32` (first name is implicit offset `0`). |
| … | Name hashes: `nameCount` × `uint32`, case-sensitive DJB2 of the names. |
| … | Names: NUL-terminated Latin-1 strings. |
| … | Tooling index block and the rest of the static-mesh tail. |

### Transform block variants

Two layouts are observed:

- **Head/attachment skeletons** (for example `CH_Head_F_Ambient_01_L1`) use 28-byte records from
  `0x20` to the separator: three `float32` position followed by a four-`float32` rotation
  quaternion. The record count is `(separator - 0x20) / 28` (5–6 bones in the sampled heads).
- **Body/creature skeletons** (for example `Mesh_Male_FullBody_Alb_High_01_L1`) use a separate
  transform block before the separator that is **not** a sequence of 28-byte records. Names and
  parent indices parse for these resources, but bind-pose transforms remain undecoded.

### Bone names and hierarchy

Bone names are Latin-1 with a trailing NUL. Each name is preceded by its case-sensitive DJB2 hash,
so names can be resolved even without the table:

```text
Male_Hips      -> 0xEC873AD7
Male_Spine     -> 0x7E395BE2
Male_Spine1    -> 0x4564D853
Male_Spine2    -> 0x4564D854
Male_Neck      -> 0xEC8A7264
Male_Head      -> 0xEC8727D5
BONE_Root      -> 0xFE9ACA6C
```

For body skeletons the parent array is one `uint32` per bone except the root; values index the same
bone order as the names. Example (`Mesh_Male_FullBody_Alb_High_01_L1`, 61 bones):

```text
names:   Male_Hips, Male_Spine, Male_Spine1, Male_Spine2, Male_LeftShoulder, ...
parents: 0, 1, 2, 3, 20, 18, 19, ...
```

The head/attachment variants carry extra or packed words before the name offsets; their parent
encoding is unresolved.

## Bone budget

| Metric | Value |
| --- | ---: |
| `skn` resources | 1,320 |
| Parsed tails | 1,270 |
| Unrecognized tails | 50 (`Mesh_Cre_*`, some `Mesh_Female_*`, hologram variants) |
| Maximum bones | 65 |
| Typical bone count | 51–65 |

`SkinnedEffect.MaxBones` is 72 in MonoGame and the vertex format stores four influences per vertex,
so the **stock `SkinnedEffect` is sufficient for the decoded retail skeletons**. The 50
unrecognized tails must be decoded before that decision is final for every creature.

## Evidence

`CH_Head_F_Ambient_01_L1` (6 bones, 9,975 vertices, 55,116 indices):

```text
tail: 8 header floats, 6 × 28-byte (position, quaternion) records
separator at 0xC8
names at 0x100: Male_Spine2, Male_Neck, Male_Head
tooling index count 55,116 follows the name tables
```

`Mesh_Male_FullBody_Alb_High_01_L1` (61 bones, 11,165 vertices, 60,342 indices):

```text
separator at 0xEA0
60 parent indices, 60 name offsets, 61 DJB2 hashes
names at 0x1200: Male_Hips, Male_Spine, Male_Spine1, ...
```

## Open questions

- Body/creature bind-pose transform layout (the block before the separator).
- Head/attachment parent encoding (packed words before the name offsets).
- The 50 resources whose tails do not match either observed layout.
- Animation clips: no separate `ani`/`mot` resource type exists; clips may be embedded in `skn`,
  `bln` or runtime Morpheme data.
- `bln` blend-shape payloads (`class eCBlendShapeResource2`), 270 resources, named
  `blendShape1.*`.
- Usage 7 (`R32_SInt`) semantics.
- Whether bone weights need renormalization after half-float quantization.

## Implementation

| Component | File |
| --- | --- |
| Skeleton tail parser | `OpenElexSharp/Assets/Skeleton.cs` |
| Mesh metadata (`eCMotionSkinResource2`) | `OpenElexSharp/Assets/MeshMetadata.cs` |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets skeletons`) |
| Tests | `OpenElexSharp.Tests/SkeletonTests.cs` |

CLI:

```powershell
dotnet run --project OpenElexSharp -- assets skeletons CH_Head_F_Ambient_01_L1 --properties
dotnet run --project OpenElexSharp -- assets skeletons --all
```

## Related tasks

- T008b - mesh vertex streams, materials and submeshes.
- T009a - skeletons and skinned meshes (this specification).
- T009b - animation clips, blend shapes and playback.
- T102/T106 - skinned rendering with `SkinnedEffect`.
- T107 - animation playback.
