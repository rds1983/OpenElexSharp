# OpenElexSharp Roadmap

Single source of truth for work. Agents follow the loop in [AGENTS.md](AGENTS.md).

Legend: `[ ]` open · `[~]` in progress (at most one) · `[x]` done · `[!]` blocked (reason noted inline)

## Working order

1. **Foundation** — toolchain, asset mounting, format research and loaders.
2. **Visible world** — render levels, objects, NPCs and creatures.
3. **Playable core** — movement, physics, interaction, combat, HUD.
4. **Game systems** — scripts, quests, dialogue, activities, progression, audio.
5. **Finishable game** — a full main-story playthrough without blockers.
6. **Parity** — custom shaders, post-processing and a UI skin that match the original.

Visual and UI parity are never a reason to delay playability. Stock MonoGame effects and plain Myra widgets are the default; custom shaders are allowed only when functionally required (for example, skinned meshes beyond `SkinnedEffect`'s bone limit) and must be documented as such.

Every format is documented in `Documentation/Formats/` **before** its loader is implemented. Research is based exclusively on game assets, never on game executables or libraries.

## Current focus

Milestone **M0 — Toolchain & Formats**. Next task: **T010**.

---

## M0 — Toolchain & Formats

Exit criteria: the solution builds and tests on a clean machine; the engine can mount an ELEX installation, enumerate and extract any asset, and parse the core object, mesh, texture, world, skeleton and template formats well enough to feed the renderer.

- [x] **T001 · Solution scaffold and CI**
  Create `OpenElexSharp.slnx`, `OpenElexSharp` (net10.0, MonoGame DesktopGL executable) and `OpenElexSharp.Tests` (xUnit, reference the engine). Add `Directory.Build.props` (nullable enabled, warnings as errors, deterministic builds), `.editorconfig`, `.gitignore`, and a GitHub Actions workflow that builds and tests.
  Done when: `dotnet build OpenElexSharp.slnx` and `dotnet test OpenElexSharp.slnx` pass with zero warnings; the app opens a MonoGame window and exits cleanly.
  Completed 2026-11-24 — Debug/Release build warning-free, 9 tests pass, local `--smoke-test` exits 0.

- [x] **T002 · Asset location and mount system**
  Resolve the ELEX installation from `ELEX_INSTALL_PATH` (and an equivalent launch argument), parse `data/ini/mount_packed.xml`, and expose a virtual file system that merges loose folders and packed volumes. Normalize virtual paths (case-insensitive), support enumeration and existence checks, and surface clear errors when the installation is missing.
  Done when: unit tests with synthetic mount files pass, and the engine can list and open loose files from a real installation without embedding absolute paths. See `Documentation/Mounts.md`.
  Completed 2026-11-24 — mount parsing, macro/path resolution and layered loose VFS; 99 tests; verified against the retail installation.

- [x] **T003 · PAK archive reader**
  Implement the G3V0 container format used by `data/packed/*.pak`: header, directory tree, per-entry metadata, compression and streamed reads. The GAR5 resource wrapper used by many compiled assets is asset-level and documented separately. Write `Documentation/Formats/Pak.md` first, covering layout, field semantics and open questions.
  Done when: every file in the shipped archives can be listed, and payloads can be extracted and re-read consistently; synthetic fixtures cover headers, compressed/uncompressed entries and corrupt data; `PackedMount` folders from T002 expose their contents through `AssetFileSystem`.
  Implemented per [Formats/Pak.md](Documentation/Formats/Pak.md) using rmtools/GameExtractor references. Validation on the test installation (including one mod) covers 156,905 packaged files, 111,661 of them compressed.
  Completed 2026-11-24 — G3V0 header/tree/attributes/raw-DEFLATE reader, PAK-backed VFS source, 127 tests and retail validation.

- [x] **T004 · Asset catalog and inspector**
  Build an index of all virtual assets (path, size, mount, extension) with fast lookup, filtering by mount and language, plus a developer tool (CLI or in-engine panel) to search paths, show statistics by extension, and export a single file for inspection. Cache the archive index if startup parsing is noticeable. This is the main research instrument for the tasks below. Note that some mounts expose the application folder, so binaries may appear in the index; report them separately and never open executable formats.
  Done when: listing the full archive index takes only a few seconds and the developer can inspect any asset without writing throwaway code.
  Implementation complete: `AssetCatalog` plus `assets list|find|stats|export`, `Documentation/Assets.md`, 167 tests; 156,954 entries indexed in ~2.4 s including mounting.
  Completed 2026-11-24 — catalog, headless inspector, binary safeguards, retail validation.

- [x] **T004b · GAR5 resource wrappers and resource databases**
  Parse the `GAR5` wrapper used by compiled assets (tag/version, `RT02`/`SNxx` sections), `E1RF` resource reference files, and the `w_*_*.db` hash-to-resource tables. The rmtools `ResolveRisen3Entries` implementation is the reference. Expose resolved resource names and sizes through the asset layer so hashed `.rom` payloads can be identified by logical name. Document in `Documentation/Formats/Gar5.md`.
  Done when: representative resource databases resolve names, a hashed dialogue/sound ROM can be addressed by its logical resource name, and synthetic fixtures cover wrappers, references and malformed indexes.
  Implementation complete: `Gar5Header`, `ResourceDatabase`, `Djb2Hash`, `E1RfFile`, `ElexResourceIndex`, `assets resources`; 203 tests; 65,965 resources resolved from retail data.
  Completed 2026-11-24 — GAR5/RT02/E1RF parsing, logical resource resolution and CLI inspection.

- [x] **T005 · Genome object serialization**
  Implement the GEL/Genome object model used by the `.r00`-family files: object headers, class identifiers, versioned property streams, references, arrays and unknown-property preservation. Use `ElexResourceIndex` when a logical resource must be located through an `RT02` database. Generate `Documentation/Formats/Objects.md`.
  Done when: representative objects of every serialized class in the game data load without unhandled errors; unknown classes/properties are skipped safely and reported.
  Implementation complete: `GenomeObject`, `GenomeHeaderDocument`, `GenomeStringTable`, `assets objects`; 228 tests; all 25 retail `.hdr` documents parse (749 `gCQuest`, 7,679 top-level `gCInfo` objects). Nested class-data objects and property type decoding remain raw.
  Completed 2026-11-24 — GEC2/DT01/GENOMFLE parsing and retail `.hdr` validation.

- [x] **T005b · Genome property decoding and nested objects**
  Decode common Genome property types (primitives, strings, GUIDs, vectors/matrices, arrays and proxies) and recursively enumerate nested `GEC2` objects inside class data. Build a practical class/property name table from hashes so object dumps are readable.
  Done when: representative `gCQuest`/`gCInfo` objects expose typed values, `gCInfo` commands/conditions are enumerable, and malformed values are rejected with clear diagnostics.
  Implementation complete: typed decoder for primitives/strings/GUIDs/vectors/enums/arrays/proxies, 18,855-entry name registry, gCInfo nested objects and `--properties`; 247 tests; all 25 retail `.hdr` documents parse and decode every property (48,493 nested gCInfo objects).
  Completed 2026-11-24 — property decoding, name registry and nested gCInfo objects.

- [x] **T006 · Templates (`tple`)**
  Parse entity/item templates, their inheritance chains, default property values and cross-references into a queryable template database. Reuse `GenomeObject`, `GenomeHeaderDocument`, `GenomePropertyDecoder` and `GenomeNameTable` from T005/T005b; `GTP0` template documents are expected to use the same class model.
  Done when: any entity's full effective property set can be resolved by template name, and inheritance/override edge cases are covered by tests.
  Implementation complete: GTP0/eCTemplateEntity parser, child composition and GUID inheritance, typed full property sets, `assets templates`; 261 tests; 8,678 top-level and 17,356 total retail templates parsed (max 15 property sets per composition).
  Completed 2026-11-24 — template database, composition/inheritance resolution and typed property sets.

- [x] **T007 · Textures**
  Parse texture assets (DDS with BC1–BC7 and mipmaps, plus any engine container) into `Texture2D`, with sRGB handling, fallback textures and a documented on-disk spec.
  Done when: diffuse/normal maps from the game display with correct orientation and alpha in a test scene.
  Implementation complete: DDS parser, format mapping, BC4/BC5/BC6H/BC7 CPU decoding, compressed/uncompressed upload, `--texture` test scene, fallback texture and `assets textures`; 325 tests; all 9,631 retail `img` resources parse, all 9,625 2D textures decode, 6 volume textures parsed but not uploaded.
  Completed 2026-11-24 — DDS image resources, BC1–BC7 upload/decoding, normal maps, fallback and texture inspector.

- [x] **T008 · Static meshes (stage A: container and geometry preview)**
  Identify and parse static mesh assets: vertex layouts, index buffers, submeshes, materials, texture bindings and LODs. Document the vertex-stream combinations actually present in the data.
  Done when: representative meshes of every vertex layout load and render with stock effects; malformed data fails with actionable diagnostics.
  **Done in this run:**
  - GMSH v2 container/header, index block and float3 position block parsing.
  - Vertex-attribute, vertex-stream and material-reference declarations exposed.
  - Validation for malformed headers, truncated data, out-of-range indices and bad positions.
  - `DrMeshPart`/`DrMeshPartTag` mesh storage, dedicated `Renderer` with draw-call/triangle statistics, generated smooth normals and fallback texture.
  - Myra HUD with FPS/draw-call/triangle/mesh statistics and an `Escape - Exit` control hint; layouts authored in `.xmmp` and exported with `myra-export`.
  - `assets meshes` inspector and `Documentation/Formats/Meshes.md`.
  - All 5,868 retail meshes parse and the six observed attribute/stream layouts are catalogued.
  - 340 tests, Debug/Release build warning-free.
  **Not done (moved to T008b):**
  - Decoding per-vertex normals, UVs, tangents and colors.
  - Resolving material-reference records to `eCMaterialResource2` and binding real textures.
  - Decoding LOD/submesh ranges and the data after the position block.
  - Confirming coordinate handedness/unit scale and skinned vertex streams.
  Completed 2026-11-24 — GMSH geometry parser, DigitalRiseModel mesh parts, renderer statistics, Myra HUD and mesh inspector.

- [x] **T008b · Mesh attributes, materials and LODs**
  Decode the GMSH v2 vertex streams and material references, including normal/UV/tangent/color layouts, `eEGfxFormat2` format codes, mesh→`eCMaterialResource2` resolution, texture bindings, LOD/submesh ranges and the coordinate system.
  Done when: representative meshes of every observed layout render with real normals/UVs and material textures, LOD0 selection matches the data, and malformed attribute streams fail with actionable diagnostics.
  **Completed 2026-11-24 — GMSH GPU index/vertex streams, all six retail layouts, DB submesh/material/LOD metadata, the material/texture chain and textured multi-part rendering.**
  - Decoded the real GPU index buffer at `GMSH+0x40` (16- or 32-bit, selected by buffer-descriptor field 3) and the vertex base at `align16(0x40 + indexCount × elementSize)`.
  - Decoded contiguous vertex streams and all five retail formats: `R32G32B32_Float` (4), `R16G16B16A16_Float` (7), `R10G10B10A2_UNorm` (15), `R16G16_Float` (23) and `B8G8R8A8_UNorm` (58). Usage map: 0 position, 1 normal, 2 tangent, 3 color, 4 texcoord.
  - Added `MeshMetadata` for the `eCMeshResource2` DB class block: submesh index ranges, material names, `LoDRangeStart` and bounds. LODs are separate `_L1/_L2/_L3` resources with `LoDRangeStart` 0/5000/10000.
  - Added `ElexMaterial`/`MaterialMetadata` and `MaterialLibrary`; the submesh material name resolves through `w_mat` to its texture entries and primary image. The runtime material reference is stored in `DrMeshPartTag`.
  - `MeshLoader` builds one `DrMesh` part per submesh with real normals and UV0; the renderer draws each part with its uploaded texture and reports per-part statistics.
  - Added the `assets materials` inspector, decoded mesh layout output and a `--capture` back-buffer PNG option.
  - Coordinate system: objects are Y-up with centimetre-like units; no axis conversion or scale is applied.
  - All 5,868 meshes parse with zero diagnostics and 13,406 submeshes; packed positions match the widened `float3` copy exactly (maximum deviation 0). All 1,726 materials parse.
  - Verified visually: door (4 submeshes/materials), mixer (5 submeshes, 92k vertices), cathedral (9 submeshes, 327k vertices, 32-bit indices), stone (5/4 layout), box, axe, shadowhull and material cube. 372 tests, Debug/Release warning-free.
  **Deferred:**
  - Material parameter blocks (shader colors/scalars and multi-pass selection) — T401.
  - Per-triangle cluster block and GAR5 trailer before the resource table — T011/T102 investigation.
  - Vertex-color and tangent rendering (stock `BasicEffect` supports lighting or vertex colors, not both) — T102/T401.
  - Automatic LOD switching by `LoDRangeStart` — T406.
  - Exact triangle winding/handedness — T102.

- [x] **T009a · Skeletons and skinned meshes**
  Parse the `skn` skinned GMSH variant and its embedded skeleton: skinned vertex attributes, bone names/hierarchy, bind poses and the retail bone limit.
  Done when: decoded skeletons expose correct names/hierarchy, skinned vertex attributes decode, and the stock-`SkinnedEffect` decision is recorded for the decoded variants.
  **Completed 2026-11-24 — `skn` skinned-mesh parsing, embedded skeleton tails, `assets skeletons` and the skeleton format spec.**
  - Identified `skn` as the skinned GMSH variant (`class eCMotionSkinResource2`): counts come from the buffer descriptors and `header[0]` points at the embedded skeleton tail.
  - Decoded usage 5 bone indices (`R8G8B8A8_UInt`, offset 0) and weights (`R16G16B16A16_Float`, offset 4); four influences per vertex.
  - Added `Skeleton` parsing for the tail: 28-byte bind-pose records (head/attachment), separator, parent indices, name offsets, DJB2 hashes and names.
  - Added `assets skeletons` and `Documentation/Formats/Skeletons.md`; 1,270 of 1,320 `skn` resources parse. Maximum 65 bones, typical 51–65; `SkinnedEffect.MaxBones` is 72, so the stock effect suffices for decoded skeletons.
  - 378 tests, Debug/Release warning-free.
  **Deferred to T009b:**
  - Body/creature bind-pose transforms and the 50 unrecognized tails (`Mesh_Cre_*`, some `Mesh_Female_*`, hologram variants).
  - Head/attachment parent encoding.

- [x] **T009b · Animation data discovery**
  Locate and document where animation clips and blend shapes live, and expose them through inspectors.
  **Completed 2026-11-24 — `net` Morpheme name extraction, `bln` header/name parsing, `assets animations`/`assets blendshapes` and the animation format spec.**
  - Confirmed there is no standalone `ani`/`mot` resource type. Animation data lives in `net` (34 Morpheme motion networks, `class eCMotionNetworkDefResource2`) and `bln` (270 blend-shape sets, `class eCBlendShapeResource2`).
  - Added `MotionNetwork`, which extracts readable Morpheme names (tracks, `BONE_*` bones, states, conditions). Largest network is `Male` (59.6 MB); names are capped at 100,000.
  - Added `ElexBlendShapeSet`, which parses the `bln` header (target count, base vertex count, three unresolved fields), the `targetCount-1` name offsets and the `blendShape1.*` target names; the name table is validated against the offsets so printable metadata bytes cannot be mistaken for a name.
  - Added `assets animations` and `assets blendshapes` inspectors, 11 tests and `Documentation/Formats/Animations.md`.
  - Retail validation: 34/34 `net` and 270/270 `bln` resources parse; every `bln` set yields one name per target.
  - 389 tests, Debug/Release warning-free.
  **Deferred:**
  - Morpheme graph, clip boundaries, keyframe tracks and compression → T009c.
  - `bln` per-target delta layout/quantization and morph sampling → T009d.
  - Body/creature bind-pose transforms and the 50 unrecognized `skn` tails (`Mesh_Cre_*`, some `Mesh_Female_*`, hologram variants) → T009c.

  - Body/creature bind-pose transforms and the 50 unrecognized `skn` tails (`Mesh_Cre_*`, some `Mesh_Female_*`, hologram variants) → T009f.

- [x] **T009c · Morpheme clip decoding and pose sampling**
  Decode the `net` network graph, clip boundaries, keyframe tracks and compression, map nodes to `skn` bones, and sample clips into poses.
  Done when: representative `Male`/creature clips sample to plausible poses and the clip/compression formats are documented in [Formats/Animations.md](Documentation/Formats/Animations.md).
  **Completed 2026-11-24 — 61 static clip poses decoded, name-based skeleton retargeting, clip blending and `assets animations` clip output.**
  - Located the clip blocks in `net`: a 32-byte header (marker at offset `0x14`), a cumulative track-name table and a unit-quaternion pose array.
  - Decoded 61 clips across the 34 networks; every clip has a pose. Retargeting matches track names to skeleton bones exactly or after stripping `BONE_`/`bone_`; `Male` full-body clips map all 61 skeleton bones, and its 40/33-track clips map 25 as an upper-body layer.
  - Added `MotionClip`, `MotionPose` (retarget + slerp blending) and `SkeletonPose` (local/world transform composition), plus `assets animations` clip output and 10 new tests.
  - Retail validation: 34/34 networks parse, 61/61 clips have poses, `Male` mapping 61/61 bones.
  - 399 tests, Debug/Release warning-free.
  **Deferred:**
  - State graph, transition timing, keyframe curves and compression → T009e.
  - Body/creature bind-pose transforms and a rendered pose viewer → T009f.
  - The 50 unrecognized `skn` tails (`Mesh_Cre_*`, some `Mesh_Female_*`, hologram variants) → T009f.

- [x] **T009d · Blend-shape deltas and morph playback**
  Decode the `bln` per-target delta data, map targets to the base mesh, and drive morph weights from animation.
  Done when: facial/body blend shapes can be weighted and rendered, and the delta layout/quantization is documented.
  **Completed 2026-11-24 — full `bln` delta decoding, sparse target masks, weighted morph application and updated format spec.**
  - Decoded the `bln` layout: vertex remap, `affectedCount` 16-byte sparse descriptors (`startDelta` + 96-bit target mask) and 12-byte quantized delta records (position `xyz` + normal `xyz` as `int16`).
  - Position deltas scale by `header[16] / 1000`; normals scale by `1 / 32767`. Added `ElexBlendShapeTarget`, `ElexBlendShapeDelta` and `ApplyWeights` for weighted morph playback.
  - Retail validation: 270/270 sets decode; 63,541,940 delta records total. The sampled head has 83 targets, 7,707 affected vertices and 135,564 deltas; the spellarm has 5 targets and 24,445 deltas.
  - The sampled head's `bln` and `skn` share 9,975 vertices and a hash suffix, confirming direct base-mesh vertex mapping.
  - `assets blendshapes` now reports decoded delta counts and per-target sample deltas; 6 new tests (412 total), Debug/Release warning-free.
  **Deferred:**
  - Exact game unit and normal-normalization convention.
  - GPU/rendering integration for morph targets → T106/T102.
  - Drive weights from facial animation/state machine → T009g.

- [x] **T009e · Morpheme state graph**
  Decode the network graph names: states, transitions and conditions used by the state machine.
  Done when: state transitions and names decode and the graph layout is documented in [Formats/Animations.md](Documentation/Formats/Animations.md).
  **Completed 2026-11-24 — name-derived state transitions, state/node classification and `assets animations` graph output.**
  - Added `MotionTransition`, which parses `Source_Target|ConditionGroup|Condition` names and resolves the longest split whose source and target are known graph names.
  - Added state/node classification: transition endpoints become `StateNames`; remaining graph tables become `NodeNames`. Both are exposed by `assets animations`, and `--properties` prints every transition, state and node.
  - Retail validation: 34/34 networks parse and 1,086 simple transitions are extracted; for example `Chest` has 4 transitions and 2 states, `Crawler-Rig` 176/3, `Weapons` 20/5 and `Troll-Rig` 227/3. `Male` uses long hierarchical paths and exposes no simple three-segment transitions yet.
  - 406 tests, Debug/Release warning-free.
  **Deferred to T009g:**
  - Binary graph records (GUIDs, edges, transition durations) and nested hierarchical paths used by `Male`.
  - Per-track keyframe/curve data and compression: the large tail of `Male` (about 54 MB) contains mixed float, index and high-entropy blocks whose layout is not decoded.

- [x] **T009f · Body bind poses and the pose viewer**
  Finish the `skn` bind-pose leftovers (body/creature transforms and the 50 unrecognized tails), then render a skinned character driven by a decoded clip and a blended transition.
  Done when: a representative character displays a decoded pose and a two-clip blend in the app.
  **Completed 2026-11-24 — body/creature bind poses, NPC object-matrix composition and the skeleton pose viewer.**
  - Decoded body/creature bind poses: after a variable AABB prefix, `nameCount-1` global and `nameCount-1` cached local 28-byte records precede the separator. The parser validates both sets and derives local bind transforms from the global set plus the (non-topological) parent array.
  - 1,045 of 1,270 parsed tails now expose bind transforms; 225 props/higher-LOD tails expose names/parents only; the same 50 tails remain unrecognized. Male and female full-body skeletons now have 61/62 bind bones.
  - Added `SkeletonPose.ComputeWorldMatrices(objectMatrix, removeRootTranslation)` matching the reference NPC path: root X/Z is removed before the object matrix, then object → root-local → child composition.
  - Added a skeleton pose viewer: `--mesh <skn> --animation <net>` loads the skeleton and network, blends the two clips with the most tracks, and draws the bone hierarchy. Verified by captures for the male body and gull.
  - 418 tests, Debug/Release warning-free.
  **Deferred:**
  - 225 parsed tails without bind records, the 50 unrecognized tails and head/attachment parent encoding.
  - Skinned-mesh rendering/skinning (GMSH `skn` geometry, blend indices/weights, `SkinnedEffect`) → T106/T102.
  - Selecting clips by state instead of the two largest → T009g.

- [x] **T009g · Morpheme hierarchical state paths**
  Decode the hierarchical `|`-separated state-machine paths used by the larger networks.
  Done when: path segments, condition groups and conditions decode and the layout is documented in [Formats/Animations.md](Documentation/Formats/Animations.md).
  **Completed 2026-11-24 — hierarchical path parsing, `assets animations` path output and the updated state-graph spec.**
  - Added `MotionStatePath`, which decodes `|`-separated names into state segments, condition group and condition; `assets animations` reports a path count and prints the first 16.
  - Retail validation: all 34 networks parse; `Male` reaches the 10,000-path cap, `Creature` 5,051, `Troll-Rig` 3,830, `Crawler-Rig` 1,767. `Weapons` has 20 simple transitions and 344 paths.
  - 422 tests, Debug/Release warning-free.
  **Deferred to T009h:**
  - Binary graph records (GUIDs, edges, state links) and transition durations.
  - Per-track keyframe/curve data and compression; the large `Male` tail (about 54 MB) remains undecoded.
  - Mapping hierarchical paths to clips/state transitions programmatically.

- [x] **T009h · Morpheme binary graph blocks**
  Decode the candidate binary state-graph blocks and expose their headers and reference words.
  Done when: candidate blocks are detected, their layout is documented and the inspector exposes them.
  **Completed 2026-11-24 — `MotionGraphNode` probe, `assets animations` block output and the binary-block spec.**
  - Added a candidate binary block probe (`MotionGraphNode`): finds 24-byte headers followed by 16×40-byte reference records. `Weapons` exposes one block at payload `0x40C0` (type 1, 16 references); the other state offsets begin directly with record arrays.
  - `assets animations` reports the candidate-block count and prints the first blocks under `--properties`.
  - 425 tests, Debug/Release warning-free.
  **Deferred to T009i:**
  - Mapping reference words/edges to state names and decoding transition durations.
  - Per-track keyframe/curve data and compression; the large `Male` tail (about 54 MB) remains undecoded.
  - The other state arrays that start directly with 40-byte records.

- [x] **T009i · Morpheme binary state records**
  Decode the fixed 40-byte records inside candidate binary graph blocks.
  Done when: each record's value layout is exposed and documented.
  **Completed 2026-11-24 — five 64-bit value slots per record, reference list and CLI output.**
  - `MotionGraphNode` now exposes all five `uint64` values of each 40-byte record; the first value is also surfaced as a reference when it is not `0xFFFFFFFF`.
  - `assets animations` prints the candidate blocks and their first references.
  - 425 tests, Debug/Release warning-free.
  **Deferred to T009j:**
  - Mapping the five values to state/edge semantics and transition durations.
  - Per-track keyframe/curve data and compression; the large `Male` tail remains undecoded.

- [x] **T009j · Timed pose transitions**
  Add a timed sampler for transitions between two decoded clip poses.
  Done when: one-shot and looping transition sampling are available and tested.
  **Completed 2026-11-24 — `MotionTransitionSampler` with one-shot and ping-pong sampling, used by the pose viewer.**
  - Added `MotionTransitionSampler(from, to, duration)` with `Sample(seconds)` and `SampleLoop(seconds)`.
  - The skeleton viewer now drives its blend through the sampler with a two-second loop.
  - 3 new tests; 428 tests, Debug/Release warning-free.
  **Deferred to T009k:**
  - Mapping binary record values to states/edges/transition durations.
  - Per-track keyframe/curve data and compression; the large `Male` tail remains undecoded.

- [x] **T009k · Keyframe data survey**
  Determine whether `net` contains uncompressed keyframe tracks or only static clip poses.
  Done when: the payload scan result is documented.
  **Completed 2026-11-24 — no uncompressed keyframe tracks found; clips behave as static poses and transitions are timed pose blends.**
  - The only raw quaternion runs in `Male` are the four clip pose arrays (98/88/40/33 quaternions).
  - The remaining tail is 714,477 printable runs (~4.1 MB of state-path text), a 2,341-name transition table, offset/index tables pointing into the string data, and high-entropy blocks.
  - 428 tests, Debug/Release warning-free.
  **Deferred to T009l:**
  - Compressed pose/curve codec in the high-entropy blocks.
  - Semantics of the binary graph record values and transition durations.

- [x] **T009l · Quantized quaternion sample probe**
  Identify candidate compressed animation samples in the high-entropy `net` regions.
  Done when: quantized sample runs are detected and documented.
  **Completed 2026-11-25 — `MotionSampleProbe`, `assets animations` output and the sample survey.**
  - Added a diagnostic scanner for runs of four `int16` values whose squared length is near `32767²`; the `Male` payload reaches the 10,000-run cap.
  - `assets animations --properties` prints the first quantized runs.
  - 431 tests, Debug/Release warning-free.
  **Deferred to T009m:**
  - Track headers, sample times, mapping to skeleton bones and graph semantics.
  - Full sample-track decoding and playback.

- [x] **T009m · Sample-track codec and interpolation**
  Decode the quantized 16-byte animation sample tracks and interpolate them over time.
  Done when: sample headers and records decode and `SampleAt` interpolates positions/rotations.
  **Completed 2026-11-25 — `MotionSampleTrack`, 1,006 `Male` tracks and sample interpolation.**
  - Header: byte size, stride 16, duration, 60 Hz rate, track/bone index and relative sample offset.
  - Record: `int16` position xyz + `int16` rotation xyzw; `SampleAt` lerps positions and slerps rotations.
  - 4 new tests; 435 tests, Debug/Release warning-free.
  **Deferred to T009n:**
  - Position scale and track/bone mapping.
  - Graph record semantics and transition durations.

- [x] **T009n · Sample scale and track-name mapping**
  Add the inferred sample position scale and resolve sample header indices against clip track names.
  Done when: scaled positions and track/bone names are available and tested.
  **Completed 2026-11-25 — `MotionSample.GetPosition`, `GetTrackName`/`GetBoneName` and the inferred 100-unit scale.**
  - Sample positions use `raw / 32767 × 100` model units (inferred from body proportions).
  - Track and bone index fields resolve against a clip's track list.
  - 435 tests, Debug/Release warning-free.
  **Deferred to T009o:**
  - Grouping the 1,006 samples into clips/animations and mapping graph records to states/edges/durations.

- [x] **T009o · Sample grouping and sampled poses**
  Group sample tracks into animations by shared duration and sample them into skeleton poses.
  Done when: groups produce interpolated poses and the viewer uses a sampled group.
  **Completed 2026-11-25 — 253 `Male` duration groups; the viewer animates the 0.8 s / 85-track group.**
  - Added `MotionSampleGroup.GroupByDuration` and `SamplePose`, mapping track indices to a bone-name list.
  - The skeleton viewer prefers the largest sample group; the captured male scene shows a standing sampled pose.
  - 4 new tests; 439 tests, Debug/Release warning-free.
  **Deferred to T009p:**
  - Mapping binary graph records to states, edges and transition durations.
  - Reliable clip/state association for the sample groups.

- [x] **T009p · Graph state record groups**
  Map 40-byte binary graph records to named states.
  Done when: state groups decode and the inspector exposes them.
  **Completed 2026-11-25 — `MotionGraphState` maps 16-record groups to `Active`, `Aim`, `Shoot`, `Reload`.**
  - State groups are 16 five-value records plus a 24-byte trailer; the state-name table order maps to the groups.
  - `assets animations --properties` prints every mapped state and its first references.
  - 4 new tests; 443 tests, Debug/Release warning-free.
  **Deferred to T009q:**
  - Transition durations and node/edge semantics.
  - Association of sample groups with states/clips.

- [x] **T009q · State value aggregation**
  Aggregate the values of each binary state record group.
  Done when: state value sets are exposed and tested.
  **Completed 2026-11-25 — state value sets exposed; `Weapons` values are 11/7/11/20.**
  - `MotionGraphState.References` aggregates distinct non-sentinel values across the 16×5 slots.
  - The sample-track matching attempted here was invalidated by T009r: the values repeat across
    unrelated resources and are a fixed template, not track indices.
  - 443 tests, Debug/Release warning-free.
  **Deferred to T009r:**
  - Transition durations and full edge semantics.

- [x] **T009r · Transition durations and edge semantics**
  Determine how transition durations and edge/node semantics are encoded in the remaining records.
  Done when: transitions carry durations or the layout is conclusively documented in [Formats/Animations.md](Documentation/Formats/Animations.md).
  **Completed 2026-11-25 — corrected state-block offsets and decoded timed 48-byte transition records.**
  - State blocks: kind/state count, marker `0x53`, 16 records per state and a per-state offset table;
    the earlier parser was 8 bytes off, so the state names and value counts were wrong.
  - `MotionGraphTransition` decodes runs of 48-byte records with index, source/target slots and a
    duration in milliseconds (`Weapons` 16 records, `Male` 939, `Chest` 4).
  - The four-state blocks share identical 16×5 values across resources, so they are a fixed template;
    the T009q sample-track association was removed.
  - 10 new tests; 453 tests, Debug/Release warning-free.
  **Open follow-ups:**
  - Decode state-name tables that store a GUID between the offsets and the strings.
  - Associate transition runs with their state block and confirm slot `0` semantics per network.

- [ ] **T010 · World and level data**
  Parse the world/level assets that place entities and static geometry: sectors, transforms, object references, and the link between instances and templates/meshes. Document entity categories found in the data.
  Done when: a level's instance graph can be dumped as text (position, rotation, template, mesh) and matches what the game data implies.

- [ ] **T011 · Collision and navigation data**
  Extract collision geometry suitable for BepuPhysics2 static bodies, plus navigation mesh data for NPC pathing. Document both formats. Also investigate the GMSH per-triangle cluster block and GAR5 trailer as potential occlusion/collision data.
  Done when: a level's collision can be rendered as a wireframe/debug shape and matches visible geometry closely enough for movement.

- [ ] **T012 · Audio assets**
  Identify and document audio containers (FMOD banks/streams and any wrappers), including sample metadata and decoding requirements. Playback itself is scheduled in M3.
  Done when: every audio asset type in the data is identified and at least its metadata can be listed.

- [ ] **T013 · Text and localization**
  Parse string tables, `strings.csvt`, `loc.ini` and related localization data into a lookup API used by UI, dialogue and item names.
  Done when: item/UI strings for the selected language resolve from game data.

- [ ] **T014 · Script system research**
  Determine how game logic is stored and executed (bytecode/format, entry points, native API surface, quest/dialogue hooks) and write `Documentation/Formats/Scripts.md`. No runtime implementation in this task.
  Done when: the document explains how a simple script could be executed, with concrete evidence from assets and a list of unknown areas.

## M1 — Visible World

Exit criteria: a level, its static geometry and its characters can be explored with a debug camera using only stock MonoGame effects.

- [ ] **T101 · Application shell**
  Window, graphics device setup, resize handling, fixed timestep, content root, logging, crash screen, Myra host and a debug overlay (FPS, timings, memory).
  Done when: the app runs for minutes without leaks or device-lost crashes, and the overlay can be toggled.

- [ ] **T102 · Mesh renderer**
  Upload parsed meshes to GPU buffers, map materials/textures, and draw with `BasicEffect`; provide checker/placeholder materials for missing data. Add a model-viewer scene. Include vertex-color and tangent rendering and resolve triangle winding/handedness.
  Done when: static meshes render with correct proportions, normals and UVs, and the viewer can cycle any mesh in the catalog.

- [ ] **T103 · Camera, transforms and culling**
  Scene graph/transform hierarchy, fly camera, frustum culling, material-sorted draw batching, and basic render statistics.
  Done when: large test scenes render at interactive frame rates with culling visibly effective.

- [ ] **T104 · Level static geometry**
  Load a world sector, resolve its static meshes and transforms, and render the full sector with textures and fog.
  Done when: a recognizable game location is visible end to end, with no missing-geometry gaps caused by loader bugs.

- [ ] **T105 · Sky and lighting approximation**
  Skybox/background, sun directional light, ambient term and fog using stock effects; establish sane defaults per level.
  Done when: levels read clearly and are not flat-black or blown out.

- [ ] **T106 · Character rendering**
  Render NPCs and creatures via skeleton + `SkinnedEffect` (or the documented custom fallback), including multi-material bodies and equipment attachments.
  Done when: the bind pose of any humanoid/monster renders correctly in the character viewer.

- [ ] **T107 · Animation playback**
  Sample, loop and crossfade animation clips; drive idle/walk/run on the character viewer.
  Done when: clips play at correct speed without pops, and looping clips are seamless.

- [ ] **T108 · World visuals pass**
  Render light entities, decals, water surfaces and basic vegetation from level data; add debug toggles per category.
  Done when: the M1 level contains all major visual categories and each can be toggled independently.

## M2 — Playable Core

Exit criteria: the hero can traverse a level, collide with the world, talk to/interact with entities and fight, with a usable HUD.

- [ ] **T201 · Runtime world model**
  Map the level instance graph to runtime entities with stable IDs, ownership, lifetimes and component storage tailored for tens of thousands of instances.
  Done when: instance counts, categories and transforms match the T010 dump, and entity lookup by ID is O(1).

- [ ] **T202 · Spawn all entity types**
  Spawn hero, NPCs, monsters, items, containers, doors, interactive objects and triggers at their correct transforms with correct visuals.
  Done when: a level's population is visually complete and a category report shows no silently skipped entity type.

- [ ] **T203 · Player movement and camera**
  Capsule character on BepuPhysics2 with walk/run/crouch/jump, slope and step handling, and third-/first-person cameras with collision.
  Done when: the hero can traverse representative terrain, stairs and interiors without falling through or sticking.

- [ ] **T204 · Interaction**
  Focus raycast, interaction prompts, doors/levers/containers/pickups, and the inventory data model behind them.
  Done when: every interactive object category can be triggered and its state change is observable.

- [ ] **T205 · NPC navigation and idle behavior**
  Navmesh pathfinding, wander/idle schedules and simple obstacle avoidance; entities remain where the game data expects them.
  Done when: NPCs across a settlement move plausibly and do not fall off the navmesh.

- [ ] **T206 · Combat basics**
  Melee attacks with hit detection, stamina/health, damage, stagger, death and loot drops; ranged combat stubs where needed.
  Done when: the hero can defeat a basic enemy and be defeated, with correct animations and state transitions.

- [ ] **T207 · HUD and core menus**
  Myra HUD (health/stamina), inventory, pause/settings, key rebinding and a minimal journal.
  Done when: all core menus work with keyboard/mouse/gamepad and never block gameplay input irrecoverably.

- [ ] **T208 · Save/load foundation**
  Engine-owned save format for player state, inventory, entity states and world flags; versioned and validated.
  Done when: save/load round-trips across restarts and rejects incompatible versions cleanly.

## M3 — Game Systems

Exit criteria: scripts, quests, dialogue, activities, progression and audio work together; the main story is completable with temporary gaps explicitly tracked.

- [ ] **T301 · Script runtime**
  Execute game scripts on the VM/format documented in T014, with native bindings for world, inventory, quest and UI functions, plus graceful handling of unsupported calls.
  Done when: a representative set of shipped scripts runs and produces observable world changes.

- [ ] **T302 · Dialogue and quests**
  Dialogue trees, conditions, quest state machine, journal entries, objectives and script hooks.
  Done when: a complete side quest can be started, advanced and finished through dialogue and world actions.

- [ ] **T303 · Activities**
  Lockpicking/hacking, crafting, cooking, alchemy, trading, pickpocketing and workbenches.
  Done when: each activity can be performed from the world UI and affects inventory/progression correctly.

- [ ] **T304 · Progression and factions**
  Attributes, skills, learning/teachers, XP and levels, faction membership and reputation gates.
  Done when: a new character can be built in different directions and faction restrictions are enforced.

- [ ] **T305 · Streaming and transitions**
  Sector streaming, doors, teleporters, fast travel and respawn rules.
  Done when: the player can cross the whole world map without manual level loading or memory growth.

- [ ] **T306 · Audio runtime**
  Music, ambience, SFX and voiced dialogue with 3D positioning and mixing categories.
  Done when: each audio category plays in the right context and volumes are configurable.

- [ ] **T307 · Game flow**
  Main menu, intro/credits video playback (with a skip/fallback path), new game, load, endings and credits roll.
  Done when: a new game can be started from the menu and the ending sequence returns to the menu.

- [ ] **T308 · Finishable game**
  Drive the main story to completion, fix blockers and crashes, and record any intentional deviations.
  Done when: the main story can be played from start to credits without a blocking bug, and the test notes list the route taken.

## M4 — Parity

Exit criteria: visuals and UI are close enough to the original that differences are deliberate and documented.

- [ ] **T401 · Material shaders**
  Normal/specular/emissive materials, terrain layer blending and the remaining feature-specific effects, matching the original look. Decode the material parameter blocks (shader colors/scalars and multi-pass selection) first.
  Done when: side-by-side comparisons show no systemic material differences.

- [ ] **T402 · Post-processing**
  Bloom, tone mapping, color grading, anti-aliasing and depth-of-field matching the original pipeline.
  Done when: the post stack is configurable and defaults reproduce the intended look.

- [ ] **T403 · Environment effects**
  Weather, time of day, water, wind/vegetation and particles.
  Done when: weather cycles and water behave like the original in representative locations.

- [ ] **T404 · UI parity**
  Myra skin, layout and transitions matching the original HUD and menus at all supported resolutions.
  Done when: screenshots at 16:9, 16:10 and ultrawide show equivalent layout and styling.

- [ ] **T405 · Animation parity**
  Blending, additive layers, look-at/IK, facial animation and root motion.
  Done when: conversations, combat and traversal animations match the original's feel.

- [ ] **T406 · Performance parity**
  Automatic LOD selection using `LoDRangeStart`, occlusion, instancing, batching and streaming budgets.
  Done when: frame times at reference settings are within an agreed tolerance of the original on the same hardware.

- [ ] **T407 · Audio parity**
  Reverb zones, occlusion, distance curves and mix levels.
  Done when: indoor/outdoor transitions and combat mixes match the original.

---

## Backlog

Not committed to a milestone; revisit after T308.

- Mod loading/overrides on top of the base mounts.
- Persist PAK directory indexes to remove the ~2.4 s cold start at ~157k entries.
- Generalize nested class-data object decoding beyond `gCInfo`.
- Asset export/screenshot tools for documentation.
- Editor support for inspecting templates and worlds.
- Upload volume textures as `Texture3D`, cubemaps and texture arrays; add an HDR float pipeline instead of clamping BC6H/16F/32F data.
- ELEX 2 support if its formats are close enough.

## Known risks

- **Script runtime** is the largest unknown; it may require a large interpreter and extensive native bindings.
- **Animation compression** may be lossy in ways that complicate exact playback.
- **World streaming and occlusion** data may be only partially recoverable from assets.
- **Archive performance**: the retail archives are multi-gigabyte; random access and indexing must be efficient.
- **Save compatibility** with the original game is out of scope; OpenElexSharp defines its own save format.
- **Mount semantics**: the meaning of `$(app)` and of `#Volume:/path` references is inferred from the retail layout; revisit if assets fail to resolve.
- **Language-specific volumes**: `_en`, `_na` and other PAK variants are all mounted; a language preference is not implemented yet.
