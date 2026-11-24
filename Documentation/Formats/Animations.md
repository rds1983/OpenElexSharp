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
the outer container, readable names, the static clip poses, sampled tracks, binary state blocks,
timed transitions and the blend-shape header/names. State-record semantics, name tables with inline
GUIDs, keyframe curves and payload bodies remain open work.

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

`Male` appears to be the human network and holds the bulk of the body animation data. Clip
boundaries, keyframe curves and compression are **not decoded yet**; the parser extracts names, the
static clip poses, sampled tracks, binary state blocks and timed transitions described below.

## Motion clips

Each clip block is preceded by a 32-byte header and followed by a track-name table and a pose array.
The block header is recognized by the fixed bytes `0A D7 23 3C 00 00 80 3E` at offset `0x14`:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Version, observed `1`. |
| 4 | `float` | Unresolved timing/playback value (field 0). |
| 8 | `uint32[2]` | Zero. |
| 16 | `float` | Unresolved timing/playback value (field 1). |
| 20 | `byte[8]` | Marker `0A D7 23 3C 00 00 80 3E` (floats `0.01`, `0.25`). |
| 28 | `uint32` | Zero. |

A few networks (for example `BigBang-Rig`) use a header variant without the marker; their clip table
and pose still decode, but `field0`/`field1` remain `0` because no header is recognized.

The track-name table uses the same cumulative layout as the skeleton name tables:
| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32[trackCount]` | Name offsets, relative to the first name: `0`, then each name's cumulative length. |
| `4 * trackCount` | `char[]` | NUL-terminated track names. |

The pose array follows the table: `trackCount` little-endian `float32` quaternions in `(x, y, z, w)`
order, all unit length. The first two tracks are normally `CharacterWorldSpaceTM` and `TRAJECTORY`;
the remaining tracks are bone names such as `Male_Hips`, `BONE_Root` or `BONE_Lid`. The names match
the `skn` skeleton bones exactly, or after removing a `BONE_`/`bone_` prefix on the track.

A clip stores a **single pose**, not a keyframe sequence. State transitions (for example
`Closed_Opened` or `Rest_Flight`) are separate graph records; their names, binary state blocks and
timed records are described below. The `field0`/`field1` values are therefore exposed unresolved;
they are likely timing or playback attributes.

| Resource | Clips | Track counts | Notes |
| --- | ---: | --- | --- |
| `Male` | 4 | 98, 88, 40, 33 | 61 skeleton bones. The 98/88-track clips map all 61; the 40/33-track clips map 25 and look like an upper-body layer. |
| `Crawler-Rig` | 4 | 111, 111, 50, 49 | Full-body and partial layers. |
| `Gull-Rig` | 2 | 31, 31 | Two full-body poses. |
| `Weapons` | 8 | 12, 9, 6, 7, 6, 5, 7, 5 | Per-weapon poses. |
| `Chest` | 2 | 4, 4 | Two container poses. |
| All resources | 61 | — | Every decoded clip has a pose array. |

### Retargeting and sampling

`MotionPose` maps a clip to skeleton bone names:

- exact name match wins;
- otherwise `BONE_`/`bone_` is removed from the track name and matched again;
- bones without a track receive the identity rotation.

`MotionPose.Blend(from, to, t, boneNames)` slerps two clips. `MotionTransitionSampler` adds a
duration and one-shot/ping-pong sampling on top. `SkeletonPose.FromMotionPose` combines the rotations
with the skeleton's bind-pose translations and parent indices, and `ComputeWorldMatrices` composes
the hierarchy. Bind-pose translations are used when the `skn`
skeleton exposes them; body skeletons still lack them, so their world positions are zero.

## State graph

The network also stores the state-machine names. Simple transitions use the convention

```text
<Source>_<Target>|ConditionGroup|<Condition>
```

for example `Closed_Opened|ConditionGroup|MessageCondition` or
`Shoot_Enabled_Idle|ConditionGroup|FractionThroughSource`. The parser resolves the longest split of
the first segment whose two sides are known graph names, giving `Source`, `Target` and `Condition`.
Names that only occur inside a transition label (for example `chst_bsk1_open`) stay unresolved and
are kept as the raw source label.

Graph name tables that are not clip, transition, event or blend-shape tables are classified as
follows:

- names that occur as transition endpoints become **state names**;
- the remaining names become **node names** (motion nodes, mesh/bone names and other graph labels).

Names containing `|` are also decoded as **hierarchical state paths**. The segments before
`ConditionGroup` are the state-machine path and the segment(s) after it are the condition, for
example:

```text
Main|Animation|Locomotion|Walk|ConditionGroup|MessageCondition
  segments = Main, Animation, Locomotion, Walk
  condition = MessageCondition
```

Simple transitions such as `Closed_Opened|ConditionGroup|MessageCondition` are paths with a single
segment. Paths are capped at 10,000 per network; `Male` reaches the cap.

### Keyframe data survey

The decoded clip blocks contain one raw unit-quaternion pose array per clip. A scan of the complete
`Male` payload finds **no other uncompressed quaternion runs**: the only runs are the four clip poses
(98, 88, 40 and 33 quaternions). The remaining tail is dominated by:

- 714,477 printable runs (about 4.1 MB of state-path/name text);
- a 2,341-name transition table followed by offset/index tables that point back into the string
  data;
- high-entropy float/index blocks whose layout has not been identified.

No uncompressed per-track keyframe tracks were found in `net`. The clips therefore behave as static
poses, and transitions are sampled as timed pose blends (`MotionTransitionSampler`). The
high-entropy blocks contain **quantized sample tracks** instead:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Sample data byte size (a multiple of 16). |
| 4 | `uint32` | Stride, observed `16`. |
| 8 | `float` | Duration in seconds. |
| 12 | `float` | Sample rate, observed `60`. |
| 16 | `uint32` | Track index (observed 54..82). |
| 20 | `uint32` | Bone/second index (observed 54..82). |
| 64 | `uint32` | Sample offset relative to the header. |
| … | `Sample[]` | 16-byte samples: position `xyz` as `int16` plus a rotation `xyzw` as `int16`. |

`Male` contains 1,006 decoded sample tracks (95–98 samples each). `MotionSampleTrack.SampleAt`
interpolates raw positions and slerps rotations over the duration. Positions use an inferred
`raw / 32767 * 100` model-unit scale; `GetTrackName`/`GetBoneName` resolve the header indices against
a clip's track list. `MotionSampleGroup.GroupByDuration` groups tracks that share a duration; `Male`
yields 253 groups (largest: 0.8 s / 85 tracks). The skeleton viewer uses the largest group to animate
a real sampled pose. Binary state blocks and timed transitions are described below.

### Binary state-machine blocks

Every sampled network contains state-machine blocks with this header:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | State count (also the block kind), observed `1`–`57`. |
| 4 | `uint32` | Marker `0x53`. |
| 8 | `uint64` | Records per state, always `16`. |
| 16 | `uint64[stateCount]` | State offsets relative to the block start; the first equals `16 + stateCount × 8`. |

Each state is 664 bytes: 16 records of 40 bytes followed by a 24-byte trailer. Every record holds
five 32-bit values in 8-byte slots (the upper half of each slot is zero). `0xFFFFFFFF` marks an empty
value. The trailer is three `0xFFFFFFFF` slots.

```text
block + 0x30  state 0 -> 16 records + trailer
block + 0x2C8 state 1 -> 16 records + trailer
...
```

Observed blocks:

| Resource | Blocks (state counts) |
| --- | --- |
| `Weapons` | 4 and 1 |
| `Chest` | 4 and 1 |
| `Items` | 4 and 1 |
| `CleaningBot-Rig` | 6 and 1 |
| `Searchlight` | 6 and 3 |
| `Male` | 57 and 34 |

Block state names come from a preceding string table. `Weapons` maps its four states to
`Active`, `Aim`, `Shoot`, `Reload` in table order. State-name tables for several other resources store
a GUID between the offsets and the strings, so they are not decoded yet.

The 16×5 values of the four-state blocks are **identical across unrelated resources** (for example
state 0 has `119`/`117` in `Weapons`, `Chest`, `Items` and `CleaningBot-Rig`). They therefore reference
a fixed template or global vocabulary rather than per-resource edges or sample-track indices. The
earlier idea of matching them against sample-track indices was not valid. The large `Male` block
shares several of these state patterns but is not identical.

### Timed transition records

Transition timing is stored in runs of 48-byte records:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint16` | Record kind, observed `1` or `2`. |
| 2 | `uint16` | Flags, always `1`. |
| 4 | `uint32` | Zero. |
| 8 | `uint16` | Zero. |
| 10 | `uint16` | Transition index. |
| 12 | `uint16` | Source state slot. |
| 14 | `uint16` | Target state slot. |
| 16 | `uint16` | Duration in milliseconds; `0` means immediate. |
| 18 | 30 bytes | Zero padding. |

Records in a run have consecutive indices starting at 4 or later. Source/target are zero-based state
slots; `0` is an implicit disabled/entry state in networks such as `Weapons`. Examples:

| Resource | Records (index: source → target @ ms) |
| --- | --- |
| `Weapons` | `7: 0→1 @ 852`, `8: 1→0 @ 808`, … `21: 2→0 @ 236`, `22: 4→0 @ 0` |
| `Chest` | `6: 0→2 @ 228`, `7: 1→3 @ 184`, `8: 2→1 @ 140`, `9: 3→0 @ 0` |
| `Items` | `4: 1→0 @ 116`, `5: 0→1 @ 0` |
| `CleaningBot-Rig` | `7: 0→1 @ 116`, `8: 1→0 @ 0` |

Within every observed run the duration decreases by exactly 44 ms, ending at 0. The reason is not
known; the field is still the only candidate for transition timing and matches the recurring 116 ms
blend seen across small networks.

| Resource | Simple transitions | State names |
| --- | ---: | ---: |
| `Crawler-Rig` | 176 | 3 |
| `Troll-Rig` | 227 | 3 |
| `Weapons` | 20 | 5 |
| `Chest` | 4 | 2 |
| `Items` | 2 | 2 |
| `Male` | 0 simple | 10,000 hierarchical paths (cap) |

`Male` uses long hierarchical path names such as
`Main|Animation|Animation|Locomotion|Locomotion|Walk|...|ConditionGroup|MessageCondition` rather than
simple three-segment transitions, so it exposes no simple transitions. Its binary state blocks
(57 and 34 states) and 939 timed records are still exposed.

## Blend shapes (`bln`)

A `bln` payload starts with a compact blend-shape header. Field offsets are relative to the payload
start (`0x2C` in the file):

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Target count (83 for the sampled head). |
| 4 | `uint32` | Base-mesh vertex count (9,975 for the sampled head). |
| 8 | `uint32` | Affected vertex count (7,707; the number of entries in the descriptor table). |
| 12 | `uint32` | Delta record count (135,564). |
| 16 | `uint32` | Position quantization range (2,925; positions scale by this / 1,000). |
| 20 | `uint32` | Repeated target count. |
| 24 | `uint32` | Reserved, observed `0`. |
| 28 | `uint32[targetCount - 1]` | End offsets of targets `0..targetCount-2`, relative to the first name. |
| … | bytes | Optional block before the names (332 bytes for the sampled head). |
| … | names | NUL-terminated `blendShape1.*` target names. |
| … | `uint32[vertexCount]` | Vertex remap: `0` for an unaffected vertex, otherwise compact index `1..affectedCount`. |
| … | `AffectedCount × 16` | Sparse descriptors: `startDelta`, `targetMask[3]`. |
| … | `deltaCount × 12` | Quantized deltas: six `int16` values (position `xyz`, normal `xyz`). |

Target 0 starts at relative offset 0. Target `i` in `1..targetCount-1` starts at entry `i-1` of the
offset array; there is no trailing length entry. Because the name table is not at a fixed offset
(a block of undecoded bytes can precede it), the parser scans for the first position where every
name derived from the offsets is printable and NUL-terminated. For the sampled head the first targets
are `blendShape1.mouth_left_up_lip_up`, `..._lip_down`, `..._right_up_lip_up`, `..._jaw_open`, and so
on (mouth, jaw, tongue, lids, cheeks, nose, brows); the name table itself is 2,257 bytes.

### Delta layout

For each descriptor entry, `startDelta` is the first delta record and `targetMask[0..2]` is a
96-bit mask. Set bits are processed in ascending target order; the delta record for a set bit is at
`startDelta + ordinal`, where `ordinal` counts the set bits before it. This makes the deltas sparse:
a vertex only stores records for the targets that move it.

Each 12-byte delta record holds six little-endian `int16` values:

```text
position.x, position.y, position.z, normal.x, normal.y, normal.z
```

Positions are scaled with `value / 32767 * (field16 / 1000)`. Normals are scaled with
`value / 32767`; they are deltas and are not unit length. `ElexBlendShapeSet.ApplyWeights` multiplies
each target's deltas by its weight and accumulates positions and normals on a base-mesh buffer; the
caller normalizes the resulting normals.

| Resource | Targets | Vertices | Affected | Delta records | Range |
| --- | ---: | ---: | ---: | ---: | ---: |
| `CH_Head_F_Ambient_01_L1` | 83 | 9,975 | 7,707 | 135,564 | 2,925 |
| `Mesh_It_Spellarm_Fire_L1` | 5 | 4,889 | 4,889 | 24,445 | 164 |
| All 270 resources | — | — | — | 63,541,940 | — |

The sampled head's largest position delta is about 4.72 units; the spellarm's is about 0.22 units,
which supports the `field16 / 1000` scale. The matching `skn` head has the same 9,975 vertices and the
same resource hash suffix (`00c90d41`), so the deltas index the skinned mesh's vertex stream directly;
other resource pairs should be checked the same way.

## Evidence

- Retail resource databases list 34 `net` and 270 `bln` resources; every one parses, and all 270
  blend-shape sets decode their vertex maps, descriptors and deltas.
- The 34 networks contain 61 clips and all 61 expose a pose array.
- Blend-shape sets contain 63,541,940 sparse delta records in total. The sampled head's `bln` and
  `skn` both have 9,975 vertices and the same hash suffix, confirming the direct vertex mapping.
- State transitions resolve from names: `Weapons` yields 20 transitions and 5 states, `Crawler-Rig`
  176/3, `Troll-Rig` 227/3, `Chest` 4/2.
- `Male` full-body clips contain 61 tracks that match the 61 `skn` skeleton bones exactly; the
  partial clips map 25. `Chest` clips map `BONE_Root`/`BONE_Lid` to `Root`/`Lid` after prefix
  stripping.
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

The `animations` command prints the readable-name count, `BONE_`-prefixed names, clip count,
posed-clip count, simple-transition count, state-name count, hierarchical-path count and candidate
binary-graph count. `--properties` also lists each clip's track table and first pose quaternions,
followed by every transition, state block entry, timed transition summary, graph node, the first 16
state paths, the first 16 binary graph blocks, the first quantized quaternion runs and the first
sample tracks.
The `blendshapes` command prints the target count, base vertex count, unresolved header fields,
decoded delta count, whether deltas were decoded and the first target names. `--properties` adds
per-target delta counts and the first two position/normal deltas.

## Retail validation

| Metric | Value |
| --- | ---: |
| `net` resources parsed | 34 / 34 |
| `net` clips decoded | 61 |
| Clips with a pose | 61 / 61 |
| `Male` full-body track mapping | 61 / 61 skeleton bones |
| Simple transitions (all networks) | 1,086 |
| Simple transitions (`Crawler-Rig` / `Weapons` / `Chest`) | 176 / 20 / 4 |
| `Male` simple transitions | 0 (hierarchical paths) |
| Hierarchical state paths | 10,000 (`Male`, cap) |
| Binary state blocks | 2 per sampled network (`Male`: 57 + 34 states) |
| Named state blocks (`Weapons`) | 4 (`Active`, `Aim`, `Shoot`, `Reload`) |
| State value counts (`Weapons`) | 11 / 7 / 11 / 20 |
| Timed transition records | 939 (`Male`), 16 (`Weapons`), 4 (`Chest`) |
| Observed transition durations | 0–5,768 ms |
| Decoded sample tracks (`Male`) | 1,006 (95–98 samples each) |
| Sample duration groups (`Male`) | 253 (largest 0.8 s / 85 tracks) |
| Inferred sample position scale | `raw / 32767 × 100` model units |
| `bln` resources parsed | 270 / 270 |
| `bln` delta sets decoded | 270 / 270 |
| Total blend-shape deltas | 63,541,940 |
| Sampled head blend targets | 83 (9,975 vertices, 135,564 deltas) |
| Largest network | `Male`, 59.6 MB |
| Maximum readable names (cap) | 100,000 |

## Open questions

- The meaning of the 16×5 values in each binary state record: they are constant across resources and
  appear to be a fixed template or global vocabulary.
- State-name tables that place a 16-byte GUID between the offset array and the strings are not
  decoded, so most state blocks currently have no names.
- Transition source/target slots are zero-based; whether slot `0` is an implicit disabled state must
  be confirmed per network, and transition runs are not yet associated with a specific state block.
- Hierarchical paths are exposed as segment/condition paths, but their binary node links and timing
  are not decoded.
- No uncompressed keyframe tracks were found; compressed pose/curve blocks remain open (T009l).
- The large `Male` tail: about 54 MB of mixed float, index and high-entropy blocks is not decoded.
- Mapping `net` nodes to `skn` skeletons beyond the bone-name convention.
- Exact blend-shape game unit and whether blended normals should be normalized before rendering.
- Whether every `bln` resource pairs with an `skn` of the same vertex count/order outside the sampled head.
- Whether facial animation is driven by `bln` weights, Morpheme nodes or both.
- A fully skinned character viewer (bind transforms are decoded; mesh skinning is T106/T102).

## Implementation

| Component | File |
| --- | --- |
| Motion-network, clip and pose parser | `OpenElexSharp/Assets/MotionNetwork.cs`, `MotionClip.cs` |
| Clip retargeting and blending | `OpenElexSharp/Assets/MotionPose.cs` |
| Timed pose transitions | `OpenElexSharp/Assets/MotionTransitionSampler.cs` |
| State-transition names | `OpenElexSharp/Assets/MotionTransition.cs` |
| Hierarchical state paths | `OpenElexSharp/Assets/MotionStatePath.cs` |
| Candidate binary graph blocks | `OpenElexSharp/Assets/MotionGraphNode.cs` |
| State-machine blocks | `OpenElexSharp/Assets/MotionGraphState.cs` |
| Timed transition records | `OpenElexSharp/Assets/MotionGraphTransition.cs` |
| Quantized quaternion probe | `OpenElexSharp/Assets/MotionSampleProbe.cs` |
| Sample-track codec | `OpenElexSharp/Assets/MotionSampleTrack.cs` |
| Sample grouping and sampled poses | `OpenElexSharp/Assets/MotionSampleGroup.cs` |
| Skeleton transform composition | `OpenElexSharp/Assets/SkeletonPose.cs` |
| Blend-shape header/name/delta parser | `OpenElexSharp/Assets/ElexBlendShapeSet.cs`, `ElexBlendShapeTarget.cs` |
| CLI inspectors | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets animations`, `assets blendshapes`) |
| Tests | `OpenElexSharp.Tests/MotionNetworkTests.cs`, `MotionPoseTests.cs`, `MotionTransitionTests.cs`, `MotionStatePathTests.cs`, `MotionGraphNodeTests.cs`, `MotionGraphStateTests.cs`, `MotionGraphTransitionTests.cs`, `MotionSampleProbeTests.cs`, `MotionSampleTrackTests.cs`, `MotionSampleGroupTests.cs`, `BlendShapeTests.cs` |

## Related tasks

- T009a - skeletons and skinned meshes.
- T009b - animation data discovery.
- T009c - motion clips and pose sampling.
- T009d - blend-shape deltas and morph weighting.
- T009e - Morpheme state graph names and transitions.
- T009f - body bind poses and the pose viewer.
- T009g - Morpheme hierarchical state paths.
- T009h - Morpheme binary graph blocks.
- T009i - binary state record layout.
- T009l - quantized quaternion sample probe.
- T009m - sample-track codec and interpolation.
- T009n - sample scale and track-name mapping.
- T009o - sample grouping and sampled poses.
- T009p - state record groups.
- T009q - state value aggregation.
- T009r - state-block offsets and timed transition records.
- T106/T107 - skinned character rendering and animation playback.
- T405/T407 - animation and audio parity.
