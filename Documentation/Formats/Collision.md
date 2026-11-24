# Collision, occlusion and navigation (`col` / `ocm` / Nav sectors)

ELEX physics uses separate collision (`col`) and occlusion (`ocm`) resources. Scene entities reference
collision meshes through `eCPhysicsActor_PS` shape lists. Navigation is not stored as a baked polygon
mesh; `Nav` scene sectors contain flex-path volumes and off-mesh connectors that adjust a nav mesh
built from collision geometry at runtime.

## Purpose and where they appear

| Type | Class | Count | Contents |
| --- | --- | ---: | --- |
| `col` | `eCCollisionMeshResource2` | 2,141 | Triangle collision meshes and convex shapes. |
| `ocm` | `eCOcclusionMeshResource2` | 299 | Low-detail occlusion triangles for rendering/culling. |
| `sec` (name contains `Nav`) | `eCScene` | 17 | `gCNavFlexPath_PS`, `gCNavOffMeshConnector_PS` and helper points. |

Logical names resolve through `w_col_0_na.db`, `w_ocm_0_na.db` and `w_sec_0_na.db`. Collision
resources usually end in `_COL`; convex variants end in `_CV`; occlusion resources end in `_OCC`.

## Container

Collision and occlusion resources have an outer GAR5 wrapper and a direct payload at offset `0x2C`:

```text
GAR5                         outer resource wrapper (44 bytes observed)
  uint32 0x20
  byte[36] zero/resource metadata
SEBD ...                     col payload
occlusion header ...         ocm payload (no magic)
```

Unlike `net`, `sec` and template resources there is no inner GAR5 or GEC2 object.

## Collision mesh (`col`)

The payload starts with `SEBD` (`53 45 42 44`). The observed header is:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0x00 | `char[4]` | `SEBD`. |
| 0x04 | `byte[4]` | Version/flags, observed `00 04 03 03`. |
| 0x0C | `byte[8]` | Unresolved hash/GUID pair, observed `EE 1F 33 01 57 5F 36 34`. |
| 0x14–0x34 | `uint32[]` | Flags and zero fields, observed `1,0,0,1,0,0,0,1,0`. |
| 0x3C | `uint32` | `0x150` in the sampled small meshes. |
| 0x60 | `uint32` | Shape count, observed `1`. |
| 0xA0 | `uint32` | Vertex count. |
| 0xA4 | `uint32` | Triangle count. |
| 0x110 | `uint32[5]` | Second shape descriptor (`4, 1, N, ...`), unresolved. |
| 0x190 | `uint32` | Triangle count (duplicate). |
| 0x194 | `uint32` | Vertex count (duplicate). |
| 0x198 | `float[]` | World-space bounds / radius values before the local arrays. |

The duplicated counts are the reliable way to locate the mesh. The exact offset of the vertex array
is not stored in a field decoded so far, so the parser scans for an index window that satisfies:

- every index is below the vertex count;
- the window length is `triangleCount × 3`;
- the maximum index is `vertexCount - 1`;
- the window contains index `0` and a reasonable number of distinct vertices;
- the vertex array immediately before the window is 4-byte aligned and contains finite coordinates.

This locates the arrays in 2,138 of 2,141 retail `col` resources. The three failures are `_CV` convex
resources, which use a different layout and are not decoded.

### Vertex and index arrays

- Vertices are `float32 xyz` with a stride of **12 bytes**.
- Indices are `uint16` when `vertexCount ≤ 65,535`, otherwise `uint32`.
- Indices form a triangle list.
- Local units are **metres**; GMSH mesh vertices are centimetres. The viewer multiplies collision
  positions by 100 to overlay them on the matching mesh (for example the `Obj_Dec_Bsk_TrapDoor_1`
  collision box spans `1 m` and its mesh spans `100 cm`).

Examples:

| Resource | Vertices | Triangles | Local bounds |
| --- | ---: | ---: | --- |
| `It_Int_Cultivate_1_COL` | 22 | 40 | `(-0.074, 0, -0.101)` … `(0.074, 1.653, 0.036)` |
| `Obj_Dec_Bsk_TrapDoor_1_COL` | 8 | 12 | `(0, 0, -0.5)` … `(1, 0.073, 0.5)` |
| `Obj_Bld_Abe_Castle_1_COL` | 3,071 | 4,912 | `(-39.757, 1.22, -16.879)` … `(4.49, 30.314, 16.104)` |
| `Obj_Bld_Cle_Cantina_Att_COL` | 66,377 | 94,718 | `(-4.6, -2.218, -20.642)` … `(44.945, 22.302, 22.6)` |

The Cantina attachment is the widest index case: its 66,377 vertices force 32-bit indices.

## Occlusion mesh (`ocm`)

The payload starts directly with a 48-byte header:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0x00 | `uint32` | Vertex offset, always `48`. |
| 0x04 | `uint32` | Zero. |
| 0x08 | `uint32` | Vertex count. |
| 0x0C | `uint32` | Zero. |
| 0x10 | `uint32` | Index offset. |
| 0x14 | `uint32` | Zero. |
| 0x18 | `uint32` | Index count (a multiple of 3). |
| 0x1C | `uint32` | Zero. |
| 0x20–0x2F | `uint32[4]` | Zero in the sampled resources. |

Vertices are `float32 xyzw` (stride 16; the fourth component is normally `1`) followed by `uint16`
triangle indices. A 36-byte trailer follows the indices; its meaning is unresolved. All **299**
retail occlusion resources parse with this layout.

Examples:

| Resource | Vertices | Triangles | Local bounds |
| --- | ---: | ---: | --- |
| `Obj_Bld_BSK_Palisade_Plank_6_OCC` | 4 | 2 | `(-90, 0, -0.16)` … `(90, 300, -0.16)` |
| `Obj_Bld_BSK_Palisade_TowerWalkable_1_OCC` | 41 | 48 | `(-466.9, -338.4, -473.3)` … `(471.2, 3086.8, 439.1)` |

Occlusion meshes are already in mesh units (centimetres) and are not scaled by 100.

## Collision shapes in scene entities

`class eCPhysicsActor_PS` (`0xC9D6C7C1`) stores a `Shapes` property (`0xD10C4009`) whose payload is:

```text
uint32 shapeCount
GEC2[] eCCollisionShapeMesh objects
```

Each `class eCCollisionShapeMesh` (`0x89063DED`) contains:

| Property | Type | Meaning |
| --- | --- | --- |
| `MeshResource` (`0xF2E29A7A`) | `eTResourceProxy<class eCCollisionMeshResource2>` | `col` resource name. |
| `LocalPose` (`0xA6736447`) | `class bCMatrix` | Shape transform relative to the entity. |

`ElexScene` exposes these as `ElexSceneEntity.CollisionShapes`; `assets scene --properties` prints one
`collision` row per shape. This is the link from an entity instance to its collision mesh.

## Navigation

There is no `nav` resource type and no baked nav-mesh polygon resource. The 17 `*_Nav` scene sectors
contain annotations:

| Property set | Contents |
| --- | --- |
| `class gCNavFlexPath_PS` | `Point` array (`bCVector[]`), `Radius`/`Height` arrays, `NavFlexPathFlag`. |
| `class gCNavOffMeshConnector_PS` | `StartPoint`, `EndPoint`, floor-scan distances, radius, bidirectional flag, user data. |
| `class gCAIHelper_FreePoint_PS` | Free-point helper transform and bounds. |

`assets navigation` summarizes these sectors. The actual walkable mesh is built by the game from
collision geometry and these annotations adjust costs and add links. The largest sampled sector,
`ABE_Nav`, contains 232 flex paths, 360 off-mesh connectors and 6 helper points among 599 entities.

## GMSH cluster block and GAR5 trailer

The GMSH per-triangle cluster block before the `0xFEEDFACE` sentinel and the 44-byte GAR5 trailer were
re-examined. They are not referenced by `col` or `ocm` resources and no collision/occlusion layout was
found in them. They remain render-side metadata; physics uses the separate resources described above.

## Evidence from the retail installation

| Metric | Value |
| --- | ---: |
| `col` resources parsed | 2,138 / 2,141 |
| Unsupported `_CV` convex resources | 3 |
| `ocm` resources parsed | 299 / 299 |
| Navigation sectors | 17 |
| `ABE_Nav` annotations | 232 flex paths, 360 connectors, 6 helpers |
| Largest parsed collision mesh | 66,377 vertices / 94,718 triangles |

## Open questions

- Meaning of the `SEBD` fields at `0x04`–0x60 and the second shape descriptor at `0x110`.
- Exact stored offset of the collision vertex array; it is currently located by scanning.
- `_CV` convex resource layout.
- The 36-byte `ocm` trailer.
- Whether the game builds navigation from `col` triangle meshes or from a simplified collision copy.
- Full semantics of `gCNavFlexPath_PS` flags and cost interaction with `gCNavigation_PS`.

## Implementation status

| Component | File |
| --- | --- |
| Collision triangle parser | `OpenElexSharp/Assets/ElexCollisionMesh.cs` |
| Occlusion triangle parser | `OpenElexSharp/Assets/ElexOcclusionMesh.cs` |
| Scene collision-shape links | `OpenElexSharp/Assets/ElexScene.cs` |
| CLI inspectors | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets collision`, `occlusion`, `navigation`) |
| Viewer wireframe | `OpenElexSharp/OpenElexGame.cs` (`--collision`), `OpenElexSharp/Graphics/Renderer.cs` |
| Tests | `OpenElexSharp.Tests/ElexCollisionMeshTests.cs`, `ElexOcclusionMeshTests.cs`, `ElexSceneTests.cs` |

Commands:

```powershell
dotnet run --project OpenElexSharp -- assets collision Castle --limit 5
dotnet run --project OpenElexSharp -- assets occlusion Palisade --limit 5
dotnet run --project OpenElexSharp -- assets navigation --all --properties
```

Viewer (collision coordinates are scaled from metres to mesh centimetres):

```powershell
dotnet run --project OpenElexSharp -- --mesh Obj_Dec_Bsk_TrapDoor_1_L1 --collision Obj_Dec_Bsk_TrapDoor_1_COL
dotnet run --project OpenElexSharp -- --mesh Obj_Bld_Abe_Castle_1_L1 --collision Obj_Bld_Abe_Castle_1_COL
```

## References

- Collision class and property hashes from the credited rmtools name registry (GPL-3.0).

## Related tasks

- T010 — scene entities and instance transforms.
- T011 — this specification and parsers.
- T012 — audio assets.
- T201/T203 — entity spawning and character movement on BepuPhysics2.
