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
the outer container, readable names, the static clip poses and the blend-shape header/names. The
state graph, keyframe curves and payload bodies remain open work.

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
extracts names and the static clip poses described below.

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
`Closed_Opened` or `Rest_Flight`) are separate graph records that are not decoded yet. The
`field0`/`field1` values are therefore exposed unresolved; they are likely timing or playback
attributes.

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

`MotionPose.Blend(from, to, t, boneNames)` slerps two clips. `SkeletonPose.FromMotionPose` combines
the rotations with the skeleton's bind-pose translations and parent indices, and
`ComputeWorldMatrices` composes the hierarchy. Bind-pose translations are used when the `skn`
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

| Resource | Simple transitions | State names |
| --- | ---: | ---: |
| `Crawler-Rig` | 176 | 3 |
| `Troll-Rig` | 227 | 3 |
| `Weapons` | 20 | 5 |
| `Chest` | 4 | 2 |
| `Items` | 2 | 2 |

`Male` uses long hierarchical path names such as
`Main|Animation|Animation|Locomotion|Locomotion|Walk|...|ConditionGroup|MessageCondition` rather than
simple three-segment transitions, so it currently exposes no simple transitions. Transition
durations and binary graph edges are not decoded yet.

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
- The 34 networks contain 61 clips and all 61 expose a pose array.
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
posed-clip count, simple-transition count and state-name count. `--properties` also lists each
clip's track table and first pose quaternions, followed by every transition, state and graph node.
The `blendshapes` command prints the target count, base vertex count, unresolved header fields and
the first target names.

## Retail validation

| Metric | Value |
| --- | ---: |
| `net` resources parsed | 34 / 34 |
| `net` clips decoded | 61 |
| Clips with a pose | 61 / 61 |
| `Male` full-body track mapping | 61 / 61 skeleton bones |
| Simple transitions (all networks) | 1,086 |
| Simple transitions (`Crawler-Rig` / `Weapons` / `Chest`) | 176 / 20 / 4 |
| `Male` simple transitions | 0 (hierarchical paths, not decoded) |
| `bln` resources parsed | 270 / 270 |
| Largest network | `Male`, 59.6 MB |
| Maximum readable names (cap) | 100,000 |
| Sampled head blend targets | 83 (9,975 vertices) |

## Open questions

- Morpheme network graph: binary node records, GUIDs, edges and transition durations.
- Nested hierarchical state paths (used by `Male`) and their condition paths.
- Keyframe curves, time bases and compression; clips currently expose one pose each.
- The large `Male` tail: about 54 MB of mixed float, index and high-entropy blocks is not decoded.
- Mapping `net` nodes to `skn` skeletons beyond the bone-name convention.
- `bln` delta layout, quantization, per-target vertex ranges and the meaning of header fields
  8/12/16.
- Whether facial animation is driven by `bln` weights, Morpheme nodes or both.
- Body bind-pose transforms, needed before a full skinned pose viewer can be rendered.
- A playable pose/character viewer.

## Implementation

| Component | File |
| --- | --- |
| Motion-network, clip and pose parser | `OpenElexSharp/Assets/MotionNetwork.cs`, `MotionClip.cs` |
| Clip retargeting and blending | `OpenElexSharp/Assets/MotionPose.cs` |
| State-transition names | `OpenElexSharp/Assets/MotionTransition.cs` |
| Skeleton transform composition | `OpenElexSharp/Assets/SkeletonPose.cs` |
| Blend-shape header/name parser | `OpenElexSharp/Assets/ElexBlendShapeSet.cs` |
| CLI inspectors | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets animations`, `assets blendshapes`) |
| Tests | `OpenElexSharp.Tests/MotionNetworkTests.cs`, `MotionPoseTests.cs`, `MotionTransitionTests.cs`, `BlendShapeTests.cs` |

## Related tasks

- T009a - skeletons and skinned meshes.
- T009b - animation data discovery.
- T009c - motion clips and pose sampling.
- T009d - blend-shape deltas and morph playback.
- T009e - Morpheme state graph names and transitions.
- T009f - body bind poses and the pose viewer.
- T009g - binary graph edges and keyframe curves.
- T106/T107 - skinned character rendering and animation playback.
- T405/T407 - animation and audio parity.
