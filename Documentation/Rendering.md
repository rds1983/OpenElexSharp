# Scene graph, camera, culling and batching

This document covers the runtime side of static geometry: the transform hierarchy, the free-fly
camera, frustum culling and material/buffer-sorted draw submission. Asset formats are documented in
[Formats/Worlds.md](Formats/Worlds.md) and [Formats/Meshes.md](Formats/Meshes.md).

## Scene graph

`SceneNode` is a row-vector transform hierarchy:

```text
world = local * parentWorld
```

The convention matches scene-sector entity transforms ([Formats/Worlds.md](Formats/Worlds.md)).
Each node stores a diagnostic name, an optional `DrMesh`, a shading mode, visibility and children.
World transforms are cached and invalidated down the subtree when a local transform changes, so a
static scene pays the composition cost once and a moving parent updates its descendants lazily.

Nodes can carry a mesh or only group children. `LocalBounds` is the union of the mesh parts' boxes;
`WorldBounds` applies the current world transform to the eight box corners. `AddChild` detaches a
node from its previous parent and rejects cycles.

The stress scene uses a root, one group per row and one node per instance:

```text
stress
  row-0        (local X/Z offset for the row)
    mesh[0,0,0] (local column/layer offset)
    ...
  row-1
  ...
```

## Fly camera

`FlyCamera` stores an eye position, yaw, pitch, FOV and clip planes. With zero angles it looks
toward +Z, and positive yaw turns toward +X. Pitch is clamped to ±89°.

| Input | Action |
| --- | --- |
| `W`/`A`/`S`/`D` | Move along the view/right axes |
| `Q`/`E` | Move down/up in world space |
| `Shift` | Speed multiplier (default ×5) |
| Right mouse drag | Look with pointer deltas |
| Arrow keys | Look at 1.5 rad/s |

The default movement speed is 2,000 units/s, the near/far planes are 1/200,000 and the field of
view is 45°. `View` is `CreateLookAt(position, position + forward, +Y)` and the projection is
`CreatePerspectiveFieldOfView`. The camera is deliberately independent of MonoGame input state
except for the update helpers, so its math is unit-tested.

## Frustum culling

`Renderer.BeginFrame(view, projection)` builds a `BoundingFrustum`. `SubmitMesh`/`SubmitMeshPart`
test each part's world-space sphere (the `DrMeshPartTag` sphere, or a sphere around the part box):

- visible parts are queued;
- rejected parts increment `MeshPartsCulled`;
- every considered part increments `MeshPartsSubmitted`.

`CullingEnabled` toggles the test. In the stress scene `F2` flips it and logs the new state, which
makes an A/B capture straightforward. Culling happens on the submitting thread before any sort or
draw, so rejected geometry costs one sphere test plus its part metadata.

## Material-sorted batching

Queued parts are sorted in `Flush` by:

1. material texture identity,
2. vertex buffer identity,
3. index buffer identity,
4. shading mode.

This groups draws that share GPU state, and `Batches` reports the number of resulting state groups.
Draw calls still equal the number of parts because each part is a separate index range; merging
ranges would require instancing or static geometry merging. The stress scene uses the latter:
see below.

`Flush` is called automatically by `EndFrame`, so scene code submits parts and closes the frame.
Immediate `DrawMesh` remains for the single-mesh viewer, where ordering and culling are irrelevant.

## Stress scene

`--stress` builds a deterministic grid of **864 instances** of one mesh (`--mesh` selects it; the
default is `Obj_Bld_Alb_Door_Closed_L1`). The logical mesh is repeated across the hierarchy of
instances, then merged into **18 static spatial chunks** (3 × 2 × 3 groups of 4 × 3 × 4 instances).
Each chunk owns:

- one vertex buffer with every instance's vertices transformed into world space;
- one 32-bit index buffer with ranges grouped by material;
- one `DrMeshPart` per material, each carrying its material and chunk bounds.

This keeps draw calls at 8–72 instead of 864–3,456 while still allowing the renderer to cull whole
chunks. The camera starts inside the grid looking diagonally, so roughly half of the chunks and
triangles fall outside the view.

Measured on the reference installation (1280 × 720, 60 Hz, covered by the debug overlay):

| Metric | Culling on | Culling off (`F2`) |
| --- | ---: | ---: |
| Instances | 864 | 864 |
| Chunks / submitted parts (door) | 18 / 72 | 18 / 72 |
| Parts drawn | 36 | 72 |
| Parts culled | 36 | 0 |
| Draw calls | 36 | 72 |
| Triangles | 289,440 | 578,880 |
| Draw time | 0.773 ms | 2.822 ms |
| FPS | 60 | 60 |

The cube variant (864 one-part instances) drops to 8 draws / 4,608 triangles when culling is on and
18 draws / 10,368 triangles when it is off, also at 60 FPS after shader warm-up.

Static batching is only used by the stress scene; it trades per-instance culling granularity for low
draw-call counts. T104's level renderer will use the scene graph and per-part culling with real
sector entities, where instancing or merged chunks can be chosen per asset.

## Render statistics

`Renderer` exposes per-frame counts:

| Counter | Meaning |
| --- | --- |
| `DrawCalls` | GPU draw calls issued |
| `Triangles` | Triangle-list primitives submitted |
| `Meshes` | Mesh parts drawn |
| `MeshPartsSubmitted` | Parts considered after culling was applied |
| `MeshPartsCulled` | Parts rejected by the frustum |
| `Batches` | Sorted state groups |

The overlay shows draw calls, triangles, drawn/culled parts, batches, frame timings and memory. The
scene row reports logical instances, static chunks and batch parts.

## Tests

- `SceneNodeTests` — parent/child composition, parent changes, dirty updates, cycle rejection and
  depth-first traversal.
- `FlyCameraTests` — forward/right basis, yaw/pitch clamping, movement speed, Shift multiplier and
  view matrix position.
- `LaunchOptionsTests` — `--stress` and `--no-cull`.
- `Renderer` counters and visual stress results are verified with captures; they need a GPU.

## Related tasks

- T103 — this document, scene graph, fly camera, culling, batching and stress scene.
- T104 — level static geometry uses the same scene and renderer APIs.
- T401 — material two-sided flags determine the final culling default.
