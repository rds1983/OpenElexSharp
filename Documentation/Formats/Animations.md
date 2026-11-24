# Animation networks and blend shapes (`net`, `bln`)

## Purpose

ELEX animation data is not stored as standalone clip files. The two resource types relevant to
animation are:

| Type | Class | Count | Contents |
| --- | --- | ---: | --- |
| `net` | `class eCMotionNetworkDefResource2` | 34 | Morpheme motion-network definitions, including bones, tracks, states and animation nodes. |
| `bln` | `class eCBlendShapeResource2` | 270 | Blend-shape target sets (`blendShape1.*`), used for facial and body morphs. |

The `skn` resources supply the skinned mesh and skeleton ([Skeletons.md](Skeletons.md)); `net`
supplies the animation graph and clips, and `bln` supplies morph targets. This specification covers
the outer container and the readable/structured parts decoded so far. The motion-network graph and
both payload bodies remain open work.

## Container and endianness

Both types are stored in a GAR5 wrapper described in [Gar5.md](Gar5.md). The wrapper is 44 bytes and
the payload begins at `0x2C`. All integer fields documented here are little-endian `uint32`.

The payloads are not compressed at the container level. The `net` payload is an opaque Morpheme
serialization; the `bln` name table is plain Latin-1/ASCII text and the delta data is not decoded.

## Motion networks (`net`)

A `net` payload is a **Morpheme** network definition (NaturalMotion middleware). The readable part is
a large collection of NUL-terminated names:

- bone and transform names such as `BONE_Root`, `BONE_Lid`, `Male_SpineLimit`;
- tracks such as `TRAJECTORY`, `CharacterWorldSpaceTM`, `DefaultDiscreteEventTrack`;
- state/condition names such as `Closed`, `Opening`, `Closed_Opened|ConditionGroup|MessageCondition`,
  `chst_bsk1_open_Opened|ConditionGroup|FractionThroughSource`.

| Resource | Size | Readable names (capped) | `BONE_` names |
| --- | ---: | ---: | ---: |
| `Male` | 59.6 MB | 100,000 | 0 (`Male_*` naming) |
| `Creature` | — | 100,000 | 896 |
| `Troll-Rig` | — | 84,049 | 439 |
| `Crawler-Rig` | 2.6 MB | 34,342 | 563 |
| `Chest` | 23 KB | 41 | 4 |

`Male` appears to be the human network and holds the bulk of the body animation data. The network
graph, clip boundaries, keyframe tracks and compression are **not decoded yet**; the current parser
extracts names only.

## Blend shapes (`bln`)

A `bln` payload starts with a compact blend-shape header. Field offsets are relative to the payload
start (`0x2C` in the file):

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Target count (83 for the sampled head). |
| 4 | `uint32` | Base-mesh vertex count (9,975 for the sampled head). |
| 8 | `uint32` | Unresolved (7,707 for the head). |
| 12 | `uint32` | Unresolved (135,564). |
| 16 | `uint32` | Unresolved (2,925). |
| 20 | `uint32` | Repeated target count. |
| 24 | `uint32` | Reserved, observed `0`. |
| 28 | `uint32[targetCount - 1]` | End offsets of targets `0..targetCount-2`, relative to the first name. |
| … | bytes | A further block before the names (layout undecoded; 332 bytes for the sampled head). |
| … | names | NUL-terminated `blendShape1.*` target names. |
| … | bytes | Per-target delta data (not decoded). |

Target 0 starts at relative offset 0. Target `i` in `1..targetCount-1` starts at entry `i-1` of the
offset array; there is no trailing length entry. Because the name table is not at a fixed offset
(a block of undecoded bytes precedes it), the parser scans for the first position where every name
derived from the offsets is printable and NUL-terminated, which rejects printable metadata bytes
immediately before the table. For the sampled head the first targets are
`blendShape1.mouth_left_up_lip_up`, `..._lip_down`, `..._right_up_lip_up`, `..._jaw_open`, and so on
(mouth, jaw, tongue, lids, cheeks, nose, brows); the name table itself is 2,257 bytes.

The delta data after the name table is the actual morph geometry. Its layout, compression and
quantization are **not decoded yet**; the current parser exposes the header and target names.

## Evidence

- Retail resource databases list 34 `net` and 270 `bln` resources; every one parses with the current
  header/name readers.
- `bln` sample `CH_Head_F_Ambient_01_L1`: 83 targets, 9,975 base vertices, 83 `blendShape1.*` names,
  82 offsets and a 2,257-byte name table. Header field 2 is 2,925 but does not equal the name-table
  length.
- `net` sample `Chest`: header value `0x20`, first readable names `CharacterWorldSpaceTM`,
  `TRAJECTORY`, `BONE_Root`, `BONE_Lid`; `Male` reaches the 100,000-name scan cap.
- Small hex excerpt at the start of a GAR5 payload: `47 41 52 35` (`GAR5`), followed by the
  little-endian header fields above.

## CLI

```powershell
dotnet run --project OpenElexSharp -- assets animations Chest --properties
dotnet run --project OpenElexSharp -- assets animations --all
dotnet run --project OpenElexSharp -- assets blendshapes CH_Head_F_Ambient_01_L1 --properties
dotnet run --project OpenElexSharp -- assets blendshapes --all
```

The `animations` command prints the readable-name count and `BONE_`-prefixed names. The
`blendshapes` command prints the target count, base vertex count, unresolved header fields and the
first target names.

## Retail validation

| Metric | Value |
| --- | ---: |
| `net` resources parsed | 34 / 34 |
| `bln` resources parsed | 270 / 270 |
| Largest network | `Male`, 59.6 MB |
| Maximum readable names (cap) | 100,000 |
| Sampled head blend targets | 83 (9,975 vertices) |

## Open questions

- Morpheme network graph: node types, connections, clip boundaries and state machines.
- Keyframe tracks, time bases and compression.
- Mapping `net` nodes to `skn` skeletons and bone indices.
- `bln` delta layout, quantization, per-target vertex ranges and the meaning of header fields
  8/12/16.
- Whether facial animation is driven by `bln` weights, Morpheme nodes or both.
- Clip sampling and a pose/character viewer.

## Implementation

| Component | File |
| --- | --- |
| Motion-network name extractor | `OpenElexSharp/Assets/MotionNetwork.cs` |
| Blend-shape header/name parser | `OpenElexSharp/Assets/ElexBlendShapeSet.cs` |
| CLI inspectors | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets animations`, `assets blendshapes`) |
| Tests | `OpenElexSharp.Tests/MotionNetworkTests.cs`, `OpenElexSharp.Tests/BlendShapeTests.cs` |

## Related tasks

- T009a - skeletons and skinned meshes.
- T009b - animation data discovery (this specification, partial).
- T009c - Morpheme clip decoding and pose sampling.
- T009d - blend-shape deltas and morph playback.
- T106/T107 - skinned character rendering and animation playback.
- T405/T407 - animation and audio parity.
